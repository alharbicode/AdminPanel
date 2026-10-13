using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AdminPanel.API.Data;
using AdminPanel.API.DTOs;
using AdminPanel.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdminPanel.Tests;

public sealed class ShippingZoneMvcTests : IDisposable
{
    private readonly ApiFactory _api = new() { PublicProductIds = [1] };
    private readonly MvcFactory _mvc;
    public ShippingZoneMvcTests() { _api.Seed(); _mvc = new(_api); }
    public void Dispose() { _mvc.Dispose(); _api.Dispose(); }

    private static string Hidden(string html, string name) => WebUtility.HtmlDecode(Regex.Match(html,
        "name=\"" + name + "\"[^>]*value=\"([^\"]*)\"", RegexOptions.IgnoreCase).Groups[1].Value);
    private static async Task<HttpResponseMessage> Post(HttpClient client, string route, string page, Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync(page);
        fields["__RequestVerificationToken"] = Hidden(html, "__RequestVerificationToken");
        if (!fields.ContainsKey("Version") && !string.IsNullOrEmpty(Hidden(html, "Version")))
            fields["Version"] = Hidden(html, "Version");
        return await client.PostAsync(route, new FormUrlEncodedContent(fields));
    }
    private async Task<HttpClient> Login()
    {
        var client = _mvc.Client();
        using var response = await Post(client, "/Account/Login", "/Account/Login",
            new() { ["Username"] = "admin", ["Password"] = _api.Password });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }
    private static Dictionary<string, string> Zone(string governorate = "Test governorate", string city = "Test city") => new()
    {
        ["Governorate"] = governorate, ["City"] = city, ["Fee"] = "500.25", ["Enabled"] = "true"
    };
    private static async Task<ShippingZoneDto[]> Zones(HttpClient api) =>
        (await api.GetFromJsonAsync<PagedResult<ShippingZoneDto>>("/api/shippingzones/admin/list"))!.Items.ToArray();

