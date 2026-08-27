namespace FinSim.Application.Pagination;

public record PagedResult<T>(List<T> Items, string? NextCursor);