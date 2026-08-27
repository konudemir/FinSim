using FinSim.Application.Pagination;

namespace FinSim.Application.Tests;

public class CursorTests
{
    private const string Sort = "orders_created_desc";
    private const string OtherSort = "tx_date_desc";

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
}