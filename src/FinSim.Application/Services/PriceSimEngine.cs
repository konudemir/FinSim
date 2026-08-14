using System.ComponentModel;
using FinSim.Application.Interfaces;
using FinSim.Domain.Models;

namespace FinSim.Application.Services
{
    public class PriceSimEngine
    {
        private readonly IInstrumentRepository _instruments;

        public PriceSimEngine(IInstrumentRepository instruments) => _instruments = instruments;

        /// <summary>Moves every active instrument's price one tick. Does not save.</summary>
        public async Task<PriceTickResult> TickAsync(CancellationToken ct)
        {
            var instruments = await _instruments.GetActiveAsync(ct);

            var marketMove = (decimal)(Random.Shared.NextDouble() * 2 - 1) * 0.02m;

            foreach (var i in instruments)
                i.CurrentPrice = NextValue(i.CurrentPrice, i.BasePrice, marketMove);

            var index = Math.Round(
            instruments.Sum(i => i.CurrentPrice / i.BasePrice) / instruments.Count * 10_000m, 2);

            return new PriceTickResult(marketMove, index, instruments);
        }

        private static decimal NextValue(decimal currVal, decimal baseVal, decimal marketMove)
        {
            if (currVal <= 0) return 0.01m;

            // hisseye özgü rastgele hareket, ortalaması sıfır
            var idio = (decimal)(Random.Shared.NextDouble() * 2 - 1) * 0.03m;

            const decimal drift = 0.0003m;

            var pull = (baseVal - currVal) / baseVal * 0.0002m;

            return Math.Round(
                currVal * (1 + marketMove + idio + drift + pull),
                2, MidpointRounding.AwayFromZero);
        }
    }

    public record PriceTickResult(decimal MarketMove, decimal IndexValue, List<Instrument> Instruments);
}