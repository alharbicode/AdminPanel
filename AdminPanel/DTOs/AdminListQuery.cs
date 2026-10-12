using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace AdminPanel.API.DTOs;

public sealed class AdminListQuery
{
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 10;
    [StringLength(200)] public string? Search { get; set; }
    [StringLength(200)] public string? Area { get; set; }
    [Range(1, int.MaxValue)] public int? ProductId { get; set; }
    [RegularExpression("^(upcoming|current|ended)$")] public string? Period { get; set; }
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public static async Task<PagedResult<T>> CreateAsync(IQueryable<T> orderedQuery, AdminListQuery query, CancellationToken cancellationToken)
    {
        var total = await orderedQuery.CountAsync(cancellationToken);
        var lastPage = Math.Max(1, (int)Math.Ceiling(total / (double)query.PageSize));
        var page = Math.Min(query.Page, lastPage);
        var items = await orderedQuery.Skip((page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken);
        return new(items, total, page, query.PageSize);
    }
}
