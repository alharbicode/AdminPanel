using System.Net;
using AdminPanel.MVC.Models;
using AdminPanel.MVC.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AdminPanel.MVC.Controllers;

public sealed class DiscountsController(ApiClient api) : ManagementController(api)
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] AdminListQuery query)
    {
        await Products();
        return View(await List<DiscountViewModel>("discounts", query));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var model = await Api.GetAsync<DiscountViewModel>($"discounts/{id}");
        return model is null ? NotFound() : View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await Products();
        return View(new DiscountViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Create(DiscountViewModel model)
    {
        await Products(model.ProductId);
        if (!ModelState.IsValid) return View(model);
        SetUtc(model);
        using var response = await Api.PostAsync("discounts", model);
        if (response.IsSuccessStatusCode) return Saved("Discount created.");
        await AddApiErrors(response, "Could not create the discount. Check the fields and try again.", "This record changed or is still in use. Reload it and try again.");
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var model = await Api.GetAsync<DiscountViewModel>($"discounts/{id}");
        if (model is null) return NotFound();
        await Products();
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(DiscountViewModel model)
    {
        await Products(model.ProductId);
        if (!ModelState.IsValid) return View(model);
        SetUtc(model);
        using var response = await Api.PutAsync($"discounts/{model.Id}", model);
        if (response.StatusCode == HttpStatusCode.NotFound) return NotFound();
        if (response.IsSuccessStatusCode) return Saved("Discount updated.");
        await AddApiErrors(response, "Could not save the discount. Check the fields and try again.", "This record changed or is still in use. Reload it and try again.");
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var model = await Api.GetAsync<DiscountViewModel>($"discounts/{id}");
        return model is null ? NotFound() : View(model);
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        using var response = await Api.DeleteAsync($"discounts/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound) return NotFound();
        if (response.IsSuccessStatusCode) return Saved("Discount deleted.");
        await AddApiErrors(response, "Could not delete the discount. Try again.", "This record changed or is still in use. Reload it and try again.");
        var model = await Api.GetAsync<DiscountViewModel>($"discounts/{id}");
        return model is null ? NotFound() : View(model);
    }

    private async Task Products(int? selectedId = null)
    {
        var products = await Api.GetAsync<List<ProductViewModel>>("products") ?? new();
        ViewBag.Products = new SelectList(products.OrderBy(x => x.Name), "Id", "Name");
        if (selectedId.HasValue && !products.Any(x => x.Id == selectedId.Value))
            ModelState.AddModelError(nameof(DiscountViewModel.ProductId), "Choose an existing product.");
    }

    private static void SetUtc(DiscountViewModel model)
    {
        model.StartDate = DateTime.SpecifyKind(model.StartDate, DateTimeKind.Utc);
        model.EndDate = DateTime.SpecifyKind(model.EndDate, DateTimeKind.Utc);
    }
}
