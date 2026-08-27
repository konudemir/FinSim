using System.Text;

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
}