using AdminPanel.Storefront.Services;
using Microsoft.AspNetCore.Mvc;
namespace AdminPanel.Storefront.ViewComponents;
public sealed class CartCountViewComponent(CartGateway carts) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        int? count;
        try { count = (await carts.Read()).ItemCount; }
        catch (Exception e) when (e is CustomerApiException or HttpRequestException or TaskCanceledException) { count = null; }
        return View(count);
    }
}