    [Fact]
    public async Task Shipping_zone_forms_create_edit_disable_and_reenable_without_deleting_history()
    {
        using var client = await Login();
        using var api = await _api.AdminClient();
        using var created = await Post(client, "/ShippingZones/Create", "/ShippingZones/Create", Zone());
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var zone = Assert.Single(await Zones(api));
        Assert.True(zone.Enabled); Assert.Equal(500.25m, zone.Fee);
        Assert.Contains("Shipping zones", await client.GetStringAsync("/Dashboard"));
        var details = await client.GetStringAsync($"/ShippingZones/Details/{zone.Id}");
        Assert.Contains("Test governorate", details); Assert.Contains("500.25 YER", details);
        var fields = Zone("Test governorate", "");
        fields["Id"] = zone.Id.ToString(); fields["Fee"] = "650.50";
        using var edited = await Post(client, "/ShippingZones/Edit", $"/ShippingZones/Edit/{zone.Id}", fields);
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        zone = (await api.GetFromJsonAsync<ShippingZoneDto>($"/api/shippingzones/{zone.Id}"))!;
        Assert.Equal(650.50m, zone.Fee); Assert.Equal("", zone.City);
        Assert.Contains("Governorate-wide", await client.GetStringAsync($"/ShippingZones/Details/{zone.Id}"));
        Assert.Contains("Existing orders", await client.GetStringAsync($"/ShippingZones/Disable/{zone.Id}"));
        Assert.True((await api.GetFromJsonAsync<ShippingZoneDto>($"/api/shippingzones/{zone.Id}"))!.Enabled);
        using var disabled = await Post(client, "/ShippingZones/Disable", $"/ShippingZones/Disable/{zone.Id}",
            new() { ["Id"] = zone.Id.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, disabled.StatusCode);
        Assert.False(Assert.Single(await Zones(api)).Enabled);
        Assert.Empty((await api.GetFromJsonAsync<ShippingDestination[]>("/api/checkout/destinations"))!);
        Assert.Contains("Test governorate", await client.GetStringAsync("/ShippingZones?Available=false&Search=governorate"));
        fields.Remove("Version");
        using var enabled = await Post(client, "/ShippingZones/Edit", $"/ShippingZones/Edit/{zone.Id}", fields);
        Assert.Equal(HttpStatusCode.Redirect, enabled.StatusCode);
        Assert.Single((await api.GetFromJsonAsync<ShippingDestination[]>("/api/checkout/destinations"))!);
    }

    [Fact]
    public async Task Zone_validation_duplicate_and_stale_edits_keep_current_values()
    {
        using var client = await Login();
        using var api = await _api.AdminClient();
        var invalid = Zone(); invalid["Fee"] = "1.001";
        using var invalidResponse = await Post(client, "/ShippingZones/Create", "/ShippingZones/Create", invalid);
        Assert.Equal(HttpStatusCode.OK, invalidResponse.StatusCode);
        Assert.Contains("two decimal places", await invalidResponse.Content.ReadAsStringAsync());
        Assert.Empty(await Zones(api));
        using var created = await Post(client, "/ShippingZones/Create", "/ShippingZones/Create", Zone());
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        using var duplicate = await Post(client, "/ShippingZones/Create", "/ShippingZones/Create", Zone(" test GOVERNORATE ", "TEST CITY"));
        Assert.Contains("already exists", await duplicate.Content.ReadAsStringAsync());
        var zone = Assert.Single(await Zones(api));
        var stalePage = await client.GetStringAsync($"/ShippingZones/Edit/{zone.Id}");
        var staleVersion = Hidden(stalePage, "Version");
        zone.Fee = 777m;
        using var updated = await api.PutAsJsonAsync($"/api/shippingzones/{zone.Id}", zone);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        var stale = Zone(); stale["Id"] = zone.Id.ToString(); stale["Version"] = staleVersion;
        using var rejected = await Post(client, "/ShippingZones/Edit", $"/ShippingZones/Edit/{zone.Id}", stale);
        Assert.Contains("Reload before editing", await rejected.Content.ReadAsStringAsync());
        using var staleDisable = await Post(client, "/ShippingZones/Disable", $"/ShippingZones/Disable/{zone.Id}",
            new() { ["Id"] = zone.Id.ToString(), ["Version"] = staleVersion });
        Assert.Contains("Reload it before disabling", await staleDisable.Content.ReadAsStringAsync());
        var current = Assert.Single(await Zones(api));
        Assert.Equal(777m, current.Fee); Assert.True(current.Enabled); Assert.NotEqual(staleVersion, current.Version.ToString());
        current.Version = null;
        Assert.Equal(HttpStatusCode.Conflict, (await api.PutAsJsonAsync($"/api/shippingzones/{zone.Id}", current)).StatusCode);
    }

    [Fact]
    public async Task Shipping_zone_routes_enforce_Admin_and_all_MVC_writes_require_CSRF()
    {
        using var anonymous = _mvc.Client();
        using var apiAnonymous = _api.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await apiAnonymous.GetAsync("/api/shippingzones/admin/list")).StatusCode);
        foreach (var action in new[] { "", "/Create", "/Edit/1", "/Details/1", "/Disable/1" })
            Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync("/ShippingZones" + action)).StatusCode);
        foreach (var role in new[] { "staff", "customer" })
        {
            using var deniedApi = _api.Client();
            deniedApi.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _api.Login(deniedApi, role));
            Assert.Equal(HttpStatusCode.Forbidden, (await deniedApi.GetAsync("/api/shippingzones/admin/list")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await deniedApi.GetAsync("/api/shippingzones/1")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await deniedApi.PostAsJsonAsync("/api/shippingzones", new ShippingZoneDto())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await deniedApi.PutAsJsonAsync("/api/shippingzones/1", new ShippingZoneDto())).StatusCode);
            using var deniedMvc = _mvc.Client();
            using var login = await Post(deniedMvc, "/Account/Login", "/Account/Login",
                new() { ["Username"] = role, ["Password"] = _api.Password });
            Assert.Contains("administrator account is required", await login.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Redirect, (await deniedMvc.GetAsync("/ShippingZones")).StatusCode);
        }
        using var admin = await Login();
        foreach (var action in new[] { "Create", "Edit", "Disable" })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/ShippingZones/" + action, new FormUrlEncodedContent(Zone()))).StatusCode);
    }

    [Theory]
    [InlineData("Governorate")]
    [InlineData("City")]
    public async Task Destination_names_respect_database_limits_in_API_and_MVC(string field)
    {
        using var api = await _api.AdminClient();
        using var client = await Login();
        var dto = new ShippingZoneDto { Governorate = "Test governorate", City = "Test city", Fee = 500, Enabled = true };
        if (field == "Governorate") dto.Governorate = new string('G', 101);
        else dto.City = new string('C', 101);
        using var invalidApi = await api.PostAsJsonAsync("/api/shippingzones", dto);
        Assert.Equal(HttpStatusCode.BadRequest, invalidApi.StatusCode);
        var fields = Zone(); fields[field] = new string('X', 101);
        using var invalidMvc = await Post(client, "/ShippingZones/Create", "/ShippingZones/Create", fields);
        Assert.Equal(HttpStatusCode.OK, invalidMvc.StatusCode);
        Assert.Contains("maximum length of 100", await invalidMvc.Content.ReadAsStringAsync());
        Assert.Empty(await Zones(api));
        fields[field] = new string('X', 100);
        using var accepted = await Post(client, "/ShippingZones/Create", "/ShippingZones/Create", fields);
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.Single(await Zones(api));
    }

    [Fact]
    public async Task Administrator_cancellation_rejects_overlong_reason_before_changing_stock()
    {
        using var api = await _api.AdminClient();
        using var client = await Login();
        using var created = await api.PostAsJsonAsync("/api/orders", new
        {
            RequestKey = Guid.NewGuid(), CustomerId = 3, Items = new[] { new { ProductId = 1, Quantity = 2 } }
        });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetInt32();
        var order = (await api.GetFromJsonAsync<OrderDetailsDto>($"/api/orders/{id}"))!;
        using var invalidApi = await api.PostAsJsonAsync($"/api/orders/{id}/cancel",
            new { order.Version, Reason = new string('R', 501) });
        Assert.Equal(HttpStatusCode.BadRequest, invalidApi.StatusCode);
        using var invalidMvc = await Post(client, "/Orders/Cancel", $"/Orders/Details/{id}",
            new() { ["id"] = id.ToString(), ["reason"] = new string('R', 501) });
        Assert.Equal(HttpStatusCode.Redirect, invalidMvc.StatusCode);
        Assert.Contains("up to 500 characters", await client.GetStringAsync($"/Orders/Details/{id}"));
        Assert.Equal("Pending", (await api.GetFromJsonAsync<OrderDetailsDto>($"/api/orders/{id}"))!.Status);
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(8, (await db.Products.AsNoTracking().SingleAsync(p => p.Id == 1)).StockQuantity);
        using var accepted = await api.PostAsJsonAsync($"/api/orders/{id}/cancel",
            new { order.Version, Reason = new string('R', 500) });
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        var cancelled = (await api.GetFromJsonAsync<OrderDetailsDto>($"/api/orders/{id}"))!;
        Assert.Equal(500, cancelled.CancellationReason!.Length);
        Assert.Equal(10, (await db.Products.AsNoTracking().SingleAsync(p => p.Id == 1)).StockQuantity);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("email")]
    [InlineData("product")]
    public async Task Legacy_snapshot_values_exceeding_limits_are_rejected_before_admin_order_writes(string field)
    {
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (field == "customer") (await db.Users.FindAsync(3))!.Username = new string('N', 101);
            if (field == "email") (await db.Users.FindAsync(3))!.Email = new string('E', 255);
            if (field == "product") (await db.Products.FindAsync(1))!.Name = new string('P', 201);
            await db.SaveChangesAsync();
        }
        using var api = await _api.AdminClient();
        using var response = await api.PostAsJsonAsync("/api/orders", new
        {
            RequestKey = Guid.NewGuid(), CustomerId = 3, Items = new[] { new { ProductId = 1, Quantity = 2 } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("legacy", (await response.Content.ReadAsStringAsync()).ToLowerInvariant());
        using var verifyScope = _api.Services.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await verify.Orders.ToListAsync());
        Assert.Equal(10, (await verify.Products.FindAsync(1))!.StockQuantity);
    }

    [Theory]
    [InlineData("product", false)]
    [InlineData("selected-discount", false)]
    [InlineData("unselected-discount", true)]
    public async Task Checkout_checks_only_snapshot_names_that_would_be_stored(string field, bool allowed)
    {
        using var api = await _api.AdminClient();
        using var zoneResponse = await api.PostAsJsonAsync("/api/shippingzones",
            new ShippingZoneDto { Governorate = "Test governorate", City = "Test city", Fee = 500, Enabled = true });
        zoneResponse.EnsureSuccessStatusCode();
        var zone = Assert.Single(await Zones(api));
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = _api.Clock.GetUtcNow().UtcDateTime;
            if (field == "product") (await db.Products.FindAsync(1))!.Name = new string('P', 201);
            if (field == "selected-discount")
                db.Discounts.Add(new Discount { Name = new string('D', 201), Percentage = 20, ProductId = 1, StartDate = now.AddDays(-1), EndDate = now.AddDays(1) });
            if (field == "unselected-discount")
                db.Discounts.AddRange(
                    new Discount { Name = "Usable discount", Percentage = 20, ProductId = 1, StartDate = now.AddDays(-1), EndDate = now.AddDays(1) },
                    new Discount { Name = new string('D', 201), Percentage = 10, ProductId = 1, StartDate = now.AddDays(-1), EndDate = now.AddDays(1) },
                    new Discount { Name = new string('D', 201), Percentage = 50, ProductId = 1, StartDate = now.AddDays(-2), EndDate = now.AddDays(-1) });
            await db.SaveChangesAsync();
        }
        using var guest = _api.Client();
        using var guestResponse = await guest.PostAsync("/api/guest/cart", null);
        var created = (await guestResponse.Content.ReadFromJsonAsync<GuestCartResult>())!;
        guest.DefaultRequestHeaders.Add("X-Guest-Cart", created.Token);
        using var added = await guest.PostAsJsonAsync("/api/guest/cart/items/1", new { created.Cart.Version, Quantity = 2 });
        var cart = (await added.Content.ReadFromJsonAsync<CartView>())!;
        using var quoted = await guest.PostAsJsonAsync("/api/guest/checkout/quote", new CheckoutQuoteDto
        {
            CartVersion = cart.Version, ShippingZoneId = zone.Id, ContactName = "Fixture buyer",
            Phone = "+967700123456", City = "Test city", AddressLine = "Test street 5"
        });
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.BadRequest, quoted.StatusCode);
        if (allowed) Assert.Equal("Usable discount", (await quoted.Content.ReadFromJsonAsync<CheckoutQuote>())!.Items.Single().DiscountName);
        using var verifyScope = _api.Services.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await verify.Orders.ToListAsync());
        Assert.Equal(10, (await verify.Products.FindAsync(1))!.StockQuantity);
    }

    [Fact]
    public async Task New_guest_order_snapshots_render_in_admin_after_product_discount_and_shipping_edits()
    {
        using var api = await _api.AdminClient();
        using var zoneResponse = await api.PostAsJsonAsync("/api/shippingzones",
            new ShippingZoneDto { Governorate = "Test governorate", City = "Test city", Fee = 500, Enabled = true });
        zoneResponse.EnsureSuccessStatusCode();
        var zone = Assert.Single(await Zones(api));
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Discounts.Add(new Discount { Name = "Original discount", ProductId = 1, Percentage = 20,
                StartDate = _api.Clock.GetUtcNow().UtcDateTime.AddDays(-1), EndDate = _api.Clock.GetUtcNow().UtcDateTime.AddDays(1) });
            await db.SaveChangesAsync();
        }
        using var guest = _api.Client();
        using var guestResponse = await guest.PostAsync("/api/guest/cart", null);
        var createdCart = (await guestResponse.Content.ReadFromJsonAsync<GuestCartResult>())!;
        guest.DefaultRequestHeaders.Add("X-Guest-Cart", createdCart.Token);
        using var addResponse = await guest.PostAsJsonAsync("/api/guest/cart/items/1", new { createdCart.Cart.Version, Quantity = 2 });
        var cart = (await addResponse.Content.ReadFromJsonAsync<CartView>())!;
        var request = new CheckoutConfirmDto
        {
            CartVersion = cart.Version, ShippingZoneId = zone.Id, ContactName = "Snapshot guest",
            Phone = "+967700123456", City = "Test city", AddressLine = "Original street <script>unsafe</script>",
            DeliveryInstructions = "Snapshot delivery instructions", IdempotencyKey = Guid.NewGuid()
        };
        using var quoteResponse = await guest.PostAsJsonAsync("/api/guest/checkout/quote", request);
        quoteResponse.EnsureSuccessStatusCode();
        request.QuoteFingerprint = (await quoteResponse.Content.ReadFromJsonAsync<CheckoutQuote>())!.QuoteFingerprint;
        using var placedResponse = await guest.PostAsJsonAsync("/api/guest/checkout/confirm", request);
        placedResponse.EnsureSuccessStatusCode();
        var order = (await placedResponse.Content.ReadFromJsonAsync<CheckoutResult>())!.Order;
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var product = (await db.Products.FindAsync(1))!; product.Name = "Changed product"; product.Price = 99m;
            db.Discounts.RemoveRange(db.Discounts);
            (await db.ShippingZones.FindAsync(zone.Id))!.Fee = 999m;
            await db.SaveChangesAsync();
        }
        using var client = await Login();
        using var detailsResponse = await client.GetAsync($"/Orders/Details/{order.Id}");
        Assert.True(detailsResponse.Headers.CacheControl!.NoStore);
        var html = await detailsResponse.Content.ReadAsStringAsync();
        Assert.Contains("Snapshot guest", html); Assert.Contains("guest checkout", html);
        Assert.Contains("Test product", html); Assert.DoesNotContain("Changed product", html);
        Assert.Contains("Original discount", html); Assert.Contains("500.00", html);
        Assert.Contains("520.00 YER", html); Assert.Contains("Cash on Delivery (COD)", html);
        Assert.Contains("&lt;script&gt;unsafe&lt;/script&gt;", html); Assert.DoesNotContain("<script>unsafe", html);
        Assert.Contains("Snapshot delivery instructions", html);
        Assert.Contains("Snapshot guest", await client.GetStringAsync("/Orders?Search=Snapshot"));
        Assert.Contains("Snapshot guest", await client.GetStringAsync("/Dashboard"));
        Assert.Contains("Confirmed", html); Assert.DoesNotContain("<option value=\"Delivered\"", html);
        using var cancelled = await Post(client, "/Orders/Cancel", $"/Orders/Details/{order.Id}",
            new() { ["id"] = order.Id.ToString(), ["reason"] = "Explicit administrator review" });
        Assert.Equal(HttpStatusCode.Redirect, cancelled.StatusCode);
        var retained = await client.GetStringAsync($"/Orders/Details/{order.Id}");
        Assert.Contains("Cancellation record", retained); Assert.Contains("Explicit administrator review", retained);
        Assert.DoesNotContain("Cancel Order</button>", retained); Assert.Contains("Original discount", retained);
        using var verifyScope = _api.Services.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(10, (await verify.Products.FindAsync(1))!.StockQuantity);
        Assert.Single(await verify.Orders.ToListAsync());
    }
}
