# Storefront Phase 1

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

Initial local restore/build passed with zero compiler warnings/errors and 68 integration tests passed.
Final verification results will be recorded after browser/CI execution.

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
  Current .NET 8/EF 9 support ends November 10, 2026. No deployment or merge is authorized by Phase 1.

Stop after Phase 1. Starting Phase 2 requires explicit owner approval.
