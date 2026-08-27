using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;
using NSubstitute;

namespace FinSim.Tests;

public class OrderServicePagingTests
{
    private static List<Order> GivenOrders(OrderTestContext ctx, int count)
    {
        var list = new List<Order>();
        for (var i = 0; i < count; i++)
        {
            var order = OrderTestContext.NewPendingOrder(
                OrderDirection.Buy, 1, 10m, ctx.UserId, ctx.InstrumentId);
            order.CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-i);
            list.Add(order);
        }
        return list;
    }

    [Fact]
    public async Task GetRecentAsync_trims_the_extra_row_and_returns_a_cursor_when_there_are_more_rows_than_the_limit()
    {
        var ctx = new OrderTestContext();
        ctx.GivenUser();
        const int limit = 3;

        // The repo is asked for limit + 1 rows so the service can tell whether
        // there's a next page; it must trim that extra row before returning.
        ctx.Orders.GetByUserPagedAsync(
                ctx.UserId, Arg.Any<bool?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<Guid?>(), limit, Arg.Any<CancellationToken>())
            .Returns(GivenOrders(ctx, limit + 1));

        var result = await ctx.Service.GetRecentAsync(ctx.UserId, null, null, limit, CancellationToken.None);

        Assert.Equal(limit, result.Items.Count);
        Assert.NotNull(result.NextCursor);
    }

    [Fact]
    public async Task GetRecentAsync_returns_no_cursor_when_rows_exactly_fill_the_limit()
    {
        var ctx = new OrderTestContext();
        ctx.GivenUser();
        const int limit = 3;

        ctx.Orders.GetByUserPagedAsync(
                ctx.UserId, Arg.Any<bool?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<Guid?>(), limit, Arg.Any<CancellationToken>())
            .Returns(GivenOrders(ctx, limit));

        var result = await ctx.Service.GetRecentAsync(ctx.UserId, null, null, limit, CancellationToken.None);

        Assert.Equal(limit, result.Items.Count);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task GetRecentAsync_returns_no_cursor_when_there_are_fewer_rows_than_the_limit()
    {
        var ctx = new OrderTestContext();
        ctx.GivenUser();
        const int limit = 5;

        ctx.Orders.GetByUserPagedAsync(
                ctx.UserId, Arg.Any<bool?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<Guid?>(), limit, Arg.Any<CancellationToken>())
            .Returns(GivenOrders(ctx, 2));

        var result = await ctx.Service.GetRecentAsync(ctx.UserId, null, null, limit, CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task GetRecentAsync_returns_empty_items_and_no_cursor_when_the_repo_has_nothing()
    {
        var ctx = new OrderTestContext();
        ctx.GivenUser();

        ctx.Orders.GetByUserPagedAsync(
                ctx.UserId, Arg.Any<bool?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<Order>());

        var result = await ctx.Service.GetRecentAsync(ctx.UserId, null, null, null, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task GetRecentAsync_cursor_minted_for_openOnly_true_does_not_page_the_openOnly_false_list()
    {
        var ctx = new OrderTestContext();
        ctx.GivenUser();
        const int limit = 2;

        ctx.Orders.GetByUserPagedAsync(
                ctx.UserId, true, Arg.Any<DateTimeOffset?>(), Arg.Any<Guid?>(), limit, Arg.Any<CancellationToken>())
            .Returns(GivenOrders(ctx, limit + 1));
        ctx.Orders.GetByUserPagedAsync(
                ctx.UserId, false, Arg.Any<DateTimeOffset?>(), Arg.Any<Guid?>(), limit, Arg.Any<CancellationToken>())
            .Returns(new List<Order>());

        var openPage = await ctx.Service.GetRecentAsync(ctx.UserId, true, null, limit, CancellationToken.None);
        Assert.NotNull(openPage.NextCursor);

        // The cursor decodes against "orders_open_desc" but this call computes
        // "orders_closed_desc" — it must fail to decode and fall back to page 1
        // (afterTs/afterId null) rather than paging into the wrong result set.
        await ctx.Service.GetRecentAsync(ctx.UserId, false, openPage.NextCursor, limit, CancellationToken.None);

        await ctx.Orders.Received().GetByUserPagedAsync(
            ctx.UserId, false,
            Arg.Is<DateTimeOffset?>(x => x == null),
            Arg.Is<Guid?>(x => x == null),
            limit, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public async Task GetRecentAsync_passes_openOnly_through_to_the_repo_unchanged(bool? openOnly)
    {
        var ctx = new OrderTestContext();
        ctx.GivenUser();

        ctx.Orders.GetByUserPagedAsync(
                ctx.UserId, openOnly, Arg.Any<DateTimeOffset?>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<Order>());

        await ctx.Service.GetRecentAsync(ctx.UserId, openOnly, null, null, CancellationToken.None);

        await ctx.Orders.Received().GetByUserPagedAsync(
            ctx.UserId, openOnly, Arg.Any<DateTimeOffset?>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
