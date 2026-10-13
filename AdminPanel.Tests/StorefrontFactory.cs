extern alias storefront;
using AdminPanel.API.Data;
using AdminPanel.API.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdminPanel.Tests;

public sealed class StorefrontFactory(ApiFactory api, Func<HttpMessageHandler>? handler = null) : WebApplicationFactory<storefront::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(Path.Combine(TestPaths.RepositoryRoot, "AdminPanel.Storefront"));
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        { ["ApiSettings:BaseUrl"] = "https://localhost/api/catalog/", ["Store:Name"] = "Alharbi Store" }));
        builder.ConfigureServices(services => services.AddHttpClient<storefront::AdminPanel.Storefront.Services.CatalogClient>()
            .ConfigurePrimaryHttpMessageHandler(() => handler?.Invoke() ?? api.Server.CreateHandler()));
        builder.ConfigureServices(services => services.AddHttpClient<storefront::AdminPanel.Storefront.Services.CustomerClient>()
            .ConfigurePrimaryHttpMessageHandler(() => handler?.Invoke() ?? api.Server.CreateHandler()));
    }
    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
}

// Disposable browser/integration data only; never part of application startup.
public static class StorefrontFixture
{
    public static void Seed(ApiFactory api)
    {
        api.Seed();
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Categories.Find(1)!.Name = "الفواكه";
        db.Categories.AddRange(new Category { Name = "المنزل" }, new Category { Name = "المكتب" }, new Category { Name = "Private category" });
        db.SaveChanges();
        var first = db.Products.Find(1)!;
        first.Name = "تفاح أحمر"; first.Description = "Fresh apple · وصف المنتج الأصلي"; first.Price = 1250.50m; first.ImageUrl = "/images/products/fixture.jpg";
        for (var id = 2; id <= 28; id++)
        {
            var category = id % 3 + 1;
            var name = category switch { 1 => "تفاح أحمر", 2 => "منتج منزلي", _ => "Office item" };
            db.Products.Add(new Product { Name = name + $" {id:00}", Description = "Catalog fixture <script>unsafe()</script>",
                Price = id * 250m, StockQuantity = id == 3 ? 0 : 10, IsAvailable = id != 2,
                CategoryId = category, ImageUrl = category == 1 ? "/images/products/fixture.jpg" : "", Unit = "Piece" });
        }
        db.SaveChanges();
        var folder = Path.Combine(api.UploadRoot, "images", "products");
        Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(TestPaths.RepositoryRoot, "AdminPanel", "wwwroot", "images", "products",
            "373935e3-68a3-4599-80f3-f5c3b83f4efc.jpg"), Path.Combine(folder, "fixture.jpg"));
    }
}
