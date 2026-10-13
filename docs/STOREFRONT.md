# Storefront Phase 1

This is the historical Phase 1 report. For the current customer accounts and carts, including the
required migration and recovery email setup, see [STOREFRONT-PHASE-2.md](STOREFRONT-PHASE-2.md).

Branch: `codex/storefront-phase-one`, based on `codex/premium-dashboard-ui`.
Scope: public browsing only. The existing administration project is unchanged.
There are no Phase 1 database/model/migration changes, customer sessions, accounts,
carts, orders, checkout, COD or online payment implementations.

## Windows / Visual Studio setup

1. Install .NET 8 SDK 8.0.400 or newer in the 8.0 feature bands. For Visual Studio,
   use a compatible Visual Studio 2022 release (17.11+) with **ASP.NET and web development**.
   Follow [WINDOWS-SETUP.md](WINDOWS-SETUP.md) for SQL Server/LocalDB, private API secrets,
   existing migrations, HTTPS certificate trust and explicit administrator bootstrap.
2. Open `AdminPanel.sln` in Visual Studio, or use a PowerShell terminal at its root:

   ```powershell
   dotnet restore AdminPanel.sln
   dotnet build AdminPanel.sln --configuration Release
   dotnet dev-certs https --trust
   ```

3. Start API and admin, using two terminals:

   ```powershell
   dotnet run --project AdminPanel --launch-profile https
   dotnet run --project AdminPanel.MVC --launch-profile https
   ```

   API: https://localhost:7056; administration: https://localhost:7189.
   Create real categories and products through administration. Note their numeric IDs.
   An empty catalog is expected until products are explicitly published below.

4. Choose the individual products approved for public display. Configure their actual
   IDs **on the API**, for example only if IDs 12 and 34 are the approved records:

   ```powershell
   dotnet user-secrets set "PublicCatalog:ProductIds:0" "12" --project AdminPanel
   dotnet user-secrets set "PublicCatalog:ProductIds:1" "34" --project AdminPanel
   ```

   These commands are examples, not seed data. Restart the API after changing secrets.
   Environment equivalents are `PublicCatalog__ProductIds__0`, etc.
   Remove obsolete indexed settings when withdrawing approval; do not merely append a new list.
   A product also needs **Available checked, stock greater than zero, a valid category and
   a nonnegative price**. Product availability alone never publishes it.
   Confirm existing numeric prices represent **YER** before publishing; there is no conversion.

5. In a third terminal:

   ```powershell
   dotnet run --project AdminPanel.Storefront --launch-profile https
   ```

   Open **https://localhost:7289**. Browse, search, filter, open product details and use AR/EN.
   In Visual Studio set multiple startup projects: API, MVC, Storefront; use their HTTPS profiles.
   Use matching API configuration if ports change. Do not disable certificate validation.

## Configuration and ownership

- Storefront `ApiSettings:BaseUrl` defaults to `https://localhost:7056/api/catalog/`.
  It requires HTTPS and a trailing slash. Environment: `ApiSettings__BaseUrl`.
  It is separate from the admin application's `.../api/` setting.
- `Store:Name` is the centralized branding setting, default **Alharbi Store**.
  Configure with `dotnet user-secrets set "Store:Name" "Your store name" --project AdminPanel.Storefront`
  or `Store__Name`. Future administration settings can replace this configuration source.
- The storefront has no SQL connection, database reference, administrator credential or API token.
  It requests only public catalog reads.
- Currency is returned as an explicit `currency: "YER"` API field, separate from the numeric amount.
  UI shows two decimal places to preserve stored fractional prices, with localized YER labels.
  Multi-currency pricing/conversion and tax display decisions remain future work.
- Arabic is the default even if the browser prefers English. The visible AR/EN POST switch stores
  a Secure/HttpOnly preference cookie, uses antiforgery and preserves the current local path/query.
  Unsupported languages and external redirects are rejected/safely redirected.
- Resource files localize the interface. Original product/category names, descriptions and units
  remain unchanged and are HTML-encoded with automatic text direction.
