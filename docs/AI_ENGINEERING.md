# AI-Assisted Engineering Record

## Operating model

**Owner:** Engineer using an AI coding assistant (Copilot-style). **Decision authority:** Engineer/reviewer. The assistant proposes code, tests, and documentation inside named tasks; the engineer validates the local behavior, reviews the resulting diff, decides whether to accept it, and owns correctness and release readiness. No task scheduler, autonomous issue execution, commit, deployment, or approval is part of this prototype.

## Requirement normalization

**Intent:** Demonstrate a working LinkFoundry URL-shortener vertical slice and a defensible AI-assisted engineering process.

**Constraints:** React + .NET, new project in Downloads, runnable without hosted infrastructure, engineer-led execution, visible API and analytics behavior, auditable quality evidence.

**Acceptance criteria:**

- A user can create, list, inspect, copy/open, and deactivate short links in the UI.
- The API validates HTTP(S) destinations, persists mappings, redirects only active/unexpired codes, and records accepted redirects.
- Analytics distinguishes all-time total from rolling 30-day daily/referrer summaries.
- Service and API behavior are tested; frontend type-check/build succeeds.
- Setup, architecture, three scenarios, risks, assumptions, limits, and AI change traceability are documented.

## Task prompt brief

Each implementation request was bounded before code generation. Representative brief:

> **Intent:** Implement the link redirect and analytics vertical slice. **Context:** .NET 8 minimal API, EF Core SQLite, existing Core link/click entities and service boundary. **Constraints:** accept only HTTP(S), record only a validated referrer host, UTC timestamps, inactive/expired links return 404, do not fetch destinations, keep analytics all-time total separate from the 30-day breakdown. **Acceptance:** service tests and an HTTP integration test prove redirect status/location, persisted click, referrer aggregation, and inactive behavior. **Validation:** run `dotnet test`; inspect the SQLite query translation and response contract. **Ownership:** report assumptions and risks; do not deploy or approve the change.

