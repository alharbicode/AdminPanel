using System.Net;
using System.Globalization;
using AdminPanel.Storefront.Models;
using AdminPanel.Storefront.Services;
using Microsoft.AspNetCore.Mvc;

namespace AdminPanel.Storefront.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CheckoutController(CheckoutGateway checkout, CartGateway carts, CustomerClient api, StoreText text) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var cart = await carts.Read();
        if (cart.Items.Count == 0) { TempData["Notice"] = "CartEmpty"; return RedirectToAction("Index", "Cart"); }
        if (checkout.Customer && carts.HasGuest)
        { TempData["Notice"] = "MergePending"; return RedirectToAction("Index", "Cart"); }
        var model = new CheckoutModel { CartVersion = cart.Version, IdempotencyKey = Guid.NewGuid() };
        if (checkout.Customer)
        {
            var customer = await api.Send<Customer>(HttpMethod.Get, "customer/account/profile");
            model.ContactName = customer.DisplayName; model.Email = customer.Email;
        }
        return await Form(model, cart);
    }
    [HttpPost]
    public async Task<IActionResult> Review(CheckoutModel model)
    {
        if (model.IdempotencyKey is null || model.IdempotencyKey == Guid.Empty)
            ModelState.AddModelError("", text["CheckoutInvalid"]);
        if (!ModelState.IsValid) return await Form(model);
        try
        {
            var quote = await checkout.Quote(model);
            var state = checkout.CreateReview(model, quote);
            return View(new ReviewPage(state, checkout.Protect(state)));
        }
        catch (CustomerApiException e) when (Recoverable(e))
        {
            ModelState.AddModelError("", text[e.Code]);
            return await Form(model, refreshVersion: true);
        }
    }
    [HttpPost]
    public IActionResult ChangeLanguage(string reviewToken, string culture)
    {
        var state = checkout.ReadReview(reviewToken);
        if (state is null) return InvalidReview();
        if (!StoreLanguage.SetCookie(HttpContext, culture)) return BadRequest();
        // Render the same protected review in the selected language, without putting its token in a URL.
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        return View("Review", new ReviewPage(state, reviewToken));
    }
    [HttpPost]
    public async Task<IActionResult> Edit(string reviewToken)
    {
        var state = checkout.ReadReview(reviewToken);
        if (state is null) return InvalidReview();
        return await Form(state.Form, refreshVersion: true);
    }
    [HttpPost]
    public async Task<IActionResult> Confirm(string reviewToken)
    {
        var state = checkout.ReadReview(reviewToken);
        if (state is null) return InvalidReview();
        try
        {
            var result = await checkout.Confirm(state);
            TempData["CreatedOrder"] = result.Order.Id;
            if (!checkout.Customer && result.GuestTrackingToken is not null)
            {
                checkout.RememberGuestOrder(result.GuestTrackingToken);
                // TempData is encrypted, Secure and HttpOnly. The code is shown on the next response only.
                TempData["OrderTrackingCode"] = result.GuestTrackingToken;
                return RedirectToAction("Guest", "Orders");
            }
            return RedirectToAction("Details", "Orders", new { id = result.Order.Id });
        }
        catch (CustomerApiException e) when (Recoverable(e))
        {
            ModelState.AddModelError("", text[e.Code]);
            if (e.Status == HttpStatusCode.Conflict && e.Code != "IdempotencyConflict")
                return await Form(state.Form, refreshVersion: true);
            return View("Review", new ReviewPage(state, reviewToken));
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException && !HttpContext.RequestAborted.IsCancellationRequested)
        {
            // Keep the same protected payload and key: the API may have committed before its response was lost.
            ModelState.AddModelError("", text["CheckoutRetry"]);
            return View("Review", new ReviewPage(state, reviewToken));
        }
    }
    private IActionResult InvalidReview()
    {
        TempData["Notice"] = "CheckoutInvalid";
        return RedirectToAction(nameof(Index));
    }
    private static bool Recoverable(CustomerApiException e) => e.Status is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    private async Task<IActionResult> Form(CheckoutModel model, CartSnapshot? cart = null, bool refreshVersion = false)
    {
        // Validation responses and Edit are POST-only routes; the normal language selector must return to a GET.
        ViewData["LanguageReturnUrl"] = Url.Action(nameof(Index), "Checkout");
        cart ??= await carts.Read();
        if (cart.Adjusted)
        {
            TempData["Notice"] = "CheckoutStockChanged";
            return RedirectToAction("Index", "Cart");
        }
        if (refreshVersion)
        {
            model.CartVersion = cart.Version;
            ModelState.Remove(nameof(model.CartVersion));
        }
        return View("Index", new CheckoutPage(model, await checkout.Destinations(), cart));
    }
}
