using AdminPanel.MVC.Models;
using AdminPanel.MVC.Services;
using Microsoft.AspNetCore.Mvc;

public class CategoriesController : Controller
{
    private readonly ApiClient _apiClient;
    private readonly IHttpContextAccessor _accessor;

    public CategoriesController(ApiClient apiClient, IHttpContextAccessor accessor)
    {
        _apiClient = apiClient;
        _accessor = accessor;
    }

    private void LoadToken()
    {
        var token = _accessor.HttpContext?.Request.Cookies["jwt_token"];
        if (!string.IsNullOrEmpty(token))
            _apiClient.SetToken(token);
    }

    public async Task<IActionResult> Index()
    {
        LoadToken();
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
        LoadToken();
        if (!ModelState.IsValid)
            return View(model);

        var response = await _apiClient.PostAsync("categories", model);

        if (response.IsSuccessStatusCode)
            return RedirectToAction(nameof(Index));

        ModelState.AddModelError("", "Failed to create category.");
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        LoadToken();
        var category = await _apiClient.GetAsync<CategoryViewModel>($"categories/{id}");
        if (category == null)
            return NotFound();

        return View(category);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(CategoryViewModel model)
    {
        LoadToken();
        if (!ModelState.IsValid)
            return View(model);

        var response = await _apiClient.PutAsync($"categories/{model.Id}", model);

        if (response.IsSuccessStatusCode)
            return RedirectToAction(nameof(Index));

        ModelState.AddModelError("", "Failed to update category.");
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        LoadToken();
        var category = await _apiClient.GetAsync<CategoryViewModel>($"categories/{id}");
        if (category == null)
            return NotFound();

        return View(category);
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        LoadToken();
        var response = await _apiClient.DeleteAsync($"categories/{id}");

        if (response.IsSuccessStatusCode)
            return RedirectToAction(nameof(Index));

        ModelState.AddModelError("", "Failed to delete category.");
        var category = await _apiClient.GetAsync<CategoryViewModel>($"categories/{id}");
        return View(category);
    }
}
