using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
namespace AdminPanel.Storefront.Controllers;

public sealed class LanguageController : Controller
{
    [HttpPost]
    public IActionResult Change(string culture, string? returnUrl)
    {
        if (culture is not ("ar" or "en")) return BadRequest();
        Response.Cookies.Append("AlharbiStore.Culture",
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, IsEssential = true });
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }
}
