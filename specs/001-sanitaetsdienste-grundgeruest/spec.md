# Feature Specification: Grundgerüst Sanitätsdienste

**Feature Branch**: `001-sanitaetsdienste-grundgeruest`

**Created**: 2026-11-23

**Status**: Draft

**Input**: User description: "Grundgerüst einer Blazor WebApp zur Verwaltung von Sanitätsdiensten (Veranstaltungen), Rollenmodell (Administrator, Sandienstverantwortlicher, Einsatzleitung, Helfer), ASP.NET Core Identity ohne Selbstregistrierung, Status- & Löschregeln inkl. 'Abgesagt', Pflichtfelder, Platzhalter-Navigation für Folge-Module; Personal-, Material-/Fahrzeugverwaltung und Bedarfsplanung in späteren Features."

## Scope & Abgrenzung

Dieses Feature etabliert das technische und fachliche Grundgerüst der Anwendung einschließlich Benutzer- und Rollenverwaltung, Veranstaltungen, Statussteuerung und Navigation. Es umfasst keine Personalverwaltung (Dienstpläne, Qualifikationen, Helfer-Zuteilungen oder Verfügbarkeiten), keine Material- oder Fahrzeugverwaltung (Bestände, Prüf- und Wartungsdaten oder Zuordnungen) und keine Bedarfsplanung. Diese Fachfunktionen werden in späteren Features spezifiziert. Die Navigation zeigt dafür lediglich erkennbare, nicht ausführbare Platzhalter.

## Clarifications

### Session 2026-11-23

- Q: Bis zu welchem Status dürfen Sandienstverantwortliche Veranstaltungsdaten außer dem Status bearbeiten? → A: Bis einschließlich „Geplant“; ab „Aktiv“ ist die Bearbeitung gesperrt.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Anmeldung und administrative Benutzerverwaltung (Priority: P1)

Ein initial eingerichteter Administrator meldet sich an, legt Benutzerkonten an und weist ihnen eine oder mehrere Rollen zu. Besucher können sich nicht selbst registrieren.

**Why this priority**: Authentifizierung und Rollen sind Voraussetzung für den Zugriffsschutz aller weiteren Funktionen.

**Independent Test**: Mit dem initialen Administratorkonto anmelden, je ein Konto mit Rollen anlegen, deren Zugriff prüfen und die öffentliche Registrierung sowie Benutzeranlage durch Nicht-Administratoren testen.

**Acceptance Scenarios**:

1. **Given** die Anwendung wird erstmals gestartet, **When** die Einrichtung abgeschlossen ist, **Then** existiert ein Administratorkonto, mit dem eine Anmeldung möglich ist.
2. **Given** ein nicht angemeldeter Besucher, **When** er die Anwendung oder einen Registrierungszugang aufruft, **Then** kann er sich nicht selbst registrieren und geschützte Bereiche bleiben unzugänglich.
3. **Given** ein Administrator ist angemeldet, **When** er einen Benutzer anlegt und eine oder mehrere der Rollen Administrator, Sandienstverantwortlicher, Einsatzleitung und Helfer zuweist, **Then** kann sich der Benutzer anmelden und erhält nur die zugewiesenen Berechtigungen.
4. **Given** ein Benutzer ohne Administratorrolle, **When** er Benutzerverwaltung oder Benutzeranlage aufruft, **Then** wird der Zugriff verweigert und es wird kein Benutzer angelegt oder geändert.

### User Story 2 - Veranstaltungen verwalten (Priority: P2)

Ein Sandienstverantwortlicher legt Veranstaltungen mit Name, Start- und Endzeitpunkt, Ort, Auftraggeber und Status an, bearbeitet ihre Daten bis einschließlich „Geplant“ und löscht sie, solange sie sich in „In Planung“ befinden.

**Why this priority**: Veranstaltungen sind der fachliche Anker für die späteren Module.

**Independent Test**: Veranstaltungen anlegen, bearbeiten und löschen; Pflichtfeld-, Zeitraums- und Statusregeln dabei sowohl bei gültigen als auch bei ungültigen Eingaben prüfen.

**Acceptance Scenarios**:

