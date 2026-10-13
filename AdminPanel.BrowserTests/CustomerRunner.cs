using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
namespace AdminPanel.BrowserTests;

internal static class CustomerRunner
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
            await using var browser = await type.LaunchAsync(new() { Headless = !args.Contains("--headed"), Channel = args.Contains("--system-edge") ? "msedge" : null });
            await using var context = await browser.NewContextAsync(new() { BaseURL = host.Url, IgnoreHTTPSErrors = true, ViewportSize = new() { Width = 1440, Height = 1000 } });
            var output = Path.Combine(AdminPanel.Tests.TestPaths.RepositoryRoot, "AdminPanel.BrowserTests", "TestResults", "customers-" + type.Name);
            Directory.CreateDirectory(output);
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(15000);
            var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
            try
            {
                await page.GotoAsync("/Catalog/Details/1");
                await page.Locator("#product-quantity").FillAsync("2");
                await page.Locator(".detail-cart-form button").ClickAsync();
                await Expect(page.Locator(".cart-line")).ToHaveCountAsync(1);
                await Expect(page.Locator("#qty-1")).ToHaveValueAsync("2");
                var cookies = await context.CookiesAsync();
                Check(cookies.Any(c => c.Name == "__Host-Alharbi.Guest" && c.HttpOnly && c.Secure && c.SameSite == SameSiteAttribute.Lax), "Guest cookie protections");
                var guestCookie = cookies.Single(c => c.Name == "__Host-Alharbi.Guest").Value;
                await page.GotoAsync("/Account/Register");
                await page.Locator("#DisplayName").FillAsync("Browser customer");
                await page.Locator("#Email").FillAsync("browser@example.test");
                await page.Locator("#Password").FillAsync(host.Api.Password);
                await page.Locator("#ConfirmPassword").FillAsync(host.Api.Password + "mismatch");
                await page.Locator(".account-panel form button").ClickAsync();
                await Expect(page.Locator(".validation-summary-errors")).ToBeVisibleAsync();
                await page.Locator("#Password").FillAsync(host.Api.Password);
                await page.Locator("#ConfirmPassword").FillAsync(host.Api.Password);
                await page.Locator(".account-panel form button").ClickAsync();
                await Expect(page.Locator(".cart-line")).ToHaveCountAsync(1);
                await Expect(page.Locator("#qty-1")).ToHaveValueAsync("2");
                cookies = await context.CookiesAsync();
                Check(cookies.Any(c => c.Name == "__Host-Alharbi.Customer" && c.HttpOnly && c.Secure), "Customer cookie protections");
                Check(cookies.All(c => c.Name != "__Host-Alharbi.Guest"), "Guest identifier not cleared after merge");
                using (var guestApi = host.Api.Client())
                {
                    guestApi.DefaultRequestHeaders.Add("X-Guest-Cart", guestCookie);
                    Check((int)(await guestApi.GetAsync("/api/guest/cart")).StatusCode == 404, "Consumed guest cart still accessible");
                }
                await page.Locator("#qty-1").FillAsync("4");
                await page.Locator(".quantity-form button").ClickAsync();
                await Expect(page.Locator(".cart-count")).ToHaveTextAsync("4");
                foreach (var language in new[] { "ar", "en" })
                {
                    await page.GetByRole(AriaRole.Button, new() { Name = language == "ar" ? "العربية" : "English", Exact = true }).ClickAsync();
                    foreach (var width in new[] { 1440, 768, 390, 320 })
                    {
                        await page.SetViewportSizeAsync(width, 1000);
                        foreach (var route in new[] { "/Cart", "/Account/Register", "/Account/Login", "/Account/ForgotPassword", "/Account/ResetPassword", "/Account/Profile", "/Account/ChangePassword" })
                        {
                            Check((await page.GotoAsync(route))?.Status == 200, "Customer route failed: " + route);
                            await Expect(page.Locator("main h1")).ToHaveCountAsync(1);
                            await Expect(page.Locator("html")).ToHaveAttributeAsync("dir", language == "ar" ? "rtl" : "ltr");
                            Check(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"), "Customer page overflow");
                            Check(await page.EvaluateAsync<int>("() => [...document.querySelectorAll('input:not([type=hidden]),select')].filter(e => !e.labels?.length && !e.getAttribute('aria-label')).length") == 0, "Unlabelled customer control");
                            await page.ScreenshotAsync(new() { Path = Path.Combine(output, language + "-" + width + "-" + route.Trim('/').Replace('/', '-') + ".png"), FullPage = true });
                        }
                    }
                }
                await page.GotoAsync("/Account/Profile");
                await page.Locator("#DisplayName").FillAsync("Updated customer");
                await page.Locator(".account-panel form").First.Locator("button").ClickAsync();
                await Expect(page.Locator("#DisplayName")).ToHaveValueAsync("Updated customer");
                await page.Locator(".customer-nav button").ClickAsync();
                await page.GotoAsync("/Cart");
                await Expect(page.Locator(".cart-line")).ToHaveCountAsync(0);
                await page.GotoAsync("/Catalog/Details/1");
                await page.Locator("#product-quantity").FillAsync("8");
                await page.Locator(".detail-cart-form button").ClickAsync();
                await Login(page, host.Api.Password);
                await Expect(page.Locator("#qty-1")).ToHaveValueAsync("10");
                await Expect(page.Locator(".store-notice").First).ToContainTextAsync("quantities reduced");
                await page.Locator(".customer-nav button").ClickAsync();
                await Login(page, host.Api.Password);
                await page.GotoAsync("/Cart");
                await Expect(page.Locator("#qty-1")).ToHaveValueAsync("10");
                Check(await page.EvaluateAsync<int>("async () => (await fetch('/Cart/Update',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:'ProductId=1&Quantity=1'})).status") == 400, "Cart accepted missing CSRF");
                Check(await page.EvaluateAsync<int>("async () => (await fetch('/Account/Logout',{method:'POST'})).status") == 400, "Logout accepted missing CSRF");
                await page.Locator(".customer-nav button").ClickAsync();
                await page.GotoAsync("/Account/ForgotPassword");
                await page.Locator("#Email").FillAsync("browser@example.test");
                await page.Locator(".account-panel form button").ClickAsync();
                await Expect(page.Locator(".store-notice")).ToContainTextAsync("If an eligible account exists");
                var mail = host.Api.Emails.Messages.Last();
                await page.GotoAsync("/Account/ResetPassword#code=" + mail.Token + "&email=" + Uri.EscapeDataString(mail.Email));
                await Expect(page.Locator("#Token")).ToHaveValueAsync(mail.Token);
                Check(!page.Url.Contains('#') && !page.Url.Contains(mail.Token), "Reset secret remained in URL");
                await page.Locator("#Password").FillAsync(host.Api.Password + "-new");
                await page.Locator("#ConfirmPassword").FillAsync(host.Api.Password + "-new");
                await page.Locator(".account-panel form button").ClickAsync();
                await Expect(page.Locator(".store-notice")).ToContainTextAsync("Password updated");
                await Login(page, host.Api.Password + "-new");
                await page.GotoAsync("/Cart");
                await Expect(page.Locator("#qty-1")).ToHaveValueAsync("10");
                await page.Locator(".cart-line form").Last.Locator("button").ClickAsync();
                await Expect(page.Locator(".cart-line")).ToHaveCountAsync(0);
                await using var basicContext = await browser.NewContextAsync(new() { BaseURL = host.Url, IgnoreHTTPSErrors = true, JavaScriptEnabled = false, ViewportSize = new() { Width = 390, Height = 900 } });
                var basic = await basicContext.NewPageAsync();
                await basic.GotoAsync("/Catalog/Details/1");
                await basic.Locator(".detail-cart-form button").ClickAsync();
                await Expect(basic.Locator(".cart-line")).ToHaveCountAsync(1);
                await basic.GetByRole(AriaRole.Button, new() { Name = "English", Exact = true }).ClickAsync();
                await Expect(basic.Locator("html")).ToHaveAttributeAsync("lang", "en");
                await basic.GotoAsync("/Account/Login");
                await basic.Locator("#Email").FillAsync("browser@example.test");
                await basic.Locator("#Password").FillAsync(host.Api.Password + "-new");
                await basic.Locator(".account-panel form button").ClickAsync();
                await Expect(basic.Locator("#qty-1")).ToHaveValueAsync("1");
                Check(errors.Count == 0, "Customer JavaScript error");
                Console.WriteLine($"CUSTOMER BROWSER PASS: {type.Name}; {_checks} explicit checks plus Playwright assertions; accounts, recovery, guest/saved carts, merging, CSRF, cookies, localization and responsive/no-JS forms.");
                await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { browser = type.Name, status = "passed", checks = _checks }));
            }
            catch (Exception error)
            {
                failed = true;
                await page.ScreenshotAsync(new() { Path = Path.Combine(output, "failure.png"), FullPage = true });
                var safe = error.ToString().Replace(host.Api.Password, "[test-password]");
                foreach (var mail in host.Api.Emails.Messages) safe = safe.Replace(mail.Token, "[test-reset-code]");
                Console.Error.WriteLine("CUSTOMER BROWSER FAIL: " + type.Name + ": " + safe);
                await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { browser = type.Name, status = "failed", error = safe }));
            }
        }
        return failed ? 1 : 0;
    }
    private static async Task Login(IPage page, string password)
    {
        await page.GotoAsync("/Account/Login");
        await page.Locator("#Email").FillAsync("browser@example.test");
        await page.Locator("#Password").FillAsync(password);
        await page.Locator(".account-panel form button").ClickAsync();
    }
}

