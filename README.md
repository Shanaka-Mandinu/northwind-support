# Northwind Support Desk

A support ticket management system: raise tickets, search and filter them, assign them to agents,
and move them through their lifecycle.

**.NET 10 Web API** + **React 19 / TypeScript** + **SQL Server**, in one monorepo.

---

## The assignment

You are joining a team that already has this app working. Complete the tasks **in order**.
Full requirements are in **[`ASSIGNMENT_CANDIDATE.md`](ASSIGNMENT_CANDIDATE.md)**.

| # | Task | Done looks like | Effort |
| --- | --- | --- | --- |
| 1 | **Fix a defect, add server-side filtering** | Root cause fixed with a regression test; every list filter applied in the database with a correct count | ~2.5 h |
| 2 | **Ticket escalation & assignment** | Automatic triage on create, escalation with history, SLA status, UI for all of it | ~7 h |
| 3 | **Login and authentication** | Agents sign in; API rejects unauthenticated calls; login UI | ~4 h |
| 4 | **Dockerise the API** *(optional)* | `docker compose up` gives a migrated, seeded API with no baked-in secret | ~1.5 h |

### Guidance

- **Time: 2 days.** Honestly marking a task "not finished" beats rushing all of them.
- **Fork** this repo to your own GitHub account and work there. **Do not push to the original.**
- **AI tools are allowed.** You must understand and be able to defend every line you submit.
- **Commit as you go** and keep the real history. Commit the Task 1 defect fix and the filtering
  separately from each other and from the feature work. A single squashed commit is not accepted.
- **Where the spec is ambiguous**, make a decision, write it down in the README, and move on.
- **Out of scope:** registration, password reset, roles, OAuth/SSO, refresh tokens, MFA,
  notifications, CI, caching, visual redesign. Mention them under *Known limitations* instead.

### Submission

1. Update this **README** with what you built, your design decisions (chosen, rejected and why),
   the defect's root cause, dev login credentials, how to run and test it, AI usage, and known
   limitations. A reviewer must be able to run it without asking you anything.
2. Add an **APPROACH.md** explaining your approach and thought process for each task.
3. Commit both, then send us the **link to your fork**, the **dev login credentials**, and a
   **rough note of time spent** on what. (If the fork is private, add the reviewers as collaborators.)

---

## Quick start

**Prerequisites:** .NET SDK 10 (`global.json`), Node.js 22 (`.nvmrc`; 20.19+ works), Docker.

```bash
cp .env.example .env          # optional, every value has a working default
npm install                   # installs the web app
npm run db:up                 # SQL Server 2022 in Docker on localhost:1433
npm run api                   # terminal 1: migrates, seeds, then serves the API
npm run web                   # terminal 2: React dev server
```

| What | Where |
| --- | --- |
| Web app | http://localhost:5173 |
| API | https://localhost:7043 (also http://localhost:5043) |
| Swagger | https://localhost:7043/swagger |

In Development the API applies migrations and seeds demo data on startup. **Never commit real
credentials.**

**Visual Studio 2026:** run `npm run db:up`, open `SupportDesk.sln`, set
**SupportDesk.Presentation** as the startup project, and press F5 on the **https** profile.

## Tests

### Task 1: DEFECT-117

The list effect watched only search, page and page size, so a status or other filter change
on page 1 did not fetch. A later search change ran the effect with the previously selected
filters, making them appear to work intermittently.

The hook now fetches whenever its effective query changes. Only search updates wait 300 ms;
all other filters, sorting and paging apply immediately through the same generic path, which
also supports a future SLA filter. While search is pending, dropdowns use the last settled
search; when typing settles, the latest search is combined with the current filters on page 1.
Reset cancels pending search. A request sequence prevents older responses or errors from
replacing newer results and invalidates responses on unmount.