1. **Given** ein Sandienstverantwortlicher gibt alle Pflichtfelder gültig an, **When** er die Veranstaltung speichert, **Then** wird sie gespeichert und in der Übersicht angezeigt.
2. **Given** ein Pflichtfeld ist leer oder besteht nur aus Leerzeichen, oder das Ende liegt vor oder auf dem Start, **When** die Veranstaltung gespeichert werden soll, **Then** wird die Eingabe abgewiesen, verständlich erläutert und nicht gespeichert.
3. **Given** eine Veranstaltung hat den Status „In Planung“, **When** ein Sandienstverantwortlicher sie löscht, **Then** wird sie entfernt.
4. **Given** eine Veranstaltung hat den Status „Geplant“, „Aktiv“, „Abgeschlossen“ oder „Abgesagt“, **When** ein Sandienstverantwortlicher sie löscht, **Then** wird der Löschversuch abgewiesen und die Veranstaltung bleibt erhalten.
5. **Given** eine Veranstaltung hat den Status „In Planung“ oder „Geplant“, **When** ein Sandienstverantwortlicher ihre Daten außer dem Status bearbeitet, **Then** wird die Änderung gespeichert.
6. **Given** eine Veranstaltung hat den Status „Aktiv“, „Abgeschlossen“ oder „Abgesagt“, **When** ein Sandienstverantwortlicher ihre Daten außer dem Status bearbeitet, **Then** wird die Änderung verweigert und die Veranstaltungsdaten bleiben unverändert.
7. **Given** ein Benutzer ohne Rolle Sandienstverantwortlicher, **When** er eine Veranstaltung anlegt, bearbeitet oder löscht, **Then** wird die Aktion verweigert und der Datenbestand bleibt unverändert.

### User Story 3 - Status steuern und Sichtbarkeit rollenabhängig begrenzen (Priority: P3)

Sandienstverantwortliche und Einsatzleitungen ändern den Status nach den zulässigen Übergängen. Helfer können Veranstaltungen nur lesen und erhalten keinen Zugriff auf interne Planungsstände.

**Why this priority**: Statusregeln verhindern unzulässige Änderungen; die Sichtbarkeitsgrenze schützt interne Planungsinformationen.

**Independent Test**: Alle definierten Statusübergänge und Rollenaktionen prüfen; für jede Rolle die sichtbaren Statuswerte und direkte Zugriffe auf verborgene Veranstaltungen testen.

**Acceptance Scenarios**:

1. **Given** eine Veranstaltung befindet sich in einem Status, **When** ein Sandienstverantwortlicher oder eine Einsatzleitung einen laut festgelegter Statusregel erlaubten Wechsel ausführt, **Then** wird der neue Status gespeichert.
2. **Given** ein Statuswechsel ist laut Statusregel unzulässig, **When** er angefordert wird, **Then** wird er mit einem fachlichen Fehler abgewiesen und der Status bleibt unverändert.
3. **Given** ein Helfer ist angemeldet, **When** er die Veranstaltungsübersicht aufruft, **Then** sieht er Veranstaltungen mit den Statuswerten „Geplant“, „Aktiv“ und „Abgeschlossen“, jedoch weder „In Planung“ noch „Abgesagt“, und kann keine Veranstaltung ändern.
4. **Given** ein Helfer versucht, eine Veranstaltung mit Status „In Planung“ oder „Abgesagt“ über einen direkten Link oder Datenzugriff aufzurufen, **When** der Zugriff erfolgt, **Then** werden keine Veranstaltungsdaten offengelegt.
5. **Given** Administrator, Sandienstverantwortlicher oder Einsatzleitung ist angemeldet, **When** die Person die Übersicht aufruft, **Then** sieht sie Veranstaltungen aller Statuswerte; Schreib- und Statusaktionen bleiben auf die jeweils erlaubten Rollen beschränkt.

**Statusregel**: „In Planung“ darf zu „Geplant“ wechseln. Sobald eine Veranstaltung erstmals „Geplant“ erreicht, ist jeder Rückwechsel zu „In Planung“ unzulässig. Der normale Fortschritt erfolgt über „Geplant“ zu „Aktiv“ und anschließend zu „Abgeschlossen“. Eine Veranstaltung darf aus „In Planung“, „Geplant“ oder „Aktiv“ nach „Abgesagt“ wechseln. „Abgeschlossen“ und „Abgesagt“ sind Endzustände.

### User Story 4 - Platzhalter-Navigation für Folge-Module (Priority: P4)

Angemeldete Benutzer sehen im Hauptmenü die aktiven Grundgerüst-Bereiche sowie klar erkennbare Platzhalter für die späteren Module Personalverwaltung, Material- und Fahrzeugverwaltung sowie Bedarfsplanung.

**Why this priority**: Eine konsistente Navigation schafft Orientierung und macht die Abgrenzung zu Folge-Features sichtbar.

**Independent Test**: Für angemeldete Benutzer prüfen, dass alle aktiven Bereiche und drei Folge-Module als Platzhalter angezeigt werden und Platzhalter keine unfertigen Funktionen oder Datenzugriffe auslösen.

