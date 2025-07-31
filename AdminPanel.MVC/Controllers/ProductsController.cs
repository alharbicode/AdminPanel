using AdminPanel.MVC.Models;
using AdminPanel.MVC.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

public class ProductsController : Controller
{
    private readonly ApiClient _apiClient;
    private readonly IHttpContextAccessor _accessor;

    public ProductsController(ApiClient apiClient, IHttpContextAccessor accessor)
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
        var products = await _apiClient.GetAsync<List<ProductViewModel>>("products");
        return View(products ?? new List<ProductViewModel>());
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        LoadToken();

        var categories = await _apiClient.GetAsync<List<CategoryViewModel>>("categories");
        ViewBag.Categories = categories?.Select(c => new SelectListItem
        {
            Value = c.Id.ToString(),
            Text = c.Name
        }).ToList();

        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Create(ProductViewModel model)
    {
        LoadToken();

        if (!ModelState.IsValid)
        {
            var categories = await _apiClient.GetAsync<List<CategoryViewModel>>("categories");
            ViewBag.Categories = categories?.Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.Name
            }).ToList();

            return View(model);
        }

        var productToSend = new
        {
            model.Name,
            model.Description,
            model.Price,
            model.StockQuantity,
            model.Unit,
            model.IsAvailable,
            model.CategoryId,
            ImageUrl = model.ImageUrl ?? ""
        };

        var response = await _apiClient.PostAsync("products", productToSend);

        if (response.IsSuccessStatusCode)
            return RedirectToAction(nameof(Index));

        ModelState.AddModelError("", "Failed to create product.");

        var categories2 = await _apiClient.GetAsync<List<CategoryViewModel>>("categories");
        ViewBag.Categories = categories2?.Select(c => new SelectListItem
        {
            Value = c.Id.ToString(),
            Text = c.Name
        }).ToList();

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        LoadToken();

        var product = await _apiClient.GetAsync<ProductViewModel>($"products/{id}");
        if (product == null)
            return NotFound();

        var categories = await _apiClient.GetAsync<List<CategoryViewModel>>("categories");
        ViewBag.Categories = categories?.Select(c => new SelectListItem
        {
            Value = c.Id.ToString(),
            Text = c.Name
        }).ToList();

        return View(product);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(ProductViewModel model)
    {
        LoadToken();

        if (!ModelState.IsValid)
        {
            var categories = await _apiClient.GetAsync<List<CategoryViewModel>>("categories");
            ViewBag.Categories = categories?.Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.Name
            }).ToList();

            return View(model);
        }

        var productToSend = new
        {
            model.Id,
            model.Name,
            model.Description,
            model.Price,
            model.StockQuantity,
            model.Unit,
            model.IsAvailable,
            model.CategoryId,
            ImageUrl = model.ImageUrl ?? ""
        };

        var response = await _apiClient.PutAsync($"products/{model.Id}", productToSend);

        if (response.IsSuccessStatusCode)
            return RedirectToAction(nameof(Index));

        ModelState.AddModelError("", "Failed to update product.");

        var categoriesReload = await _apiClient.GetAsync<List<CategoryViewModel>>("categories");
        ViewBag.Categories = categoriesReload?.Select(c => new SelectListItem
        {
            Value = c.Id.ToString(),
            Text = c.Name
        }).ToList();

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        LoadToken();

        var product = await _apiClient.GetAsync<ProductViewModel>($"products/{id}");
        if (product == null)
            return NotFound();

        return View(product);
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        LoadToken();

        var response = await _apiClient.DeleteAsync($"products/{id}");

        if (response.IsSuccessStatusCode)
            return RedirectToAction(nameof(Index));

        ModelState.AddModelError("", "Failed to delete product.");

        var product = await _apiClient.GetAsync<ProductViewModel>($"products/{id}");
        return View(product);
    }
}
