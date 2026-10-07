# Architecture and Decisions

## Intent and boundaries

LinkFoundry is a single-process, engineer-operated prototype that turns a destination URL into a durable redirect and reviewable click metrics. It is not a public multi-tenant SaaS deployment. A human chooses the work, reviews diffs and test evidence, and owns release approval. The assistant only helps inside explicitly scoped tasks.

## Components

```mermaid
flowchart LR
  Engineer[Engineer / Reviewer] -->|creates, inspects, disables| React[React + TypeScript workspace]
  React -->|JSON over HTTP| API[ASP.NET Core minimal API]
  Browser[Visitor] -->|GET /{code}| API
  API --> Core[Shortener.Core contracts and entities]
  API --> Infra[Shortener.Infrastructure service and EF Core]
  Infra --> SQLite[(SQLite links and click events)]
  API -->|302 after click commit| Browser
  API -->|analytics and list| React
  Tests[xUnit service + HTTP integration tests] --> API
  Tests --> Infra
```

`Shortener.Core` contains the domain model and service contract. `Shortener.Infrastructure` implements validation, slug generation, analytics queries, and the EF Core SQLite model. `Shortener.Api` wires persistence, CORS, rate limiting, Swagger, and HTTP routes. `web` is a Vite React client. `Shortener.Tests` checks core behavior and the public HTTP flow.

## Request flow

1. The operator submits a destination, optional custom code, and optional expiry in React.
2. The API validates absolute HTTP(S) destinations, length, expiry, credential-free URLs, and custom-code syntax. Create requests are limited to 20 per minute per source IP per process.
3. The service allocates a cryptographically random 12-character code when no custom code was requested. A database unique index is the final collision guard; concurrent random-code conflicts retry with a new code.
4. Link data is stored in SQLite. The client receives the stable short URL.
5. A visitor requests `/{code}`. The service checks active/expiry state, records a UTC click and referrer host, then sends a temporary redirect. Unknown, inactive, and expired codes return `404`.
6. Analytics returns all-time click count plus daily counts and top referrer hosts over a rolling 30-day window. The UI keeps these scopes distinct.

## Key decisions and trade-offs

- **SQLite for a runnable prototype:** no external service is needed and real relational constraints are exercised in integration tests. It is not a horizontally scalable write store.
- **UTC `DateTime` persistence:** EF Core SQLite cannot translate the needed `DateTimeOffset` range comparison. API expiry accepts an offset-aware value, then the service normalizes it to UTC before persistence. Analytics comparisons remain database-side.
- **Event rows rather than only a counter:** retaining click timestamps enables daily trends and referrer aggregation. It increases storage without a retention/compaction job; the prototype only filters analytics to 30 days and does not delete old events.
- **Referrer host only:** raw referrer paths and query strings are not retained, reducing accidental personal-data collection. Click events still reveal activity and need a real retention/privacy policy.
- **Soft deactivation:** the original mapping remains inspectable and its clicks remain available; redirection stops immediately.
- **302 redirects:** the destination may change only by creating a new mapping; 302 avoids browsers caching a permanent redirect during prototype operations.
- **Explicit base URL and web origin:** do not infer public links from an untrusted `Host` header. Production values must be configured by deployment.
- **One-process, source-IP rate limit:** cheap abuse friction, not a distributed quota. The service intentionally does not trust forwarded IP headers by default; behind a proxy, operators must configure trusted proxy forwarding before applying client-IP policies.

## API and data contract

OpenAPI is available at `/swagger` in Development. `POST /api/links` accepts `destinationUrl` (required, absolute HTTP or HTTPS, max 2048 characters, no embedded user-info credentials), `customCode` (optional, 3-32 ASCII letters/digits/underscore/hyphen; case-sensitive; service route names are reserved), and `expiresAt` (optional future timestamp). Duplicate custom codes return `409`; malformed input returns a validation problem. An expired custom code may be reused: the mapping is updated in place and its prior click events are deleted to prevent attribution to the new destination. Manually deactivated, non-expired codes remain reserved. Random codes use 12 characters from a 62-character alphabet.

Tables:

- `Links`: unique code, canonical destination, UTC creation/expiry timestamps, active flag.
- `Clicks`: link code, UTC occurrence timestamp, optional referrer host. An index supports per-link time-window queries; a foreign key ties events to their mapping.

## Reliability and security controls

- Input validation is repeated at the API service boundary; client validation is convenience only.
- Destination schemes are restricted to HTTP(S); the service never fetches the destination itself, so this is not an SSRF feature.
- Random code generation uses `RandomNumberGenerator`; the database enforces code uniqueness.
- SQLite persistence survives API restarts on the same local disk. EF migrations are checked in; Development applies them at startup, while production requires a reviewed migration bundle run before app rollout. The first idempotent migration preserves the earlier prototype schema/data.
- Create rate limiting returns `429` and does not queue excess traffic.
- Production management routes require OIDC JWT validation and the exact configured `scope` or `scp` management permission. The React SPA uses Authorization Code + PKCE and session-scoped state; Development deliberately stays anonymous for local demos and must never be exposed publicly.
- Swagger is development-only; CORS allows only the configured web origin. API responses receive `nosniff`, `DENY` frame policy, referrer policy, and `no-store`; request bodies are capped at 32 KiB and Kestrel server headers are suppressed.
- `/health/live` checks process liveness. `/health/ready` (also `/health`) checks database connectivity and reports `503` when migrations remain pending.

## Quality gates and change control

The local gates are `dotnet test` (service rules plus API integration through real SQLite), `npm run build` (TypeScript plus Vite production bundle), `npm run lint`, and `npm audit`. CI uses locked NuGet/npm dependencies, fails on NuGet/npm security advisories, tests in Release, and publishes a migration bundle for reviewed deployment. Review generated changes as diffs; validate acceptance criteria; never pass secrets, production customer data, or access tokens to an AI tool. Require human security/privacy and operations approval before changing authentication, redirect policy, data retention, public exposure, migrations, or production rate limits. The assistant does not commit, deploy, or sign off.

## Scale-up path

For a real service, replace SQLite with a managed relational store, configure and test the organization's OIDC issuer/audience/scope, introduce reviewed migration bundles and restore-tested backups, make click capture asynchronous or use a bounded event pipeline after measuring redirect latency, add global quotas, define tenant ownership and abuse handling, and instrument database readiness, redirect latency, errors, and event loss. Each change needs load and failure testing; none is implied by this prototype.