Verification: the request-level tests in `useTicketList.test.ts` capture `fetch` URLs, including
status-only changes, rapid typing, paging, clearing/resetting search and out-of-order responses.
On the original hook 14 of the 15 tests failed; all pass with the fix. Run
`npm run test --workspace apps/web`, `npm run typecheck` and `npm run lint`.
For a manual check, open browser Network on `/tickets`: changing Status sends a request
immediately; typing sends one request after settling.

### Task 1: database filtering

`TicketQueries.ApplyFilters` composes `IQueryable.Where` predicates before `CountAsync`,
sorting and paging. Status, priority, category, customer and agent match exactly; unassigned
means a null agent. Search is a trimmed literal substring of title, reference or customer name;
blank search is ignored. All supplied filters combine with AND, so an agent plus
`unassignedOnly=true` returns no tickets. Search case sensitivity follows the database collation.
Reads remain untracked with narrow projections. No schema changes or new indexes were needed;
existing filter indexes remain in use as available to the query planner. Substring search can
scan at scale; full-text search is a future decision, not part of this task.

Task 2 adds `slaStatus` through a SQL-translatable projection and a predicate in `ApplyFilters`;
the count/sort/page pipeline is unchanged. The frontend adds its typed field, API parameter and
control without special fetching-effect wiring.

`TicketQueriesTests` runs 16 cases against in-memory SQLite by default, including exact matches,
combinations, customer/title/reference search, literal wildcard characters, null assignment,
empty results and paging. The same 16 cases also passed against SQL Server. A command interceptor
verified that both executed queries contain WHERE, and the page query applies it before ORDER BY
and OFFSET/FETCH; the count has no paging. Detailed test output prints those SQL statements.
HTTP checks against the updated API and existing demo data also returned 14 High tickets
(7 pages at size 2), 4 High/InProgress tickets, 7 Contoso tickets and 4 unassigned tickets.
To repeat against local SQL Server in PowerShell:

```powershell
$settings = Get-Content apps/api/src/SupportDesk.Presentation/appsettings.Development.json -Raw | ConvertFrom-Json
$env:SUPPORTDESK_TEST_SQLSERVER = $settings.ConnectionStrings.Default
dotnet test apps/api/tests/SupportDesk.UnitTests --filter FullyQualifiedName~TicketQueriesTests --logger 'console;verbosity=detailed'
Remove-Item Env:SUPPORTDESK_TEST_SQLSERVER
```

This optional SQL Server run creates and deletes uniquely named test databases; it never uses
the application's database for test data. The login needs database-creation permissions.
The complete suite passed as 42 backend unit/query tests, 4 architecture tests and 16 frontend
tests. An already running API locked Debug DLLs on Windows, so the full backend suite was
verified with `dotnet test --configuration Release --no-restore`. Stop the API before `npm test`
if that happens. Existing NuGet vulnerability warnings for `System.Security.Cryptography.Pkcs`
9.0.11 remain; upgrading dependencies is outside this Task 1 change. Browser automation was not
available; frontend regression tests inspect actual request URLs through a mocked fetch boundary.

```bash
npm test                      # dotnet test, then the web tests (Vitest)
```

Both pass on a clean checkout and neither needs a database.

### Task 2: Ticket escalation & assignment

Task 2 implements the shared triage rules that drive both ticket creation and escalation.
The core design is a single `TriagePolicy` + `SlaSettings` model used everywhere: priority,
SLA windows, specialist checks, and assignment selection all resolve through the same logic.
That keeps automatic triage, manual assignment, and escalation consistent instead of duplicating
business rules in separate handlers.

Key implementation details:

- `POST /api/tickets` now applies priority, due-date calculation, and agent assignment as part of
  the creation flow. Categories with `ForcesCriticalPriority` always become `Critical`, and a
  category requiring a specialist can only be assigned to an eligible specialist.
- Assignment uses a deterministic selection rule: among active agents with capacity and required
  specialization, choose the fewest open tickets; ties select the lowest agent ID.
- If no eligible agent exists, the ticket is still created with HTTP 201 and an explanatory
  triage reason instead of failing the request.
