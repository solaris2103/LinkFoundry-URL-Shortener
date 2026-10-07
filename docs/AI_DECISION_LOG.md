# AI Decision Log

## Decision record: SQLite date handling

**Task:** Support analytics and expiry comparisons consistently.

**AI proposal:** Use `DateTimeOffset`-based comparisons through the EF provider.

**Engineer review:** SQLite provider could not translate the query as expected; tests failed during integration validation.

**Decision:** Normalize persistence to UTC `DateTime` and preserve offset-aware input parsing at the API boundary.

**Result:** Service and API tests passed after rerunning the focused validation.

## Decision record: referrer storage

**Task:** Store referrer information for analytics.

**AI proposal:** Persist the full referrer URL.

**Engineer review:** This creates unnecessary privacy risk and path leakage.

**Decision:** Store only the validated referrer host.

**Result:** Analytics still work while complying with the documented privacy constraints.

## Decision record: analytics totals

**Task:** Implement all-time totals and 30-day summaries.

**AI proposal:** Derive all totals from the recent-window query.

**Engineer review:** This contradicts the all-time UI semantics.

**Decision:** Separate all-time totals from rolling 30-day daily/referrer windows.

**Result:** The UI labels, counts, and metrics align with the product contract.

## Operational principle

AI output is treated as a draft. The engineer is the final owner of correctness, security, data governance, and release acceptance.
