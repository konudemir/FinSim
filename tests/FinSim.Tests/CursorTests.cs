using System.Globalization;
using FinSim.Application.Pagination;

namespace FinSim.Tests;

public class CursorTests
{
    private const string Sort = "orders_created_desc";
    private const string OtherSort = "tx_date_desc";

    private const string PriceSort = "price_asc";
    private const string OtherPriceSort = "price_desc";
    private const string SymbolSort = "symbol_asc";
    private const string OtherSymbolSort = "symbol_desc";

    [Fact]
    public void Encode_then_decode_returns_the_original_values()
    {
        var ts = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        var cursor = Cursor.Encode(Sort, ts, id);

        Assert.True(Cursor.TryDecode(cursor, Sort, out var gotTs, out var gotId));
        Assert.Equal(ts.UtcTicks, gotTs.UtcTicks);
        Assert.Equal(id, gotId);
    }

    [Fact]
    public void Decode_preserves_sub_second_precision()
    {
        // Two orders placed within the same second must produce different cursors.
        // If precision is lost here, keyset paging skips or repeats rows.
        var a = new DateTimeOffset(2026, 8, 27, 10, 0, 0, TimeSpan.Zero).AddTicks(1);
        var b = a.AddTicks(1);
        var id = Guid.NewGuid();

        Cursor.TryDecode(Cursor.Encode(Sort, a, id), Sort, out var gotA, out _);
        Cursor.TryDecode(Cursor.Encode(Sort, b, id), Sort, out var gotB, out _);

        Assert.NotEqual(gotA.UtcTicks, gotB.UtcTicks);
        Assert.Equal(a.UtcTicks, gotA.UtcTicks);
    }

    [Fact]
    public void Decode_normalises_a_non_utc_offset_to_utc()
    {
        // Istanbul is +03:00. The same instant must compare equal regardless
        // of the offset it arrived with.
        var local = new DateTimeOffset(2026, 8, 27, 13, 0, 0, TimeSpan.FromHours(3));
        var utc = local.ToUniversalTime();

        Cursor.TryDecode(Cursor.Encode(Sort, local, Guid.NewGuid()), Sort, out var got, out _);

        Assert.Equal(utc.UtcTicks, got.UtcTicks);
        Assert.Equal(TimeSpan.Zero, got.Offset);
    }

    [Fact]
    public void Decode_rejects_a_cursor_from_a_different_sort()
    {
        var cursor = Cursor.Encode(Sort, DateTimeOffset.UtcNow, Guid.NewGuid());

        Assert.False(Cursor.TryDecode(cursor, OtherSort, out _, out _));
    }

    [Theory]
    [InlineData("garbage!!")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("bm90LWEtY3Vyc29y")]           // valid base64, wrong shape ("not-a-cursor")
    [InlineData("b3JkZXJzX2NyZWF0ZWRfZGVzY3x8")] // right sort, missing parts
    public void Decode_returns_false_instead_of_throwing_on_bad_input(string? cursor)
    {
        Assert.False(Cursor.TryDecode(cursor, Sort, out _, out _));
    }

    [Fact]
    public void Decode_rejects_a_non_numeric_tick_value()
    {
        var forged = System.Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{Sort}|not-a-number|{Guid.NewGuid()}"));

        Assert.False(Cursor.TryDecode(forged, Sort, out _, out _));
    }

    [Fact]
    public void Encoded_cursor_is_url_safe()
    {
        // The cursor travels in a query string. '+' would silently become a
        // space, and '/' and '=' need escaping.
        for (var i = 0; i < 200; i++)
        {
            var cursor = Cursor.Encode(Sort, DateTimeOffset.UtcNow.AddTicks(i), Guid.NewGuid());

            Assert.DoesNotContain('+', cursor);
            Assert.DoesNotContain('/', cursor);
            Assert.DoesNotContain('=', cursor);
            Assert.Equal(cursor, Uri.EscapeDataString(cursor));
        }
    }

    [Theory]
    [InlineData(null, Cursor.DefaultLimit)]
    [InlineData(0, Cursor.DefaultLimit)]
    [InlineData(-5, Cursor.DefaultLimit)]
    [InlineData(1, 1)]
    [InlineData(25, 25)]
    [InlineData(Cursor.MaxLimit, Cursor.MaxLimit)]
    [InlineData(Cursor.MaxLimit + 1, Cursor.MaxLimit)]
    [InlineData(int.MaxValue, Cursor.MaxLimit)]
    public void ClampLimit_keeps_the_limit_in_range(int? limit, int expected)
    {
        Assert.Equal(expected, Cursor.ClampLimit(limit));
    }

    // ── decimal encoder (board price sorts) ─────────────────────

