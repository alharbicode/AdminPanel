using Microsoft.AspNetCore.Localization;

namespace AdminPanel.Storefront.Services;

public static class StoreLanguage
{
    public static bool SetCookie(HttpContext context, string culture)
    {
        if (culture is not ("ar" or "en")) return false;
        context.Response.Cookies.Append("AlharbiStore.Culture",
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), HttpOnly = true, Secure = true,
                SameSite = SameSiteMode.Lax, IsEssential = true });
        return true;
    }
}
