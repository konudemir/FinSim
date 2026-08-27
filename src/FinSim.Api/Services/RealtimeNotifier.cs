using FinSim.Api.Hubs;
using FinSim.Application.Interfaces;
using FinSim.Application.Services;
using Microsoft.AspNetCore.SignalR;

namespace FinSim.Api.Services
{
    /// <summary>
    /// Lives in Api, not Infrastructure, because IHubContext&lt;PriceHub&gt; needs the
    /// concrete Hub type — and PriceHub belongs to Api, which Infrastructure can't
    /// reference without creating a cycle (Api already references Infrastructure).
    /// </summary>
    public class RealtimeNotifier : IRealtimeNotifier
    {
        private readonly IHubContext<PriceHub> _hub;
        private readonly UserService _users;
        private readonly OrderService _orders;

        public RealtimeNotifier(IHubContext<PriceHub> hub, UserService users, OrderService orders)
        {
            _hub = hub;
            _users = users;
            _orders = orders;
        }

        public async Task NotifyOrderUpdateAsync(Guid userId, CancellationToken ct)
        {
            var orders = (await _orders.GetRecentAsync(userId, null, null, null, ct)).Items;
            var balance = await _users.GetBalanceAsync(userId, ct);
            var portfolio = await _users.GetPortfolioAsync(userId, ct);

            await _hub.Clients.User(userId.ToString()).SendAsync("OrderUpdate", new
            {
                orders,
                balance,
                portfolio
            }, ct);
        }
    }
}