**Acceptance Scenarios**:

1. **Given** ein Benutzer ist angemeldet, **When** das Hauptmenü angezeigt wird, **Then** sind Personalverwaltung, Material- und Fahrzeugverwaltung sowie Bedarfsplanung als nicht ausführbare, eindeutig gekennzeichnete Platzhalter sichtbar.

### Edge Cases

- Name, Ort und Auftraggeber fehlen oder enthalten nur Leerzeichen.
- Start- oder Endzeitpunkt fehlt; Ende liegt vor oder exakt auf dem Start.
- Eine Veranstaltung erhält eine Statusänderung, die laut festgelegter Statusregel unzulässig ist.
- Eine Löschung wird für jeden Status außer „In Planung“ angefordert, insbesondere für „Abgesagt“.
- Einsatzleitung versucht eine Veranstaltung anzulegen, zu bearbeiten oder zu löschen.
- Ein Sandienstverantwortlicher versucht, Veranstaltungsdaten ab „Aktiv“ zu bearbeiten.
- Helfer versucht, einen Status zu ändern oder eine nicht freigegebene Veranstaltung direkt aufzurufen.
- Ein nicht angemeldeter Besucher versucht Selbstregistrierung oder Zugriff auf geschützte Bereiche.
- Die erstmalige Administratoreinrichtung wird wiederholt ausgeführt; dadurch dürfen keine doppelten Rollen oder Konten entstehen.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Das System MUSS Anmeldung und Autorisierung mit ASP.NET Core Identity (Individual Identity) bereitstellen.
- **FR-002**: Das System MUSS öffentliche Selbstregistrierung deaktivieren und bei der Ersteinrichtung ein Administratorkonto sowie die vier definierten Rollen idempotent bereitstellen. Zugangsdaten des initialen Kontos MÜSSEN über einen sicheren, nicht versionierten Weg bereitgestellt werden.
- **FR-003**: Ausschließlich Administratoren DÜRFEN Benutzerkonten anlegen und ihnen eine oder mehrere der Rollen Administrator, Sandienstverantwortlicher, Einsatzleitung und Helfer zuweisen.
- **FR-004**: Das System MUSS Veranstaltungen mit den Pflichtfeldern Name, Startzeitpunkt, Endzeitpunkt, Ort, Auftraggeber und Status unterstützen.
- **FR-005**: Name, Ort und Auftraggeber MÜSSEN nach Entfernung führender und nachfolgender Leerzeichen mindestens ein Zeichen enthalten. Start- und Endzeitpunkt MÜSSEN vorhanden sein; das Ende MUSS strikt nach dem Start liegen.
- **FR-006**: Veranstaltungsstatus MUSS die Werte „In Planung“, „Geplant“, „Aktiv“, „Abgeschlossen“ und „Abgesagt“ unterstützen. „In Planung“ darf zu „Geplant“ wechseln; nach dem erstmaligen Erreichen von „Geplant“ ist kein Rückwechsel zu „In Planung“ zulässig. Der normale Fortschritt führt von „Geplant“ zu „Aktiv“ und von „Aktiv“ zu „Abgeschlossen“. Aus „In Planung“, „Geplant“ oder „Aktiv“ darf nach „Abgesagt“ gewechselt werden. „Abgeschlossen“ und „Abgesagt“ sind Endzustände. Ungültige Wechsel DÜRFEN den gespeicherten Zustand nicht verändern.
- **FR-007**: Eine Veranstaltung DARF nur gelöscht werden, solange sie den Status „In Planung“ hat. Ab „Geplant“ einschließlich „Aktiv“, „Abgeschlossen“ und „Abgesagt“ MUSS das Löschen verweigert werden.
- **FR-008**: Ausschließlich Sandienstverantwortliche DÜRFEN Veranstaltungen anlegen. Sie DÜRFEN Veranstaltungsdaten außer dem Status nur in „In Planung“ und „Geplant“ bearbeiten. Sie DÜRFEN Veranstaltungen ausschließlich in „In Planung“ löschen.
- **FR-009**: Ausschließlich Sandienstverantwortliche und Einsatzleitungen DÜRFEN Veranstaltungsstatus ändern.
- **FR-010**: Helfer DÜRFEN Veranstaltungen ausschließlich lesen und keine Schreib- oder Statusänderungen ausführen. Sie DÜRFEN nur Veranstaltungen mit den Statuswerten „Geplant“, „Aktiv“ oder „Abgeschlossen“ sehen oder direkt abrufen. Veranstaltungen mit Status „In Planung“ oder „Abgesagt“ DÜRFEN Helfern nicht offengelegt werden.
- **FR-011**: Alle Commands, Requests und Formulareingaben MÜSSEN mit FluentValidation validiert werden. Validierungs- und fachliche Regelverletzungen MÜSSEN als standardisierte ProblemDetails nach RFC 9457 / RFC 7807 beantwortet und im Client nachvollziehbar dargestellt werden.
- **FR-012**: Die Benutzeroberfläche MUSS MudBlazor verwenden und Interactive Auto gezielt auf Seiten- und/oder Komponentenebene anwenden. Die Hauptnavigation MUSS die Platzhalter Personalverwaltung, Material- und Fahrzeugverwaltung sowie Bedarfsplanung anzeigen; Platzhalter DÜRFEN keine unfertigen Fachaktionen auslösen.
- **FR-013**: Datenoperationen der Blazor-Oberfläche MÜSSEN ausschließlich über eine REST-API erfolgen. API-Endpunkte und Anwendungsfälle MÜSSEN Commands und Queries nach CQRS trennen. Die Architektur MUSS Clean Architecture und DDD mit Abhängigkeiten ausschließlich nach innen zur Domain einhalten.
- **FR-014**: Autorisierung für geschützte Seiten, Komponenten und API-Endpunkte MUSS serverseitig durchgesetzt werden und darf nicht allein von der UI abhängen.
- **FR-015**: xUnit und Moq MÜSSEN für Tests und Test-Doubles verwendet werden. Alle Domain-Aggregate und CQRS-Handler MÜSSEN Unit-Tests für Erfolgs-, relevante Grenz- und Fehlerfälle besitzen; alle API-Endpunkte MÜSSEN Integrationstests besitzen; GUI-Komponenten MÜSSEN mit bUnit auf Normal-, Grenz- und Fehlerfälle geprüft werden.

