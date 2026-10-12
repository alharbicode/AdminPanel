using System.Security.Claims;
using AdminPanel.MVC.Models;
using AdminPanel.MVC.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdminPanel.MVC.Controllers;

public class AccountController(ApiClient api) : Controller
{
    [AllowAnonymous, HttpGet]
    public IActionResult Login() => View();

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        using var response = await api.PostAsync("auth/login", new { model.Username, model.Password });
        if (!response.IsSuccessStatusCode)
        {
            ModelState.AddModelError("", response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                ? "Too many login attempts. Try again in a minute." : "Login failed. Check your credentials or try again later.");
            return View(model);
        }
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (login is null || string.IsNullOrEmpty(login.Token) || login.User.Role != "Admin" || login.ExpiresInSeconds <= 0)
        {
            ModelState.AddModelError("", "An administrator account is required.");
            return View(model);
        }
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, login.User.Id.ToString()),
            new Claim(ClaimTypes.Name, login.User.Username),
            new Claim(ClaimTypes.Role, login.User.Role)
        }, CookieAuthenticationDefaults.AuthenticationScheme);
        var properties = new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(login.ExpiresInSeconds),
            AllowRefresh = false
        };
        properties.StoreTokens(new[] { new AuthenticationToken { Name = "access_token", Value = login.Token } });
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), properties);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }
    [AllowAnonymous, HttpGet]
    public IActionResult AccessDenied() => StatusCode(403, "Administrator access is required.");

    public sealed record LoginResponse(string Token, int ExpiresInSeconds, LoginUser User);
    public sealed record LoginUser(int Id, string Username, string Role);
}
