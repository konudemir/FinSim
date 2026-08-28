using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;

namespace FinSim.Tests;

/// <summary>
/// A short sell reserves initial margin at its limit price — the lowest price the
/// order can fill at. When it crosses a resting bid ABOVE that limit, the position
/// it opens needs more margin than was reserved, and ResyncShortCollateral takes
/// the difference from FreeCashBalance unconditionally.
///
/// The bid must therefore be the maker (older, non-IOC) so the fill prices off it.
/// With the ask as maker the fill happens at the ask's own limit, there is no gap,
/// and the scenario doesn't exist.
/// </summary>
public class ShortFillCashGuardTests
{
    private readonly OrderTestContext _ctx = new();
    private readonly CancellationToken _ct = CancellationToken.None;
    private static readonly DateTimeOffset Base = DateTimeOffset.UtcNow.AddMinutes(-10);

    [Fact]
    public async Task ShortFillAboveLimit_RejectedWhenFreeCashCannotCoverMarginGap()
    {
        // Seller: 4750 locked as initial margin for a 100-lot short at 95
        // (0.5 x 95 x 100), and not one kurus free beyond it.
        var seller = _ctx.GivenUser(_ctx.UserId, free: 0m, locked: 4_750m);
        _ctx.GivenNoPosition(_ctx.UserId);

        var buyer = _ctx.GivenUser(_ctx.CounterpartyId, free: 100_000m, locked: 4_000m);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);

        // Resting bid — OLDER, so it is the maker and the fill prices at 100.
        _ctx.GivenPendingInQueue(
            OrderDirection.Buy, 40, 100m,
            lockedAmount: 4_000m, ownerId: _ctx.CounterpartyId, createdAt: Base);

        // Short sell arrives after, crossing upward into the bid.
        // Margin reserved for 40 lots: 0.5 x 95 x 40 = 1900.
        // Margin the position needs:   0.5 x 100 x 40 = 2000.  Gap: 100.
        var ask = _ctx.GivenPendingInQueue(
            OrderDirection.Sell, 100, 95m,
            lockedAmount: 4_750m, ownerId: _ctx.UserId, createdAt: Base.AddSeconds(10));

        var instrument = _ctx.GivenInstrument(100m);
        await _ctx.Engine.MatchAsync([instrument], _ct);

        // Free and Locked are asserted separately, not as a sum: the bug is that the
        // two drift apart by the margin gap while the total still looks correct.
        Assert.True(seller.FreeCashBalance >= 0m,
            $"seller Free went negative: {seller.FreeCashBalance}");
        Assert.Equal(0m, seller.FreeCashBalance);
        Assert.Equal(4_750m, seller.LockedCashBalance);

        // All-or-nothing: the rejected pair leaves both sides completely untouched.
        Assert.Equal(0, ask.FilledQuantity);
        Assert.Equal(OrderStatus.Pending, ask.Status);
        Assert.Equal(100_000m, buyer.FreeCashBalance);
    }

    [Fact]
    public async Task ShortFillAboveLimit_SettlesWhenFreeCashCoversMarginGap()
    {
        // Same setup, except the seller has exactly the 100 gap sitting free.
        var seller = _ctx.GivenUser(_ctx.UserId, free: 100m, locked: 4_750m);
        _ctx.GivenNoPosition(_ctx.UserId);

        _ctx.GivenUser(_ctx.CounterpartyId, free: 100_000m, locked: 4_000m);
        _ctx.GivenNoPosition(_ctx.CounterpartyId);

        _ctx.GivenPendingInQueue(
            OrderDirection.Buy, 40, 100m,
            lockedAmount: 4_000m, ownerId: _ctx.CounterpartyId, createdAt: Base);
        var ask = _ctx.GivenPendingInQueue(
            OrderDirection.Sell, 100, 95m,
            lockedAmount: 4_750m, ownerId: _ctx.UserId, createdAt: Base.AddSeconds(10));

        var instrument = _ctx.GivenInstrument(100m);
        await _ctx.Engine.MatchAsync([instrument], _ct);

        Assert.Equal(40, ask.FilledQuantity);
        Assert.Equal(OrderStatus.PartiallyFilled, ask.Status);

        // 4750 locked - 1900 released + 6000 position collateral = 8850.
        // The extra 100 came out of Free, which is the whole point: it was there.
        Assert.Equal(0m, seller.FreeCashBalance);
        Assert.Equal(8_850m, seller.LockedCashBalance);
    }
}