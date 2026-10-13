using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using AdminPanel.MVC.Models;
using Microsoft.AspNetCore.Authentication;

namespace AdminPanel.MVC.Services;

public class ApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IHttpContextAccessor _accessor;
    public ApiClient(HttpClient httpClient, IConfiguration configuration, IHttpContextAccessor accessor)
    {
        _httpClient = httpClient;
        _accessor = accessor;
        var url = configuration["ApiSettings:BaseUrl"];
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !uri.AbsolutePath.EndsWith('/') || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("ApiSettings:BaseUrl must be an absolute HTTP(S) URL ending in /.");
        _httpClient.BaseAddress = uri;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string endpoint, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, endpoint) { Content = content };
        var context = _accessor.HttpContext;
        var token = context is null ? null : await context.GetTokenAsync("access_token");
        if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _httpClient.SendAsync(request, context?.RequestAborted ?? CancellationToken.None);
        if (endpoint != "auth/login" && response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            var status = response.StatusCode;
            response.Dispose();
            throw new HttpRequestException("API access denied.", null, status);
        }
        return response;
    }

    public async Task<T?> GetAsync<T>(string endpoint)
    {
        using var response = await SendAsync(HttpMethod.Get, endpoint);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>();
    }
    public Task<HttpResponseMessage> PostAsync<T>(string endpoint, T data) =>
        SendAsync(HttpMethod.Post, endpoint, JsonContent.Create(data));
    public Task<HttpResponseMessage> PutAsync<T>(string endpoint, T data) =>
        SendAsync(HttpMethod.Put, endpoint, JsonContent.Create(data));
    public Task<HttpResponseMessage> DeleteAsync(string endpoint) => SendAsync(HttpMethod.Delete, endpoint);

    public Task<HttpResponseMessage> SaveProductAsync(ProductViewModel model, bool update)
    {
        var form = new MultipartFormDataContent();
        void Add(string name, string value) => form.Add(new StringContent(value), name);
        Add("Name", model.Name); Add("Description", model.Description ?? "");
        Add("Price", model.Price.ToString(CultureInfo.InvariantCulture));
        Add("StockQuantity", model.StockQuantity.ToString(CultureInfo.InvariantCulture));
        if (update && model.ExpectedStockQuantity.HasValue)
            Add("ExpectedStockQuantity", model.ExpectedStockQuantity.Value.ToString(CultureInfo.InvariantCulture));
        Add("Unit", model.Unit); Add("IsAvailable", model.IsAvailable.ToString());
        Add("CategoryId", model.CategoryId.ToString(CultureInfo.InvariantCulture));
        if (model.ImageFile is not null)
            form.Add(new StreamContent(model.ImageFile.OpenReadStream()), "ImageFile", Path.GetFileName(model.ImageFile.FileName));
        return SendAsync(update ? HttpMethod.Put : HttpMethod.Post, update ? $"products/{model.Id}" : "products", form);
    }
}
