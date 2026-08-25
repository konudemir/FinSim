using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FinSim.Application.Services
{
    /// <summary>
    /// Imports the shape of real-world price moves. Applies the ratio between
    /// consecutive real prices to our own price; levels drift apart on purpose.
    /// Mutates the instruments in place and does not save.
    /// </summary>
    public class ExternalPriceEngine
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(15);
        private const int MaxPerTick = 2;
        private const decimal MaxRatio = 1.15m;

        private readonly IExternalPriceSource _source;
        private readonly ILogger<ExternalPriceEngine> _log;

        public ExternalPriceEngine(IExternalPriceSource source, ILogger<ExternalPriceEngine> log)
        {
            _source = source;
            _log = log;
        }

        public async Task ApplyAsync(List<Instrument> instruments, CancellationToken ct)
        {
            var now = DateTimeOffset.UtcNow;
            var cutoff = now - PollInterval;

            // Poll the stalest few rather than everything at once, so the request
            // rate stays flat instead of bursting every 15 minutes.
            var due = instruments
                .Where(i => !string.IsNullOrWhiteSpace(i.RealSymbol) && i.IsActive)
                .Where(i => i.LastRealPriceAt is null || i.LastRealPriceAt < cutoff)
                .OrderBy(i => i.LastRealPriceAt ?? DateTimeOffset.MinValue)
                .Take(MaxPerTick)
                .ToList();

            foreach (var inst in due)
            {
                var real = await _source.TryGetPriceAsync(inst.RealSymbol!, ct);

                // Leave the anchor untouched on failure so no move is lost;
                // the next tick retries against the same reference point.
                if (real is null) continue;

                // First poll ever, or the anchor was just reset: seed only.
                if (inst.LastRealPrice is not > 0)
                {
                    inst.LastRealPrice = real;
                    inst.LastRealPriceAt = now;
                    continue;
                }

                var ratio = real.Value / inst.LastRealPrice.Value;

                // Splits, wrong-symbol data, garbage responses. Re-anchor and skip
                // the move rather than applying it.
                if (ratio > MaxRatio || ratio < 1m / MaxRatio)
                {
                    _log.LogWarning(
                        "{Symbol}: implausible ratio {Ratio:F4} ({Old} -> {New}); resetting anchor",
                        inst.RealSymbol, ratio, inst.LastRealPrice, real);
                    inst.LastRealPrice = real;
                    inst.LastRealPriceAt = now;
                    continue;
                }

                // Market closed or a frozen quote.
                if (ratio == 1m)
                {
                    inst.LastRealPriceAt = now;
                    continue;
                }

                var next = Math.Round(inst.CurrentPrice * ratio, 2, MidpointRounding.AwayFromZero);
                if (next < 0.01m) next = 0.01m;

                if (next != inst.CurrentPrice)
                {
                    _log.LogInformation("{Symbol}: external {Ratio:F4}, {Old} -> {New}",
                        inst.RealSymbol, ratio, inst.CurrentPrice, next);
                    inst.CurrentPrice = next;
                }

                inst.LastRealPrice = real;
                inst.LastRealPriceAt = now;
            }
        }
    }
}