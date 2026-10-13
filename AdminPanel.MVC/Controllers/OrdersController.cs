using AdminPanel.MVC.Models;
using AdminPanel.MVC.Services;
using Microsoft.AspNetCore.Mvc;

namespace AdminPanel.MVC.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class OrdersController(ApiClient api) : ManagementController(api)
{
    public async Task<IActionResult> Index([FromQuery] AdminListQuery query) =>
        View(await List<OrderViewModel>("orders", query));

    public async Task<IActionResult> Details(int id)
    {
        var order = await Api.GetAsync<OrderViewModel>($"orders/{id}");
        return order is null ? NotFound() : View(order);
    }
    [HttpPost]
    public async Task<IActionResult> UpdateStatus(int id, string newStatus, Guid? version)
    {
        if (!ModelState.IsValid || version is null)
        {
            TempData["Message"] = "Reload the order before changing its status.";
            return RedirectToAction(nameof(Details), new { id });
        }
        using var response = await Api.PutAsync($"orders/{id}/transition", new { status = newStatus, version });
        TempData["Message"] = response.IsSuccessStatusCode ? "Order status updated." : "Could not update the order status. It may have changed. Review the current state below.";
        return RedirectToAction(nameof(Details), new { id });
    }
    [HttpPost]
    public async Task<IActionResult> Cancel(int id, Guid? version, string? reason)
    {
        if (!ModelState.IsValid || version is null || string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            TempData["Message"] = "Enter a cancellation reason of up to 500 characters and review the current order.";
            return RedirectToAction(nameof(Details), new { id });
        }
        using var response = await Api.PostAsync($"orders/{id}/cancel", new { version, reason });
        TempData["Message"] = response.IsSuccessStatusCode ? "Order cancelled and stock restored." :
            "Could not cancel the order. It may have changed or already shipped. Review the current state below.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
