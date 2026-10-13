using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AdminPanel.API.Data;
using AdminPanel.API.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdminPanel.Tests;

public sealed class AdministrationTests : IDisposable
{
    private readonly ApiFactory _api = new();
    private readonly MvcFactory _mvc;
    public AdministrationTests() { _api.Seed(); _mvc = new MvcFactory(_api); }
    public void Dispose() { _mvc.Dispose(); _api.Dispose(); }

    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html,
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private async Task<HttpClient> Login(string username = "admin")
    {
        var client = _mvc.Client();
        var response = await Post(client, "/Account/Login", "/Account/Login",
            new() { ["Username"] = username, ["Password"] = _api.Password });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }
    private static async Task<HttpResponseMessage> Post(HttpClient client, string url, string page, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = Token(await client.GetStringAsync(page));
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }
    private static Dictionary<string, string> Fields(string resource, string name) => resource switch
    {
        "ShippingOptions" => new() { ["Name"] = name, ["Area"] = "North", ["Price"] = "4.25" },
        "Discounts" => new() { ["Name"] = name, ["ProductId"] = "1", ["Percentage"] = "12.50",
            ["StartDate"] = "2030-01-01T10:00:00", ["EndDate"] = "2030-02-01T10:00:00" },
        _ => new() { ["Title"] = name, ["Slug"] = "Phase Two", ["Content"] = "<script>alert('stored')</script>\nPage body" }
    };

