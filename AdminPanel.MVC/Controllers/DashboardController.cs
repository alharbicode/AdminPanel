using Microsoft.AspNetCore.Mvc;
namespace AdminPanel.MVC.Controllers;
public class DashboardController : Controller
{
    public IActionResult Index() => View();
}
