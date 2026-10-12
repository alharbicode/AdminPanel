using AdminPanel.MVC.Models;
using AdminPanel.MVC.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AdminPanel.MVC.Controllers;

public class ProductsController(ApiClient api) : ManagementController(api)
{
    public async Task<IActionResult> Index([FromQuery] AdminListQuery query) { await LoadCategories(); return View(await List<ProductViewModel>("products", query)); }

    private async Task LoadCategories() => ViewBag.Categories =
        (await api.GetAsync<List<CategoryViewModel>>("categories") ?? new())
        .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToList();

    [HttpGet]
    public async Task<IActionResult> Lookup([FromQuery] AdminListQuery query)
    {
        if (!ModelState.IsValid) return BadRequest(new { message = "Check the product search values." });
        return Json(await api.GetAsync<PagedList<ProductOptionViewModel>>(query.Url("products/admin/lookup")));
    }

    [HttpGet]
    public async Task<IActionResult> Create() { await LoadCategories(); return View(new ProductViewModel()); }
    [HttpPost]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Create(ProductViewModel model) => await Save(model, false);
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var product = await api.GetAsync<ProductViewModel>($"products/{id}");
        if (product is null) return NotFound();
        await LoadCategories();
        return View(product);
    }
    [HttpPost]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Edit(ProductViewModel model) => await Save(model, true);

    private async Task<IActionResult> Save(ProductViewModel model, bool update)
    {
        if (model.ImageFile?.Length > 5 * 1024 * 1024) ModelState.AddModelError(nameof(model.ImageFile), "Image must be at most 5 MiB.");
        if (ModelState.IsValid)
        {
            using var response = await api.SaveProductAsync(model, update);
            if (response.IsSuccessStatusCode) return RedirectToAction(nameof(Index));
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return NotFound();
            ModelState.AddModelError("", "Could not save product. Check the category, values and image (JPEG, PNG or WebP).");
        }
        await LoadCategories();
        return View(update ? "Edit" : "Create", model);
    }
    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var product = await api.GetAsync<ProductViewModel>($"products/{id}");
        return product is null ? NotFound() : View(product);
    }
    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        using var response = await api.DeleteAsync($"products/{id}");
        if (response.IsSuccessStatusCode) return RedirectToAction(nameof(Index));
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return NotFound();
        ModelState.AddModelError("", "Could not delete product. Products referenced by orders or discounts must be retained.");
        return View(await api.GetAsync<ProductViewModel>($"products/{id}"));
    }
}
