using FinSim.Domain.Models;
using FinSim.Domain.Models.Enums;

namespace FinSim.Application.Services;

/// <summary>
/// Undoes whatever a pending order reserved at placement. Cancellation and
/// expiry differ only in the status they write afterwards, so the release
/// itself lives here — two copies of this is how a margin leak happens.
/// </summary>
public static class OrderReleaseExecutor
{
    /// <summary>
    /// Returns false when the order reserved shares but the position has gone
    /// missing, which should be impossible and means something else corrupted it.
    /// The caller decides whether that's NoPosition or a log line.
    /// </summary>
    public static bool Release(User user, Order order, PortfolioItem? portItem)
    {
        if (order.Direction == OrderDirection.Buy && order.LockedAmount > 0)
        {
            // Release exactly what was held. Recomputing from Price * Quantity is
            // what produced the leftover kuruş: the multiply was rounded at a
            // different point than it was when the funds were locked.
            user.LockedCashBalance -= order.LockedAmount;
            user.FreeCashBalance   += order.LockedAmount;
            return true;
        }

        if (order.Direction == OrderDirection.Sell && order.LockedAmount > 0)
        {
            // short sell that reserved margin, not shares
            user.LockedCashBalance -= order.LockedAmount;
            user.FreeCashBalance   += order.LockedAmount;
            user.MarginUsed        -= order.LockedAmount;
            return true;
        }

        // Everything else reserved a share quantity: a cover buy against the
        // short, or an ordinary sell against a long.
        if (portItem is null) return false;

        portItem.LockedQuantity -= order.Quantity;
        return true;
    }
}