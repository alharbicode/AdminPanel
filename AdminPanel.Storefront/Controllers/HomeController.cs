using AdminPanel.Storefront.Models;
using AdminPanel.Storefront.Services;
using Microsoft.AspNetCore.Mvc;
namespace AdminPanel.Storefront.Controllers;

public sealed class HomeController(CatalogClient catalog) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var products = await catalog.Products(new CatalogQuery { PageSize = 8 }, ct);
        var categories = await catalog.Categories(new CatalogQuery { PageSize = 6 }, ct);
        return View(new HomeModel(products.Items, categories.Items));
    }
    public IActionResult Error()
    {
        Response.StatusCode = 503;
        return View();
    }
}
