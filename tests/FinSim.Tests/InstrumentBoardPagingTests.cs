using FinSim.Application.Pagination;
using FinSim.Domain.Models;
using NSubstitute;

namespace FinSim.Tests;

public class InstrumentBoardPagingTests
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

    [Theory]
    [InlineData("price_asc")]
    [InlineData("price_desc")]
    [InlineData("symbol_asc")]
    [InlineData("symbol_desc")]
    public async Task GetBoardAsync_passes_a_valid_sort_through_to_the_repo_unchanged(string sort)
    {
        var ctx = new InstrumentTestContext();

        await ctx.Service.GetBoardAsync(sort, null, null, null, CancellationToken.None);

        await ctx.Instruments.Received().GetBoardPagedAsync(
            sort, Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData(null)]
    public async Task GetBoardAsync_normalises_an_invalid_sort_to_symbol_asc(string? badSort)
    {
        var ctx = new InstrumentTestContext();

        await ctx.Service.GetBoardAsync(badSort, null, null, null, CancellationToken.None);

        await ctx.Instruments.Received().GetBoardPagedAsync(
            "symbol_asc", Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("aapl")]
    [InlineData(null)]
    public async Task GetBoardAsync_passes_q_through_to_the_repo_unchanged(string? q)
    {
        var ctx = new InstrumentTestContext();

        await ctx.Service.GetBoardAsync("symbol_asc", q, null, null, CancellationToken.None);

        await ctx.Instruments.Received().GetBoardPagedAsync(
            "symbol_asc", Arg.Is<string?>(x => x == q), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ── full multi-page walk: no duplicates, no skipped rows ────────────

    [Fact]
    public async Task GetBoardAsync_walking_every_page_covers_every_row_exactly_once()
    {
        var ctx = new InstrumentTestContext();
        const int limit = 2;
        var all = GivenInstruments(5).OrderBy(i => i.Symbol).ToList();

        for (var page = 1; page <= 3; page++)
        {
            var thisPage = page;
            ctx.Instruments.GetBoardPagedAsync(
                    "symbol_asc", Arg.Any<string?>(), thisPage, limit, Arg.Any<CancellationToken>())
                .Returns(new PagedRows<Instrument>(
                    all.Skip((thisPage - 1) * limit).Take(limit).ToList(), all.Count));
        }

        var seen = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await ctx.Service.GetBoardAsync("symbol_asc", null, page, limit, CancellationToken.None);
            seen.AddRange(result.Items.Select(i => i.Id));
        }

        Assert.Equal(all.Select(i => i.Id), seen);
        Assert.Equal(all.Count, seen.Distinct().Count());
    }

    [Fact]
    public async Task GetBoardAsync_the_last_page_returns_the_partial_remainder_and_the_correct_total()
    {
        var ctx = new InstrumentTestContext();
        const int limit = 2;
        var all = GivenInstruments(5).OrderBy(i => i.Symbol).ToList(); // 3 pages, page 3 has 1 row

        ctx.Instruments.GetBoardPagedAsync("symbol_asc", Arg.Any<string?>(), 3, limit, Arg.Any<CancellationToken>())
            .Returns(new PagedRows<Instrument>(all.Skip(4).Take(limit).ToList(), all.Count));

        var result = await ctx.Service.GetBoardAsync("symbol_asc", null, 3, limit, CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(all.Count, result.TotalCount);
    }

    [Fact]
    public async Task GetBoardAsync_a_page_past_the_last_page_returns_no_items_but_the_correct_total()
    {
        var ctx = new InstrumentTestContext();
        const int limit = 2;
        var all = GivenInstruments(5); // totalPages = 3

        ctx.Instruments.GetBoardPagedAsync("symbol_asc", Arg.Any<string?>(), 4, limit, Arg.Any<CancellationToken>())
            .Returns(new PagedRows<Instrument>(new List<Instrument>(), all.Count));

        var result = await ctx.Service.GetBoardAsync("symbol_asc", null, 4, limit, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(all.Count, result.TotalCount);
        Assert.NotEqual(0, result.TotalCount);
    }

    [Fact]
    public async Task GetBoardAsync_returns_empty_items_when_the_repo_has_nothing()
    {
        var ctx = new InstrumentTestContext();

        var result = await ctx.Service.GetBoardAsync("symbol_asc", null, null, null, CancellationToken.None);

        Assert.Empty(result.Items);
    }

    // ── tiebreak regression: rows sharing a CurrentPrice must not repeat or
    // vanish across page boundaries when paging price_asc by offset. Mirrors
    // InstrumentRepository.ApplyBoardSort's OrderBy(CurrentPrice).ThenBy(Id). ──

    [Fact]
    public async Task GetBoardAsync_paging_price_asc_over_tied_prices_covers_every_row_exactly_once()
    {
        var ctx = new InstrumentTestContext();
        const int limit = 2;
        const decimal tiedPrice = 150m;

        // 5 instruments sharing the same CurrentPrice -- a tied group bigger than the page size.
        var tied = Enumerable.Range(0, 5).Select(i => new Instrument
        {
            Id = Guid.NewGuid(),
            Symbol = $"TIE{i}",
            Name = $"Tied {i}",
            BasePrice = tiedPrice,
            CurrentPrice = tiedPrice,
            IsActive = true
        }).ToList();

        // The stable order a correct ThenBy(Id) tiebreak would produce.
        var all = tied.OrderBy(i => i.CurrentPrice).ThenBy(i => i.Id).ToList();

        for (var page = 1; page <= 3; page++)
        {
            var thisPage = page;
            ctx.Instruments.GetBoardPagedAsync(
                    "price_asc", Arg.Any<string?>(), thisPage, limit, Arg.Any<CancellationToken>())
                .Returns(new PagedRows<Instrument>(
                    all.Skip((thisPage - 1) * limit).Take(limit).ToList(), all.Count));
        }

        var seen = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await ctx.Service.GetBoardAsync("price_asc", null, page, limit, CancellationToken.None);
            seen.AddRange(result.Items.Select(i => i.Id));
        }

        Assert.Equal(tied.Select(i => i.Id).OrderBy(id => id), seen.OrderBy(id => id));
        Assert.Equal(tied.Count, seen.Distinct().Count());
    }
}
