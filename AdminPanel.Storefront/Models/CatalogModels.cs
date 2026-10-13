using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
namespace AdminPanel.Storefront.Models;

public sealed class CatalogQuery
{
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 48)] public int PageSize { get; set; } = 12;
    [StringLength(100)] public string? Search { get; set; }
    [Range(1, int.MaxValue)] public int? CategoryId { get; set; }
    [RegularExpression("^(newest|name)$")] public string Sort { get; set; } = "newest";
    public string Url(string path, int? page = null) => QueryHelpers.AddQueryString(path, new Dictionary<string, string?>
    {
        ["Page"] = (page ?? Page).ToString(CultureInfo.InvariantCulture), ["PageSize"] = PageSize.ToString(CultureInfo.InvariantCulture),
        ["Search"] = Search, ["CategoryId"] = CategoryId?.ToString(CultureInfo.InvariantCulture), ["Sort"] = Sort
    });
}
public sealed record Product(int Id, string Name, string Description, decimal Price, string Currency,
    string Unit, int CategoryId, string CategoryName, bool HasImage);
public sealed record Category(int Id, string Name, string Description, int ProductCount);
public sealed record CatalogPage<T>(List<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}
public sealed record HomeModel(List<Product> Products, List<Category> Categories);
public sealed record CatalogModel(CatalogPage<Product> Products, CatalogQuery Query, List<Category> Categories);
public sealed record CategoriesModel(CatalogPage<Category> Categories, CatalogQuery Query);
public sealed record DetailsModel(Product Product, List<Product> Related);
public sealed record PaginationModel(int Page, int TotalPages, CatalogQuery Query, string Path);
