using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdminPanel.API.Data;
using AdminPanel.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdminPanel.Tests;

public sealed class OrderIntegrityTests
{
    [Fact]
    public async Task Legacy_schema_upgrade_preserves_orders_users_catalog_shipping_and_discounts()
    {
        using var api = new ApiFactory { MigrationTarget = "20261013202004_AddCustomerAccountsAndCarts" };
        api.Seed();
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = await db.Users.AsNoTracking().Select(u => new { u.Id, u.Username, u.Email, u.Password, u.Role }).OrderBy(u => u.Id).ToListAsync();
        db.Products.Find(1)!.StockQuantity = 8;
        db.Products.Find(1)!.ImageUrl = "/images/products/legacy.jpg";
        db.Discounts.Add(new Discount { Name = "Legacy discount", Percentage = 5, ProductId = 1, StartDate = new DateTime(2025, 1, 1), EndDate = new DateTime(2025, 2, 1) });
        await db.SaveChangesAsync();
        if (db.Database.IsSqlServer())
        {
            // Existing columns only: the new fields do not exist until the explicit disposable upgrade.
            await db.Database.ExecuteSqlRawAsync("INSERT INTO Orders (CustomerId, OrderDate, TotalPrice, Status, ShippingOptionId) VALUES (3, '2025-01-02T12:00:00', 25.00, 'Pending', 1)");
            await db.Database.ExecuteSqlRawAsync("INSERT INTO OrderItems (OrderId, ProductId, Quantity, Price) VALUES (1, 1, 2, 12.50)");
            await db.Database.MigrateAsync();
            Assert.Throws<NotSupportedException>(() => db.Database.GetService<IMigrator>().GenerateScript(
                "20261013213455_AddCheckoutSnapshotsAndShippingZones", "20261013202004_AddCustomerAccountsAndCarts"));
        }
        else
        {
            db.Orders.Add(new Order { CustomerId = 3, OrderDate = new DateTime(2025, 1, 2, 12, 0, 0), TotalPrice = 25, Status = "Pending", ShippingOptionId = 1,
                OrderItems = [new OrderItem { ProductId = 1, Quantity = 2, Price = 12.5m }] });
            await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();
        Assert.Equal(users, await db.Users.AsNoTracking().Select(u => new { u.Id, u.Username, u.Email, u.Password, u.Role }).OrderBy(u => u.Id).ToListAsync());
        var order = await db.Orders.Include(o => o.OrderItems).SingleAsync();
        Assert.Equal(25m, order.TotalPrice); Assert.Null(order.Reference); Assert.Null(order.ShippingFee);
        Assert.Equal(12.5m, order.OrderItems.Single().Price); Assert.Null(order.OrderItems.Single().ProductName);
        Assert.Equal(8, db.Products.Single().StockQuantity); Assert.Equal("/images/products/legacy.jpg", db.Products.Single().ImageUrl);
        Assert.Equal(3m, db.ShippingOptions.Single().Price); Assert.Equal("Legacy discount", db.Discounts.Single().Name);
        Assert.Empty(db.ShippingZones);
        // Do not call Seed again: this fixture intentionally started at an older migration.
        using var admin = api.Client();
        admin.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await api.Login(admin));
        (await admin.DeleteAsync("/api/orders/1")).EnsureSuccessStatusCode();
        (await admin.DeleteAsync("/api/orders/1")).EnsureSuccessStatusCode();
        db.ChangeTracker.Clear();
        Assert.Equal(10, db.Products.Single().StockQuantity); Assert.Single(db.OrderItems);
        Assert.Equal("Cancelled", db.Orders.Single().Status);
    }

    [Fact]
    public async Task Admin_creation_is_idempotent_and_rejects_same_key_different_request()
    {
        using var api = new ApiFactory(); using var admin = await api.AdminClient();
        var key = Guid.NewGuid();
        var body = new { requestKey = key, customerId = 3, items = new[] { new { productId = 1, quantity = 2 } } };
        var first = await admin.PostAsJsonAsync("/api/orders", body); first.EnsureSuccessStatusCode();
        var second = await admin.PostAsJsonAsync("/api/orders", body); second.EnsureSuccessStatusCode();
        Assert.Equal((await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetInt32(),
            (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/orders", new { requestKey = key, customerId = 3,
            items = new[] { new { productId = 1, quantity = 1 } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/orders", new { customerId = 3,
            items = new[] { new { productId = 1, quantity = 1 } } })).StatusCode);
        using var scope = api.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(db.Orders); Assert.Equal(8, db.Products.Single().StockQuantity);
    }

    [Fact]
    public async Task Stale_admin_product_form_cannot_overwrite_checkout_inventory()
    {
        using var api = new ApiFactory(); using var admin = await api.AdminClient();
        var before = await admin.GetFromJsonAsync<JsonElement>("/api/products/1");
        Assert.Equal(10, before.GetProperty("expectedStockQuantity").GetInt32());
        (await admin.PostAsJsonAsync("/api/orders", new { requestKey = Guid.NewGuid(), customerId = 3,
            items = new[] { new { productId = 1, quantity = 2 } } })).EnsureSuccessStatusCode();
        using var stale = ApiTests.ProductForm(); stale.Add(new StringContent("10"), "ExpectedStockQuantity");
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsync("/api/products/1", stale)).StatusCode);
        using var noPrecondition = ApiTests.ProductForm();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsync("/api/products/1", noPrecondition)).StatusCode);
        Assert.Equal(8, (await admin.GetFromJsonAsync<JsonElement>("/api/products/1")).GetProperty("stockQuantity").GetInt32());
        using var refreshed = ApiTests.ProductForm(); refreshed.Add(new StringContent("8"), "ExpectedStockQuantity");
        (await admin.PutAsync("/api/products/1", refreshed)).EnsureSuccessStatusCode();
        Assert.Equal(7, (await admin.GetFromJsonAsync<JsonElement>("/api/products/1")).GetProperty("stockQuantity").GetInt32());
    }

    [Fact]
    public async Task Shipping_zone_writes_are_admin_only_validated_unique_and_versioned()
    {
        using var api = new ApiFactory(); using var admin = await api.AdminClient(); using var anonymous = api.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/shippingzones")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/shippingzones", new { governorate = "", city = "City", fee = -1, enabled = true })).StatusCode);
        (await admin.PostAsJsonAsync("/api/shippingzones", new { governorate = "Taiz", city = "", fee = 350, enabled = true })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/shippingzones", new { governorate = " taiz ", city = "", fee = 1, enabled = true })).StatusCode);
        var zone = await admin.GetFromJsonAsync<JsonElement>("/api/shippingzones/1"); var version = zone.GetProperty("version").GetGuid();
        (await admin.PutAsJsonAsync("/api/shippingzones/1", new { governorate = "Taiz", city = "", fee = 500, enabled = false, version })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync("/api/shippingzones/1", new { governorate = "Taiz", city = "", fee = 0, enabled = true, version })).StatusCode);
        Assert.Empty((await anonymous.GetFromJsonAsync<JsonElement[]>("/api/checkout/destinations"))!);
    }
}