- `POST /api/tickets/{id}/escalate` raises priority one step, recomputes due date from the
  escalation moment, keeps the current agent if still eligible, otherwise reassigns using the same
  triage rules, and writes an immutable escalation history row in the same transaction.
- Escalations record a fresh SLA start time so the derived `AtRisk` state is evaluated relative
  to the escalation window, not the original creation time.
- `GET /api/tickets?slaStatus=...` is implemented as a database-translatable predicate. The SQL
  layer computes `NotApplicable`, `WithinSla`, `AtRisk`, `Breached`, and `Met` without calling the
  in-memory evaluator inside a `WHERE` clause.
- The UI reflects the shared SLA badge/state, exposes the SLA status filter, shows triage details
  after create, and surfaces an escalation form with history and validation.
- A final manual-assignment gap was fixed so the assignment endpoint also validates the same
  specialist/capacity rules as triage and escalation instead of bypassing them.

Verification and coverage:

- Backend unit tests cover SLA windows, forced-critical categories, eligibility rules,
  deterministic assignment, escalation history, and boundary conditions at the `AtRisk` threshold.
- The SQL provider tests confirm the `slaStatus` filter is applied before paging and count logic,
  with the same behavior across the SQLite and SQL Server execution paths.
- The included `spec/task2-smoke.mjs` script validates create, escalation, specialist-capacity
  behavior, 400/404/409 responses, and the database-side SLA filtering workflow against a fresh
  disposable database.

Configuration and persistence decisions:

- Edit the existing `Sla` section in `appsettings.Development.json`, or supply process environment
  variables such as `Sla__AtRiskThresholdPercent=25`. Restart the API after changing settings.
  `dotnet run` does not automatically load the root `.env`; that file supplies Docker Compose.
- The `TicketEscalationAndSlaStart` migration adds `TicketEscalations` and nullable
  `Tickets.SlaStartedAtUtc`. Existing tickets fall back to creation time; escalation sets a new
  start. SLA status itself is never stored. Development startup applies the migration automatically.
- The `(TicketId, EscalatedAtUtc DESC, Id DESC)` index supports newest-first history for one ticket,
  with a deterministic tie-break. Agent foreign-key indexes support the before/after relationships.
  Restrict deletes preserve history. Private setters and DbContext save guards prevent application
  updates/deletes of history; privileged direct SQL is outside that protection.
- Eligibility uses the current open count, including the ticket being escalated. An owner at their
  limit is ineligible even when retaining the same ticket. Reasons and actors are trimmed before
  checking the 5–500 and 1–100 character limits, respectively.

Known limitations: assignment capacity and escalation are not locked across concurrent requests;
production would need a concurrency strategy. Escalation actors now come from the authenticated
token. SLA states refresh on data requests, not a live timer. Browser automation was
unavailable; component tests and real API checks cover behaviour but do not replace a visual review.
The optional SLA report is deferred until Tasks 1–3 are complete.

## Task 3: Authentication

Start the database and restart the API with `npm run api`, then run `npm run web`.
Development startup applies `AddAgentPasswordHash` and fills missing passwords for the six
known demo agents, including an existing Task 2 database; no reset is needed and existing
password hashes are preserved. Sign in at `/login` with:

- Email: `alex.turner@northwind-support.example`
- Password: `NorthwindDev!2026` (local development fixture only)

The same fixture password works for the other active seeded agents; inactive Yuki cannot log in.
Credentials live on `Agent` because an account is an agent in this assignment. The migration adds
`PasswordHash`; ASP.NET Core Identity's vetted password hasher provides salted PBKDF2 hashes.
No plaintext password is stored in the database or returned by the API.

`POST /api/auth/login` returns a signed HS256 JWT, its UTC expiry, and the agent's ID/name/email.
Wrong passwords, unknown emails and inactive agents receive the same 401 response.
`GET /api/auth/me` returns the current active agent. A central fallback authorization policy
protects API endpoints, including future ones; login and development Swagger remain public.
Escalation accepts only `reason`; its actor is recorded as `Name (agent ID)` from validated claims.

