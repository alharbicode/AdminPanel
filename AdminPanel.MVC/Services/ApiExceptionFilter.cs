using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AdminPanel.MVC.Services;

public class ApiExceptionFilter : IAsyncExceptionFilter
{
    public async Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.Exception is not (HttpRequestException or JsonException or TaskCanceledException)) return;
        if (context.Exception is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized })
        {
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Result = new RedirectToActionResult("Login", "Account", null);
        }
        else if (context.Exception is HttpRequestException { StatusCode: HttpStatusCode.Forbidden })
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
        else
            context.Result = new ContentResult { StatusCode = 503, Content = "The API is unavailable. Please try again later." };
        context.ExceptionHandled = true;
    }
}
