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

## Task 2 — Ticket escalation & assignment

Task 2 was implemented as a shared business-rule layer rather than separate rules scattered across
handlers. The main design choice was to keep the policy in `TriagePolicy` and `SlaSettings` and let
both the create flow and the escalate flow call the same logic. That mattered because the rules for
priority, due dates, specialist checks, reassignment, and SLA status were all coupled to the same
source of truth.

The first rule was to separate pure domain decisions from database access. The domain model decides
which agent is eligible and which priority window applies; the application layer fetches candidate
agents and stores the escalation record. This keeps the logic easy to test and prevents the SQL layer
from depending on an in-memory SLA evaluator.

For ticket creation, the domain policy calculates the requested priority, applies forced-critical
categories, and selects the best agent among active candidates that have spare capacity and the
required specialization. The result includes a human-readable triage reason so the API can explain
why the ticket was unassigned or why a specific agent won the assignment.

For escalation, the same policy was reused to decide if the current ticket owner should stay in place
or if a new agent should be selected. The escalation handler raises the priority one step, recalculates
its due date from the escalation timestamp, and persists the before/after priority and agent changes in
an immutable history row. The escalation is done in a single transaction so the ticket update and the
history row are committed together.

A key behavioral detail was the SLA reset during escalation: the code stores a new SLA start time when
escalating, which means the derived `AtRisk` status is based on the current escalation window instead of
an older creation-time baseline. That was the required interpretation of the business rule and matched
the tests for precise threshold boundaries.

The final gap was manual assignment. The assignment endpoint originally bypassed the specialist and
capacity checks; that was fixed so it uses the same eligibility rule as automatic triage and escalation.
This prevents a manual override from undoing a valid automatic assignment or violating the category's
specialist requirements.

The database side was also made consistent with the domain model. The SLA status filter was built as a
SQL-translatable predicate in `TicketQueries`, so `GET /api/tickets?slaStatus=...` works before paging
and counting. This keeps the filter behavior in the database and avoids calling the domain evaluator in
`WHERE` clauses.

UI work then connected to the same backend contract: the SLA badge/status filter, triage details after
creation, and the escalation form/history panel all read from the same model and rules. The result is a
single user experience across the list, detail page, and escalation flow.

AI assistance was used to verify the implementation through the existing xUnit suite and the Task 2
smoke script, and to keep the decision-making transparent. The important constraint was to keep the
business rules centralized and test the exact boundary conditions instead of hard-coding one-off fixes.

## Task 3: Authentication

Kept the existing Agent credential model, Identity password hasher, migration, and authentication
service instead of introducing a second user system. Completed authorization with a central fallback
policy and explicit anonymous login; restored public development Swagger. Token creation uses the
shared clock and returns the exact expiry encoded in the JWT. Configuration is validated at startup.

The main upgrade issue was the seeder's early return when tickets already existed: migrated demo
agents would have empty password hashes. Development seeding now backfills only missing hashes
for known fixture agents and preserves existing credentials and ticket data. A regression test
covers this upgrade and repeated startup.

Escalation no longer accepts an actor from the form. The controller derives name and ID from the
validated token and passes that identity to the handler. The existing history column remains useful
as an audit snapshot, with no additional migration required.

The browser session is a small shared store with same-tab and cross-tab notifications. Route guards,
logout, token expiry and API 401 handling use it consistently. LocalStorage was retained for simplicity
and reload persistence; its XSS exposure is documented rather than claiming it is secure cookie storage.

AI assistance reviewed and improved the initial implementation and added behavioral checks. It found
that a long-lived test session overflowed the browser timeout limit; expiry scheduling now caps each
wait and checks again. The initial per-controller authorization and client-supplied escalation actor
were replaced. Verification included backend tests, frontend tests/types/lint/build, and both Task 2
and Task 3 HTTP smoke scripts against a disposable SQL Server database. No visual browser review
was performed. The candidate should review these choices and be able to explain them independently.

## Task 4

Not implemented yet.
