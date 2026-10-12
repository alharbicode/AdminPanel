using AdminPanel.API.Data;
using AdminPanel.API.DTOs;
using AdminPanel.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdminPanel.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ShippingOptionsController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public ShippingOptionsController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ShippingOptionDto>>> GetOptions()
    {
        var options = await _context.ShippingOptions
            .Select(o => new ShippingOptionDto
            {
                Id = o.Id,
                Name = o.Name,
                Area = o.Area,
                Price = o.Price
            }).ToListAsync();

        return Ok(options);
    }

    [HttpPost]
    public async Task<IActionResult> Create(ShippingOptionDto dto)
    {
        var option = new ShippingOption
        {
            Name = dto.Name,
            Area = dto.Area,
            Price = dto.Price
        };

        _context.ShippingOptions.Add(option);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Shipping option created." });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, ShippingOptionDto dto)
    {
        var option = await _context.ShippingOptions.FindAsync(id);
        if (option == null) return NotFound();

        option.Name = dto.Name;
        option.Area = dto.Area;
        option.Price = dto.Price;

        await _context.SaveChangesAsync();

        return Ok(new { message = "Shipping option updated." });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var option = await _context.ShippingOptions.FindAsync(id);
        if (option == null) return NotFound();

        if (await _context.Orders.AnyAsync(o => o.ShippingOptionId == id))
            return Conflict(new { message = "Shipping option is used by orders." });
        _context.ShippingOptions.Remove(option);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Shipping option deleted." });
    }
}
