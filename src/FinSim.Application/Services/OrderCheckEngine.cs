using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;
using Microsoft.Extensions.Logging;
using FinSim.Application.Dtos;

namespace FinSim.Application.Services
{
    public class OrderCheckEngine
    {
        private readonly IOrderRepository _orders;
        private readonly IUserRepository _users;
        private readonly IPortfolioRepository _portfolio;
        private readonly ITransactionRepository _transactions;
        private readonly ILogger<OrderCheckEngine> _logger;
        
        /// <summary>Traded quantity per instrument from the last MatchAsync call.
        /// BackgroundWorker reads this to stamp Volume on the PriceHistory row.</summary>
        public Dictionary<Guid, double> LastTickVolume { get; private set; } = new();

        public OrderCheckEngine(
            IOrderRepository orders,
            IUserRepository users,
            IPortfolioRepository portfolio,
            ITransactionRepository transactions,
            ILogger<OrderCheckEngine> logger)
        {
            _orders = orders;
            _users = users;
            _portfolio = portfolio;
            _transactions = transactions;
            _logger = logger;
        }

        public async Task<List<OrderOutcome>> MatchAsync(
            IReadOnlyCollection<Instrument> instruments, CancellationToken ct)
        {
            var touched = new List<OrderOutcome>();
            //var map = instruments.ToDictionary(i => i.Id);

            // Accumulate traded quantity per instrument this tick; BackgroundWorker
            // stamps it onto the PriceHistory row it writes after the save.
            LastTickVolume = new Dictionary<Guid, double>();

            foreach (var instrument in instruments)
            {
                if (!instrument.IsActive) continue;

                var book = await _orders.GetOpenBookAsync(instrument.Id, ct);
                if (book.Count == 0) continue;

                // Frozen collar reference for the whole walk on this instrument.
                var reference = instrument.CurrentPrice;
                var low  = reference * (1m - MarketRules.CollarBand);
                var high = reference * (1m + MarketRules.CollarBand);

                foreach (var o in book)
                {
                    if (o.StopPrice is null || o.ImmediateOrCancel) continue;
                    if (reference > o.StopPrice.Value) continue;

                    o.Price = Math.Round(reference * (1m - MarketRules.CollarBand), 2, MidpointRounding.AwayFromZero);
                    o.ImmediateOrCancel = true;
                    o.UpdatedAt = DateTimeOffset.UtcNow;
                    touched.Add(new OrderOutcome(o.UserId, ToDto(o, instrument, null)));
                }

                int Remaining(Order o) => o.Quantity - o.FilledQuantity;

                // Bids: highest price first, then FIFO. Asks: lowest first, then FIFO.
                var bids = book.Where(o => o.Direction == OrderDirection.Buy  && o.Price is not null
                                        && (o.StopPrice is null || o.ImmediateOrCancel))
                               .OrderByDescending(o => o.Price!.Value).ThenBy(o => o.CreatedAt).ToList();
                var asks = book.Where(o => o.Direction == OrderDirection.Sell && o.Price is not null
                                        && (o.StopPrice is null || o.ImmediateOrCancel))
                               .OrderBy(o => o.Price!.Value).ThenBy(o => o.CreatedAt).ToList();

                int bi = 0, ai = 0;

                while (bi < bids.Count && ai < asks.Count)
                {
                    var bid = bids[bi];
                    var ask = asks[ai];

                    if (Remaining(bid) <= 0) { bi++; continue; }
                    if (Remaining(ask) <= 0) { ai++; continue; }

                    // Self-trade: same user both sides. Skip this bid and try the next
                    // one against the same ask — advancing the ask would discard a
                    // resting order that other bids could still legitimately match.
                    if (bid.UserId == ask.UserId)
                    {
                        bi++;
                        continue;
                    }

                    // Cross? highest bid must meet lowest ask.
                    if (bid.Price!.Value < ask.Price!.Value) break;

                    Order? maker =
                        bid.ImmediateOrCancel && ask.ImmediateOrCancel ? null
                        : bid.ImmediateOrCancel ? ask
                        : ask.ImmediateOrCancel ? bid
                        : (bid.CreatedAt <= ask.CreatedAt ? bid : ask);

                    var fillPrice = maker?.Price!.Value ?? reference;   // both takers → frozen reference

                    // Collar: resting price outside frozen ±5% → no trade, walk stops.
                    // The aggressor can't complete this tick either — rather than leave
                    // it resting against a quote outside the band indefinitely, its
                    // remainder is cancelled and refunded here. The resting side (whose
                    // price caused the breach) is left untouched. Scenario 3.
                    if (fillPrice < low || fillPrice > high)
                    {
                        await CancelLeftoverAsync(maker == bid ? ask : bid, instrument, touched, ct);
                        break;
                    }

                    var qty = Math.Min(Remaining(bid), Remaining(ask));

                    // All-or-nothing: settle both sides; if either can't, skip this pair.
                    if (!await TrySettleFillAsync(bid, ask, qty, fillPrice, instrument, touched, ct))
                    {
                        // Couldn't settle (e.g. short side, not yet implemented) — don't
                        // let it wedge the walk; step past the resting order.
                        if (maker == bid) bi++; else ai++;
                        continue;
                    }

                    LastTickVolume[instrument.Id] =
                        LastTickVolume.GetValueOrDefault(instrument.Id) + qty;

                    instrument.CurrentPrice = fillPrice; // last-trade price
                }
                                // IOC leftover: a market order (emulated as aggressive limit) must not
                // rest. Anything still open on this instrument that's IOC gets cancelled
                // and its reservation refunded. Scenario 4. (A collar-break aggressor may
                // already have been cancelled above — CancelLeftoverAsync no-ops on it.)
                foreach (var o in book)
                {
                    if (!o.ImmediateOrCancel) continue;
                    await CancelLeftoverAsync(o, instrument, touched, ct);
                }
            }

            return touched;
        }

