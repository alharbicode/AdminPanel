using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdminPanel.API.Data;
using AdminPanel.API.DTOs;
using AdminPanel.API.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AdminPanel.API.Services;

public sealed class CheckoutService(ApplicationDbContext db, CartService carts, CatalogVisibility visibility, TimeProvider clock)
{
    private const decimal MaxAmount = 9999999999999999.99m;
    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string Digest(object value) => CustomerSecurity.Hash(JsonSerializer.Serialize(value));
    private static object Address(CheckoutQuoteDto dto) => new
    {
        dto.ShippingZoneId, ContactName = dto.ContactName.Trim(), Phone = dto.Phone.Trim(),
        Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim(), City = dto.City.Trim(),
        AddressLine = dto.AddressLine.Trim(), DeliveryInstructions = string.IsNullOrWhiteSpace(dto.DeliveryInstructions) ? null : dto.DeliveryInstructions.Trim()
    };
    private static string RequestHash(CheckoutConfirmDto dto) => Digest(new { dto.CartVersion, Address = Address(dto), QuoteFingerprint = dto.QuoteFingerprint.ToUpperInvariant() });
    private static string Owner(ShoppingCart cart) => cart.CustomerId.HasValue ? $"customer:{cart.CustomerId}" : $"guest:{cart.Id:N}";
    private Task<ShoppingCart> Cart(int? customerId, string? guestToken, CancellationToken ct) =>
        customerId.HasValue ? carts.Customer(customerId.Value, ct) : carts.Guest(guestToken, false, ct);
    private static bool ValidZone(ShippingZone zone) => zone.Enabled && !string.IsNullOrWhiteSpace(zone.Governorate) &&
        zone.Fee >= 0 && zone.Fee <= MaxAmount && Round(zone.Fee) == zone.Fee;

    public async Task<IReadOnlyList<ShippingDestination>> Destinations(CancellationToken ct) =>
        (await db.ShippingZones.AsNoTracking().Where(z => z.Enabled).OrderBy(z => z.Governorate).ThenBy(z => z.City).ToListAsync(ct))
        .Where(ValidZone).Select(z => new ShippingDestination(z.Id, z.Governorate, z.City, z.Fee, "YER")).ToList();

    public async Task<CheckoutQuote> Quote(int? customerId, string? guestToken, CheckoutQuoteDto dto, CancellationToken ct) =>
        (await Price(await Cart(customerId, guestToken, ct), dto, ct)).Quote;

