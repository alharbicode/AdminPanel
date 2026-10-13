# Storefront Phase 3: checkout, Yemen delivery and COD

Branch: `codex/storefront-phase-three`; base: `codex/storefront-phase-two`.
This phase preserves the premium administration and bilingual storefront designs. It does not
authorize a merge, deployment or migration of the owner's existing database, including `AdminPanel_Dev`.

## Delivered workflows

| Area | Behavior |
| --- | --- |
| Guest checkout | A valid guest cart can proceed without registration. Contact details, selected delivery zone, city, address and optional instructions are validated before review and confirmation. |
| Customer checkout | Uses the signed-in customer's saved cart and claims-based ownership. Name/email can be prefilled; submitted contact information becomes an order snapshot. |
| Review and confirmation | The API supplies item prices, eligible discounts, shipping and the final YER cash-on-delivery amount. Confirmation rechecks the entire reviewed quote. |
| Customer orders | Paged history, details, current status, eligible cancellation and later cancellation requests. History contains this customer's storefront checkout orders; older manual administration orders remain in administration. |
| Guest tracking | Confirmation displays a private tracking code once. A separate form accepts the code; a public order reference alone never grants access. |
| Delivery administration | Admin-only create/edit/details/paged search/filter of enabled governorate/city zones and YER rates, with version checks. Disable a zone to stop new selections; existing orders retain their snapshots. |
| Order administration | Guest/customer contact and address snapshots, price/discount/shipping totals, validated state transitions and non-destructive cancellation. Preparation/shipping cancellation requests appear in order details. |
| Cart repair | An explicit antiforgery-protected availability refresh removes hidden/unavailable lines and caps quantities at current limits before checkout. Reads alone do not reserve or deduct stock. |

All storefront write forms retain antiforgery protection, Arabic/English messages and RTL/LTR layouts.
Customer and administrator token audiences remain separate. Both MVC applications call the API;
neither connects to SQL Server or borrows administrator credentials for customer operations.

## Shipping selection

New `ShippingZones` are separate from the existing legacy `ShippingOptions` records. No legacy option
is automatically published as a delivery destination, and no zone/rate is seeded by the application.
An administrator must configure the destinations actually supported by the store.

- Every zone has a governorate, an optional city, a nonnegative two-decimal YER fee and an enabled flag.
- A named city permits that city only. Checkout validates the supplied city against the configured
  city, ignoring case and surrounding whitespace, and snapshots the configured name.
- An empty zone city means the entire named governorate uses that rate. The customer must supply
  their city, which is retained with the detailed address.
- Customers explicitly select an enabled zone. There is **no automatic city-rate precedence**.
  If both a governorate-wide and city-specific zone are enabled, both may be selected where applicable.
  Disabling a city-specific row does not exclude it from an enabled governorate-wide row. Do not
  enable a broad rate if it would offer delivery to unsupported cities; use only the supported city rows.
- Unsupported, disabled or invalidly configured zones are rejected. The order country is always `YE`.
- Normalized destination uniqueness and a version token prevent duplicate/stale administrative edits.

The project does not invent a governorate/city directory, carrier tariff or international shipping
rule. Real service coverage, delivery commitments and any future precedence/exclusion rules require
the owner to configure or approve them.

## Pricing and discounts

The existing product-discount definitions remain the source of eligibility. The API applies at most
one discount per product: greatest valid percentage first, then lowest discount ID for an exact tie.
This deterministic overlap rule never stacks discounts.

Dates are evaluated in **UTC**, with an inclusive start and exclusive end (`start <= now < end`).
The start must precede the end; percentage must be greater than zero, at most 100, and have no more
than two decimal places. Invalid, future, expired and other-product definitions are ignored.

Each item's unit discount is `Round(original unit price * percentage / 100, 2, AwayFromZero)`.
Final unit price is the original price less that rounded discount; quantity multiplies these unit
values. Subtotal is original unit price times quantity, discount total sums unit discounts times
quantity, and the payable amount is `subtotal - discount total + shipping fee`. A 100% product
discount does not waive shipping. Negative/unsupported monetary values are rejected. Currency is
always **YER**, and payment method is always **COD**.

Product names, original/final unit prices, applied discount ID/name/percentage/amount, quantities,
shipping fee, totals, currency, country, contact details and address are stored with the order.
Editing a product, discount or zone later does not reprice or rewrite that history. There is no
discount foreign key whose removal would erase the item snapshot.

No tax is added. Tax applicability, registration, inclusive/exclusive display and invoice obligations
remain owner decisions; this implementation does not assert that tax is legally unnecessary.

## Confirmation, stock and retries

The cart version and the authoritative quote fingerprint must still match at confirmation. The
fingerprint covers normalized contact/address data, destination/version, line pricing and totals.
A changed cart, hidden product, insufficient stock, changed rate or changed eligible pricing requires
review again. Limits remain 99 units per product, 100 distinct products and current available stock.

