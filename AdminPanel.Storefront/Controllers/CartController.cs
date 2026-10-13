using System.Net;
using AdminPanel.Storefront.Models;
using AdminPanel.Storefront.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace AdminPanel.Storefront.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CartController(CartGateway carts) : Controller
{
    [HttpGet] public async Task<IActionResult> Index() =>
        View(new CartPage(await carts.Read(), User.Identity?.IsAuthenticated == true && carts.HasGuest));
    [HttpPost] public Task<IActionResult> Add(CartEditModel model) => Change(model, "add");
    [HttpPost] public Task<IActionResult> Update(CartEditModel model) => Change(model, "set");
    [HttpPost] public Task<IActionResult> Remove(CartEditModel model) => Change(model, "remove");
    private async Task<IActionResult> Change(CartEditModel model, string operation)
    {
        if (!ModelState.IsValid) { TempData["Notice"] = "QuantityInvalid"; return RedirectToAction(nameof(Index)); }
        try
        {
            var cart = await carts.Change(model, operation);
            TempData["Notice"] = cart.Adjusted ? "CartAdjusted" : operation == "add" ? "CartAdded" : "CartUpdated";
        }
        catch (CustomerApiException e) when (e.Status is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
        { TempData["Notice"] = e.Code; }
        return operation == "add" && Url.IsLocalUrl(model.ReturnUrl)
            ? LocalRedirect(model.ReturnUrl!) : RedirectToAction(nameof(Index));
    }
    [Authorize, HttpPost]
    public async Task<IActionResult> Merge()
    {
        try
        {
            var cart = await carts.Merge();
            TempData["Notice"] = cart is null ? "GuestExpired" : cart.Adjusted ? "CartAdjusted" : "CartMerged";
        }
        catch (CustomerApiException e) when (e.Status is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
        { TempData["Notice"] = "MergePending"; }
        return RedirectToAction(nameof(Index));
    }
}