    private async Task<(CheckoutQuote Quote, Dictionary<int, Product> Products)> Price(ShoppingCart cart, CheckoutQuoteDto dto, CancellationToken ct)
    {
        if (dto.CartVersion != cart.Version) throw new CartProblem(409, "CheckoutChanged");
        if (cart.Items.Count == 0) throw new CartProblem(400, "CartEmpty");
        if (cart.Items.Count > CartService.MaxProducts || cart.Items.Any(i => i.Quantity is < 1 or > CartService.MaxQuantity))
            throw new CartProblem(409, "CheckoutChanged");
        if (string.IsNullOrWhiteSpace(dto.ContactName) || string.IsNullOrWhiteSpace(dto.City) || dto.AddressLine.Trim().Length < 5)
            throw new CartProblem(400, "CheckoutInvalid");
        var zone = await db.ShippingZones.SingleOrDefaultAsync(z => z.Id == dto.ShippingZoneId, ct);
        if (zone is null || !ValidZone(zone) || (zone.City.Length > 0 && !string.Equals(zone.City.Trim(), dto.City.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new CartProblem(400, "ShippingUnavailable");
        var ids = cart.Items.Select(i => i.ProductId).ToArray();
        var products = await visibility.Products().AsTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        if (products.Count != ids.Length || cart.Items.Any(i => products[i.ProductId].StockQuantity < i.Quantity))
            throw new CartProblem(409, "CheckoutChanged");
        var now = clock.GetUtcNow().UtcDateTime;
        // Materialize before decimal sorting so SQL Server and SQLite choose the same winner.
        var eligible = (await db.Discounts.AsNoTracking().Where(d => ids.Contains(d.ProductId) && d.StartDate <= now && d.EndDate > now && d.StartDate < d.EndDate).ToListAsync(ct))
            .Where(d => d.Percentage > 0 && d.Percentage <= 100 && Round(d.Percentage) == d.Percentage)
            .GroupBy(d => d.ProductId).ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.Percentage).ThenBy(d => d.Id).First());
        var lines = new List<CheckoutLine>();
        foreach (var item in cart.Items.OrderBy(i => i.ProductId))
        {
            var product = products[item.ProductId];
            if (product.Price < 0 || product.Price > MaxAmount || Round(product.Price) != product.Price)
                throw new CartProblem(409, "CheckoutChanged");
            eligible.TryGetValue(product.Id, out var discount);
            // Older catalog rows predate form limits. Reject unsupported snapshots before inventory writes;
            // never truncate a product/discount name or block checkout for an unrelated discount.
            if (product.Name.Length > 200 || discount?.Name.Length > 200)
                throw new CartProblem(400, "CheckoutInvalid");
            var unitDiscount = Round(product.Price * (discount?.Percentage ?? 0) / 100m);
            var final = product.Price - unitDiscount;
            lines.Add(new(product.Id, product.Name, item.Quantity, product.Price, discount?.Id, discount?.Name,
                discount?.Percentage ?? 0, unitDiscount, final, final * item.Quantity));
        }
        var subtotal = lines.Sum(i => i.OriginalUnitPrice * i.Quantity);
        var discountTotal = lines.Sum(i => i.UnitDiscount * i.Quantity);
        var total = subtotal - discountTotal + zone.Fee;
        if (total < 0 || total > MaxAmount || subtotal > MaxAmount) throw new CartProblem(400, "CheckoutInvalid");
        var city = zone.City.Length == 0 ? dto.City.Trim() : zone.City;
        var fingerprint = Digest(new { CartVersion = cart.Version, Address = Address(dto), zone.Id, zone.Version,
            zone.Governorate, City = city, Items = lines, Subtotal = subtotal, DiscountTotal = discountTotal, ShippingFee = zone.Fee, Total = total, Currency = "YER", PaymentMethod = "COD" });
        return (new(cart.Version, fingerprint, zone.Id, zone.Governorate, city, lines, subtotal, discountTotal, zone.Fee, total, "YER", "COD"), products);
    }

    public async Task<CheckoutResult> Confirm(int? customerId, string? guestToken, CheckoutConfirmDto dto, CancellationToken ct)
    {
        if (!dto.IdempotencyKey.HasValue || dto.IdempotencyKey == Guid.Empty) throw new CartProblem(400, "CheckoutInvalid");
        string? owner = null;
        var requestHash = RequestHash(dto);
        // Serializable keeps destination/discount/visibility reads consistent until the stock and order write commits.
        await using var transaction = await BeginConfirmation(ct);
        try
        {
            var cart = await Cart(customerId, guestToken, ct);
            owner = Owner(cart);
            var prior = await Existing(owner, dto.IdempotencyKey.Value, ct);
            if (prior is not null)
            {
                var replay = Replay(prior, requestHash, guestToken);
                await transaction.CommitAsync(ct);
                return replay;
            }
            var priced = await Price(cart, dto, ct);
            var quote = priced.Quote;
            if (!string.Equals(dto.QuoteFingerprint, quote.QuoteFingerprint, StringComparison.OrdinalIgnoreCase))
                throw new CartProblem(409, "CheckoutChanged");
            var order = new Order
            {
                CustomerId = customerId, Reference = "AP-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)),
                CheckoutOwner = owner, IdempotencyKey = dto.IdempotencyKey.Value, CheckoutRequestHash = requestHash,
                OrderDate = clock.GetUtcNow().UtcDateTime, ContactName = dto.ContactName.Trim(), Phone = dto.Phone.Trim(),
                ContactEmail = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim(), CountryCode = "YE",
                Governorate = quote.Governorate, City = quote.City, AddressLine = dto.AddressLine.Trim(),
                DeliveryInstructions = string.IsNullOrWhiteSpace(dto.DeliveryInstructions) ? null : dto.DeliveryInstructions.Trim(),
                Currency = "YER", PaymentMethod = "COD", Subtotal = quote.Subtotal, DiscountTotal = quote.DiscountTotal,
                ShippingFee = quote.ShippingFee, TotalPrice = quote.Total, Status = "Pending"
            };
            if (!customerId.HasValue) order.GuestTrackingHash = CustomerSecurity.Hash(TrackingToken(order, guestToken!));
            foreach (var line in quote.Items)
            {
                var product = priced.Products[line.ProductId];
                product.StockQuantity -= line.Quantity;
                order.OrderItems.Add(new OrderItem
                {
                    ProductId = line.ProductId, ProductName = line.ProductName, Quantity = line.Quantity, Price = line.UnitPrice,
                    OriginalUnitPrice = line.OriginalUnitPrice, AppliedDiscountId = line.DiscountId, DiscountName = line.DiscountName,
                    DiscountPercentage = line.DiscountPercentage, UnitDiscount = line.UnitDiscount
                });
            }
            db.ShoppingCartItems.RemoveRange(cart.Items); cart.Items.Clear(); cart.Version = Guid.NewGuid();
            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(View(order), customerId.HasValue ? null : TrackingToken(order, guestToken!), false);
        }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException ||
            exception is DbException busy && ExpectedConflict(busy) ||
            exception is DbUpdateException { InnerException: DbException database } && ExpectedConflict(database))
        {
            // Dispose rolls back any live transaction and also handles a SQL Server deadlock victim,
            // whose transaction the server has already rolled back. Clear it before replay lookup.
            await transaction.DisposeAsync();
            db.ChangeTracker.Clear();
            // A successful competing submission is durable. A later retry uses exactly this same lookup.
            if (owner is not null)
            {
                var prior = await Existing(owner, dto.IdempotencyKey.Value, ct);
                if (prior is not null) return Replay(prior, requestHash, guestToken);
            }
            throw new CartProblem(409, "CheckoutChanged");
        }
    }

