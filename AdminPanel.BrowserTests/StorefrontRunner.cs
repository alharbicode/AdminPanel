using System.Net.Http.Json;
using System.Text.Json;
using AdminPanel.API.Data;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace AdminPanel.BrowserTests;
internal static class StorefrontRunner
{
    private static int _checks;
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); _checks++; }
    public static async Task<int> Run(string[] args)
    {
        using var playwright = await Playwright.CreateAsync();
        var failed = false;
        foreach (var browserType in args.Contains("--system-edge") ? new[] { playwright.Chromium } : new[] { playwright.Chromium, playwright.Firefox })
        {
            _checks = 0;
            var output = Path.Combine(AdminPanel.Tests.TestPaths.RepositoryRoot, "AdminPanel.BrowserTests", "TestResults", "storefront-" + browserType.Name);
            Directory.CreateDirectory(output);
            await using var host = new StorefrontBrowserHost(); await host.Start();
            await using var browser = await browserType.LaunchAsync(new() { Headless = !args.Contains("--headed"), Channel = args.Contains("--system-edge") ? "msedge" : null });
            await using var context = await browser.NewContextAsync(new() { BaseURL = host.Url, IgnoreHTTPSErrors = true, ViewportSize = new() { Width = 1440, Height = 1000 } });
            var page = await context.NewPageAsync();
            var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
            try
            {
                await page.GotoAsync("/");
                await Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "ar");
                await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("اكتشف ما يناسب يومك.");
                foreach (var language in new[] { "ar", "en" })
                {
                    await page.GotoAsync("/");
                    await page.GetByRole(AriaRole.Button, new() { Name = language == "ar" ? "العربية" : "English", Exact = true }).ClickAsync();
                    foreach (var width in new[] { 1440, 1024, 768, 390, 320 })
                    {
                        await page.SetViewportSizeAsync(width, 1000);
                        foreach (var route in new[] { "/", "/Catalog", "/Catalog/Categories", "/Catalog/Details/27", "/Catalog/Details/28", "/Catalog?Page=0" })
                        {
                            var response = await page.GotoAsync(route);
                            Check(response?.Status == (route.EndsWith("/28") ? 404 : route.EndsWith("=0") ? 400 : 200), "Route status: " + route);
                            await Expect(page.Locator("main h1")).ToHaveCountAsync(1);
                            await Expect(page.Locator("html")).ToHaveAttributeAsync("dir", language == "ar" ? "rtl" : "ltr");
                            Check(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"), $"Overflow {language}/{width}/{route}");
                            Check(await page.EvaluateAsync<int>("() => [...document.querySelectorAll('input:not([type=hidden]),select')].filter(e => !e.labels?.length && !e.getAttribute('aria-label')).length") == 0, "Unlabelled controls");
                            // Eager-load below-fold fixture images before full-page screenshot capture.
                            await page.EvaluateAsync("() => { document.querySelectorAll('img[loading=lazy]').forEach(img => img.loading = 'eager'); }");
                            await page.EvaluateAsync("() => Promise.all([...document.images].map(img => img.complete ? Promise.resolve() : new Promise(resolve => { img.onload = resolve; img.onerror = resolve; })))");
                            Check(await page.EvaluateAsync<bool>("() => [...document.images].filter(i=>i.loading !== 'lazy').every(i=>i.naturalWidth>0)"), "Broken eager image");
                            await page.ScreenshotAsync(new() { Path = Path.Combine(output, $"{language}-{width}-{(route == "/" ? "Home" : route.Trim('/').Replace('/', '-').Replace('?', '-').Replace('=', '-'))}.png"), FullPage = true });
                        }
                    }
                }
                await page.SetViewportSizeAsync(1440, 1000);
                await page.GotoAsync("/");
                await page.Keyboard.PressAsync("Tab");
                await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Skip to content" })).ToBeFocusedAsync();
                await page.Keyboard.PressAsync("Enter"); await Expect(page.Locator("main")).ToBeFocusedAsync();
                await page.Locator("#global-search").FillAsync("Office");
                await page.GetByRole(AriaRole.Button, new() { Name = "Search catalog", Exact = true }).ClickAsync();
                await Expect(page.Locator(".product-card")).ToHaveCountAsync(8);
                await page.Locator("#category-filter").SelectOptionAsync("3");
                await page.Locator("#catalog-sort").SelectOptionAsync("name");
                await page.GetByRole(AriaRole.Button, new() { Name = "Apply filters", Exact = true }).ClickAsync();
                Check(page.Url.Contains("CategoryId=3") && page.Url.Contains("Sort=name"), "Filters not preserved.");
                await page.GotoAsync("/Catalog?PageSize=3&Search=Office&Sort=name&CategoryId=3");
                await page.GetByRole(AriaRole.Link, new() { Name = "Next", Exact = true }).ClickAsync();
                Check(page.Url.Contains("Page=2") && page.Url.Contains("Search=Office") && page.Url.Contains("CategoryId=3"), "Paging lost filters");
                await page.GetByRole(AriaRole.Button, new() { Name = "العربية", Exact = true }).ClickAsync();
                Check(page.Url.Contains("Page=2") && page.Url.Contains("Search=Office"), "Language switch lost location");
                await Expect(page.Locator("html")).ToHaveAttributeAsync("dir", "rtl");
                await page.GotoAsync("/Catalog/Categories");
                await page.Locator("#category-search").FillAsync("المكتب");
                await page.Locator(".category-search button").ClickAsync();
                await Expect(page.Locator(".category-card")).ToHaveCountAsync(1);
                await page.Locator(".category-card").ClickAsync();
                Check(page.Url.Contains("CategoryId=3"), "Category link did not filter");
                await page.GotoAsync("/Catalog/Details/27");
                await Expect(page.Locator(".product-description")).ToContainTextAsync("<script>unsafe()</script>");
                await Expect(page.Locator(".detail-image img")).ToHaveAttributeAsync("src", "/Catalog/Image/27");
                Check(await page.EvaluateAsync<int>("async () => (await fetch('/Language/Change', {method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:'culture=en'})).status") == 400, "Missing CSRF accepted");
                foreach (var route in new[] { "/Account/Register", "/Checkout", "/Cart" })
                    Check(await page.EvaluateAsync<int>("async path => (await fetch(path)).status", route) == 404, "Future phase exposed: " + route);
                await page.GotoAsync("/");
                await context.ClearCookiesAsync();
                await page.GotoAsync("/"); await Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "ar");
                var noJs = await browser.NewContextAsync(new() { BaseURL = host.Url, IgnoreHTTPSErrors = true, JavaScriptEnabled = false, ViewportSize = new() { Width = 390, Height = 900 } });
                await using (noJs)
                {
                    var basic = await noJs.NewPageAsync(); await basic.GotoAsync("/");
                    await basic.GetByRole(AriaRole.Button, new() { Name = "English", Exact = true }).ClickAsync();
                    await Expect(basic.Locator("html")).ToHaveAttributeAsync("lang", "en");
                    await basic.Locator("#global-search").FillAsync("Office");
                    await basic.Locator("#global-search").PressAsync("Enter");
                    await Expect(basic.Locator(".product-card")).ToHaveCountAsync(8);
                }
                await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
                await page.GotoAsync("/");
                Check(await page.Locator(".button").First.EvaluateAsync<string>("e => getComputedStyle(e).transitionDuration") == "0s", "Reduced motion not honored.");
                Check(errors.Count == 0, string.Join("; ", errors));
                Console.WriteLine($"STOREFRONT BROWSER PASS: {browserType.Name}; {_checks} explicit checks plus Playwright assertions; Arabic/English, five viewport widths, real catalog, filters, paging, images, safe errors, CSRF, keyboard and no-JavaScript navigation.");
                await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { browser = browserType.Name, status = "passed", checks = _checks }));
            }
            catch(Exception error)
            {
                failed = true; await page.ScreenshotAsync(new() { Path = Path.Combine(output, "failure.png"), FullPage = true });
                Console.Error.WriteLine($"STOREFRONT BROWSER FAIL: {browserType.Name}: {error}");
                await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { browser = browserType.Name, status = "failed", error = error.ToString() }));
            }
        }
        return failed ? 1 : 0;
    }
}