        /// <summary>
        /// Cancels a still-open order and refunds exactly what it reserved — the same
        /// release logic as OrderReleaseExecutor.Release, duplicated here because this
        /// runs mid-tick against orders the caller already loaded, not by id.
        /// </summary>
        private async Task CancelLeftoverAsync(
            Order o, Instrument instrument, List<OrderOutcome> touched, CancellationToken ct)
        {
            if (o.Status != OrderStatus.Pending && o.Status != OrderStatus.PartiallyFilled) return;

            var owner = await _users.GetByIdAsync(o.UserId, ct);
            var pos   = await _portfolio.GetAsync(o.UserId, o.InstrumentId, ct);

            if (o.LockedAmount > 0 && owner is not null)
            {
                owner.LockedCashBalance -= o.LockedAmount;
                owner.FreeCashBalance   += o.LockedAmount;
                if (o.Direction == OrderDirection.Sell)
                    owner.MarginUsed -= o.LockedAmount;
                o.LockedAmount = 0m;
            }
            else if (pos is not null)
            {
                pos.LockedQuantity -= Math.Min(pos.LockedQuantity, o.Quantity - o.FilledQuantity);
            }

            o.Status = OrderStatus.Cancelled;
            o.UpdatedAt = DateTimeOffset.UtcNow;
            touched.Add(new OrderOutcome(o.UserId, ToDto(o, instrument, null)));
        }
        
