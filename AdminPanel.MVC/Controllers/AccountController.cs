using AdminPanel.MVC.Models;
using AdminPanel.MVC.Services;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace AdminPanel.MVC.Controllers;

public class AccountController : Controller
{
    private readonly ApiClient _apiClient;

    public AccountController(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [HttpGet]
    public IActionResult Login()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var response = await _apiClient.PostAsync("auth/login", model);
        if (!response.IsSuccessStatusCode)
        {
            ModelState.AddModelError("", "Invalid login.");
            return View(model);
        }

        var json = await response.Content.ReadAsStringAsync();
        var obj = JObject.Parse(json);

        var token = obj["token"]?.ToString();
        var username = obj["user"]?["username"]?.ToString();
        var role = obj["user"]?["role"]?.ToString();

        if (string.IsNullOrEmpty(token)) return View(model);

        // حفظ التوكن مؤقتًا في الكوكيز
        HttpContext.Response.Cookies.Append("jwt_token", token!, new CookieOptions
        {
            HttpOnly = true,
            Expires = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(7) : DateTimeOffset.UtcNow.AddHours(1)
        });

        return RedirectToAction("Index", "Dashboard");
    }

    public IActionResult Logout()
    {
        Response.Cookies.Delete("jwt_token");
        return RedirectToAction("Login");
    }
}
