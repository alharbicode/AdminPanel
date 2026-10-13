using System.Net;
using System.Net.Http.Json;
using AdminPanel.API.Data;
using AdminPanel.API.DTOs;
using AdminPanel.API.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdminPanel.Tests;

public sealed class CheckoutBoundaryTests : IDisposable
{
    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class BoundaryFactory : ApiFactory
    {
        public FixedClock Fixed { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(Fixed));
        }
    }
    private readonly BoundaryFactory _api = new() { PublicProductIds = [1] };
    public CheckoutBoundaryTests()
    {
        _api.Seed();
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.ShippingZones.Add(new ShippingZone { Governorate = "Sana'a", City = "", NormalizedDestination = "SANA'A|", Fee = 500m, Enabled = true });
        db.SaveChanges();
    }
    public void Dispose() => _api.Dispose();
    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        using (response) { response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<T>())!; }
    }
    private async Task<(HttpClient Client, CheckoutQuoteDto Request)> Guest(bool add = true)
    {
        var client = _api.Client();
        var guest = await Read<GuestCartResult>(await client.PostAsync("/api/guest/cart", null));
        client.DefaultRequestHeaders.Add("X-Guest-Cart", guest.Token);
        var cart = add ? await Read<CartView>(await client.PostAsJsonAsync("/api/guest/cart/items/1", new { guest.Cart.Version, Quantity = 2 })) : guest.Cart;
        return (client, new CheckoutQuoteDto
        {
            CartVersion = cart.Version, ShippingZoneId = 1, ContactName = "Boundary customer", Phone = "+967 700123456",
            City = "Sana'a", AddressLine = "Disposable test address"
        });
    }
    private async Task Unchanged()
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(10, (await db.Products.FindAsync(1))!.StockQuantity);
        Assert.Empty(await db.Orders.ToListAsync());
    }

    [Fact]
    public async Task Discount_start_is_inclusive_end_is_exclusive_and_full_discount_preserves_shipping()
    {
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = _api.Fixed.Now.UtcDateTime;
            db.Discounts.AddRange(
                new Discount { Name = "Ends exactly now", ProductId = 1, Percentage = 100, StartDate = now.AddDays(-1), EndDate = now },
                new Discount { Name = "Starts exactly now", ProductId = 1, Percentage = 100, StartDate = now, EndDate = now.AddDays(1) },
                new Discount { Name = "Starts later", ProductId = 1, Percentage = 100, StartDate = now.AddTicks(1), EndDate = now.AddDays(1) });
            await db.SaveChangesAsync();
        }
        var (client, request) = await Guest(); using var owner = client;
        var quote = await Read<CheckoutQuote>(await client.PostAsJsonAsync("/api/guest/checkout/quote", request));
        Assert.Equal("Starts exactly now", Assert.Single(quote.Items).DiscountName);
        Assert.Equal(0m, quote.Items.Single().UnitPrice); Assert.Equal(25m, quote.DiscountTotal);
        Assert.Equal(500m, quote.Total); await Unchanged();
    }

    [Theory]
    [InlineData("name")]
    [InlineData("phone")]
    [InlineData("phone-digits")]
    [InlineData("email")]
    [InlineData("address")]
    [InlineData("city")]
    [InlineData("version")]
    public async Task Invalid_contact_and_missing_cart_version_are_rejected_before_writes(string invalid)
    {
        var (client, request) = await Guest(); using var owner = client;
        switch (invalid)
        {
            case "name": request.ContactName = " "; break;
            case "phone": request.Phone = "not a telephone"; break;
            case "phone-digits": request.Phone = "1      "; break;
            case "email": request.Email = "invalid-address"; break;
            case "address": request.AddressLine = "ab"; break;
            case "city": request.City = " "; break;
            case "version": request.CartVersion = null; break;
        }
        using var response = await client.PostAsJsonAsync("/api/guest/checkout/quote", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); await Unchanged();
    }

    [Theory]
    [InlineData("missing-key")]
    [InlineData("empty-key")]
    [InlineData("missing-fingerprint")]
    [InlineData("changed-fingerprint")]
    public async Task Confirmation_requires_nonempty_durable_key_and_matching_review(string invalid)
    {
        var (client, request) = await Guest(); using var owner = client;
        var quote = await Read<CheckoutQuote>(await client.PostAsJsonAsync("/api/guest/checkout/quote", request));
        var body = new CheckoutConfirmDto
        {
            CartVersion = request.CartVersion, ShippingZoneId = 1, ContactName = request.ContactName, Phone = request.Phone,
            City = request.City, AddressLine = request.AddressLine, IdempotencyKey = Guid.NewGuid(), QuoteFingerprint = quote.QuoteFingerprint
        };
        switch (invalid)
        {
            case "missing-key": body.IdempotencyKey = null; break;
            case "empty-key": body.IdempotencyKey = Guid.Empty; break;
            case "missing-fingerprint": body.QuoteFingerprint = ""; break;
            case "changed-fingerprint": body.QuoteFingerprint = new string('A', 64); break;
        }
        using var response = await client.PostAsJsonAsync("/api/guest/checkout/confirm", body);
        Assert.Equal(invalid == "changed-fingerprint" ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest, response.StatusCode);
        await Unchanged();
    }

    [Fact]
    public async Task Empty_cart_and_random_guest_tracking_secret_do_not_disclose_orders()
    {
        var (client, request) = await Guest(add: false); using var owner = client;
        using var response = await client.PostAsJsonAsync("/api/guest/checkout/quote", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("CartEmpty", await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add("X-Guest-Order", new string('A', 43));
        using var lookup = await client.PostAsync("/api/guest/orders/lookup", null);
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
        Assert.DoesNotContain("AddressLine", await lookup.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        await Unchanged();
    }

    [Fact]
    public async Task Administrator_and_customer_token_audiences_cannot_cross_checkout_permissions()
    {
        using var admin = await _api.AdminClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/customer/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.PostAsJsonAsync("/api/customer/checkout/quote", new { })).StatusCode);
        using var anonymous = _api.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/customer/orders")).StatusCode);
        var login = await Read<CustomerLoginResult>(await anonymous.PostAsJsonAsync("/api/customer/account/register",
            new { DisplayName = "Buyer", Email = "boundary@example.test", Password = _api.Password }));
        anonymous.DefaultRequestHeaders.Authorization = new("Bearer", login.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/shippingzones/admin/list")).StatusCode);
        await Unchanged();
    }

    [Fact]
    public async Task Explicit_cart_refresh_repairs_stored_availability_without_reserving_stock()
    {
        var (client, request) = await Guest(); using var owner = client;
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Products.Find(1)!.StockQuantity = 1;
            var hidden = new Product { Name = "Hidden product", CategoryId = 1, Price = 10m, StockQuantity = 5, IsAvailable = false };
            db.Products.Add(hidden);
            db.ShoppingCartItems.Add(new ShoppingCartItem { CartId = (await db.ShoppingCarts.SingleAsync()).Id, Product = hidden, Quantity = 2 });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/guest/checkout/quote", request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/guest/cart/refresh", new { })).StatusCode);
        var refreshed = await Read<CartView>(await client.PostAsJsonAsync("/api/guest/cart/refresh", new { Version = request.CartVersion }));
        Assert.True(refreshed.Adjusted); Assert.Equal(1, Assert.Single(refreshed.Items).Quantity);
        Assert.NotEqual(request.CartVersion, refreshed.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/guest/cart/refresh", new { Version = request.CartVersion })).StatusCode);
        request.CartVersion = refreshed.Version;
        var quote = await Read<CheckoutQuote>(await client.PostAsJsonAsync("/api/guest/checkout/quote", request));
        Assert.Equal(1, quote.Items.Single().Quantity);
        using var check = _api.Services.CreateScope();
        var context = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, (await context.Products.FindAsync(1))!.StockQuantity);
        Assert.Equal(5, (await context.Products.FindAsync(2))!.StockQuantity);
        Assert.Single(await context.ShoppingCartItems.ToListAsync()); Assert.Empty(await context.Orders.ToListAsync());
    }
}
