using AdminPanel.API.Data;
using AdminPanel.API.DTOs;
using AdminPanel.API.Models;
using Microsoft.EntityFrameworkCore;
namespace AdminPanel.API.Services;

public sealed class CartProblem(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
public sealed class CartService(ApplicationDbContext db, CatalogVisibility visibility, TimeProvider clock)
{
    public const int MaxQuantity = 99;
    public const int MaxProducts = 100;
    public const int GuestDays = 30;
    private IQueryable<ShoppingCart> Carts => db.ShoppingCarts.Include(c => c.Items);
    public async Task<GuestCartResult> CreateGuest(CancellationToken ct)
    {
        var secret = CustomerSecurity.NewSecret();
        var expires = clock.GetUtcNow().AddDays(GuestDays);
        var cart = new ShoppingCart { GuestTokenHash = CustomerSecurity.Hash(secret), ExpiresUtcTicks = expires.UtcTicks };
        db.ShoppingCarts.Add(cart); await db.SaveChangesAsync(ct);
        return new(secret, expires, await Read(cart, ct));
    }
    public async Task<ShoppingCart> Guest(string? token, bool allowMerged, CancellationToken ct)
    {
        if (token is null || token.Length != 43 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new CartProblem(404, "GuestExpired");
        var hash = CustomerSecurity.Hash(token);
        var now = clock.GetUtcNow().UtcTicks;
        var cart = await Carts.SingleOrDefaultAsync(c => c.GuestTokenHash == hash && c.CustomerId == null && c.ExpiresUtcTicks > now, ct);
        if (cart is null || (!allowMerged && cart.MergedToCustomerId.HasValue)) throw new CartProblem(404, "GuestExpired");
        return cart;
    }
    public async Task<ShoppingCart> Customer(int customerId, CancellationToken ct) =>
        await Carts.SingleOrDefaultAsync(c => c.CustomerId == customerId, ct) ?? throw new CartProblem(409, "CartConflict");

    private async Task<Dictionary<int, Product>> Products(IEnumerable<int> ids, CancellationToken ct)
    {
        var values = ids.Distinct().ToArray();
        return await visibility.Products().Include(p => p.Category).Where(p => values.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
    }
    private static int Limit(Product p) => Math.Min(MaxQuantity, p.StockQuantity);
    public async Task<CartView> Read(ShoppingCart cart, CancellationToken ct, bool adjusted = false, bool alreadyMerged = false)
    {
        var products = await Products(cart.Items.Select(i => i.ProductId), ct);
        var lines = new List<CartLine>();
        foreach (var item in cart.Items.OrderBy(i => i.ProductId))
        {
            if (!products.TryGetValue(item.ProductId, out var p)) { adjusted = true; continue; }
            var quantity = Math.Clamp(item.Quantity, 1, Limit(p));
            adjusted |= quantity != item.Quantity;
            lines.Add(new(p.Id, p.Name, p.Category!.Name, p.Price, "YER", p.ImageUrl != "", quantity, Limit(p), p.Price * quantity));
        }
        return new(cart.Version, lines, lines.Sum(i => i.Quantity), lines.Sum(i => i.LineTotal), "YER", adjusted, alreadyMerged);
    }
    private bool Normalize(ShoppingCart cart, Dictionary<int, Product> products)
    {
        var adjusted = false;
        foreach (var item in cart.Items.ToList())
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                db.ShoppingCartItems.Remove(item); cart.Items.Remove(item); adjusted = true;
            }
            else
            {
                var quantity = Math.Clamp(item.Quantity, 1, Limit(product));
                adjusted |= quantity != item.Quantity; item.Quantity = quantity;
            }
        }
        return adjusted;
    }
    public async Task<CartView> Mutate(ShoppingCart cart, int productId, CartMutationDto dto, string operation, CancellationToken ct)
    {
        if (dto.Version != cart.Version) throw new CartProblem(409, "CartConflict");
        var products = await Products(cart.Items.Select(i => i.ProductId).Append(productId), ct);
        if (operation != "remove" && !products.ContainsKey(productId)) throw new CartProblem(404, "ProductUnavailable");
        var adjusted = Normalize(cart, products);
        var item = cart.Items.SingleOrDefault(i => i.ProductId == productId);
        if (operation == "remove")
        {
            if (item is not null) { db.ShoppingCartItems.Remove(item); cart.Items.Remove(item); }
        }
        else
        {
            if (item is null)
            {
                if (cart.Items.Count >= MaxProducts) throw new CartProblem(400, "CartFull");
                item = new() { ProductId = productId, Cart = cart };
                cart.Items.Add(item);
            }
            var desired = operation == "add" ? (long)item.Quantity + dto.Quantity : dto.Quantity;
            item.Quantity = (int)Math.Min(desired, Limit(products[productId]));
            adjusted |= item.Quantity != desired;
        }
        cart.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct); // Parent version + all item changes commit or roll back together.
        return await Read(cart, ct, adjusted);
    }
    public async Task<CartView> Merge(int customerId, string token, CancellationToken ct)
    {
        var guest = await Guest(token, allowMerged: true, ct);
        var customer = await Customer(customerId, ct);
        if (guest.MergedToCustomerId.HasValue)
        {
            if (guest.MergedToCustomerId != customerId) throw new CartProblem(404, "GuestExpired");
            return await Read(customer, ct, alreadyMerged: true);
        }
        var products = await Products(guest.Items.Concat(customer.Items).Select(i => i.ProductId), ct);
        var adjusted = Normalize(customer, products) | Normalize(guest, products);
        foreach (var source in guest.Items.OrderBy(i => i.ProductId).ToList())
        {
            var target = customer.Items.SingleOrDefault(i => i.ProductId == source.ProductId);
            if (target is null)
            {
                if (customer.Items.Count >= MaxProducts) { adjusted = true; continue; }
                target = new() { Cart = customer, ProductId = source.ProductId };
                customer.Items.Add(target);
            }
            var desired = (long)target.Quantity + source.Quantity;
            target.Quantity = (int)Math.Min(desired, Limit(products[source.ProductId]));
            adjusted |= target.Quantity != desired;
        }
        db.ShoppingCartItems.RemoveRange(guest.Items);
        guest.Items.Clear();
        guest.MergedToCustomerId = customerId;
        guest.Version = Guid.NewGuid(); customer.Version = Guid.NewGuid();
        // Both version checks and consumed-guest marker are in ONE SaveChanges transaction.
        // A concurrent update/merge rolls back all writes and returns 409; a retry cannot merge twice.
        await db.SaveChangesAsync(ct);
        return await Read(customer, ct, adjusted);
    }
}

