using FinSim.Application.Interfaces;
using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;

namespace FinSim.Application.Services;

/// <summary>Which of the four position moves a fill represents, given the side already held.</summary>
public enum FillKind
{
    OpenOrAddLong,
    ReduceOrCloseLong,
    OpenOrAddShort,
    CoverShort,
}

public readonly record struct FillResult(FillKind Kind, decimal? Realized);

internal static class PortfolioFillExecutor
{
    public static FillKind Classify(int currentQuantity, OrderDirection direction) =>
        direction == OrderDirection.Buy
            ? currentQuantity < 0 ? FillKind.CoverShort : FillKind.OpenOrAddLong
            : currentQuantity > 0 ? FillKind.ReduceOrCloseLong : FillKind.OpenOrAddShort;

    public static FillResult Apply(
        IPortfolioRepository portfolio,
        User user,
        PortfolioItem? portItem,
        Guid userId,
        Guid instrumentId,
        OrderDirection direction,
        int quantity,
        decimal price)
    {
        var kind = Classify(portItem?.TotalQuantity ?? 0, direction);

        switch (kind)
        {
            case FillKind.OpenOrAddLong:
                if (portItem is null)
                    portfolio.Add(PortfolioItem.Open(userId, instrumentId, quantity, price));
                else
                    portItem.ApplyBuy(quantity, price);
                return new FillResult(kind, null);

            case FillKind.OpenOrAddShort:
                if (portItem is null)
                    portfolio.Add(PortfolioItem.Open(userId, instrumentId, -quantity, price));
                else
                    portItem.ApplyShortOpen(quantity, price);
                return new FillResult(kind, null);

            case FillKind.ReduceOrCloseLong:
            {
                var realized = portItem!.ApplySell(quantity, price);
                user.RealizedProfitLoss += realized;
                if (portItem.TotalQuantity == 0) portfolio.Remove(portItem);
                return new FillResult(kind, realized);
            }

            default: // CoverShort
            {
                var realized = portItem!.ApplyShortCover(quantity, price);
                user.RealizedProfitLoss += realized;
                if (portItem.TotalQuantity == 0) portfolio.Remove(portItem);
                return new FillResult(kind, realized);
            }
        }
    }
}
