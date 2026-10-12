using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;

namespace AdminPanel.MVC.Models;

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

    public string Url(string path, int? page = null) => QueryHelpers.AddQueryString(path, new Dictionary<string, string?>
    {
        ["Page"] = (page ?? Page).ToString(CultureInfo.InvariantCulture),
        ["PageSize"] = PageSize.ToString(CultureInfo.InvariantCulture),
        ["Search"] = Search, ["Area"] = Area, ["ProductId"] = ProductId?.ToString(CultureInfo.InvariantCulture), ["Period"] = Period,
        ["Sort"] = Sort, ["CategoryId"] = CategoryId?.ToString(CultureInfo.InvariantCulture),
        ["Available"] = Available?.ToString(), ["HasProducts"] = HasProducts?.ToString(),
        ["Stock"] = Stock, ["Status"] = Status, ["From"] = From?.ToString("yyyy-MM-dd"), ["To"] = To?.ToString("yyyy-MM-dd")
    });
}

public class PagedList
{
    public int TotalCount { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public AdminListQuery Query { get; set; } = new();
}
public sealed class PagedList<T> : PagedList
{
    public List<T> Items { get; set; } = new();
}

public sealed class TwoDecimalPlacesAttribute : ValidationAttribute
{
    public TwoDecimalPlacesAttribute() : base("Use no more than two decimal places.") { }
    public override bool IsValid(object? value) => value is not decimal amount || decimal.Round(amount, 2) == amount;
}

public sealed class ShippingOptionViewModel
{
    public int Id { get; set; }
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [Required, StringLength(200)] public string Area { get; set; } = "";
    [TwoDecimalPlaces, Range(typeof(decimal), "0", "9999999999999999.99")] public decimal Price { get; set; }
}

public sealed class DiscountViewModel : IValidatableObject
{
    public int Id { get; set; }
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [TwoDecimalPlaces, Range(typeof(decimal), "0", "100")] public decimal Percentage { get; set; }
    [Display(Name = "Product"), Range(1, int.MaxValue, ErrorMessage = "Choose a product.")] public int ProductId { get; set; }
    public string? ProductName { get; set; }
    [Display(Name = "Start date (UTC)"), DataType(DataType.DateTime)] public DateTime StartDate { get; set; }
    [Display(Name = "End date (UTC)"), DataType(DataType.DateTime)] public DateTime EndDate { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (StartDate == default) yield return new ValidationResult("Enter a valid start date.", new[] { nameof(StartDate) });
        if (EndDate <= StartDate) yield return new ValidationResult("End date must be after the start date.", new[] { nameof(EndDate) });
    }
}

public sealed class ContentPageViewModel
{
    public int Id { get; set; }
    [Required, StringLength(200)] public string Title { get; set; } = "";
    [Required, StringLength(200), RegularExpression(@"[A-Za-z0-9]+(?:[- ][A-Za-z0-9]+)*",
        ErrorMessage = "Use letters and numbers, separated by single spaces or hyphens.")]
    public string Slug { get; set; } = "";
    [Required, StringLength(100000)] public string Content { get; set; } = "";
}

public sealed class DashboardViewModel
{
    public int CategoryCount { get; set; }
    public int ProductCount { get; set; }
    public int AvailableProductCount { get; set; }
    public int OutOfStockProductCount { get; set; }
    public int OrderCount { get; set; }
    public int ShippingOptionCount { get; set; }
    public int DiscountCount { get; set; }
    public int ContentPageCount { get; set; }
    public List<OrderStatusCount> OrdersByStatus { get; set; } = new();
    public List<RecentOrder> RecentOrders { get; set; } = new();
}
public sealed record OrderStatusCount(string Status, int Count);
public sealed record ProductOptionViewModel(int Id, string Name);
public sealed record RecentOrder(int Id, string CustomerName, DateTime OrderDate, string Status, decimal TotalPrice);
