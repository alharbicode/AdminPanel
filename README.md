# AdminPanel

ASP.NET Core **.NET 8** Web API and MVC administration client, using **EF Core 9** and **SQL Server**. The MVC app calls the API through an HTTP client; it has no database connection.

## Projects and implemented features

- `AdminPanel/`: API, JWT authentication, EF models/migrations, category and product CRUD, product image uploads, order creation/list/details/status/cancellation, shipping options, discounts and content page CRUD.
- `AdminPanel.MVC/`: administrator login/logout, responsive navigation, category/product CRUD, order list/details/status/cancellation, full shipping/discount/content management and a data-backed dashboard.
- `AdminPanel.Tests/`: xUnit integration tests. Both ASP.NET request pipelines run in-process with disposable relational databases. SQLite is the default; CI also runs the full suite against SQL Server 2022 with all migrations. Tests never use the application connection string.
- `AdminPanel.BrowserTests/`: official Playwright Chromium/Firefox workflow runner; disposable fixtures, responsive-layout and basic accessibility checks, screenshots and result artifacts.
- `docs/AUDIT.md`: findings, prioritized acceptance criteria, assumptions and verification record.

Administration is **Admin only**. Staff and Customer credentials do not authorize administration. There is no public registration, customer checkout UI, or customer-facing API policy.

For a complete Windows walkthrough, troubleshooting and local browser-test commands, see [docs/WINDOWS-SETUP.md](docs/WINDOWS-SETUP.md).

## Prerequisites

1. .NET 8 SDK (8.0.400 or later in the 8.0 feature bands; `global.json` selects it).
2. SQL Server or SQL Server Express LocalDB, and permission to create a **development** database. LocalDB is Windows-only; Linux/macOS developers need a reachable SQL Server instance/container.
3. A trusted ASP.NET development HTTPS certificate.

From the repository root:

```powershell
dotnet restore AdminPanel.sln
dotnet tool restore
dotnet dev-certs https --trust
dotnet build AdminPanel.sln
dotnet test AdminPanel.sln
```

The local tool manifest pins `dotnet-ef` to the EF Core package version. EF Core 9 targets .NET 8; these versions are intentional. Plan a supported-runtime upgrade before the .NET 8 / EF 9 support window ends; this repair does not change the framework.

## Configure the API

The API deliberately has no checked-in signing key or database connection. It fails startup with a configuration message if required values are missing. Use user secrets for local development (they are stored outside the repository):

```powershell
$jwtSecret = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
dotnet user-secrets set "JwtSettings:SecretKey" $jwtSecret --project AdminPanel
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\MSSQLLocalDB;Database=AdminPanelDev;Trusted_Connection=True;TrustServerCertificate=True" --project AdminPanel
Remove-Variable jwtSecret
```

For another SQL Server, supply its connection string privately. The LocalDB example uses Windows authentication and trusts a local development certificate; configure proper certificate validation for shared or production SQL Server.

Other settings already have defaults in `AdminPanel/appsettings.json`:

| Setting | Default |
| --- | --- |
| `JwtSettings:Issuer` | `AdminPanel.API` |
| `JwtSettings:Audience` | `AdminPanel.MVC` |
| `JwtSettings:LifetimeMinutes` | `60` (allowed 1–1440) |

Environment variable equivalents are `JwtSettings__SecretKey` and `ConnectionStrings__DefaultConnection`. Use a deployment secret store outside local development. Never commit credentials. Replace the signing key from older repository revisions; removing it from the current file does not remove it from Git history.

## Create or upgrade a development database

Inspect the migration script before applying it:

```powershell
dotnet ef migrations script --idempotent --project AdminPanel --output migrations.sql -- --environment Development
dotnet ef database update --project AdminPanel -- --environment Development
```

The new `HardenDataIntegrity` migration adds unique usernames/slugs and prevents cascade deletion of catalog/order references. It stops with an explicit error if existing usernames/slugs are duplicates or too long. Resolve those records deliberately before retrying. It does not delete or merge business records. Existing migrations are preserved, including the historically empty `AddShippingOptions` migration (that table was already added by `AddOrderEntities`).

**No startup migration runs automatically.** Applying migrations to production requires separate review and approval.

## Provision the first administrator

There are no default accounts or passwords. After migrating your development database, run this explicit one-time command:

