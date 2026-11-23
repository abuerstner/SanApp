# Implementation Plan: Grundgerüst Sanitätsdienste

**Branch**: `001-sanitaetsdienste-grundgeruest` | **Date**: 2026-11-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-sanitaetsdienste-grundgeruest/spec.md`

## Summary

Deliver server-side Identity without self-registration, administrator-managed roles, the event lifecycle and authorization rules, and a MudBlazor navigation shell. Preserve the existing Aspire-hosted Blazor Web App. Add inward-dependent Domain, Application, and Infrastructure projects; host REST endpoints and the server-rendered Identity pages in the existing `SanApp` server; make `SanApp.Client` consume only those endpoints; use Aspire-managed PostgreSQL and EF Core migrations. The key domain decisions are recorded in [research.md](research.md), [data-model.md](data-model.md), and [contracts/rest-api.md](contracts/rest-api.md).

## Technical Context

**Language/Version**: C# / .NET 11 (`net11.0`, matching all existing projects)

**Primary Dependencies**: ASP.NET Core Identity (Individual Accounts, cookie auth), ASP.NET Core REST endpoints and ProblemDetails, EF Core/Npgsql with Aspire PostgreSQL integration, Cortex.Mediator, FluentValidation, MudBlazor, xUnit, Moq, bUnit. CQRS uses Cortex.Mediator commands, queries and their typed handlers in Application.

**Storage**: PostgreSQL managed as an Aspire AppHost resource; EF Core migrations; Identity and event data persisted through Infrastructure. Development applies migrations after the database is ready; production uses a controlled deployment migration step.

**Testing**: xUnit/Moq for Domain and Application unit tests; HTTP integration tests for all REST endpoints and authorization; bUnit for UI component states and interactions. Integration tests must also prove mediator registration and command/query dispatch resolve the Application handlers, propagate cancellation, and enforce server-side validation.

**Target Platform**: Browser clients; ASP.NET Core server host on the existing Linux-container target; PostgreSQL container/resource locally through Aspire and a configured PostgreSQL service in deployment.

**Project Type**: Multi-project Blazor Web App with server host, WebAssembly client, Aspire AppHost and ServiceDefaults, plus Clean Architecture Domain/Application/Infrastructure projects.

**Performance Goals**: No numeric latency or throughput objective is specified. Implement the interactive workflows without adding a performance target beyond the current feature scope.

**Constraints**: No public self-registration; no credentials in source control or client assets; cookie/Identity endpoints remain static SSR; all application data operations go through the REST API; server-side authorization is authoritative; only the roles and status transitions in the spec are allowed.

**Scale/Scope**: Single SanApp solution for one operational organization; no volume, concurrency, or availability targets specified. Scope is user/role administration, event CRUD and lifecycle, protected visibility, API/UI shell, and future-module navigation placeholders only.

## Constitution Check

*Gate before Phase 0: PASS. Rechecked after Phase 1 design: PASS.*

- **Hosting/rendering**: Preserve Interactive Auto for application pages/components. Identity cookie-setting pages use static SSR as a framework constraint; this is a required exception to interactivity, not a governance deviation.
- **Clean Architecture / DDD**: Domain has no outer-project references; Application depends on Domain; Infrastructure implements Application/domain abstractions; the existing server host composes the API, Identity UI, and presentation. Client code calls HTTP API only.
- **REST / CQRS**: Cortex.Mediator commands mutate state, queries read state, and their handlers reside in Application. The server composition root registers Cortex.Mediator and scans the Application handler assembly; REST endpoints dispatch through the mediator and pass cancellation through.
- **UI**: MudBlazor is used for application surfaces and navigation; providers are registered within their interactive render-mode boundary. Identity SSR avoids interactive MudBlazor-only features.
- **Validation / errors**: FluentValidation guards commands/requests at the server boundary; invalid requests and domain errors use RFC 9457 ProblemDetails. Do not assume Cortex.Mediator enables FluentValidation by default: verify the chosen package version and explicitly wire and test validation for commands and queries.
- **Authentication / authorization / secrets**: Identity individual accounts, no self-registration, role checks at API boundaries, idempotent configured administrator bootstrap, secret-only credentials.
- **Quality gates**: xUnit/Moq, integration coverage for every API endpoint and role boundary, bUnit normal/edge/error UI scenarios, relevant builds and tests.

No Constitution exceptions or unjustified deviations are planned.

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
└── SanApp.Client.Tests/               # bUnit component and navigation tests
```

**Structure Decision**: Keep the current AppHost, ServiceDefaults, server host and WASM client. Introduce three production projects to enforce the Constitution's inward dependency rule. Put API endpoints in the existing server host as selected by the user; do not create a second web host. Add test projects matching the required unit, HTTP integration and component test boundaries.

## Phase 0: Research

Research decisions and sources are documented in [research.md](research.md). The user selected PostgreSQL through Aspire, REST endpoints in the existing `SanApp` server, and Cortex.Mediator for commands, queries and handlers. Remaining unspecified numeric NFRs are intentionally treated as no target, not as unresolved architecture decisions.

## Phase 1: Design & Contracts

- [data-model.md](data-model.md) defines Event, Identity user/role relationships, validation, lifecycle, visibility and editing/deletion invariants.
- [contracts/rest-api.md](contracts/rest-api.md) defines the HTTP surface, roles, error codes, validation payloads and the no-self-registration boundary.
- [quickstart.md](quickstart.md) defines local Aspire/PostgreSQL startup, safe Identity bootstrap configuration, migration, test commands and end-to-end acceptance scenarios.

## Post-Design Constitution Check

PASS. The proposed structure separates Domain/Application/Infrastructure, keeps all persistence behind the API and Infrastructure boundary, reserves server enforcement for authorization, preserves the static-SSR Identity constraint, and maps all constitution-mandated test levels. PostgreSQL and the single server/API host align with user selections. No exception requires approval.

