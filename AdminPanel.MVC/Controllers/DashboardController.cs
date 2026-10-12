using AdminPanel.MVC.Models;
using AdminPanel.MVC.Services;
using Microsoft.AspNetCore.Mvc;

namespace AdminPanel.MVC.Controllers;
public class DashboardController(ApiClient api) : Controller
{
    public async Task<IActionResult> Index() => View(await api.GetAsync<DashboardViewModel>("dashboard") ?? new());
}
