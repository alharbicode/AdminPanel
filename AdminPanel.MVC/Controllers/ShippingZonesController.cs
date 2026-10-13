using System.Net;
using AdminPanel.MVC.Models;
using AdminPanel.MVC.Services;
using Microsoft.AspNetCore.Mvc;

namespace AdminPanel.MVC.Controllers;

public sealed class ShippingZonesController(ApiClient api) : ManagementController(api)
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] AdminListQuery query) =>
        View(await List<ShippingZoneViewModel>("shippingzones", query));

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var model = await Api.GetAsync<ShippingZoneViewModel>($"shippingzones/{id}");
        return model is null ? NotFound() : View(model);
    }

    [HttpGet]
    public IActionResult Create() => View(new ShippingZoneViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(ShippingZoneViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        using var response = await Api.PostAsync("shippingzones", model);
        if (response.IsSuccessStatusCode) return Saved("Shipping zone created.");
        await AddApiErrors(response, "Could not create the shipping zone. Check the fields and try again.", "A shipping zone already exists for that destination.");
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var model = await Api.GetAsync<ShippingZoneViewModel>($"shippingzones/{id}");
        return model is null ? NotFound() : View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(ShippingZoneViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        using var response = await Api.PutAsync($"shippingzones/{model.Id}", model);
        if (response.StatusCode == HttpStatusCode.NotFound) return NotFound();
        if (response.IsSuccessStatusCode) return Saved("Shipping zone updated.");
        await AddApiErrors(response, "Could not save the shipping zone. Check the fields and try again.",
            "This zone changed or a zone already exists for that destination. Reload before editing.");
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Disable(int id)
    {
        var model = await Api.GetAsync<ShippingZoneViewModel>($"shippingzones/{id}");
        return model is null ? NotFound() : View(model);
    }

    [HttpPost, ActionName("Disable")]
    public async Task<IActionResult> DisableConfirmed(int id, Guid? version)
    {
        var model = await Api.GetAsync<ShippingZoneViewModel>($"shippingzones/{id}");
        if (model is null) return NotFound();
        // Preserve the version from the reviewed confirmation page; do not silently overwrite edits.
        model.Version = version;
        model.Enabled = false;
        using var response = await Api.PutAsync($"shippingzones/{id}", model);
        if (response.IsSuccessStatusCode) return Saved("Shipping zone disabled. Existing orders retain their shipping snapshots.");
        await AddApiErrors(response, "Could not disable the shipping zone. Try again.", "This zone changed. Reload it before disabling.");
        return View(model);
    }
}
