using System.Net;
using System.Text.RegularExpressions;
using AdminPanel.API.Data;
using AdminPanel.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdminPanel.Tests;

public sealed class StorefrontCheckoutMvcTests : IDisposable
{
    private readonly ApiFactory _api = new() { PublicProductIds = [1] };
    private readonly StorefrontFactory _store;
    public StorefrontCheckoutMvcTests()
    {
        _api.Seed();
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.ShippingZones.Add(new ShippingZone { Governorate = "Test governorate", City = "Test city", Fee = 7.50m,
            Enabled = true, NormalizedDestination = "TEST GOVERNORATE\nTEST CITY" });
        db.SaveChanges();
        _store = new(_api);
    }
    public void Dispose() { _store.Dispose(); _api.Dispose(); }
    private static string Field(string html, string name)
    {
        var tag = Regex.Match(html, "<input\\b(?=[^>]*\\bname=\"" + Regex.Escape(name) + "\")[^>]*>", RegexOptions.IgnoreCase).Value;
        return WebUtility.HtmlDecode(Regex.Match(tag, "value=\"([^\"]*)\"").Groups[1].Value);
    }
    private static string Csrf(string html) => Field(html, "__RequestVerificationToken");
    private static Task<HttpResponseMessage> Post(HttpClient client, string path, string html, params (string Key, string Value)[] values) =>
        client.PostAsync(path, new FormUrlEncodedContent(values.Append(("__RequestVerificationToken", Csrf(html))).ToDictionary(v => v.Item1, v => v.Item2)));
    private static async Task Add(HttpClient client)
    {
        using var added = await Post(client, "/Cart/Add", await client.GetStringAsync("/"), ("ProductId", "1"), ("Quantity", "2"));
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
    }
    private static async Task<string> Review(HttpClient client)
    {
        var page = await client.GetStringAsync("/Checkout");
        using var review = await Post(client, "/Checkout/Review", page,
            ("CartVersion", Field(page, "CartVersion")), ("IdempotencyKey", Field(page, "IdempotencyKey")),
            ("ShippingZoneId", "1"), ("ContactName", "Private recipient <script>x</script>"), ("Phone", "+967 777123456"),
            ("Email", "recipient@example.test"), ("City", "Test city"), ("AddressLine", "Private test street 123"),
            ("DeliveryInstructions", "Ring once"));
        review.EnsureSuccessStatusCode();
        var html = await review.Content.ReadAsStringAsync();
        Assert.NotEmpty(Field(html, "reviewToken"));
        Assert.DoesNotContain("<script>x</script>", html);
        return html;
    }
    private static Task<HttpResponseMessage> Confirm(HttpClient client, string review) => Post(client, "/Checkout/Confirm", review, ("reviewToken", Field(review, "reviewToken")));
    private static async Task Register(HttpClient client, string email, string password)
    {
        var page = await client.GetStringAsync("/Account/Register");
        using var response = await Post(client, "/Account/Register", page, ("DisplayName", "Customer"), ("Email", email), ("Password", password), ("ConfirmPassword", password));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Guest_checkout_keeps_tracking_private_preserves_snapshots_and_requires_csrf()
    {
        using var client = _store.Client();
        foreach (var path in new[] { "/Checkout/Review", "/Checkout/Edit", "/Checkout/Confirm", "/Checkout/ChangeLanguage", "/Orders/Track", "/Orders/Cancel", "/Orders/RequestCancellation", "/Orders/Forget", "/Cart/Refresh" })
        {
            using var denied = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>()));
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }
        await Add(client);
        var review = await Review(client);
        Assert.DoesNotContain("Private test street", Field(review, "reviewToken"));
        using var created = await Confirm(client, review);
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal("/Orders/Guest", created.Headers.Location!.ToString());
        var cookie = Assert.Single(created.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-Alharbi.Order="));
        Assert.Contains("secure", cookie); Assert.Contains("httponly", cookie); Assert.Contains("samesite=lax", cookie); Assert.Contains("path=/", cookie);
        using var confirmation = await client.GetAsync("/Orders/Guest");
        Assert.True(confirmation.Headers.CacheControl!.NoStore);
        var html = await confirmation.Content.ReadAsStringAsync();
        var tracking = WebUtility.HtmlDecode(Regex.Match(html, "id=\"saved-tracking-code\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.Equal(43, tracking.Length); Assert.DoesNotContain(tracking, cookie); Assert.DoesNotContain(tracking, created.Headers.Location.ToString());
        Assert.DoesNotContain("saved-tracking-code", await client.GetStringAsync("/Orders/Guest"));
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var order = Assert.Single(await db.Orders.Include(o => o.OrderItems).ToListAsync());
            Assert.Equal(32.50m, order.TotalPrice); Assert.Equal(8, (await db.Products.FindAsync(1))!.StockQuantity);
            Assert.Equal("Private test street 123", order.AddressLine); Assert.Single(order.OrderItems); Assert.Empty(db.ShoppingCartItems);
        }
        using var forgotten = await Post(client, "/Orders/Forget", html);
        Assert.Equal(HttpStatusCode.Redirect, forgotten.StatusCode);
        Assert.Equal("/Orders/Track", (await client.GetAsync("/Orders/Guest")).Headers.Location!.ToString());
        using var visitor = _store.Client();
        var trackPage = await visitor.GetStringAsync("/Orders/Track");
        using var tracked = await Post(visitor, "/Orders/Track", trackPage, ("TrackingCode", tracking));
        Assert.Equal("/Orders/Guest", tracked.Headers.Location!.ToString());
        var trackedHtml = await visitor.GetStringAsync("/Orders/Guest");
        Assert.Contains("Private test street 123", trackedHtml);
        Assert.DoesNotContain("saved-tracking-code", trackedHtml);
        using var cancelled = await Post(visitor, "/Orders/Cancel", trackedHtml, ("Id", Field(trackedHtml, "Id")),
            ("Guest", "true"), ("Version", Field(trackedHtml, "Version")), ("Reason", "Customer changed plans"));
        Assert.Equal(HttpStatusCode.Redirect, cancelled.StatusCode);
        using var checkScope = _api.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal("Cancelled", (await checkDb.Orders.SingleAsync()).Status);
        Assert.Equal(10, (await checkDb.Products.FindAsync(1))!.StockQuantity);
    }

    [Fact]
    public async Task Protected_review_rejects_tampering_and_different_guest_owners()
    {
        using var owner = _store.Client(); await Add(owner); var review = await Review(owner);
        using var altered = await Post(owner, "/Checkout/Confirm", review, ("reviewToken", "modified" + Field(review, "reviewToken")));
        Assert.Equal("/Checkout", altered.Headers.Location!.ToString());
        using var other = _store.Client(); await Add(other);
        using var stolen = await Post(other, "/Checkout/Confirm", await other.GetStringAsync("/Cart"), ("reviewToken", Field(review, "reviewToken")));
        Assert.Equal("/Checkout", stolen.Headers.Location!.ToString());
        using var scope = _api.Services.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Orders);
    }

