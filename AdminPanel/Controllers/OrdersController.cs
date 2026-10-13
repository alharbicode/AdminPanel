using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdminPanel.API.Data;
using AdminPanel.API.DTOs;
using AdminPanel.API.Models;
using AdminPanel.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdminPanel.API.Controllers;

[Route("api/[controller]")]
[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class OrdersController(ApplicationDbContext context, OrderLifecycle lifecycle) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateOrder(CreateOrderDto dto, CancellationToken ct)
    {
        var owner = "admin:" + User.FindFirstValue(ClaimTypes.NameIdentifier);
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            dto.CustomerId, dto.ShippingOptionId,
            Items = dto.Items.OrderBy(x => x.ProductId).Select(x => new { x.ProductId, x.Quantity })
        }))));
        var existing = await context.Orders.AsNoTracking().SingleOrDefaultAsync(
            x => x.CheckoutOwner == owner && x.IdempotencyKey == dto.RequestKey, ct);
        if (existing is not null) return Replay(existing, requestHash);
        var customer = await context.Users.SingleOrDefaultAsync(u => u.Id == dto.CustomerId && !u.IsBlocked, ct);
        if (customer is null) return BadRequest(new { message = "Invalid customer." });
        if (customer.Username.Length > 100 || customer.Email.Length > 254)
            return BadRequest(new { message = "The legacy customer contact details exceed supported order limits. Review the customer data before creating this order." });
        if (dto.ShippingOptionId.HasValue && !await context.ShippingOptions.AnyAsync(s => s.Id == dto.ShippingOptionId, ct))
            return BadRequest(new { message = "Invalid shipping option." });
        if (dto.Items.Select(i => i.ProductId).Distinct().Count() != dto.Items.Count)
            return BadRequest(new { message = "Combine duplicate product lines before submitting." });
        var ids = dto.Items.Select(i => i.ProductId).ToList();
        var products = await context.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var order = new Order
        {
            CustomerId = dto.CustomerId, ShippingOptionId = dto.ShippingOptionId,
            ContactName = customer.Username, ContactEmail = customer.Email,
            CheckoutOwner = owner, IdempotencyKey = dto.RequestKey, CheckoutRequestHash = requestHash
        };
        foreach (var item in dto.Items)
        {
            if (!products.TryGetValue(item.ProductId, out var product) || !product.IsAvailable || product.Price < 0 || product.StockQuantity < item.Quantity)
                return BadRequest(new { message = $"Product {item.ProductId} is unavailable or has insufficient stock." });
            if (product.Name.Length > 200)
                return BadRequest(new { message = $"Product {item.ProductId} has a legacy name exceeding the supported order limit. Review it before creating this order." });
            order.TotalPrice += product.Price * item.Quantity;
            order.OrderItems.Add(new OrderItem
            {
                ProductId = product.Id, ProductName = product.Name, Quantity = item.Quantity,
                Price = product.Price, OriginalUnitPrice = product.Price, UnitDiscount = 0
            });
        }
        if (order.TotalPrice > 9999999999999999.99m)
            return BadRequest(new { message = "Order total exceeds the supported amount." });
        // Preserve the legacy admin calculation, which never charged shipping or discounts.
        // Stock concurrency predicates and the unique owner/request key commit in one transaction.
        foreach (var item in dto.Items) products[item.ProductId].StockQuantity -= item.Quantity;
        context.Orders.Add(order);
        try { await context.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            existing = await context.Orders.AsNoTracking().SingleOrDefaultAsync(
                x => x.CheckoutOwner == owner && x.IdempotencyKey == dto.RequestKey, ct);
            if (existing is not null) return Replay(existing, requestHash);
            throw;
        }
        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, new { orderId = order.Id });
    }

    private IActionResult Replay(Order existing, string requestHash) => existing.CheckoutRequestHash == requestHash
        ? Ok(new { orderId = existing.Id })
        : Conflict(new { message = "This request key was already used for a different order." });

    private IQueryable<OrderDetailsDto> DetailsQuery() => context.Orders.AsNoTracking().Select(o => new OrderDetailsDto
    {
        Id = o.Id, CustomerName = o.ContactName ?? (o.Customer == null ? "Guest" : o.Customer.Username),
        CustomerEmail = o.ContactEmail ?? (o.Customer == null ? "" : o.Customer.Email),
        IsGuest = o.CustomerId == null, Reference = o.Reference, Version = o.Version,
        OrderDate = o.OrderDate, TotalPrice = o.TotalPrice, Status = o.Status,
        Phone = o.Phone, CountryCode = o.CountryCode, Governorate = o.Governorate, City = o.City,
        AddressLine = o.AddressLine, DeliveryInstructions = o.DeliveryInstructions,
        Currency = o.Currency, PaymentMethod = o.PaymentMethod, Subtotal = o.Subtotal,
        DiscountTotal = o.DiscountTotal, ShippingFee = o.ShippingFee,
        CancelledAt = o.CancelledAt, CancelledBy = o.CancelledBy, CancellationReason = o.CancellationReason,
        StockRestoredAt = o.StockRestoredAt, CancellationRequestedAt = o.CancellationRequestedAt,
        CancellationRequestReason = o.CancellationRequestReason,
        Items = o.OrderItems.OrderBy(i => i.Id).Select(i => new OrderItemViewDto
        {
            ProductName = i.ProductName ?? (i.Product == null ? "Retained product" : i.Product.Name),
            Quantity = i.Quantity, Price = i.Price, OriginalUnitPrice = i.OriginalUnitPrice,
            UnitDiscount = i.UnitDiscount, DiscountName = i.DiscountName, DiscountPercentage = i.DiscountPercentage
        }).ToList()
    });

    [HttpGet]
    public async Task<ActionResult<List<OrderDetailsDto>>> GetOrders(CancellationToken ct) =>
        await DetailsQuery().OrderByDescending(o => o.Id).ToListAsync(ct);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderDetailsDto>> GetOrder(int id, CancellationToken ct)
    {
        var order = await DetailsQuery().SingleOrDefaultAsync(o => o.Id == id, ct);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpPut("{id:int}/transition")]
    public async Task<IActionResult> Transition(int id, AdminOrderTransition dto, CancellationToken ct)
    {
        await lifecycle.Transition(await lifecycle.Load(id, ct), dto.Version, dto.Status, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, AdminOrderCancellation dto, CancellationToken ct)
    {
        await lifecycle.Cancel(await lifecycle.Load(id, ct), dto.Version,
            "admin:" + User.FindFirstValue(ClaimTypes.NameIdentifier), dto.Reason, true, ct);
        return NoContent();
    }

    // Older clients retain their routes, subject to the same non-destructive lifecycle.
    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] string status, CancellationToken ct)
    {
        if (!OrderLifecycle.States.Contains(status) || status == "Cancelled")
            return BadRequest(new { message = "Use an order lifecycle status. Use the cancellation route to cancel." });
        await lifecycle.Transition(await lifecycle.Load(id, ct), null, status, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> CancelOrder(int id, CancellationToken ct)
    {
        await lifecycle.Cancel(await lifecycle.Load(id, ct), null,
            "admin:" + User.FindFirstValue(ClaimTypes.NameIdentifier), "Cancelled through the legacy administration endpoint.", true, ct);
        return NoContent();
    }
}
