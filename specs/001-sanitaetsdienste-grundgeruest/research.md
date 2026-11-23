# Research: Grundgerüst Sanitätsdienste

## Entscheidungen

### Identity und Blazor-Render-Modi

- **Entscheidung**: ASP.NET Core Identity Individual Accounts wird serverseitig im bestehenden `SanApp`-Host mit Cookie-Authentifizierung eingerichtet. Die Blazor-Web-App behält Interactive Auto für geeignete angemeldete Anwendungsseiten. Identity-Konto-Seiten werden statisch serverseitig gerendert.
- **Begründung**: Die bestehende Lösung ist bereits ein Blazor Web App Host mit separatem WebAssembly-Client. Der ältere gehostete Blazor-WASM-IdentityServer-Ansatz ist seit .NET 8 abgelöst; Identity-Cookies werden sicher durch den Server verwaltet. Identity-Seiten benötigen statisches SSR, weil sie Cookies ausstellen.
- **Alternativen**: Separate Token-Ausgabe/JWT für WASM oder eine eigenständige IdentityServer-Lösung. Für die bestehende same-origin gehostete Anwendung unnötig komplex und mit mehr Authentifizierungsfläche verbunden.
- **Quellen**: [ASP.NET Core Identity: Blazor Web App mit Authentifizierung](https://learn.microsoft.com/aspnet/core/security/authentication/identity?view=aspnetcore-11.0#create-a-blazor-web-app-with-authentication); [Blazor-Authentifizierung](https://learn.microsoft.com/aspnet/core/blazor/security/?view=aspnetcore-11.0); [Blazor-Render-Modi](https://learn.microsoft.com/aspnet/core/blazor/components/render-modes?view=aspnetcore-11.0).

### Keine Selbstregistrierung und Bootstrap-Administrator

- **Entscheidung**: Selbstregistrierung wird nicht bereitgestellt. Rollen und das initiale Administratorkonto werden idempotent über die Identity-Manager angelegt. Bootstrap-E-Mail und Kennwort kommen ausschließlich aus lokaler User-Secrets-Konfiguration oder dem Secret Store/Umgebungsvariablen der Zielumgebung. Bei fehlender Bootstrap-Konfiguration muss das erstmalige Setup mit einer klaren Fehlermeldung abbrechen, statt still ohne Administratorkonto fortzufahren.
- **Begründung**: Das erfüllt die Spezifikation, ohne Zugangsdaten in Quellcode oder versionierter Konfiguration abzulegen. Wiederholte Initialisierung erzeugt keine Duplikate.
- **Alternativen**: Fest eingebautes Standardkennwort (unsicher); manuelles SQL-Seeding (umgeht Identity-Validierung); anonymes Registrierungsformular (ausdrücklich ausgeschlossen).
- **Quellen**: [Identity in Blazor Web Apps](https://learn.microsoft.com/aspnet/core/security/authentication/identity?view=aspnetcore-11.0#create-a-blazor-web-app-with-authentication); [ASP.NET Core Secret Manager](https://learn.microsoft.com/aspnet/core/security/app-secrets?view=aspnetcore-11.0).

### Datenbank, Aspire und Migrationen

- **Entscheidung**: PostgreSQL wird als Aspire-verwaltete Ressource im `SanApp.AppHost` konfiguriert. `SanApp` bezieht die Datenbankverbindung über den benannten Aspire-Ressourcenbezug und verwendet EF Core/Npgsql für Identity und Veranstaltungsdaten. EF-Core-Migrationen sind das Schema-Änderungsverfahren. In Development werden Migrationen beim Start der einzelnen Serverinstanz nach Verfügbarkeit der Datenbank angewandt; Produktionsmigrationen sind ein kontrollierter Deployment-Schritt und laufen nicht unkoordiniert bei jedem Hoststart.
- **Begründung**: Der User wählte PostgreSQL über Aspire. Der vorhandene AppHost ist derzeit leer; es gibt weder Datenbankanbieter noch Connection String. Benannte Aspire-Ressourcen vermeiden fest codierte Verbindungsdaten und integrieren lokale Container in die bestehende Orchestrierung. Development-Startup vereinfacht den lokalen End-to-End-Test; kontrollierte Produktionsmigrationen vermeiden konkurrierende Schemaänderungen bei mehreren Instanzen.
- **Alternativen**: SQLite (lokal einfach, aber nicht die gewählte Aspire-Serverdatenbank); SQL Server über Aspire (technisch möglich, aber vom User nicht gewählt); direkte Connection Strings in appsettings (nicht umgebungs-/secret-sicher).
- **Quellen**: [Aspire PostgreSQL](https://learn.microsoft.com/dotnet/aspire/database/postgresql-integration); [Aspire EF Core PostgreSQL Integration](https://learn.microsoft.com/dotnet/aspire/database/postgresql-entity-framework-integration); [EF-Core-Migrationen](https://learn.microsoft.com/ef/core/managing-schemas/migrations/).

### Clean Architecture, REST und CQRS

- **Entscheidung**: `SanApp.Domain`, `SanApp.Application` und `SanApp.Infrastructure` sind getrennte Projekte mit Abhängigkeiten nach innen. Der bestehende `SanApp`-Server hostet REST-Endpunkte, Identity-UI, Cookie-Authentifizierung und die Blazor-Web-App. `SanApp.Client` ruft ausschließlich diese API auf. CQRS-Commands, Queries und ihre typisierten Handler werden in Application mit dem vom User gewählten NuGet-Paket `Cortex.Mediator` umgesetzt. Der Server registriert den Mediator per `AddCortexMediator` und scannt die Application-Assembly; Endpunkte dispatchen Commands und Queries über den Mediator.
- **Begründung**: Die Architekturkonstitution verlangt Clean Architecture, REST und CQRS; die Mediator-Bibliothek ist vom User ausdrücklich vorgegeben. Die bestehende Server-App kann als Web-/API-Composition-Root dienen, ohne einen zusätzlichen API-Host zu duplizieren. Getrennte Domain/Application/Infrastructure-Projekte machen die Abhängigkeitsrichtung prüfbar.
- **Alternativen**: API direkt im Client oder Zugriff auf Infrastructure aus UI (verstößt gegen Konstitution); separates `SanApp.Api`-Projekt (vom User nicht gewählt und für den einzelnen bestehenden Host nicht notwendig); eigene direkte Handler-Verdrahtung oder MediatR (nicht die vom User gewählte Mediator-Bibliothek).
- **Quellen**: [Cortex.Mediator Getting Started](https://cortex.buildersoft.io/mediator-design-pattern/getting-started/); [Cortex.Mediator auf NuGet](https://www.nuget.org/packages/Cortex.Mediator/2.2.0); [Microsoft .NET Microservices Architecture: CQRS und DDD](https://learn.microsoft.com/dotnet/architecture/microservices/); [ASP.NET Core API-Fehler und ProblemDetails](https://learn.microsoft.com/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-11.0).

### Cortex.Mediator und Validierung

- **Entscheidung**: `ICommand`/`ICommandHandler` und `IQuery`/`IQueryHandler` trennen Schreib- und Lesevorgänge; HTTP-Endpunkte dispatchen sie über den registrierten Mediator und reichen Cancellation weiter. Die konkrete, mit .NET 11 kompatible Paketversion wird bei der Implementierung geprüft und festgelegt. FluentValidation wird explizit serverseitig für Commands und Queries ausgeführt; Fehler werden weiterhin als ProblemDetails abgebildet. Integrationstests prüfen Registrierung, Handler-Auflösung, Dispatch, Cancellation und abgewiesene ungültige Anfragen.
- **Begründung**: Assembly-Scanning bindet Handler der Application-Schicht in den bestehenden Composition Root ein. Die Cortex-Getting-Started-Seite und die NuGet-Paketbeschreibung widersprechen sich bezüglich eingebauter FluentValidation-Unterstützung; deshalb darf deren Aktivierung nicht vorausgesetzt werden. Validierung und Fehlerabbildung müssen unabhängig davon nachweislich funktionieren.
- **Alternativen**: Standard-Behaviors ungeprüft als vollständige Validierung annehmen (kann unvalidierte Requests zulassen); nur UI-Validierung (serverseitig umgehbar); parallele eigene Mediator-Abstraktion (unnötige Duplizierung).

### Validierung, Fehler und UI

- **Entscheidung**: FluentValidation wird in Application für Commands/Requests und im API-Eingang erzwungen. API-Fehler verwenden ASP.NET-Core-ProblemDetails nach RFC 9457, einschließlich Validierungsfehlern. MudBlazor wird in interaktiven Ansichten und im gemeinsamen Layout eingesetzt; seine Provider müssen innerhalb desselben interaktiven Render-Modus wie die Komponenten leben. Identity-Seiten bleiben SSR und umgehen interaktive MudBlazor-Provider.
- **Begründung**: Die Konstitution schreibt FluentValidation, ProblemDetails und MudBlazor vor. Getrennte Behandlung der Identity-Seiten respektiert Blazors Cookie-/SSR-Anforderung und MudBlazors Render-Modus-Anforderungen.
- **Alternativen**: Nur Clientvalidierung (kann umgangen werden); ad-hoc-Fehlerantworten (inkonsistente API); statisch gerenderte interaktive MudBlazor-Provider (nicht unterstützt).
- **Quellen**: [ASP.NET Core API-Fehler und ProblemDetails](https://learn.microsoft.com/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-11.0#problem-details); [Blazor-Render-Modi](https://learn.microsoft.com/aspnet/core/blazor/components/render-modes?view=aspnetcore-11.0); [MudBlazor Installation](https://mudblazor.com/getting-started/installation).

### Tests

- **Entscheidung**: xUnit und Moq gemäß Constitution. Domain- und CQRS-Handler erhalten Unit-Tests; HTTP-Endpunkte erhalten Integrationstests; Razor-Komponenten erhalten bUnit-Tests. Tests werden auf separate Testprojekte aufgeteilt, soweit die Produktionsprojektgrenzen dies sinnvoll erfordern.
- **Begründung**: Die vorgeschriebenen Prüfungen decken fachliche Regeln, Autorisierung am tatsächlichen HTTP-Eingang und UI-Zustände ab.
- **Alternativen**: Nur Unit-Tests (keine Garantie der API-Authentifizierungs-/Autorisierungsgrenzen); UI-Snapshot-only (prüft Interaktionen und Fehlerzustände nicht ausreichend).
