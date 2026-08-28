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
        private readonly InstrumentService _instruments;

        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public FavoritesController(FavoriteService favorites, InstrumentService instruments)
        {
            _favorites = favorites;
            _instruments = instruments;
        }

        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken ct) =>
            Ok(await _favorites.GetAsync(CurrentUserId, ct));

        [HttpGet("board")]
        public async Task<IActionResult> GetBoard(
            [FromQuery] string? sort,
            [FromQuery] int? page,
            [FromQuery] int? limit,
            CancellationToken ct) =>
            Ok(await _instruments.GetFavoritesBoardAsync(CurrentUserId, sort, page, limit, ct));

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
