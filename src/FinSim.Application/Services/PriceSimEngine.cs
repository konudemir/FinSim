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
        /// Reads current stock prices (set by fills, not here), reprices funds off
        /// them, and reports the index plus its move since the previous tick.
        /// Does not save.
        /// </summary>
        /// <param name="previousIndex">
        /// Index value from the last tick, or null on the first tick — in which
        /// case MarketMove is reported as zero.
        /// </param>
        public async Task<PriceTickResult> TickAsync(decimal? previousIndex, CancellationToken ct)
        {
            var stocks = await _instruments.GetActiveStocksAsync(ct);
            var funds  = await _instruments.GetActiveFundsAsync(ct);

            // CurrentPrice is owned by the fill handler now. This only guards
            // against a non-positive price left behind by bad data.
            foreach (var i in stocks)
                if (i.CurrentPrice <= 0) i.CurrentPrice = 0.01m;

            // Index tracks the market itself, so funds are excluded — including
            // them would count their constituents a second time.
            var index = stocks.Count == 0
                ? 10_000m
                : Math.Round(stocks.Sum(i => i.CurrentPrice / i.BasePrice) / stocks.Count * 10_000m, 2);

            // Market move is now an observation, not an input: the fractional
            // change in the index caused by whatever traded this tick.
            var marketMove = previousIndex is > 0
                ? Math.Round((index - previousIndex.Value) / previousIndex.Value, 6)
                : 0m;

            var prices = stocks.ToDictionary(i => i.Id, i => i.CurrentPrice);

            foreach (var f in funds)
                f.CurrentPrice = FundPricer.Price(
                    FundPricer.Nav(f.Holdings, prices), f.Divisor ?? 1m);

            return new PriceTickResult(marketMove, index, [.. stocks, .. funds]);
        }
    }

    public record PriceTickResult(decimal MarketMove, decimal IndexValue, List<Instrument> Instruments);
}