using System.Net.Http.Json;
using System.Text.Json;
using AdminPanel.API.Data;
using AdminPanel.API.Models;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace AdminPanel.BrowserTests;

internal static class BrowserRunner
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--install-browsers"))
            return Microsoft.Playwright.Program.Main(new[] { "install", "--with-deps", "chromium", "firefox" });
        using var playwright = await Playwright.CreateAsync();
        var failed = false;
        foreach (var browserType in new[] { playwright.Chromium, playwright.Firefox })
        {
            var output = Path.Combine(AdminPanel.Tests.TestPaths.RepositoryRoot, "AdminPanel.BrowserTests", "TestResults", browserType.Name);
            Directory.CreateDirectory(output);
            _checks = 0;
            await using var host = new BrowserHost();
            await host.Start();
            await using var browser = await browserType.LaunchAsync(new() { Headless = !args.Contains("--headed") });
            await using var context = await browser.NewContextAsync(new()
            {
                BaseURL = host.Url, IgnoreHTTPSErrors = true, ViewportSize = new() { Width = 1365, Height = 900 }
            });
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(15000);
            var scriptErrors = new List<string>();
            page.PageError += (_, error) => scriptErrors.Add(error);
            try
            {
                using var api = await host.Api.AdminClient();
                await SeedCatalog(host);
                await Login(page, host);
                var cookies = await context.CookiesAsync();
                Check(cookies.Any(c => c.Name == "AdminPanel.Auth" && c.Secure && c.HttpOnly), "Authentication cookie must be Secure and HttpOnly.");
                await Screens(page, output);
                await Lists(page);
                await Crud(page, api);
                await SubmissionAndSecurity(page, api);
                await Picker(page);
                await WithoutJavaScript(browser, host, api);
                await page.GotoAsync("/");
                await page.GetByRole(AriaRole.Button, new() { Name = "Sign out admin" }).ClickAsync();
                await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Account/Login"));
                await page.GotoAsync("/Products");
                await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Account/Login"));
                Check(scriptErrors.Count == 0, "Unexpected JavaScript errors: " + string.Join("; ", scriptErrors));
                Console.WriteLine($"BROWSER PASS: {browserType.Name}; {_checks} explicit checks plus Playwright assertions; desktop/mobile screens, CRUD, lists, picker, validation, CSRF, loading, login/logout.");
                await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { browser = browserType.Name, status = "passed", explicitChecks = _checks }));
            }
            catch (Exception error)
            {
                failed = true;
                // Never record login form state, credentials, cookies or authentication traces.
                if (!page.Url.Contains("/Account/Login")) await page.ScreenshotAsync(new() { Path = Path.Combine(output, "failure.png"), FullPage = true });
                var safeError = error.ToString().Replace(host.Api.Password, "[REDACTED]");
                Console.Error.WriteLine($"BROWSER FAIL: {browserType.Name}: {safeError}");
                await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { browser = browserType.Name, status = "failed", error = safeError }));
            }
        }
        return failed ? 1 : 0;
    }

    private static async Task SeedCatalog(BrowserHost host)
    {
        using var scope = host.Api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        for (var i = 1; i <= 32; i++)
        {
            db.Categories.Add(new Category { Name = $"Browser category {i:00}", Description = "Search fixture" });
            db.Products.Add(new Product { Name = $"Browser item {i:00}", Description = "Search fixture", CategoryId = 1, Price = i, StockQuantity = i, IsAvailable = i % 2 == 0 });
        }
        db.Discounts.Add(new Discount { Name = "Browser seed discount", ProductId = 1, Percentage = 5, StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(1) });
        db.ContentPages.Add(new ContentPage { Title = "Browser seed page", Slug = "browser-seed", Content = "Safe preview" });
        await db.SaveChangesAsync();
        using var api = await host.Api.AdminClient();
        (await api.PostAsJsonAsync("/api/orders", new { customerId = 3, shippingOptionId = 1, items = new[] { new { productId = 1, quantity = 1 } } })).EnsureSuccessStatusCode();
    }

    private static async Task Login(IPage page, BrowserHost host)
    {
        var stage = "open login";
        try
        {
            await page.GotoAsync("/Account/Login");
            stage = "validate empty login";
            await page.GetByRole(AriaRole.Button, new() { Name = "Login", Exact = true }).ClickAsync();
            await Expect(page.Locator(".field-validation-error").First).ToBeVisibleAsync();
            stage = "fill credentials";
            await page.GetByLabel("Username", new() { Exact = true }).FillAsync("admin");
            await page.GetByLabel("Password", new() { Exact = true }).FillAsync(host.Api.Password);
            stage = "submit login";
            await page.GetByRole(AriaRole.Button, new() { Name = "Login", Exact = true }).ClickAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard", Exact = true })).ToBeVisibleAsync();
        }
        catch (Exception error)
        {
            var safeError = error.Message.Replace(host.Api.Password, "[REDACTED]");
            var diagnostics = await page.EvaluateAsync<string>(@"() => {
                const form = document.querySelector('main form');
                const validator = window.jQuery?.(form).data('validator');
                return JSON.stringify({path:location.pathname, jquery:!!window.jQuery, validator:!!validator,
                    submitting:form?.dataset.submitting, invalid:validator?.errorList.map(x => ({name:x.element.name,message:x.message})),
                    names:[...form.elements].map(x=>({name:x.name,disabled:x.disabled,filled:!!x.value})),
                    nativeValid:form?.checkValidity()});
            }");
            throw new InvalidOperationException($"Browser login failed at {stage}; {diagnostics}; {safeError}");
        }
    }

    private static async Task Screens(IPage page, string output)
    {
        var routes = new List<string> { "/", "/Orders", "/Orders/Details/1" };
        foreach (var area in new[] { "Categories", "Products", "ShippingOptions", "Discounts", "ContentPages" })
        {
            routes.AddRange(new[] { "/" + area, "/" + area + "/Create", "/" + area + "/Edit/1", "/" + area + "/Delete/1" });
            if (area is not ("Categories" or "Products")) routes.Add("/" + area + "/Details/1");
        }
        foreach (var width in new[] { 1365, 390, 320 })
        {
            await page.SetViewportSizeAsync(width, 900);
            foreach (var route in routes)
            {
                var response = await page.GotoAsync(route);
                Check(response?.Status == 200, "Screen failed: " + route);
                await Expect(page.Locator("main h1")).ToHaveCountAsync(1);
                Check(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth + 1"), $"Horizontal viewport overflow at {width}: {route}");
                var unlabeled = await page.EvaluateAsync<int>(@"() => [...document.querySelectorAll('input:not([type=hidden]),select,textarea')].filter(e => e.getBoundingClientRect().width > 0 && !(e.labels?.length || e.getAttribute('aria-label') || e.getAttribute('aria-labelledby'))).length");
                Check(unlabeled == 0, "Unlabeled form controls: " + route);
                Check(await page.EvaluateAsync<bool>("document.documentElement.lang === 'en' && !!document.querySelector('main')"), "Language/main landmark missing.");
                await page.ScreenshotAsync(new() { Path = Path.Combine(output, width + "-" + (route == "/" ? "Dashboard" : route.Trim('/').Replace('/', '-')) + ".png"), FullPage = true });
            }
            if (width < 1200)
            {
                await page.GotoAsync("/");
                await page.GetByRole(AriaRole.Button, new() { Name = "Toggle navigation" }).ClickAsync();
                await page.Locator("#admin-nav").GetByRole(AriaRole.Link, new() { Name = "Products", Exact = true }).ClickAsync();
                await Expect(page.Locator("main h1")).ToHaveTextAsync("Products");
            }
        }
        await page.SetViewportSizeAsync(1365, 900);
        await page.GotoAsync("/");
        await page.Keyboard.PressAsync("Tab");
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Skip to content" })).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator("main")).ToBeFocusedAsync();
        Console.WriteLine("BROWSER: all administration screens rendered at 1365, 390 and 320 pixels; screenshots saved; labels, landmarks and overflow checked.");
    }

    private static async Task Lists(IPage page)
    {
        await page.GotoAsync("/Products");
        await page.GetByLabel("Search", new() { Exact = true }).FillAsync("Browser item");
        await page.GetByLabel("Sort by").SelectOptionAsync("name-desc");
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply filters" }).ClickAsync();
        await Expect(page.Locator("tbody tr").First).ToContainTextAsync("Browser item 32");
        await page.GetByRole(AriaRole.Link, new() { Name = "Next", Exact = true }).ClickAsync();
        await Expect(page.Locator("tbody tr").First).ToContainTextAsync("Browser item 22");
        Check(page.Url.Contains("Sort=name-desc") && page.Url.Contains("Search=Browser"), "Paging must preserve search and sort.");
        await page.GetByLabel("Availability").SelectOptionAsync("false");
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply filters" }).ClickAsync();
        await Expect(page.Locator("tbody tr").First).ToContainTextAsync("Browser item 31");
        await page.GotoAsync("/Categories?Search=Browser&HasProducts=false&Sort=name&Page=2");
        await Expect(page.Locator("tbody tr").First).ToContainTextAsync("Browser category 11");
        await page.GotoAsync("/Orders?Status=Pending&Search=customer");
        await Expect(page.Locator("tbody")).ToContainTextAsync("customer");
        await page.GotoAsync("/Products?Search=no-such-product");
        await Expect(page.GetByText("No products found.", new() { Exact = false })).ToBeVisibleAsync();
        await page.GotoAsync("/Orders?From=2030-02-02&To=2030-01-01");
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("End date must");
    }

    private static async Task Crud(IPage page, HttpClient api)
    {
        await page.GotoAsync("/Categories/Create");
        await page.GetByLabel("Name", new() { Exact = true }).FillAsync("Workflow category");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add Category", Exact = true }).ClickAsync();
        var categories = await api.GetFromJsonAsync<JsonElement[]>("/api/categories");
        var categoryId = categories!.Single(x => x.GetProperty("name").GetString() == "Workflow category").GetProperty("id").GetInt32();
        await page.GotoAsync($"/Categories/Edit/{categoryId}");
        await page.GetByLabel("Name", new() { Exact = true }).FillAsync("Workflow category edited");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save Changes", Exact = true }).ClickAsync();

        await page.GotoAsync("/Products/Create");
        await page.GetByLabel("Name", new() { Exact = true }).FillAsync("Workflow product");
        await page.GetByLabel("Price", new() { Exact = true }).FillAsync("12.50");
        await page.GetByLabel("Stock quantity").FillAsync("7");
        await page.GetByLabel("Category", new() { Exact = true }).SelectOptionAsync(categoryId.ToString());
        await page.GetByLabel("Product Image (optional)").SetInputFilesAsync(new FilePayload
        {
            Name = "pixel.png", MimeType = "image/png", Buffer = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aK1cAAAAASUVORK5CYII=")
        });
        await page.GetByRole(AriaRole.Button, new() { Name = "Add Product", Exact = true }).ClickAsync();
        var products = await api.GetFromJsonAsync<JsonElement[]>("/api/products");
        var product = products!.Single(x => x.GetProperty("name").GetString() == "Workflow product");
        var productId = product.GetProperty("id").GetInt32();
        Check(product.GetProperty("imageUrl").GetString()!.StartsWith("/images/products/"), "Browser multipart image upload failed.");
        await page.GotoAsync($"/Products/Edit/{productId}");
        await page.GetByLabel("Stock quantity").FillAsync("9");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save Changes", Exact = true }).ClickAsync();
        Check((await api.GetFromJsonAsync<JsonElement>($"/api/products/{productId}")).GetProperty("stockQuantity").GetInt32() == 9, "Product edit did not persist.");

        foreach (var area in new[] { "ShippingOptions", "Discounts", "ContentPages" })
        {
            await page.GotoAsync($"/{area}/Create");
            await page.GetByLabel(area == "ContentPages" ? "Title" : "Name", new() { Exact = true }).FillAsync("Workflow " + area);
            if (area == "ShippingOptions")
            {
                await page.GetByLabel("Area", new() { Exact = true }).FillAsync("North");
                await page.GetByLabel("Price", new() { Exact = true }).FillAsync("3.50");
            }
            if (area == "Discounts")
            {
                await page.GetByLabel("Find product").FillAsync("Workflow product");
                await page.GetByRole(AriaRole.Button, new() { Name = "Search products", Exact = true }).ClickAsync();
                await Expect(page.Locator("#ProductMatches option")).ToHaveCountAsync(2);
                await page.GetByLabel("Product search results").SelectOptionAsync(productId.ToString());
                await Expect(page.GetByLabel("Product ID", new() { Exact = true })).ToHaveValueAsync(productId.ToString());
                await page.GetByLabel("Percentage", new() { Exact = true }).FillAsync("10");
                await page.GetByLabel("Start date (UTC)").FillAsync("2030-01-01T10:00");
                await page.GetByLabel("End date (UTC)").FillAsync("2030-02-01T10:00");
            }
            if (area == "ContentPages")
            {
                await page.GetByLabel("Slug", new() { Exact = true }).FillAsync("Workflow Page");
                await page.GetByLabel("Content", new() { Exact = true }).FillAsync("<script>window.storedXss=true</script>Browser body");
            }
            await page.Locator("main form button[type=submit]").ClickAsync();
            await Expect(page.Locator("main .alert-success")).ToBeVisibleAsync();
            var records = await api.GetFromJsonAsync<JsonElement[]>("/api/" + area);
            var id = records!.Single(x => x.GetProperty(area == "ContentPages" ? "title" : "name").GetString() == "Workflow " + area).GetProperty("id").GetInt32();
            await page.GotoAsync($"/{area}/Details/{id}");
            if (area == "ContentPages") Check(await page.EvaluateAsync<bool>("window.storedXss !== true"), "Stored markup executed.");
            await page.GotoAsync($"/{area}/Edit/{id}");
            await page.GetByLabel(area == "ContentPages" ? "Title" : "Name", new() { Exact = true }).FillAsync("Workflow edited " + area);
            await page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
            await Expect(page.Locator("main .alert-success")).ToBeVisibleAsync();
            await page.GotoAsync($"/{area}/Delete/{id}");
            await page.Locator("main form button[type=submit]").ClickAsync();
            await Expect(page.Locator("main .alert-success")).ToBeVisibleAsync();
            Check(!(await api.GetFromJsonAsync<JsonElement[]>("/api/" + area))!.Any(x => x.GetProperty("id").GetInt32() == id), "Delete did not persist: " + area);
        }
        await page.GotoAsync($"/Products/Delete/{productId}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();
        await page.GotoAsync($"/Categories/Delete/{categoryId}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();

        await page.GotoAsync("/Orders/Details/1");
        await page.GetByLabel("New status").SelectOptionAsync("Confirmed");
        await page.GetByRole(AriaRole.Button, new() { Name = "Update", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Order status updated.", new() { Exact = true })).ToBeVisibleAsync();
        page.Dialog += (_, dialog) => dialog.AcceptAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Cancel Order", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Order cancelled and stock restored.", new() { Exact = true })).ToBeVisibleAsync();
        Check((await api.GetFromJsonAsync<JsonElement>("/api/products/1")).GetProperty("stockQuantity").GetInt32() == 10, "Browser cancellation failed to restore stock.");
    }

    private static async Task SubmissionAndSecurity(IPage page, HttpClient api)
    {
        await page.GotoAsync("/Categories/Create");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add Category", Exact = true }).ClickAsync();
        await Expect(page.Locator(".field-validation-error")).ToBeVisibleAsync();
        await Expect(page.Locator("#request-progress")).ToBeHiddenAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Add Category", Exact = true })).ToBeEnabledAsync();
        await page.GetByLabel("Name", new() { Exact = true }).FillAsync("Submission guard");
        var requests = 0;
        page.Request += (_, request) =>
        {
            if (request.Method == "POST" && new Uri(request.Url).AbsolutePath == "/Categories/Create")
                Interlocked.Increment(ref requests);
        };
        // Hold only the browser's default navigation after the application's normal
        // submit handlers. A paused network navigation prevents Chromium from inspecting
        // the outgoing document. No validation, DOM state or API response is mocked here.
        await page.EvaluateAsync(@"() => {
            window.holdTestSubmission = event => {
                if (event.target === document.querySelector('main form')) event.preventDefault();
            };
            document.addEventListener('submit', window.holdTestSubmission);
        }");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add Category", Exact = true }).ClickAsync();
        await Expect(page.Locator("#request-progress")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Add Category", Exact = true })).ToBeDisabledAsync();
        var duplicatePrevented = await page.EvaluateAsync<bool>(@"() => {
            document.removeEventListener('submit', window.holdTestSubmission);
            let prevented = false;
            document.addEventListener('submit', event => { prevented = event.defaultPrevented; }, { once: true });
            document.querySelector('main form').requestSubmit();
            return prevented;
        }");
        Check(duplicatePrevented && requests == 0, "The application must prevent a second submission while saving.");
        // Resume the first, already validated default submission against the real MVC/API.
        var navigation = page.WaitForURLAsync("**/Categories");
        await page.EvaluateAsync("HTMLFormElement.prototype.submit.call(document.querySelector('main form'))");
        await navigation;
        Check(requests == 1, "Duplicate submission reached the server.");
        Check((await api.GetFromJsonAsync<JsonElement[]>("/api/categories"))!.Count(x => x.GetProperty("name").GetString() == "Submission guard") == 1, "Duplicate record created.");
        await page.GoBackAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Add Category", Exact = true })).ToBeEnabledAsync();
        var csrfStatus = await page.EvaluateAsync<int>(@"async () => (await fetch('/ShippingOptions/Create', { method:'POST', headers:{'Content-Type':'application/x-www-form-urlencoded'}, body:'Name=CSRF&Area=North&Price=1' })).status");
        Check(csrfStatus == 400, "A browser POST without an antiforgery token must fail.");
        Check(!(await api.GetFromJsonAsync<JsonElement[]>("/api/shippingoptions"))!.Any(x => x.GetProperty("name").GetString() == "CSRF"), "CSRF request wrote data.");
    }

    private static async Task WithoutJavaScript(IBrowser browser, BrowserHost host, HttpClient api)
    {
        await using var context = await browser.NewContextAsync(new()
        {
            BaseURL = host.Url, IgnoreHTTPSErrors = true, JavaScriptEnabled = false,
            ViewportSize = new() { Width = 390, Height = 900 }
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/Account/Login");
        await page.GetByLabel("Username", new() { Exact = true }).FillAsync("admin");
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(host.Api.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Login", Exact = true }).ClickAsync();
        await Expect(page.Locator("main h1")).ToHaveTextAsync("Dashboard");
        await page.Locator("#admin-nav").GetByRole(AriaRole.Link, new() { Name = "Categories", Exact = true }).ClickAsync();
        await page.GotoAsync("/Categories/Create");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add Category", Exact = true }).ClickAsync();
        await Expect(page.Locator(".field-validation-error")).ToBeVisibleAsync();
        await page.GetByLabel("Name", new() { Exact = true }).FillAsync("No script category");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add Category", Exact = true }).ClickAsync();
        Check((await api.GetFromJsonAsync<JsonElement[]>("/api/categories"))!.Any(x => x.GetProperty("name").GetString() == "No script category"), "Server validation and submission must work without JavaScript.");
        await page.GotoAsync("/Discounts/Create");
        await Expect(page.Locator(".product-search")).ToBeHiddenAsync();
        await page.GetByLabel("Name", new() { Exact = true }).FillAsync("No script discount");
        await page.GetByLabel("Product ID", new() { Exact = true }).FillAsync("1");
        await page.GetByLabel("Percentage", new() { Exact = true }).FillAsync("10");
        await page.GetByLabel("Start date (UTC)").FillAsync("2030-01-01T10:00");
        await page.GetByLabel("End date (UTC)").FillAsync("2030-02-01T10:00");
        await page.Locator("main form button[type=submit]").ClickAsync();
        await Expect(page.Locator("main .alert-success")).ToBeVisibleAsync();
        Check((await api.GetFromJsonAsync<JsonElement[]>("/api/discounts"))!.Any(x => x.GetProperty("name").GetString() == "No script discount" && x.GetProperty("productId").GetInt32() == 1), "Numeric product selection must work without JavaScript.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign out admin" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Account/Login"));
    }

    private static async Task Picker(IPage page)
    {
        await page.GotoAsync("/Discounts/Create");
        await page.GetByLabel("Find product").FillAsync("Browser item");
        await page.GetByLabel("Find product").PressAsync("Enter");
        await Expect(page.Locator(".product-search-status")).ToHaveTextAsync("32 matches. Page 1 of 2.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Next matches" }).ClickAsync();
        await Expect(page.Locator(".product-search-status")).ToHaveTextAsync("32 matches. Page 2 of 2.");
        await Expect(page.Locator("#ProductMatches option")).ToHaveCountAsync(13);
        await page.GetByLabel("Find product").FillAsync("does not exist");
        await page.GetByRole(AriaRole.Button, new() { Name = "Search products", Exact = true }).ClickAsync();
        await Expect(page.Locator(".product-search-status")).ToContainTextAsync("No products found.");
        await page.RouteAsync("**/Products/Lookup?**", route => route.AbortAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Search products", Exact = true }).ClickAsync();
        await Expect(page.Locator(".product-search-status")).ToContainTextAsync("Could not search products.");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Search products", Exact = true })).ToBeEnabledAsync();
        await page.UnrouteAllAsync();
    }
}
