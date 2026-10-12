using System.Net;
using AdminPanel.MVC.Models;
using AdminPanel.MVC.Services;
using Microsoft.AspNetCore.Mvc;

namespace AdminPanel.MVC.Controllers;

public sealed class ContentPagesController(ApiClient api) : ManagementController(api)
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] AdminListQuery query)
    {
        return View(await List<ContentPageViewModel>("contentpages", query));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var model = await Api.GetAsync<ContentPageViewModel>($"contentpages/by-id/{id}");
        return model is null ? NotFound() : View(model);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new ContentPageViewModel());
    }

    [HttpPost]
    public async Task<IActionResult> Create(ContentPageViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        using var response = await Api.PostAsync("contentpages", model);
        if (response.IsSuccessStatusCode) return Saved("Content page created.");
        await AddApiErrors(response, "Could not create the content page. Check the fields and try again.", "That slug is already in use. Choose a different slug.");
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var model = await Api.GetAsync<ContentPageViewModel>($"contentpages/by-id/{id}");
        if (model is null) return NotFound();
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(ContentPageViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        using var response = await Api.PutAsync($"contentpages/{model.Id}", model);
        if (response.StatusCode == HttpStatusCode.NotFound) return NotFound();
        if (response.IsSuccessStatusCode) return Saved("Content page updated.");
        await AddApiErrors(response, "Could not save the content page. Check the fields and try again.", "That slug is already in use. Choose a different slug.");
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var model = await Api.GetAsync<ContentPageViewModel>($"contentpages/by-id/{id}");
        return model is null ? NotFound() : View(model);
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        using var response = await Api.DeleteAsync($"contentpages/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound) return NotFound();
        if (response.IsSuccessStatusCode) return Saved("Content page deleted.");
        await AddApiErrors(response, "Could not delete the content page. Try again.", "That slug is already in use. Choose a different slug.");
        var model = await Api.GetAsync<ContentPageViewModel>($"contentpages/by-id/{id}");
        return model is null ? NotFound() : View(model);
    }
}
