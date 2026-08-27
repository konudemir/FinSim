using System.Text;
using System.Globalization;

namespace FinSim.Application.Pagination;

public static class Cursor
{
    public const int DefaultLimit = 25;
    public const int MaxLimit = 100;

    public static int ClampLimit(int? limit) =>
        limit is null or < 1 ? DefaultLimit : Math.Min(limit.Value, MaxLimit);

    public static string Encode(string sort, DateTimeOffset ts, Guid id)
    {
        var raw = $"{sort}|{ts.UtcTicks}|{id}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(
        string? cursor, string expectedSort,
        out DateTimeOffset ts, out Guid id)
    {
        ts = default;
        id = default;
        if (string.IsNullOrEmpty(cursor)) return false;

        try
        {
            var b64 = cursor.Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(b64)).Split('|');

            if (parts.Length != 3) return false;
            if (parts[0] != expectedSort) return false;
            if (!long.TryParse(parts[1], out var ticks)) return false;
            if (!Guid.TryParse(parts[2], out id)) return false;

            ts = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Pack(string sort, string key, Guid id)
    {
        var raw = $"{sort}|{key}|{id}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool Unpack(string? cursor, string expectedSort, out string key, out Guid id)
    {
        key = "";
        id = default;
        if (string.IsNullOrEmpty(cursor)) return false;

        try
        {
            var b64 = cursor.Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(b64)).Split('|');

            if (parts.Length != 3) return false;
            if (parts[0] != expectedSort) return false;
            if (!Guid.TryParse(parts[2], out id)) return false;

            key = parts[1];
            return true;
        }
        catch
        {
            return false;
        }
    }

    // --- decimal (price) ---

    public static string EncodeDecimal(string sort, decimal key, Guid id) =>
        Pack(sort, key.ToString(CultureInfo.InvariantCulture), id);

    public static bool TryDecodeDecimal(string? cursor, string expectedSort, out decimal key, out Guid id)
    {
        key = default;
        return Unpack(cursor, expectedSort, out var raw, out id)
            && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out key);
    }

    // --- string (symbol) ---

    public static string EncodeString(string sort, string key, Guid id) =>
        Pack(sort, key, id);

    public static bool TryDecodeString(string? cursor, string expectedSort, out string key, out Guid id) =>
        Unpack(cursor, expectedSort, out key, out id);
}