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

            return new PriceTickResult(marketMove, instruments);
        }

        private static decimal NextValue(decimal currVal, decimal baseVal, decimal marketMove)
        {
            if (currVal <= 0) return 0.01m;

            var idio = (decimal)(Random.Shared.NextDouble() * 2 - 1) * 0.03m;  // hisseye özgü
            var pull = (baseVal - currVal) / baseVal * 0.02m;                  // ortalamaya dönüş

            return Math.Round(currVal * (1 + marketMove + idio + pull), 2, MidpointRounding.AwayFromZero);
        }
    }

    public record PriceTickResult(decimal MarketMove, List<Instrument> Instruments);
}