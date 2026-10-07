# Requirement Scenarios

The same service is used to demonstrate greenfield, brownfield, and ambiguity handling. Each scenario begins with a requirement, states an engineer-approved interpretation, sequences dependent work, and names an observable validation result. These are review exercises, not autonomous agent runs.

## 1. Greenfield: launch a URL shortener

**Initial requirement:** “Build a URL shortener with core APIs, analytics, and reliability features.”

**Normalized problem:** Deliver a local, runnable React/.NET vertical slice that accepts only absolute HTTP(S) destinations, returns a shareable short URL, persists links, redirects and records click events, exposes a useful analytics summary, and supports controlled deactivation. Explicitly exclude accounts, custom domains, billing, and production deployment.

**Decomposition and dependency order:**

1. Define link, click-event, create-request, and summary contracts; agree URL and code limits.
2. Add the unique link-code constraint and click/time index; use persistent SQLite for local runs.
3. Implement service rules and UTC timestamp handling before API routes depend on them.
4. Wire create/list/detail/delete/redirect/health endpoints, CORS, Swagger, and create rate limiting.
5. Build the React operator surface against those contracts.
6. Verify service behavior, then the complete HTTP flow; document setup and limits.

**Execution:** Implemented in `Shortener.Core`, `Shortener.Infrastructure`, `Shortener.Api`, `web`, and `Shortener.Tests`. The first SQLite integration run disproved the assumption that offset timestamps would work in range queries; persistence was changed to normalized UTC `DateTime`, retaining offset-aware request parsing.

**Validation:** `dotnet test` covers validation, conflicts, expiry/deactivation, event analytics, HTTP create/redirect/referrer/deactivate, and invalid HTTP input. `npm run build` covers TypeScript and production bundling. See `AI_ENGINEERING.md` for observed test results and toolchain findings.

## 2. Brownfield: add expiry and safe deactivation

**Starting point:** An existing shortener already has link persistence and redirect behavior, but needs expiring links and a way for an operator to stop a bad redirect without deleting its evidence.

**Interpretation:** Expiry is optional and must be future-dated at creation; an expired or inactive mapping behaves as not found for redirects and is shown as Expired in the UI. An expired custom code can be reclaimed for a new destination; old click history is cleared because it cannot safely be attributed to the replacement link. Deactivation is idempotent for an existing mapping, preserves click history, and is distinguishable in the operator UI. No background cleanup is required in this iteration.

**Decomposition:**

1. Extend the persistence/domain model and request/summary contract.
2. Enforce expiry during create and again at redirect time; do not trust the UI clock.
3. Add a soft-deactivate service operation and `DELETE` route.
4. Expose active state/expiry in list and analytics payloads.
5. Add tests for expired and deactivated redirects and verify click history remains.
6. Add a confirmation before the destructive-looking UI action.

**Execution:** Expiry is stored in UTC; deactivation flips `IsActive` rather than deleting the row. The UI requires confirmation. The behavior is covered by service tests and the HTTP integration test checks that a deactivated code returns `404`.

**Validation and review:** Check future/past boundaries in tests, confirm no click is added after a rejected redirect, inspect the SQL schema/index diff, and manually confirm the operator sees a disabled status. A real production rollout needs authorization because deactivation changes externally visible redirect behavior.

## 3. Ambiguous: “show link performance”

**Ambiguities to resolve:** What is a click? How much history? Are repeated requests included? What attribution data is safe? Does “total” mean all time or within the chart window? Is a missing referrer an error?

**Engineer-approved prototype assumptions:**

- Each accepted redirect request is one click; bots, retries, and previews are not filtered.
- Total clicks are all-time. Daily chart and top-five referrer hosts are scoped to a rolling 30 days.
- Referrer host is stored only when the header is a valid HTTP(S) URL. Paths/query strings and IP addresses are not persisted.
- Missing referrer is normal and excluded from host ranking.
- No retention deletion occurs yet; operators must treat raw event rows as sensitive activity data.

**Decomposition:**

1. Make click-event capture part of the redirect service’s success path.
2. Index by link and occurrence time.
3. Define separate all-time and recent-window query semantics.
4. Aggregate daily and referrer results; cap referrers at five.
5. Surface empty analytics explicitly rather than implying zero-quality data.
6. Verify one redirect changes totals and the correct referrer bucket.

**Execution and validation:** Click events capture UTC time and referrer host. Analytics keeps the all-time summary independent of the recent window. Integration tests make one redirect with a referrer and assert total and attribution; the UI has a specific no-click/no-referrer state.

**Unresolved before production:** Have product/privacy owners approve the click definition, bot policy, retention/deletion schedule, regional obligations, and access controls. Do not silently convert these assumptions into a production analytics policy.