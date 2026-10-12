using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AdminPanel.API.Data;
using AdminPanel.API.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdminPanel.Tests;

public sealed class ListTests : IDisposable
{
    private readonly ApiFactory _api = new();
    public ListTests()
    {
        _api.Seed();
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        for (var i = 1; i <= 25; i++)
        {
            db.Categories.Add(new Category { Name = $"Category {i:00}", Description = "Lookup text" });
            db.Products.Add(new Product { Name = $"Product {i:00}", Description = "Lookup text", CategoryId = 1,
                StockQuantity = i <= 5 ? 0 : i, IsAvailable = i % 2 == 0, Price = i });
            db.Orders.Add(new Order { CustomerId = 3, Status = i <= 15 ? "Pending" : "Confirmed", OrderDate = new DateTime(2030, 1, i, 12, 0, 0, DateTimeKind.Utc), TotalPrice = i });
        }
        db.SaveChanges();
    }
    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Categories_filter_search_sort_and_page_in_the_database()
    {
        using var client = await _api.AdminClient();
        var result = await client.GetFromJsonAsync<JsonElement>("/api/categories/admin/list?Search=Lookup&HasProducts=false&Sort=name-desc&Page=2");
        Assert.Equal(25, result.GetProperty("totalCount").GetInt32());
        Assert.Equal(10, result.GetProperty("items").GetArrayLength());
        Assert.Equal("Category 15", result.GetProperty("items")[0].GetProperty("name").GetString());
        result = await client.GetFromJsonAsync<JsonElement>("/api/categories/admin/list?HasProducts=true");
        Assert.Equal(1, result.GetProperty("totalCount").GetInt32());
        Assert.Equal("Tools", result.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Products_combine_filters_sort_stably_and_bound_pages()
    {
        using var client = await _api.AdminClient();
        var path = "/api/products/admin/list?Search=Lookup&CategoryId=1&Available=true&Stock=in-stock&Sort=stock-desc&PageSize=3";
        var result = await client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(10, result.GetProperty("totalCount").GetInt32());
        Assert.Equal(new[] { 24, 22, 20 }, result.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("stockQuantity").GetInt32()));
        result = await client.GetFromJsonAsync<JsonElement>(path + "&Page=2147483647");
        Assert.Equal(4, result.GetProperty("page").GetInt32());
        Assert.Single(result.GetProperty("items").EnumerateArray());
        result = await client.GetFromJsonAsync<JsonElement>("/api/products/admin/list?Stock=out-of-stock&Sort=stock");
        Assert.Equal(5, result.GetProperty("totalCount").GetInt32());
        // Same stock value must use the ID tie-breaker.
        var ids = result.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt32()).ToArray();
        Assert.Equal(ids.OrderBy(x => x), ids);
    }

    [Fact]
    public async Task Orders_search_customer_or_id_and_filter_inclusive_UTC_dates()
    {
        using var client = await _api.AdminClient();
        var result = await client.GetFromJsonAsync<JsonElement>("/api/orders/admin/list?Search=customer&Status=Pending&From=2030-01-03&To=2030-01-07&Sort=oldest&PageSize=2&Page=2");
        Assert.Equal(5, result.GetProperty("totalCount").GetInt32());
        Assert.Equal(new[] { 5, 6 }, result.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt32()));
        result = await client.GetFromJsonAsync<JsonElement>("/api/orders/admin/list?Search=Customer%40example.test&Sort=newest");
        Assert.Equal(25, result.GetProperty("totalCount").GetInt32());
        Assert.Equal(25, result.GetProperty("items")[0].GetProperty("id").GetInt32());
        result = await client.GetFromJsonAsync<JsonElement>("/api/orders/admin/list?Search=17");
        Assert.Equal(17, Assert.Single(result.GetProperty("items").EnumerateArray()).GetProperty("id").GetInt32());
        // The day upper bound must not overflow DateTime.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/orders/admin/list?To=9999-12-31")).StatusCode);
    }

    [Fact]
    public async Task Product_lookup_is_bounded_searchable_metadata_only()
    {
        using var client = await _api.AdminClient();
        var result = await client.GetFromJsonAsync<JsonElement>("/api/products/admin/lookup?Search=Lookup&PageSize=100");
        Assert.Equal(20, result.GetProperty("pageSize").GetInt32());
        Assert.Equal(25, result.GetProperty("totalCount").GetInt32());
        Assert.Equal(20, result.GetProperty("items").GetArrayLength());
        Assert.Equal(new[] { "id", "name" }, result.GetProperty("items")[0].EnumerateObject().Select(x => x.Name));
        result = await client.GetFromJsonAsync<JsonElement>("/api/products/admin/lookup?Search=Lookup&Page=2");
        Assert.Equal(5, result.GetProperty("items").GetArrayLength());
        result = await client.GetFromJsonAsync<JsonElement>("/api/products/admin/lookup?Search=%3Cscript%3E");
        Assert.Equal(0, result.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("Sort=price;DROP")]
    [InlineData("PageSize=101")]
    [InlineData("CategoryId=0")]
    [InlineData("Available=invalid")]
    [InlineData("Stock=unknown")]
    [InlineData("Status=Cancelled")]
    [InlineData("From=2030-02-01&To=2030-01-01")]
    public async Task Invalid_list_queries_are_rejected(string query)
    {
        using var client = await _api.AdminClient();
        foreach (var route in new[] { "categories/admin/list", "products/admin/list", "orders/admin/list", "products/admin/lookup" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/" + route + "?" + query)).StatusCode);
    }

    [Fact]
    public async Task All_new_list_and_lookup_endpoints_require_Admin()
    {
        using var client = _api.Client();
        var routes = new[] { "categories/admin/list", "products/admin/list", "orders/admin/list", "products/admin/lookup" };
        foreach (var route in routes) Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/" + route)).StatusCode);
        foreach (var role in new[] { "staff", "customer" })
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _api.Login(client, role));
            foreach (var route in routes) Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/" + route)).StatusCode);
        }
    }
}
