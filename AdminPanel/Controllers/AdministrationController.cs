using AdminPanel.API.Data;
using AdminPanel.API.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdminPanel.API.Controllers;

// Additive read endpoints: existing collection and mutation contracts remain available.
[ApiController]
public sealed class AdministrationController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet("/api/shippingoptions/admin/list")]
    public async Task<IActionResult> Shipping([FromQuery] AdminListQuery query, CancellationToken ct)
    {
        var rows = db.ShippingOptions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            rows = rows.Where(x => x.Name.Contains(term) || x.Area.Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(query.Area))
        {
            var area = query.Area.Trim();
            rows = rows.Where(x => x.Area.Contains(area));
        }
        return Ok(await PagedResult<ShippingOptionDto>.CreateAsync(rows.OrderBy(x => x.Id)
            .Select(x => new ShippingOptionDto { Id = x.Id, Name = x.Name, Area = x.Area, Price = x.Price }), query, ct));
    }

    [HttpGet("/api/shippingoptions/{id:int}")]
    public async Task<IActionResult> ShippingDetails(int id, CancellationToken ct)
    {
        var item = await db.ShippingOptions.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new ShippingOptionDto { Id = x.Id, Name = x.Name, Area = x.Area, Price = x.Price }).SingleOrDefaultAsync(ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpGet("/api/discounts/admin/list")]
    public async Task<IActionResult> Discounts([FromQuery] AdminListQuery query, CancellationToken ct)
    {
        var rows = db.Discounts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            rows = rows.Where(x => x.Name.Contains(term) || x.Product!.Name.Contains(term));
        }
        if (query.ProductId.HasValue) rows = rows.Where(x => x.ProductId == query.ProductId.Value);
        // These are date-window filters only; they do not determine checkout eligibility.
        var now = DateTime.UtcNow;
        rows = query.Period switch
        {
            "upcoming" => rows.Where(x => x.StartDate > now),
            "current" => rows.Where(x => x.StartDate <= now && x.EndDate >= now),
            "ended" => rows.Where(x => x.EndDate < now),
            _ => rows
        };
        return Ok(await PagedResult<DiscountDto>.CreateAsync(rows.OrderBy(x => x.Id).Select(x => new DiscountDto
        {
            Id = x.Id, Name = x.Name, Percentage = x.Percentage, ProductId = x.ProductId,
            ProductName = x.Product!.Name, StartDate = x.StartDate, EndDate = x.EndDate
        }), query, ct));
    }

    [HttpGet("/api/discounts/{id:int}")]
    public async Task<IActionResult> DiscountDetails(int id, CancellationToken ct)
    {
        var item = await db.Discounts.AsNoTracking().Where(x => x.Id == id).Select(x => new DiscountDto
        {
            Id = x.Id, Name = x.Name, Percentage = x.Percentage, ProductId = x.ProductId,
            ProductName = x.Product!.Name, StartDate = x.StartDate, EndDate = x.EndDate
        }).SingleOrDefaultAsync(ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpGet("/api/contentpages/admin/list")]
    public async Task<IActionResult> Pages([FromQuery] AdminListQuery query, CancellationToken ct)
    {
        var rows = db.ContentPages.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            rows = rows.Where(x => x.Title.Contains(term) || x.Slug.Contains(term));
        }
        // Do not transfer potentially large page bodies for a list.
        return Ok(await PagedResult<ContentPageDto>.CreateAsync(rows.OrderBy(x => x.Id)
            .Select(x => new ContentPageDto { Id = x.Id, Title = x.Title, Slug = x.Slug }), query, ct));
    }

    [HttpGet("/api/contentpages/by-id/{id:int}")]
    public async Task<IActionResult> PageDetails(int id, CancellationToken ct)
    {
        var item = await db.ContentPages.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new ContentPageDto { Id = x.Id, Title = x.Title, Slug = x.Slug, Content = x.Content }).SingleOrDefaultAsync(ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpGet("/api/dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        var products = db.Products.AsNoTracking();
        var orders = db.Orders.AsNoTracking();
        return Ok(new
        {
            CategoryCount = await db.Categories.CountAsync(ct),
            ProductCount = await products.CountAsync(ct),
            AvailableProductCount = await products.CountAsync(x => x.IsAvailable, ct),
            OutOfStockProductCount = await products.CountAsync(x => x.StockQuantity == 0, ct),
            OrderCount = await orders.CountAsync(ct),
            OrdersByStatus = await orders.GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).OrderBy(x => x.Status).ToListAsync(ct),
            ShippingOptionCount = await db.ShippingOptions.CountAsync(ct),
            DiscountCount = await db.Discounts.CountAsync(ct),
            ContentPageCount = await db.ContentPages.CountAsync(ct),
            RecentOrders = await orders.OrderByDescending(x => x.OrderDate).ThenByDescending(x => x.Id).Take(5)
                .Select(x => new { x.Id, CustomerName = x.Customer!.Username, x.OrderDate, x.Status, x.TotalPrice }).ToListAsync(ct)
        });
    }
}