    [Theory]
    [InlineData("ShippingOptions", "name", "shippingoptions/")]
    [InlineData("Discounts", "name", "discounts/")]
    [InlineData("ContentPages", "title", "contentpages/by-id/")]
    public async Task Management_CRUD_round_trips_through_real_MVC_and_API(string resource, string nameField, string read)
    {
        using var client = await Login();
        using var api = await _api.AdminClient();
        var fields = Fields(resource, "Phase two record");
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, $"/{resource}/Create", $"/{resource}/Create", fields)).StatusCode);
        var records = await api.GetFromJsonAsync<JsonElement[]>("/api/" + resource);
        var id = records!.Single(x => x.GetProperty(nameField).GetString() == "Phase two record").GetProperty("id").GetInt32();
        var list = await client.GetStringAsync($"/{resource}");
        Assert.Contains("Phase two record", list);
        Assert.Contains("created.", list);
        Assert.Contains("Details", list);
        var details = await client.GetStringAsync($"/{resource}/Details/{id}");
        Assert.Contains("Phase two record", details);
        if (resource == "ContentPages")
        {
            Assert.DoesNotContain("<script>alert", details);
            Assert.Contains("&lt;script&gt;", details);
            Assert.Contains("phase-two", details);
        }
        if (resource == "Discounts")
        {
            Assert.Contains("UTC", details);
            Assert.Contains("Test product", details);
        }
        fields[nameField == "title" ? "Title" : "Name"] = "Updated record";
        fields["Id"] = id.ToString();
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, $"/{resource}/Edit", $"/{resource}/Edit/{id}", fields)).StatusCode);
        var updated = await api.GetFromJsonAsync<JsonElement>("/api/" + read + id);
        Assert.Equal("Updated record", updated.GetProperty(nameField).GetString());
        // Opening confirmation must never mutate the record.
        Assert.Contains("cannot be undone", await client.GetStringAsync($"/{resource}/Delete/{id}"));
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/" + read + id)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, $"/{resource}/Delete", $"/{resource}/Delete/{id}", new() { ["Id"] = id.ToString() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync("/api/" + read + id)).StatusCode);
        foreach (var action in new[] { "Details", "Edit", "Delete" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/{resource}/{action}/{id}")).StatusCode);
    }

    [Fact]
    public async Task All_new_reads_and_mutations_enforce_Admin_and_antiforgery()
    {
        var reads = new[] { "dashboard", "shippingoptions/admin/list", "discounts/admin/list", "contentpages/admin/list",
            "shippingoptions/1", "discounts/1", "contentpages/by-id/1" };
        using var anonymousApi = _api.Client();
        using var anonymousMvc = _mvc.Client();
        foreach (var endpoint in reads) Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousApi.GetAsync("/api/" + endpoint)).StatusCode);
        foreach (var resource in new[] { "ShippingOptions", "Discounts", "ContentPages" })
        {
            foreach (var action in new[] { "", "/Create", "/Edit/1", "/Details/1", "/Delete/1" })
                Assert.Equal(HttpStatusCode.Redirect, (await anonymousMvc.GetAsync("/" + resource + action)).StatusCode);
        }
        foreach (var role in new[] { "staff", "customer" })
        {
            using var api = _api.Client();
            api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _api.Login(api, role));
            foreach (var endpoint in reads) Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync("/api/" + endpoint)).StatusCode);
            foreach (var resource in new[] { "shippingoptions", "discounts", "contentpages" })
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await api.PostAsJsonAsync("/api/" + resource, new { })).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await api.PutAsJsonAsync("/api/" + resource + "/1", new { })).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await api.DeleteAsync("/api/" + resource + "/1")).StatusCode);
            }
            using var mvc = _mvc.Client();
            var denied = await Post(mvc, "/Account/Login", "/Account/Login", new() { ["Username"] = role, ["Password"] = _api.Password });
            Assert.Contains("administrator account is required", await denied.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Redirect, (await mvc.GetAsync("/Discounts")).StatusCode);
        }
        using var admin = await Login();
        foreach (var resource in new[] { "ShippingOptions", "Discounts", "ContentPages" })
            foreach (var action in new[] { "Create", "Edit", "Delete" })
                Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/{resource}/{action}", new FormUrlEncodedContent(Fields(resource, "Rejected")))).StatusCode);
    }

    [Fact]
    public async Task Validation_preserves_fields_and_does_not_write_invalid_records()
    {
        using var client = await Login();
        using var api = await _api.AdminClient();
        var shipping = Fields("ShippingOptions", "Keep my entry");
        shipping["Price"] = "1.001";
        var response = await Post(client, "/ShippingOptions/Create", "/ShippingOptions/Create", shipping);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("two decimal places", html);
        Assert.Contains("Keep my entry", html);
        Assert.Single((await api.GetFromJsonAsync<JsonElement[]>("/api/shippingoptions"))!);

        var discount = Fields("Discounts", "Invalid dates");
        discount["EndDate"] = discount["StartDate"];
        response = await Post(client, "/Discounts/Create", "/Discounts/Create", discount);
        Assert.Contains("End date must be after", await response.Content.ReadAsStringAsync());
        discount["EndDate"] = "2030-02-01T00:00:00";
        discount["Percentage"] = "101";
        response = await Post(client, "/Discounts/Create", "/Discounts/Create", discount);
        Assert.Contains("between 0 and 100", await response.Content.ReadAsStringAsync());
        discount["Percentage"] = "5";
        discount["ProductId"] = "999999";
        response = await Post(client, "/Discounts/Create", "/Discounts/Create", discount);
        Assert.Contains("Choose an existing product", await response.Content.ReadAsStringAsync());
        Assert.Empty((await api.GetFromJsonAsync<JsonElement[]>("/api/discounts"))!);

        var page = Fields("ContentPages", "Keep title");
        page["Slug"] = "../bad";
        response = await Post(client, "/ContentPages/Create", "/ContentPages/Create", page);
        Assert.Contains("single spaces or hyphens", await response.Content.ReadAsStringAsync());
        Assert.Empty((await api.GetFromJsonAsync<JsonElement[]>("/api/contentpages"))!);
    }

    [Fact]
    public async Task Slug_conflicts_and_referenced_shipping_deletion_are_actionable()
    {
        using var client = await Login();
        using var api = await _api.AdminClient();
        var fields = Fields("ContentPages", "First");
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/ContentPages/Create", "/ContentPages/Create", fields)).StatusCode);
        fields["Title"] = "Duplicate";
        var duplicate = await Post(client, "/ContentPages/Create", "/ContentPages/Create", fields);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("slug is already in use", await duplicate.Content.ReadAsStringAsync());
        Assert.Single((await api.GetFromJsonAsync<JsonElement[]>("/api/contentpages"))!);
        (await api.PostAsJsonAsync("/api/orders", new { requestKey = Guid.NewGuid(), customerId = 3, shippingOptionId = 1, items = new[] { new { productId = 1, quantity = 1 } } })).EnsureSuccessStatusCode();
        var deletion = await Post(client, "/ShippingOptions/Delete", "/ShippingOptions/Delete/1", new() { ["Id"] = "1" });
        Assert.Equal(HttpStatusCode.OK, deletion.StatusCode);
        Assert.Contains("used by an order", await deletion.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/shippingoptions/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(client, "/ShippingOptions/Edit", "/ShippingOptions/Edit/1",
            new() { ["Id"] = "99999", ["Name"] = "Gone", ["Area"] = "North", ["Price"] = "3" })).StatusCode);
    }

    [Fact]
    public async Task Paging_search_and_filters_are_applied_before_database_pagination()
    {
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (var i = 1; i <= 25; i++)
            {
                db.ShippingOptions.Add(new ShippingOption { Name = $"Option {i:00}", Area = i <= 15 ? "North" : "South", Price = i });
                db.ContentPages.Add(new ContentPage { Title = $"Page {i:00}", Slug = $"page-{i:00}", Content = new string('x', 1000) });
                db.Discounts.Add(new Discount { Name = $"Discount {i:00}", ProductId = 1, Percentage = i,
                    StartDate = DateTime.UtcNow.AddDays(i <= 15 ? -2 : 2), EndDate = DateTime.UtcNow.AddDays(i <= 15 ? -1 : 3) });
            }
            await db.SaveChangesAsync();
        }
        using var api = await _api.AdminClient();
        var shipping = await api.GetFromJsonAsync<JsonElement>("/api/shippingoptions/admin/list?Search=Option&Area=North&Page=2&PageSize=10");
        Assert.Equal(15, shipping.GetProperty("totalCount").GetInt32());
        Assert.Equal(5, shipping.GetProperty("items").GetArrayLength());
        Assert.Equal("Option 11", shipping.GetProperty("items")[0].GetProperty("name").GetString());
        var discounts = await api.GetFromJsonAsync<JsonElement>("/api/discounts/admin/list?ProductId=1&Period=ended&Page=2&PageSize=10");
        Assert.Equal(15, discounts.GetProperty("totalCount").GetInt32());
        Assert.Equal(5, discounts.GetProperty("items").GetArrayLength());
        Assert.Equal(10, (await api.GetFromJsonAsync<JsonElement>("/api/discounts/admin/list?Period=upcoming")).GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await api.GetFromJsonAsync<JsonElement>("/api/discounts/admin/list?Period=current")).GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await api.GetFromJsonAsync<JsonElement>("/api/discounts/admin/list?ProductId=9999")).GetProperty("totalCount").GetInt32());
        var pages = await api.GetFromJsonAsync<JsonElement>("/api/contentpages/admin/list?Search=page-0&Page=2147483647&PageSize=5");
        Assert.Equal(9, pages.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, pages.GetProperty("page").GetInt32());
        Assert.Equal("", pages.GetProperty("items")[0].GetProperty("content").GetString());

        using var client = await Login();
        var html = await client.GetStringAsync("/ShippingOptions?Search=Option&Area=North&PageSize=10");
        Assert.Contains("15 record(s)", html);
        Assert.Contains("Area=North", html);
        Assert.Contains("Page=2", html);
        Assert.DoesNotContain("Option 11", html);
        Assert.Contains("Option 11", await client.GetStringAsync("/ShippingOptions?Search=Option&Area=North&PageSize=10&Page=2"));
        Assert.Contains("15 record(s)", await client.GetStringAsync("/Discounts?Period=ended&ProductId=1"));
        Assert.Contains("Page 06", await client.GetStringAsync("/ContentPages?Search=page-0&PageSize=5&Page=2"));
        Assert.Contains("No content pages found", await client.GetStringAsync("/ContentPages?Search=nonexistent"));
        var injection = await client.GetStringAsync("/ContentPages?Search=%3Cscript%3E");
        Assert.DoesNotContain("value=\"<script>", injection);
        Assert.Contains("&lt;script&gt;", injection);
    }

    [Theory]
    [InlineData("Page=0")]
    [InlineData("PageSize=101")]
    [InlineData("ProductId=-1")]
    [InlineData("Period=unknown")]
    public async Task Invalid_query_returns_API_validation_and_safe_MVC_feedback(string query)
    {
        using var api = await _api.AdminClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("/api/discounts/admin/list?" + query)).StatusCode);
        using var mvc = await Login();
        var response = await mvc.GetAsync("/Discounts?" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("validation-summary-errors", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Dashboard_uses_current_counts_statuses_and_five_recent_orders()
    {
        using var api = await _api.AdminClient();
        var initial = await api.GetFromJsonAsync<JsonElement>("/api/dashboard");
        Assert.Equal(1, initial.GetProperty("productCount").GetInt32());
        Assert.Equal(1, initial.GetProperty("categoryCount").GetInt32());
        Assert.Equal(1, initial.GetProperty("shippingOptionCount").GetInt32());
        Assert.Equal(0, initial.GetProperty("orderCount").GetInt32());
        Assert.Equal(0, initial.GetProperty("discountCount").GetInt32());
        Assert.Equal(0, initial.GetProperty("contentPageCount").GetInt32());
        using var client = await Login();
        Assert.Contains("No orders yet", await client.GetStringAsync("/"));
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Products.Add(new Product { Name = "Unavailable", CategoryId = 1, IsAvailable = false, StockQuantity = 0 });
            db.Discounts.Add(new Discount { Name = "Window", ProductId = 1, StartDate = DateTime.UtcNow, EndDate = DateTime.UtcNow.AddDays(1) });
            db.ContentPages.Add(new ContentPage { Title = "About", Slug = "about", Content = "Body" });
            // Same timestamp exercises the stable ID tie-breaker.
            var date = DateTime.UtcNow;
            for (var i = 1; i <= 6; i++) db.Orders.Add(new Order { CustomerId = 3, Status = i <= 2 ? "Pending" : "Confirmed", OrderDate = date, TotalPrice = i });
            await db.SaveChangesAsync();
        }
        var dashboard = await api.GetFromJsonAsync<JsonElement>("/api/dashboard");
        Assert.Equal(2, dashboard.GetProperty("productCount").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("availableProductCount").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("outOfStockProductCount").GetInt32());
        Assert.Equal(6, dashboard.GetProperty("orderCount").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("discountCount").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("contentPageCount").GetInt32());
        var statuses = dashboard.GetProperty("ordersByStatus").EnumerateArray().ToDictionary(x => x.GetProperty("status").GetString()!, x => x.GetProperty("count").GetInt32());
        Assert.Equal(2, statuses["Pending"]);
        Assert.Equal(4, statuses["Confirmed"]);
        Assert.Equal(new[] { 6, 5, 4, 3, 2 }, dashboard.GetProperty("recentOrders").EnumerateArray().Select(x => x.GetProperty("id").GetInt32()));
        var html = await client.GetStringAsync("/");
        Assert.Contains("Products with zero stock", html);
        Assert.Contains("Confirmed", html);
        Assert.Contains("customer", html);
        Assert.Contains("/Orders/Details/6", html);
    }
}
