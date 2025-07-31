using AdminPanel.MVC.Models;
using AdminPanel.MVC.Services;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class OrdersController : Controller
{
    private readonly ApiClient _apiClient;
    private readonly IHttpContextAccessor _accessor;

    public OrdersController(ApiClient apiClient, IHttpContextAccessor accessor)
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

    // ===== LIST =====
    public async Task<IActionResult> Index()
    {
        LoadToken();

        // نجلب قائمة الطلبات من API
        var rawOrders = await _apiClient.GetAsync<List<JObject>>("orders");

        var orders = new List<OrderViewModel>();

        if (rawOrders != null)
        {
            foreach (var o in rawOrders)
            {
                orders.Add(new OrderViewModel
                {
                    Id = (int)(o["id"] ?? 0),
                    CustomerName = (string?)o["customerName"] ?? "",
                    Status = (string?)o["status"] ?? "Pending",
                    OrderDate = (DateTime?)o["orderDate"] ?? DateTime.MinValue,
                    TotalPriceFromApi = (decimal?)o["totalPrice"] ?? 0m
                });
            }
        }

        return View(orders);
    }

    // ===== DETAILS =====
    public async Task<IActionResult> Details(int id)
    {
        LoadToken();

        // نجلب طلب واحد
        var raw = await _apiClient.GetAsync<JObject>($"orders/{id}");
        if (raw == null)
            return NotFound();

        var vm = new OrderViewModel
        {
            Id = (int)(raw["id"] ?? 0),
            CustomerName = (string?)raw["customerName"] ?? "",
            CustomerEmail = (string?)raw["customerEmail"],
            CustomerPhone = (string?)raw["customerPhone"],
            CustomerAddress = (string?)raw["customerAddress"],
            Status = (string?)raw["status"] ?? "Pending",
            OrderDate = (DateTime?)raw["orderDate"] ?? DateTime.MinValue,
            TotalPriceFromApi = (decimal?)raw["totalPrice"] ?? 0m
        };

        // العناصر
        var itemsToken = raw["items"];
        if (itemsToken != null && itemsToken.Type == JTokenType.Array)
        {
            foreach (var it in itemsToken)
            {
                //vm.Items.Add(new OrderItemViewModel             
                //{
                //    ProductName = (string?)it["productName"] ?? "",
                //    Quantity = (int)(it["quantity"] ?? 0),
                //    Price = (decimal?)it["price"] ?? 0m
                //});
            }
        }

        return View(vm);
    }

    // ===== UPDATE STATUS =====
    [HttpPost]
    public async Task<IActionResult> UpdateStatus(int id, string newStatus)
    {
        LoadToken();

        var body = new { status = newStatus }; // حروف صغيرة حتى لو كان الـ API حساس
        var response = await _apiClient.PutAsync($"orders/{id}/status", body);

        TempData["Message"] = response.IsSuccessStatusCode
            ? "✅ Order status updated successfully!"
            : "❌ Failed to update order status.";

        return RedirectToAction("Details", new { id });
    }

    // ===== CANCEL =====
    [HttpPost]
    public async Task<IActionResult> Cancel(int id)
    {
        LoadToken();

        // نستخدم DELETE إذا الـ API يدعمها (الأفضل)
        // لو APIك الحالي يستخدم PUT للإلغاء، عدّل السطر أدناه:
        // var response = await _apiClient.PutAsync<object>($"orders/{id}/cancel", null);

        var response = await _apiClient.DeleteAsync($"orders/{id}");

        TempData["Message"] = response.IsSuccessStatusCode
            ? "✅ Order cancelled and stock updated."
            : "❌ Failed to cancel order.";

        return RedirectToAction(nameof(Index));
    }
}
