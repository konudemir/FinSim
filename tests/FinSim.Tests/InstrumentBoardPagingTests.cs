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
            sort, Arg.Any<string?>(), Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData(null)]
    public async Task GetBoardAsync_normalises_an_invalid_sort_to_symbol_asc(string? badSort)
    {
        var ctx = new InstrumentTestContext();

        await ctx.Service.GetBoardAsync(badSort, null, null, null, CancellationToken.None);

        await ctx.Instruments.Received().GetBoardPagedAsync(
            "symbol_asc", Arg.Any<string?>(), Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData(null)]
    public async Task GetBoardAsync_cursor_minted_under_a_bad_sort_still_decodes_on_a_repeat_request_with_the_same_bad_sort(string? badSort)
    {
        // This is the bug the normalisation exists to prevent: if the raw
        // (unrecognised) sort string were baked into the cursor while the repo
        // query used the normalised "symbol_asc", the follow-up request would
        // fail TryDecodeString and silently reset to page 1 forever.
        var ctx = new InstrumentTestContext();
        const int limit = 2;
        var rows = GivenInstruments(limit + 1);
        var expectedSymbol = rows[limit - 1].Symbol; // the row that survives the trim

        ctx.Instruments.GetBoardPagedAsync(
                "symbol_asc", Arg.Any<string?>(), Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
                limit, Arg.Any<CancellationToken>())
            .Returns(rows);

        var first = await ctx.Service.GetBoardAsync(badSort, null, null, limit, CancellationToken.None);
        Assert.NotNull(first.NextCursor);

        ctx.Instruments.GetBoardPagedAsync(
                "symbol_asc", Arg.Any<string?>(), Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
                limit, Arg.Any<CancellationToken>())
            .Returns(new List<Instrument>());

        await ctx.Service.GetBoardAsync(badSort, null, first.NextCursor, limit, CancellationToken.None);

        await ctx.Instruments.Received().GetBoardPagedAsync(
            "symbol_asc", Arg.Any<string?>(), Arg.Any<decimal?>(),
            Arg.Is<string?>(s => s == expectedSymbol), Arg.Any<Guid?>(),
            limit, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("price_asc")]
    [InlineData("price_desc")]
    public async Task GetBoardAsync_mints_a_decimal_cursor_carrying_CurrentPrice_for_price_sorts(string sort)
    {
        var ctx = new InstrumentTestContext();
        const int limit = 2;
        var rows = GivenInstruments(limit + 1);
        var lastRemaining = rows[limit - 1];

        ctx.Instruments.GetBoardPagedAsync(
                sort, Arg.Any<string?>(), Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
                limit, Arg.Any<CancellationToken>())
            .Returns(rows);

        var result = await ctx.Service.GetBoardAsync(sort, null, null, limit, CancellationToken.None);

        Assert.True(Cursor.TryDecodeDecimal(result.NextCursor, sort, out var key, out var id));
        Assert.Equal(lastRemaining.CurrentPrice, key);
        Assert.Equal(lastRemaining.Id, id);
    }

    [Theory]
    [InlineData("symbol_asc")]
    [InlineData("symbol_desc")]
    public async Task GetBoardAsync_mints_a_string_cursor_carrying_Symbol_for_symbol_sorts(string sort)
    {
        var ctx = new InstrumentTestContext();
        const int limit = 2;
        var rows = GivenInstruments(limit + 1);
        var lastRemaining = rows[limit - 1];

        ctx.Instruments.GetBoardPagedAsync(
                sort, Arg.Any<string?>(), Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
                limit, Arg.Any<CancellationToken>())
            .Returns(rows);

        var result = await ctx.Service.GetBoardAsync(sort, null, null, limit, CancellationToken.None);

        Assert.True(Cursor.TryDecodeString(result.NextCursor, sort, out var key, out var id));
        Assert.Equal(lastRemaining.Symbol, key);
        Assert.Equal(lastRemaining.Id, id);
    }

    [Fact]
    public async Task GetBoardAsync_a_price_cursor_does_not_decode_against_a_symbol_sort()
    {
        var ctx = new InstrumentTestContext();
        const int limit = 2;
        var rows = GivenInstruments(limit + 1);

        ctx.Instruments.GetBoardPagedAsync(
                "price_asc", Arg.Any<string?>(), Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
                limit, Arg.Any<CancellationToken>())
            .Returns(rows);
        ctx.Instruments.GetBoardPagedAsync(
                "symbol_asc", Arg.Any<string?>(), Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
                limit, Arg.Any<CancellationToken>())
            .Returns(new List<Instrument>());

        var priceCursor = (await ctx.Service.GetBoardAsync("price_asc", null, null, limit, CancellationToken.None)).NextCursor;
        Assert.NotNull(priceCursor);

        await ctx.Service.GetBoardAsync("symbol_asc", null, priceCursor, limit, CancellationToken.None);

        await ctx.Instruments.Received().GetBoardPagedAsync(
            "symbol_asc", Arg.Any<string?>(),
            Arg.Is<decimal?>(p => p == null), Arg.Is<string?>(s => s == null), Arg.Is<Guid?>(id => id == null),
            limit, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetBoardAsync_a_symbol_cursor_does_not_decode_against_a_price_sort()
    {
        var ctx = new InstrumentTestContext();
        const int limit = 2;
        var rows = GivenInstruments(limit + 1);

        ctx.Instruments.GetBoardPagedAsync(
                "symbol_asc", Arg.Any<string?>(), Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
                limit, Arg.Any<CancellationToken>())
            .Returns(rows);
        ctx.Instruments.GetBoardPagedAsync(
                "price_asc", Arg.Any<string?>(), Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
                limit, Arg.Any<CancellationToken>())
            .Returns(new List<Instrument>());

        var symbolCursor = (await ctx.Service.GetBoardAsync("symbol_asc", null, null, limit, CancellationToken.None)).NextCursor;
        Assert.NotNull(symbolCursor);

        await ctx.Service.GetBoardAsync("price_asc", null, symbolCursor, limit, CancellationToken.None);

        await ctx.Instruments.Received().GetBoardPagedAsync(
            "price_asc", Arg.Any<string?>(),
            Arg.Is<decimal?>(p => p == null), Arg.Is<string?>(s => s == null), Arg.Is<Guid?>(id => id == null),
            limit, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetBoardAsync_trims_the_extra_row_and_returns_a_cursor_when_there_are_more_rows_than_the_limit()
    {
        var ctx = new InstrumentTestContext();
        const int limit = 3;

        ctx.Instruments.GetBoardPagedAsync(
                "symbol_asc", Arg.Any<string?>(), Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
                limit, Arg.Any<CancellationToken>())
            .Returns(GivenInstruments(limit + 1));

        var result = await ctx.Service.GetBoardAsync("symbol_asc", null, null, limit, CancellationToken.None);

        Assert.Equal(limit, result.Items.Count);
        Assert.NotNull(result.NextCursor);
    }

    [Fact]
    public async Task GetBoardAsync_returns_no_cursor_when_rows_exactly_fill_the_limit()
    {
        var ctx = new InstrumentTestContext();
        const int limit = 3;

        ctx.Instruments.GetBoardPagedAsync(
                "symbol_asc", Arg.Any<string?>(), Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
                limit, Arg.Any<CancellationToken>())
            .Returns(GivenInstruments(limit));

        var result = await ctx.Service.GetBoardAsync("symbol_asc", null, null, limit, CancellationToken.None);

        Assert.Equal(limit, result.Items.Count);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task GetBoardAsync_returns_empty_items_and_no_cursor_when_the_repo_has_nothing()
    {
        var ctx = new InstrumentTestContext();

        var result = await ctx.Service.GetBoardAsync("symbol_asc", null, null, null, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Null(result.NextCursor);
    }

    [Theory]
    [InlineData("aapl")]
    [InlineData(null)]
    public async Task GetBoardAsync_passes_q_through_to_the_repo_unchanged(string? q)
    {
        var ctx = new InstrumentTestContext();

        await ctx.Service.GetBoardAsync("symbol_asc", q, null, null, CancellationToken.None);

        await ctx.Instruments.Received().GetBoardPagedAsync(
            "symbol_asc", Arg.Is<string?>(x => x == q), Arg.Any<decimal?>(), Arg.Any<string?>(), Arg.Any<Guid?>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
