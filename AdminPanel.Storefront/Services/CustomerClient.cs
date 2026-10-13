using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
namespace AdminPanel.Storefront.Services;

public sealed class CustomerApiException(HttpStatusCode status, string code) : Exception(code)
{
    public HttpStatusCode Status { get; } = status;
    public string Code { get; } = code;
}
public sealed class CustomerClient
{
    private readonly HttpClient _http;
    private readonly IHttpContextAccessor _accessor;
    public CustomerClient(HttpClient http, IConfiguration config, IHttpContextAccessor accessor)
    {
        var url = config["ApiSettings:BaseUrl"];
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.AbsolutePath.EndsWith('/') || uri.Query != "" || uri.Fragment != "" || uri.UserInfo != "")
            throw new InvalidOperationException("Configure an HTTPS catalog API URL ending in /.");
        _http = http; _http.BaseAddress = new Uri(uri, "../"); _http.Timeout = TimeSpan.FromSeconds(15);
        _accessor = accessor;
    }
    public async Task<T> Send<T>(HttpMethod method, string path, object? data = null, string? guest = null,
        string? token = null, bool anonymous = false)
    {
        var context = _accessor.HttpContext!;
        using var request = new HttpRequestMessage(method, path);
        if (data is not null) request.Content = JsonContent.Create(data);
        if (!anonymous) token ??= await context.GetTokenAsync("access_token");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (guest is not null) request.Headers.Add("X-Guest-Cart", guest);
        using var response = await _http.SendAsync(request, context.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            var code = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "SignInAgain",
                HttpStatusCode.Conflict => "CartConflict", HttpStatusCode.TooManyRequests => "RateLimited",
                HttpStatusCode.BadRequest => "InvalidInput", _ => "ServiceUnavailable"
            };
            try
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(context.RequestAborted);
                if (body.TryGetProperty("code", out var value)) code = value.GetString() ?? code;
            }
            catch (JsonException) { }
            throw new CustomerApiException(response.StatusCode, code);
        }
        return await response.Content.ReadFromJsonAsync<T>(context.RequestAborted)
            ?? throw new HttpRequestException("Empty customer API response.");
    }
}

