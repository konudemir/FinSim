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
            UserId, sort, Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ── full multi-page walk: no duplicates, no skipped rows ────────────

    [Fact]
    public async Task GetFavoritesBoardAsync_walking_every_page_covers_every_row_exactly_once()
    {
        var ctx = new InstrumentTestContext();
        const int limit = 2;
        var all = GivenInstruments(5).OrderBy(i => i.Symbol).ToList();

        ctx.Instruments.GetFavoritesBoardPagedAsync(
                UserId, "symbol_asc", Arg.Is<decimal?>(p => p == null), Arg.Is<string?>(x => x == null), Arg.Is<Guid?>(id => id == null),
                limit, Arg.Any<CancellationToken>())
            .Returns(all.Take(limit + 1).ToList());

        var seen = new List<Instrument>();
        var page1 = await ctx.Service.GetFavoritesBoardAsync(UserId, "symbol_asc", null, limit, CancellationToken.None);
        seen.AddRange(page1.Items);
        Assert.NotNull(page1.NextCursor);

        ctx.Instruments.GetFavoritesBoardPagedAsync(
                UserId, "symbol_asc", Arg.Is<decimal?>(p => p == null), Arg.Any<string?>(), Arg.Any<Guid?>(),
                limit, Arg.Any<CancellationToken>())
            .Returns(all.Skip(limit).Take(limit + 1).ToList());

        var page2 = await ctx.Service.GetFavoritesBoardAsync(UserId, "symbol_asc", page1.NextCursor, limit, CancellationToken.None);
        seen.AddRange(page2.Items);
        Assert.NotNull(page2.NextCursor);

        ctx.Instruments.GetFavoritesBoardPagedAsync(
                UserId, "symbol_asc", Arg.Is<decimal?>(p => p == null), Arg.Any<string?>(), Arg.Any<Guid?>(),
                limit, Arg.Any<CancellationToken>())
            .Returns(all.Skip(2 * limit).Take(limit + 1).ToList());

        var page3 = await ctx.Service.GetFavoritesBoardAsync(UserId, "symbol_asc", page2.NextCursor, limit, CancellationToken.None);
        seen.AddRange(page3.Items);
        Assert.Null(page3.NextCursor);

        Assert.Equal(all.Select(i => i.Id), seen.Select(i => i.Id));
        Assert.Equal(all.Count, seen.Select(i => i.Id).Distinct().Count());
    }

    // ── cursor minted for one sort is rejected under a different sort ───

    [Fact]
    public async Task GetFavoritesBoardAsync_a_price_cursor_does_not_decode_against_a_symbol_sort()
    {
        var ctx = new InstrumentTestContext();
        const int limit = 2;
        var rows = GivenInstruments(limit + 1);

        ctx.Instruments.GetFavoritesBoardPagedAsync(
                UserId, "price_asc", Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
                limit, Arg.Any<CancellationToken>())
            .Returns(rows);
        ctx.Instruments.GetFavoritesBoardPagedAsync(
                UserId, "symbol_asc", Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
                limit, Arg.Any<CancellationToken>())
            .Returns(new List<Instrument>());

        var priceCursor = (await ctx.Service.GetFavoritesBoardAsync(UserId, "price_asc", null, limit, CancellationToken.None)).NextCursor;
        Assert.NotNull(priceCursor);

        await ctx.Service.GetFavoritesBoardAsync(UserId, "symbol_asc", priceCursor, limit, CancellationToken.None);

        await ctx.Instruments.Received().GetFavoritesBoardPagedAsync(
            UserId, "symbol_asc",
            Arg.Is<decimal?>(p => p == null), Arg.Is<string?>(s => s == null), Arg.Is<Guid?>(id => id == null),
            limit, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetFavoritesBoardAsync_a_cursor_minted_for_the_portfolio_board_does_not_decode_here()
    {
        var ctx = new InstrumentTestContext();
        var portfolioCursor = Cursor.EncodeString("portfolio_symbol_asc", "SYM0", Guid.NewGuid());

        ctx.Instruments.GetFavoritesBoardPagedAsync(
                UserId, "symbol_asc", Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<Instrument>());

        await ctx.Service.GetFavoritesBoardAsync(UserId, "symbol_asc", portfolioCursor, null, CancellationToken.None);

        await ctx.Instruments.Received().GetFavoritesBoardPagedAsync(
            UserId, "symbol_asc",
            Arg.Is<decimal?>(p => p == null), Arg.Is<string?>(s => s == null), Arg.Is<Guid?>(id => id == null),
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ── limit clamping ───────────────────────────────────────────────

    [Theory]
    [InlineData(null, Cursor.DefaultLimit)]
    [InlineData(0, Cursor.DefaultLimit)]
    [InlineData(-5, Cursor.DefaultLimit)]
    [InlineData(Cursor.MaxLimit + 50, Cursor.MaxLimit)]
    public async Task GetFavoritesBoardAsync_clamps_the_limit_before_calling_the_repo(int? limit, int expected)
    {
        var ctx = new InstrumentTestContext();

        await ctx.Service.GetFavoritesBoardAsync(UserId, "symbol_asc", null, limit, CancellationToken.None);

        await ctx.Instruments.Received().GetFavoritesBoardPagedAsync(
            UserId, "symbol_asc", Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
            expected, Arg.Any<CancellationToken>());
    }

    // ── a favorite is un-hearted mid-walk ────────────────────────────

    [Fact]
    public async Task GetFavoritesBoardAsync_a_row_unfavorited_between_page_fetches_is_simply_absent_next_page()
    {
        var ctx = new InstrumentTestContext();
        const int limit = 2;
        var all = GivenInstruments(4).OrderBy(i => i.Symbol).ToList();

        ctx.Instruments.GetFavoritesBoardPagedAsync(
                UserId, "symbol_asc", Arg.Is<decimal?>(p => p == null), Arg.Is<string?>(x => x == null), Arg.Is<Guid?>(id => id == null),
                limit, Arg.Any<CancellationToken>())
            .Returns(all.Take(limit + 1).ToList());

        var page1 = await ctx.Service.GetFavoritesBoardAsync(UserId, "symbol_asc", null, limit, CancellationToken.None);
        Assert.Equal(limit, page1.Items.Count);
        Assert.NotNull(page1.NextCursor);

        var remaining = new List<Instrument> { all[3] };
        ctx.Instruments.GetFavoritesBoardPagedAsync(
                UserId, "symbol_asc", Arg.Is<decimal?>(p => p == null), Arg.Any<string?>(), Arg.Any<Guid?>(),
                limit, Arg.Any<CancellationToken>())
            .Returns(remaining);

        var page2 = await ctx.Service.GetFavoritesBoardAsync(UserId, "symbol_asc", page1.NextCursor, limit, CancellationToken.None);

        Assert.Single(page2.Items);
        Assert.Equal(all[3].Id, page2.Items[0].Id);
        Assert.Null(page2.NextCursor);
    }
}
