using System.Net;
using AdminPanel.MVC.Models;
using AdminPanel.MVC.Services;
using Microsoft.AspNetCore.Mvc;

namespace AdminPanel.MVC.Controllers;

public sealed class ShippingOptionsController(ApiClient api) : ManagementController(api)
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] AdminListQuery query)
    {
        return View(await List<ShippingOptionViewModel>("shippingoptions", query));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var model = await Api.GetAsync<ShippingOptionViewModel>($"shippingoptions/{id}");
        return model is null ? NotFound() : View(model);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new ShippingOptionViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Create(ShippingOptionViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        using var response = await Api.PostAsync("shippingoptions", model);
        if (response.IsSuccessStatusCode) return Saved("Shipping option created.");
        await AddApiErrors(response, "Could not create the shipping option. Check the fields and try again.", "This shipping option is used by an order and cannot be deleted.");
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var model = await Api.GetAsync<ShippingOptionViewModel>($"shippingoptions/{id}");
        if (model is null) return NotFound();
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(ShippingOptionViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        using var response = await Api.PutAsync($"shippingoptions/{model.Id}", model);
        if (response.StatusCode == HttpStatusCode.NotFound) return NotFound();
        if (response.IsSuccessStatusCode) return Saved("Shipping option updated.");
        await AddApiErrors(response, "Could not save the shipping option. Check the fields and try again.", "This shipping option is used by an order and cannot be deleted.");
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var model = await Api.GetAsync<ShippingOptionViewModel>($"shippingoptions/{id}");
        return model is null ? NotFound() : View(model);
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        using var response = await Api.DeleteAsync($"shippingoptions/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound) return NotFound();
        if (response.IsSuccessStatusCode) return Saved("Shipping option deleted.");
        await AddApiErrors(response, "Could not delete the shipping option. Try again.", "This shipping option is used by an order and cannot be deleted.");
        var model = await Api.GetAsync<ShippingOptionViewModel>($"shippingoptions/{id}");
        return model is null ? NotFound() : View(model);
    }
}
