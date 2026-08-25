using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
using FinSim.Domain.Services;

namespace FinSim.Application.Services
{
    public class PriceSimEngine
    {
        private readonly IInstrumentRepository _instruments;

        public PriceSimEngine(IInstrumentRepository instruments) => _instruments = instruments;

                /// <summary>
        /// Moves every active stock one tick, then reprices funds off the new
        /// stock prices. Does not save.
        /// </summary>
        public async Task<PriceTickResult> TickAsync(CancellationToken ct)
        {
            var stocks = await _instruments.GetActiveStocksAsync(ct);
            var funds  = await _instruments.GetActiveFundsAsync(ct);

            // Index tracks the market itself, so funds are excluded — including
            // them would count their constituents a second time.
            var index = stocks.Count == 0
                ? 10_000m
                : Math.Round(stocks.Sum(i => i.CurrentPrice / i.BasePrice) / stocks.Count * 10_000m, 2);

            var marketMove = index / 10_000m - 1m;

            var prices = stocks.ToDictionary(i => i.Id, i => i.CurrentPrice);

            foreach (var f in funds)
                f.CurrentPrice = FundPricer.Price(
                    FundPricer.Nav(f.Holdings, prices), f.Divisor ?? 1m);

            return new PriceTickResult(marketMove, index, [.. stocks, .. funds]);
        }
    }

    public record PriceTickResult(decimal MarketMove, decimal IndexValue, List<Instrument> Instruments);
}