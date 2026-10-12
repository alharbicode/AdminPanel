using System.Net;
using AdminPanel.MVC.Models;
using AdminPanel.MVC.Services;
using Microsoft.AspNetCore.Mvc;

namespace AdminPanel.MVC.Controllers;

public sealed class DiscountsController(ApiClient api) : ManagementController(api)
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] AdminListQuery query)
    {
        await Product(query.ProductId);
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
        await Product(null);
        return View(new DiscountViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Create(DiscountViewModel model)
    {
        await Product(model.ProductId, validate: true);
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
        await Product(model.ProductId);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(DiscountViewModel model)
    {
        await Product(model.ProductId, validate: true);
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

    private async Task Product(int? selectedId, bool validate = false)
    {
        var product = selectedId > 0 ? await Api.GetAsync<ProductViewModel>($"products/{selectedId}") : null;
        ViewBag.SelectedProductId = selectedId;
        ViewBag.SelectedProductName = product?.Name;
        if (validate && product is null)
            ModelState.AddModelError(nameof(DiscountViewModel.ProductId), "Choose an existing product.");
    }

    private static void SetUtc(DiscountViewModel model)
    {
        model.StartDate = DateTime.SpecifyKind(model.StartDate, DateTimeKind.Utc);
        model.EndDate = DateTime.SpecifyKind(model.EndDate, DateTimeKind.Utc);
    }
}
