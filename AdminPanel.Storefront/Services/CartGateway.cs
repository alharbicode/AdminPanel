using System.Net;
using AdminPanel.Storefront.Models;
namespace AdminPanel.Storefront.Services;

public sealed class CartGateway(CustomerClient api, IHttpContextAccessor accessor)
{
    public const string GuestCookie = "__Host-Alharbi.Guest";
    private HttpContext Context => accessor.HttpContext!;
    private string? _newGuest;
    public string? GuestToken => _newGuest ?? Context.Request.Cookies[GuestCookie];
    private Task<CartSnapshot>? _read;
    public bool HasGuest => GuestToken is not null;
    public Task<CartSnapshot> Read() => _read ??= ReadCore();
    private async Task<CartSnapshot> ReadCore()
    {
        if (Context.User.Identity?.IsAuthenticated == true)
            return await api.Send<CartSnapshot>(HttpMethod.Get, "customer/cart");
        if (!HasGuest) return CartSnapshot.Empty;
        try { return await api.Send<CartSnapshot>(HttpMethod.Get, "guest/cart", guest: GuestToken, anonymous: true); }
        catch (CustomerApiException e) when (e.Status == HttpStatusCode.NotFound) { ClearGuest(); return CartSnapshot.Empty; }
    }
    public void ClearGuest()
    {
        _newGuest = null;
        Context.Response.Cookies.Delete(GuestCookie, new CookieOptions { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/" });
    }
    public async Task<CartSnapshot?> Merge(string? token = null)
    {
        if (!HasGuest) return null;
        try
        {
            var result = await api.Send<CartSnapshot>(HttpMethod.Post, "customer/cart/merge", new { GuestToken }, token: token);
            ClearGuest(); _read = Task.FromResult(result);
            return result;
        }
        catch (CustomerApiException e) when (e.Status == HttpStatusCode.NotFound) { ClearGuest(); return null; }
    }
    public async Task<CartSnapshot> Change(CartEditModel model, string operation)
    {
        var authenticated = Context.User.Identity?.IsAuthenticated == true;
        var current = await Read();
        if (!authenticated && current.Version == Guid.Empty)
        {
            if (operation != "add") throw new CustomerApiException(HttpStatusCode.Conflict, "GuestExpired");
            var created = await api.Send<GuestCart>(HttpMethod.Post, "guest/cart", anonymous: true);
            _newGuest = created.Token; current = created.Cart;
            Context.Response.Cookies.Append(GuestCookie, created.Token, new CookieOptions
            {
                HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", Expires = created.ExpiresUtc, IsEssential = true
            });
        }
        var version = operation == "add" ? current.Version : model.Version;
        var method = operation switch { "add" => HttpMethod.Post, "set" => HttpMethod.Put, _ => HttpMethod.Delete };
        var result = await api.Send<CartSnapshot>(method, (authenticated ? "customer/cart" : "guest/cart") + "/items/" + model.ProductId,
            new { Version = version, model.Quantity }, guest: authenticated ? null : GuestToken, anonymous: !authenticated);
        _read = Task.FromResult(result);
        return result;
    }
}

