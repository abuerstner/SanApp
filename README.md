# SanApp

## Datenbankmigrationen

Der AppHost stellt lokal getrennte PostgreSQL-Datenbanken für Anwendung und Migrations-Gate bereit. Beide Verbindungen müssen konfiguriert sein (`ConnectionStrings:sanapp` und `ConnectionStrings:sanapp-migration-gate`); in produktiven Umgebungen muss das Gate unabhängig von der Zieldatenbank dauerhaft und gemeinsam für alle Hosts und Migratoren bereitgestellt werden.

Development-Migrationen laufen beim Start durch ein PostgreSQL-Advisory-Lock. Vor der Migration wird der dauerhafte Gate-Zustand auf ausstehend gesetzt; nur bestätigter Migrationserfolg hebt ihn auf. Fehler, Prozessabbruch, unklarer Commit oder Gate-Ausfall blockieren den Start und weitere Migrationen. Es gibt keinen automatischen Wiederholungsversuch.

Produktionsmigrationen sind ein kontrollierter Deployment-Schritt. Hosts vor der Migration stoppen oder leeren und erst nach erfolgreicher Migration wieder starten:

```powershell
dotnet run --project SanApp\SanApp\SanApp.csproj -- --migrate-database
```

Bei ausstehendem Gate müssen Administratoren zuerst beide Datenbanken und den tatsächlichen Schemazustand prüfen und erforderliche Reparaturen abschließen. Erst danach darf ein Operator die ausdrückliche Freigabe erteilen; der Befehl löscht ausschließlich den Gate-Marker und führt keine Migration aus:

```powershell
dotnet run --project SanApp\SanApp\SanApp.csproj -- --authorize-migration-recovery
```

Anschließend Migration erneut als separaten kontrollierten Schritt ausführen. Niemals Gate- oder Anwendungsdaten automatisch löschen. Befehle und Zugangsdaten ausschließlich über geschützte Deployment-Konfiguration bereitstellen.

## API-Sitzungsschutz

Die REST-API verwendet das sichere ASP.NET-Core-Identity-Cookie. Nach Anmeldung ruft der Client `GET /api/antiforgery` ab und sendet den zurückgegebenen `requestToken` bei authentifizierten `POST`-, `PUT`-, `PATCH`- und `DELETE`-Anfragen im Header `X-CSRF-TOKEN` zusammen mit dem Cookie. Cookie und Antiforgery-Token sind ausschließlich über HTTPS verfügbar; fehlende oder ungültige Tokens werden als `400 application/problem+json` abgewiesen. API-Authentifizierungsfehler liefern `401` bzw. `403` ProblemDetails statt HTML-Redirects.

## Tests

Die Testprojekte verwenden .NET 11 und xUnit v3. Browserprüfungen im API-Integrationstestprojekt verwenden Playwright. Nach dem Build des Projekts werden die zugehörigen Browser einmalig installiert:

```powershell
dotnet build Tests\SanApp.Api.IntegrationTests\SanApp.Api.IntegrationTests.csproj
pwsh Tests\SanApp.Api.IntegrationTests\bin\Debug\net11.0\playwright.ps1 install
```

Die gesamte Testsuite wird mit `dotnet test SanApp.slnx` ausgeführt. Die fachlichen und Browserprüfungen werden im Rahmen der Feature-Aufgaben ergänzt.
