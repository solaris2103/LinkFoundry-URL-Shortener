# Threat Model

## Assets

- Short-link mappings and destination URLs.
- Click and referrer analytics.
- Admin and management API access.
- System integrity and database migration path.

## Threats and mitigations

| Threat | Risk | Current mitigation | Residual risk |
| --- | --- | --- | --- |
| Open redirect abuse | Phishing or malicious destination abuse | HTTP(S)-only destinations, validation, no raw URL storage | Needs abuse monitoring and policy |
| API abuse | Traffic spikes and spam | Per-IP window limiter | Requires distributed rate limiting behind a load balancer |
| Unauthorized management access | Escalated actions | OIDC JWT validation and scope requirement | Requires real issuer registration and tenant authorization |
| SQL injection | Database compromise | Parameterized EF queries and DB abstraction | Low when using ORM correctly |
| PII leakage via referrer data | Privacy leak | Store only referrer host, not full URLs | Requires privacy retention policy |
| Misconfigured dev exposure | External access to local-only tools | Dev-only migrations and local defaults | Must never expose a development profile publicly |
| Broken migration safety | Data loss or runtime errors | Versioned EF migration and bundle generation | Human approval still required |

## Production review gates

Before public exposure, a human owner must verify external IdP configuration, tenant authorization, managed database backup/restore, trusted ingress, telemetry, load tests, and abuse monitoring.
