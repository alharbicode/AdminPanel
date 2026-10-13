using AdminPanel.Storefront.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddHttpClient<CatalogClient>();
builder.Services.Configure<StoreOptions>(builder.Configuration.GetSection("Store"));
builder.Services.AddScoped<StoreText>();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.SetDefaultCulture("ar").AddSupportedCultures("ar", "en").AddSupportedUICultures("ar", "en");
    // Arabic first on a new visit, then the customer's explicit language preference.
    options.RequestCultureProviders = [new CookieRequestCultureProvider { CookieName = "AlharbiStore.Culture" }];
});
var app = builder.Build();
app.UseExceptionHandler("/Home/Error");
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self'; style-src 'self'; script-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
    await next(context);
});
app.UseStaticFiles();
app.UseRequestLocalization();
app.UseRouting();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.Run();
public partial class Program { }
