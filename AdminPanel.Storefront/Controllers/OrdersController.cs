using System.ComponentModel.DataAnnotations;
using System.Net;
using AdminPanel.Storefront.Models;
using AdminPanel.Storefront.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AdminPanel.Storefront.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OrdersController(CheckoutGateway checkout, CustomerClient api, StoreText text) : Controller
{
    [Authorize, HttpGet]
    public async Task<IActionResult> Index([Range(1, int.MaxValue)] int pageNumber = 1)
    {
        if (!ModelState.IsValid) { TempData["Notice"] = "InvalidInput"; return RedirectToAction(nameof(Index)); }
        return View(await api.Send<CustomerOrders>(HttpMethod.Get, "customer/orders?pageNumber=" + pageNumber + "&pageSize=20"));
    }
    [Authorize, HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        try { return OrderView(await checkout.Order(id), false); }
        catch (CustomerApiException e) when (e.Status == HttpStatusCode.NotFound) { return Missing(); }
    }
    [HttpGet] public IActionResult Track() => View(new TrackOrderModel());
    [HttpPost]
    public async Task<IActionResult> Track(TrackOrderModel model)
    {
        if (!ModelState.IsValid) return View(model);
        try
        {
            await checkout.GuestOrder(model.TrackingCode);
            checkout.RememberGuestOrder(model.TrackingCode);
            return RedirectToAction(nameof(Guest));
        }
        catch (CustomerApiException e) when (e.Status is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
        { ModelState.AddModelError("", text[e.Code]); return View(model); }
    }
    [HttpGet]
    public async Task<IActionResult> Guest()
    {
        var token = checkout.GuestOrderToken;
        if (token is null) return RedirectToAction(nameof(Track));
        try { return OrderView(await checkout.GuestOrder(token), true); }
        catch (CustomerApiException e) when (e.Status == HttpStatusCode.NotFound)
        { checkout.ForgetGuestOrder(); TempData["Notice"] = "OrderNotFound"; return RedirectToAction(nameof(Track)); }
    }
    [HttpPost]
    public IActionResult Forget()
    {
        checkout.ForgetGuestOrder();
        return RedirectToAction(nameof(Track));
    }
    [HttpPost] public Task<IActionResult> Cancel(OrderActionModel model) => Change(model, "cancel");
    [HttpPost] public Task<IActionResult> RequestCancellation(OrderActionModel model) => Change(model, "cancellation-request");
    private async Task<IActionResult> Change(OrderActionModel model, string operation)
    {
        if (!model.Guest && !checkout.Customer) return Challenge();
        if (model.Guest && checkout.GuestOrderToken is null) return RedirectToAction(nameof(Track));
        if (!ModelState.IsValid)
        {
            TempData["Notice"] = "CancellationReasonRequired";
            return OrderRedirect(model);
        }
        try
        {
            await checkout.ChangeOrder(model, operation);
            TempData["Notice"] = operation == "cancel" ? "OrderCancelled" : "CancellationRequested";
        }
        catch (CustomerApiException e) when (e.Status is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
        { TempData["Notice"] = e.Code; }
        return OrderRedirect(model);
    }
    private IActionResult OrderRedirect(OrderActionModel model) => model.Guest ? RedirectToAction(nameof(Guest)) : RedirectToAction(nameof(Details), new { id = model.Id });
    private IActionResult OrderView(OrderSnapshot order, bool guest)
    {
        var confirmed = TempData["CreatedOrder"] is int id && id == order.Id;
        var code = guest && confirmed ? TempData["OrderTrackingCode"] as string : null;
        return View("Details", new OrderPage(order, guest, confirmed, code));
    }
    private IActionResult Missing()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return View("Missing");
    }
}
