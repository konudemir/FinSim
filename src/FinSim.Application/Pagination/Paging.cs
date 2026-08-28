namespace FinSim.Application.Pagination;

public static class Paging
{
    public const int DefaultLimit = 25;
    public const int MaxLimit = 100;

    public static int ClampLimit(int? limit) =>
        limit is null or < 1 ? DefaultLimit : Math.Min(limit.Value, MaxLimit);

    public static int ClampPage(int? page) =>
        page is null or < 1 ? 1 : page.Value;
}