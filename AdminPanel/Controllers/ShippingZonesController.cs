using AdminPanel.API.Data;
using AdminPanel.API.DTOs;
using AdminPanel.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdminPanel.API.Controllers;

[ApiController]
[Route("api/shippingzones")]
public sealed class ShippingZonesController(ApplicationDbContext db) : ControllerBase
{
    private IQueryable<ShippingZoneDto> Details(IQueryable<ShippingZone> rows) => rows.Select(x => new ShippingZoneDto
    {
        Id = x.Id, Governorate = x.Governorate, City = x.City, Fee = x.Fee, Enabled = x.Enabled, Version = x.Version
    });

    [HttpGet("admin/list")]
    public async Task<IActionResult> List([FromQuery] AdminListQuery query, CancellationToken ct)
    {
        var rows = db.ShippingZones.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            rows = rows.Where(x => x.Governorate.Contains(search) || x.City.Contains(search));
        }
        if (query.Available.HasValue) rows = rows.Where(x => x.Enabled == query.Available.Value);
        return Ok(await PagedResult<ShippingZoneDto>.CreateAsync(
            Details(rows.OrderBy(x => x.Governorate).ThenBy(x => x.City).ThenBy(x => x.Id)), query, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var item = await Details(db.ShippingZones.AsNoTracking().Where(x => x.Id == id)).SingleOrDefaultAsync(ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public Task<IActionResult> Create(ShippingZoneDto dto, CancellationToken ct) => Save(null, dto, ct);

    [HttpPut("{id:int}")]
    public Task<IActionResult> Edit(int id, ShippingZoneDto dto, CancellationToken ct) => Save(id, dto, ct);

    private async Task<IActionResult> Save(int? id, ShippingZoneDto dto, CancellationToken ct)
    {
        var item = id.HasValue ? await db.ShippingZones.FindAsync([id.Value], ct) : new ShippingZone();
        if (item is null) return NotFound();
        if (id.HasValue && (dto.Version is null || dto.Version != item.Version))
            return Conflict(new { message = "Shipping zone changed. Reload before saving." });
        var governorate = dto.Governorate.Trim();
        var city = dto.City?.Trim() ?? "";
        var normalized = governorate.ToUpperInvariant() + "\n" + city.ToUpperInvariant();
        if (await db.ShippingZones.AnyAsync(x => x.Id != item.Id && x.NormalizedDestination == normalized, ct))
            return Conflict(new { message = "A shipping zone already exists for that destination." });
        item.Governorate = governorate; item.City = city; item.NormalizedDestination = normalized;
        item.Fee = dto.Fee; item.Enabled = dto.Enabled; item.Version = Guid.NewGuid();
        if (!id.HasValue) db.ShippingZones.Add(item);
        await db.SaveChangesAsync(ct);
        return id.HasValue ? NoContent() : CreatedAtAction(nameof(Get), new { id = item.Id }, new { item.Id });
    }
}