- UI works without JavaScript. The small script only provides a broken-image fallback.
  CSS respects reduced motion; navigation, forms and cards have keyboard focus styling.
- No fake discounts, ratings, delivery guarantees or inventory numbers. Home highlights come
  from the newest visible product IDs. IDs are the existing insertion-order proxy; the schema
  has no created-at or featured flag. Category counts reflect visible products only.
- Images pass through visibility-checked API and storefront routes; only local JPEG/PNG/WebP paths
  are allowed, with a 5 MiB transfer bound and no external fetches. Missing/bad images use a local
  neutral placeholder. The API's **pre-existing** static image URLs remain public and unchanged;
  the allowlist is not a private-media storage system.
- Catalog category filter loads up to 48 visible categories. The separate category directory is
  paged and supports search; selecting a category beyond that first set still filters correctly.
- Public catalog reads are not cached, so new requests recheck visibility. The pages are for
  browsing; prices/availability are not reservations or checkout quotes.

## Additive API contract

All endpoints below are anonymous **GET** operations. No existing controller's authorization
or mutation contract is changed.

| Route | Response |
| --- | --- |
| `/api/catalog/products` | Bounded product page |
| `/api/catalog/products/{id}` | Visible product or 404 |
| `/api/catalog/categories` | Bounded categories containing visible products |
| `/api/catalog/products/{id}/image` | Visibility-checked image or 404 |

List inputs: `Page` >= 1; `PageSize` 1–48, default 12; `Search` <= 100 characters.
Products additionally use `CategoryId` and `Sort=newest|name`. Category listing sorts by name/ID.
Product search matches name, description or category name; case sensitivity follows DB collation.
Extreme pages clamp to the last page. Responses have `items`, `totalCount`, `page`, `pageSize`.
Product projections omit stock quantities, internal image paths, users, discounts and order data.
Unavailable, zero-stock and unapproved products return indistinguishable 404s on detail/image routes.
Empty/missing publication configuration exposes no catalog records.

## Verification

```powershell
dotnet test AdminPanel.sln --configuration Release
dotnet run --project AdminPanel.BrowserTests --configuration Release -- --install-browsers
dotnet run --project AdminPanel.BrowserTests --configuration Release
dotnet run --project AdminPanel.BrowserTests --configuration Release -- --storefront
```

The first browser run retains all admin regression checks; the second exercises the storefront in
Chromium and Firefox, Arabic/English, at 1440, 1024, 768, 390 and 320 pixels. It covers live API
catalog data, search/filter/pagination, language state, images, validation/error pages, CSRF, keyboard,
reduced motion and JavaScript-disabled browsing. Screenshots use explicitly seeded **disposable test
data**, including an existing repository apple image. No sample data is inserted by application startup.
CI runs these suites with SQLite and SQL Server and the separate three-process Windows LocalDB smoke.
The Windows smoke uses a uniquely named disposable instance, temporary credentials/certificate and cleanup.

