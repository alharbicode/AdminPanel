using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdminPanel.Storefront.Models;
using Microsoft.AspNetCore.DataProtection;

namespace AdminPanel.Storefront.Services;

public sealed class CheckoutGateway(CustomerClient api, CartGateway carts, IHttpContextAccessor accessor,
    IDataProtectionProvider protection)
{
    public const string OrderCookie = "__Host-Alharbi.Order";
    // 100 lines may each contain 200-character product and discount names. JSON's Unicode
    // escaping plus Data Protection encoding can exceed 128 KiB for valid Arabic carts.
    private const int MaximumReviewTokenLength = 524288;
    private readonly IDataProtector _reviews = protection.CreateProtector("Storefront.Checkout.Review.v1");
    private readonly ITimeLimitedDataProtector _orders = protection.CreateProtector("Storefront.GuestOrder.v1").ToTimeLimitedDataProtector();
    private HttpContext Context => accessor.HttpContext!;
    public bool Customer => Context.User.Identity?.IsAuthenticated == true;
    private string Owner => Customer ? "customer:" + Context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        : "guest:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(carts.GuestToken ?? "")));
    public Task<List<ShippingDestination>> Destinations() => api.Send<List<ShippingDestination>>(HttpMethod.Get, "checkout/destinations", anonymous: true);
    public Task<CheckoutQuote> Quote(CheckoutModel model) => api.Send<CheckoutQuote>(HttpMethod.Post,
        (Customer ? "customer" : "guest") + "/checkout/quote", model, guest: Customer ? null : carts.GuestToken, anonymous: !Customer);
    public string Protect(ReviewState state) => _reviews.Protect(JsonSerializer.Serialize(state));
    public ReviewState CreateReview(CheckoutModel form, CheckoutQuote quote) => new(form, quote, Owner);
    public ReviewState? ReadReview(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumReviewTokenLength) return null;
        try
        {
            var state = JsonSerializer.Deserialize<ReviewState>(_reviews.Unprotect(value));
            return state?.Owner == Owner ? state : null;
        }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException) { return null; }
    }
    public Task<CheckoutResult> Confirm(ReviewState state) => api.Send<CheckoutResult>(HttpMethod.Post,
        (Customer ? "customer" : "guest") + "/checkout/confirm", new
        {
            state.Form.CartVersion, state.Form.ShippingZoneId, state.Form.ContactName, state.Form.Phone, state.Form.Email,
            state.Form.City, state.Form.AddressLine, state.Form.DeliveryInstructions, state.Form.IdempotencyKey, state.Quote.QuoteFingerprint
        }, guest: Customer ? null : carts.GuestToken, anonymous: !Customer);
    public string? GuestOrderToken
    {
        get
        {
            if (!Context.Request.Cookies.TryGetValue(OrderCookie, out var value)) return null;
            try { return _orders.Unprotect(value); }
            catch (Exception e) when (e is CryptographicException or FormatException) { ForgetGuestOrder(); return null; }
        }
    }
    public void RememberGuestOrder(string token) => Context.Response.Cookies.Append(OrderCookie,
        _orders.Protect(token, TimeSpan.FromDays(30)), new CookieOptions
        {
            Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/",
            IsEssential = true, Expires = DateTimeOffset.UtcNow.AddDays(30)
        });
    public void ForgetGuestOrder() => Context.Response.Cookies.Delete(OrderCookie,
        new CookieOptions { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/" });
    public Task<OrderSnapshot> GuestOrder(string token) => api.Send<OrderSnapshot>(HttpMethod.Post,
        "guest/orders/lookup", anonymous: true, guestOrder: token);
    public Task<OrderSnapshot> Order(int id) => api.Send<OrderSnapshot>(HttpMethod.Get, "customer/orders/" + id);
    public Task<OrderSnapshot> ChangeOrder(OrderActionModel model, string operation) => api.Send<OrderSnapshot>(HttpMethod.Post,
        model.Guest ? "guest/orders/" + operation : "customer/orders/" + model.Id + "/" + operation,
        new { model.Version, model.Reason }, anonymous: model.Guest, guestOrder: model.Guest ? GuestOrderToken : null);
}
