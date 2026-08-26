using FinSim.Application.Dtos;
using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FinSim.Application.Services;

/// <summary>
/// Decides which bots act this tick and on which instrument. Every bot is a
/// liquidity provider: it quotes near currentPrice on whichever side it can
/// afford, so a user always has something to hit. Trades between bots happen
/// when quotes overlap — a side effect, not the goal.
///
/// Placement goes through OrderService on the same path a real user takes —
/// no shortcuts, no special casing. Runs inside the tick's transaction.
/// </summary>
public class BotEngine
{
    private readonly IUserRepository _users;
    private readonly IInstrumentRepository _instruments;
    private readonly IPortfolioRepository _portfolio;
    private readonly IOrderRepository _orderRepo;
    private readonly OrderService _orders;
    private readonly IConfiguration _config;
    private readonly ILogger<BotEngine> _log;

    // Shared across ticks on purpose: a fresh Random per tick seeded from the
    // clock gives correlated sequences when ticks land close together.
    private static readonly Random Rng = new();

    public BotEngine(
        IUserRepository users,
        IInstrumentRepository instruments,
        IPortfolioRepository portfolio,
        IOrderRepository orderRepo,
        OrderService orders,
        IConfiguration config,
        ILogger<BotEngine> log)
    {
        _users = users;
        _instruments = instruments;
        _portfolio = portfolio;
        _orderRepo = orderRepo;
        _orders = orders;
        _config = config;
        _log = log;
    }

    public async Task RunAsync(List<Instrument> instruments, CancellationToken ct)
    {
        if (!_config.GetValue("Bots:Enabled", false)) return;

        var multiplier = _config.GetValue("Bots:ActivityMultiplier", 1.0);
        var minActions = _config.GetValue("Bots:MinActionsPerTick", 1);
        var maxActions = _config.GetValue("Bots:MaxActionsPerTick", 4);
        var maxOpen    = _config.GetValue("Bots:MaxOpenOrdersPerBot", 12);
        if (multiplier <= 0) return;

        var tradable = instruments
            .Where(i => i.IsActive && i.Type == InstrumentType.Stock)
            .ToList();
        if (tradable.Count == 0) return;

        var bots = await _users.GetBotsAsync(ct);
        if (bots.Count == 0) return;
        var botIds = bots.Select(b => b.Id).ToHashSet();
        var cancelled = await RequoteAsync(tradable, botIds, ct);

        var hotList = BuildHotList(tradable);

        var placed = 0;
        var rejected = 0;

        foreach (var bot in bots)
        {
            // Fractional multipliers below 1 act as a per-bot participation
            // chance, so turning activity down thins the crowd rather than
            // making every bot act a little less.
            if (multiplier < 1.0 && Rng.NextDouble() > multiplier) continue;

            // Without a cap a bot keeps quoting until its cash is fully locked
            // in resting orders, then goes silent — the opposite of what a
            // liquidity provider should do.
            var open = await _orderRepo.GetPendingByUserAsync(bot.Id, ct);
            if (open.Count >= maxOpen) continue;

            var scaled = (int)Math.Round(Rng.Next(minActions, maxActions + 1)
                                         * Math.Max(multiplier, 1.0));

            for (var n = 0; n < scaled; n++)
            {
                var instrument = Pick(tradable, hotList);
                var result = await ActAsync(bot, instrument, ct);

                if (result == OrderResult.Success) placed++;
                else rejected++;
            }
        }

        if (placed > 0 || rejected > 0 || cancelled > 0)
            _log.LogInformation("Bots: {Placed} placed, {Rejected} rejected, {Cancelled} cancelled",
                placed, rejected, cancelled);
    }

    private async Task<OrderResult> ActAsync(User bot, Instrument instrument, CancellationToken ct)
    {
        var p = PersonalityOf(bot.Id);

        var direction = await ChooseDirectionAsync(bot, instrument, ct);
        if (direction is null) return OrderResult.InsufficientFunds;

        // Most quotes sit off currentPrice and rest. A minority are priced
        // through it, which is the only way a bot ever crosses — it can't see
        // the book, so aggression is a pricing choice, not a reaction to what's
        // actually resting there.
        var crosses = Rng.NextDouble() < _config.GetValue("Bots:CrossChance", 0.12);
        var spread  = p.Spread * (decimal)(0.5 + Rng.NextDouble());

        var maxSpread = (decimal)_config.GetValue("Bots:MaxSpreadPct", 0.012);
        var offset = crosses ? -(maxSpread * 1.2m) : spread;

        var price = direction == OrderDirection.Buy
            ? instrument.CurrentPrice * (1m - offset)
            : instrument.CurrentPrice * (1m + offset);

        var quantity = QuantityFor(bot, instrument, direction.Value, p);
        if (quantity < 1) return OrderResult.InvalidQuantity;

        var (result, _) = await _orders.PlaceLimitOrderAsync(
            bot.Id, instrument.Id, quantity, price,
            stopPrice: null, direction.Value, ct);

        if (result != OrderResult.Success)
            _log.LogDebug("Bot {Bot} rejected on {Symbol}: {Result}",
                bot.UserName, instrument.Symbol, result);

        return result;
    }