Verified on 2026-10-13 at commit `f0ed003` in
[CI run 38433192498](https://github.com/alharbicode/AdminPanel/actions/runs/38433192498):

| Check | Executed result |
| --- | --- |
| Restore / Release build | Passed on Linux and Windows; zero compiler warnings/errors. |
| Integration tests | 70/70 with SQLite and 70/70 with isolated SQL Server; no failures or skips. |
| Administration regression browsers | Chromium and Firefox each passed 329 explicit checks plus Playwright assertions with each provider. |
| Storefront browsers | Chromium and Firefox each passed 251 explicit checks plus Playwright assertions with each provider; both languages and all five widths. |
| Windows standalone setup | Fresh LocalDB migrations/bootstrap and three real HTTPS processes; admin login/write/CSRF and bilingual real-product browsing passed. |
| Existing migrations | No model drift; SQL Server full migration chain and idempotent script generation passed. No new migration. |
| Dependency audit | No vulnerable NuGet packages reported by configured sources, including transitive packages in all five projects. |
| Local Windows checks | SDK 8.0.425 restore/build and 70 integration tests passed; installed Edge via Playwright passed 251 storefront checks. |

Local browser log review exposed parallel requests sharing one SQLite test connection. Each test now
has a unique shared-memory database and a separate connection per request, held alive by a keeper
connection. A new integration test and browser check issue concurrent image reads and require actual
JPEG responses, so a placeholder cannot hide that regression. The final local browser log has no
application errors.

Screenshots and result JSON are in the run's `verification-results-sqlite` and
`verification-results-sqlserver` artifacts under `AdminPanel.BrowserTests/TestResults/`.
For example: `storefront-chromium/ar-1440-Home.png`, `ar-390-Home.png`,
`en-1440-Home.png`, `en-390-Catalog-Details-27.png`; equivalent Firefox captures are included.
Selected desktop/mobile screenshots were visually inspected. These checks are not a full WCAG,
screen-reader, WebKit/Safari, physical-device or load-testing certification.

## Change map and remaining limitations

- New `AdminPanel.Storefront/`: MVC controllers/client/models, Razor views, resource files, scoped
  styling, image fallback script, neutral placeholder and local launch/configuration files.
- New API `PublicCatalogController.cs` and `PublicCatalogDtos.cs`; `appsettings.json` adds the empty
  publication allowlist. Existing administrator controllers/services/authentication are unchanged.
- Solution and test project references, new storefront integration/browser fixtures and runner,
  existing browser entry-point dispatch, CI storefront step and Windows three-process smoke.
- README, this guide, `AUDIT.md` and `WINDOWS-SETUP.md` document setup, evidence and later phases.

Publication and branding are operator configuration in Phase 1, not administration settings screens.
There is one existing product image per product, original-language catalog text, ID-based recency and
the bounded category dropdown described above. Legacy static images are not private media. Large-catalog
load testing, full accessibility review and production infrastructure remain unverified. Phase 1 has no
accounts, cart, addresses, checkout, shipping calculation, discounts, COD, online payments or currency
conversion. Existing administrator shipping/discount/order records are not presented as customer rules.

## Approved future phases — plan only

- **Phase 2:** guest and registered-user cart persistence, customer registration/login/recovery,
  separate sessions, addresses and ownership enforcement. Preserve existing Admin auth and use vetted
  identity components. Decide guest-to-account cart merging and account recovery policy.
- **Phase 3:** guest/registered COD checkout, enabled Yemen governorate/city destinations and admin-managed
  rates. Calculate totals in the API, snapshot YER/address/items/shipping, add durable idempotency and
  concurrency protection, retain cancellation history and provide safe confirmation/tracking.
  Obtain decisions on taxes, discount application, COD eligibility/limits, stock reservation timing and
  cancellation transitions before implementation. Later schema changes need separate review.
- **Phase 4:** approved online provider, verified/replay-safe payment events, reconciliation/refunds,
  international destinations, additional currencies, operational/security and deployment readiness.
- Before a public production release, separately review the supported-runtime upgrade, rate limiting,
  trusted reverse proxy configuration, durable image storage, backup recovery and full accessibility.
  Current [.NET 8](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) and
  [EF Core 9](https://learn.microsoft.com/en-us/ef/core/what-is-new/) support ends November 10, 2026.
  No deployment or merge is authorized by Phase 1.

| Future phase | Complexity | Acceptance gate before completion |
| --- | --- | --- |
| 2: accounts and carts | Medium–high | Customer-only sessions, tested recovery, guest/cart merge rules, persistent cart validation and address ownership; admin authentication remains separate. |
| 3: Yemen COD checkout | High | Server totals and immutable snapshots, explicit shipping destinations/rates, duplicate submissions create one order, concurrent stock updates cannot oversell, cancellation retains history and tracking does not expose another customer's data. |
| 4: payments/expansion | High; provider-dependent | Signed/replay-safe provider events, reconciliation/refunds, explicit international/currency policies, backup restoration and separately approved production readiness. |

These are dependency estimates, not delivery-date promises. No later-phase schema, API or UI has
been implemented. The existing administrator order API is not a customer checkout endpoint.

Stop after Phase 1. Starting Phase 2 requires explicit owner approval.
