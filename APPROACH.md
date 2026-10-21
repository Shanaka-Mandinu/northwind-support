# Implementation approach

## Task 1 — frontend defect

Reproduced the missing status request using the existing hook and captured outgoing URLs.
The cause was an incomplete effect dependency list, not the dropdown or API client. Wrote
request-level regression tests first and observed 14 failures against the original hook.

Separated displayed input from the effective request query so only search is debounced.
All other filter fields share one update path; a future SLA field needs no hook-specific
dependency wiring. Reset cancels pending input and older requests cannot overwrite newer
results. Rejected forced reloads and client-side filtering because they hide the defect.

AI assistance: Codex inspected the code, reproduced the problem, and drafted implementation,
tests and documentation. Verification includes running the tests before and after the fix,
TypeScript checking and ESLint. An initial implementation triggered the React effect-state
lint rule; loading state was moved to user actions and response updates use promise callbacks.
The candidate should review and understand the changes before submission.

## Task 1 — database filtering

Kept the existing projected read query and added composable predicates before count and paging.
This preserves SQL execution, avoids loading all tickets into memory, and gives the later SLA
filter one extension point. No extra abstraction or new index was justified for these filters.
AND semantics also apply to conflicting assigned/unassigned criteria. Search treats wildcard
characters literally and follows the database collation for case sensitivity.

Used the already available SQLite provider for fast relational tests, then ran the same cases
against SQL Server in isolated temporary databases. Inspected captured commands to verify the
filter is in both count and page SQL, ahead of pagination. The full suite ran in Release because
the user's running API locked Debug output files. No application database reset was needed.

AI assistance continued with the query implementation and test fixtures. Accepted the simple
conditional WHERE composition; rejected relying on mocks alone to claim SQL correctness.
Actual execution on both providers and SQL interception verified the generated queries.

## Tasks 2–4

Not implemented yet.
