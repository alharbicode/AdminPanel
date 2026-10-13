using System.Security.Claims;
using System.Text.Json;
using AdminPanel.Storefront.Models;
using AdminPanel.Storefront.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace AdminPanel.Storefront.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountController(CustomerClient api, CartGateway carts, StoreText text) : Controller
{
    [HttpGet] public IActionResult Register() => View(new RegisterModel());
    [HttpGet] public IActionResult Login(string? returnUrl, bool expired = false)
    {
        if (expired) ModelState.AddModelError("", text["SignInAgain"]);
        return View(new LoginModel { ReturnUrl = returnUrl });
    }
    [HttpGet] public IActionResult ForgotPassword() => View(new ForgotModel());
    [HttpGet] public IActionResult ResetPassword() => View(new ResetModel());

    [HttpPost]
    public async Task<IActionResult> Register(RegisterModel model)
    {
        if (!ModelState.IsValid) return View(model);
        try
        {
            var result = await api.Send<CustomerLogin>(HttpMethod.Post, "customer/account/register",
                new { model.DisplayName, model.Email, model.Password }, anonymous: true);
            return await SignedIn(result, false, null);
        }
        catch (CustomerApiException e) { ModelState.AddModelError("", text[e.Code]); return View(model); }
    }
    [HttpPost]
    public async Task<IActionResult> Login(LoginModel model)
    {
        if (!ModelState.IsValid) return View(model);
        try
        {
            var result = await api.Send<CustomerLogin>(HttpMethod.Post, "customer/account/login",
                new { model.Email, model.Password }, anonymous: true);
            return await SignedIn(result, model.RememberMe, model.ReturnUrl);
        }
        catch (CustomerApiException e) { ModelState.AddModelError("", text[e.Code]); return View(model); }
    }
    private async Task<IActionResult> SignedIn(CustomerLogin result, bool persistent, string? returnUrl)
    {
        var identity = new ClaimsIdentity([new(ClaimTypes.NameIdentifier, result.Customer.Id.ToString()),
            new(ClaimTypes.Role, "Customer")], "StoreCustomer");
        var properties = new AuthenticationProperties
        {
            IsPersistent = persistent, ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(result.ExpiresInSeconds), AllowRefresh = false
        };
        properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = result.Token }]);
        await HttpContext.SignInAsync("StoreCustomer", new ClaimsPrincipal(identity), properties);
        if (carts.HasGuest)
        {
            try
            {
                var merged = await carts.Merge(result.Token);
                TempData["Notice"] = merged is null ? "GuestExpired" : merged.Adjusted ? "CartAdjusted" : "CartMerged";
            }
            catch (Exception e) when (e is CustomerApiException or HttpRequestException or TaskCanceledException)
            {
                // Keep the guest secret so an authenticated, antiforgery-protected retry can finish the merge.
                TempData["Notice"] = "MergePending";
            }
            return RedirectToAction("Index", "Cart");
        }
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction(nameof(Profile));
    }
    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("StoreCustomer");
        return RedirectToAction("Index", "Home");
    }
    [HttpPost]
    public async Task<IActionResult> ForgotPassword(ForgotModel model)
    {
        if (!ModelState.IsValid) return View(model);
        try
        {
            await api.Send<JsonElement>(HttpMethod.Post, "customer/account/forgot-password",
                new { model.Email, Culture = text.Language }, anonymous: true);
            TempData["Notice"] = "RecoveryRequested";
            return RedirectToAction(nameof(ForgotPassword));
        }
        catch (CustomerApiException e) { ModelState.AddModelError("", text[e.Code]); return View(model); }
    }
    [HttpPost]
    public async Task<IActionResult> ResetPassword(ResetModel model)
    {
        if (!ModelState.IsValid) return View(model);
        try
        {
            await api.Send<JsonElement>(HttpMethod.Post, "customer/account/reset-password",
                new { model.Email, model.Token, model.Password }, anonymous: true);
            await HttpContext.SignOutAsync("StoreCustomer");
            TempData["Notice"] = "PasswordUpdated";
            return RedirectToAction(nameof(Login));
        }
        catch (CustomerApiException e) { ModelState.AddModelError("", text[e.Code]); return View(model); }
    }
    [Authorize, HttpGet]
    public async Task<IActionResult> Profile()
    {
        var customer = await api.Send<Customer>(HttpMethod.Get, "customer/account/profile");
        return View(new ProfileModel { DisplayName = customer.DisplayName, Email = customer.Email });
    }
    [Authorize, HttpPost]
    public async Task<IActionResult> Profile(ProfileModel model)
    {
        try
        {
            if (ModelState.IsValid)
            {
                await api.Send<Customer>(HttpMethod.Put, "customer/account/profile", new { model.DisplayName });
                TempData["Notice"] = "ProfileUpdated";
                return RedirectToAction(nameof(Profile));
            }
        }
        catch (CustomerApiException e) when (e.Status is not (System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden))
        { ModelState.AddModelError("", text[e.Code]); }
        var customer = await api.Send<Customer>(HttpMethod.Get, "customer/account/profile");
        model.Email = customer.Email;
        ModelState.Remove(nameof(model.Email));
        return View(model);
    }
    [Authorize, HttpGet] public IActionResult ChangePassword() => View(new ChangePasswordModel());
    [Authorize, HttpPost]
    public async Task<IActionResult> ChangePassword(ChangePasswordModel model)
    {
        if (!ModelState.IsValid) return View(model);
        try
        {
            await api.Send<JsonElement>(HttpMethod.Post, "customer/account/change-password", new { model.CurrentPassword, model.Password });
            await HttpContext.SignOutAsync("StoreCustomer");
            TempData["Notice"] = "PasswordUpdated";
            return RedirectToAction(nameof(Login));
        }
        catch (CustomerApiException e) when (e.Status is not (System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden))
        { ModelState.AddModelError("", text[e.Code]); return View(model); }
    }
}

