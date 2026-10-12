# Solution audit and completion plan

Baseline: `afdeda5208a2dc75bf410771e47b3ff7a55c5aef` on `master`.
Development branch: `codex/audit-repair-adminpanel`.

## Priorities and acceptance criteria

1. **P0 - Restore a coherent build and data model.** The order model is in the global namespace, contains a duplicate OrderItem, and disagrees with every order migration and controller. MVC order models and Razor views also disagree. Restore the migration-backed entities, remove obsolete EF 1.x tooling, align EF packages, and repair view names/models. Acceptance: restore/build both applications; migration model consistency check passes.
2. **P0 - Secure administration.** API CRUD is anonymous, passwords are compared as plaintext, the signing key is committed, issuer/audience validation is disabled, and MVC relies on an unvalidated token cookie. Use hashed passwords, external secret configuration, validated JWTs and current user checks, protected MVC authentication cookies, authorization and antiforgery. Acceptance: invalid/expired/wrong-issuer tokens, blocked users, non-admin roles and missing antiforgery tokens cannot mutate data; valid admin login reaches the dashboard. Provide an explicit local bootstrap/reset command with no default credentials.
3. **P1 - Repair existing MVC/API workflows.** Product requests send JSON to form endpoints; order details endpoint is missing; order serialization/status bodies differ; product delete has the wrong model. Acceptance: MVC login, category CRUD, multipart product create/edit/delete, order list/details/status/cancel work against the real API pipeline, including prices/items and validation errors.
4. **P1 - Protect data integrity and errors.** Validate DTOs and references; reject invalid quantities, unavailable products and insufficient stock; enforce optimistic stock concurrency; avoid cascading removal of catalog/history; validate uploads; return safe errors. Acceptance: invalid or conflicting requests do not change inventory/data; cancellation restores inventory once; relational integration tests verify constraints and HTTP failures.
5. **P2 - Make development reproducible.** Document SQL Server setup, explicit migrations, admin provisioning, secret management, HTTPS endpoints and tests. Add CI and automated integration coverage. Acceptance: documented commands match actual projects/configuration; restore, build, tests and migration script checks have recorded results.

## Product decisions and boundaries

- The user confirmed **Admin only** administration for this repair; Staff permissions remain unspecified. Customer self-service is not implemented or inferred.
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

P0/P1 repairs and the P2 reproducibility work listed above are implemented. Phase 2 implements the previously missing secondary screens; remaining scope is tracked below. No browser-driven visual QA, live deployment, or migration against existing production data is claimed. Image signatures are checked but files are not fully decoded or malware-scanned; old assets remain pending an explicit cleanup policy.

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

### Phase 3 verification status

Final verification is in progress. The latest completed integration runs pass **57 tests on SQLite and 57 on SQL Server**. The first complete browser pass through all screens and CRUD workflows succeeded, but its loading-state assertion blocked on its own deliberately held navigation; the harness was corrected and the complete suite is being rerun. Windows migrations and administrator insertion have executed; the standalone HTTPS smoke check is not yet declared passed. Final CI links and counts will replace this status after verification.

### Remaining gaps

- Staff permissions, customer onboarding/checkout, shipping/discount/tax calculations, currency, fulfillment transitions and retained cancellation history still require product decisions.
- Category dropdowns still load all categories; product pickers are bounded. Legacy unpaged API collection endpoints remain for compatibility. Search follows each database's collation; no fuzzy search or large-scale performance claim is made.
- Browser checks cover Chromium/Firefox and selected desktop/phone widths. WebKit/Safari, physical mobile devices, screen-reader testing, full contrast/WCAG review and production infrastructure are not verified.
- Image decoding/malware scanning, orphan cleanup, shared Data Protection keys and distributed rate limiting remain operational follow-ups. Plan the separately scoped framework support upgrade.
- The local agent host lacks a usable .NET SDK; builds and full application tests execute in GitHub Actions. No production data, master merge or deployment is involved.
