using System.Security.Claims;
using FinSim.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinSim.Controllers
{
    [ApiController]
    [Route("api/favorites")]
    [Authorize]
    public class FavoritesController : ControllerBase
    {
        private readonly FavoriteService _favorites;

        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public FavoritesController(FavoriteService favorites) => _favorites = favorites;

        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken ct) =>
            Ok(await _favorites.GetAsync(CurrentUserId, ct));

        [HttpPost("{instrumentId:guid}")]
        public async Task<IActionResult> Add(Guid instrumentId, CancellationToken ct)
        {
            var result = await _favorites.AddAsync(CurrentUserId, instrumentId, ct);
            return result == FavoriteResult.InstrumentNotFound
                ? NotFound("InstrumentNotFound")
                : Ok();
        }

        [HttpDelete("{instrumentId:guid}")]
        public async Task<IActionResult> Remove(Guid instrumentId, CancellationToken ct)
        {
            await _favorites.RemoveAsync(CurrentUserId, instrumentId, ct);
            return Ok();
        }
    }
}