    private static bool ExpectedConflict(DbException exception)
    {
        if (exception is SqlException sql) return sql.Number is 1205 or 1222 or 2601 or 2627;
        // SQLite is provided only by the test host, not a production API dependency.
        return exception.GetType().FullName == "Microsoft.Data.Sqlite.SqliteException" &&
            exception.GetType().GetProperty("SqliteErrorCode")?.GetValue(exception) is int code && code is 5 or 6 or 19;
    }
    private async Task<IDbContextTransaction> BeginConfirmation(CancellationToken ct)
    {
        try { return await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct); }
        catch (DbException exception) when (ExpectedConflict(exception)) { throw new CartProblem(409, "CheckoutChanged"); }
    }

    private Task<Order?> Existing(string owner, Guid key, CancellationToken ct) => db.Orders.AsNoTracking().Include(o => o.OrderItems)
        .SingleOrDefaultAsync(o => o.CheckoutOwner == owner && o.IdempotencyKey == key, ct);
    private CheckoutResult Replay(Order order, string hash, string? guestToken)
    {
        if (order.CheckoutRequestHash != hash) throw new CartProblem(409, "IdempotencyConflict");
        return new(View(order), order.CustomerId.HasValue ? null : TrackingToken(order, guestToken!), true);
    }
    private static string TrackingToken(Order order, string guestToken)
    {
        // Domain-separated HMAC provides a non-guessable, replay-recoverable 256-bit secret; only its hash is stored.
        // The original random guest-cart secret keeps replay stable even when the API signing key rotates.
        var key = Encoding.UTF8.GetBytes(guestToken);
        var data = Encoding.UTF8.GetBytes($"AdminPanel.GuestOrder.v1\n{order.CheckoutOwner}\n{order.IdempotencyKey:N}\n{order.Reference}");
        return WebEncoders.Base64UrlEncode(HMACSHA256.HashData(key, data));
    }
    public async Task<Order> GuestOrder(string? token, CancellationToken ct)
    {
        if (token is null || token.Length != 43 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new CartProblem(404, "OrderNotFound");
        var hash = CustomerSecurity.Hash(token);
        return await db.Orders.Include(o => o.OrderItems).ThenInclude(i => i.Product)
            .SingleOrDefaultAsync(o => o.CustomerId == null && o.GuestTrackingHash == hash, ct) ?? throw new CartProblem(404, "OrderNotFound");
    }
    public async Task<Order> CustomerOrder(int customerId, int id, CancellationToken ct) =>
        await db.Orders.Include(o => o.OrderItems).ThenInclude(i => i.Product)
            .SingleOrDefaultAsync(o => o.Id == id && o.CustomerId == customerId && o.Reference != null, ct)
        ?? throw new CartProblem(404, "OrderNotFound");
    public async Task<CustomerOrderPage> History(int customerId, int pageNumber, int pageSize, CancellationToken ct)
    {
        pageNumber = Math.Clamp(pageNumber, 1, 100000); pageSize = Math.Clamp(pageSize, 1, 50);
        var query = db.Orders.AsNoTracking().Where(o => o.CustomerId == customerId && o.Reference != null);
        var count = await query.CountAsync(ct);
        var orders = await query.Include(o => o.OrderItems).OrderByDescending(o => o.Id).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(orders.Select(View).ToList(), count, pageNumber, pageSize);
    }
    public static OrderView View(Order order) => new(order.Id, order.Reference ?? $"{order.Id}", order.Version, order.OrderDate, order.Status,
        order.ContactName ?? "", order.Phone ?? "", order.ContactEmail, order.Governorate ?? "", order.City ?? "", order.AddressLine ?? "", order.DeliveryInstructions,
        order.OrderItems.OrderBy(i => i.ProductId).Select(i => new CheckoutLine(i.ProductId, i.ProductName ?? i.Product?.Name ?? "", i.Quantity,
            i.OriginalUnitPrice ?? i.Price, i.AppliedDiscountId, i.DiscountName, i.DiscountPercentage ?? 0, i.UnitDiscount ?? 0, i.Price, i.Price * i.Quantity)).ToList(),
        order.Subtotal ?? order.TotalPrice, order.DiscountTotal ?? 0, order.ShippingFee ?? 0, order.TotalPrice,
        order.Currency ?? "YER", order.PaymentMethod ?? "COD", OrderLifecycle.CustomerCanCancel(order),
        OrderLifecycle.CustomerCanRequestCancellation(order) && order.CancellationRequestedAt == null, order.CancelledAt, order.CancellationReason,
        order.CancellationRequestedAt, order.CancellationRequestReason);
}
