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

public sealed class CustomerCartTests : IDisposable
{
    private readonly ApiFactory _api = new() { PublicProductIds = Enumerable.Range(1, 27).ToArray() };
    private readonly StorefrontFactory _store;
    public CustomerCartTests() { StorefrontFixture.Seed(_api); _store = new(_api); }
    public void Dispose() { _store.Dispose(); _api.Dispose(); }
    private string Password => _api.Password;
    private async Task<CustomerLoginResult> Register(HttpClient client, string email = "one@example.test")
    {
        using var r = await client.PostAsJsonAsync("/api/customer/account/register", new { Email = email, DisplayName = "Customer", Password, Role = "Admin", IsBlocked = false });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<CustomerLoginResult>())!;
    }
    private static void Authenticate(HttpClient client, CustomerLoginResult login) => client.DefaultRequestHeaders.Authorization = new("Bearer", login.Token);
    private async Task<(HttpClient Client, CartView Cart, string Token)> Guest()
    {
        var client = _api.Client();
        var r = await client.PostAsync("/api/guest/cart", null);
        r.EnsureSuccessStatusCode();
        var created = (await r.Content.ReadFromJsonAsync<GuestCartResult>())!;
        client.DefaultRequestHeaders.Add("X-Guest-Cart", created.Token);
        return (client, created.Cart, created.Token);
    }
    private static Task<HttpResponseMessage> Mutate(HttpClient c, string route, CartView cart, int id, int qty = 1, HttpMethod? method = null) =>
        c.SendAsync(new HttpRequestMessage(method ?? HttpMethod.Post, route + "/items/" + id) { Content = JsonContent.Create(new { cart.Version, Quantity = qty, Price = 0, CustomerId = 999 }) });
    private static async Task<CartView> Cart(HttpResponseMessage r) { r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<CartView>())!; }

    [Fact]
    public async Task Registration_normalizes_email_hashes_password_and_cannot_grant_admin()
    {
        using var client = _api.Client();
        var registered = await Register(client, "One@Example.Test");
        Authenticate(client, registered);
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.FindAsync(registered.Customer.Id);
        Assert.Equal("Customer", user!.Role); Assert.NotEqual(Password, user.Password);
        Assert.Equal("ONE@EXAMPLE.TEST", (await db.CustomerAccounts.FindAsync(user.Id))!.NormalizedEmail);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/customer/account/register",
            new { Email = "one@example.test", DisplayName = "Again", Password })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/customer/account/register",
            new { Email = "bad", DisplayName = " ", Password = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/customer/account/login",
            new { Email = "one@example.test", Password = "wrong-password" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/customer/account/login",
            new { Email = "ONE@example.test", Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/orders", new { })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", await _api.Login(client));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/customer/account/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/customer/cart")).StatusCode);
    }

    [Fact]
    public async Task Recovery_is_single_use_hashed_and_revokes_all_customer_sessions()
    {
        using var client = _api.Client(); var login = await Register(client); Authenticate(client, login);
        var forgot = await client.PostAsJsonAsync("/api/customer/account/forgot-password", new { Email = "one@example.test", Culture = "ar" });
        var unknown = await client.PostAsJsonAsync("/api/customer/account/forgot-password", new { Email = "unknown@example.test", Culture = "ar" });
        Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        Assert.Equal(await forgot.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        var message = Assert.Single(_api.Emails.Messages);
        using (var scope = _api.Services.CreateScope())
        {
            var account = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().CustomerAccounts.FindAsync(login.Customer.Id);
            Assert.NotEqual(message.Token, account!.ResetTokenHash);
        }
        Assert.DoesNotContain(message.Token, await forgot.Content.ReadAsStringAsync());
        var reset = new { message.Email, message.Token, Password = Password + "-new" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/customer/account/reset-password", reset)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/customer/account/reset-password", reset)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/customer/cart")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/customer/account/login", new { message.Email, Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/customer/account/login", new { message.Email, Password = Password + "-new" })).StatusCode);
    }

    [Fact]
    public async Task Recovery_expires_and_new_request_invalidates_previous_code()
    {
        using var client = _api.Client(); await Register(client);
        for (var i = 0; i < 2; i++) await client.PostAsJsonAsync("/api/customer/account/forgot-password", new { Email = "one@example.test" });
        var messages = _api.Emails.Messages.ToArray(); Assert.Equal(2, messages.Length);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/customer/account/reset-password",
            new { messages[0].Email, messages[0].Token, Password })).StatusCode);
        _api.Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/customer/account/reset-password",
            new { messages[1].Email, messages[1].Token, Password })).StatusCode);
        await client.PostAsJsonAsync("/api/customer/account/forgot-password", new { Email = "Admin@example.test" });
        Assert.Equal(2, _api.Emails.Messages.Count);
    }

    [Fact]
    public async Task Profile_password_change_and_blocked_account_checks_preserve_boundaries()
    {
        using var client = _api.Client(); var login = await Register(client); Authenticate(client, login);
        var updated = await client.PutAsJsonAsync("/api/customer/account/profile", new { DisplayName = "<script>name</script>", Email = "stolen@example.test", Role = "Admin" });
        var profile = (await updated.Content.ReadFromJsonAsync<CustomerView>())!;
        Assert.Equal("one@example.test", profile.Email);
        Assert.Equal("<script>name</script>", profile.DisplayName);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/customer/account/change-password",
            new { CurrentPassword = "wrong", Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/customer/account/change-password",
            new { CurrentPassword = Password, Password = Password + "-changed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/customer/account/profile")).StatusCode);
        var response = await client.PostAsJsonAsync("/api/customer/account/login", new { Email = "one@example.test", Password = Password + "-changed" });
        Authenticate(client, (await response.Content.ReadFromJsonAsync<CustomerLoginResult>())!);
        using var scope = _api.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Users.FindAsync(login.Customer.Id))!.IsBlocked = true; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/customer/cart")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/customer/account/login",
            new { Email = "one@example.test", Password = Password + "-changed" })).StatusCode);
    }

    [Fact]
    public async Task Guest_cart_is_private_persistent_and_uses_authoritative_prices_without_reserving_stock()
    {
        var guest = await Guest(); using var client = guest.Client;
        var cart = await Cart(await Mutate(client, "/api/guest/cart", guest.Cart, 1, 2));
        Assert.Equal(2501m, cart.Total); Assert.Equal("YER", cart.Currency);
        var again = (await client.GetFromJsonAsync<CartView>("/api/guest/cart"))!;
        Assert.Equal(cart.Version, again.Version); Assert.Equal(2, Assert.Single(again.Items).Quantity);
        using var other = _api.Client();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/api/guest/cart")).StatusCode);
        other.DefaultRequestHeaders.Add("X-Guest-Cart", new string('x', 43));
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/api/guest/cart")).StatusCode);
        using var scope = _api.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(10, (await db.Products.FindAsync(1))!.StockQuantity);
        Assert.Empty(db.Orders); Assert.Empty(db.OrderItems);
        _api.Clock.Advance(TimeSpan.FromDays(31));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/guest/cart")).StatusCode);
    }

    [Fact]
    public async Task Guest_cart_revalidates_price_stock_and_publication_on_every_read()
    {
        var guest = await Guest(); using var client = guest.Client;
        var cart = await Cart(await Mutate(client, "/api/guest/cart", guest.Cart, 1, 9));
        using var scope = _api.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var product = (await db.Products.FindAsync(1))!;
        product.Price = 77.50m; product.StockQuantity = 2; await db.SaveChangesAsync();
        var updated = (await client.GetFromJsonAsync<CartView>("/api/guest/cart"))!;
        Assert.Equal(155m, updated.Total); Assert.Equal(2, updated.ItemCount); Assert.True(updated.Adjusted);
        product.IsAvailable = false; await db.SaveChangesAsync();
        var hidden = await client.GetStringAsync("/api/guest/cart");
        Assert.DoesNotContain(product.Name, hidden); Assert.DoesNotContain("77.50", hidden);
        Assert.Empty(JsonSerializer.Deserialize<JsonElement>(hidden).GetProperty("items").EnumerateArray());
        foreach (var id in new[] { 1, 2, 3, 28, 999 })
            Assert.Equal(HttpStatusCode.NotFound, (await Mutate(client, "/api/guest/cart", cart, id)).StatusCode);
    }

    [Theory]
    [InlineData(0)] [InlineData(100)] [InlineData(int.MaxValue)]
    public async Task Invalid_cart_quantities_are_rejected(int quantity)
    {
        var guest = await Guest(); using var client = guest.Client;
        Assert.Equal(HttpStatusCode.BadRequest, (await Mutate(client, "/api/guest/cart", guest.Cart, 1, quantity)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<CartView>("/api/guest/cart"))!.Items);
    }

    [Fact]
    public async Task Quantity_combination_is_capped_and_stale_writes_do_not_overwrite()
    {
        var guest = await Guest(); using var client = guest.Client;
        var first = await Cart(await Mutate(client, "/api/guest/cart", guest.Cart, 1, 7));
        Assert.Equal(HttpStatusCode.Conflict, (await Mutate(client, "/api/guest/cart", guest.Cart, 1)).StatusCode);
        var combined = await Cart(await Mutate(client, "/api/guest/cart", first, 1, 99));
        Assert.True(combined.Adjusted); Assert.Equal(10, combined.ItemCount); Assert.Single(combined.Items);
        var reduced = await Cart(await Mutate(client, "/api/guest/cart", combined, 1, 2, HttpMethod.Put));
        Assert.Equal(2, reduced.ItemCount);
        var removed = await Cart(await Mutate(client, "/api/guest/cart", reduced, 1, 1, HttpMethod.Delete));
        Assert.Empty(removed.Items);
    }

    [Fact]
    public async Task Registered_carts_are_owned_and_merge_is_atomic_repeat_safe_and_private()
    {
        using var one = _api.Client(); var login = await Register(one); Authenticate(one, login);
        using var two = _api.Client(); Authenticate(two, await Register(two, "two@example.test"));
        var saved = (await one.GetFromJsonAsync<CartView>("/api/customer/cart"))!;
        saved = await Cart(await Mutate(one, "/api/customer/cart", saved, 1, 5));
        var guest = await Guest(); using var client = guest.Client;
        await Cart(await Mutate(client, "/api/guest/cart", guest.Cart, 1, 8));
        var requests = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            one.PostAsJsonAsync("/api/customer/cart/merge", new { GuestToken = guest.Token })));
        Assert.Contains(requests, r => r.IsSuccessStatusCode);
        Assert.All(requests, r => Assert.True(r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.Conflict));
        var merged = await Cart(await one.PostAsJsonAsync("/api/customer/cart/merge", new { GuestToken = guest.Token }));
        Assert.True(merged.AlreadyMerged); Assert.Equal(10, merged.ItemCount);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/guest/cart")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await two.PostAsJsonAsync("/api/customer/cart/merge", new { GuestToken = guest.Token })).StatusCode);
        Assert.Empty((await two.GetFromJsonAsync<CartView>("/api/customer/cart?customerId=" + login.Customer.Id))!.Items);
        using var returning = _api.Client();
        var auth = await returning.PostAsJsonAsync("/api/customer/account/login", new { Email = "one@example.test", Password });
        Authenticate(returning, (await auth.Content.ReadFromJsonAsync<CustomerLoginResult>())!);
        Assert.Equal(10, (await returning.GetFromJsonAsync<CartView>("/api/customer/cart"))!.ItemCount);
    }

    [Fact]
    public async Task Merge_excludes_now_unavailable_products_and_never_changes_inventory()
    {
        using var one = _api.Client(); var login = await Register(one); Authenticate(one, login);
        var guest = await Guest(); using var client = guest.Client;
        var cart = await Cart(await Mutate(client, "/api/guest/cart", guest.Cart, 1, 4));
        await Cart(await Mutate(client, "/api/guest/cart", cart, 4, 3));
        using var scope = _api.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Products.FindAsync(1))!.IsAvailable = false; await db.SaveChangesAsync();
        var merged = await Cart(await one.PostAsJsonAsync("/api/customer/cart/merge", new { GuestToken = guest.Token }));
        Assert.True(merged.Adjusted); Assert.Equal(4, Assert.Single(merged.Items).ProductId);
        Assert.Equal(10, (await db.Products.FindAsync(4))!.StockQuantity);
    }

    [Fact]
    public async Task Concurrent_cart_updates_allow_one_version_winner()
    {
        var guest = await Guest(); using var client = guest.Client;
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Mutate(client, "/api/guest/cart", guest.Cart, 1)));
        Assert.Single(responses, r => r.IsSuccessStatusCode);
        Assert.All(responses.Where(r => !r.IsSuccessStatusCode), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(1, (await client.GetFromJsonAsync<CartView>("/api/guest/cart"))!.ItemCount);
    }


    [Fact]
    public async Task Quantity_and_distinct_product_limits_hold_during_merge()
    {
        using var api = new ApiFactory { PublicProductIds = Enumerable.Range(1, 150).ToArray() };
        api.Seed();
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Products.Find(1)!.StockQuantity = 1000;
        db.Products.AddRange(Enumerable.Range(2, 101).Select(i => new Product { Name = "Limit product " + i, CategoryId = 1, Price = 1, StockQuantity = 1000 }));
        await db.SaveChangesAsync();
        using var customer = api.Client();
        var register = await customer.PostAsJsonAsync("/api/customer/account/register", new { Email = "limits@example.test", DisplayName = "Limits", Password = api.Password });
        var login = (await register.Content.ReadFromJsonAsync<CustomerLoginResult>())!;
        Authenticate(customer, login);
        var saved = await db.ShoppingCarts.SingleAsync(c => c.CustomerId == login.Customer.Id);
        saved.Items = Enumerable.Range(1, 100).Select(id => new ShoppingCartItem { Cart = saved, ProductId = id, Quantity = 1 }).ToList();
        await db.SaveChangesAsync();
        var snapshot = (await customer.GetFromJsonAsync<CartView>("/api/customer/cart"))!;
        Assert.Equal(HttpStatusCode.BadRequest, (await Mutate(customer, "/api/customer/cart", snapshot, 101)).StatusCode);
        snapshot = await Cart(await Mutate(customer, "/api/customer/cart", snapshot, 1, 99));
        Assert.Equal(99, snapshot.Items.Single(i => i.ProductId == 1).Quantity); Assert.True(snapshot.Adjusted);
        using var guest = api.Client();
        var created = (await (await guest.PostAsync("/api/guest/cart", null)).Content.ReadFromJsonAsync<GuestCartResult>())!;
        guest.DefaultRequestHeaders.Add("X-Guest-Cart", created.Token);
        await Cart(await Mutate(guest, "/api/guest/cart", created.Cart, 101));
        var merged = await Cart(await customer.PostAsJsonAsync("/api/customer/cart/merge", new { GuestToken = created.Token }));
        Assert.Equal(100, merged.Items.Count); Assert.True(merged.Adjusted);
        Assert.DoesNotContain(merged.Items, i => i.ProductId == 101);
        Assert.Equal(1000, (await db.Products.FindAsync(1))!.StockQuantity);
    }

    [Fact]
    public async Task Repeated_bad_passwords_temporarily_lock_customer_only()
    {
        using var client = _api.Client(); await Register(client);
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/customer/account/login",
                new { Email = "one@example.test", Password = "invalid-password" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/customer/account/login",
            new { Email = "one@example.test", Password })).StatusCode);
        _api.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/customer/account/login",
            new { Email = "one@example.test", Password })).StatusCode);
    }


    [Fact]
    public async Task Additive_schema_keeps_legacy_users_products_and_password_hashes()
    {
        using var legacy = new ApiFactory { MigrationTarget = "20261012000000_HardenDataIntegrity" };
        legacy.Seed();
        using var scope = legacy.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var before = await db.Users.AsNoTracking().OrderBy(u => u.Id).Select(u => new { u.Id, u.Username, u.Password, u.Role }).ToListAsync();
        // Only the disposable SQL Server CI database executes the historical-to-current migration.
        if (db.Database.IsSqlServer()) await db.Database.MigrateAsync();
        var after = await db.Users.AsNoTracking().OrderBy(u => u.Id).Select(u => new { u.Id, u.Username, u.Password, u.Role }).ToListAsync();
        Assert.Equal(before, after);
        Assert.Single(db.Products); Assert.Equal(10, db.Products.Single().StockQuantity);
        Assert.Empty(db.CustomerAccounts); Assert.Empty(db.ShoppingCarts);
        using var client = legacy.Client();
        var registered = await client.PostAsJsonAsync("/api/customer/account/register",
            new { Email = "upgraded@example.test", DisplayName = "After upgrade", Password = legacy.Password });
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        Assert.Equal(4, await db.Users.CountAsync());
    }

    private static async Task<string> FormToken(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        return WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    }
    private static Task<HttpResponseMessage> Form(HttpClient client, string path, string token, params (string Key, string Value)[] values) =>
        client.PostAsync(path, new FormUrlEncodedContent(values.Append(("__RequestVerificationToken", token)).ToDictionary(v => v.Item1, v => v.Item2)));

    [Fact]
    public async Task Storefront_guest_registration_merge_profile_and_logout_keep_secure_cookies_and_saved_cart()
    {
        using var client = _store.Client();
        foreach (var path in new[] { "/Cart/Add", "/Cart/Update", "/Cart/Remove", "/Cart/Merge", "/Account/Login", "/Account/Register", "/Account/Logout", "/Account/ForgotPassword", "/Account/ResetPassword", "/Account/Profile", "/Account/ChangePassword" })
        {
            var rejected = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string,string>()));
            Assert.True(rejected.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Redirect);
            if (rejected.StatusCode == HttpStatusCode.Redirect) Assert.Contains("/Account/Login", rejected.Headers.Location!.ToString());
        }
        var added = await Form(client, "/Cart/Add", await FormToken(client, "/"), ("ProductId","1"), ("Quantity","3"));
        var guestCookie = Assert.Single(added.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-Alharbi.Guest="));
        Assert.Contains("secure", guestCookie); Assert.Contains("httponly", guestCookie); Assert.Contains("samesite=lax", guestCookie);
        Assert.Contains("rtl", await client.GetStringAsync("/Cart"));
        var registered = await Form(client, "/Account/Register", await FormToken(client, "/Account/Register"),
            ("DisplayName","<script>Name</script>"), ("Email","web@example.test"), ("Password",Password), ("ConfirmPassword",Password));
        Assert.Equal(HttpStatusCode.Redirect, registered.StatusCode);
        var customerCookie = Assert.Single(registered.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-Alharbi.Customer="));
        Assert.Contains("secure", customerCookie); Assert.Contains("httponly", customerCookie);
        Assert.Contains(registered.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-Alharbi.Guest=;"));
        var cart = await client.GetStringAsync("/Cart"); Assert.Contains("value=\"3\"", cart);
        var profile = await client.GetStringAsync("/Account/Profile"); Assert.Contains("&lt;script&gt;", profile); Assert.DoesNotContain("<script>Name", profile);
        var switched = await Form(client, "/Language/Change", await FormToken(client, "/Cart"), ("culture","en"), ("returnUrl","/Cart"));
        Assert.Equal("/Cart", switched.Headers.Location!.ToString());
        Assert.Contains("lang=\"en\" dir=\"ltr\"", await client.GetStringAsync("/Cart"));
        await Form(client, "/Account/Logout", await FormToken(client, "/Cart"));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Account/Profile")).StatusCode);
        var signedIn = await Form(client, "/Account/Login", await FormToken(client, "/Account/Login"),
            ("Email","web@example.test"), ("Password",Password), ("ReturnUrl","https://example.test"));
        Assert.Equal("/Account/Profile", signedIn.Headers.Location!.ToString());
        Assert.Contains("3,751.50", await client.GetStringAsync("/Cart"));
    }
}

