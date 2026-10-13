using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AdminPanel.API.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdminPanel.Tests;

public sealed class StorefrontTests : IDisposable
{
    private readonly ApiFactory _api = new() { PublicProductIds = Enumerable.Range(1, 27).ToArray() };
    private readonly StorefrontFactory _store;
    public StorefrontTests() { StorefrontFixture.Seed(_api); _store = new StorefrontFactory(_api); }
    public void Dispose() { _store.Dispose(); _api.Dispose(); }

    [Fact]
    public async Task Public_catalog_fails_closed_without_publication_configuration()
    {
        using var closed = new ApiFactory(); closed.Seed(); using var client = closed.Client();
        var products = await client.GetFromJsonAsync<JsonElement>("/api/catalog/products");
        Assert.Equal(0, products.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/catalog/products/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/catalog/products/1/image")).StatusCode);
    }

    [Fact]
    public async Task Public_projection_excludes_private_inventory_and_enforces_visibility_everywhere()
    {
        using var client = _api.Client();
        var result = await client.GetFromJsonAsync<JsonElement>("/api/catalog/products?PageSize=48");
        Assert.Equal(25, result.GetProperty("totalCount").GetInt32());
        var items = result.GetProperty("items").EnumerateArray().ToList();
        Assert.DoesNotContain(items, p => new[] { 2, 3, 28 }.Contains(p.GetProperty("id").GetInt32()));
        Assert.All(items, p => {
            Assert.Equal("YER", p.GetProperty("currency").GetString());
            Assert.Equal(new[] { "categoryId", "categoryName", "currency", "description", "hasImage", "id", "name", "price", "unit" },
                p.EnumerateObject().Select(x => x.Name).Order().ToArray());
        });
        foreach (var id in new[] { 2, 3, 28, 999 })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/catalog/products/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/catalog/products/{id}/image")).StatusCode);
        }
        var categories = await client.GetFromJsonAsync<JsonElement>("/api/catalog/categories");
        Assert.Equal(3, categories.GetProperty("totalCount").GetInt32());
        Assert.DoesNotContain("Private category", categories.ToString());
        using var scope = _api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Products.Find(1)!.IsAvailable = false;
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SaveChanges();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/catalog/products/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/catalog/products/1/image")).StatusCode);
    }

    [Fact]
    public async Task Catalog_filters_search_sort_and_paging_are_bounded_and_deterministic()
    {
        using var client = _api.Client();
        var result = await client.GetFromJsonAsync<JsonElement>("/api/catalog/products?CategoryId=3&Search=Office&PageSize=3&Page=2&Sort=name");
        Assert.Equal(8, result.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, result.GetProperty("page").GetInt32());
        Assert.Equal(new[] { 14, 17, 20 }, result.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetInt32()));
        var extreme = await client.GetFromJsonAsync<JsonElement>("/api/catalog/products?Page=2147483647");
        Assert.Equal(3, extreme.GetProperty("page").GetInt32());
        Assert.Single(extreme.GetProperty("items").EnumerateArray());
        var category = await client.GetFromJsonAsync<JsonElement>("/api/catalog/categories?Search=Private");
        Assert.Equal(0, category.GetProperty("totalCount").GetInt32());
        var byCategoryName = await client.GetFromJsonAsync<JsonElement>("/api/catalog/products?Search=" + Uri.EscapeDataString("الفواكه"));
        Assert.Equal(9, byCategoryName.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("Page=0")]
    [InlineData("PageSize=49")]
    [InlineData("CategoryId=-1")]
    [InlineData("Sort=stock")]
    [InlineData("Search=xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public async Task Invalid_public_queries_are_rejected(string query)
    {
        using var api = _api.Client(); using var store = _store.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("/api/catalog/products?" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("/api/catalog/categories?" + query)).StatusCode);
        var response = await store.GetAsync("/Catalog?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("dir=\"rtl\"", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await store.GetAsync("/Catalog/Categories?" + query)).StatusCode);
    }

