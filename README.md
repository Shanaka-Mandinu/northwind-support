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

To add `slaStatus` in Task 2, extend the query contract and append a SQL-translatable predicate
in `ApplyFilters`; the shared count/sort/page pipeline stays unchanged. Do not call the current
C# SLA evaluator inside a WHERE clause. The frontend needs a typed field, API parameter and
control, but no special fetching-effect wiring.

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
| `POST /api/tickets` | Create a ticket |
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
