# Storefront Phase 2: accounts and carts

Branch: `codex/storefront-phase-two`; base: `codex/storefront-phase-one`.
No merge, deployment or migration of an existing local/production database is included.

## Implementation and boundaries

- Customer registration, email/password login, logout, name/profile editing, password change,
  forgot-password and single-use reset, with Arabic/English validation and RTL/LTR.
- Guest cart with add/update/remove, header count and a fixed 30-day secure guest identifier.
- Customer cart persisted in the API database, owned through authenticated claims and retained at logout.
- Registration/login merges guest quantities into saved quantities. Limit: **99 units per product**
  and **100 distinct products**, additionally bounded by current available stock.
- All prices/totals are current **YER estimates** from the API. Client prices/stock/ownership are ignored.
  No inventory deduction/reservation, checkout, order placement, COD, shipping, taxes or discounts.
- Existing styling, catalog publication/images and administrator workflows are preserved.

## Identity and recovery security

Credentials reuse ASP.NET Core `PasswordHasher<User>`. Registration always creates a Customer user
with a generated internal username and a separate `CustomerAccounts` enrollment. Customer email
uniqueness is normalized and database-enforced. Legacy users/emails are not rewritten, merged or
silently enrolled. Legacy Admin/Staff/Customer records cannot use the new customer email flow unless
enrolled through a separately reviewed process.

Customer JWTs use a separate scheme and the fixed audience `AdminPanel.Storefront.Customer`.
Admin-audience tokens cannot authenticate to customer endpoints; customer tokens cannot authenticate
to administrator endpoints. The existing signing key stays on the API. The storefront never connects
to SQL Server or uses administrator tokens.

The `__Host-Alharbi.Customer` cookie is Secure, HttpOnly, SameSite=Lax, and uses a separate Data
Protection application name. Sessions expire after one hour; remember-me persists only this same
lifetime. Logout removes the browser ticket. There is no refresh-token flow. Password reset/change
rotates a security stamp, revoking all customer API tokens. Blocked/deleted users and changed roles
are rechecked on authenticated API requests.

Authentication/recovery/password-change requests are IP-rate-limited; ten invalid password attempts
lock a customer account for 15 minutes. API limits currently aggregate requests from the storefront
host. Production needs deliberate gateway/client rate limiting, not spoofable forwarded headers.
Never log bodies, bearer tokens, guest secrets or reset secrets.

Recovery uses 256-bit random, single-use codes, stored only as hashes, expiring after 30 minutes.
A new request invalidates the previous code. Forgot-password responses do not disclose whether an
eligible account exists. Password change requires the current password. Name editing cannot change
email, role, blocking state or ownership. Email change/verification, MFA, addresses and session refresh
are not implemented and need separately approved policies.

## Cart ownership, merging and conflicts

The guest cookie contains a 256-bit secret; the database stores its SHA-256 hash. Guest carts hold no
personal information. Missing, expired or consumed secrets return 404. Customer IDs come only from
validated claims, never submitted form/JSON values.

Every item mutation requires the current cart version. All item changes and a new parent version
commit together. A merge checks both parent versions and commits combined lines plus the consumed
guest marker in **one transaction**. Concurrent changes return 409 for review/retry, not a blind
overwrite. A repeated successful merge into the same customer does not add quantities again; another
customer cannot consume that guest cart.

Merging keeps existing customer products first, then guest products in product-ID order up to the
100-product limit. Duplicate quantities combine using overflow-safe arithmetic and are capped at
99/current stock. Excess distinct products and unavailable/unpublished/zero-stock lines are excluded.
A localized message asks the customer to review adjustments. No excluded product names/prices are
returned. Reads also revalidate current prices, visibility and quantity limits; writes persist
normalization. Availability may change after any response: carts are not reservations.

The guest cookie is cleared only after successful/already-completed merge or confirmed expiry.
A transient merge failure leaves the customer signed in and retains the guest secret. The cart page
offers an antiforgery-protected retry. All storefront writes, including login/logout/recovery, require
antiforgery. API writes use bearer authorization or an explicit guest header, not browser cookies.

## Windows and Visual Studio

1. Follow [WINDOWS-SETUP.md](WINDOWS-SETUP.md) and [STOREFRONT.md](STOREFRONT.md) for .NET 8,
   SQL Server/LocalDB, HTTPS, private JWT/database settings, administrator bootstrap and publication.
2. Restore/build, then generate a script **without applying it**:

   ```powershell
   dotnet restore AdminPanel.sln
   dotnet tool restore
   dotnet build AdminPanel.sln --configuration Release
   dotnet ef migrations script 20261012000000_HardenDataIntegrity AddCustomerAccountsAndCarts --idempotent --project AdminPanel --output phase2-upgrade.sql -- --environment Development
   ```

3. Review the migration/script and back up the intended development database. **Owner action after
   approval:** apply the migration to that deliberately selected database:

   ```powershell
   dotnet ef database update --project AdminPanel -- --environment Development
   ```

   This command was not run against the owner's local database. The agent generated the migration
   using an unused connection string; no startup migration runs. Only disposable CI SQL Server/LocalDB
   databases apply migrations during verification. Local tests use isolated in-memory SQLite.

