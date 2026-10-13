using System.Net.Http.Json;
using System.Text.Json;
using AdminPanel.API.Data;
using AdminPanel.API.Models;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace AdminPanel.BrowserTests;

// Real browser -> HTTPS MVC -> API -> disposable database, including administrator fulfillment.
internal static class CheckoutRunner
{
    private static int _checks;
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); _checks++; }

    public static async Task<int> Run(string[] args)
    {
        using var playwright = await Playwright.CreateAsync();
        var failed = false;
        foreach (var type in args.Contains("--system-edge") ? new[] { playwright.Chromium } : new[] { playwright.Chromium, playwright.Firefox })
        {
            _checks = 0;
            await using var host = new StorefrontBrowserHost(); await host.Start();
            await using var adminHost = new BrowserHost(host.Api); await adminHost.Start();
            await using var browser = await type.LaunchAsync(new() { Headless = !args.Contains("--headed"), Channel = args.Contains("--system-edge") ? "msedge" : null });
            await using var context = await browser.NewContextAsync(new() { BaseURL = host.Url, IgnoreHTTPSErrors = true, ViewportSize = new() { Width = 1440, Height = 1000 } });
            await using var adminContext = await browser.NewContextAsync(new() { BaseURL = adminHost.Url, IgnoreHTTPSErrors = true });
            var page = await context.NewPageAsync(); var adminPage = await adminContext.NewPageAsync();
            page.SetDefaultTimeout(15000); adminPage.SetDefaultTimeout(15000);
            var errors = new List<string>(); page.PageError += (_, e) => errors.Add(e); adminPage.PageError += (_, e) => errors.Add(e);
            var secrets = new List<string> { host.Api.Password };
            var output = Path.Combine(AdminPanel.Tests.TestPaths.RepositoryRoot, "AdminPanel.BrowserTests", "TestResults", "checkout-" + type.Name);
            Directory.CreateDirectory(output);
            try
            {
                using var api = await host.Api.AdminClient();
                await adminPage.GotoAsync("/Account/Login");
                await adminPage.GetByLabel("Username", new() { Exact = true }).FillAsync("admin");
                await adminPage.GetByLabel("Password", new() { Exact = true }).FillAsync(host.Api.Password);
                await adminPage.GetByRole(AriaRole.Button, new() { Name = "Login", Exact = true }).ClickAsync();
                await Expect(adminPage.GetByRole(AriaRole.Heading, new() { Name = "Dashboard", Exact = true })).ToBeVisibleAsync();
                await adminPage.GotoAsync("/ShippingZones/Create");
                await adminPage.Locator("#Governorate").FillAsync("Sana'a");
                await adminPage.Locator("#City").FillAsync("Sana'a");
                await adminPage.Locator("#Fee").FillAsync("500");
                await adminPage.Locator("#Enabled").CheckAsync();
                await adminPage.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();
                await Expect(adminPage.Locator("main")).ToContainTextAsync("Sana'a");
                var zones = await api.GetFromJsonAsync<JsonElement>("/api/shippingzones/admin/list");
                var zone = zones.GetProperty("items").EnumerateArray().Single(); var zoneId = zone.GetProperty("id").GetInt32();
                await adminPage.GotoAsync("/ShippingZones/Edit/" + zoneId);
                await adminPage.Locator("#Fee").FillAsync("600");
                await adminPage.Locator("main form button[type=submit]").ClickAsync();
                await adminPage.GotoAsync("/ShippingZones/Edit/" + zoneId);
                Check(decimal.Parse(await adminPage.Locator("#Fee").InputValueAsync(), System.Globalization.CultureInfo.InvariantCulture) == 600m, "Edited shipping fee did not persist");
                await adminPage.Locator("#Fee").FillAsync("500");
                await adminPage.Locator("main form button[type=submit]").ClickAsync();
                await adminPage.GotoAsync("/ShippingZones/Disable/" + zoneId);
                await adminPage.GetByRole(AriaRole.Button, new() { Name = "Disable shipping zone", Exact = true }).ClickAsync();
                Check((await host.Api.Client().GetFromJsonAsync<JsonElement>("/api/checkout/destinations")).GetArrayLength() == 0, "Disabled zone remained public");
                await adminPage.GotoAsync("/ShippingZones/Edit/" + zoneId);
                await adminPage.Locator("#Enabled").CheckAsync();
                await adminPage.Locator("main form button[type=submit]").ClickAsync();
                (await api.PostAsJsonAsync("/api/discounts", new { name = "Browser automatic discount", percentage = 20, productId = 1, startDate = DateTime.UtcNow.AddDays(-1), endDate = DateTime.UtcNow.AddDays(1) })).EnsureSuccessStatusCode();

                await Add(page, 2); await English(page);
                await page.GotoAsync("/Checkout");
                await Fill(page, zoneId);
                await Screens(page, output, "delivery");
                await page.Locator("form[data-checkout-form] button").ClickAsync();
                await Expect(page.Locator(".order-grand-total bdi")).ToHaveTextAsync("2,500.80");
                await Screens(page, output, "review");
                await page.GetByRole(AriaRole.Button, new() { Name = "العربية", Exact = true }).ClickAsync();
                await Expect(page.Locator("html")).ToHaveAttributeAsync("dir", "rtl");
                await Expect(page.Locator("form[data-checkout-confirm]")).ToHaveCountAsync(1);
                await Screens(page, output, "review-rtl");
                await English(page);
                Check(await MissingCsrf(page, "/Checkout/Confirm") == 400, "Checkout accepted missing CSRF");
                await page.Locator("form[data-checkout-confirm] button").ClickAsync();
                await Expect(page.Locator("#saved-tracking-code")).ToBeVisibleAsync();
                var tracking = await page.Locator("#saved-tracking-code").InputValueAsync(); secrets.Add(tracking);
                Check(tracking.Length == 43 && !page.Url.Contains(tracking), "Guest access code missing or exposed in URL");
                var cookies = await context.CookiesAsync();
                Check(cookies.Any(c => c.Name == "__Host-Alharbi.Order" && c.HttpOnly && c.Secure && c.SameSite == SameSiteAttribute.Lax), "Tracking cookie protections");
                await Screens(page, output, "confirmation");
                int orderId;
                using (var scope = host.Api.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var order = db.Orders.Single(); orderId = order.Id;
                    Check(order.CustomerId is null && order.TotalPrice == 2500.80m && order.DiscountTotal == 500.20m && order.ShippingFee == 500m && order.Currency == "YER" && order.PaymentMethod == "COD", "Guest checkout snapshots");
                    Check(db.Products.Find(1)!.StockQuantity == 8 && !db.ShoppingCartItems.Any(), "Checkout stock/cart atomicity");
                }
                await adminPage.GotoAsync("/Orders/Details/" + orderId);
                await Expect(adminPage.Locator("main")).ToContainTextAsync("Browser buyer");
                await Expect(adminPage.Locator("main")).ToContainTextAsync("2,500.80 YER");
                foreach (var next in new[] { "Confirmed", "Preparing" })
                {
                    await adminPage.GetByLabel("New status").SelectOptionAsync(next);
                    await adminPage.GetByRole(AriaRole.Button, new() { Name = "Update", Exact = true }).ClickAsync();
                    await Expect(adminPage.Locator("main")).ToContainTextAsync(next);
                }
                await page.GotoAsync("/Orders/Guest");
                await Expect(page.Locator("form[action='/Orders/Cancel']")).ToHaveCountAsync(0);
                await page.Locator("#Reason").FillAsync("Please review my delivery change");
                await page.Locator("form[action='/Orders/RequestCancellation'] button").ClickAsync();
                await adminPage.ReloadAsync();
                await Expect(adminPage.Locator("main")).ToContainTextAsync("Please review my delivery change");
                adminPage.Dialog += async (_, dialog) => await dialog.AcceptAsync();
                await adminPage.GetByLabel("Cancellation reason", new() { Exact = true }).FillAsync("Accepted before shipment");
                await adminPage.GetByRole(AriaRole.Button, new() { Name = "Cancel Order", Exact = true }).ClickAsync();
                await Expect(adminPage.Locator("main")).ToContainTextAsync("Order cancelled and stock restored.");
                await page.ReloadAsync(); await Expect(page.Locator(".order-status")).ToHaveTextAsync("Cancelled");
                using (var scope = host.Api.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    Check(db.Products.Find(1)!.StockQuantity == 10 && db.Orders.Count() == 1 && db.OrderItems.Count() == 1, "Cancellation lost history or stock");
                }
                await using var basicContext = await browser.NewContextAsync(new() { BaseURL = host.Url, IgnoreHTTPSErrors = true, JavaScriptEnabled = false, ViewportSize = new() { Width = 390, Height = 900 } });
                var basic = await basicContext.NewPageAsync(); await basic.GotoAsync("/Orders/Track"); await English(basic);
                await basic.Locator("#TrackingCode").FillAsync(tracking);
                await basic.Locator("main form button").ClickAsync();
                await Expect(basic.Locator(".order-status")).ToHaveTextAsync("Cancelled");
                Check(!basic.Url.Contains(tracking), "No-JS tracking exposed secret");
                await Add(basic, 1); await basic.GotoAsync("/Checkout"); await Fill(basic, zoneId);
                await basic.Locator("form[data-checkout-form] button").ClickAsync();
                await basic.Locator("form[data-checkout-confirm] button").ClickAsync();
                await Expect(basic.Locator("#saved-tracking-code")).ToBeVisibleAsync();
                secrets.Add(await basic.Locator("#saved-tracking-code").InputValueAsync());
                await basic.Locator("#Reason").FillAsync("No-JS cancellation");
                await basic.Locator("form[action='/Orders/Cancel'] button").ClickAsync();
                await Expect(basic.Locator(".order-status")).ToHaveTextAsync("Cancelled");

                await page.GotoAsync("/Account/Register");
                await page.Locator("#DisplayName").FillAsync("Registered buyer");
                await page.Locator("#Email").FillAsync("checkout-browser@example.test");
                await page.Locator("#Password").FillAsync(host.Api.Password);
                await page.Locator("#ConfirmPassword").FillAsync(host.Api.Password);
                await page.Locator(".account-panel form button").ClickAsync();
                await Add(page, 1); await page.GotoAsync("/Checkout");
                await Expect(page.Locator("#ContactName")).ToHaveValueAsync("Registered buyer");
                await Fill(page, zoneId); await page.Locator("form[data-checkout-form] button").ClickAsync();
                await page.Locator("form[data-checkout-confirm] button").ClickAsync();
                Check(page.Url.Contains("/Orders/Details/"), "Registered confirmation route");
                Check(await MissingCsrf(page, "/Orders/Cancel") == 400, "Cancellation accepted missing CSRF");
                await page.Locator("#Reason").FillAsync("Customer changed plans");
                await page.Locator("form[action='/Orders/Cancel'] button").ClickAsync();
                await Expect(page.Locator(".order-status")).ToHaveTextAsync("Cancelled");
                await page.GotoAsync("/Orders"); await Expect(page.Locator(".order-history-card")).ToHaveCountAsync(1);
                await Screens(page, output, "history");
                await page.GetByRole(AriaRole.Button, new() { Name = "العربية", Exact = true }).ClickAsync();
                await Screens(page, output, "history-rtl");
                await page.Locator(".customer-nav button").ClickAsync(); await page.GotoAsync("/Orders");
                Check(page.Url.Contains("/Account/Login"), "Order history accessible after logout");
                Check(errors.Count == 0, "Checkout JavaScript errors: " + string.Join("; ", errors));
                Console.WriteLine($"CHECKOUT BROWSER PASS: {type.Name}; {_checks} explicit checks plus Playwright assertions; guest/customer/no-JS checkout, shipping admin, discounts, fulfillment, cancellation, tracking, CSRF, RTL/LTR and responsive screens.");
                await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { browser = type.Name, status = "passed", checks = _checks }));
            }
            catch (Exception error)
            {
                failed = true;
                await page.ScreenshotAsync(new() { Path = Path.Combine(output, "failure.png"), FullPage = true, Mask = [page.Locator("input[type=password], #saved-tracking-code, #TrackingCode")] });
                var safe = error.ToString(); foreach (var secret in secrets) safe = safe.Replace(secret, "[test-secret]");
                Console.Error.WriteLine("CHECKOUT BROWSER FAIL: " + type.Name + ": " + safe);
                await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { browser = type.Name, status = "failed", error = safe }));
            }
        }
        return failed ? 1 : 0;
    }
    private static async Task Add(IPage page, int quantity)
    {
        await page.BringToFrontAsync();
        await page.GotoAsync("/Catalog/Details/1"); await page.Locator("#product-quantity").FillAsync(quantity.ToString());
        // Keyboard activation also verifies the native form works without client JavaScript.
        await page.Locator(".detail-cart-form button").PressAsync("Enter"); await Expect(page.Locator("#qty-1")).ToHaveValueAsync(quantity.ToString());
    }
    private static Task English(IPage page) => page.GetByRole(AriaRole.Button, new() { Name = "English", Exact = true }).ClickAsync();
    private static async Task Fill(IPage page, int zone)
    {
        await page.Locator("#ContactName").FillAsync("Browser buyer"); await page.Locator("#Phone").FillAsync("+967 777 000 111");
        await page.Locator("#Email").FillAsync("buyer@example.test"); await page.Locator("#ShippingZoneId").SelectOptionAsync(zone.ToString());
        await page.Locator("#City").FillAsync("Sana'a"); await page.Locator("#AddressLine").FillAsync("Disposable browser test address, building 1");
        await page.Locator("#DeliveryInstructions").FillAsync("Test delivery instructions");
    }
    private static Task<int> MissingCsrf(IPage page, string path) => page.EvaluateAsync<int>("async path => (await fetch(path,{method:'POST'})).status", path);
    private static async Task Screens(IPage page, string output, string stage)
    {
        foreach (var width in new[] { 1440, 768, 390, 320 })
        {
            await page.SetViewportSizeAsync(width, 1000);
            await Expect(page.Locator("main h1")).ToHaveCountAsync(1);
            Check(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"), "Checkout overflow: " + stage + " " + width);
            Check(await page.EvaluateAsync<int>("() => [...document.querySelectorAll('input:not([type=hidden]),select,textarea')].filter(e => !e.labels?.length && !e.getAttribute('aria-label')).length") == 0, "Unlabelled checkout control");
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, stage + "-" + width + ".png"), FullPage = true, Mask = [page.Locator("input[type=password], #saved-tracking-code, #TrackingCode")] });
        }
    }
}
