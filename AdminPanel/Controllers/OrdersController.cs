using AdminPanel.API.Data;
using AdminPanel.API.DTOs;
using AdminPanel.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdminPanel.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class OrdersController(ApplicationDbContext context) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateOrder(CreateOrderDto dto)
    {
        if (!await context.Users.AnyAsync(u => u.Id == dto.CustomerId && !u.IsBlocked))
            return BadRequest(new { message = "Invalid customer." });
        if (dto.ShippingOptionId.HasValue && !await context.ShippingOptions.AnyAsync(s => s.Id == dto.ShippingOptionId))
            return BadRequest(new { message = "Invalid shipping option." });
        if (dto.Items.Select(i => i.ProductId).Distinct().Count() != dto.Items.Count)
            return BadRequest(new { message = "Combine duplicate product lines before submitting." });
        var ids = dto.Items.Select(i => i.ProductId).ToList();
        var products = await context.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var order = new Order { CustomerId = dto.CustomerId, ShippingOptionId = dto.ShippingOptionId };
        foreach (var item in dto.Items)
        {
            if (!products.TryGetValue(item.ProductId, out var product) || !product.IsAvailable || product.StockQuantity < item.Quantity)
                return BadRequest(new { message = $"Product {item.ProductId} is unavailable or has insufficient stock." });
            order.TotalPrice += product.Price * item.Quantity;
            order.OrderItems.Add(new OrderItem { ProductId = product.Id, Quantity = item.Quantity, Price = product.Price });
        }
        if (order.TotalPrice > 9999999999999999.99m)
            return BadRequest(new { message = "Order total exceeds the supported amount." });
        // One SaveChanges transaction plus stock concurrency tokens prevents partial orders and lost stock updates.
        foreach (var item in dto.Items) products[item.ProductId].StockQuantity -= item.Quantity;
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, new { orderId = order.Id });
    }

    private IQueryable<OrderDetailsDto> DetailsQuery() => context.Orders.AsNoTracking().Select(o => new OrderDetailsDto
    {
        Id = o.Id, CustomerName = o.Customer!.Username, CustomerEmail = o.Customer.Email,
        OrderDate = o.OrderDate, TotalPrice = o.TotalPrice, Status = o.Status,
        Items = o.OrderItems.Select(i => new OrderItemViewDto
        { ProductName = i.Product!.Name, Quantity = i.Quantity, Price = i.Price }).ToList()
    });

    [HttpGet]
    public async Task<ActionResult<List<OrderDetailsDto>>> GetOrders() =>
        await DetailsQuery().OrderByDescending(o => o.Id).ToListAsync();

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderDetailsDto>> GetOrder(int id)
    {
        var order = await DetailsQuery().SingleOrDefaultAsync(o => o.Id == id);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] string status)
    {
        if (status is not ("Pending" or "Confirmed" or "Shipped" or "Delivered"))
            return BadRequest(new { message = "Use Pending, Confirmed, Shipped or Delivered. Use DELETE to cancel and restore stock." });
        var order = await context.Orders.FindAsync(id);
        if (order is null) return NotFound();
        order.Status = status;
        await context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> CancelOrder(int id)
    {
        var order = await context.Orders.Include(o => o.OrderItems).ThenInclude(i => i.Product)
            .SingleOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();
        foreach (var item in order.OrderItems)
        {
            if (item.Product is null || (long)item.Product.StockQuantity + item.Quantity > int.MaxValue)
                return Conflict(new { message = "Cannot restore product stock." });
            item.Product.StockQuantity += item.Quantity;
        }
        context.Orders.Remove(order);
        await context.SaveChangesAsync();
        return NoContent();
    }
}
