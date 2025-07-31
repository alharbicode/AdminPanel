using AdminPanel.API.Data;
using AdminPanel.API.DTOs;
using AdminPanel.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdminPanel.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class OrdersController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public OrdersController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder(CreateOrderDto dto)
    {
        decimal total = 0;
        var items = new List<OrderItem>();

        foreach (var item in dto.Items)
        {
            var product = await _context.Products.FindAsync(item.ProductId);
            if (product == null || product.StockQuantity < item.Quantity)
                return BadRequest($"Invalid product or not enough stock for product ID: {item.ProductId}");

            decimal itemPrice = product.Price;
            total += itemPrice * item.Quantity;

            // خصم الكمية من المخزون
            product.StockQuantity -= item.Quantity;

            items.Add(new OrderItem
            {
                ProductId = product.Id,
                Quantity = item.Quantity,
                Price = itemPrice
            });
        }

        var order = new Order
        {
            CustomerId = dto.CustomerId,
            TotalPrice = total,
            ShippingOptionId = dto.ShippingOptionId,
            OrderItems = items
        };

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Order created successfully", orderId = order.Id });
    }

    [HttpGet]
    public async Task<ActionResult<List<OrderDetailsDto>>> GetOrders()
    {
        var orders = await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.OrderItems!)
            .ThenInclude(i => i.Product)
            .ToListAsync();

        return orders.Select(o => new OrderDetailsDto
        {
            Id = o.Id,
            CustomerName = o.Customer!.Username,
            OrderDate = o.OrderDate,
            TotalPrice = o.TotalPrice,
            Status = o.Status,
            Items = o.OrderItems!.Select(i => new OrderItemViewDto
            {
                ProductName = i.Product!.Name,
                Quantity = i.Quantity,
                Price = i.Price
            }).ToList()
        }).ToList();
    }

    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] string status)
    {
        var order = await _context.Orders.FindAsync(id);
        if (order == null) return NotFound();

        order.Status = status;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Order status updated" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> CancelOrder(int id)
    {
        var order = await _context.Orders
            .Include(o => o.OrderItems!)
            .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null) return NotFound();

        // إرجاع الكميات للمخزون
        foreach (var item in order.OrderItems!)
        {
            if (item.Product != null)
            {
                item.Product.StockQuantity += item.Quantity;
            }
        }

        _context.Orders.Remove(order);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Order cancelled and stock updated" });
    }
}
