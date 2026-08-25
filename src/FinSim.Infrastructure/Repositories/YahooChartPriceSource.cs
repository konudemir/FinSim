using System.Text.Json;
using FinSim.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinSim.Infrastructure.Services;

public sealed class YahooChartPriceSource : IExternalPriceSource
{
    private const string ExpectedCurrency = "TRY";

    private readonly HttpClient _http;
    private readonly ILogger<YahooChartPriceSource> _log;

    public YahooChartPriceSource(HttpClient http, ILogger<YahooChartPriceSource> log)
    {
        _http = http;
        _log = log;
    }

    public async Task<decimal?> TryGetPriceAsync(string symbol, CancellationToken ct)
    {
        try
        {
            var url = $"v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval=1d&range=1d";

            using var res = await _http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode)
            {
                _log.LogWarning("Yahoo {Symbol} -> HTTP {Status}", symbol, (int)res.StatusCode);
                return null;
            }

            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("chart", out var chart))
            {
                _log.LogWarning("Yahoo {Symbol}: no 'chart' field", symbol);
                return null;
            }

            // A populated error can arrive alongside HTTP 200 — status code alone isn't enough.
            if (chart.TryGetProperty("error", out var err) && err.ValueKind != JsonValueKind.Null)
            {
                _log.LogWarning("Yahoo {Symbol} error: {Err}", symbol, err.ToString());
                return null;
            }

            if (!chart.TryGetProperty("result", out var result)
                || result.ValueKind != JsonValueKind.Array
                || result.GetArrayLength() == 0)
            {
                _log.LogWarning("Yahoo {Symbol}: empty result", symbol);
                return null;
            }

            if (!result[0].TryGetProperty("meta", out var meta))
            {
                _log.LogWarning("Yahoo {Symbol}: no meta", symbol);
                return null;
            }

            // Catches a mistyped ticker resolving to a listing on another exchange
            // (e.g. dropping ".IS" and landing on a US stock). The ratio bound can't
            // catch this — a wrong stock still moves plausibly on its own.
            if (meta.TryGetProperty("currency", out var cur)
                && !string.Equals(cur.GetString(), ExpectedCurrency, StringComparison.OrdinalIgnoreCase))
            {
                _log.LogWarning("Yahoo {Symbol}: unexpected currency {Cur}, skipping",
                    symbol, cur.GetString());
                return null;
            }

            // Comes back as a JSON number and may have no decimal point (e.g. 301).
            if (!meta.TryGetProperty("regularMarketPrice", out var priceEl)
                || priceEl.ValueKind != JsonValueKind.Number)
            {
                _log.LogWarning("Yahoo {Symbol}: regularMarketPrice missing or not a number", symbol);
                return null;
            }

            var price = priceEl.GetDecimal();
            if (price <= 0)
            {
                _log.LogWarning("Yahoo {Symbol}: invalid price {Price}", symbol, price);
                return null;
            }

            _log.LogDebug("Yahoo {Symbol} -> {Price}", symbol, price);
            return price;
        }
        catch (Exception ex) when (ex is HttpRequestException
                                     or JsonException
                                     or TaskCanceledException
                                     or OperationCanceledException)
        {
            // Swallowed on purpose: a dead feed must not break the tick loop.
            _log.LogWarning(ex, "Yahoo fetch failed for {Symbol}", symbol);
            return null;
        }
    }
}