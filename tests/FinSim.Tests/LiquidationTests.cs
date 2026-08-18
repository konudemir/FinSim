using FinSim.Domain.Dtos;
using FinSim.Domain.Models.Enums;

namespace FinSim.Tests;

/// <summary>Force-liquidation on instrument deactivation (Part 2 of the admin feature).</summary>
public class LiquidationTests
{
    private readonly InstrumentTestContext _ctx = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Deactivate_LiquidatesHolding_CreditsCashAndRealizedPnL()
    {
        _ctx.GivenInstrument(price: 120m);
        var user = _ctx.GivenUser(free: 1_000m);
        _ctx.GivenPosition(user.Id, quantity: 10, averageCost: 100m);

        var (result, outcome) = await _ctx.Service.DeactivateAsync(_ctx.InstrumentId, _ct);

        Assert.Equal(DeactivateResult.Success, result);
        Assert.Equal(2_200m, user.FreeCashBalance);    // 1000 + (120 x 10)
        Assert.Equal(200m, user.RealizedProfitLoss);   // (120 - 100) x 10
        Assert.Equal(1, outcome!.AffectedUsers);
        Assert.Equal(10, outcome.TotalSharesLiquidated);
        Assert.False(outcome.Instrument.IsActive);
    }

    [Fact]
    public async Task Deactivate_CancelsOpenBuyOrder_ReturnsLockedAmountToFreeCash()
    {
        _ctx.GivenInstrument(price: 100m);
        var user = _ctx.GivenUser(free: 700m, locked: 300m);
        var order = _ctx.GivenPendingBuyOrder(user.Id, quantity: 3, price: 100m, lockedAmount: 300m);

        var (result, _) = await _ctx.Service.DeactivateAsync(_ctx.InstrumentId, _ct);

        Assert.Equal(DeactivateResult.Success, result);
        Assert.Equal(1_000m, user.FreeCashBalance);
        Assert.Equal(0m, user.LockedCashBalance);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public async Task Deactivate_CancelsOpenSellOrder_LiquidatesFullHoldingWithoutDoubleCounting()
    {
        _ctx.GivenInstrument(price: 90m);
        var user = _ctx.GivenUser(free: 0m);
        _ctx.GivenPosition(user.Id, quantity: 10, averageCost: 80m, locked: 4);
        var order = _ctx.GivenPendingSellOrder(user.Id, quantity: 4, price: 150m);

        var (result, outcome) = await _ctx.Service.DeactivateAsync(_ctx.InstrumentId, _ct);

        Assert.Equal(DeactivateResult.Success, result);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        // the full holding liquidates once — the 4 shares the pending sell had
        // locked are not skipped, and they are not paid out a second time either
        Assert.Equal(10, outcome!.TotalSharesLiquidated);
        Assert.Equal(900m, user.FreeCashBalance);   // 90 x 10, exactly once
    }

    [Fact]
    public async Task Deactivate_AlreadyInactiveInstrument_IsRejected()
    {
        _ctx.GivenInstrument(price: 100m, active: false);

        var (result, outcome) = await _ctx.Service.DeactivateAsync(_ctx.InstrumentId, _ct);

        Assert.Equal(DeactivateResult.AlreadyInactive, result);
        Assert.Null(outcome);
    }

    [Fact]
    public async Task Deactivate_UnknownInstrument_IsRejected()
    {
        var (result, outcome) = await _ctx.Service.DeactivateAsync(_ctx.InstrumentId, _ct);

        Assert.Equal(DeactivateResult.NotFound, result);
        Assert.Null(outcome);
    }
}