        /// <summary>
        /// Settles one fill against BOTH orders. All-or-nothing: returns false without
        /// mutating anything if either side can't be settled, so the caller skips the pair.
        /// </summary>
        private async Task<bool> TrySettleFillAsync(
            Order bid, Order ask, int qty, decimal price,
            Instrument instrument, List<OrderOutcome> touched, CancellationToken ct)
        {
            var buyer  = await _users.GetByIdAsync(bid.UserId, ct);
            var seller = await _users.GetByIdAsync(ask.UserId, ct);
            if (buyer is null || seller is null) return false;

            var buyerPos  = await _portfolio.GetAsync(bid.UserId, instrument.Id, ct);
            var sellerPos = await _portfolio.GetAsync(ask.UserId, instrument.Id, ct);

            var buyerKind  = PortfolioFillExecutor.Classify(buyerPos?.TotalQuantity ?? 0, OrderDirection.Buy);
            var sellerKind = PortfolioFillExecutor.Classify(sellerPos?.TotalQuantity ?? 0, OrderDirection.Sell);

            // All-or-nothing: every share reservation is verified before anything mutates.
            if (buyerKind == FillKind.CoverShort && (buyerPos is null || buyerPos.LockedQuantity < qty))
                return false;
            if (sellerKind == FillKind.ReduceOrCloseLong && (sellerPos is null || sellerPos.LockedQuantity < qty))
                return false;

            var gross = Math.Round(price * qty, 2, MidpointRounding.AwayFromZero);

            // ---- NEW: cash reservation, verified on the same all-or-nothing footing ----
            // A short sell reserves margin at its limit price, which is the LOWEST price it
            // can fill at — so any fill above the limit needs more margin than was reserved.
            // The proceeds half of the collateral is self-financing (gross comes in, gross
            // gets locked), so the entire shortfall is the margin half. Verify the seller
            // can cover it before anything mutates; otherwise the caller skips the pair.
            if (sellerKind == FillKind.OpenOrAddShort)
            {
                var askRemainingPre     = ask.Quantity - ask.FilledQuantity;
                var askLockedPerUnitPre = askRemainingPre > 0 ? ask.LockedAmount / askRemainingPre : 0m;
                var askReleasePre       = Math.Round(askLockedPerUnitPre * qty, 2, MidpointRounding.AwayFromZero);
                var requiredMargin      = Math.Round(MarginCalculator.InitialMarginRate * gross, 2, MidpointRounding.AwayFromZero);

                if (requiredMargin - askReleasePre > seller.FreeCashBalance) return false;
            }
            // ---- end NEW ----

            // Short size and entry price before the fill. The collateral recompute needs
            // the pair, and ApplyShortOpen moves both at once.
            var buyerShortBefore  = Math.Max(0, -(buyerPos?.TotalQuantity  ?? 0));
            var buyerAvgBefore    = buyerPos?.AverageCost  ?? 0m;
            var sellerShortBefore = Math.Max(0, -(sellerPos?.TotalQuantity ?? 0));
            var sellerAvgBefore   = sellerPos?.AverageCost ?? 0m;

            // ---- BUYER: per-unit slice of the cash locked at placement. A cover buy
            // reserved shares instead, so its LockedAmount is 0 and the slice is 0. ----
            var buyerRemaining = bid.Quantity - bid.FilledQuantity;
            var buyerLockedPerUnit = buyerRemaining > 0 ? bid.LockedAmount / buyerRemaining : 0m;
            var buyerReleaseFromLock = Math.Round(buyerLockedPerUnit * qty, 2, MidpointRounding.AwayFromZero);

            buyer.LockedCashBalance -= buyerReleaseFromLock;
            buyer.FreeCashBalance   += buyerReleaseFromLock - gross;   // refund = locked slice − actual cost
            bid.LockedAmount        -= buyerReleaseFromLock;

            if (buyerKind == FillKind.CoverShort) buyerPos!.LockedQuantity -= qty;

            var buyerFill = PortfolioFillExecutor.Apply(
                _portfolio, buyer, buyerPos, bid.UserId, instrument.Id,
                OrderDirection.Buy, qty, price);

            // ---- SELLER ----
            if (sellerKind == FillKind.ReduceOrCloseLong) sellerPos!.LockedQuantity -= qty;

            if (sellerKind == FillKind.OpenOrAddShort)
            {
                // Order-level margin reserved at placement, drawn down per unit like the
                // buyer's cash. Position margin is handled separately below.
                var askRemaining = ask.Quantity - ask.FilledQuantity;
                var askLockedPerUnit = askRemaining > 0 ? ask.LockedAmount / askRemaining : 0m;
                var askRelease = Math.Round(askLockedPerUnit * qty, 2, MidpointRounding.AwayFromZero);

                seller.LockedCashBalance -= askRelease;
                seller.FreeCashBalance   += askRelease;
                seller.MarginUsed        -= askRelease;
                ask.LockedAmount         -= askRelease;
            }

            var sellerFill = PortfolioFillExecutor.Apply(
                _portfolio, seller, sellerPos, ask.UserId, instrument.Id,
                OrderDirection.Sell, qty, price);

            seller.FreeCashBalance += gross;

            // ---- position collateral, recomputed from scratch (scenario 6) ----
            if (buyerKind == FillKind.CoverShort)
                MarginCalculator.ResyncShortCollateral(buyer, buyerShortBefore, buyerAvgBefore,
                    buyerShortBefore - qty, buyerAvgBefore);   // cover never moves avgCost

            if (sellerKind == FillKind.OpenOrAddShort)
                MarginCalculator.ResyncShortCollateral(seller, sellerShortBefore, sellerAvgBefore,
                    sellerShortBefore + qty, sellerPos?.AverageCost ?? price);

            // ---- order caches: FilledQuantity, running AvgPrice, status ----
            ApplyFillCache(bid, qty, price);
            ApplyFillCache(ask, qty, price);

            // ---- transaction: six-field split ----
            _transactions.Add(new Transaction
            {
                Id               = Guid.NewGuid(),
                BuyerOrderId     = bid.Id,
                SellerOrderId    = ask.Id,
                BuyerUserId      = bid.UserId,
                SellerUserId     = ask.UserId,
                InstrumentId     = instrument.Id,
                ExecutedQuantity = qty,
                ExecutedPrice    = price,
                TotalAmount      = gross,
                BuyerRealizedPnL = buyerFill.Realized,
                SellerRealizedPnL = sellerFill.Realized,
                TransactionDate  = DateTimeOffset.UtcNow
            });

            touched.Add(new OrderOutcome(bid.UserId, ToDto(bid, instrument, gross)));
            touched.Add(new OrderOutcome(ask.UserId, ToDto(ask, instrument, gross)));
            return true;
        }
        private static void ApplyFillCache(Order o, int qty, decimal price)
        {
            var newFilled = o.FilledQuantity + qty;
            o.AvgPrice = ((o.AvgPrice * o.FilledQuantity) + price * qty) / newFilled;
            o.FilledQuantity = newFilled;
            o.Status = o.FilledQuantity >= o.Quantity
                ? OrderStatus.Filled
                : OrderStatus.PartiallyFilled;
            o.UpdatedAt = DateTimeOffset.UtcNow;
        }

