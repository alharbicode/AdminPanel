using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace AdminPanel.API.DTOs;

public sealed class AdminListQuery : IValidatableObject
{
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 10;
    [StringLength(200)] public string? Search { get; set; }
    [StringLength(200)] public string? Area { get; set; }
    [Range(1, int.MaxValue)] public int? ProductId { get; set; }
    [RegularExpression("^(upcoming|current|ended)$")] public string? Period { get; set; }
    [RegularExpression("^(id|name|name-desc|newest|oldest|stock|stock-desc)$")] public string? Sort { get; set; }
    [Range(1, int.MaxValue)] public int? CategoryId { get; set; }
    public bool? Available { get; set; }
    public bool? HasProducts { get; set; }
    [RegularExpression("^(in-stock|out-of-stock)$")] public string? Stock { get; set; }
    [RegularExpression("^(Pending|Confirmed|Shipped|Delivered)$")] public string? Status { get; set; }
    [DataType(DataType.Date)] public DateTime? From { get; set; }
    [DataType(DataType.Date)] public DateTime? To { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (From.HasValue && To.HasValue && From.Value.Date > To.Value.Date)
            yield return new ValidationResult("End date must be on or after start date.", new[] { nameof(To) });
    }
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
