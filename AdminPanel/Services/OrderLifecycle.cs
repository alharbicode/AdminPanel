using AdminPanel.API.Data;
using AdminPanel.API.Models;
using Microsoft.EntityFrameworkCore;

namespace AdminPanel.API.Services;

public sealed class OrderLifecycle(ApplicationDbContext db, TimeProvider clock)
{
    public static readonly string[] States = ["Pending", "Confirmed", "Preparing", "Shipped", "Delivered", "Cancelled"];
    public static bool CustomerCanCancel(Order order) => order.Status is "Pending" or "Confirmed";
    public static bool CustomerCanRequestCancellation(Order order) => order.Status is "Preparing" or "Shipped";
    public async Task<Order> Load(int id, CancellationToken ct) =>
        await db.Orders.Include(o => o.OrderItems).ThenInclude(i => i.Product).SingleOrDefaultAsync(o => o.Id == id, ct)
        ?? throw new CartProblem(404, "OrderNotFound");

    private static void CheckVersion(Order order, Guid? expectedVersion)
    {
        if (expectedVersion.HasValue && expectedVersion.Value != order.Version) throw new CartProblem(409, "OrderConflict");
    }
    public async Task Transition(Order order, Guid? expectedVersion, string status, CancellationToken ct)
    {
        CheckVersion(order, expectedVersion);
        if (order.Status == status) return;
        var next = order.Status switch { "Pending" => "Confirmed", "Confirmed" => "Preparing", "Preparing" => "Shipped", "Shipped" => "Delivered", _ => null };
        if (next != status) throw new CartProblem(409, "OrderTransitionInvalid");
        order.Status = status; order.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
    }
    public async Task Cancel(Order order, Guid? expectedVersion, string actor, string? reason, bool administrator, CancellationToken ct)
    {
        // A repeat cancellation acknowledges existing history without touching stock again.
        if (order.Status == "Cancelled") return;
        CheckVersion(order, expectedVersion);
        if (!CustomerCanCancel(order) && !(administrator && order.Status == "Preparing"))
            throw new CartProblem(409, "CancellationUnavailable");
        if (order.StockRestoredAt is not null) throw new CartProblem(409, "OrderConflict");
        foreach (var item in order.OrderItems)
        {
            if (item.Product is null || item.Quantity < 1 || (long)item.Product.StockQuantity + item.Quantity > int.MaxValue)
                throw new CartProblem(409, "OrderConflict");
            item.Product.StockQuantity += item.Quantity;
        }
        var now = clock.GetUtcNow().UtcDateTime;
        order.Status = "Cancelled"; order.CancelledAt = now; order.StockRestoredAt = now;
        order.CancelledBy = actor; order.CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        order.Version = Guid.NewGuid();
        // Stock tokens + order version/status predicates commit in one transaction.
        await db.SaveChangesAsync(ct);
    }
    public async Task RequestCancellation(Order order, Guid? expectedVersion, string reason, CancellationToken ct)
    {
        CheckVersion(order, expectedVersion);
        if (!CustomerCanRequestCancellation(order)) throw new CartProblem(409, "CancellationUnavailable");
        if (order.CancellationRequestedAt is not null) return;
        order.CancellationRequestedAt = clock.GetUtcNow().UtcDateTime;
        order.CancellationRequestReason = reason.Trim(); order.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
    }
}
