using AdminPanel.API.Data;
using AdminPanel.API.DTOs;
using AdminPanel.API.Models;
using AdminPanel.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdminPanel.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ProductsController(ApplicationDbContext context, ProductImageStore images) : ControllerBase
{
    private IQueryable<ProductDto> Products() => context.Products.AsNoTracking().Select(p => new ProductDto
    {
        Id = p.Id, Name = p.Name, Description = p.Description, Price = p.Price, ImageUrl = p.ImageUrl,
        StockQuantity = p.StockQuantity, Unit = p.Unit, IsAvailable = p.IsAvailable, CategoryId = p.CategoryId
    });
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProductDto>>> GetProducts() => Ok(await Products().ToListAsync());
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductDto>> GetProduct(int id)
    {
        var product = await Products().SingleOrDefaultAsync(p => p.Id == id);
        return product is null ? NotFound() : Ok(product);
    }
    [HttpPost]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public Task<IActionResult> Create([FromForm] ProductDto dto) => Save(null, dto);
    [HttpPut("{id:int}")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public Task<IActionResult> UpdateProduct(int id, [FromForm] ProductDto dto) => Save(id, dto);

    private async Task<IActionResult> Save(int? id, ProductDto dto)
    {
        var product = id.HasValue ? await context.Products.FindAsync(id.Value) : new Product();
        if (product is null) return NotFound();
        if (!await context.Categories.AnyAsync(c => c.Id == dto.CategoryId))
            return BadRequest(new { message = "Invalid category." });
        string? newImage = null;
        if (dto.ImageFile is not null)
        {
            try { newImage = await images.SaveAsync(dto.ImageFile); }
            catch (InvalidDataException exception) { return BadRequest(new { message = exception.Message }); }
        }
        product.Name = dto.Name; product.Description = dto.Description ?? "";
        product.Price = dto.Price; product.StockQuantity = dto.StockQuantity;
        product.Unit = dto.Unit; product.IsAvailable = dto.IsAvailable; product.CategoryId = dto.CategoryId;
        if (newImage is not null) product.ImageUrl = newImage;
        if (!id.HasValue) context.Products.Add(product);
        try { await context.SaveChangesAsync(); }
        catch { if (newImage is not null) images.Delete(newImage); throw; }
        // Keep old images for now: deleting a shared legacy file could break another product.
        return id.HasValue ? NoContent() : CreatedAtAction(nameof(GetProduct), new { id = product.Id }, new { product.Id });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        var product = await context.Products.FindAsync(id);
        if (product is null) return NotFound();
        if (await context.OrderItems.AnyAsync(i => i.ProductId == id) || await context.Discounts.AnyAsync(d => d.ProductId == id))
            return Conflict(new { message = "Remove related discounts first. Products in order history cannot be deleted." });
        context.Products.Remove(product);
        await context.SaveChangesAsync();
        return NoContent();
    }
}

