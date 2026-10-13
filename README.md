# AdminPanel

## Checkout and order management (Storefront Phase 3)

The storefront now supports guest and customer checkout, explicitly configured Yemen delivery
zones, automatic eligible product discounts and Cash on Delivery in YER. Orders retain immutable
pricing/address snapshots, durable submission keys and protected customer/guest access. Administration
adds zone management, validated fulfillment states and cancellation that retains history and restores
stock once. Existing premium layouts and Arabic/English interfaces are preserved.

Read the [Phase 3 setup, pricing, lifecycle and migration guide](docs/STOREFRONT-PHASE-3.md).
Branch `codex/storefront-phase-three` is based on `codex/storefront-phase-two`. Review the
[upgrade SQL](docs/migrations/phase3-upgrade.sql) and back up the intended database before any
owner-approved migration. The owner's local database, including `AdminPanel_Dev`, has not been migrated.
Final Phase 3 browser/CI results are pending; see the [verification record](docs/AUDIT.md#storefront-phase-3).
No online payment gateway, tax charge, order email/SMS notification, deployment or merge is included.

## Customer accounts and carts (Storefront Phase 2)

The storefront now supports customer registration/login/logout, recovery, profile/password management,
guest carts, persistent customer carts and safe merging in Arabic and English.
See [the Phase 2 setup, migration and email guide](docs/STOREFRONT-PHASE-2.md).
Review and apply the additive migration before running these features on an existing development
database. It has not been applied to the owner's local database. Phase 2's accounts/cart scope is
preserved; Phase 3 adds the checkout and order workflows described above.

The Phase 2 implementation passed SQLite/SQL Server integration tests, Chromium/Firefox admin,
catalog and customer workflows, and Windows LocalDB setup in [CI](https://github.com/alharbicode/AdminPanel/actions/runs/38464970518).
The Phase 2 suite reached 91 passing tests including email-safety checks; that phase's results are
available in [draft PR #4](https://github.com/alharbicode/AdminPanel/pull/4).

## Customer storefront (Phase 1)

The solution also contains `AdminPanel.Storefront`, an Arabic-first/English MVC catalog at
`https://localhost:7289`, using dedicated public API reads and the existing database.
Products require explicit API publication configuration; the default public catalog is empty.
See [docs/STOREFRONT.md](docs/STOREFRONT.md) for Windows/Visual Studio setup, publication,
branding, YER display, language switching, verification and the future COD roadmap.
The premium administration dashboard is preserved. Phase 2 added accounts and carts; Phase 3 adds checkout.


ASP.NET Core **.NET 8** Web API, MVC administration client and MVC public storefront, using **EF Core 9** and **SQL Server**. Both MVC applications call the API through HTTP clients; neither connects to the database.

## Projects and implemented features

- `AdminPanel/`: API, separate administrator/customer authentication, EF models/migrations, catalog/image management, customer accounts/carts, authoritative checkout, immutable orders, safe fulfillment/cancellation, shipping zones/options, discounts and content.
- `AdminPanel.MVC/`: Admin-only premium dashboard and catalog/content management, guest/customer order details, fulfillment/cancellation and shipping-zone/rate management.
- `AdminPanel.Storefront/`: Arabic/English catalog, accounts/recovery/profile, guest/saved carts, guest/customer checkout, YER/COD review, order history and secure guest tracking. Public products require explicit publication configuration.
- `AdminPanel.Tests/`: xUnit integration tests. The API and both MVC request pipelines run in-process with disposable relational databases. SQLite is the default; CI also runs the full suite against SQL Server 2022 with all migrations. Tests never use the application connection string.
- `AdminPanel.BrowserTests/`: official Playwright Chromium/Firefox workflow runner; disposable fixtures, responsive-layout and basic accessibility checks, screenshots and result artifacts.
- `docs/AUDIT.md`: findings, prioritized acceptance criteria, assumptions and verification record.

Administration is **Admin only**. Customer and administrator authentication use separate audiences and cookies. Public catalog reads, accounts, carts and checkout have narrowly scoped APIs. Customer order access comes from authenticated ownership; guest order access requires a private tracking secret.

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

Generating a migration script does not apply it. Review the migration chain and the
[Phase 3 upgrade/rollback restrictions](docs/STOREFRONT-PHASE-3.md#migration-review-and-rollback),
confirm the intended database and take a tested backup. The update command below is an **owner action
after approval**, never an automatic setup step against an existing database:

```powershell
dotnet ef migrations script --idempotent --project AdminPanel --output migrations.sql -- --environment Development
# Owner only, after target/backup/script review and approval:
dotnet ef database update --project AdminPanel -- --environment Development
```

The new `HardenDataIntegrity` migration adds unique usernames/slugs and prevents cascade deletion of catalog/order references. It stops with an explicit error if existing usernames/slugs are duplicates or too long. Resolve those records deliberately before retrying. It does not delete or merge business records. Existing migrations are preserved, including the historically empty `AddShippingOptions` migration (that table was already added by `AddOrderEntities`).

Storefront Phase 2 adds customer/cart tables. Phase 3 adds shipping zones, nullable guest ownership and
order/item snapshots, idempotency and cancellation metadata. No existing history is deleted or repriced.
The Phase 3 Down migration refuses automatic downgrade to prevent loss of guest/order history.
**No startup migration runs automatically.** Applying migrations to any existing database requires
owner review; production deployment remains a separate decision.

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

## Run the three applications

Open three terminals in the repository root:

```powershell
# Terminal 1
dotnet run --project AdminPanel --launch-profile https

# Terminal 2
dotnet run --project AdminPanel.MVC --launch-profile https

# Terminal 3
dotnet run --project AdminPanel.Storefront --launch-profile https
```

- API: [https://localhost:7056](https://localhost:7056)
- Development Swagger: [https://localhost:7056/swagger](https://localhost:7056/swagger)
- MVC login: [https://localhost:7189/Account/Login](https://localhost:7189/Account/Login)
- Public storefront: [https://localhost:7289](https://localhost:7289)

The storefront is empty until approved product IDs are configured on the API. Follow the
[publication instructions](docs/STOREFRONT.md#windows--visual-studio-setup); confirm the stored prices
are YER before publishing. The storefront's API base URL is `https://localhost:7056/api/catalog/`.

MVC `ApiSettings:BaseUrl` defaults to `https://localhost:7056/api/` and must end with `/`. Set `ApiSettings__BaseUrl` if the API URL changes. Keep HTTPS: MVC authentication cookies are Secure and HttpOnly. Do not disable TLS certificate validation. The MVC's server-side API calls do not require a browser CORS policy.

Login returns a short-lived JWT to the MVC server. MVC stores it inside its encrypted authentication ticket, forwards it as a Bearer token, and expires the cookie at the same time as the API token. Remember Me persists that cookie only until token expiration; there is no refresh-token workflow. Blocked/deleted accounts or changed roles are rejected on API requests. A 401 signs the MVC session out; API/network failures produce a safe retry message.

## API contracts and data behavior

- All administration routes require a Bearer token for an Admin user. `POST /api/auth/login` is anonymous and rate-limited. The separate `/api/catalog` GET endpoints are anonymous with a restricted public projection and publication policy; see [their contract](docs/STOREFRONT.md#additive-api-contract).
- Products: `POST /api/products` and `PUT /api/products/{id}` accept **multipart/form-data**, with Name, Description, Price, StockQuantity, Unit, IsAvailable, CategoryId, and optional ImageFile. PUT also requires the previously displayed `ExpectedStockQuantity` to reject stale stock edits. Client-supplied ImageUrl is ignored. JPEG/PNG/WebP uploads require matching signatures and are limited to 5 MiB (6 MiB total request).
- Manual administration orders: `POST /api/orders` accepts a required nonempty GUID `RequestKey`, CustomerId, optional ShippingOptionId, and 1–100 Items with positive ProductId/Quantity. Duplicate lines must be combined. Products must be available and have sufficient stock. Repeating the same key/payload returns the original order; reusing a key for different data conflicts. These manual orders preserve their product-price-only calculation.
- Storefront checkout uses `/api/{guest|customer}/checkout/{quote|confirm}`. The API applies eligible discounts and the selected enabled Yemen zone fee; all monetary values, YER/COD and snapshots are authoritative. Confirmation deducts stock and finalizes the cart atomically. See the [checkout contract](docs/STOREFRONT-PHASE-3.md#api-additions).
- `GET /api/orders/{id}` is Admin-only and includes retained customer/guest contacts, delivery and item/total snapshots where present. Legacy orders keep their existing values and are labeled separately.
- Fulfillment follows `Pending -> Confirmed -> Preparing -> Shipped -> Delivered`. Current admin forms use versioned transition/cancellation routes. The older `PUT /api/orders/{id}/status` JSON-string route enforces the same forward state machine.
- Cancellation **retains history**. Customers/guests can cancel their own Pending/Confirmed orders; admins can also cancel Preparing orders. Shipped/Delivered cancellation is prohibited. `DELETE /api/orders/{id}` remains a compatibility alias for retained cancellation; repeat calls cannot restore stock twice. Later customer cancellation requests record a reason without changing status/stock.
- Deleting categories with products, products referenced by orders/discounts, or shipping options used by orders returns 409. Concurrent stock writes return 409 rather than silently overwriting inventory.
- Content slugs normalize to lowercase hyphenated values and are unique. Duplicate slugs/usernames are also blocked by database constraints.
- Unknown IDs return 404; invalid input returns 400; authorization uses 401/403. Unexpected errors do not expose database details.

## Administration UI (Administration Phase 2)

Use the Shipping, Discounts and Content pages navigation links after signing in as an Admin. Each area supports list, details, create, edit and a separate delete confirmation. Validation retains entered fields; conflicts explain what must change. POST forms use antiforgery tokens. Submission progress and duplicate-submit prevention are progressive enhancements.

- Shipping: search name/area and filter by area. Referenced shipping options cannot be deleted.
- Discounts: choose an existing product, enter a percentage with at most two decimal places and start/end dates in **UTC**. Filter by product or the stored date window (Upcoming, Includes now, Ended). Storefront checkout independently validates eligibility and applies one highest eligible percentage (lowest ID for ties), with inclusive start/exclusive end and two-decimal unit rounding.
- Shipping zones: configure enabled Yemen governorate/city rates separately from legacy shipping options. Empty city means governorate-wide coverage. Customers explicitly select a zone; there is no automatic city-rate precedence or exclusion. Existing order snapshots survive later rate edits/disablement.
- Content pages: search title/slug; edit the body as text. The details preview encodes markup instead of executing stored HTML. Slugs follow the existing lowercase/space-to-hyphen normalization and uniqueness constraint.
- All three lists page in the database with a stable ID ordering, ten records by default, and a maximum of 100 per page. Pagination retains search/filter values and clamps pages beyond the last result. Search case sensitivity follows the database collation.
- Dashboard: catalog/administration counts, available/unavailable/zero-stock product counts, existing order statuses and five recent orders. Amounts are stored order totals; no currency, revenue recognition or checkout calculation is inferred. Counts refresh on page load and are not a transactional reporting snapshot.

Administration Phase 2 added reads: `GET /api/{shippingoptions|discounts|contentpages}/admin/list` accepts `Page`, `PageSize`, `Search`, with `Area` for shipping and `ProductId`/`Period` (`upcoming`, `current`, `ended`) for discounts. Responses contain `items`, `totalCount`, `page`, `pageSize`. Content list bodies are omitted. Individual reads are `/api/shippingoptions/{id}`, `/api/discounts/{id}`, `/api/contentpages/by-id/{id}`; the original content slug endpoint remains available. `/api/dashboard` returns dashboard statistics. All require Admin authentication. Storefront Phase 3 adds the order/product safety preconditions described above; external mutation clients must send them.

## Catalog and order lists (Administration Phase 3)

Categories search names/descriptions and filter categories with or without products. Products search names/descriptions and filter category, availability and stock. Orders search by exact numeric ID, customer username or email, and filter status and inclusive UTC date range. Each list pages in the database (default 10, maximum 100), preserves its controls in paging links and uses an ID tie-breaker for stable ordering. Sort choices are ID/name for catalog lists, stock quantity for products, and newest/oldest date for orders. Existing unpaged API collection routes remain compatible; MVC uses `GET /api/{categories|products|orders}/admin/list`.

Discount create/edit/filter screens search products by name or description through `GET /Products/Lookup`, which forwards the administrator's token to `GET /api/products/admin/lookup`. Lookup responses contain at most 20 ID/name pairs per page. The native JavaScript control supports Enter, result selection, previous/next matches, loading, empty and retry states without a new frontend dependency. A known product ID can be entered directly, including without JavaScript. Selected records are validated individually on save.

## Verification and remaining scope

CI runs restore, Release build, all integration tests on SQLite and SQL Server 2022, EF model/migration checks, SQL script generation and a dependency vulnerability audit. Phase 3 has **128 locally passing integration tests at the preliminary checkpoint**; final totals and browser/CI verification are pending. See [docs/AUDIT.md](docs/AUDIT.md#storefront-phase-3) for current results. Earlier phase records remain historical evidence, not a substitute for testing the Phase 3 branch.

Browser checks include 26 administration routes at desktop and phone widths, CRUD/uploads/order cancellation, filters and product search, validation, loading/duplicate prevention, CSRF, keyboard navigation and JavaScript-disabled forms. Screenshots/result artifacts are retained by CI. These are selected accessibility and layout checks, not a full WCAG audit; WebKit and physical devices remain unverified.

The integration suite covers authentication, token validation, Admin-only authorization, CSRF protection, MVC-to-API category/product/order/shipping/discount/content flows, dashboard counts, search/filter/pagination, encoded content, uploads, input validation, reference constraints, and stock/cancellation concurrency.

The approved single-vendor Yemen/YER account, cart, zone-based COD checkout and fulfillment/cancellation scope is now implemented. Remaining decisions include tax/invoices, COD collection/reconciliation/refusal, actual coverage/rates, shipped-order recall/returns/refunds, notifications and Staff permissions. No online gateway or international delivery is included. All administration lists use server-side pagination; admin category selectors still load all categories. Old/replaced image files remain pending a reviewed cleanup policy; uploads have size/type/signature checks, not malware scanning or image decoding. Production requires verified email, backups/monitoring, durable/shared Data Protection keys, deliberate client rate limiting, retention and a supported-runtime upgrade. See the [Phase 3 limitations](docs/STOREFRONT-PHASE-3.md#verification-record-and-remaining-work).

For optional local SQL Server integration runs, `ADMINPANEL_TEST_SQLSERVER` must point to a disposable server at `localhost,1433` or `127.0.0.1,1433`, with permission to create/drop databases. The fixture ignores the supplied database name, generates a unique `AdminPanel_Test_` database per test, applies migrations and deletes that database afterwards. CI generates ephemeral credentials and stops its container after verification. Keep this variable unset for normal SQLite tests.

Review the generated migration script and existing data before using a real environment. Neither a production migration nor deployment is part of this change.
