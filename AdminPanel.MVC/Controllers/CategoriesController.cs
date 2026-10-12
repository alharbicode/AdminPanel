using AdminPanel.MVC.Models;
using AdminPanel.MVC.Services;
using Microsoft.AspNetCore.Mvc;

namespace AdminPanel.MVC.Controllers;

public class CategoriesController : Controller
{
    private readonly ApiClient _apiClient;

    public CategoriesController(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }


    public async Task<IActionResult> Index()
    {
        var categories = await _apiClient.GetAsync<List<CategoryViewModel>>("categories");
        return View(categories ?? new List<CategoryViewModel>());
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Create(CategoryViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        using var response = await _apiClient.PostAsync("categories", model);

        if (response.IsSuccessStatusCode)
            return RedirectToAction(nameof(Index));

        ModelState.AddModelError("", "Failed to create category.");
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var category = await _apiClient.GetAsync<CategoryViewModel>($"categories/{id}");
        if (category == null)
            return NotFound();

        return View(category);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(CategoryViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        using var response = await _apiClient.PutAsync($"categories/{model.Id}", model);

        if (response.IsSuccessStatusCode)
            return RedirectToAction(nameof(Index));

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return NotFound();
        ModelState.AddModelError("", "Failed to update category.");
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var category = await _apiClient.GetAsync<CategoryViewModel>($"categories/{id}");
        if (category == null)
            return NotFound();

        return View(category);
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        using var response = await _apiClient.DeleteAsync($"categories/{id}");

        if (response.IsSuccessStatusCode)
            return RedirectToAction(nameof(Index));

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return NotFound();
        ModelState.AddModelError("", "Could not delete category. Remove or reassign its products first.");
        var category = await _apiClient.GetAsync<CategoryViewModel>($"categories/{id}");
        return View(category);
    }
}
