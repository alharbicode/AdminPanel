using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AdminPanel.API.Data;
using AdminPanel.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace AdminPanel.Tests;

public sealed class ApiTests : IDisposable
{
    private readonly ApiFactory _api = new();
    public void Dispose() => _api.Dispose();

    [Theory]
    [InlineData("/api/categories")]
    [InlineData("/api/products")]
    [InlineData("/api/orders")]
    [InlineData("/api/discounts")]
    [InlineData("/api/shippingoptions")]
    [InlineData("/api/contentpages")]
    public async Task Administration_requires_authentication(string path)
    {
        _api.Seed();
        using var client = _api.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("customer")]
    public async Task Non_admin_roles_cannot_read_or_write_administration(string role)
    {
        _api.Seed();
        using var client = _api.Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", await _api.Login(client, role));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/categories", new { name = "Blocked" })).StatusCode);
    }

    [Fact]
    public async Task Passwords_blocking_and_token_revocation_are_enforced()
    {
        using var client = await _api.AdminClient();
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(u => u.Username == "admin");
        Assert.NotEqual(_api.Password, user.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "incorrect" })).StatusCode);
        user.IsBlocked = true;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = _api.Password })).StatusCode);
        user.IsBlocked = false;
        user.Password = "legacy-plaintext";
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "legacy-plaintext" })).StatusCode);
    }

    [Theory]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    [InlineData("expired")]
    [InlineData("wrong-key")]
    public async Task Invalid_tokens_are_rejected(string reason)
    {
        _api.Seed();
        using var client = _api.Client();
        var key = reason == "wrong-key" ? new string('x', 48) : _api.Secret;
        var jwt = new JwtSecurityToken(
            reason == "wrong-issuer" ? "wrong" : "AdminPanel.API",
            reason == "wrong-audience" ? "wrong" : "AdminPanel.MVC",
            new[] { new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, "Admin") },
            expires: reason == "expired" ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/products")).StatusCode);
    }

    [Fact]
    public async Task Login_is_rate_limited()
    {
        _api.Seed();
        using var client = _api.Client();
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { username = "missing", password = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/auth/login", new { username = "missing", password = "wrong" })).StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(11)]
    public async Task Invalid_quantities_do_not_change_stock(int quantity)
    {
        using var client = await _api.AdminClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/orders",
            new { customerId = 3, items = new[] { new { productId = 1, quantity } } })).StatusCode);
        await AssertStock(client, 10);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement[]>("/api/orders"))!);
    }

    [Fact]
    public async Task Order_details_status_and_cancellation_preserve_price_and_stock()
    {
        using var client = await _api.AdminClient();
        using var created = await client.PostAsJsonAsync("/api/orders", new { customerId = 3, shippingOptionId = 1, items = new[] { new { productId = 1, quantity = 2 } } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetInt32();
        var order = await client.GetFromJsonAsync<JsonElement>($"/api/orders/{id}");
        Assert.Equal(25m, order.GetProperty("totalPrice").GetDecimal());
        Assert.Equal(12.5m, order.GetProperty("items")[0].GetProperty("price").GetDecimal());
        Assert.Equal("Customer@example.test", order.GetProperty("customerEmail").GetString());
        await AssertStock(client, 8);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/orders/{id}/status", "Confirmed")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/orders/{id}/status", "Cancelled")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/orders/{id}/status", "arbitrary")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync("/api/products/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync("/api/shippingoptions/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/orders/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/orders/{id}")).StatusCode);
        await AssertStock(client, 10);
    }

    [Fact]
    public async Task Empty_duplicate_and_invalid_references_do_not_create_orders()
    {
        using var client = await _api.AdminClient();
        object[] invalid =
        {
            new { customerId = 3, items = Array.Empty<object>() },
            new { customerId = 999, items = new[] { new { productId = 1, quantity = 1 } } },
            new { customerId = 3, shippingOptionId = 999, items = new[] { new { productId = 1, quantity = 1 } } },
            new { customerId = 3, items = new[] { new { productId = 1, quantity = 1 }, new { productId = 1, quantity = 1 } } },
            new { customerId = 3, items = new[] { new { productId = 1, quantity = 1 }, new { productId = 999, quantity = 1 } } }
        };
        foreach (var body in invalid) Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/orders", body)).StatusCode);
        await AssertStock(client, 10);
    }

    [Fact]
    public async Task Stale_inventory_writes_roll_back_the_entire_order_transaction()
    {
        _api.Seed();
        using var firstScope = _api.Services.CreateScope();
        using var secondScope = _api.Services.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var firstProduct = await first.Products.FindAsync(1);
        var secondProduct = await second.Products.FindAsync(1);
        firstProduct!.StockQuantity -= 8;
        await first.SaveChangesAsync();
        secondProduct!.StockQuantity -= 8;
        second.Orders.Add(new Order { CustomerId = 3, TotalPrice = 100, OrderItems = new List<OrderItem> { new() { ProductId = 1, Quantity = 8, Price = 12.5m } } });
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        first.ChangeTracker.Clear();
        Assert.Equal(2, (await first.Products.FindAsync(1))!.StockQuantity);
        Assert.Empty(await first.Orders.ToListAsync());
    }

    [Fact]
    public async Task Catalog_validation_and_referential_integrity_are_enforced()
    {
        using var client = await _api.AdminClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/categories", new { name = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync("/api/categories/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/orders/999")).StatusCode);
        using var invalidProduct = ProductForm(categoryId: 999);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/products", invalidProduct)).StatusCode);
        using var script = ProductForm();
        script.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("<script>alert(1)</script>")), "ImageFile", "payload.html");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/products", script)).StatusCode);
        Assert.Empty(Directory.GetFiles(_api.UploadRoot, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Content_shipping_and_discount_crud_are_validated()
    {
        using var client = await _api.AdminClient();
        var content = new { title = "About", slug = "About Us", content = "Test content" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/contentpages", content)).StatusCode);
        var page = await client.GetFromJsonAsync<JsonElement>("/api/contentpages/about-us");
        var pageId = page.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/contentpages", content)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/contentpages/{pageId}", new { title = "Changed", slug = "changed", content = "Updated" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/contentpages/{pageId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/contentpages/changed")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/shippingoptions", new { name = "Negative", area = "A", price = -1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/shippingoptions", new { name = "Express", area = "A", price = 4 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/shippingoptions/2", new { name = "Express", area = "A", price = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync("/api/shippingoptions/2")).StatusCode);
        var discount = new { name = "Sale", percentage = 10, startDate = DateTime.UtcNow, endDate = DateTime.UtcNow.AddDays(1), productId = 1 };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/discounts", discount)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/discounts/1", new { name = "Sale", percentage = 10, startDate = DateTime.UtcNow, endDate = DateTime.UtcNow.AddDays(1), productId = 999 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/discounts/1", discount)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync("/api/discounts/1")).StatusCode);
    }

    public static MultipartFormDataContent ProductForm(int categoryId = 1)
    {
        var form = new MultipartFormDataContent();
        foreach (var (key, value) in new Dictionary<string, string>
        { ["Name"] = "Multipart product", ["Price"] = "19.95", ["StockQuantity"] = "7", ["Unit"] = "Piece", ["IsAvailable"] = "true", ["CategoryId"] = categoryId.ToString() })
            form.Add(new StringContent(value), key);
        return form;
    }
    private static async Task AssertStock(HttpClient client, int expected)
    {
        var product = await client.GetFromJsonAsync<JsonElement>("/api/products/1");
        Assert.Equal(expected, product.GetProperty("stockQuantity").GetInt32());
    }
}
