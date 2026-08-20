using System.ComponentModel;
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
        public async Task<PriceTickResult> TickAsync(double bias, CancellationToken ct)
        {
            var stocks = await _instruments.GetActiveStocksAsync(ct);
            var funds  = await _instruments.GetActiveFundsAsync(ct);

            var marketMove = (decimal)(Random.Shared.NextDouble() * 2 - bias) * 0.02m;

            foreach (var i in stocks)
                i.CurrentPrice = NextValue(i.CurrentPrice, i.BasePrice, marketMove);

            // Index tracks the market itself, so funds are excluded — including
            // them would count their constituents a second time.
            var index = stocks.Count == 0
                ? 10_000m
                : Math.Round(stocks.Sum(i => i.CurrentPrice / i.BasePrice) / stocks.Count * 10_000m, 2);

            var prices = stocks.ToDictionary(i => i.Id, i => i.CurrentPrice);

            foreach (var f in funds)
                f.CurrentPrice = FundPricer.Price(
                    FundPricer.Nav(f.Holdings, prices), f.Divisor ?? 1m);

            return new PriceTickResult(marketMove, index, [.. stocks, .. funds]);
        }

        private static decimal NextValue(decimal currVal, decimal baseVal, decimal marketMove)
        {
            if (currVal <= 0) return 0.01m;

            // hisseye özgü rastgele hareket, ortalaması sıfır
            var idio = (decimal)(Random.Shared.NextDouble() * 2 - 1.01) * 0.02m;

            const decimal drift = 0.00002m;

            var pull = (baseVal - currVal) / baseVal * 0.002m;

            return Math.Round(
                currVal * (1 + marketMove + idio + drift + pull),
                2, MidpointRounding.AwayFromZero);
        }
    }

    public record PriceTickResult(decimal MarketMove, decimal IndexValue, List<Instrument> Instruments);
}