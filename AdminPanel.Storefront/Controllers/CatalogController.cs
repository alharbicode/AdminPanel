using AdminPanel.Storefront.Models;
using AdminPanel.Storefront.Services;
using Microsoft.AspNetCore.Mvc;
namespace AdminPanel.Storefront.Controllers;

public sealed class CatalogController(CatalogClient catalog) : Controller
{
    public async Task<IActionResult> Index([FromQuery] CatalogQuery query, CancellationToken ct)
    {
        if (!ModelState.IsValid) { Response.StatusCode = 400; return View("Invalid"); }
        return View(new CatalogModel(await catalog.Products(query, ct), query,
            (await catalog.Categories(new CatalogQuery { PageSize = 48 }, ct)).Items));
    }
    public async Task<IActionResult> Categories([FromQuery] CatalogQuery query, CancellationToken ct)
    {
        if (!ModelState.IsValid) { Response.StatusCode = 400; return View("Invalid"); }
        return View(new CategoriesModel(await catalog.Categories(query, ct), query));
    }
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var product = await catalog.Product(id, ct);
        if (product is null) { Response.StatusCode = 404; return View("Missing"); }
        var related = await catalog.Products(new CatalogQuery { CategoryId = product.CategoryId, PageSize = 5 }, ct);
        return View(new DetailsModel(product, related.Items.Where(p => p.Id != id).Take(4).ToList()));
    }
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Image(int id, CancellationToken ct)
    {
        try
        {
            var image = await catalog.Image(id, ct);
            if (image.HasValue) return File(image.Value.Bytes, image.Value.Type);
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
        return Redirect("~/images/product-placeholder.svg");
    }
}
