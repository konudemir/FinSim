using FinSim.Application.Pagination;
using FinSim.Domain.Models;
using NSubstitute;

namespace FinSim.Tests;

public class FavoritesBoardPagingTests
{
    private static List<Instrument> GivenInstruments(int count) =>
        Enumerable.Range(0, count).Select(i => new Instrument
        {
            Id = Guid.NewGuid(),
            Symbol = $"SYM{i}",
            Name = $"Instrument {i}",
            BasePrice = 100m + i,
            CurrentPrice = 100m + i,
            IsActive = true
        }).ToList();

    private static readonly Guid UserId = Guid.NewGuid();

    [Theory]
    [InlineData("price_asc")]
    [InlineData("price_desc")]
    [InlineData("symbol_asc")]
    [InlineData("symbol_desc")]
    public async Task GetFavoritesBoardAsync_passes_a_valid_sort_through_to_the_repo_unchanged(string sort)
    {
        var ctx = new InstrumentTestContext();

        await ctx.Service.GetFavoritesBoardAsync(UserId, sort, null, null, CancellationToken.None);

        await ctx.Instruments.Received().GetFavoritesBoardPagedAsync(
            UserId, sort, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ── full multi-page walk: no duplicates, no skipped rows ────────────

    [Fact]
    public async Task GetFavoritesBoardAsync_walking_every_page_covers_every_row_exactly_once()
    {
        var ctx = new InstrumentTestContext();
        const int limit = 2;
        var all = GivenInstruments(5).OrderBy(i => i.Symbol).ToList();

        for (var page = 1; page <= 3; page++)
        {
            var thisPage = page;
            ctx.Instruments.GetFavoritesBoardPagedAsync(
                    UserId, "symbol_asc", thisPage, limit, Arg.Any<CancellationToken>())
                .Returns(new PagedRows<Instrument>(
                    all.Skip((thisPage - 1) * limit).Take(limit).ToList(), all.Count));
        }

        var seen = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await ctx.Service.GetFavoritesBoardAsync(UserId, "symbol_asc", page, limit, CancellationToken.None);
            seen.AddRange(result.Items.Select(i => i.Id));
        }

        Assert.Equal(all.Select(i => i.Id), seen);
        Assert.Equal(all.Count, seen.Distinct().Count());
    }

    [Fact]
    public async Task GetFavoritesBoardAsync_the_last_page_returns_the_partial_remainder_and_the_correct_total()
    {
        var ctx = new InstrumentTestContext();
        const int limit = 2;
        var all = GivenInstruments(5).OrderBy(i => i.Symbol).ToList(); // 3 pages, page 3 has 1 row

        ctx.Instruments.GetFavoritesBoardPagedAsync(UserId, "symbol_asc", 3, limit, Arg.Any<CancellationToken>())
            .Returns(new PagedRows<Instrument>(all.Skip(4).Take(limit).ToList(), all.Count));

        var result = await ctx.Service.GetFavoritesBoardAsync(UserId, "symbol_asc", 3, limit, CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(all.Count, result.TotalCount);
    }

    [Fact]
    public async Task GetFavoritesBoardAsync_a_page_past_the_last_page_returns_no_items_but_the_correct_total()
    {
        var ctx = new InstrumentTestContext();
        const int limit = 2;
        var all = GivenInstruments(5); // totalPages = 3

        ctx.Instruments.GetFavoritesBoardPagedAsync(UserId, "symbol_asc", 4, limit, Arg.Any<CancellationToken>())
            .Returns(new PagedRows<Instrument>(new List<Instrument>(), all.Count));

        var result = await ctx.Service.GetFavoritesBoardAsync(UserId, "symbol_asc", 4, limit, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(all.Count, result.TotalCount);
        Assert.NotEqual(0, result.TotalCount);
    }

    // ── limit clamping ───────────────────────────────────────────────

    [Theory]
    [InlineData(null, Paging.DefaultLimit)]
    [InlineData(0, Paging.DefaultLimit)]
    [InlineData(-5, Paging.DefaultLimit)]
    [InlineData(Paging.MaxLimit + 50, Paging.MaxLimit)]
    public async Task GetFavoritesBoardAsync_clamps_the_limit_before_calling_the_repo(int? limit, int expected)
    {
        var ctx = new InstrumentTestContext();

        await ctx.Service.GetFavoritesBoardAsync(UserId, "symbol_asc", null, limit, CancellationToken.None);

        await ctx.Instruments.Received().GetFavoritesBoardPagedAsync(
            UserId, "symbol_asc", Arg.Any<int>(), expected, Arg.Any<CancellationToken>());
    }

    // ── a favorite is un-hearted mid-walk ────────────────────────────

    [Fact]
    public async Task GetFavoritesBoardAsync_a_row_unfavorited_between_page_fetches_is_simply_absent_next_page()
    {
        var ctx = new InstrumentTestContext();
        const int limit = 2;
        var all = GivenInstruments(4).OrderBy(i => i.Symbol).ToList();

        ctx.Instruments.GetFavoritesBoardPagedAsync(UserId, "symbol_asc", 1, limit, Arg.Any<CancellationToken>())
            .Returns(new PagedRows<Instrument>(all.Take(limit).ToList(), all.Count));

        var page1 = await ctx.Service.GetFavoritesBoardAsync(UserId, "symbol_asc", 1, limit, CancellationToken.None);
        Assert.Equal(limit, page1.Items.Count);

        var remaining = new List<Instrument> { all[3] };
        ctx.Instruments.GetFavoritesBoardPagedAsync(UserId, "symbol_asc", 2, limit, Arg.Any<CancellationToken>())
            .Returns(new PagedRows<Instrument>(remaining, 3));

        var page2 = await ctx.Service.GetFavoritesBoardAsync(UserId, "symbol_asc", 2, limit, CancellationToken.None);

        Assert.Single(page2.Items);
        Assert.Equal(all[3].Id, page2.Items[0].Id);
    }
}
