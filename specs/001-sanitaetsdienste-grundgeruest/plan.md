# Implementation Plan: Grundgerüst Sanitätsdienste

**Branch**: `001-sanitaetsdienste-grundgeruest` | **Date**: 2026-11-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-sanitaetsdienste-grundgeruest/spec.md`

## Summary

Deliver server-side Identity without self-registration, administrator-managed roles and first-password invitations, email-based password recovery, the event lifecycle and authorization rules, and a MudBlazor navigation shell. Preserve the existing Aspire-hosted Blazor Web App. Add inward-dependent Domain, Application, and Infrastructure projects; host REST endpoints and the server-rendered Identity pages in the existing `SanApp` server; make `SanApp.Client` consume only those endpoints; use Aspire-managed PostgreSQL and EF Core migrations. The design meets the specified one-hour maximum data-loss objective and validates the 50-user API performance target. The key domain decisions are recorded in [research.md](research.md), [data-model.md](data-model.md), and [contracts/rest-api.md](contracts/rest-api.md).

## Technical Context

**Language/Version**: C# / .NET 11 (`net11.0`, matching all existing projects)

**Primary Dependencies**: ASP.NET Core Identity (Individual Accounts, cookie auth), ASP.NET Core REST endpoints and ProblemDetails, EF Core/Npgsql with Aspire PostgreSQL integration, Cortex.Mediator, FluentValidation, MudBlazor, xUnit, Moq, bUnit. CQRS uses Cortex.Mediator commands, queries and their typed handlers in Application.

**Storage**: PostgreSQL managed as an Aspire AppHost resource; EF Core migrations; Identity, event, server-side drafts and operation evidence persisted through Infrastructure. Development and production migrations use a durable fail-closed gate in a separately provisioned PostgreSQL database independent of the application database.

**Testing**: xUnit/Moq for Domain and Application unit tests; HTTP integration tests for all REST endpoints and authorization; bUnit for UI component states and interactions. Integration tests must also prove mediator registration and command/query dispatch resolve the Application handlers, propagate cancellation, and enforce server-side validation. Exercise role grants and revocations in existing sessions, protected pages/components and APIs, concurrent role and event changes against real PostgreSQL, creation with every initial status, Berlin daylight-saving gaps and overlaps, and stale event update/status/delete requests. Add concurrent duplicate operation, crash/uncertain commit, deletion tombstone, access revocation, migration recovery, missing bootstrap secrets, same-tab reauthentication and non-disclosure tests; use browser tests for real SSR navigation and tab behavior.

**Target Platform**: Browser clients; ASP.NET Core server host on the existing Linux-container target; PostgreSQL container/resource locally through Aspire and a configured PostgreSQL service in deployment.

**Project Type**: Multi-project Blazor Web App with server host, WebAssembly client, Aspire AppHost and ServiceDefaults, plus Clean Architecture Domain/Application/Infrastructure projects.

**Performance Goals**: At up to 50 simultaneously signed-in users, p95 successful REST API request latency is at most 1 second and request error rate is below 1%, measured under a documented representative workload. For password-recovery requests, compare known and unknown accounts using at least 1,000 alternating requests per account type under the same topology and load; the larger-to-smaller p95 ratio must be at most 1.10. The specification defines no maximum recovery-time objective or backup-retention period.

**Constraints**: No public self-registration; no credentials in source control or client assets; cookie/Identity endpoints remain static SSR; all application data operations go through the REST API; server-side authorization is authoritative, and self-profile lookup derives the account from the authenticated principal rather than a caller-supplied account ID; only the roles and status transitions in the spec are allowed. A verified restore must lose no more than one hour of data.

**Scale/Scope**: Single SanApp solution for one operational organization; capacity target is 50 simultaneously signed-in users. Scope includes user/role administration and first-password invitations, email password recovery, event CRUD and lifecycle, protected visibility, API/UI shell, and future-module navigation placeholders.

## Constitution Check

*Architecture constitution check: conditional PASS in design only; secure cross-account draft deletion, recovery delivery and validation still require the release evidence below.*

- **Hosting/rendering**: Preserve Interactive Auto for application pages/components. Identity cookie-setting pages use static SSR as a framework constraint; this is a required exception to interactivity, not a governance deviation.
- **Clean Architecture / DDD**: Domain has no outer-project references; Application depends on Domain; Infrastructure implements Application/domain abstractions; the existing server host composes the API, Identity UI, and presentation. Client code calls HTTP API only.
- **REST / CQRS**: Cortex.Mediator commands mutate state, queries read state, and their handlers reside in Application. The server composition root registers Cortex.Mediator and scans the Application handler assembly; REST endpoints dispatch through the mediator and pass cancellation through.
- **UI**: MudBlazor is used for application surfaces and navigation; providers are registered within their interactive render-mode boundary. Identity SSR avoids interactive MudBlazor-only features.
- **Validation / errors**: FluentValidation guards commands/requests at the server boundary; invalid requests and domain errors use RFC 9457 ProblemDetails. Do not assume Cortex.Mediator enables FluentValidation by default: verify the chosen package version and explicitly wire and test validation for commands and queries.
- **Authentication / authorization / secrets**: Identity individual accounts, no self-registration, current server-side role checks for protected API requests and interactive page/component access, idempotent configured administrator bootstrap, secret-only credentials. Validate bootstrap email/password before writes; atomically persist roles, initial admin and completion marker. On incomplete bootstrap repair a matching account in-place, preserving its user ID, other roles and linked records; after completion never reset its password or roles on startup. Fail closed with actionable, secret-free diagnostics and allow controlled idempotent recovery. Existing sessions must reflect committed role changes at the next protected access without requiring re-login; stale cookies or circuit/client claims are not authoritative.
- **Quality gates**: xUnit/Moq, integration coverage for every API endpoint and role boundary, bUnit normal/edge/error UI scenarios, relevant builds and tests.

No Constitution exceptions or unjustified deviations are planned. This is a design check, not a claim that the pending implementation or its tests already comply.

## Project Structure

### Documentation (this feature)

```text
specs/001-sanitaetsdienste-grundgeruest/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── rest-api.md
└── tasks.md                 # Created by /speckit-tasks
```

### Source Code (repository root)

```text
SanApp.Domain/                         # Event aggregate, role-independent domain rules
SanApp.Application/                    # Commands, queries, handlers, validators, abstractions
SanApp.Infrastructure/                 # EF Core/Npgsql, Identity persistence and migrations
SanApp/
├── SanApp/                            # Existing server host: Identity SSR, REST API, composition root
└── SanApp.Client/                     # Existing WebAssembly client; REST API consumers and MudBlazor UI
SanApp.AppHost/                        # Existing Aspire host; PostgreSQL resource and SanApp reference
SanApp.ServiceDefaults/                # Existing shared Aspire service defaults
Tests/
├── SanApp.Domain.Tests/               # xUnit domain rule tests
├── SanApp.Application.Tests/          # xUnit/Moq CQRS and validation tests
├── SanApp.Api.IntegrationTests/       # xUnit HTTP, Identity, role and ProblemDetails tests
├── SanApp.Client.Tests/               # bUnit component and navigation tests
└── LoadTests/
    └── sanapp.js                      # k6 external CLI load acceptance for SC-009