### Key Entities *(include if feature involves data)*

- **Veranstaltung**: Ein geplanter oder durchgeführter Sanitätsdienst mit eindeutiger Identität, Name, Start- und Endzeitpunkt, Ort, Auftraggeber und Status.
- **Benutzer**: Eine Person mit Anmeldeidentität und einem oder mehreren der definierten Rollen.
- **Rolle**: Eine Berechtigungsgruppe aus Administrator, Sandienstverantwortlicher, Einsatzleitung oder Helfer.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In allen Abnahmetests kann der initial eingerichtete Administrator sich anmelden und Benutzerkonten mit den vorgesehenen Rollen erstellen; Selbstregistrierungsversuche werden zu 100 % abgewiesen.
- **SC-002**: 100 % der Testfälle mit fehlenden Pflichtfeldern, Whitespace-only-Feldern oder ungültigen Zeiträumen werden abgewiesen, ohne Veranstaltungsdaten zu speichern.
- **SC-003**: 100 % der getesteten unzulässigen Statuswechsel und Löschversuche ab „Geplant“ werden abgewiesen, ohne den vorherigen Datenzustand zu verändern.
- **SC-004**: In der vollständigen Rollen-Zugriffsmatrix können Helfer keine Schreibaktionen durchführen und keine nicht freigegebenen Veranstaltungsdaten abrufen.
- **SC-005**: Alle drei Folge-Module sind im Hauptmenü als Platzhalter erkennbar; keiner der Platzhalter führt eine nicht spezifizierte Fachaktion aus.
- **SC-006**: Alle definierten Abnahmeszenarien für Benutzerverwaltung, Veranstaltungen, Status, Zugriffsrechte und Navigation bestehen vor der Freigabe.

## Assumptions

- Der konkrete Benutzername und das initiale Administratorkennwort werden nicht in Quellcode oder Spec festgeschrieben; die Bereitstellung erfolgt über einen sicheren, umgebungsspezifischen Kanal.
- Rollen können einem Benutzer einzeln oder mehrfach zugewiesen sein; Rechte ergeben sich aus der Vereinigung der ausdrücklich zugewiesenen Rollenrechte.
- Administratoren dürfen Benutzer verwalten und alle Veranstaltungen lesen, erhalten aber keine Veranstaltungs-Schreib- oder Statusrechte, sofern ihnen nicht zusätzlich eine dafür berechtigende Rolle zugewiesen wird.
- Für Prerendering und Latenz werden keine Grenzwerte festgelegt; konkrete Anforderungen können in späteren Feature-Specs ergänzt werden.
- Personal-, Material-/Fahrzeugverwaltung und Bedarfsplanung sind außerhalb dieses Features; nur die genannten Navigationsplatzhalter gehören zum Umfang.
