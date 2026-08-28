namespace FinSim.Application.Pagination;

/// <summary>
/// What a repository returns for an offset-paged query: the rows for the
/// requested page plus the total row count for the same filter (ignoring
/// Skip/Take). Services turn this into a PagedResult&lt;TDto&gt;.
/// </summary>
public record PagedRows<T>(List<T> Items, int Total);