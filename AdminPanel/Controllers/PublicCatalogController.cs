using System.Text.RegularExpressions;
using AdminPanel.API.Data;
using AdminPanel.API.DTOs;
using AdminPanel.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdminPanel.API.Controllers;

// Explicitly public READ surface. Existing controllers retain the Admin fallback policy.
[ApiController, AllowAnonymous, Route("api/catalog")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicCatalogController(ApplicationDbContext db, IConfiguration configuration,
    IWebHostEnvironment environment) : ControllerBase
{
    private IQueryable<Product> VisibleProducts()
    {
        // Empty/missing allowlist publishes nothing. Availability and stock are additional gates.
        var approved = configuration.GetSection("PublicCatalog:ProductIds").Get<int[]>() ?? [];
        return db.Products.AsNoTracking().Where(p => approved.Contains(p.Id) &&
            p.IsAvailable && p.StockQuantity > 0 && p.Price >= 0 && p.Category != null);
    }

    private static IQueryable<PublicProduct> Project(IQueryable<Product> rows) => rows.Select(p =>
        new PublicProduct(p.Id, p.Name, p.Description, p.Price, "YER", p.Unit,
            p.CategoryId, p.Category!.Name, p.ImageUrl != ""));

    [HttpGet("products")]
    public async Task<ActionResult<PublicPage<PublicProduct>>> Products([FromQuery] PublicCatalogQuery query, CancellationToken ct)
    {
        var rows = VisibleProducts();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            rows = rows.Where(p => p.Name.Contains(term) || p.Description.Contains(term) || p.Category!.Name.Contains(term));
        }
        if (query.CategoryId.HasValue) rows = rows.Where(p => p.CategoryId == query.CategoryId.Value);
        var total = await rows.CountAsync(ct);
        var page = Math.Min(query.Page, Math.Max(1, (int)Math.Ceiling(total / (double)query.PageSize)));
        var ordered = query.Sort == "name" ? rows.OrderBy(p => p.Name).ThenBy(p => p.Id) : rows.OrderByDescending(p => p.Id);
        var items = await Project(ordered.Skip((page - 1) * query.PageSize).Take(query.PageSize)).ToListAsync(ct);
        return Ok(new PublicPage<PublicProduct>(items, total, page, query.PageSize));
    }

    [HttpGet("products/{id:int}")]
    public async Task<ActionResult<PublicProduct>> Product(int id, CancellationToken ct)
    {
        var item = await Project(VisibleProducts().Where(p => p.Id == id)).SingleOrDefaultAsync(ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpGet("categories")]
    public async Task<ActionResult<PublicPage<PublicCategory>>> Categories([FromQuery] PublicCatalogQuery query, CancellationToken ct)
    {
        var visible = VisibleProducts();
        var rows = db.Categories.AsNoTracking().Where(c => visible.Any(p => p.CategoryId == c.Id));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            rows = rows.Where(c => c.Name.Contains(term) || c.Description.Contains(term));
        }
        var total = await rows.CountAsync(ct);
        var page = Math.Min(query.Page, Math.Max(1, (int)Math.Ceiling(total / (double)query.PageSize)));
        var items = await rows.OrderBy(c => c.Name).ThenBy(c => c.Id).Skip((page - 1) * query.PageSize).Take(query.PageSize)
            .Select(c => new PublicCategory(c.Id, c.Name, c.Description, visible.Count(p => p.CategoryId == c.Id))).ToListAsync(ct);
        return Ok(new PublicPage<PublicCategory>(items, total, page, query.PageSize));
    }

    [HttpGet("products/{id:int}/image")]
    public async Task<IActionResult> Image(int id, CancellationToken ct)
    {
        var path = await VisibleProducts().Where(p => p.Id == id).Select(p => p.ImageUrl).SingleOrDefaultAsync(ct);
        // Never fetch external/legacy URLs or allow traversal, even for approved products.
        if (path is null || !Regex.IsMatch(path, @"\A/images/products/[A-Za-z0-9_-]+\.(jpg|jpeg|png|webp)\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return NotFound();
        var fullPath = Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"),
            "images", "products", Path.GetFileName(path));
        if (!System.IO.File.Exists(fullPath) || new FileInfo(fullPath).Length > 5 * 1024 * 1024) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var type = Path.GetExtension(path).ToLowerInvariant() switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
        return PhysicalFile(fullPath, type);
    }
}