4. Start three terminals, or select multiple Visual Studio startup projects with HTTPS profiles:

   ```powershell
   dotnet run --project AdminPanel --launch-profile https
   dotnet run --project AdminPanel.MVC --launch-profile https
   dotnet run --project AdminPanel.Storefront --launch-profile https
   ```

   API: https://localhost:7056; admin: https://localhost:7189; storefront: https://localhost:7289.
   The customer client derives the API root from the parent of the existing `.../api/catalog/` URL.

5. Publish an approved real product through the Phase 1 allowlist; confirm its existing price is YER.
   Add it as a guest, register, verify the merge, sign out/in and verify the saved cart. Test both
   languages and two browser contexts for independent guests.

## Development email: no paid service needed

In **Development**, the default sender writes private plain-text `.eml` messages outside web roots:

`%LOCALAPPDATA%\AdminPanel\DevelopmentMail`

Request recovery at `/Account/ForgotPassword`, open the matching local email and follow its link.
The link carries email/code in a **URL fragment**, never an HTTP request URL. A local script fills
the form then clears the fragment from browser history. Without JavaScript, paste the email/code
manually. Keep the mail folder private; it contains development reset secrets. Do not use real
customer data with this sender. No public mail-preview route exists. `DevelopmentFile` is refused
outside Development; tests inject an in-memory outbox. Delivery failures log only a generic
diagnostic, never exception bodies, email addresses or reset links.

## Production email prerequisite

`IPasswordResetEmail` supports the built-in SMTP sender or a separately reviewed provider.
Non-Development defaults to disabled delivery. Configure and verify delivery **before launch**.
Forgot-password still returns the same generic response if delivery is unavailable.

Configure privately on the API, using deployment secrets rather than checked-in credentials:

| Setting | Purpose |
| --- | --- |
| `CustomerEmail:Provider` | `Smtp` |
| `CustomerEmail:StorefrontUrl` | Trusted public HTTPS storefront base URL; never an incoming Host header |
| `CustomerEmail:Smtp:Host` / `Port` | Server and STARTTLS port (default 587) |
| `CustomerEmail:Smtp:From` | Verified sender address |
| `CustomerEmail:Smtp:Username` / `Password` | Private provider credentials |

SMTP requires TLS and normal certificate validation. Configure sender-domain authentication and test
real delivery with the chosen provider. No production email provider was configured or verified.

## Migration and rollback review

`20261013202004_AddCustomerAccountsAndCarts` adds only `CustomerAccounts`, `ShoppingCarts` and
`ShoppingCartItems`, with keys/indexes/foreign keys. It does not alter/backfill existing user,
product or order rows. There is one cart per customer and one item row per cart/product.

Deleting a product may cascade its **cart items** only; existing order/reference protections remain.
Expired/consumed guest rows are retained; a reviewed retention/cleanup job is future work.

The migration's Down path drops the three new tables, losing enrollments, carts and reset state.
Customer rows/password hashes created in `Users` remain. Prefer a forward fix or restore a verified
backup; code rollback alone must disable customer features. Never apply a destructive rollback on an
existing database without separate review/approval. Migration generation and disposable verification
do not authorize deployment.

## Narrow API surface

- Anonymous POST `/api/customer/account/{register|login|forgot-password|reset-password}`.
- Customer-only GET/PUT `/api/customer/account/profile`, POST `change-password`.
- Anonymous POST `/api/guest/cart` returns a new secret once. GET cart and POST/PUT/DELETE
  `items/{id}` require `X-Guest-Cart`.
- Customer-only GET `/api/customer/cart`, POST/PUT/DELETE `items/{id}`, POST `merge`.
- Item writes accept `version` and `quantity` (1–99). Price, currency, stock and ownership are
  server-controlled. Merge accepts a guest secret; its destination is the authenticated customer.

## Verification

```powershell
dotnet test AdminPanel.sln --configuration Release
dotnet run --project AdminPanel.BrowserTests --configuration Release -- --install-browsers
dotnet run --project AdminPanel.BrowserTests --configuration Release
dotnet run --project AdminPanel.BrowserTests --configuration Release -- --storefront
dotnet run --project AdminPanel.BrowserTests --configuration Release -- --customers
```

The implementation passed [CI run 38464970518](https://github.com/alharbicode/AdminPanel/actions/runs/38464970518)
at `9651ac4`: restore/build, 87 tests on each database, 329 admin / 250 catalog / 176 customer browser
checks plus assertions in each Chromium/Firefox and SQLite/SQL Server combination, Windows LocalDB
and all three HTTPS apps, migration consistency/scripts, and dependency audit. SQL Server verification
includes upgrading a seeded historical schema and checking original users/password hashes/stock.

Four additional email-safety cases bring the current local suite to **91 passing tests**, none failed
or skipped; restore/build have zero compiler warnings/errors. Every follow-up commit runs the same
CI matrix; see [PR #4 checks](https://github.com/alharbicode/AdminPanel/pull/4/checks) for the latest result.
Local Playwright/Edge passed 250 catalog and 176 customer checks plus assertions.
Customer coverage includes seven account/cart routes, Arabic/English, desktop/tablet/mobile,
registration validation, guest/saved carts, merging, recovery, logout/login, cookies, CSRF and no-JS.
Screenshots use disposable fixtures and appear in `AdminPanel.BrowserTests/TestResults/customers-*`.

Full WCAG/screen-reader, Safari/WebKit, physical-device, mail-provider and load testing remain unverified.
Production rate limiting, durable shared Data Protection keys, retention and the supported-runtime
upgrade remain readiness work. Stop after Phase 2 for owner review.

