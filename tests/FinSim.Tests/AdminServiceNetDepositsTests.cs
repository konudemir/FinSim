using FinSim.Application.Dtos;
using NSubstitute;

namespace FinSim.Tests;

/// <summary>NetDeposits must move with every admin grant, or grants register as fake profit.</summary>
public class AdminServiceNetDepositsTests
{
    private readonly AdminTestContext _ctx = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task PositiveCashGrant_RaisesNetDepositsBySameAmount()
    {
        var user = _ctx.GivenUser(free: 1_000m, netDeposits: 1_000m);

        var result = await _ctx.Service.AdjustCashAsync(_ctx.AdminId, _ctx.UserId, 500m, "top-up", _ct);

        Assert.Equal(CashAdjustResult.Success, result);
        Assert.Equal(1_500m, user.FreeCashBalance);
        Assert.Equal(1_500m, user.NetDeposits);
        // Portfolio value and NetDeposits both moved by the same amount, so P&L is unchanged.
        Assert.Equal(0m, user.FreeCashBalance - user.NetDeposits);
    }

    [Fact]
    public async Task NegativeCashGrant_LowersNetDepositsSymmetrically()
    {
        var user = _ctx.GivenUser(free: 1_000m, netDeposits: 1_000m);

        var result = await _ctx.Service.AdjustCashAsync(_ctx.AdminId, _ctx.UserId, -300m, "clawback", _ct);

        Assert.Equal(CashAdjustResult.Success, result);
        Assert.Equal(700m, user.FreeCashBalance);
        Assert.Equal(700m, user.NetDeposits);
    }

    [Fact]
    public async Task ShareGrant_RaisesNetDepositsByQuantityTimesPrice()
    {
        var user = _ctx.GivenUser(netDeposits: 1_000m);
        _ctx.GivenInstrument(price: 50m);
        _ctx.GivenNoPosition();

        var result = await _ctx.Service.AdjustSharesAsync(_ctx.AdminId, _ctx.UserId, _ctx.InstrumentId, 10, _ct);

        Assert.Equal(ShareAdjustResult.Success, result);
        Assert.Equal(1_500m, user.NetDeposits);   // 1000 + (10 x 50)
    }

    [Fact]
    public async Task ShareRemoval_LowersNetDepositsByQuantityTimesPrice()
    {
        var user = _ctx.GivenUser(netDeposits: 1_500m);
        _ctx.GivenInstrument(price: 50m);
        _ctx.GivenPosition(quantity: 10, averageCost: 50m);

        var result = await _ctx.Service.AdjustSharesAsync(_ctx.AdminId, _ctx.UserId, _ctx.InstrumentId, -4, _ct);

        Assert.Equal(ShareAdjustResult.Success, result);
        Assert.Equal(1_300m, user.NetDeposits);   // 1500 - (4 x 50)
    }
}
