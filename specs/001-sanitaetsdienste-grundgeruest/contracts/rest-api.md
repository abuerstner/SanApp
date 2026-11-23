# REST API Contract: Grundgerüst Sanitätsdienste

Base path: `/api`. Alle Endpunkte erfordern eine authentifizierte Sitzung, sofern nicht anders angegeben. Selbstregistrierung ist nicht vorhanden. Autorisierung wird serverseitig geprüft. JSON-Antworten verwenden `application/json`; Fehler verwenden `application/problem+json` gemäß RFC 9457.

## Veranstaltungen

| Methode | Pfad | Berechtigung | Verhalten |
|---|---|---|---|
| GET | `/api/veranstaltungen` | Administrator, Sandienstverantwortlicher, Einsatzleitung, Helfer | Übersicht; Helfer erhalten ausschließlich Geplant/Aktiv/Abgeschlossen |
| GET | `/api/veranstaltungen/{id}` | Gleiche Rollen | Einzelabruf; für Helfer sind In Planung und Abgesagt nicht auffindbar/offengelegt |
| POST | `/api/veranstaltungen` | Sandienstverantwortlicher | Anlegen; Pflichtfelder einschließlich Status |
| PUT | `/api/veranstaltungen/{id}` | Sandienstverantwortlicher | Daten außer Status aktualisieren; nur bei In Planung oder Geplant |
| DELETE | `/api/veranstaltungen/{id}` | Sandienstverantwortlicher | Löschen ausschließlich bei In Planung |
| PATCH | `/api/veranstaltungen/{id}/status` | Sandienstverantwortlicher, Einsatzleitung | Statusübergang gemäß Zustandsautomat |

### Payloads

Veranstaltungsrepräsentation enthält `id`, `name`, `startzeitpunkt`, `endzeitpunkt`, `ort`, `auftraggeber` und `status`. Zeitwerte sind ISO 8601 mit Offset; Statuswerte sind `In Planung`, `Geplant`, `Aktiv`, `Abgeschlossen` oder `Abgesagt`.

`POST` und `PUT` enthalten `name`, `startzeitpunkt`, `endzeitpunkt`, `ort` und `auftraggeber`. Der Status wird beim Anlegen angegeben und in Folgeänderungen ausschließlich über den Statusendpunkt geändert. `PATCH` enthält ausschließlich `status`.

## Benutzerverwaltung

| Methode | Pfad | Berechtigung | Verhalten |
|---|---|---|---|
| POST | `/api/admin/users` | Administrator | Benutzerkonto mit einer oder mehreren erlaubten Rollen anlegen |

Es gibt keinen öffentlichen Registrierungsendpunkt. Anmelde- und Identitätsverwaltungsseiten werden durch serverseitige Identity-UI bereitgestellt; deren Kontoänderungsfunktionen müssen durch Authentifizierung und Autorisierung geschützt sein.

## Rollenmatrix

| Aktion | Administrator | Sandienstverantwortlicher | Einsatzleitung | Helfer |
|---|---:|---:|---:|---:|
| Benutzer anlegen/Rollen zuweisen | Ja | Nein | Nein | Nein |
| Veranstaltungen aller Status lesen | Ja | Ja | Ja | Nein |
| Helfer-freigegebene Veranstaltungen lesen | Ja | Ja | Ja | Ja |
| Veranstaltung anlegen | Nein* | Ja | Nein | Nein |
| Veranstaltungsdaten bearbeiten (In Planung/Geplant) | Nein* | Ja | Nein | Nein |
| Veranstaltung löschen (In Planung) | Nein* | Ja | Nein | Nein |
| Status ändern | Nein* | Ja | Ja | Nein |

`*` Administratoren erhalten diese Rechte nur, wenn ihnen zusätzlich die jeweilige Rolle zugewiesen ist.

## Fehlerverhalten

- `401 Unauthorized`: keine gültige Anmeldung.
- `403 Forbidden`: angemeldet, aber Aktion nicht erlaubt.
- `404 Not Found`: Ressource existiert nicht; ebenso für nicht sichtbare Veranstaltungseinzelabrufe durch Helfer, um deren Existenz nicht offenzulegen.
- `400 Bad Request`: FluentValidation-Fehler, mit `errors`-Einträgen im ProblemDetails-Objekt.
- `409 Conflict`: ungültiger Statusübergang oder gesperrte Datenänderung/Löschung aufgrund des aktuellen Lebenszyklus.

ProblemDetails enthält mindestens `type`, `title`, `status`, `detail` (soweit sicher/nützlich) und `instance`; Validierungsfehler werden feldbezogen strukturiert. Fehler dürfen keine Geheimnisse oder internen Stacktraces enthalten.

## Identity und Bootstrap

Identity-Login und initiale Administratoreinrichtung sind serverseitig. Es gibt keinen anonymen Benutzeranlagepfad. Initiale Bootstrap-Zugangsdaten werden nie als API-Antwort offengelegt oder in Repository-Konfiguration gespeichert.
