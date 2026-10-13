using System.Net;
using System.Net.Http.Json;
using System.Text;
using AdminPanel.API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdminPanel.Tests;

public sealed class EdgeCaseTests
{
    [Fact]
    public async Task Null_lines_and_excess_precision_return_validation_errors()
    {
        using var api = new ApiFactory();
        using var client = await api.AdminClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/orders",
            new StringContent("{\"customerId\":3,\"items\":[null]}", Encoding.UTF8, "application/json"))).StatusCode);
        // Replace the price field rather than creating an ambiguous duplicate form key.
        using var precise = new MultipartFormDataContent();
        precise.Add(new StringContent("Precision"), "Name");
        precise.Add(new StringContent("1.001"), "Price");
        precise.Add(new StringContent("1"), "CategoryId");
        precise.Add(new StringContent("Piece"), "Unit");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/products", precise)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/discounts",
            new { name = "Invalid", percentage = 101, productId = 1, startDate = DateTime.UtcNow, endDate = DateTime.UtcNow.AddDays(-1) })).StatusCode);
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var product = await db.Products.FindAsync(1);
        product!.IsAvailable = false;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/orders",
            new { requestKey = Guid.NewGuid(), customerId = 3, items = new[] { new { productId = 1, quantity = 1 } } })).StatusCode);
        Assert.Equal(10, (await db.Products.AsNoTracking().SingleAsync()).StockQuantity);
    }

    [Fact]
    public async Task Upstream_network_errors_are_safe_and_actionable()
    {
        using var api = new ApiFactory();
        api.Seed();
        using var mvc = new MvcFactory(api, () => new UnavailableApi());
        using var client = mvc.Client();
        var html = await client.GetStringAsync("/Account/Login");
        var token = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        using var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Username"] = "admin", ["Password"] = api.Password, ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var message = await response.Content.ReadAsStringAsync();
        Assert.Contains("try again", message);
        Assert.DoesNotContain("internal-only", message);
    }

    private sealed class UnavailableApi : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("internal-only diagnostic");
    }
}