The engineer refined tasks when evidence disproved assumptions (notably SQLite time comparison and the browser's `pattern` syntax), then reran the narrowest relevant test/build check before proceeding.

## Execution tasks

| Task | Intent and constraints | Acceptance evidence | Engineer disposition |
| --- | --- | --- | --- |
| Scaffold solution | Create separate React/TypeScript and .NET 8 projects under Downloads; do not modify the source workspace. Respect installed runtimes. | Project paths verified; package restore succeeds. | Accepted. The installed Node 16 forced initial Vite 4 scaffolding; for security support, the delivered frontend is upgraded to Vite 7 and ESLint 10 and requires Node 20.19+. Node 20.20.2 was fetched into npm's cache for local verification without changing the system runtime. |
| Implement persistence and domain | Persist validated mappings and click events; use a unique code index and no raw referrer URL storage. | Service tests for invalid scheme, slug conflict, click attribution, expiry, and deactivation. | Accepted after test-guided correction. |
| Expose API | Small HTTP contract with create/list/analytics/deactivate/redirect/health; add security headers, size cap, strict CORS, source-IP throttling, OIDC JWT and scope policy. | WebApplicationFactory exercises real SQLite, legacy migration, anonymous denial, wrong-scope denial, and allowed-scope access. | Accepted; external issuer/tenant registration remains deployment-owned. |
| Build operator UI | React client with configurable OIDC PKCE sign-in and session-scoped token state; make link lifecycle/errors visible. | Node 20 `npm ci`, lint, production build, full npm audit; manual create/redirect/analytics/deactivate smoke check. | Accepted; local UI created `review-demo`, a redirect returned `302`, and analytics showed one click from `newsletter.example`. |
| Schema and delivery gates | Replace startup `EnsureCreated` with an idempotent, checked-in migration; generate deployment bundle and automate quality gates. | Legacy data preservation test; locked restores; CI builds migration bundle. | Accepted; production migration remains a human-approved deployment operation. |
| Document scenarios and risk | Show greenfield, brownfield, and ambiguous requirement execution, not just the happy-path result. | Three scenario narratives and explicit approval gates in this record. | Accepted. |

## Traceability: generated, edited, rejected

| AI-assisted output | Status | Engineer review / rationale |
| --- | --- | --- |
| Initial .NET template and Vite scaffold | Edited | Removed generated weather and counter demos; retained the solution boundaries, replaced the UI and API with the shortener workflow. |
| Initial latest-Vite scaffold | Rejected at first | The machine had Node 16.11, incompatible with current Vite. Scaffolded with Vite 4 to make the first prototype runnable, then upgraded to Vite 7 and used temporary Node 20.20.2 for validation. |
| `DateTimeOffset`-based SQLite analytics query | Rejected after integration evidence | SQLite provider could not translate the range predicate. Persist UTC `DateTime`, retain offset-aware API input, and rerun HTTP integration tests. |
| Separate in-memory test database mutation | Rejected after test failure | The test modified a different store than the service. Shared the exact context and reran the behavior test. |
| Analytics total derived from recent-window rows | Edited | This contradicted the all-time UI label. Total now uses a separate all-time database count; daily/referrer summaries remain 30-day. |
| Raw referrer retention | Rejected | Store only a validated referrer host to reduce query/path leakage. This is not a substitute for a formal privacy and retention review. |

## Validation gates

| Gate | Current evidence | Release interpretation |
| --- | --- | --- |
| .NET compile and service/API tests | Release `dotnet test`: 14 passed, 0 failed. Includes real SQLite HTTP tests, legacy-schema migration, production auth/scope policy, response security headers, creation throttling, expiry-aware statuses, and expired-code reuse with analytics reset. | Passed locally; reviewer still inspects coverage and diff. |
| Migration deployment artifact | `dotnet ef migrations bundle` built successfully. NuGet locked restore passed before and after bundle generation after normalizing host RID metadata. | Passed locally; pipeline upload is CI-configured but hosted CI itself was not run from this workspace. |
| React type check, lint, and production bundle | Under Node 20.20.2: clean `npm ci`, ESLint 10 `npm run lint`, and Vite 7.3.7 `npm run build` all passed. Desktop and 375px screenshots were checked before the auth enhancement. | Passed locally; repeat visual checks after integrating a real identity provider. |
| Dependency security | Full `npm audit --audit-level=moderate`: 0 vulnerabilities. `dotnet list ... package --vulnerable --include-transitive`: no vulnerable packages. NuGet package lock files and npm lockfile are checked in. | Passed against current configured package sources. |
| API abuse/security | HTTP(S)-only credential-free destinations, route-reserved custom codes, database uniqueness/retry, parameterized EF queries, 32 KiB request cap, response security headers, scope-protected admin routes, strict CORS, dev-only Swagger, create rate limit. | Prototype controls only; external issuer configuration, trusted ingress, tenant authorization, and human threat model required before exposure. |
| Performance/reliability | SQLite indexes code and `(Code, OccurredAt)`; create requests are bounded; readiness checks connection/migration state; CI emits a migration bundle. | No load, failover, restore, or multi-instance tests. Not evidence of production capacity. Define SLOs and run resilience tests before deployment. |

## Secure AI use and human approval

- Do not submit secrets, tokens, production customer data, or sensitive link/click records to an AI service.
- Treat generated code and dependency suggestions as untrusted until reviewed and tested. Pin intentional dependency versions; inspect lockfile/audit changes.
- Require an engineer to approve all output before merge. Require security/privacy/operations sign-off for auth, public exposure, redirect policy, retention, schema migration, and distributed throttling changes.
- Keep a reviewer-readable diff and test output. Do not accept broad edits outside the task boundary merely because generated code compiles.
- AI cannot approve a risk exception, change production data, publish a package, merge, or deploy.

## Limitations and trade-offs

- No external identity-provider registration, per-user/tenant ownership model, custom domains, bot filtering, click-retention job, distributed rate limiting, cache, load/failover/restore test, alerting, or production deployment is included.
- SQLite stores each click synchronously before redirect, prioritizing accurate local accounting over redirect latency. Write contention and file durability need reevaluation at scale.
- Rate limiting is process-local; forwarded headers are not trusted. Behind a reverse proxy, configure trusted proxy/network forwarding before relying on client-IP partitioning.
- Redirects can be abused for phishing even when destinations are HTTP(S). Public deployment needs abuse reporting, monitoring, policy, and possibly destination reputation controls.
- Production management APIs require a valid OIDC bearer token and configured scope, but the sample has no tenant/resource ownership checks. Never expose it until issuer registration, scope assignment, ingress, and privacy controls are reviewed.
- This execution record describes actual implementation decisions and local checks, not proof that an external AI model independently verified the system. Engineer review remains the controlling gate.