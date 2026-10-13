using AdminPanel.Storefront.Services;
using Microsoft.AspNetCore.Mvc;
namespace AdminPanel.Storefront.Controllers;

public sealed class LanguageController : Controller
{
    [HttpPost]
    public IActionResult Change(string culture, string? returnUrl)
    {
        if (!StoreLanguage.SetCookie(HttpContext, culture)) return BadRequest();
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }
}