Successful confirmation creates the order in **Pending**, deducts inventory immediately and clears
the cart in one database transaction. A serializable transaction protects pricing/destination reads
until commit; product stock and cart version predicates protect concurrent writes. Failure rolls back
order, lines, stock and cart finalization together. Browsing, cart edits and quote requests never
reserve stock. There is no reservation expiry job or separate COD payment collection ledger.

Each intended submission carries a nonempty GUID idempotency key, scoped to the customer or guest
cart. A durable unique database index and canonical request hash prevent duplicates. Repeating the
same successful request returns the same order; reusing its key with different data returns 409.
The storefront protects the reviewed payload, binds it to the current customer/guest and preserves
the key when a response is lost. Refreshing a successful POST is not permission to create another order.

Concurrent/stale requests return a safe conflict for review/retry. Expected SQL Server deadlocks and
SQLite lock conflicts do not report a partial success. An old guest confirmation replay still
requires the original valid, unconsumed guest cart secret; its independent order tracking code remains
the way to retrieve a retained order after the cart expires or is merged.

Administrator product edits also submit the previously displayed stock quantity. A newer checkout or
stock write causes a conflict instead of silently overwriting inventory.

## Order states and cancellation

The forward state path is:

`Pending -> Confirmed -> Preparing -> Shipped -> Delivered`

`Cancelled` is terminal. Arbitrary skips, backwards changes and revival of cancelled orders are
rejected. Repeating an already achieved state does not repeat side effects.

| Current state | Customer/guest direct cancellation | Administrator cancellation | Customer/guest request |
| --- | --- | --- | --- |
| Pending / Confirmed | Allowed for the owner | Allowed | Use direct cancellation |
| Preparing | Unavailable | Allowed after reviewing the request | Allowed |
| Shipped | Unavailable | Unavailable | Allowed for administration follow-up |
| Delivered | Unavailable | Unavailable | Separate returns/support policy is not implemented |
| Cancelled | Repeating cancellation is harmless | Repeating cancellation is harmless | No new request |

Cancellation retains the order and every line, records actor/time/reason and restores each deducted
quantity exactly once. Order status/version and stock predicates commit together. Legacy order
cancellation uses the same protections; no historical orders are rewritten or deleted by migration.
A request records its time/reason without changing fulfillment state or stock. Shipped requests are
for administrator review only: this phase intentionally has no carrier recall, return/refund or shipped-stock
restoration workflow. Administrators must never move an order backwards to simulate one.

The existing administration DELETE route remains as a compatibility alias for retained cancellation,
and the old status route uses the same state machine. Current MVC forms use versioned action routes.
Manual `POST /api/orders` now requires a unique nonempty `RequestKey` GUID to prevent duplicates;
older external clients must add it. Manual orders retain their prior product-price-only calculation,
without silently adding storefront discounts/shipping. Their details label that distinction.

## Guest access and operational privacy

Guest order access requires a 256-bit non-guessable tracking secret. The API stores only its hash.
The secret is reproducible for a successful retry using a domain-separated HMAC keyed by the
original random guest-cart secret; rotating the API JWT signing key does not change that code.
Only an explicit `X-Guest-Order` header supplies it to the API. It is never placed in a URL.

The storefront remembers the latest selected guest order in a protected, Secure, HttpOnly,
SameSite=Lax, host-only cookie for 30 days. The confirmation page displays the code for the customer
to save privately; the tracking form can re-open a saved code after the cookie expires. The API code
has no automatic expiry/revocation policy in this phase. Possession grants access to contact/address
details and eligible order actions, so it must be treated as a credential. There is no lookup by email,
phone or public reference, and no order-confirmation email/SMS is sent.

Order pages and API responses are not cacheable. Do not log checkout bodies, authorization headers,
tracking codes or full customer addresses. Production still needs durable/shared Data Protection
keys, deliberate reverse-proxy/client rate limits, restricted database/backups and a retention policy.
API IP limits currently aggregate requests from the storefront host. These are operational launch
requirements, not features supplied by a successful local test run.

## Windows and Visual Studio setup

1. Use the [.NET 8/LocalDB/HTTPS/private secrets and administrator bootstrap instructions](WINDOWS-SETUP.md).
   Existing installations keep their private signing key and connection configuration. Never commit
   credentials. Confirm the repository branch is `codex/storefront-phase-three`.
2. Restore and build from the repository root:

   ```powershell
   dotnet restore AdminPanel.sln
   dotnet tool restore
   dotnet build AdminPanel.sln --configuration Release
   dotnet dev-certs https --trust
   ```

3. Review migration `20261013213455_AddCheckoutSnapshotsAndShippingZones` and the reviewable
   [Phase 3 upgrade SQL](migrations/phase3-upgrade.sql). To regenerate that script without applying it:

   ```powershell
   dotnet ef migrations script 20261013202004_AddCustomerAccountsAndCarts 20261013213455_AddCheckoutSnapshotsAndShippingZones --idempotent --project AdminPanel --output phase3-upgrade.sql -- --environment Development
   ```

   Scripting reads the migration definitions; it does not upgrade the configured database. For a
   database older than Phase 2, review the entire required migration chain separately. No startup
   migration is enabled. The agent must not run an update against `AdminPanel_Dev` or another owner database.

