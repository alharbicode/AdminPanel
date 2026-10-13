# Solution audit and completion plan

Baseline: `afdeda5208a2dc75bf410771e47b3ff7a55c5aef` on `master`.
Development branch: `codex/audit-repair-adminpanel`.

The administration repair and Storefront Phase 1/2 sections below are historical records of their
respective scopes and results. The latest addition is **Storefront Phase 3** on
`codex/storefront-phase-three`, based on `codex/storefront-phase-two`. The owner approved Yemen/YER
guest/customer checkout, configured shipping, automatic eligible discounts, COD and retained order
cancellation. These approved rules supersede the earlier out-of-scope/decision notes; they do not
authorize deployment or an existing-database migration. See [the current Phase 3 record](#storefront-phase-3)
and [implementation/setup guide](STOREFRONT-PHASE-3.md).

## Priorities and acceptance criteria

1. **P0 - Restore a coherent build and data model.** The order model is in the global namespace, contains a duplicate OrderItem, and disagrees with every order migration and controller. MVC order models and Razor views also disagree. Restore the migration-backed entities, remove obsolete EF 1.x tooling, align EF packages, and repair view names/models. Acceptance: restore/build both applications; migration model consistency check passes.
2. **P0 - Secure administration.** API CRUD is anonymous, passwords are compared as plaintext, the signing key is committed, issuer/audience validation is disabled, and MVC relies on an unvalidated token cookie. Use hashed passwords, external secret configuration, validated JWTs and current user checks, protected MVC authentication cookies, authorization and antiforgery. Acceptance: invalid/expired/wrong-issuer tokens, blocked users, non-admin roles and missing antiforgery tokens cannot mutate data; valid admin login reaches the dashboard. Provide an explicit local bootstrap/reset command with no default credentials.
3. **P1 - Repair existing MVC/API workflows.** Product requests send JSON to form endpoints; order details endpoint is missing; order serialization/status bodies differ; product delete has the wrong model. Acceptance: MVC login, category CRUD, multipart product create/edit/delete, order list/details/status/cancel work against the real API pipeline, including prices/items and validation errors.
4. **P1 - Protect data integrity and errors.** Validate DTOs and references; reject invalid quantities, unavailable products and insufficient stock; enforce optimistic stock concurrency; avoid cascading removal of catalog/history; validate uploads; return safe errors. Acceptance: invalid or conflicting requests do not change inventory/data; cancellation restores inventory once; relational integration tests verify constraints and HTTP failures.
5. **P2 - Make development reproducible.** Document SQL Server setup, explicit migrations, admin provisioning, secret management, HTTPS endpoints and tests. Add CI and automated integration coverage. Acceptance: documented commands match actual projects/configuration; restore, build, tests and migration script checks have recorded results.

## Product decisions and boundaries at the original administration repair

- The user confirmed **Admin only** administration for this repair; Staff permissions remain unspecified. Storefront Phase 2 now adds explicitly approved customer accounts and carts; customer checkout remains out of scope.
- Existing order creation totals product line prices only. Applying discounts/shipping, currency/tax rules, and fulfillment transition rules require product decisions.
- Preserve the existing DELETE cancellation contract (remove the order and restore stock). A retained audit/history cancellation workflow is a separate product decision.
- Preserve the status values already offered in the MVC screen: Pending, Confirmed, Shipped, Delivered, Cancelled. Cancelled must use cancellation so stock cannot silently diverge.
- Phase 2 adds shipping, discount and content page MVC CRUD plus descriptive dashboard statistics. Discount date-window filters classify stored dates only; they do not apply discounts to orders.
- Existing plaintext passwords must be explicitly reset; there is no silent plaintext fallback. No database migration or bootstrap runs automatically at startup.
- No production database or deployment is part of this repair.

## Audit coverage

Reviewed both project/solution configurations, startup/configuration/launch settings, every controller/service/DTO/model, Razor views and shared layout, database context, all migrations and model snapshots, authentication and static upload handling. No automated test project or CI exists at baseline. Template weather/home/privacy pages remain non-business scaffolding.

## Verification

Verified on 2026-10-12 in [GitHub Actions run 38305307485](https://github.com/alharbicode/AdminPanel/actions/runs/38305307485), commit `3dcb035`:

| Check | Result |
| --- | --- |
| `dotnet restore AdminPanel.sln` | Passed on both CI jobs |
| Release build | Passed; 0 compiler warnings, 0 errors |
| Integration tests with relational SQLite | 33 passed, 0 failed, 0 skipped |
| Same suite with disposable SQL Server 2022 | 33 passed, 0 failed, 0 skipped; full migration chain applied |
| EF pending model changes | None |
| Idempotent SQL migration script | Generated successfully; saved as CI artifact |
| NuGet vulnerability audit, including transitive packages | No vulnerable packages reported by configured sources |

The first CI test run found optional descriptions incorrectly rejected by implicit API validation; this was corrected and the same tests passed. Added coverage also verifies null order lines, decimal precision, unavailable products, safe upstream failure handling, and explicit hashed account provisioning/reset.

The host has no usable `dotnet` on PATH. A workspace-only SDK download was attempted but its transfer stalled/reset, so all .NET verification above was actually executed on GitHub's Ubuntu runners with SDK 8.0.425. No local or production database was touched. SQL Server test databases use unique names and are dropped on disposal; the ephemeral container is removed by CI.

P0/P1 repairs and the P2 reproducibility work listed above are implemented. Phase 2 implements the previously missing secondary screens; remaining scope is tracked below. Phase 1 did not include browser-driven QA; Phase 3 results appear below. No live deployment or migration against existing production data is claimed. Image signatures are checked but files are not fully decoded or malware-scanned; old assets remain pending an explicit cleanup policy.

## Phase 2 acceptance and remaining-work checklist

| Feature | Verifiable acceptance |
| --- | --- |
| Shipping options | MVC list/details/create/edit/delete use API DTOs; name/area search, area filter, paging and referenced-order conflict feedback work. |
| Discounts | MVC CRUD selects existing products, validates percentage/precision and date ordering, explicitly labels UTC, supports product/date-window filters. No discount application is added. |
| Content pages | MVC CRUD validates slug/content, reports normalized slug conflicts and HTML-encodes stored markup in previews/forms. |
| Dashboard | Counts reflect seeded database records, status groups reflect stored values, latest five orders have stable timestamp/ID ordering; empty state renders. |
| Security and usability | Every new read is Admin-only, every MVC write requires antiforgery, delete GET only confirms, invalid input preserves fields, filters survive pagination, out-of-range pages are bounded. |
| Verification | Entire existing and extended suite must pass on both relational SQLite and disposable SQL Server; restore/build, migrations and vulnerability audit must pass. |

Phase 2 passed [PR verification run 38308393672](https://github.com/alharbicode/AdminPanel/actions/runs/38308393672) at commit `49d2134` on 2026-10-12: **45/45 tests on SQLite and 45/45 on SQL Server 2022**, zero failed or skipped. Restore and Release build passed with zero compiler warnings/errors. EF reported no pending model changes, generated the idempotent migration script, and the NuGet audit reported no vulnerable packages in any project. SQL Server applied the full migration chain to disposable test databases. The new integration tests execute the real MVC and API request pipelines and Razor rendering. They cover all three CRUD journeys, unauthorized roles, CSRF, validation, conflicts, stored markup, pagination/filtering, missing records and dashboard aggregates.

- [x] Shipping, discount and content management screens and navigation.
- [x] Dashboard descriptive statistics, status breakdown and recent orders.
- [x] Server-side pagination/search and relevant filters for the three new lists.
- [x] Validation, encoded content preview, confirmation/success feedback, responsive navigation/tables, submit progress and duplicate-submit prevention.
- [x] Record passing Phase 2 CI on SQLite and SQL Server (45 tests per provider).
- [x] Categories/products/orders database pagination, search/filter/sort and bounded searchable product selection (Phase 3).
- [ ] Product decisions: customer onboarding/checkout, shipping/discount/tax calculations, currency, Staff permissions, fulfillment transitions and retained cancellation history.
- [ ] Operational follow-ups: orphan image cleanup policy, image decoding/malware scanning, shared Data Protection keys and distributed rate limiting for multiple instances.
- [x] Add real Chromium/Firefox workflow and responsive-layout checks; Phase 3 records executed results below. Full manual accessibility certification and other browsers remain separate.

No new schema migration or new secret/configuration is required for Phase 2. Existing production data was not accessed and deployment/merge are excluded.

## Phase 3 implementation and acceptance

| Feature | Implementation and verification criterion |
| --- | --- |
| Categories | Database paging, name/description search, has-products filter, ID/name sort with stable ID tie-breakers; filters survive paging. |
| Products | Database paging, name/description search, category/availability/stock filters, ID/name/stock sort; invalid queries return 400 and extreme pages are bounded. |
| Orders | Database paging; exact ID or customer username/email search; stored status and inclusive UTC date filters; newest/oldest/ID sorting. No checkout or transition rules added. |
| Product selection | Discount forms and filters fetch at most 20 ID/name pairs; Enter/search, result pages, loading/empty/retry states, explicit selection and numeric-ID fallback. No new frontend dependency. |
| Browser usability | Real Chromium/Firefox against both application request pipelines. Every administration route is rendered at 1365, 390 and 320 pixels; form labels, headings, language/main landmark and viewport overflow are checked. CRUD, uploads, filters, navigation, keyboard skip link, corrected validation, submit progress/duplicate prevention, back navigation, CSRF rejection and login/logout have executable checks. |
| Progressive enhancement | Separate JavaScript-disabled browser context verifies mobile navigation, server validation, category creation and numeric product selection on discount creation. |
| Windows readiness | Document SDK, LocalDB, private configuration, JWT generation, explicit migrations/bootstrap and both HTTPS projects. CI creates a unique LocalDB instance and runs actual API/MVC processes with a temporary trusted certificate, then checks login, shipping creation and missing-token rejection. |

The browser runner uses the official Microsoft.Playwright package with Chromium and Firefox. Its HTTPS bridge forwards to the actual MVC TestServer and API TestServer pipelines; the separate Windows smoke job exercises two standalone HTTPS processes. Fixtures use random credentials and disposable databases only. Browser artifacts contain screenshots and result JSON, without authentication traces or credentials.

Browser testing reproduced a corrected-field click being swallowed by validation layout changes; fields now revalidate while being corrected and reserve error-message space. Screenshot review also found unreadable compressed mobile table cells; tables now preserve readable width in keyboard-focusable scroll regions. Other repairs include semantic headings, a keyboard-focusable skip target, descriptive stock labels, and UTC dates and amount formatting without an assumed currency consistent with the existing data.

Browser JavaScript-error checks also exposed jQuery Validate's unsupported `step` rule on discount `datetime-local` fields. The adapter delegates that rule to native input validity while preserving millisecond values and all server validation. Browser fixtures preserve their real loopback host in authentication redirects. The submission test holds the default navigation after normal application handlers, checks loading/duplicate prevention, then resumes a real form POST and verifies the database record; it does not mock API responses.

### Phase 3 verification results

Verified on **2026-10-12**, commit `688dfe1`, in [PR run 38319499503](https://github.com/alharbicode/AdminPanel/actions/runs/38319499503):

| Check | Executed result |
| --- | --- |
| Restore and Release build | Passed on SQLite, SQL Server and Windows; zero compiler warnings/errors. |
| Integration tests | **57/57 SQLite and 57/57 SQL Server 2022**, zero failures or skips. |
| Chromium browser workflows | **329 explicit checks plus Playwright assertions passed with each database.** |
| Firefox browser workflows | **329 explicit checks plus Playwright assertions passed with each database.** |
| Screen coverage | **26 administration routes at 1365, 390 and 320 pixels** per browser/provider; 312 screen renders across four combinations, plus login and interaction journeys. |
| Browser interactions | CRUD across five management areas, multipart product upload, order status/cancellation/stock restoration, search/filter/paging, bounded picker/retry, client/server validation, stored markup encoding, loading/duplicate prevention, back navigation, CSRF rejection, keyboard skip link, mobile navigation, JavaScript-disabled forms and login/logout passed. No unexpected page JavaScript errors. |
| Windows standalone smoke | Fresh LocalDB migrations and administrator bootstrap; two real HTTPS processes with trusted TLS; MVC/API login and shipping creation; missing-CSRF rejection; owned database/instance cleanup passed. |
| SQL Server migrations | Full migration chain applied to isolated databases; no pending model changes; idempotent SQL generated as artifact. |
| Dependency audit | No vulnerable NuGet packages reported by configured sources, including transitive dependencies in all four projects. |

Screenshots and result JSON are in each database's verification artifact. Selected Chromium/Firefox desktop and phone screenshots were also inspected manually; the mobile-table fix was rechecked in screenshots. These layout and accessibility basics are not a full visual regression or WCAG certification. The agent host's minimal browser reproduction verified the corrected-validation click; the full .NET and browser suites ran in CI, not on that host.

Windows CI initially stalled on an interactive CurrentUser root-certificate prompt. The CI-only script now trusts the short-lived localhost certificate in the disposable runner's machine store and removes that exact certificate during cleanup. Application TLS checks remain enabled. CI has bounded job timeouts and a push-only job that cancels older verification runs on the same branch; only this cleanup job has `actions: write`, while build/test jobs retain read-only repository permissions.

- [x] Database paging/search/filter/sort for all requested lists and bounded product selection.
- [x] Real browser workflows on both relational providers, including JavaScript and no-JavaScript paths.
- [x] Isolated SQL Server migrations and standalone Windows HTTPS setup verification.
- [x] Windows setup, verification evidence and remaining limitations documented.
- [ ] Full manual accessibility review, WebKit/physical-device coverage and the product/operational decisions below.

### Remaining gaps

- Staff permissions, customer onboarding/checkout, shipping/discount/tax calculations, currency, fulfillment transitions and retained cancellation history still require product decisions.
- Category dropdowns still load all categories; product pickers are bounded. Legacy unpaged API collection endpoints remain for compatibility. Search follows each database's collation; no fuzzy search or large-scale performance claim is made.
- Browser checks cover Chromium/Firefox and selected desktop/phone widths. WebKit/Safari, physical mobile devices, screen-reader testing, full contrast/WCAG review and production infrastructure are not verified.
- Image decoding/malware scanning, orphan cleanup, shared Data Protection keys and distributed rate limiting remain operational follow-ups. Plan the separately scoped framework support upgrade.
- During the administration repair the agent host lacked a usable .NET SDK. A usable SDK is now available for the local storefront verification recorded below. No production data, master merge or deployment is involved.

## Storefront Phase 1

Branch `codex/storefront-phase-one` starts from premium-dashboard commit `6cf0cb8`.
[Draft PR #3](https://github.com/alharbicode/AdminPanel/pull/3) targets `codex/premium-dashboard-ui`.
The earlier administration Phase 1–3 sections describe a different workstream; only the public
browsing phase of the approved e-commerce roadmap is implemented here.

| Feature | Implementation and acceptance |
| --- | --- |
| Separate storefront | New ASP.NET Core MVC project uses public API reads only, with no SQL reference, administrator credential or bearer token. |
| Public catalog | Dedicated anonymous GET routes return bounded projections; publication requires an explicit configured ID plus availability, positive stock, valid category and nonnegative price. Empty configuration exposes nothing. |
| Browsing | Responsive home, search/paged products, category filtering/name sorting, searchable/paged category directory, product details and related products use actual API records. |
| Images | Visibility rechecked at the API, local JPEG/PNG/WebP paths only, bounded proxy transfer, no external fetch, neutral missing-image fallback. Concurrent requests must return the real fixture image. |
| Arabic/English | Arabic-first interface with visible AR/EN switch, RTL/LTR, original catalog text, YER prices and centralized store branding. Switch is antiforgery-protected and preserves only local return locations. |
| Existing admin | Premium MVC project, administrator authentication/authorization, backend services, models, database context and migrations have no changes from the premium base. Existing admin integration/browser workflows still pass. |
| Local setup | Windows/Visual Studio guide covers all three apps, publication allowlist, currency confirmation and branding; existing JWT/bootstrap/database instructions remain applicable. |

Verified on **2026-10-13**, implementation commit `f0ed003`, in
[run 38433192498](https://github.com/alharbicode/AdminPanel/actions/runs/38433192498): restore and Release
build passed with zero compiler warnings/errors; **70/70 integration tests on each provider**;
**329 administration and 251 storefront explicit browser checks plus Playwright assertions** in each
Chromium/Firefox and SQLite/SQL Server combination; Windows isolated LocalDB, existing migrations,
bootstrap, three HTTPS processes and real catalog/language-switch smoke passed. EF reports no pending
model changes; idempotent SQL generated; NuGet reported no vulnerable packages in the five projects.

Local Windows SDK 8.0.425 restore/build and all 70 tests also passed. Playwright using installed Edge
passed all 251 storefront checks after a test-fixture concurrency repair: independent request connections
now share only a uniquely named in-memory database. Earlier parallel image requests could produce
SQLite connection errors and fall back to placeholders; the added concurrent-image checks require JPEG
responses. The final local browser log was checked and had no application errors.

Storefront browser coverage includes Arabic/English at 1440, 1024, 768, 390 and 320 pixels, normal and
invalid/missing pages, real catalog images, search/filter/paging, preserved language/query state,
encoded stored markup, CSRF, keyboard skip navigation, reduced motion and no-JavaScript browsing.
Selected desktop/mobile screenshots were reviewed; all captures/result JSON are CI artifacts.
No production data was accessed or changed. Nothing was merged or deployed.

- [x] Public MVC storefront and narrow catalog API, preserving the premium admin.
- [x] Arabic/English, RTL/LTR, YER, original product content and centralized configurable name.
- [x] Fail-closed publication policy, safe images, bounded queries, encoded content and antiforgery.
- [x] SQL Server/SQLite integration and Chromium/Firefox regression/browser verification.
- [x] Windows setup, screenshots, recorded evidence and explicit later-phase plan.
- [x] Phase 2 approved: customer accounts/recovery/sessions and guest/registered carts (implemented below).
- [ ] Addresses: excluded from the approved Phase 2 scope; revisit with checkout requirements.
- [ ] Phase 3 approval and rules: Yemen governorate/city rates, guest/registered COD, server totals,
  snapshots, idempotency, concurrency, retained cancellations and private tracking.
- [ ] Phase 4: provider evaluation, verified payment events/refunds, international/currency expansion
  and separately approved production readiness.
- [ ] Publication/branding administration settings, translated catalog content, large-catalog performance,
  full accessibility/device review, private/durable image storage and supported-runtime upgrade.

The user has now approved single-vendor retail, Yemen/YER, guest and registered shopping, destination
rates and future COD. Detailed tax/discount/COD/fulfillment rules and Staff permissions remain decisions.
The [storefront guide](STOREFRONT.md) records limitations, acceptance gates and complexity for future
phases. Phase 2 was subsequently approved; later phases still require separate owner approval.

## Storefront Phase 2

Branch `codex/storefront-phase-two` starts at Phase 1 commit `7e3b70f`.
[Draft PR #4](https://github.com/alharbicode/AdminPanel/pull/4) targets `codex/storefront-phase-one`.
The owner approved accounts and carts only. No checkout, order placement, payment, COD, shipping
calculation, stock reservation, currency conversion, Staff policy or visual redesign is included.

| Feature | Implementation and acceptance |
| --- | --- |
| Customer identity | Registration creates only Customer users using ASP.NET password hashes; normalized email uniqueness, invalid credentials and temporary lockout tested. Existing users are preserved, not silently enrolled. |
| Authorization | Separate customer JWT audience and protected storefront cookie; server checks current role/block/security stamp. Admin/customer tokens cannot cross their authorization boundaries. |
| Profile and recovery | Name editing and current-password change; hashed random reset codes, 30-minute expiry, replacement/single-use and session revocation. Generic recovery responses, private development mail and configurable TLS SMTP. |
| Guest cart | Secure, host-only, HttpOnly identifier with 30-day expiry; API stores only its hash. Add/update/remove and header quantity work; no public GET creates a cart. |
| Saved cart | One database cart per enrolled customer; owner comes from authenticated claims. Logout/login preserves lines. Storefront has no SQL reference or administrator credentials. |
| Merging | One transaction covers both cart versions, lines and consumed-guest marker. Repeated merge cannot double quantities. Conflicts return 409 and preserve the guest secret for explicit retry. |
| Product integrity | Current catalog publication, stock and price rechecked. Quantity capped at 99/current stock; 100 distinct products; overflow-safe combining. Hidden/unavailable products excluded; localized adjustment message; YER estimates only. |
| UI and security | Arabic/English account/cart pages retain existing styling, RTL/LTR and responsive forms. Every storefront write requires antiforgery. Reset secrets use URL fragments, never request query strings; screenshots mask password/code fields. |
| Migration | Additive CustomerAccounts, ShoppingCarts and ShoppingCartItems tables only. Upgrade-preservation test compares existing users/password hashes and product stock. Migration generated without applying it to the owner's local database. |
| Setup | Windows/Visual Studio, all three HTTPS apps, development/production email, migration review and destructive-rollback implications documented in STOREFRONT-PHASE-2.md. |

The implementation at `9651ac4` passed [CI run 38464970518](https://github.com/alharbicode/AdminPanel/actions/runs/38464970518)
on **2026-10-13**: restore and Release build, **87/87 integration tests on SQLite and SQL Server**,
and **329 administration, 250 catalog and 176 customer explicit browser checks plus assertions**
for each Chromium/Firefox and database combination. SQL Server applied the full migration chain
and the upgrade-preservation test passed. Windows isolated LocalDB, bootstrap, all three HTTPS apps,
admin CRUD/CSRF, bilingual catalog, customer registration/login/logout and guest/saved merge passed.
No pending EF model changes; idempotent SQL generated; no vulnerable NuGet packages reported.

Four additional production-email safety cases bring the current suite to **91 local passing tests**;
restore/Release build have zero compiler warnings/errors. Follow-up commits receive the same CI
matrix; see PR #4 checks for the latest commit's results. Installed Edge/Playwright also passed
250 catalog and 176 customer checks. Customer pages were rendered at 1440/768/390/320 pixels in
both languages; selected desktop/mobile screenshots were inspected. Screenshots and test results
are retained as CI artifacts. Full accessibility or production email delivery is not claimed.

- [x] Customer accounts, profile, recovery and secure administration boundary.
- [x] Guest/saved carts, quantity controls and atomic, repeat-safe merging.
- [x] Localized responsive forms, antiforgery and regression coverage.
- [x] Additive migration generated; owner's database untouched; setup/rollback documentation.
- [x] Implementation verified on SQL Server/SQLite, Chromium/Firefox and Windows LocalDB in CI.
- [ ] Owner review before any merge, migration of an existing database or deployment.

Remaining readiness work: production email configuration/delivery verification, email verification
and address/change policies, MFA, session refresh, expired/consumed-cart retention, distributed rate
limiting and shared Data Protection keys. API auth limits currently aggregate requests from the
storefront host. Full accessibility, Safari/physical-device and load testing are not claimed.
Framework support upgrade remains separate. Checkout and later business rules are deferred.

## Storefront Phase 3

Branch `codex/storefront-phase-three` starts from approved Phase 2 and targets
`codex/storefront-phase-two`. The new work implements only the owner's approved single-vendor Yemen
checkout/COD scope. Existing dashboard/storefront design, administrator permissions and earlier
account/cart behavior are preserved. No owner database, including `AdminPanel_Dev`, was upgraded.

### Audit findings and repairs

| Finding | Repair and acceptance criterion |
| --- | --- |
| Repeating manual order creation could create duplicates | Admin create now requires a nonempty `RequestKey`; owner/key uniqueness and canonical payload comparison return the original success or reject key reuse. Storefront confirmation has equivalent durable idempotency. |
| DELETE cancellation erased historical orders | Both legacy and current cancellation routes retain order/lines, actor/time/reason and a restoration marker. Repeat/concurrent calls must restore stock once only. |
| Status updates allowed arbitrary fulfillment changes | Shared state machine enforces Pending, Confirmed, Preparing, Shipped, Delivered forward transitions; Cancelled is terminal. No cancellation after shipping/delivery. |
| Stale admin stock forms could overwrite checkout changes | Product PUT requires the displayed expected stock value, then EF checks the originally loaded stock at save. Conflicting updates return 409. |
| Products, discount definitions and shipping could change after a quote | Confirmation recalculates authoritative data and compares the reviewed fingerprint. A serializable transaction and concurrency predicates protect order/stock/cart consistency. |
| Order displays relied on mutable product/user records | New checkout orders retain contact/address/country, item name, original/discount/final price, shipping, currency and COD snapshots. Later edits do not rewrite them. Legacy values are preserved without fabricated backfills. |
| Legacy shipping options did not define public delivery coverage | Separate Admin-managed ShippingZones expose only valid enabled governorate/city selections. No legacy option is automatically enabled for checkout. |
| No customer checkout or protected order access existed | Guest/customer review and confirmation, customer-owned history/details, private guest tracking, early cancellation and later request forms are implemented in Arabic/English. |
| Read-only cart normalization could leave invalid stored lines before checkout | Explicit versioned refresh persists availability/quantity adjustments without touching stock and supplies a clear repair path. |

### Approved behavior and boundaries

- Product prices and delivery fees are YER; initial payment is COD. No online payment, tax charge,
  currency conversion, international delivery or multi-vendor workflow is added.
- A selected enabled shipping zone supplies the fee. Named-city rows require that city; empty city
  means governorate-wide. There is no implicit city-rate precedence or exclusion. An enabled broad
  zone covers its cities even if a separate city row is disabled; administrators must configure only
  coverage the store actually supports.
- Eligible product discounts use UTC inclusive start/exclusive end, valid percentages through 100%,
  greatest percentage then lowest ID. Only one applies. Unit discount rounds to two decimals away
  from zero before quantity multiplication. Shipping is added after the item discount; no tax is inferred.
- Stock is deducted when a Pending order is successfully confirmed by the customer, never while
  browsing, maintaining a cart or requesting a quote. Order/lines/stock/cart finalize together.
- Customers/guests directly cancel Pending/Confirmed orders. Administrators may also cancel Preparing
  orders. Shipped/Delivered cancellation is blocked. Preparing/Shipped customers can record an
  administration request; it changes neither status nor stock. Returns/recalls/refunds remain separate.
- Manual administration creation retains its existing product-price-only calculation, now with
  explicit request-key protection. Current stock/order edits use expected values/versions; external
  clients must supply the new safety preconditions.
- Guest tracking uses a non-guessable secret supplied in an explicit API header, hash-only database
  storage and a protected browser cookie. Public references/IDs/email/phone cannot retrieve an order.
  Save the displayed private tracking code; order email/SMS notifications are not implemented.

### Migration and setup evidence

`20261013213455_AddCheckoutSnapshotsAndShippingZones` adds the new zone table, nullable order/item
snapshots, idempotency/cancellation metadata and indexes. Customer ownership becomes nullable for
genuine guests. Existing user, catalog, image, discount, shipping and order records are not deleted
or repriced. The migration's Down path refuses automatic downgrade to prevent losing guest/order
history. Use a reviewed forward fix or verified backup restoration instead.

The reviewable [Phase 3 SQL](migrations/phase3-upgrade.sql) and
[Windows/Visual Studio instructions](STOREFRONT-PHASE-3.md#windows-and-visual-studio-setup) are for owner
review. Generation is not application. Any verification migration must target an isolated test
database; a production/local owner database remains untouched until separately approved.

### Phase 3 verification checkpoint

At the preliminary checkpoint, **128 integration tests passed locally**. This includes existing
regressions and checkout/order-integrity cases. Additional validation/boundary/browser coverage and
final results are being collected; no final Phase 3 SQL Server, Chromium/Firefox or CI success is
claimed yet. Replace this checkpoint with executed run/commit evidence before completion.

The expanded checks cover authoritative guest/customer pricing, active/expired/invalid/overlapping
discounts, exact UTC boundaries, disabled destinations, cart changes, idempotency, concurrent stock
and duplicate submissions, private ownership/tracking, cancellation/request state rules, exactly-once
restoration, legacy records, stale admin writes, shipping CRUD, antiforgery and audience isolation.
Browser targets include Arabic/English checkout/tracking/history, responsive widths, real submission,
CSRF, cancellation, navigation and no-JavaScript behavior. Targets are not proof of passed execution.

### Current remaining-work checklist

- [x] Guest/customer checkout, authoritative quote review and durable submission handling.
- [x] Yemen zone/rate administration, approved discount calculation and immutable snapshots.
- [x] Safe fulfillment/cancellation, order ownership, private guest tracking and availability repair.
- [x] Additive/reviewed migration generated; owner's database not migrated; automatic Down refused.
- [x] Setup, API contracts, calculation rules, retry behavior and rollback risks documented.
- [ ] Record final restore/build and complete SQLite/SQL Server integration results.
- [ ] Record executed Chromium/Firefox and Windows standalone verification with screenshots/artifacts.
- [ ] Publish/update the Phase 3 draft PR and record final matching-head CI evidence.
- [ ] Owner review before any merge, existing-database migration or deployment.
- [ ] Configure real coverage/rates, tax/invoice policy, COD collection/reconciliation and refusal handling.
- [ ] Decide shipped cancellations/returns/refunds, notification delivery and tracking/PII retention/revocation.
- [ ] Production email verification, backups/monitoring, durable/shared Data Protection, client rate limiting,
  framework support upgrade, full accessibility, Safari/physical-device and load testing.

Staff permissions, online gateways, international delivery, currency conversion, multi-vendor features
and a major visual redesign remain excluded. Phase 3 ends with owner review, not automatic launch.
