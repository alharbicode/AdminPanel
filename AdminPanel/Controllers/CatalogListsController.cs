using AdminPanel.API.Data;
using AdminPanel.API.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdminPanel.API.Controllers;

[ApiController]
public sealed class CatalogListsController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet("/api/categories/admin/list")]
    public async Task<IActionResult> Categories([FromQuery] AdminListQuery query, CancellationToken ct)
    {
        var rows = db.Categories.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            rows = rows.Where(x => x.Name.Contains(term) || x.Description.Contains(term));
        }
        if (query.HasProducts.HasValue)
            rows = rows.Where(x => db.Products.Any(p => p.CategoryId == x.Id) == query.HasProducts.Value);
        var ordered = query.Sort switch
        {
            "name" => rows.OrderBy(x => x.Name).ThenBy(x => x.Id),
            "name-desc" => rows.OrderByDescending(x => x.Name).ThenBy(x => x.Id),
            _ => rows.OrderBy(x => x.Id)
        };
        return Ok(await PagedResult<CategoryDto>.CreateAsync(ordered.Select(x => new CategoryDto
        { Id = x.Id, Name = x.Name, Description = x.Description }), query, ct));
    }

    [HttpGet("/api/products/admin/list")]
    public async Task<IActionResult> Products([FromQuery] AdminListQuery query, CancellationToken ct)
    {
        var rows = ProductRows(query);
        var ordered = query.Sort switch
        {
            "name" => rows.OrderBy(x => x.Name).ThenBy(x => x.Id),
            "name-desc" => rows.OrderByDescending(x => x.Name).ThenBy(x => x.Id),
            "stock" => rows.OrderBy(x => x.StockQuantity).ThenBy(x => x.Id),
            "stock-desc" => rows.OrderByDescending(x => x.StockQuantity).ThenBy(x => x.Id),
            _ => rows.OrderBy(x => x.Id)
        };
        return Ok(await PagedResult<ProductDto>.CreateAsync(ordered.Select(x => new ProductDto
        {
            Id = x.Id, Name = x.Name, Description = x.Description, CategoryId = x.CategoryId,
            Price = x.Price, StockQuantity = x.StockQuantity, ExpectedStockQuantity = x.StockQuantity, Unit = x.Unit, IsAvailable = x.IsAvailable, ImageUrl = x.ImageUrl
        }), query, ct));
    }

    [HttpGet("/api/products/admin/lookup")]
    public async Task<IActionResult> Lookup([FromQuery] AdminListQuery query, CancellationToken ct)
    {
        // A bounded, metadata-only picker; never download the full product catalog.
        query.PageSize = 20;
        return Ok(await PagedResult<ProductOption>.CreateAsync(ProductRows(query).OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new ProductOption(x.Id, x.Name)), query, ct));
    }

    private IQueryable<Models.Product> ProductRows(AdminListQuery query)
    {
        var rows = db.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            rows = rows.Where(x => x.Name.Contains(term) || x.Description.Contains(term));
        }
        if (query.CategoryId.HasValue) rows = rows.Where(x => x.CategoryId == query.CategoryId.Value);
        if (query.Available.HasValue) rows = rows.Where(x => x.IsAvailable == query.Available.Value);
        if (query.Stock == "in-stock") rows = rows.Where(x => x.StockQuantity > 0);
        if (query.Stock == "out-of-stock") rows = rows.Where(x => x.StockQuantity == 0);
        return rows;
    }

    [HttpGet("/api/orders/admin/list")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Orders([FromQuery] AdminListQuery query, CancellationToken ct)
    {
        var rows = db.Orders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var orderId = int.TryParse(term, out var parsed) ? parsed : 0;
            rows = rows.Where(x => x.Id == orderId || (x.ContactName != null && x.ContactName.Contains(term)) ||
                (x.ContactEmail != null && x.ContactEmail.Contains(term)) || (x.Reference != null && x.Reference.Contains(term)) ||
                (x.Customer != null && (x.Customer.Username.Contains(term) || x.Customer.Email.Contains(term))));
        }
        if (query.Status is not null) rows = rows.Where(x => x.Status == query.Status);
        if (query.From.HasValue)
        {
            var from = query.From.Value.Date;
            rows = rows.Where(x => x.OrderDate >= from);
        }
        if (query.To.HasValue)
        {
            var to = query.To.Value.Date == DateTime.MaxValue.Date ? DateTime.MaxValue : query.To.Value.Date.AddDays(1).AddTicks(-1);
            rows = rows.Where(x => x.OrderDate <= to);
        }
        var ordered = query.Sort switch
        {
            "oldest" => rows.OrderBy(x => x.OrderDate).ThenBy(x => x.Id),
            "id" => rows.OrderBy(x => x.Id),
            _ => rows.OrderByDescending(x => x.OrderDate).ThenByDescending(x => x.Id)
        };
        return Ok(await PagedResult<OrderDetailsDto>.CreateAsync(ordered.Select(x => new OrderDetailsDto
        {
            Id = x.Id, CustomerName = x.ContactName ?? (x.Customer == null ? "Guest" : x.Customer.Username),
            CustomerEmail = x.ContactEmail ?? (x.Customer == null ? "" : x.Customer.Email),
            IsGuest = x.CustomerId == null, Reference = x.Reference, Currency = x.Currency,
            Status = x.Status, OrderDate = x.OrderDate, TotalPrice = x.TotalPrice
        }), query, ct));
    }

    public sealed record ProductOption(int Id, string Name);
}