    [Fact]
    public void EncodeDecimal_then_decode_returns_the_original_values()
    {
        var key = 123.45m;
        var id = Guid.NewGuid();

        var cursor = Cursor.EncodeDecimal(PriceSort, key, id);

        Assert.True(Cursor.TryDecodeDecimal(cursor, PriceSort, out var gotKey, out var gotId));
        Assert.Equal(key, gotKey);
        Assert.Equal(id, gotId);
    }

    [Fact]
    public void EncodeDecimal_is_culture_invariant()
    {
        // The app runs under tr-TR, where ',' is the decimal separator. If Pack
        // ever used the ambient culture instead of InvariantCulture, 123.45
        // would encode as "123,45" and TryDecodeDecimal would fail to parse it
        // back (or parse it as a completely different number).
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            var key = 123.45m;
            var id = Guid.NewGuid();
            var cursor = Cursor.EncodeDecimal(PriceSort, key, id);

            Assert.True(Cursor.TryDecodeDecimal(cursor, PriceSort, out var gotKey, out var gotId));
            Assert.Equal(key, gotKey);
            Assert.Equal(id, gotId);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void EncodeDecimal_preserves_scale_so_100_00_and_100_do_not_collide()
    {
        // decimal's scale is part of its representation (100.00m != 100m in
        // ToString terms, even though they compare equal numerically). The
        // encoder must round-trip that scale rather than normalising it away,
        // or two rows priced "100" and "100.00" could be conflated in the
        // ordering the keyset comparison relies on.
        var id = Guid.NewGuid();

        Assert.True(Cursor.TryDecodeDecimal(Cursor.EncodeDecimal(PriceSort, 100.00m, id), PriceSort, out var gotA, out _));
        Assert.True(Cursor.TryDecodeDecimal(Cursor.EncodeDecimal(PriceSort, 100m, id), PriceSort, out var gotB, out _));

        Assert.Equal("100.00", gotA.ToString(CultureInfo.InvariantCulture));
        Assert.Equal("100", gotB.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void DecodeDecimal_rejects_a_cursor_from_a_different_sort()
    {
        var cursor = Cursor.EncodeDecimal(PriceSort, 1.23m, Guid.NewGuid());

        Assert.False(Cursor.TryDecodeDecimal(cursor, OtherPriceSort, out _, out _));
    }

    [Theory]
    [InlineData("garbage!!")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("bm90LWEtY3Vyc29y")]     // valid base64, wrong shape ("not-a-cursor")
    [InlineData("cHJpY2VfYXNjfHw")]      // right sort, missing parts ("price_asc||")
    public void DecodeDecimal_returns_false_instead_of_throwing_on_bad_input(string? cursor)
    {
        Assert.False(Cursor.TryDecodeDecimal(cursor, PriceSort, out _, out _));
    }

    // ── string encoder (board symbol sorts) ─────────────────────

    [Fact]
    public void EncodeString_then_decode_returns_the_original_values()
    {
        var key = "AAPL";
        var id = Guid.NewGuid();

        var cursor = Cursor.EncodeString(SymbolSort, key, id);

        Assert.True(Cursor.TryDecodeString(cursor, SymbolSort, out var gotKey, out var gotId));
        Assert.Equal(key, gotKey);
        Assert.Equal(id, gotId);
    }

    [Fact]
    public void DecodeString_rejects_a_cursor_from_a_different_sort()
    {
        var cursor = Cursor.EncodeString(SymbolSort, "AAPL", Guid.NewGuid());

        Assert.False(Cursor.TryDecodeString(cursor, OtherSymbolSort, out _, out _));
    }

    [Theory]
    [InlineData("garbage!!")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("bm90LWEtY3Vyc29y")]     // valid base64, wrong shape ("not-a-cursor")
    [InlineData("c3ltYm9sX2FzY3x8")]     // right sort, missing parts ("symbol_asc||")
    public void DecodeString_returns_false_instead_of_throwing_on_bad_input(string? cursor)
    {
        Assert.False(Cursor.TryDecodeString(cursor, SymbolSort, out _, out _));
    }

    [Fact]
    public void DecodeString_accepts_a_cursor_minted_by_EncodeDecimal_under_the_same_sort()
    {
        // This is not a gap: TryDecodeString only ever splits the packed
        // string on '|' and hands the middle segment back as-is, so any
        // decimal's InvariantCulture string form parses fine as a "symbol".
        // What actually keeps price cursors out of symbol pagination (and vice
        // versa) is that the four board sorts are mutually exclusive by name
        // ("price_asc" is never passed to a symbol decode, and vice versa) —
        // not that the encoders reject each other's payloads by shape.
        var cursor = Cursor.EncodeDecimal(PriceSort, 123.45m, Guid.NewGuid());

        Assert.True(Cursor.TryDecodeString(cursor, PriceSort, out var key, out _));
        Assert.Equal("123.45", key);
    }
}
