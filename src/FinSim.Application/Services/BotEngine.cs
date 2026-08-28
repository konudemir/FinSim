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

    public async Task RunAsync(List<Instrument> instruments, decimal marketMove, CancellationToken ct)
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

        var prices = tradable.ToDictionary(i => i.Id, i => i.CurrentPrice);
        var hotList = BuildHotList(tradable);
        var hotIds = hotList.Select(i => i.Id).ToHashSet();
        var coldList = tradable.Where(i => !hotIds.Contains(i.Id)).ToList();

        var placed = 0;
        var rejected = 0;
        var cancelled = 0;

        foreach (var bot in bots)
        {
            // Fractional multipliers below 1 act as a per-bot participation
            // chance, so turning activity down thins the crowd rather than
            // making every bot act a little less.
            if (multiplier < 1.0 && Rng.NextDouble() > multiplier) continue;

            // A bot approaching its cap trims its own least-likely-to-fill
            // quotes (furthest from currentPrice) to make room, instead of
            // quoting until it's full and then going silent.
            var open = await _orderRepo.GetPendingByUserAsync(bot.Id, ct);
            var freed = await TrimNearCapacityAsync(bot, open, prices, maxOpen, ct);
            cancelled += freed;
            if (open.Count - freed >= maxOpen) continue;

            var scaled = (int)Math.Round(Rng.Next(minActions, maxActions + 1)
                                         * Math.Max(multiplier, 1.0));

            for (var n = 0; n < scaled; n++)
            {
                var instrument = Pick(hotList, coldList);
                var result = await ActAsync(bot, instrument, marketMove, ct);

                if (result == OrderResult.Success) placed++;
                else rejected++;
            }
        }

        // The hot list soaks up most random picks by design, so a large cold
        // tail can go ticks without a single quote purely by chance — its book
        // thins out and its liquidity dies, not because anyone chose to ignore
        // it. Round-robin a few guaranteed actions across the cold list every
        // tick so every instrument gets touched regularly regardless of luck.
        var guaranteedCold = _config.GetValue("Bots:GuaranteedColdPicksPerTick", 3);
        for (var g = 0; g < Math.Min(guaranteedCold, coldList.Count); g++)
        {
            var instrument = coldList[_coldRotation % coldList.Count];
            _coldRotation++;

            var bot = bots[Rng.Next(bots.Count)];
            var result = await ActAsync(bot, instrument, marketMove, ct);

            if (result == OrderResult.Success) placed++;
            else rejected++;
        }

        if (placed > 0 || rejected > 0 || cancelled > 0)
            _log.LogInformation("Bots: {Placed} placed, {Rejected} rejected, {Cancelled} cancelled",
                placed, rejected, cancelled);
    }

    // Shared across ticks so the round-robin keeps advancing through the cold
    // list rather than resetting to the same starting point every tick.
    private static int _coldRotation;

    private async Task<OrderResult> ActAsync(User bot, Instrument instrument, decimal marketMove, CancellationToken ct)
    {
        var p = PersonalityOf(bot.Id);

        var (direction, isShortOpen) = await ChooseDirectionAsync(bot, instrument, p, marketMove, ct);
        if (direction is null) return OrderResult.InsufficientFunds;

        // Most quotes sit off currentPrice and rest. A minority are priced
        // through it, which is the only way a bot ever crosses — it can't see
        // the book, so aggression is a pricing choice, not a reaction to what's
        // actually resting there. Contrarians cross far more often: leaning
        // against the trend only moves the price if the order actually trades.
        var crossChance = p.Contrarian
            ? _config.GetValue("Bots:ContrarianCrossChance", 0.55)
            : _config.GetValue("Bots:CrossChance", 0.16);
        var crosses = Rng.NextDouble() < crossChance;
        var spread  = p.Spread * (decimal)(0.5 + Rng.NextDouble());

        var maxSpread = (decimal)_config.GetValue("Bots:MaxSpreadPct", 0.016);
        var offset = crosses ? -(maxSpread * 1.2m) : spread;

        var price = direction == OrderDirection.Buy
            ? instrument.CurrentPrice * (1m - offset)
            : instrument.CurrentPrice * (1m + offset);

        var quantity = QuantityFor(bot, instrument, direction.Value, p, isShortOpen);
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
    /// Sell only what the bot actually holds unlocked, otherwise buy — except most
    /// bots hold only a handful of the ~100 tradable names (seeded to half the
    /// bots, 2-5 instruments each), so for most bot/instrument pairs canSell is
    /// false and the old rule forced a buy every time. Applied across the whole
    /// crowd that's a one-way bid on nearly every instrument, not a coin flip —
    /// it pushes price up regardless of what direction any individual bot wants.
    ///
    /// A bounded slice of that would-be-buy flow instead opens a small short, so
    /// instruments nobody happens to hold still get real sell-side pressure. This
    /// isn't unconstrained "coin-flip shorting" the size cap in QuantityFor keeps
    /// any one short small, and OrderService's margin check bounds it further.
    ///
    /// Most bots are trend-neutral and coin-flip when both sides are open. A
    /// small contrarian minority leans the other way on purpose: they buy
    /// into a falling market and sell into a rising one, fading the move
    /// instead of following it. That's what stops the book from being pure
    /// one-way liquidity and makes the tape push back on runs.
    /// </summary>
    private async Task<(OrderDirection? Direction, bool IsShortOpen)> ChooseDirectionAsync(
        User bot, Instrument instrument, Personality p, decimal marketMove, CancellationToken ct)
    {
        var item = await _portfolio.GetAsync(bot.Id, instrument.Id, ct);
        var sellable = item is null ? 0 : item.TotalQuantity - item.LockedQuantity;

        var canSell = sellable > 0;
        var canBuy  = bot.FreeCashBalance > instrument.CurrentPrice * 5m;

        if (canSell && canBuy)
        {
            var deadband = (decimal)_config.GetValue("Bots:ContrarianDeadbandPct", 0.001);
            if (p.Contrarian && Math.Abs(marketMove) > deadband)
                return (marketMove > 0 ? OrderDirection.Sell : OrderDirection.Buy, false);

            return (Rng.Next(2) == 0 ? OrderDirection.Buy : OrderDirection.Sell, false);
        }
        if (canSell) return (OrderDirection.Sell, false);
        if (canBuy)
        {
            var shortChance = _config.GetValue("Bots:ShortChance", 0.35);
            if (Rng.NextDouble() < shortChance) return (OrderDirection.Sell, true);
            return (OrderDirection.Buy, false);
        }
        return (null, false);
    }

    /// <summary>
    /// Deliberately smaller than a typical user order, so a user's order fills
    /// across several bot quotes and exercises the partial-fill path.
    /// </summary>
    private int QuantityFor(
        User bot, Instrument instrument, OrderDirection direction, Personality p, bool isShortOpen)
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

        if (isShortOpen)
        {
            // Same slice-of-cash discipline as a buy, sized against the margin a
            // short actually reserves rather than the full notional, so opening
            // one doesn't lock up a wildly different share of the bot's cash
            // than a buy of the same "size" would.
            var affordable = (int)(bot.FreeCashBalance * 0.05m
                                   / Math.Max(instrument.CurrentPrice * MarginCalculator.InitialMarginRate, 0.01m));
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

    /// <summary>
    /// The non-hot branch draws only from <paramref name="cold"/>, not the full
    /// universe — sampling from everyone there would re-hit hot names too and
    /// starve the cold tail of the share it's supposed to get.
    /// </summary>
    private Instrument Pick(List<Instrument> hot, List<Instrument> cold)
    {
        var share = _config.GetValue("Bots:HotListShare", 0.8);

        if (hot.Count > 0 && (cold.Count == 0 || Rng.NextDouble() < share))
            return hot[Rng.Next(hot.Count)];

        return cold[Rng.Next(cold.Count)];
    }

    private readonly record struct Personality(decimal Spread, double Size, bool Contrarian);

    /// <summary>
    /// Derived from the Id rather than stored: no migration, and a bot behaves
    /// the same way across restarts without a column to keep in sync.
    /// </summary>
    private Personality PersonalityOf(Guid id)
    {
        var h = Math.Abs(id.GetHashCode());

        var minSpread = _config.GetValue("Bots:MinSpreadPct", 0.002);
        var maxSpread = _config.GetValue("Bots:MaxSpreadPct", 0.016);

        var spread = minSpread + (maxSpread - minSpread) * ((h % 100) / 100.0);
        var size   = 0.5 + ((h / 100) % 100) / 100.0 * 1.5;   // 0.5x - 2.0x

        // A fixed, deterministic slice of bots are contrarians — same bot,
        // same role, every tick and every restart.
        var contrarianShare = _config.GetValue("Bots:ContrarianShare", 0.15);
        var contrarian = (h / 10_000 % 100) / 100.0 < contrarianShare;

        return new Personality((decimal)spread, size, contrarian);
    }


    /// <summary>
    /// A bot's resting quotes only free up by filling. If price drifts away from
    /// one it just never fills, and with nothing else expiring it the bot's open
    /// orders climb until it hits MaxOpenOrdersPerBot and goes silent — the
    /// opposite of what a liquidity provider should do.
    ///
    /// Instead of cancelling on drift alone, this only triggers once a bot is
    /// close to its cap, and only cancels as many of its own quotes as needed
    /// to make room — the ones furthest from currentPrice, since those are the
    /// least likely to ever fill. Cancelled, not re-priced: the next ActAsync
    /// in this same tick can write a fresh quote against the current price, and
    /// going through CancelAsync releases the reservation like a user's cancel.
    /// </summary>
    private async Task<int> TrimNearCapacityAsync(
        User bot, List<Order> open, Dictionary<Guid, decimal> prices, int maxOpen, CancellationToken ct)
    {
        var nearLimitShare = _config.GetValue("Bots:NearLimitShare", 0.8);
        var threshold = (int)(maxOpen * nearLimitShare);
        if (open.Count < threshold) return 0;

        // Leave enough headroom for at least one fresh quote this tick.
        var toFree = open.Count - threshold + 1;

        var candidates = open
            .Where(o => o.Price is > 0 && prices.TryGetValue(o.InstrumentId, out var current) && current > 0)
            .OrderByDescending(o =>
                Math.Abs(o.Price!.Value - prices[o.InstrumentId]) / prices[o.InstrumentId])
            .Take(toFree)
            .ToList();

        var cancelled = 0;

        foreach (var order in candidates)
        {
            // NotCancellable means the match pass filled it first — expected, not an error.
            var result = await _orders.CancelAsync(bot.Id, order.Id, ct);
            if (result == OrderResult.Success) cancelled++;
        }

        return cancelled;
    }




}