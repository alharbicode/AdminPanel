using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdminPanel.API.Data;
using AdminPanel.API.DTOs;
using AdminPanel.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdminPanel.Tests;

public sealed class CheckoutTests : IDisposable
{
    private readonly ApiFactory _api = new() { PublicProductIds = [1] };
    public CheckoutTests()
    {
        _api.Seed();
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.ShippingZones.AddRange(
            new ShippingZone { Governorate = "Sana'a", City = "Sana'a", NormalizedDestination = "SANA'A|SANA'A", Fee = 500m, Enabled = true },
            new ShippingZone { Governorate = "Taiz", City = "", NormalizedDestination = "TAIZ|", Fee = 700m, Enabled = true },
            new ShippingZone { Governorate = "Aden", City = "Aden", NormalizedDestination = "ADEN|ADEN", Fee = 200m, Enabled = false });
        db.SaveChanges();
    }
    public void Dispose() => _api.Dispose();
    private async Task<(HttpClient Client, CartView Cart, string Token)> Guest(int quantity = 2)
    {
        var client = _api.Client();
        var created = await Read<GuestCartResult>(await client.PostAsync("/api/guest/cart", null));
        client.DefaultRequestHeaders.Add("X-Guest-Cart", created.Token);
        var cart = await Read<CartView>(await client.PostAsJsonAsync("/api/guest/cart/items/1", new { created.Cart.Version, Quantity = quantity }));
        return (client, cart, created.Token);
    }
    private async Task<(HttpClient Client, CartView Cart)> Customer(string email = "buyer@example.test", int quantity = 2)
    {
        var client = _api.Client();
        var login = await Read<CustomerLoginResult>(await client.PostAsJsonAsync("/api/customer/account/register",
            new { Email = email, DisplayName = "Buyer", Password = _api.Password }));
        client.DefaultRequestHeaders.Authorization = new("Bearer", login.Token);
        var empty = (await client.GetFromJsonAsync<CartView>("/api/customer/cart"))!;
        var cart = await Read<CartView>(await client.PostAsJsonAsync("/api/customer/cart/items/1", new { empty.Version, Quantity = quantity }));
        return (client, cart);
    }
    private static CheckoutQuoteDto Request(CartView cart, int zone = 1, string city = "Sana'a") => new()
    {
        CartVersion = cart.Version, ShippingZoneId = zone, ContactName = "Checkout buyer", Phone = "+967 700123456",
        Email = "buyer@example.test", City = city, AddressLine = "Test street, building 5", DeliveryInstructions = "Fixture only"
    };
    private static CheckoutConfirmDto ConfirmRequest(CheckoutQuoteDto request, CheckoutQuote quote, Guid? key = null) => new()
    {
        CartVersion = request.CartVersion, ShippingZoneId = request.ShippingZoneId, ContactName = request.ContactName,
        Phone = request.Phone, Email = request.Email, City = request.City, AddressLine = request.AddressLine,
        DeliveryInstructions = request.DeliveryInstructions, QuoteFingerprint = quote.QuoteFingerprint, IdempotencyKey = key ?? Guid.NewGuid()
    };
    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        using (response) { response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<T>())!; }
    }
    private async Task Stock(int expected, int orders)
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(expected, (await db.Products.FindAsync(1))!.StockQuantity);
        Assert.Equal(orders, await db.Orders.CountAsync());
    }

    [Fact]
    public async Task Guest_checkout_is_authoritative_idempotent_and_retains_immutable_snapshots()
    {
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = _api.Clock.GetUtcNow().UtcDateTime;
            db.Discounts.AddRange(new Discount { Name = "Low", Percentage = 10, ProductId = 1, StartDate = now.AddDays(-1), EndDate = now.AddDays(1) },
                new Discount { Name = "Best", Percentage = 20, ProductId = 1, StartDate = now.AddDays(-1), EndDate = now.AddDays(1) });
            await db.SaveChangesAsync();
        }
        var (client, cart, _) = await Guest(); using var owner = client;
        var request = Request(cart);
        var quote = await Read<CheckoutQuote>(await client.PostAsJsonAsync("/api/guest/checkout/quote", request));
        Assert.Equal(25m, quote.Subtotal); Assert.Equal(5m, quote.DiscountTotal); Assert.Equal(500m, quote.ShippingFee);
        Assert.Equal(520m, quote.Total); Assert.Equal("Best", quote.Items.Single().DiscountName);
        var body = ConfirmRequest(request, quote);
        var json = JsonSerializer.SerializeToNode(body)!; json["Total"] = 0; json["ShippingFee"] = 0; json["CustomerId"] = 1;
        var placed = await Read<CheckoutResult>(await client.PostAsJsonAsync("/api/guest/checkout/confirm", json));
        Assert.Equal(520m, placed.Order.Total); Assert.Equal("YER", placed.Order.Currency); Assert.Equal("COD", placed.Order.PaymentMethod);
        Assert.NotNull(placed.GuestTrackingToken); Assert.Equal(43, placed.GuestTrackingToken.Length);
        await Stock(8, 1);
        Assert.Empty((await client.GetFromJsonAsync<CartView>("/api/guest/cart"))!.Items);
        var replay = await Read<CheckoutResult>(await client.PostAsJsonAsync("/api/guest/checkout/confirm", body));
        Assert.True(replay.Replayed); Assert.Equal(placed.Order.Id, replay.Order.Id); Assert.Equal(placed.GuestTrackingToken, replay.GuestTrackingToken);
        body.AddressLine = "Changed request should conflict";
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/guest/checkout/confirm", body)).StatusCode);
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Null((await db.Orders.SingleAsync()).CustomerId);
            Assert.NotEqual(placed.GuestTrackingToken, (await db.Orders.SingleAsync()).GuestTrackingHash);
            var product = (await db.Products.FindAsync(1))!; product.Name = "Changed later"; product.Price = 100m;
            db.Discounts.RemoveRange(db.Discounts); db.ShippingZones.Find(1)!.Fee = 999m;
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Add("X-Guest-Order", placed.GuestTrackingToken);
        var tracked = await Read<OrderView>(await client.PostAsync("/api/guest/orders/lookup", null));
        Assert.Equal("Test product", tracked.Items.Single().ProductName); Assert.Equal(520m, tracked.Total);
        Assert.Equal(5m, tracked.DiscountTotal); Assert.Equal("Test street, building 5", tracked.AddressLine);
        await Stock(8, 1);
    }

    [Theory]
    [InlineData(3, "Aden")]
    [InlineData(999, "Unknown")]
    [InlineData(1, "Different city")]
    public async Task Unsupported_or_disabled_destinations_cannot_checkout(int zone, string city)
    {
        var (client, cart, _) = await Guest(); using var owner = client;
        var destinations = (await client.GetFromJsonAsync<ShippingDestination[]>("/api/checkout/destinations"))!;
        Assert.DoesNotContain(destinations, z => z.Id == 3);
        var response = await client.PostAsJsonAsync("/api/guest/checkout/quote", Request(cart, zone, city));
        Assert.False(response.IsSuccessStatusCode); await Stock(10, 0);
    }

    [Fact]
    public async Task Governorate_wide_zone_uses_configured_fee_and_customer_city_snapshot()
    {
        var (client, cart, _) = await Guest(); using var owner = client;
        var request = Request(cart, 2, "Al Turbah");
        var quote = await Read<CheckoutQuote>(await client.PostAsJsonAsync("/api/guest/checkout/quote", request));
        Assert.Equal("Taiz", quote.Governorate); Assert.Equal("Al Turbah", quote.City); Assert.Equal(700m, quote.ShippingFee);
        Assert.Equal(725m, quote.Total);
    }

    [Theory]
    [InlineData("price")]
    [InlineData("discount")]
    [InlineData("shipping")]
    [InlineData("disabled")]
    [InlineData("stock")]
    [InlineData("hidden")]
    [InlineData("cart")]
    public async Task Changed_checkout_requires_review_and_never_partially_writes(string change)
    {
        var (client, cart, _) = await Guest(); using var owner = client;
        var request = Request(cart);
        var quote = await Read<CheckoutQuote>(await client.PostAsJsonAsync("/api/guest/checkout/quote", request));
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var product = (await db.Products.FindAsync(1))!;
            switch (change)
            {
                case "price": product.Price += 1; break;
                case "discount": db.Discounts.Add(new Discount { Name = "New sale", ProductId = 1, Percentage = 10, StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(1) }); break;
                case "shipping": db.ShippingZones.Find(1)!.Fee += 1; break;
                case "disabled": db.ShippingZones.Find(1)!.Enabled = false; break;
                case "stock": product.StockQuantity = 1; break;
                case "hidden": product.IsAvailable = false; break;
                case "cart": var c = await db.ShoppingCarts.SingleAsync(); c.Version = Guid.NewGuid(); break;
            }
            await db.SaveChangesAsync();
        }
        var response = await client.PostAsJsonAsync("/api/guest/checkout/confirm", ConfirmRequest(request, quote));
        Assert.True(response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest);
        await Stock(change == "stock" ? 1 : 10, 0);
    }

    [Fact]
    public async Task Discounts_ignore_invalid_expired_future_rows_and_round_once_per_unit()
    {
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = _api.Clock.GetUtcNow().UtcDateTime;
            db.Products.Find(1)!.Price = 12.55m;
            db.Discounts.AddRange(new Discount { Name = "Expired", Percentage = 90, ProductId = 1, StartDate = now.AddDays(-2), EndDate = now.AddDays(-1) },
                new Discount { Name = "Future", Percentage = 90, ProductId = 1, StartDate = now.AddDays(1), EndDate = now.AddDays(2) },
                new Discount { Name = "Invalid", Percentage = 110, ProductId = 1, StartDate = now.AddDays(-1), EndDate = now.AddDays(1) },
                new Discount { Name = "First tie", Percentage = 10, ProductId = 1, StartDate = now.AddDays(-1), EndDate = now.AddDays(1) },
                new Discount { Name = "Second tie", Percentage = 10, ProductId = 1, StartDate = now.AddDays(-1), EndDate = now.AddDays(1) });
            await db.SaveChangesAsync();
        }
        var (client, cart, _) = await Guest(3); using var owner = client;
        var quote = await Read<CheckoutQuote>(await client.PostAsJsonAsync("/api/guest/checkout/quote", Request(cart)));
        Assert.Equal("First tie", quote.Items.Single().DiscountName); Assert.Equal(1.26m, quote.Items.Single().UnitDiscount);
        Assert.Equal(3.78m, quote.DiscountTotal); Assert.Equal(533.87m, quote.Total);
        await Stock(10, 0);
    }

    [Fact]
    public async Task Concurrent_buyers_cannot_oversell_and_retries_are_durable()
    {
        var first = await Guest(8); var second = await Guest(8);
        using var a = first.Client; using var b = second.Client;
        var ra = Request(first.Cart); var rb = Request(second.Cart);
        var qa = await Read<CheckoutQuote>(await a.PostAsJsonAsync("/api/guest/checkout/quote", ra));
        var qb = await Read<CheckoutQuote>(await b.PostAsJsonAsync("/api/guest/checkout/quote", rb));
        var ba = ConfirmRequest(ra, qa); var bb = ConfirmRequest(rb, qb);
        var results = await Task.WhenAll(a.PostAsJsonAsync("/api/guest/checkout/confirm", ba), b.PostAsJsonAsync("/api/guest/checkout/confirm", bb));
        Assert.Single(results, r => r.IsSuccessStatusCode);
        Assert.All(results.Where(r => !r.IsSuccessStatusCode), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        await Stock(2, 1);
        var winner = results[0].IsSuccessStatusCode ? a : b; var body = results[0].IsSuccessStatusCode ? ba : bb;
        var replay = await Read<CheckoutResult>(await winner.PostAsJsonAsync("/api/guest/checkout/confirm", body));
        Assert.True(replay.Replayed); await Stock(2, 1);
    }

    [Fact]
    public async Task Concurrent_duplicate_confirmation_creates_one_order_and_finalizes_once()
    {
        var (client, cart, _) = await Guest(); using var owner = client;
        var request = Request(cart); var quote = await Read<CheckoutQuote>(await client.PostAsJsonAsync("/api/guest/checkout/quote", request));
        var body = ConfirmRequest(request, quote);
        var responses = await Task.WhenAll(client.PostAsJsonAsync("/api/guest/checkout/confirm", body), client.PostAsJsonAsync("/api/guest/checkout/confirm", body));
        Assert.Contains(responses, r => r.IsSuccessStatusCode);
        Assert.All(responses.Where(r => !r.IsSuccessStatusCode), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        var replay = await Read<CheckoutResult>(await client.PostAsJsonAsync("/api/guest/checkout/confirm", body));
        Assert.True(replay.Replayed); await Stock(8, 1);
    }

    [Fact]
    public async Task Registered_orders_are_private_and_customer_cancellation_restores_exactly_once()
    {
        var (client, cart) = await Customer(); using var owner = client;
        var request = Request(cart); var quote = await Read<CheckoutQuote>(await client.PostAsJsonAsync("/api/customer/checkout/quote", request));
        var placed = await Read<CheckoutResult>(await client.PostAsJsonAsync("/api/customer/checkout/confirm", ConfirmRequest(request, quote)));
        Assert.Null(placed.GuestTrackingToken);
        var other = await Customer("other@example.test"); using var stranger = other.Client;
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync("/api/customer/orders/" + placed.Order.Id)).StatusCode);
        Assert.Empty((await stranger.GetFromJsonAsync<CustomerOrderPage>("/api/customer/orders"))!.Items);
        Assert.Single((await client.GetFromJsonAsync<CustomerOrderPage>("/api/customer/orders"))!.Items);
        var cancel = new OrderActionDto { Version = placed.Order.Version, Reason = "Changed mind" };
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/customer/orders/{placed.Order.Id}/cancel", cancel)).StatusCode);
        var cancellations = await Task.WhenAll(client.PostAsJsonAsync($"/api/customer/orders/{placed.Order.Id}/cancel", cancel), client.PostAsJsonAsync($"/api/customer/orders/{placed.Order.Id}/cancel", cancel));
        Assert.Contains(cancellations, r => r.IsSuccessStatusCode);
        Assert.All(cancellations.Where(r => !r.IsSuccessStatusCode), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        (await client.PostAsJsonAsync($"/api/customer/orders/{placed.Order.Id}/cancel", cancel)).EnsureSuccessStatusCode();
        await Stock(10, 1);
        var saved = (await client.GetFromJsonAsync<OrderView>($"/api/customer/orders/{placed.Order.Id}"))!;
        Assert.Equal("Cancelled", saved.Status); Assert.Equal("Changed mind", saved.CancellationReason); Assert.NotNull(saved.CancelledAt);
        Assert.Single(saved.Items);
    }

    [Fact]
    public async Task Guest_tracking_requires_secret_and_cancellation_after_preparing_becomes_request()
    {
        var (client, cart, _) = await Guest(); using var owner = client;
        var request = Request(cart); var quote = await Read<CheckoutQuote>(await client.PostAsJsonAsync("/api/guest/checkout/quote", request));
        var placed = await Read<CheckoutResult>(await client.PostAsJsonAsync("/api/guest/checkout/confirm", ConfirmRequest(request, quote)));
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/guest/orders/lookup", null)).StatusCode);
        using var admin = await _api.AdminClient();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/orders/{placed.Order.Id}/status", "Delivered")).StatusCode);
        (await admin.PutAsJsonAsync($"/api/orders/{placed.Order.Id}/status", "Confirmed")).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/orders/{placed.Order.Id}/status", "Preparing")).EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add("X-Guest-Order", placed.GuestTrackingToken);
        var prepared = await Read<OrderView>(await client.PostAsync("/api/guest/orders/lookup", null));
        Assert.False(prepared.CanCancel); Assert.True(prepared.CanRequestCancellation);
        var action = new OrderActionDto { Version = prepared.Version, Reason = "Please contact me" };
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/guest/orders/cancel", action)).StatusCode);
        (await client.PostAsJsonAsync("/api/guest/orders/cancellation-request", action)).EnsureSuccessStatusCode();
        await Stock(8, 1);
        var details = await admin.GetFromJsonAsync<JsonElement>($"/api/orders/{placed.Order.Id}");
        Assert.Equal("Please contact me", details.GetProperty("cancellationRequestReason").GetString());
        (await admin.DeleteAsync($"/api/orders/{placed.Order.Id}")).EnsureSuccessStatusCode();
        (await admin.DeleteAsync($"/api/orders/{placed.Order.Id}")).EnsureSuccessStatusCode();
        await Stock(10, 1);
    }

    [Theory]
    [InlineData("Shipped")]
    [InlineData("Delivered")]
    public async Task Shipped_and_delivered_orders_cannot_restore_stock_through_cancel(string terminal)
    {
        var (client, cart, _) = await Guest(); using var owner = client;
        var request = Request(cart); var quote = await Read<CheckoutQuote>(await client.PostAsJsonAsync("/api/guest/checkout/quote", request));
        var order = (await Read<CheckoutResult>(await client.PostAsJsonAsync("/api/guest/checkout/confirm", ConfirmRequest(request, quote)))).Order;
        using var admin = await _api.AdminClient();
        foreach (var state in new[] { "Confirmed", "Preparing", "Shipped" })
            (await admin.PutAsJsonAsync($"/api/orders/{order.Id}/status", state)).EnsureSuccessStatusCode();
        if (terminal == "Delivered") (await admin.PutAsJsonAsync($"/api/orders/{order.Id}/status", terminal)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/orders/{order.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/orders/{order.Id}/status", "Pending")).StatusCode);
        await Stock(8, 1);
    }
}
