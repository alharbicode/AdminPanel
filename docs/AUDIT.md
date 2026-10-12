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
- Shipping, discounts and content pages have API CRUD only. New MVC screens and dashboard analytics are subsequent scope, not fabricated product workflows.
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

P0/P1 repairs and the P2 reproducibility work listed above are implemented. Remaining scope is the product decisions and missing secondary MVC screens described above and in README. No browser-driven visual QA, live deployment, or migration against existing production data is claimed. Image signatures are checked but files are not fully decoded or malware-scanned; old assets remain pending an explicit cleanup policy.
