extern alias mvc;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using AdminPanel.API.Data;
using AdminPanel.API.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AdminPanel.Tests;

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly string? _sqlServerConnection = CreateSqlServerConnection();
    private static string? CreateSqlServerConnection()
    {
        var template = Environment.GetEnvironmentVariable("ADMINPANEL_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(template)) return null;
        var settings = new SqlConnectionStringBuilder(template);
        if (settings.DataSource is not ("localhost,1433" or "127.0.0.1,1433"))
            throw new InvalidOperationException("SQL integration tests only accept a disposable loopback server on port 1433.");
        settings.InitialCatalog = "AdminPanel_Test_" + Guid.NewGuid().ToString("N");
        return settings.ConnectionString;
    }
    public string Secret { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    public string Password { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    public string UploadRoot { get; } = Path.Combine(Path.GetTempPath(), "AdminPanel-tests-" + Guid.NewGuid().ToString("N"));

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:SecretKey"] = Secret, ["JwtSettings:Issuer"] = "AdminPanel.API",
            ["JwtSettings:Audience"] = "AdminPanel.MVC", ["JwtSettings:LifetimeMinutes"] = "60",
            ["ConnectionStrings:DefaultConnection"] = "Server=unused;Database=unused"
        }));
        return base.CreateHost(builder);
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(Path.Combine(TestPaths.RepositoryRoot, "AdminPanel"));
        builder.UseEnvironment("Testing");
        Directory.CreateDirectory(UploadRoot);
        builder.UseWebRoot(UploadRoot);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            if (_sqlServerConnection is null)
            {
                _connection.Open();
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(_connection));
            }
            else services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(_sqlServerConnection));
        });
    }
    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    public void Seed()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (_sqlServerConnection is null) db.Database.EnsureCreated();
        else db.Database.Migrate();
        if (db.Users.Any()) return;
        var hasher = new PasswordHasher<User>();
        foreach (var role in new[] { "Admin", "Staff", "Customer" })
        {
            var user = new User { Username = role.ToLowerInvariant(), Email = role + "@example.test", Role = role };
            user.Password = hasher.HashPassword(user, Password);
            db.Users.Add(user);
        }
        db.Categories.Add(new Category { Name = "Tools" });
        db.SaveChanges();
        db.Products.Add(new Product { Name = "Test product", Price = 12.50m, StockQuantity = 10, CategoryId = 1, IsAvailable = true });
        db.ShippingOptions.Add(new ShippingOption { Name = "Standard", Area = "Test area", Price = 3m });
        db.SaveChanges();
    }
    public async Task<string> Login(HttpClient client, string username = "admin")
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password = Password });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("token").GetString()!;
    }
    public async Task<HttpClient> AdminClient()
    {
        Seed();
        var client = Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await Login(client));
        return client;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && _sqlServerConnection is not null)
        {
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(_sqlServerConnection).Options);
            db.Database.EnsureDeleted();
        }
        base.Dispose(disposing);
        if (!disposing) return;
        _connection.Dispose();
        if (Directory.Exists(UploadRoot)) Directory.Delete(UploadRoot, recursive: true);
    }
}

public sealed class MvcFactory(ApiFactory api, Func<HttpMessageHandler>? handler = null) : WebApplicationFactory<mvc::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(Path.Combine(TestPaths.RepositoryRoot, "AdminPanel.MVC"));
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        { ["ApiSettings:BaseUrl"] = "https://localhost/api/" }));
        builder.ConfigureServices(services => services.AddHttpClient<mvc::AdminPanel.MVC.Services.ApiClient>()
            .ConfigurePrimaryHttpMessageHandler(() => handler?.Invoke() ?? api.Server.CreateHandler()));
    }
    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
}

public static class TestPaths
{
    public static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AdminPanel.sln")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Run tests from an AdminPanel checkout.");
        }
    }
}