    [Fact]
    public async Task Invalid_fields_preserve_entered_values_and_changed_shipping_requires_review()
    {
        using var client = _store.Client(); await Add(client);
        var initial = await client.GetStringAsync("/Checkout");
        using var invalid = await Post(client, "/Checkout/Review", initial, ("CartVersion", Field(initial, "CartVersion")),
            ("IdempotencyKey", Field(initial, "IdempotencyKey")), ("ContactName", "Keep my name"), ("Phone", "bad"),
            ("ShippingZoneId", "1"), ("City", "Test city"), ("AddressLine", "Test address"));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        var errors = await invalid.Content.ReadAsStringAsync(); Assert.Contains("validation-summary-errors", errors);
        Assert.Equal("Keep my name", Field(errors, "ContactName")); Assert.Equal("bad", Field(errors, "Phone"));
        var review = await Review(client);
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.ShippingZones.SingleAsync()).Fee = 99m; await db.SaveChangesAsync();
        }
        using var changed = await Confirm(client, review);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var html = await changed.Content.ReadAsStringAsync(); Assert.Contains("validation-summary-errors", html);
        Assert.Empty(Field(html, "reviewToken")); Assert.NotEmpty(Field(html, "IdempotencyKey"));
        using var checkScope = _api.Services.CreateScope();
        Assert.Empty(checkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Orders);
    }

    [Fact]
    public async Task Lost_confirmation_response_preserves_same_key_and_replays_one_committed_order()
    {
        var failure = new LostResponseState();
        using var store = new StorefrontFactory(_api, () => new LoseConfirmResponse(_api.Server.CreateHandler(), failure));
        using var client = store.Client(); await Add(client); var review = await Review(client);
        using var ambiguous = await Confirm(client, review);
        Assert.Equal(HttpStatusCode.OK, ambiguous.StatusCode);
        var retry = await ambiguous.Content.ReadAsStringAsync();
        Assert.Equal(Field(review, "reviewToken"), Field(retry, "reviewToken"));
        Assert.Contains("validation-summary-errors", retry);
        using var completed = await Confirm(client, retry);
        Assert.Equal("/Orders/Guest", completed.Headers.Location!.ToString());
        using var scope = _api.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(db.Orders); Assert.Equal(8, (await db.Products.FindAsync(1))!.StockQuantity);
    }

    [Fact]
    public async Task Customer_checkout_history_and_details_require_the_matching_account()
    {
        using var first = _store.Client(); await Register(first, "first@example.test", _api.Password);
        await Add(first); var review = await Review(first);
        using var created = await Confirm(first, review);
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var path = created.Headers.Location!.ToString(); Assert.StartsWith("/Orders/Details/", path);
        var details = await first.GetStringAsync(path); Assert.Contains("Private test street 123", details); Assert.DoesNotContain("saved-tracking-code", details);
        Assert.Contains(path, await first.GetStringAsync("/Orders"));
        using var second = _store.Client(); await Register(second, "second@example.test", _api.Password);
        Assert.Equal(HttpStatusCode.NotFound, (await second.GetAsync(path)).StatusCode);
        Assert.DoesNotContain(path, await second.GetStringAsync("/Orders"));
        using var anonymous = _store.Client(); Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync(path)).StatusCode);
        using var stolenReview = await Post(second, "/Checkout/Confirm", await second.GetStringAsync("/Account/Profile"), ("reviewToken", Field(review, "reviewToken")));
        Assert.Equal("/Checkout", stolenReview.Headers.Location!.ToString());
        using var scope = _api.Services.CreateScope(); Assert.Single(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Orders);
    }

    [Fact]
    public async Task Cart_refresh_recovers_reduced_stock_and_hidden_items_before_checkout()
    {
        using var client = _store.Client(); await Add(client);
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Products.FindAsync(1))!.StockQuantity = 1; await db.SaveChangesAsync();
        }
        Assert.Equal("/Cart", (await client.GetAsync("/Checkout")).Headers.Location!.ToString());
        var adjusted = await client.GetStringAsync("/Cart"); Assert.Contains("/Cart/Refresh", adjusted);
        using var refreshed = await Post(client, "/Cart/Refresh", adjusted, ("Version", Field(adjusted, "Version")));
        Assert.Equal(HttpStatusCode.Redirect, refreshed.StatusCode);
        var review = await Review(client); Assert.NotEmpty(Field(review, "reviewToken"));
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, (await db.ShoppingCartItems.SingleAsync()).Quantity);
            (await db.Products.FindAsync(1))!.IsAvailable = false; await db.SaveChangesAsync();
        }
        var hidden = await client.GetStringAsync("/Cart"); Assert.Contains("/Cart/Refresh", hidden);
        using var cleared = await Post(client, "/Cart/Refresh", hidden, ("Version", Field(hidden, "Version")));
        Assert.Equal(HttpStatusCode.Redirect, cleared.StatusCode);
        using var finalScope = _api.Services.CreateScope();
        Assert.Empty(finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ShoppingCartItems);
    }

    [Fact]
    public async Task Review_language_switch_preserves_the_protected_submission_and_avoids_get_only_errors()
    {
        using var client = _store.Client(); await Add(client); var review = await Review(client);
        var token = Field(review, "reviewToken");
        foreach (var culture in new[] { "en", "ar" })
        {
            using var switched = await Post(client, "/Checkout/ChangeLanguage", review, ("reviewToken", token), ("culture", culture));
            Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
            review = await switched.Content.ReadAsStringAsync();
            Assert.Contains("lang=\"" + culture + "\" dir=\"" + (culture == "ar" ? "rtl" : "ltr") + "\"", review);
            Assert.Equal(token, Field(review, "reviewToken"));
            Assert.Contains("action=\"/Checkout/ChangeLanguage\"", review);
            Assert.Null(switched.Headers.Location);
        }
        using var placed = await Confirm(client, review);
        Assert.Equal("/Orders/Guest", placed.Headers.Location!.ToString());
        using var scope = _api.Services.CreateScope();
        Assert.Single(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Orders);
    }

    [Fact]
    public async Task Maximum_cart_with_long_arabic_product_and_discount_names_survives_review_language_and_confirmation()
    {
        using var api = new ApiFactory { PublicProductIds = Enumerable.Range(1, 100).ToArray() };
        api.Seed();
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var first = db.Products.Find(1)!;
            first.Name = new string('م', 200); first.Price = 10m; first.StockQuantity = 100;
            db.Products.AddRange(Enumerable.Range(2, 99).Select(i => new Product
            {
                Name = new string('م', 200), CategoryId = 1, Price = 10m, StockQuantity = 100, IsAvailable = true
            }));
            db.ShippingZones.Add(new ShippingZone { Governorate = "Test governorate", City = "Test city", Fee = 7.50m,
                Enabled = true, NormalizedDestination = "TEST GOVERNORATE\nTEST CITY" });
            await db.SaveChangesAsync();
            var now = api.Clock.GetUtcNow().UtcDateTime;
            db.Discounts.AddRange(Enumerable.Range(1, 100).Select(id => new Discount
            {
                ProductId = id, Name = new string('خ', 200), Percentage = 10m, StartDate = now.AddDays(-1), EndDate = now.AddDays(1)
            }));
            await db.SaveChangesAsync();
        }
        using var store = new StorefrontFactory(api);
        using var client = store.Client(); await Add(client);
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cart = await db.ShoppingCarts.SingleAsync();
            db.ShoppingCartItems.AddRange(Enumerable.Range(2, 99).Select(id => new ShoppingCartItem
            { CartId = cart.Id, ProductId = id, Quantity = 1 }));
            cart.Version = Guid.NewGuid(); await db.SaveChangesAsync();
        }
        var review = await Review(client);
        var token = Field(review, "reviewToken");
        Assert.InRange(token.Length, 131073, 524288);
        using var switched = await Post(client, "/Checkout/ChangeLanguage", review, ("reviewToken", token), ("culture", "en"));
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        var translated = await switched.Content.ReadAsStringAsync();
        Assert.Equal(token, Field(translated, "reviewToken"));
        using var created = await Confirm(client, translated);
        Assert.Equal("/Orders/Guest", created.Headers.Location!.ToString());
        using var checkScope = api.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = Assert.Single(await checkDb.Orders.Include(o => o.OrderItems).ToListAsync());
        Assert.Equal(100, order.OrderItems.Count); Assert.Equal(916.50m, order.TotalPrice);
        Assert.Empty(checkDb.ShoppingCartItems);
    }

    private sealed class LostResponseState { public int Dropped; }
    private sealed class LoseConfirmResponse(HttpMessageHandler inner, LostResponseState state) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (request.RequestUri!.AbsolutePath.EndsWith("/guest/checkout/confirm") && response.IsSuccessStatusCode && Interlocked.Exchange(ref state.Dropped, 1) == 0)
            { response.Dispose(); throw new HttpRequestException("Simulated lost response after API commit."); }
            return response;
        }
    }
}
