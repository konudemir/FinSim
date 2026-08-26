using FinSim.Application.Interfaces;
using FinSim.Domain.Models;

namespace FinSim.Application.Services;

public class FavoriteService
{
    private readonly IFavoriteRepository _favorites;
    private readonly IInstrumentRepository _instruments;
    private readonly IUnitOfWork _unitOfWork;

    public FavoriteService(
        IFavoriteRepository favorites, IInstrumentRepository instruments, IUnitOfWork unitOfWork)
    {
        _favorites = favorites;
        _instruments = instruments;
        _unitOfWork = unitOfWork;
    }

    public Task<List<Guid>> GetAsync(Guid userId, CancellationToken ct) =>
        _favorites.GetInstrumentIdsAsync(userId, ct);

    public async Task<FavoriteResult> AddAsync(Guid userId, Guid instrumentId, CancellationToken ct)
    {
        var instrument = await _instruments.GetByIdAsync(instrumentId, ct);
        if (instrument is null) return FavoriteResult.InstrumentNotFound;

        var existing = await _favorites.FindAsync(userId, instrumentId, ct);
        if (existing is not null) return FavoriteResult.Success;

        _favorites.Add(new FavoriteInstrument
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            InstrumentId = instrumentId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await _unitOfWork.TrySaveChangesAsync(ct);
        return FavoriteResult.Success;
    }

    public async Task<FavoriteResult> RemoveAsync(Guid userId, Guid instrumentId, CancellationToken ct)
    {
        var existing = await _favorites.FindAsync(userId, instrumentId, ct);
        if (existing is null) return FavoriteResult.Success;

        _favorites.Remove(existing);
        await _unitOfWork.TrySaveChangesAsync(ct);
        return FavoriteResult.Success;
    }
}

public enum FavoriteResult
{
    Success,
    InstrumentNotFound,
}