        private OrderOutcome Reject(
            Order order, User? user, PortfolioItem? portItem, Instrument? instrument, string reason)
        {
            // LockedAmount > 0 means cash was reserved at placement — an ordinary buy or a
            // short-opening sell's margin. Otherwise (buy or sell alike) a share quantity was
            // reserved instead — a cover buy's short shares or an ordinary sell's long shares.
            if (order.LockedAmount > 0)
            {
                if (user is not null)
                {
                    user.LockedCashBalance -= order.LockedAmount;
                    user.FreeCashBalance   += order.LockedAmount;
                    if (order.Direction == OrderDirection.Sell)
                        user.MarginUsed -= order.LockedAmount;
                }
            }
            else if (portItem is not null)
            {
                portItem.LockedQuantity -= Math.Min(portItem.LockedQuantity, order.Quantity - order.FilledQuantity);
            }

            order.Status    = OrderStatus.Rejected;
            order.UpdatedAt = DateTimeOffset.UtcNow;

            _logger.LogWarning("Order {OrderId} rejected: {Reason}", order.Id, reason);

            return new OrderOutcome(order.UserId, ToDto(order, instrument, null));
        }

        /// <summary>
        /// GetRecentAsync ile aynı alanları üretir; ikisi ayrışırsa istemci
        /// push ile fetch arasında farklı satır görür.
        /// </summary>
        private static OrderDto ToDto(Order o, Instrument? instrument, decimal? executedAmount) =>
            OrderDtoMapper.ToDto(o, instrument?.Symbol ?? "?", executedAmount: executedAmount);
    }
}