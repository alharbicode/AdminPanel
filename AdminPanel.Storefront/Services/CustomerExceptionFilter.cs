using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
namespace AdminPanel.Storefront.Services;

public sealed class CustomerExceptionFilter : IAsyncExceptionFilter
{
    public async Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.Exception is CustomerApiException e && e.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            await context.HttpContext.SignOutAsync("StoreCustomer");
            context.Result = new RedirectToActionResult("Login", "Account", new { expired = true });
        }
        else if (context.Exception is CustomerApiException or HttpRequestException or TaskCanceledException)
        {
            context.HttpContext.Response.StatusCode = 503;
            context.Result = new ViewResult { ViewName = "~/Views/Home/Error.cshtml" };
        }
        else return;
        context.ExceptionHandled = true;
    }
}