```powershell
$env:BootstrapAdmin__Username = Read-Host "Administrator username"
$env:BootstrapAdmin__Email = Read-Host "Administrator email"
$securePassword = Read-Host "Administrator password (at least 12 characters)" -AsSecureString
$env:BootstrapAdmin__Password = [Net.NetworkCredential]::new("", $securePassword).Password
try {
    dotnet run --project AdminPanel --launch-profile https -- --bootstrap-admin
} finally {
    Remove-Item Env:BootstrapAdmin__Password
    Remove-Variable securePassword
}
```

The command writes an ASP.NET Identity password hash and exits without starting a server. It refuses to overwrite an existing username. For an existing account, supply the same settings and use `--reset-password` instead; this changes only its password, preserving role and blocked state. Existing plaintext passwords are **not accepted** by login and require this reset. Provisioning/reset is an operator action, not an HTTP endpoint.

## Run both applications

Open two terminals in the repository root:

```powershell
# Terminal 1
dotnet run --project AdminPanel --launch-profile https

# Terminal 2
dotnet run --project AdminPanel.MVC --launch-profile https
```

- API: [https://localhost:7056](https://localhost:7056)
- Development Swagger: [https://localhost:7056/swagger](https://localhost:7056/swagger)
- MVC login: [https://localhost:7189/Account/Login](https://localhost:7189/Account/Login)

MVC `ApiSettings:BaseUrl` defaults to `https://localhost:7056/api/` and must end with `/`. Set `ApiSettings__BaseUrl` if the API URL changes. Keep HTTPS: MVC authentication cookies are Secure and HttpOnly. Do not disable TLS certificate validation. The MVC's server-side API calls do not require a browser CORS policy.

Login returns a short-lived JWT to the MVC server. MVC stores it inside its encrypted authentication ticket, forwards it as a Bearer token, and expires the cookie at the same time as the API token. Remember Me persists that cookie only until token expiration; there is no refresh-token workflow. Blocked/deleted accounts or changed roles are rejected on API requests. A 401 signs the MVC session out; API/network failures produce a safe retry message.

## API contracts and data behavior

- All administration routes require a Bearer token for an Admin user. Only `POST /api/auth/login` is anonymous; it is rate-limited.
- Products: `POST /api/products` and `PUT /api/products/{id}` accept **multipart/form-data**, with Name, Description, Price, StockQuantity, Unit, IsAvailable, CategoryId, and optional ImageFile. Client-supplied ImageUrl is ignored. JPEG/PNG/WebP uploads require matching signatures and are limited to 5 MiB (6 MiB total request).
- Orders: `POST /api/orders` accepts CustomerId, optional ShippingOptionId, and 1–100 Items with positive ProductId/Quantity. Duplicate product lines must be combined. Products must exist, be available and have enough stock.
- `GET /api/orders/{id}` includes customer name/email and line items with the captured purchase price. Totals currently sum product prices only; shipping and discounts are **not applied**.
- `PUT /api/orders/{id}/status` accepts a JSON **string**, e.g. `"Confirmed"`. Supported values follow the existing UI: Pending, Confirmed, Shipped, Delivered. Arbitrary transition restrictions require product decisions.
- `DELETE /api/orders/{id}` preserves the original cancellation contract: restore stock and remove the order transactionally. A repeated cancellation returns 404 and does not restore stock again. Selecting Cancelled as a status is rejected; use cancellation.
- Deleting categories with products, products referenced by orders/discounts, or shipping options used by orders returns 409. Concurrent stock writes return 409 rather than silently overwriting inventory.
- Content slugs normalize to lowercase hyphenated values and are unique. Duplicate slugs/usernames are also blocked by database constraints.
- Unknown IDs return 404; invalid input returns 400; authorization uses 401/403. Unexpected errors do not expose database details.

## Administration UI (Phase 2)

Use the Shipping, Discounts and Content pages navigation links after signing in as an Admin. Each area supports list, details, create, edit and a separate delete confirmation. Validation retains entered fields; conflicts explain what must change. POST forms use antiforgery tokens. Submission progress and duplicate-submit prevention are progressive enhancements.

- Shipping: search name/area and filter by area. Referenced shipping options cannot be deleted.
- Discounts: choose an existing product, enter a percentage with at most two decimal places and start/end dates in **UTC**. Filter by product or the stored date window (Upcoming, Includes now, Ended). These labels do not imply checkout eligibility or application.
- Content pages: search title/slug; edit the body as text. The details preview encodes markup instead of executing stored HTML. Slugs follow the existing lowercase/space-to-hyphen normalization and uniqueness constraint.
- All three lists page in the database with a stable ID ordering, ten records by default, and a maximum of 100 per page. Pagination retains search/filter values and clamps pages beyond the last result. Search case sensitivity follows the database collation.
- Dashboard: catalog/administration counts, available/unavailable/zero-stock product counts, existing order statuses and five recent orders. Amounts are stored order totals; no currency, revenue recognition or checkout calculation is inferred. Counts refresh on page load and are not a transactional reporting snapshot.

Existing mutation and collection API contracts remain unchanged. Additive reads: `GET /api/{shippingoptions|discounts|contentpages}/admin/list` accepts `Page`, `PageSize`, `Search`, with `Area` for shipping and `ProductId`/`Period` (`upcoming`, `current`, `ended`) for discounts. Responses contain `items`, `totalCount`, `page`, `pageSize`. Content list bodies are omitted. Individual reads are `/api/shippingoptions/{id}`, `/api/discounts/{id}`, `/api/contentpages/by-id/{id}`; the original content slug endpoint remains available. `/api/dashboard` returns dashboard statistics. All require Admin authentication.

## Catalog and order lists (Phase 3)

Categories search names/descriptions and filter categories with or without products. Products search names/descriptions and filter category, availability and stock. Orders search by exact numeric ID, customer username or email, and filter status and inclusive UTC date range. Each list pages in the database (default 10, maximum 100), preserves its controls in paging links and uses an ID tie-breaker for stable ordering. Sort choices are ID/name for catalog lists, stock quantity for products, and newest/oldest date for orders. Existing unpaged API collection routes remain compatible; MVC uses `GET /api/{categories|products|orders}/admin/list`.

Discount create/edit/filter screens search products by name or description through `GET /Products/Lookup`, which forwards the administrator's token to `GET /api/products/admin/lookup`. Lookup responses contain at most 20 ID/name pairs per page. The native JavaScript control supports Enter, result selection, previous/next matches, loading, empty and retry states without a new frontend dependency. A known product ID can be entered directly, including without JavaScript. Selected records are validated individually on save.

## Verification and remaining scope

CI runs restore, Release build, all integration tests on SQLite and SQL Server 2022, EF model/migration checks, SQL script generation and a dependency vulnerability audit. Phase 3 passed **57 integration tests per provider**, **329 explicit browser checks plus Playwright assertions per browser/provider** in Chromium and Firefox, and the standalone Windows LocalDB/HTTPS smoke check. See [docs/AUDIT.md](docs/AUDIT.md) for run links, screen coverage and results.

Browser checks include 26 administration routes at desktop and phone widths, CRUD/uploads/order cancellation, filters and product search, validation, loading/duplicate prevention, CSRF, keyboard navigation and JavaScript-disabled forms. Screenshots/result artifacts are retained by CI. These are selected accessibility and layout checks, not a full WCAG audit; WebKit and physical devices remain unverified.

The integration suite covers authentication, token validation, Admin-only authorization, CSRF protection, MVC-to-API category/product/order/shipping/discount/content flows, dashboard counts, search/filter/pagination, encoded content, uploads, input validation, reference constraints, and stock/cancellation concurrency.

Outstanding product decisions: Staff permissions, customer onboarding/checkout, discount/shipping/tax calculation, currency, fulfillment transition rules, and retained cancellation history. All six administration lists use server-side pagination. Discount product selection uses bounded search; category selectors still load all categories. Old/replaced image files are deliberately retained to avoid deleting shared legacy assets; an explicit orphan cleanup policy remains future work. Images have size/type/signature checks, not malware scanning or image decoding. Multi-instance hosting requires shared MVC Data Protection keys and distributed rate limiting.

For optional local SQL Server integration runs, `ADMINPANEL_TEST_SQLSERVER` must point to a disposable server at `localhost,1433` or `127.0.0.1,1433`, with permission to create/drop databases. The fixture ignores the supplied database name, generates a unique `AdminPanel_Test_` database per test, applies migrations and deletes that database afterwards. CI generates ephemeral credentials and stops its container after verification. Keep this variable unset for normal SQLite tests.

Review the generated migration script and existing data before using a real environment. Neither a production migration nor deployment is part of this change.
