using FinSim.Application.Pagination;
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

    // ── full multi-page walk: no duplicates, no skipped rows ────────────

    [Fact]
    public async Task GetRecentAsync_walking_every_page_covers_every_row_exactly_once()
    {
        var ctx = new OrderTestContext();
        ctx.GivenUser();
        const int limit = 3;
        var all = GivenOrders(ctx, 7);

        for (var page = 1; page <= 3; page++)
        {
            var thisPage = page;
            ctx.Orders.GetByUserPagedAsync(
                    ctx.UserId, Arg.Any<bool?>(), thisPage, limit, Arg.Any<CancellationToken>())
                .Returns(new PagedRows<Order>(
                    all.Skip((thisPage - 1) * limit).Take(limit).ToList(), all.Count));
        }

        var seen = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await ctx.Service.GetRecentAsync(ctx.UserId, null, page, limit, CancellationToken.None);
            seen.AddRange(result.Items.Select(o => o.Id));
        }

        Assert.Equal(all.Select(o => o.Id), seen);
        Assert.Equal(all.Count, seen.Distinct().Count());
    }

    [Fact]
    public async Task GetRecentAsync_the_last_page_returns_the_partial_remainder_and_the_correct_total()
    {
        var ctx = new OrderTestContext();
        ctx.GivenUser();
        const int limit = 3;
        var all = GivenOrders(ctx, 7); // 3 full pages -> page 3 has 1 row

        ctx.Orders.GetByUserPagedAsync(ctx.UserId, Arg.Any<bool?>(), 3, limit, Arg.Any<CancellationToken>())
            .Returns(new PagedRows<Order>(all.Skip(6).Take(limit).ToList(), all.Count));

        var result = await ctx.Service.GetRecentAsync(ctx.UserId, null, 3, limit, CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(all.Count, result.TotalCount);
    }

    [Fact]
    public async Task GetRecentAsync_a_page_past_the_last_page_returns_no_items_but_the_correct_total()
    {
        var ctx = new OrderTestContext();
        ctx.GivenUser();
        const int limit = 3;
        var all = GivenOrders(ctx, 7); // totalPages = 3

        ctx.Orders.GetByUserPagedAsync(ctx.UserId, Arg.Any<bool?>(), 4, limit, Arg.Any<CancellationToken>())
            .Returns(new PagedRows<Order>(new List<Order>(), all.Count));

        var result = await ctx.Service.GetRecentAsync(ctx.UserId, null, 4, limit, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(all.Count, result.TotalCount);
        Assert.NotEqual(0, result.TotalCount);
    }

    [Fact]
    public async Task GetRecentAsync_returns_empty_items_when_the_repo_has_nothing()
    {
        var ctx = new OrderTestContext();
        ctx.GivenUser();

        ctx.Orders.GetByUserPagedAsync(
                ctx.UserId, Arg.Any<bool?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedRows<Order>(new List<Order>(), 0));

        var result = await ctx.Service.GetRecentAsync(ctx.UserId, null, null, null, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
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
                ctx.UserId, openOnly, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedRows<Order>(new List<Order>(), 0));

        await ctx.Service.GetRecentAsync(ctx.UserId, openOnly, null, null, CancellationToken.None);

        await ctx.Orders.Received().GetByUserPagedAsync(
            ctx.UserId, openOnly, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