4. **Owner action only, after reviewing the database target, tested backup and migration:** select
   the intended development database privately, then deliberately apply the reviewed migration:

   ```powershell
   dotnet ef database update 20261013213455_AddCheckoutSnapshotsAndShippingZones --project AdminPanel -- --environment Development
   ```

   This command is documentation for the owner, not an action performed by the agent. Verification
   may migrate only uniquely named disposable test databases. Never change an existing database's
   connection/name merely to make a migration succeed.

5. In Visual Studio, open `AdminPanel.sln`, select multiple startup projects (`AdminPanel`,
   `AdminPanel.MVC`, `AdminPanel.Storefront`) and HTTPS launch profiles. Alternatively use three terminals:

   ```powershell
   dotnet run --project AdminPanel --launch-profile https
   dotnet run --project AdminPanel.MVC --launch-profile https
   dotnet run --project AdminPanel.Storefront --launch-profile https
   ```

   API: https://localhost:7056; administration: https://localhost:7189; storefront: https://localhost:7289.
   Keep the storefront's configured API catalog base ending in `/api/catalog/`. Its HTTP client derives
   the account/cart/checkout API root from that trusted URL. Do not disable certificate validation.
6. In administration, create only the intended Yemen shipping zones, then explicitly publish approved
   products using the [catalog allowlist](STOREFRONT.md). Confirm stored prices/rates are YER. Use
   disposable test products/accounts to verify guest checkout, customer checkout, order details and
   cancellation in both languages. A checkout reduces stock and creates retained history: do not use
   production records for a smoke test.

## Migration review and rollback

The migration adds `ShippingZones`, snapshot/idempotency/cancellation columns on existing orders,
item snapshot columns and unique/version constraints. `Order.CustomerId` becomes nullable to represent
genuine guests. Existing users, hashes, catalog, images, discounts, shipping options, orders and their
original line amounts remain intact. Existing rows are not assigned fabricated contacts or destinations.

The Down path explicitly refuses automatic downgrade because dropping snapshots/zones and making
customer ownership non-nullable would destroy history or fail once guest orders exist. Assigning
guests to a fabricated customer is not an acceptable workaround. Prefer a reviewed forward fix
or restoring a verified backup. Rolling back application code to the old destructive cancellation
implementation would also be unsafe against new orders. Back up and review code/schema together.

## API additions

- GET `/api/checkout/destinations`: valid enabled zones only.
- POST `/api/{guest|customer}/checkout/quote` and `/confirm`: guest cart header or separate customer
  bearer authentication. Confirm adds idempotency key and reviewed fingerprint; client monetary fields are ignored.
- GET `/api/customer/orders` and `/{id}`: authenticated owner only; paged history is bounded to 50 items.
- POST `/api/customer/orders/{id}/{cancel|cancellation-request}`: version and reason required.
- POST `/api/guest/orders/{lookup|cancel|cancellation-request}`: `X-Guest-Order` required; actions also
  require version/reason. No secret appears in a route or query string.
- POST `/api/{guest|customer}/cart/refresh`: current version required; returns the reconciled cart.
- Admin `/api/shippingzones`: create/update/details and paged `/admin/list`; current version required for edit.
- Admin PUT `/api/orders/{id}/transition`, POST `/{id}/cancel`: current version required.

## Verification record and remaining work

At the preliminary checkpoint, **128 integration tests passed** locally. Final integration totals,
SQLite/SQL Server CI, Chromium/Firefox workflows, Windows smoke and migration evidence are still
being collected. This document does not claim those final Phase 3 checks have passed yet. Final
executed results will replace this checkpoint before completion; previous phase CI is historical evidence.

```powershell
dotnet test AdminPanel.sln --configuration Release
dotnet run --project AdminPanel.BrowserTests --configuration Release -- --install-browsers
dotnet run --project AdminPanel.BrowserTests --configuration Release
dotnet run --project AdminPanel.BrowserTests --configuration Release -- --storefront
dotnet run --project AdminPanel.BrowserTests --configuration Release -- --customers
dotnet run --project AdminPanel.BrowserTests --configuration Release -- --checkout
```

Local tests use isolated in-memory SQLite by default. Optional SQL Server tests use the strictly
guarded `ADMINPANEL_TEST_SQLSERVER` loopback fixture and unique disposable databases, never the app's
connection string. The Windows CI smoke script owns its temporary LocalDB instance/certificate/processes.

Remaining decisions/readiness work: real delivery coverage/rates and service commitments; taxes and
invoices; COD collection/reconciliation/refusal policy; shipped cancellations, returns and refunds;
order notifications; tracking-code revocation/retention; customer/email verification; production email
delivery; full accessibility/Safari/device/load testing; backups/monitoring/key storage/rate limiting;
framework support upgrade. Online payments, international delivery, currency conversion, multi-vendor
operation, Staff permissions and a visual redesign are excluded. Stop after Phase 3 for owner review.
