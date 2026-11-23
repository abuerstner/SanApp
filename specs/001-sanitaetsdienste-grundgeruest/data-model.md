# Datenmodell: Grundgerüst Sanitätsdienste

## Veranstaltung

Repräsentiert einen Sanitätsdienst zu einer Veranstaltung.

| Feld | Bedeutung | Regeln |
|---|---|---|
| Id | Eindeutige Identität | Vom System erzeugt; unveränderlich |
| Name | Veranstaltungsname | Pflicht; nach Trim nicht leer |
| Startzeitpunkt | Beginn des Dienstes | Pflicht; absoluter Zeitpunkt |
| Endzeitpunkt | Ende des Dienstes | Pflicht; muss strikt nach Startzeitpunkt liegen |
| Ort | Veranstaltungsort | Pflicht; nach Trim nicht leer |
| Auftraggeber | Auftraggebende Person/Organisation | Pflicht; nach Trim nicht leer |
| Status | Lebenszyklusstatus | Pflicht; einer der unten aufgeführten Werte |

Die Zeitpunkte werden über die API als ISO-8601-Zeitpunkte mit Offset übertragen und in der Persistenz als UTC-Zeitpunkte normalisiert. Der fachliche Vertrag definiert keine maximale Textlänge; keine zusätzliche fachliche Feldlängenbegrenzung wird erfunden.

### Status und Übergänge

| Von | Erlaubte Übergänge |
|---|---|
| In Planung | Geplant, Abgesagt |
| Geplant | Aktiv, Abgesagt |
| Aktiv | Abgeschlossen, Abgesagt |
| Abgeschlossen | Keine; Endzustand |
| Abgesagt | Keine; Endzustand |

Sobald „Geplant“ erreicht ist, ist „In Planung“ dauerhaft nicht mehr zulässig. Der Status kann nur durch Sandienstverantwortliche oder Einsatzleitungen geändert werden. Datenfelder außer Status dürfen Sandienstverantwortliche nur in „In Planung“ oder „Geplant“ ändern. Löschen ist ausschließlich in „In Planung“ erlaubt.

## Benutzer

ASP.NET-Core-Identity-Konto. Die E-Mail ist die Anmeldeidentität und muss durch Identity eindeutig behandelt werden. Konten entstehen ausschließlich durch administrative Anlage oder den initialen, idempotenten Bootstrap-Administrator. Selbstregistrierung ist ausgeschlossen.

## Rolle

Eine Identity-Rolle, die Berechtigungen bündelt. Der Anwendungskatalog enthält exakt: Administrator, Sandienstverantwortlicher, Einsatzleitung und Helfer. Benutzer dürfen eine oder mehrere Rollen haben; ihre Berechtigungen ergeben sich aus der Vereinigung der zugewiesenen Rollenrechte.

## Beziehungen und Berechtigungsregeln

- Benutzer und Rolle stehen in einer Viele-zu-viele-Beziehung.
- Eine Veranstaltung hat einen einzelnen Statuswert.
- Administratoren dürfen Benutzer verwalten und Veranstaltungen lesen; zusätzliche Veranstaltungsrechte bestehen nur, wenn dem Konto eine weitere berechtigende Rolle zugewiesen ist.
- Helfer dürfen nur Veranstaltungen in „Geplant“, „Aktiv“ oder „Abgeschlossen“ lesen. „In Planung“ und „Abgesagt“ dürfen auch über Einzelabrufe nicht offengelegt werden.
- Einsatzleitungen dürfen Statusänderungen durchführen, aber keine Veranstaltungsdaten pflegen oder Veranstaltungen löschen.