    [Fact]
    public async Task Public_image_is_bounded_safe_and_does_not_fetch_external_paths()
    {
        using var client = _api.Client(); using var store = _store.Client();
        var image = await client.GetAsync("/api/catalog/products/1/image");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType!.MediaType);
        Assert.True(image.Headers.CacheControl!.NoStore);
        Assert.Equal("image/jpeg", (await store.GetAsync("/Catalog/Image/1")).Content.Headers.ContentType!.MediaType);
        using var scope = _api.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        foreach (var bad in new[] { "/images/products/../../appsettings.json", "https://example.test/private.jpg", "/images/products/evil.svg" })
        {
            db.Products.Find(1)!.ImageUrl = bad; db.SaveChanges();
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/catalog/products/1/image")).StatusCode);
            Assert.Equal("/images/product-placeholder.svg", (await store.GetAsync("/Catalog/Image/1")).Headers.Location!.ToString());
        }
    }

    [Fact]
    public async Task Concurrent_storefront_image_requests_return_actual_catalog_images()
    {
        using var store = _store.Client();
        await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            using var response = await store.GetAsync("/Catalog/Image/27");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/jpeg", response.Content.Headers.ContentType!.MediaType);
            Assert.True((await response.Content.ReadAsByteArrayAsync()).Length > 0);
        }));
    }

    [Fact]
    public async Task Administration_stays_protected_and_public_surface_has_no_writes()
    {
        using var anonymous = _api.Client();
        foreach (var path in new[] { "products", "categories", "orders", "shippingoptions", "discounts", "contentpages", "dashboard", "products/admin/list" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/" + path)).StatusCode);
        foreach (var role in new[] { "customer", "staff" })
        {
            using var client = _api.Client(); client.DefaultRequestHeaders.Authorization = new("Bearer", await _api.Login(client, role));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/products")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/orders", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/catalog/products")).StatusCode);
        }
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
        {
            var response = await anonymous.SendAsync(new HttpRequestMessage(method, "/api/catalog/products/1"));
            Assert.False(response.IsSuccessStatusCode);
        }
    }

    [Fact]
    public async Task Empty_and_unavailable_catalogs_have_safe_localized_pages()
    {
        using var closed = new ApiFactory(); closed.Seed();
        using var emptyStore = new StorefrontFactory(closed); using var emptyClient = emptyStore.Client();
        var empty = await emptyClient.GetStringAsync("/");
        Assert.Contains("نعمل على تجهيز", WebUtility.HtmlDecode(empty));
        using var unavailable = new StorefrontFactory(_api, () => new UnavailableCatalog());
        using var client = unavailable.Client();
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("الكتالوج غير متاح", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("internal-only", html);
    }

    private sealed class UnavailableCatalog : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("internal-only diagnostic");
    }

    [Fact]
    public async Task Storefront_renders_Arabic_first_preserves_original_content_and_switches_safely()
    {
        using var client = _store.Client();
        var html = await client.GetStringAsync("/");
        Assert.Contains("lang=\"ar\" dir=\"rtl\"", html);
        Assert.Contains("Alharbi Store", html);
        Assert.DoesNotContain("HeroTitle", html);
        Assert.Contains("اكتشف", WebUtility.HtmlDecode(html));
        var details = await client.GetStringAsync("/Catalog/Details/27");
        Assert.Contains("&lt;script&gt;", details);
        Assert.DoesNotContain("<script>unsafe()", details);
        Assert.Contains("YER", details);
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var csrf = await client.PostAsync("/Language/Change", new FormUrlEncodedContent(new Dictionary<string,string>{{"culture","en"}}));
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        var changed = await client.PostAsync("/Language/Change", new FormUrlEncodedContent(new Dictionary<string,string>
        { ["culture"]="en", ["returnUrl"]="/Catalog?Search=Office&Page=2", ["__RequestVerificationToken"]=token }));
        Assert.Equal("/Catalog?Search=Office&Page=2", changed.Headers.Location!.ToString());
        Assert.Contains(changed.Headers.GetValues("Set-Cookie"), c => c.Contains("AlharbiStore.Culture") && c.Contains("secure") && c.Contains("httponly"));
        var english = await client.GetStringAsync("/");
        Assert.Contains("lang=\"en\" dir=\"ltr\"", english);
        Assert.Contains("Discover your next everyday favorite.", english);
        var redirect = await client.PostAsync("/Language/Change", new FormUrlEncodedContent(new Dictionary<string,string>
        { ["culture"]="ar", ["returnUrl"]="https://example.test", ["__RequestVerificationToken"]=token }));
        Assert.Equal("/", redirect.Headers.Location!.ToString());
        var invalid = await client.PostAsync("/Language/Change", new FormUrlEncodedContent(new Dictionary<string,string>
        { ["culture"]="fr", ["__RequestVerificationToken"]=token }));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Catalog/Details/28")).StatusCode);
    }
}
