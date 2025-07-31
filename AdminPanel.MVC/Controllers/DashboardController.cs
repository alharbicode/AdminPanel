using AdminPanel.MVC.Services;
using Microsoft.AspNetCore.Mvc;

public class DashboardController : Controller
{
    private readonly ApiClient _apiClient;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DashboardController(ApiClient apiClient, IHttpContextAccessor httpContextAccessor)
    {
        _apiClient = apiClient;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<IActionResult> Index()
    {
        var token = _httpContextAccessor.HttpContext?.Request.Cookies["jwt_token"];
        if (string.IsNullOrEmpty(token))
            return RedirectToAction("Login", "Account");

        _apiClient.SetToken(token);

        // يمكنك هنا استدعاء بيانات من الـ API (مثلاً المنتجات أو الطلبات)
        return View();
    }
}