Tokens last **120 minutes** with no expiry grace period. Configure `Authentication__Jwt__Key`,
`Authentication__Jwt__Issuer`, `Authentication__Jwt__Audience`, and
`Authentication__Jwt__ExpiryMinutes` as process environment variables (see `.env.example`).
The development configuration contains an explicitly public fixture key, not a production secret.
There is no C# key fallback; startup rejects a missing/short key, missing issuer/audience, or invalid
lifetime. `dotnet run` does not automatically import the root `.env` file.

The browser keeps the token and agent in `localStorage` so reloads preserve login. This exposes
the token to JavaScript and therefore XSS; a production design should consider HttpOnly cookies
with appropriate CSRF protection. The shared API client attaches bearer tokens and clears rejected
sessions on 401. Route guards preserve the requested path, query and fragment. Expiry and logout
redirect to login, including across tabs. An old request cannot clear a newer login session.

Verification: backend tests cover real hashing, authentication decisions, JWT expiry/configuration,
and non-destructive seed upgrades. Frontend tests cover login, route guards, malformed/expired
sessions, bearer headers, 401 redirects and logout. Run `npm test`, `npm run typecheck`, and
`npm run lint`. For real HTTP checks, point `TASK3_API_URL` at a disposable development API and run
`node spec/task3-smoke.mjs`; it creates/escalates a ticket. Both this script and the authenticated
Task 2 smoke script passed against SQL Server. A production web build also passes.

Known limitations: logout removes the browser token but does not revoke issued JWTs. Deactivating
an agent prevents login and `/me` access; other endpoints accept an already-issued token until
expiry. Production needs a private signing key, HTTPS, a revocation strategy, and consideration of
rate limiting/lockout. Registration, password reset, refresh tokens, MFA and roles are intentionally
outside this task. Task 4 (Dockerising the API) remains unfinished.

## Commands

| Command | What it does |
| --- | --- |
| `npm run db:up` / `db:down` / `db:reset` | Start / stop / wipe SQL Server |
| `npm run api` / `npm run web` | Run the API / the React app |
| `npm run build` | Build both |
| `npm run lint` / `typecheck` / `format` | Web lint and types / `dotnet format` |
| `npm run ef -- migrations add <Name>` | Add an EF Core migration |

## API at a glance

| Endpoint | Description |
| --- | --- |
| `GET /api/tickets` | Paged list with `search`, `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `sortBy`, `sortDirection`. Filters run in SQL before counting and paging. |
| `GET /api/tickets/{id}` | One ticket |
| `POST /api/tickets` | Create and triage a ticket, returning decision reasons |
| `POST /api/tickets/{id}/escalate` | Escalate with `reason`; actor comes from the token; returns updated ticket and history entry |
| `GET /api/tickets/{id}/escalations` | Escalation history, newest first |
| `PATCH /api/tickets/{id}/status` · `/assignment` | Change status / assignee |
| `GET /api/customers` · `/api/agents` · `/api/categories` | Reference data |

Errors are `ProblemDetails`: `400` validation, `404` not found, `409` conflict or business rule.

**Seed data:** 5 categories, 6 customers (3 Premium), 6 agents (one inactive, one at their limit)
and 40 tickets across every status, priority and SLA state.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| API cannot reach the database | `npm run db:up`, wait ~20 s, check `docker compose ps` |
| Port 1433 already in use | Change the port in `docker-compose.yml` and `.env` |
| Browser warns about the certificate | `dotnet dev-certs https --trust`, or use the web app, which proxies |
| Web app shows "Unable to load tickets" | The API is not running, or `VITE_API_PROXY_TARGET` points at the wrong port |
| SQL Server slow on Apple Silicon | Enable Rosetta in Docker Desktop → Settings → General |
