using AdminPanel.MVC.Models;
using AdminPanel.MVC.Services;
using Microsoft.AspNetCore.Mvc;

namespace AdminPanel.MVC.Controllers;

public class OrdersController(ApiClient api) : ManagementController(api)
{
    public async Task<IActionResult> Index([FromQuery] AdminListQuery query) =>
        View(await List<OrderViewModel>("orders", query));

    public async Task<IActionResult> Details(int id)
    {
        var order = await api.GetAsync<OrderViewModel>($"orders/{id}");
        return order is null ? NotFound() : View(order);
    }
    [HttpPost]
    public async Task<IActionResult> UpdateStatus(int id, string newStatus)
    {
        using var response = await api.PutAsync($"orders/{id}/status", newStatus);
        TempData["Message"] = response.IsSuccessStatusCode ? "Order status updated." : "Could not update the order status.";
        return RedirectToAction(nameof(Details), new { id });
    }
    [HttpPost]
    public async Task<IActionResult> Cancel(int id)
    {
        using var response = await api.DeleteAsync($"orders/{id}");
        TempData["Message"] = response.IsSuccessStatusCode ? "Order cancelled and stock restored." : "Could not cancel the order.";
        return RedirectToAction(nameof(Index));
    }
}
