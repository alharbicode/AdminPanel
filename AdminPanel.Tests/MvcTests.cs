using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace AdminPanel.Tests;

public sealed class MvcTests : IDisposable
{
    private readonly ApiFactory _api = new();
    private readonly MvcFactory _mvc;
    public MvcTests() { _api.Seed(); _mvc = new MvcFactory(_api); }
    public void Dispose() { _mvc.Dispose(); _api.Dispose(); }

    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html,
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private async Task<HttpClient> Login()
    {
        var client = _mvc.Client();
        var token = Token(await client.GetStringAsync("/Account/Login"));
        Assert.NotEmpty(token);
        using var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Username"] = "admin", ["Password"] = _api.Password, ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.Contains("AdminPanel.Auth=") && c.Contains("secure", StringComparison.OrdinalIgnoreCase) && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        return client;
    }
    private static async Task<HttpResponseMessage> Post(HttpClient client, string url, string page, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = Token(await client.GetStringAsync(page));
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Categories")]
    [InlineData("/Products/Create")]
    [InlineData("/Orders")]
    public async Task Anonymous_MVC_requests_redirect_to_login(string path)
    {
        using var client = _mvc.Client();
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Login_and_mutations_require_antiforgery_and_logout_is_POST_only()
    {
        using var anonymous = _mvc.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsync("/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["Username"] = "admin", ["Password"] = _api.Password }))).StatusCode);
        using var client = await Login();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Categories/Create", new FormUrlEncodedContent(new Dictionary<string, string> { ["Name"] = "CSRF" }))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.GetAsync("/Account/Logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Account/Logout", "/", new())).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Products")).StatusCode);
    }

    [Fact]
    public async Task Categories_round_trip_through_MVC_and_API()
    {
        using var client = await Login();
        using var api = await _api.AdminClient();
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Categories/Create", "/Categories/Create", new() { ["Name"] = "MVC category" })).StatusCode);
        var categories = await api.GetFromJsonAsync<JsonElement[]>("/api/categories");
        var id = categories!.Single(c => c.GetProperty("name").GetString() == "MVC category").GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Categories/Edit", $"/Categories/Edit/{id}", new() { ["Id"] = id.ToString(), ["Name"] = "Edited" })).StatusCode);
        Assert.Contains("Edited", await client.GetStringAsync("/Categories"));
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Categories/Delete", $"/Categories/Delete/{id}", new() { ["Id"] = id.ToString() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Categories/Edit/{id}")).StatusCode);
    }

    [Fact]
    public async Task Products_round_trip_with_image_through_MVC_and_API()
    {
        using var client = await Login();
        using var api = await _api.AdminClient();
        using var form = ApiTests.ProductForm();
        form.Add(new StringContent(Token(await client.GetStringAsync("/Products/Create"))), "__RequestVerificationToken");
        form.Add(new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aK1cAAAAASUVORK5CYII=")), "ImageFile", "pixel.png");
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Products/Create", form)).StatusCode);
        var products = await api.GetFromJsonAsync<JsonElement[]>("/api/products");
        var product = products!.Single(p => p.GetProperty("name").GetString() == "Multipart product");
        var id = product.GetProperty("id").GetInt32();
        Assert.Equal(19.95m, product.GetProperty("price").GetDecimal());
        var image = product.GetProperty("imageUrl").GetString()!;
        Assert.StartsWith("/images/products/", image);
        using var imageResponse = await api.GetAsync(image);
        Assert.Equal(HttpStatusCode.OK, imageResponse.StatusCode);
        Assert.Equal("image/png", imageResponse.Content.Headers.ContentType!.MediaType);
        using var update = ApiTests.ProductForm();
        update.Add(new StringContent(id.ToString()), "Id");
        update.Add(new StringContent(Token(await client.GetStringAsync($"/Products/Edit/{id}"))), "__RequestVerificationToken");
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Products/Edit", update)).StatusCode);
        var after = await api.GetFromJsonAsync<JsonElement>($"/api/products/{id}");
        Assert.Equal(image, after.GetProperty("imageUrl").GetString());
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Products/Delete", $"/Products/Delete/{id}", new() { ["Id"] = id.ToString() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Products/Edit/{id}")).StatusCode);
    }

    [Fact]
    public async Task Order_views_render_items_and_status_and_cancel_use_the_API_contract()
    {
        using var client = await Login();
        using var api = await _api.AdminClient();
        using var created = await api.PostAsJsonAsync("/api/orders", new { customerId = 3, items = new[] { new { productId = 1, quantity = 2 } } });
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetInt32();
        Assert.Contains("customer", await client.GetStringAsync("/Orders"));
        Assert.Contains("Test product", await client.GetStringAsync($"/Orders/Details/{id}"));
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Orders/UpdateStatus", $"/Orders/Details/{id}",
            new() { ["id"] = id.ToString(), ["newStatus"] = "Confirmed" })).StatusCode);
        Assert.Equal("Confirmed", (await api.GetFromJsonAsync<JsonElement>($"/api/orders/{id}")).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Orders/Cancel", $"/Orders/Details/{id}", new() { ["id"] = id.ToString() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Orders/Details/{id}")).StatusCode);
    }
}
