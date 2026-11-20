# SanApp Constitution

## Core Principles

### I. Hosting- und Rendering-Strategie
Interactive Auto ist der Standard-Interaktivitätsmodus und MUSS gezielt auf Seiten- und/oder
Komponentenebene angewendet werden. Für Prerendering und Latenz gelten derzeit keine festen
Grenzwerte; entsprechende Anforderungen MÜSSEN bei Bedarf in der jeweiligen Feature-Spec oder
im technischen Plan festgelegt werden.

### II. Architektur, Schichten und Datenzugriff
Die Solution MUSS Clean Architecture in Kombination mit Domain-Driven Design (DDD) umsetzen.
Domain-Modelle und Aggregate, Application-Logik, Infrastructure und Präsentationsschicht
MÜSSEN getrennt sein; Abhängigkeiten MÜSSEN ausschließlich nach innen zur Domain zeigen.
Die Blazor-UI MUSS Datenoperationen ausschließlich über eine REST-API ausführen und DARF
Persistenz- oder Infrastructure-Implementierungen nicht direkt aufrufen. API-Endpunkte und
Anwendungsfälle MÜSSEN CQRS mit getrennten Commands für Schreibvorgänge und Queries für
Lesevorgänge verwenden.

### III. UI-State- und Komponentenstrategie
State-Management-Techniken DÜRFEN abhängig von Komplexität und Aufgabenstellung eingesetzt
werden; ihre Wahl MUSS zum Anwendungsfall verhältnismäßig sein. MudBlazor ist das verbindliche
UI-Komponenten-Framework. Oberflächen, Formulare, Dialoge und Layouts MÜSSEN primär mit
MudBlazor-Komponenten und dessen Theming-System umgesetzt werden. Interaktive Bedienelemente
MÜSSEN per Tastatur nutzbar sein und aussagekräftige Beschriftungen und Semantik bereitstellen.

### IV. Code-Qualität, Testing und Fehlerbehandlung
xUnit ist das verbindliche Test-Framework und Moq das verbindliche Mocking-Framework. Alle
Domain-Aggregate und CQRS-Handler MÜSSEN Unit-Tests für den Happy Path, relevante Edge Cases
und Fehlerbehandlungen besitzen. Alle API-Endpunkte MÜSSEN durch Integrationstests abgesichert
sein. GUI-Komponenten MÜSSEN mit bUnit auf Normalfälle, Grenzfälle und Fehlerzustände getestet
werden. FluentValidation MUSS für die Validierung von Commands, Requests und Formulareingaben
verwendet werden. API-Fehler MÜSSEN als standardisierte ProblemDetails nach RFC 9457 / RFC 7807
zurückgegeben und vom Client ausgewertet werden. Änderungen MÜSSEN dem Repository-Muster folgen,
Nullable-Analyse erhalten und vor Abschluss erfolgreich gebaut sowie mit den relevanten Tests
verifiziert werden. Nicht automatisierbare Prüfungen MÜSSEN als solche ausgewiesen und manuell
verifiziert werden.

### V. Authentifizierung und Autorisierung
Authentifizierung und Autorisierung MÜSSEN ASP.NET Core Identity mit Individual Accounts /
Individual Identity verwenden. Geschützte Seiten, Komponenten und REST-API-Endpunkte MÜSSEN
explizit mit ASP.NET-Core-Autorisierungsmechanismen wie Policies, Rollen oder `[Authorize]`
abgesichert werden. Die Autorisierung MUSS serverseitig durchgesetzt werden und DARF nicht
allein von UI-Sichtbarkeit oder clientseitiger Logik abhängen. Geheimnisse DÜRFEN weder in
Versionskontrolle noch in Client-Code oder unsicheren Fehlermeldungen offengelegt werden.

## Verbindlichkeit und Ausnahmen
Diese Constitution ist der verbindliche und enge Architektur- und Qualitätsrahmen für alle
Spezifikationen, technischen Pläne, Aufgaben und Implementierungen dieser Solution. Alle
Artefakte MÜSSEN vor der Umsetzung im Constitution Check auf Konformität geprüft werden.
Abweichungen sind grundsätzlich unzulässig. Eine technisch oder fachlich zwingende Ausnahme
MUSS vor Umsetzung schriftlich begründet und ausdrücklich durch den User freigegeben werden.
Ohne diese vorherige Freigabe DARF die Abweichung nicht umgesetzt werden.

## Spec-, Plan- und Umsetzungsworkflow
Spezifikationen MÜSSEN die betroffenen Prinzipien und gegebenenfalls offene Festlegungen wie
Prerendering oder Latenz benennen. Technische Pläne und Aufgaben MÜSSEN die Architektur- und
Qualitätsvorgaben konkret abbilden. Reviews MÜSSEN die Einhaltung dieser Vorgaben und den
erforderlichen Testumfang prüfen. Änderungen MÜSSEN auf den angefragten Umfang begrenzt bleiben
und nicht zusammenhängendes bestehendes Verhalten erhalten.

## Governance
Änderungen an dieser Constitution MÜSSEN ausdrücklich vorgeschlagen, auf Auswirkungen und
Kompatibilität geprüft und vor ihrer Annahme dokumentiert werden. Die Versionsnummer folgt
Semantic Versioning: MAJOR für inkompatible Änderungen oder Neudefinitionen von Prinzipien,
MINOR für neue Prinzipien oder wesentliche Erweiterungen und PATCH für Klarstellungen und
nicht-semantische Korrekturen. Das Änderungsdatum MUSS bei jeder angenommenen Änderung
aktualisiert werden. Jede Prüfung von Specs, Plänen, Aufgaben und Implementierungen MUSS die
Konformität mit den anwendbaren Prinzipien bewerten. Genehmigte Ausnahmen MÜSSEN mit Umfang,
Begründung und Freigabe dokumentiert werden; sie ändern die Constitution nicht.

**Version**: 2.0.0 | **Ratified**: 2026-11-20 | **Last Amended**: 2026-11-20
