using AdminPanel.API.Data;
using AdminPanel.API.DTOs;
using AdminPanel.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdminPanel.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class DiscountsController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public DiscountsController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DiscountDto>>> GetDiscounts()
    {
        var discounts = await _context.Discounts
            .Include(d => d.Product)
            .Select(d => new DiscountDto
            {
                Id = d.Id,
                Name = d.Name,
                Percentage = d.Percentage,
                StartDate = d.StartDate,
                EndDate = d.EndDate,
                ProductId = d.ProductId,
                ProductName = d.Product!.Name
            }).ToListAsync();

        return Ok(discounts);
    }

    [HttpPost]
    public async Task<IActionResult> CreateDiscount(CreateDiscountDto dto)
    {
        var product = await _context.Products.FindAsync(dto.ProductId);
        if (product == null) return BadRequest("Invalid product ID");

        var discount = new Discount
        {
            Name = dto.Name,
            Percentage = dto.Percentage,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            ProductId = dto.ProductId
        };

        _context.Discounts.Add(discount);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Discount created successfully" });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateDiscount(int id, CreateDiscountDto dto)
    {
        var discount = await _context.Discounts.FindAsync(id);
        if (discount == null) return NotFound();

        if (!await _context.Products.AnyAsync(p => p.Id == dto.ProductId))
            return BadRequest(new { message = "Invalid product ID." });
        discount.Name = dto.Name;
        discount.Percentage = dto.Percentage;
        discount.StartDate = dto.StartDate;
        discount.EndDate = dto.EndDate;
        discount.ProductId = dto.ProductId;

        await _context.SaveChangesAsync();

        return Ok(new { message = "Discount updated successfully" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteDiscount(int id)
    {
        var discount = await _context.Discounts.FindAsync(id);
        if (discount == null) return NotFound();

        _context.Discounts.Remove(discount);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Discount deleted successfully" });
    }
}
