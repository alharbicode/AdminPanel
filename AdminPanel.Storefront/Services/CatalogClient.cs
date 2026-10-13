using System.Net;
using AdminPanel.Storefront.Models;
namespace AdminPanel.Storefront.Services;

public sealed class CatalogClient
{
    private readonly HttpClient _http;
    public CatalogClient(HttpClient http, IConfiguration configuration)
    {
        var value = configuration["ApiSettings:BaseUrl"];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.AbsolutePath.EndsWith('/') || uri.Query != "" || uri.Fragment != "" || uri.UserInfo != "")
            throw new InvalidOperationException("ApiSettings:BaseUrl must be an HTTPS catalog URL ending in /.");
        _http = http;
        _http.BaseAddress = uri;
        _http.Timeout = TimeSpan.FromSeconds(15);
        // Public reads only. No credentials, bearer tokens or administration client.
    }
    public Task<CatalogPage<Product>> Products(CatalogQuery query, CancellationToken ct) => Get<CatalogPage<Product>>(query.Url("products"), ct);
    public Task<CatalogPage<Category>> Categories(CatalogQuery query, CancellationToken ct) => Get<CatalogPage<Category>>(query.Url("categories"), ct);
    public async Task<Product?> Product(int id, CancellationToken ct)
    {
        using var response = await _http.GetAsync($"products/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Product>(ct);
    }
    private async Task<T> Get<T>(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw new HttpRequestException("Empty catalog response.");
    }
    public async Task<(byte[] Bytes, string Type)?> Image(int id, CancellationToken ct)
    {
        using var response = await _http.GetAsync($"products/{id}/image", HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) return null;
        var type = response.Content.Headers.ContentType?.MediaType;
        if (type is not ("image/jpeg" or "image/png" or "image/webp")) return null;
        // Bound transfer even if the upstream response omits Content-Length.
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + count > 5 * 1024 * 1024) return null;
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        return (output.ToArray(), type);
    }
}