```

**Structure Decision**: Keep the current AppHost, ServiceDefaults, server host and WASM client. Introduce three production projects to enforce the Constitution's inward dependency rule. Put API endpoints in the existing server host as selected by the user; do not create a second web host. Add test projects matching the required unit, HTTP integration and component test boundaries.

## Phase 0: Research

Research decisions and sources are documented in [research.md](research.md). The user selected PostgreSQL through Aspire, REST endpoints in the existing `SanApp` server, and Cortex.Mediator for commands, queries and handlers. The specification sets a one-hour maximum data-loss objective, a 50-user API performance target, and a measurable password-recovery timing comparison; it deliberately has no fixed recovery-time objective or backup-retention period.

The user confirmed the transition table, Berlin time-zone, stale-write behaviors, creation-operation rules, initial-password invitation lifecycle and recovery rules. The feature checklist is complete (38/38) and the requirements checklist is complete (16/16). The reviewer-owned feature gate T001 remains open independently of those checklist states. Package compatibility, durable recovery-store provisioning, backup/restore proof, load-test execution and implementation verification remain delivery gates, not established runtime behavior.

## Phase 1: Design & Contracts

- [data-model.md](data-model.md) defines Event, Identity user/role relationships, validation, lifecycle, visibility and editing/deletion invariants.
- [contracts/rest-api.md](contracts/rest-api.md) defines the HTTP surface, including `GET /api/users/me` derived from the authenticated principal, administrative user management, roles, error codes, validation payloads and the no-self-registration boundary.
- [quickstart.md](quickstart.md) defines local Aspire/PostgreSQL startup, safe Identity bootstrap configuration, migration, test commands and end-to-end acceptance scenarios, including own-account access boundaries, invitation delivery, recovery-point validation, the 1.10 password-recovery p95 ratio, and the 50-user load test.
- Initial-password invitations use one-hour, single-use, account-bound links. Pending accounts cannot sign in; administrator resend invalidates earlier links, and delivery failure leaves the account pending while clearly reporting failure to the administrator.
- Validate a restorable PostgreSQL backup/restore path against the one-hour maximum data-loss objective. No recovery-time or backup-retention target is imposed by the specification.
- Exercise up to 50 simultaneously signed-in users under a documented workload; calculate p95 only over expected successful REST API responses (<= 1 second) and count unexpected statuses, timeouts and transport errors in the < 1% request error rate. Record test environment and workload so results are reproducible.
- Interpret input and display in Europe/Berlin independently of the device zone. Reject spring gaps; require a choice for each autumn overlap. Validate the submitted local time and offset against server-side zone rules, store the selected instant in UTC and display overlaps distinctly. Compare actual instants, not local clock readings.
- Give every event an opaque version, refresh it on every persisted data or status change, and require it for update, status change and deletion. Check the version and lifecycle atomically; reject stale requests with ProblemDetails and offer a fresh authorized read without overwriting user edits. Test independent PostgreSQL connections and races across operations.
- Commit role-set replacement atomically using a shared PostgreSQL transaction lock for all role-set changes, including bootstrap; check minimum-role and last-administrator invariants after acquiring the lock. Reject stale user-role versions without modifying any assignment. Test racing requests using independent connections.
- Refresh authorization from current server-side assignments for each protected operation and page/component access, including existing interactive circuits; client-side visibility is advisory only. Propagate access changes to the interactive UI without requiring re-login and test both grant and revocation.
- Provide shared MudBlazor providers before the first interactive US1/US2 surface, not only in the navigation story. Prove foundation migration and mediator wiring with self-contained fixtures or move their checkpoints after the first real migration and handler exist.
- For creation POSTs authenticate, authorize, parse the request and validate the key first; atomically reserve or recognize the operation before invoking business validation. New attempts must still pass FluentValidation before execution; recognized repeats follow operation-specific rules without re-executing the command. Reserve an event key and its canonical comparable inputs in one transaction, including invalid business inputs, before validation; this permits comparison during processing or after rejection. Commit successful evidence with the resource; preserve a minimal tombstone atomically with event deletion. Never automatically retry uncertain operations. See [data-model.md](data-model.md#anlagevorgang) and [REST contract](contracts/rest-api.md#anlagevorgänge-und-unklare-ergebnisse).
- Use a separate deployment-provisioned PostgreSQL gate database, independent of the application database, shared by all hosts and the deployment migrator. Under an exclusive cross-instance gate lock durably record pending before migration; only confirmed success clears it. Failure, interruption, inaccessible gate or uncertain outcome blocks startup and further migration. An administrator inspects and repairs the database, then explicitly authorizes controlled recovery; authorization alone does not declare migration success. Do not delete data or retry automatically. Drain or stop production hosts before migration and restart only after confirmed completion. Provision, access control, independent availability, crash-persistent state, locking semantics and operator recovery procedure are prerequisites; an unavailable separate gate is a release blocker.
- Persist only allow-listed non-secret fields in authenticated server-side drafts. The user expressly accepts recovery of the last acknowledged server revision only; warn about unsynchronized changes. Enforce expiry after one hour since the last confirmed update; deny access at expiry and delete within five minutes while available, or within five minutes after recovery from an outage. Track submitted and later draft revisions separately: acknowledgement of a successful earlier save must not delete later edits. No credentials in draft storage and no automatic business submission.
- Reauthentication as another account in the same tab requires confirmed server-side deletion before issuing its authenticated session. A tab-specific, unguessable deletion-only capability, separate from the draft identifier and stored as a server-side hash, is proposed for this transition; the newly authenticated identity must not read or modify the prior draft. Bind it to the login transaction, protect against CSRF, replay, fixation, capability theft and cross-tab deletion; failed or uncertain deletion blocks login and offers retry. Verify the complete design in real browsers before release; the capability is a sensitive bearer secret, not an ordinary draft identifier.
- Implement FR-021 through Identity and an environment-configured email adapter. Use one-hour, single-use, account-bound reset credentials; atomically serialize issuance and consumption per account so newer issuance invalidates previous links and concurrent use succeeds at most once. Protect secrets from logs, referrers and client persistence. Enforce reset-related session invalidation at every protected HTTP and interactive access, not only periodic cookie checks. Make known global email outage reporting independent of account existence; recipient-specific asynchronous failures cannot be exposed only for existing accounts. Configure account-neutral abuse limiting without permanent lockouts; thresholds, provider, sender and operational secrets remain deployment prerequisites. See the [REST contract](contracts/rest-api.md#kennwortwiederherstellung) and [research](research.md#kennwortwiederherstellung-und-e-mail).
- Run a monitored background cleanup across instances with a controlled-clock test; alert on missed cleanup deadlines or unavailable storage without logging personal information. Demonstrate cleanup within five minutes of expiry or recovery and fail safely if its execution cannot be assured. Add HTTP integration tests for every new route, CQRS/domain tests, bUnit interaction tests and real-browser/multi-instance tests for account switches, spoofing, lost ACKs, concurrent later edits, reset races, enumeration, delivery failure and session revocation.
- Show German errors and dates as specified; distinguish successful empty lists from read failure; use uniform login failure and permission messages. Never log email addresses, passwords, reset links, capabilities or credentials, including framework/HTTP request logging; verify failure paths and redaction.

The updated [tasks.md](tasks.md) includes implementation and verification work for FR-019, FR-021, FR-022 and FR-023, including the self-profile access boundary and measurable recovery-timing test. The user explicitly deferred reviewer-owned T001 for implementation start on 2026-11-23; T001 remains open until the reviewer assesses the current specification and contract. This is not a positive review or a Constitution exception.

## Post-Design Constitution Check

Conditional PASS in design only: architecture boundaries and required test levels remain specified. The self-profile endpoint identifies the account from the authenticated principal; administrative enumeration remains separately protected. Release is blocked until the cross-account deletion authorization and login ordering are demonstrated securely, expiry and revision races are tested, email availability and account-neutral responses are verified, the FR-021 p95 comparison is measured, per-access session revocation is proven across HTTP and interactive circuits, the 50-user latency/error target is measured, and a PostgreSQL restore demonstrates the one-hour maximum data-loss objective. No recovery-time or backup-retention target is specified. FR-019 no longer promises recovery of unacknowledged edits; it explicitly requires a warning. The reviewer gate remains open; no Constitution exception is approved.