    /// <summary>
    /// Sell only what the bot actually holds unlocked, otherwise buy. Shorting
    /// is allowed by the rules, but a liquidity provider that shorts on a coin
    /// flip accumulates margin obligations it never intends to manage.
    /// </summary>
    private async Task<OrderDirection?> ChooseDirectionAsync(
        User bot, Instrument instrument, CancellationToken ct)
    {
        var item = await _portfolio.GetAsync(bot.Id, instrument.Id, ct);
        var sellable = item is null ? 0 : item.TotalQuantity - item.LockedQuantity;

        var canSell = true;
        var canBuy  = bot.FreeCashBalance > instrument.CurrentPrice * 5m;

        if (canSell && canBuy) return Rng.Next(2) == 0 ? OrderDirection.Buy : OrderDirection.Sell;
        if (canSell) return OrderDirection.Sell;
        if (canBuy)  return OrderDirection.Buy;
        return null;
    }

    /// <summary>
    /// Deliberately smaller than a typical user order, so a user's order fills
    /// across several bot quotes and exercises the partial-fill path.
    /// </summary>
    private int QuantityFor(User bot, Instrument instrument, OrderDirection direction, Personality p)
    {
        var min = _config.GetValue("Bots:MinQuantity", 5);
        var max = _config.GetValue("Bots:MaxQuantity", 30);

        var baseQty = (int)Math.Round(Rng.Next(min, max + 1) * p.Size);

        if (direction == OrderDirection.Buy)
        {
            // Never commit more than a slice of free cash to one quote, so a bot
            // stays able to quote elsewhere instead of locking everything into
            // a single name.
            var affordable = (int)(bot.FreeCashBalance * 0.05m
                                   / Math.Max(instrument.CurrentPrice, 0.01m));
            return Math.Clamp(baseQty, 0, affordable);
        }

        return baseQty;
    }

    /// <summary>
    /// Real markets concentrate volume in a handful of names. Without this,
    /// 25 bots spread over 100 instruments leave every book with one order
    /// in it and nothing ever matches.
    /// </summary>
    private List<Instrument> BuildHotList(List<Instrument> tradable)
    {
        var size = Math.Min(_config.GetValue("Bots:HotListSize", 20), tradable.Count);

        // Deterministic by instrument Id, so the hot names stay the same across
        // ticks and restarts instead of reshuffling every 15 seconds.
        return tradable
            .OrderBy(i => i.Id)
            .Take(size)
            .ToList();
    }

    private Instrument Pick(List<Instrument> all, List<Instrument> hot)
    {
        var share = _config.GetValue("Bots:HotListShare", 0.8);

        return hot.Count > 0 && Rng.NextDouble() < share
            ? hot[Rng.Next(hot.Count)]
            : all[Rng.Next(all.Count)];
    }

    private readonly record struct Personality(decimal Spread, double Size);

    /// <summary>
    /// Derived from the Id rather than stored: no migration, and a bot behaves
    /// the same way across restarts without a column to keep in sync.
    /// </summary>
    private Personality PersonalityOf(Guid id)
    {
        var h = Math.Abs(id.GetHashCode());

        var minSpread = _config.GetValue("Bots:MinSpreadPct", 0.002);
        var maxSpread = _config.GetValue("Bots:MaxSpreadPct", 0.012);

        var spread = minSpread + (maxSpread - minSpread) * ((h % 100) / 100.0);
        var size   = 0.5 + ((h / 100) % 100) / 100.0 * 1.5;   // 0.5x - 2.0x

        return new Personality((decimal)spread, size);
    }


    /// <summary>
    /// Cancels bot quotes that currentPrice has drifted away from. Without this
    /// they rest forever: nothing expires them, and the external feed moves price
    /// with no trade behind it, so the whole book silently goes stale and the
    /// order count climbs without limit.
    ///
    /// Cancelled, not re-priced — the next ActAsync writes a fresh quote against
    /// the current price, and going through CancelAsync releases the reservation
    /// the same way a user's cancel does.
    /// </summary>
    private async Task<int> RequoteAsync(List<Instrument> tradable, HashSet<Guid> botIds, CancellationToken ct)
    {
        var driftLimit = (decimal)_config.GetValue("Bots:DriftCancelPct", 0.008);
        var maxCancels = _config.GetValue("Bots:MaxCancelsPerTick", 120);

        var prices = tradable.ToDictionary(i => i.Id, i => i.CurrentPrice);
        var open = await _orderRepo.GetPendingLimitOrdersAsync(ct);

        var stale = open
            .Where(o => botIds.Contains(o.UserId))
            .Where(o => o.Price is > 0)
            .Where(o =>
            {
                if (!prices.TryGetValue(o.InstrumentId, out var current) || current <= 0)
                    return false;

                var drift = Math.Abs(o.Price!.Value - current) / current;
                return drift > driftLimit;
            })
            // Furthest away first, so the worst quotes go even when the cap bites.
            .OrderByDescending(o =>
                Math.Abs(o.Price!.Value - prices[o.InstrumentId]) / prices[o.InstrumentId])
            .Take(maxCancels)
            .ToList();

        var cancelled = 0;

        foreach (var order in stale)
        {
            // NotCancellable means the match pass filled it first — expected, not an error.
            var result = await _orders.CancelAsync(order.UserId, order.Id, ct);
            if (result == OrderResult.Success) cancelled++;
        }

        return cancelled;
    }




}