using System.ComponentModel.DataAnnotations;
namespace AdminPanel.API.DTOs;

public sealed class PublicCatalogQuery
{
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 48)] public int PageSize { get; set; } = 12;
    [StringLength(100)] public string? Search { get; set; }
    [Range(1, int.MaxValue)] public int? CategoryId { get; set; }
    [RegularExpression("^(newest|name)$")] public string Sort { get; set; } = "newest";
}
public sealed record PublicProduct(int Id, string Name, string Description, decimal Price,
    string Currency, string Unit, int CategoryId, string CategoryName, bool HasImage);
public sealed record PublicCategory(int Id, string Name, string Description, int ProductCount);
public sealed record PublicPage<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
