# Failure-Mode Validation

## Functional failure scenarios

| Scenario | Expected response | Status |
| --- | --- | --- |
| Invalid destination scheme | `400` | Covered by service/API tests |
| Duplicate custom code | `409` | Covered |
| Expired link access | `404` | Covered |
| Inactive link access | `404` | Covered |
| Rate limit exceeded | `429` | Covered |
| Wrong OIDC scope | `403` | Covered |
| Missing token | `401` | Covered |
| Migration not current | readiness `503` | Covered by readiness flow |

## Reliability assumptions

The prototype deliberately prioritizes local correctness and reviewability over distributed scale. Production rollout requires load tests, restore drills, and operational monitoring.
