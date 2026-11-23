# Anforderungs-Checkliste: Grundgerüst Sanitätsdienste

**Zweck**: Detaillierte Prüfung der Vollständigkeit, Eindeutigkeit und Konsistenz der Feature-Anforderungen vor der Umsetzung.
**Erstellt**: 2026-11-23
**Feature**: [spec.md](../spec.md) | [plan.md](../plan.md) | [API-Vertrag](../contracts/rest-api.md) | [Datenmodell](../data-model.md)
**Zielgruppe und Zeitpunkt**: Reviewer und Feature-Verantwortliche vor der Umsetzung.

**Review Ownership**: Diese Checkliste bewertet die Qualität der Anforderungen und gehört den Reviewern. Ein Eintrag wird nur nach positiver Prüfung mit `[x]` markiert; dies bestätigt keine abgeschlossene Implementierung.

## Anforderungsvollständigkeit

- [x] CHK001 Sind die aktiven Funktionsbereiche und die drei ausdrücklich ausgeschlossenen Folge-Module voneinander eindeutig abgegrenzt? [Completeness, Spec §Scope & Abgrenzung]
- [x] CHK002 Sind Anmeldungs-, initiale Administrator- und administrative Benutzeranlage-Anforderungen einschließlich der Rolle der Selbstregistrierung vollständig beschrieben? [Completeness, Spec §FR-001–FR-003]
- [x] CHK003 Sind für die Benutzerverwaltung die Anforderungen an die nachträgliche Zuweisung und Änderung von Rollen bestehender Konten oder deren Ausschluss eindeutig festgelegt? [Gap, Spec §FR-003]
- [x] CHK004 Sind für Veranstaltungserstellung und -bearbeitung alle Pflichtfelder, deren Bedeutung und erforderliche Beziehungen dokumentiert? [Completeness, Spec §FR-004–FR-005]
- [x] CHK005 Sind für jede der vier Rollen Lese-, Anlage-, Bearbeitungs-, Lösch- und Statusrechte inklusive Konten mit mehreren Rollen vollständig spezifiziert? [Completeness, Spec §FR-008–FR-010, Spec §Assumptions]
- [x] CHK006 Sind Anforderungen an den Umgang mit fehlendem oder ungültigem initialem Administratorkonto einschließlich Wiederanlauf und erforderlicher Meldungen dokumentiert? [Coverage, Gap, Spec §FR-002]

## Klarheit und fachliche Grenzen

- [x] CHK007 Ist eindeutig festgelegt, welche Anfangsstatus bei der Veranstaltungserstellung zulässig sind und wie sie sich zu den Übergangsregeln verhalten? [Clarity, Gap, Spec §FR-004, Spec §FR-006]
- [x] CHK008 Sind erlaubte und verbotene Statusübergänge für jeden Status einschließlich identischer Ausgangs- und Zielwerte eindeutig bestimmbar? [Clarity, Spec §FR-006]
- [x] CHK009 Sind die Statusgrenzen für Datenbearbeitung, Statusänderung und Löschung anhand einzelner Aktionen unmissverständlich getrennt? [Clarity, Spec §FR-007–FR-009]
- [x] CHK010 Sind Definitionen für fehlende Werte, ausschließlich aus Leerzeichen bestehende Texte und die zeitliche Reihenfolge der Pflichtfelder eindeutig und einheitlich? [Clarity, Spec §FR-005]
- [x] CHK011 Sind Anforderungen für Zeitzonen, Sommerzeit und mehrdeutige oder ungültige Ortszeiten ausreichend konkret, um Veranstaltungszeiten einheitlich zu interpretieren? [Gap, Data Model §Veranstaltung]
- [x] CHK012 Sind Regeln zur Eindeutigkeit von Benutzerkonten und zu Konflikten bei bereits vorhandenen Anmeldedaten ausreichend präzise? [Gap, Data Model §Benutzer]

## Konsistenz zwischen Artefakten

- [x] CHK013 Sind Statuswerte und Übergänge in Spec, Datenmodell und API-Vertrag konsistent und vollständig aufeinander abgebildet? [Consistency, Spec §FR-006, Data Model §Status und Übergänge, Contract §Veranstaltungen]
- [x] CHK014 Sind alle Rollenrechte einschließlich kumulativer Mehrfachrollen in Spec, Datenmodell und Rollenmatrix widerspruchsfrei beschrieben? [Consistency, Spec §FR-008–FR-010, Contract §Rollenmatrix]
- [x] CHK015 Sind die Anforderungen an Status bei Neuanlage und Datenänderung zwischen Feldern, Schreibvorgängen und Übergangsregeln widerspruchsfrei? [Consistency, Spec §FR-004–FR-006, Contract §Payloads]
- [x] CHK016 Sind Statusgrenzen für Bearbeiten und Löschen in Spezifikation, API-Vertrag und Datenmodell übereinstimmend? [Consistency, Spec §FR-007–FR-008, Data Model §Veranstaltung, Contract §Veranstaltungen]
- [x] CHK017 Sind Anforderungen an Cortex.Mediator, Command-/Query-Trennung, Validierung und Schichtgrenzen im Plan mit der Constitution vereinbar? [Consistency, Plan §Constitution Check, Constitution §II, Constitution §IV]
- [x] CHK018 Sind die Regeln zur Sichtbarkeit abgesagter und in Planung befindlicher Veranstaltungen zwischen Übersicht und Einzelzugriff identisch beschrieben? [Consistency, Spec §FR-010, Contract §Veranstaltungen]

## Abnahmekriterien und Nachvollziehbarkeit

- [x] CHK019 Hat jede funktionale Anforderung nachvollziehbare, unabhängig beurteilbare Abnahmekriterien einschließlich Erfolgs- und Ablehnungsfällen? [Traceability, Spec §Functional Requirements, Spec §User Scenarios & Testing]
- [x] CHK020 Sind die in den Erfolgskriterien genannten Mengen und Fallklassen für ungültige Eingaben, Statuswechsel und Rollenrechte hinreichend bestimmt? [Measurability, Spec §SC-002–SC-004]
- [x] CHK021 Ist für jede geforderte Fehlermeldung ausreichend konkret festgelegt, welche Informationen für Nutzer verständlich und welche aus Sicherheitsgründen auszusparen sind? [Clarity, Spec §FR-011, Contract §Fehlerverhalten]
- [x] CHK022 Sind Anforderungen an die Kennzeichnung und Unbenutzbarkeit der drei Navigationsplatzhalter objektiv beurteilbar? [Measurability, Spec §FR-012, Spec §SC-005]

## Szenarien und Fehlerfälle

- [x] CHK023 Sind Anforderungen für leere Veranstaltungsübersichten sowie fehlende oder nicht sichtbare Einzeleinträge beschrieben? [Coverage, Gap, Spec §User Story 3]
- [x] CHK024 Sind Fehleranforderungen für ungültige Zugangsdaten, abgelaufene Sitzungen und nicht berechtigte Aktionen je Interaktionsoberfläche eindeutig? [Coverage, Gap, Spec §FR-001, Spec §FR-014]
- [x] CHK025 Sind Anforderungen für gleichzeitige Bearbeitung, Statusänderung und Löschung derselben Veranstaltung definiert, insbesondere zur Vermeidung überholter Statusentscheidungen? [Edge Case, Gap, Spec §FR-006–FR-008]
- [x] CHK026 Sind Anforderungen für fehlgeschlagene oder teilweise abgeschlossene Benutzeranlage und Rollenzuweisung einschließlich Wiederholung beschrieben? [Recovery, Gap, Spec §FR-003]
- [x] CHK027 Sind Anforderungen für Datenbank- oder Verbindungsfehler bei lesenden und schreibenden Vorgängen sowie zur Wiederherstellung eines konsistenten Zustands dokumentiert? [Recovery, Gap, Plan §Technical Context]
- [x] CHK028 Sind Anforderungen an Abbruch und Wiederanlauf bei fehlgeschlagener Datenbankschemamigration einschließlich Schutz vorhandener Daten beschrieben? [Recovery, Gap, Plan §Technical Context]
- [x] CHK029 Sind Anforderungen an ungültige, unbekannte oder unvollständige Status- und Rollenwerte inklusive zugehöriger Fehlermeldungen definiert? [Edge Case, Gap, Spec §FR-003, Spec §FR-006]

## Nicht-funktionale Anforderungen

- [x] CHK030 Sind Tastaturbedienbarkeit und aussagekräftige Beschriftungen für alle interaktiven Oberflächen als überprüfbare Anforderungen nachvollziehbar? [Coverage, Gap, Constitution §III, Spec §FR-012]
- [x] CHK031 Sind Schutz und Offenlegung von Zugangsdaten und personenbezogenen Benutzerinformationen über Oberfläche, Servermeldungen und Protokolle hinweg ausreichend beschrieben? [Security, Gap, Constitution §V, Spec §FR-002]
- [x] CHK032 Sind Risiken und Schutzanforderungen für nicht authentifizierte, unberechtigte und direkt adressierte Zugriffe widerspruchsfrei auf allen geschützten Bereichen beschrieben? [Security, Coverage, Spec §FR-010, Spec §FR-014]
- [x] CHK033 Ist ausdrücklich dokumentiert, ob Leistungs-, Kapazitäts- und Verfügbarkeitsziele für dieses Feature ausgeschlossen oder für spätere Planung offen sind? [Clarity, Plan §Technical Context, Spec §Assumptions]
- [x] CHK034 Sind Anforderungen an Sprache und Datums-/Zeitdarstellung für Nutzeroberflächen einschließlich verständlicher Fehlertexte festgelegt oder bewusst als offen gekennzeichnet? [Gap, Spec §FR-011–FR-012]

## Abhängigkeiten und Annahmen

- [x] CHK035 Sind Voraussetzungen für Ersteinrichtung, Datenbankverfügbarkeit und sichere Bereitstellung des initialen Administratorkontos dokumentiert und untereinander konsistent? [Dependency, Spec §FR-002, Plan §Technical Context]
- [x] CHK036 Sind Verantwortlichkeiten und Abhängigkeitsrichtungen zwischen Oberfläche, Anwendungsfällen, Fachmodell und Persistenz hinreichend klar abgegrenzt? [Clarity, Plan §Constitution Check, Spec §FR-013]
- [x] CHK037 Sind Auswahl und Verantwortlichkeiten für die Vermittlung von Schreib-/Leseanfragen sowie die verbindliche serverseitige Validierung trotz widersprüchlicher Herstellerangaben eindeutig dokumentiert? [Dependency, Plan §Constitution Check, Research §Cortex.Mediator und Validierung]
- [x] CHK038 Sind Annahmen zu Erststart, mehreren Rollen und administrativen Zusatzrechten nachvollziehbar und von ausdrücklich festgelegten Anforderungen unterscheidbar? [Assumption, Spec §Assumptions]

## Reviewbefunde vom 2026-11-23

Bewertung durch Copilot auf ausdrückliche Anfrage des Feature-Verantwortlichen. Markierungen bewerten die Anforderungen, nicht eine Implementierung. Offene Punkte benötigen Präzisierung und erneute Prüfung; aus ihnen wird keine neue Fachentscheidung abgeleitet.

- **CHK006 (erneut geprüft, erfüllt)**: [Spec §FR-002 und Abnahmeszenarien 10–12](../spec.md#functional-requirements) legen Validierung und sichere Meldungen bei fehlender/ungültiger Konfiguration, atomaren Abschluss, kontrollierten Wiederanlauf, In-place-Korrektur bei Erhalt von Benutzer-ID und verknüpften Daten sowie das Verhalten nach Bootstrap-Abschluss fest; entsprechende Integrationsprüfungen stehen in [T013](../tasks.md) und die Ablaufanleitung in [Quickstart](../quickstart.md#lokale-einrichtung-und-start).
- **CHK012 (erneut geprüft, erfüllt)**: [Spec §FR-003 und Abnahmeszenario 11](../spec.md#functional-requirements) legen die Normalisierung (äußere Leerzeichen entfernen, Groß-/Kleinschreibung ignorieren), Ablehnung und Unverändertheit des bestehenden Kontos fest. [Datenmodell §Benutzer](../data-model.md#benutzer) übernimmt dieselbe Normalisierung; [API-Vertrag §Benutzerverwaltung und Anlagevorgänge](../contracts/rest-api.md#benutzerverwaltung) bestimmt `409 Conflict`, sicheren Fehlercode und atomare Eindeutigkeitsprüfung; [T013](../tasks.md) verlangt Normalisierungs- und Konkurrenztests.
- **CHK016 (erneut geprüft, erfüllt)**: [Spec §FR-007–FR-008](../spec.md#functional-requirements), [Datenmodell §Veranstaltung](../data-model.md#veranstaltung) und [API-Vertrag §Veranstaltungen](../contracts/rest-api.md#veranstaltungen) definieren übereinstimmend: Bearbeitung von Veranstaltungsdaten außer Status nur in „In Planung“ und „Geplant“; Löschen ausschließlich in „In Planung“. Ein separater Validierungsleitfaden gehört nicht zu den vorhandenen maßgeblichen Artefakten und wurde daher aus dem Prüfkriterium entfernt.
- **CHK019 (erneut geprüft, erfüllt)**: Die Abnahmeszenarien in [US1–US4](../spec.md#user-scenarios--testing-mandatory) decken die fachlichen Erfolgs-, Ablehnungs- und Konfliktfälle der funktionalen Anforderungen ab; Szenario 36–37 prüft zusätzlich Kontodatenzugriff und Rollenwerte, US2-Szenario 15 unbekannte Statuswerte. Technische und Fehlerfälle werden in den [Quickstart-Abnahmeszenarien](../quickstart.md#end-to-end-abnahmeszenarien) und den referenzierten Testaufgaben nachvollziehbar geprüft.
- **CHK021 (erneut geprüft, erfüllt)**: [Spec FR-011, FR-014 und FR-017](../spec.md#functional-requirements) sowie [API-Vertrag §Fehlerverhalten](../contracts/rest-api.md#fehlerverhalten) ordnen Feldvalidierung, Anmeldung, Berechtigung, Nichtauffindbarkeit, Konflikte, Abhängigkeits- und unbekannte Fehler verständlichen Meldungen und nächsten Aktionen zu. Rohdetails, Stacktraces, Geheimnisse und unberechtigte Kontodaten bleiben ausgeschlossen.
- **CHK023 (erneut geprüft, erfüllt)**: [Spec FR-020](../spec.md#functional-requirements) unterscheidet eine nach erfolgreichem Abruf leere Veranstaltungsübersicht von einem Ladefehler und verlangt erneutes Laden. Der [API-Vertrag §Fehlerverhalten](../contracts/rest-api.md#fehlerverhalten) definiert `404` für fehlende und für Helfer nicht sichtbare Veranstaltungseinzelabrufe.
- **CHK024 (erneut geprüft, erfüllt)**: [Spec FR-001, FR-014 und FR-019](../spec.md#functional-requirements) definieren die neutrale Meldung bei ungültigen Zugangsdaten, die Berechtigungsablehnung und den Sitzungsablauf mit erneuter Anmeldung und Entwurfsbehandlung. Der [API-Vertrag §Fehlerverhalten](../contracts/rest-api.md#fehlerverhalten) legt die entsprechenden `401`/`403`-Antworten und UI-Meldungen fest.
- **CHK026 (erneut geprüft, erfüllt)**: [Spec FR-003 und FR-017](../spec.md#functional-requirements) sowie [API-Vertrag §Benutzerverwaltung und Anlagevorgänge](../contracts/rest-api.md#benutzerverwaltung) definieren gesperrte Konten bei Linkausfall, erneuten Linkversand, atomare Rollenänderungen und den Status bei wiederholten oder unklaren Anlageanfragen. [T018](../tasks.md) verlangt atomaren Commit beziehungsweise Rollback.
- **CHK027 (erneut geprüft, erfüllt)**: [Spec FR-016, FR-017 und FR-020](../spec.md#functional-requirements) regeln sichere Fehlerbehandlung bei Datenbank-/Verbindungsausfällen, unklare Schreibergebnisse, Ladefehler und erneuten Abruf. Der [API-Vertrag §Fehlerverhalten](../contracts/rest-api.md#fehlerverhalten) definiert `503` für nicht verfügbare Abhängigkeiten; [T018 und T023](../tasks.md) verlangen atomare Änderungen und Persistenzfehlertests.
- **CHK028 (erneut geprüft, erfüllt)**: [Spec FR-016 und Edge Cases](../spec.md#functional-requirements) verlangen Startabbruch, sichere Protokollierung, Administratorprüfung und Schutz vorhandener Daten. Der [Quickstart](../quickstart.md#lokale-einrichtung-und-start) beschreibt Pending-Gate, kontrollierte Freigabe und erneute Migration ohne automatisches Löschen.
- **CHK029 (erneut geprüft, erfüllt)**: [Spec FR-023 und US1-Szenario 37/US2-Szenario 15](../spec.md#functional-requirements) sowie [API-Vertrag §Payloads und §Fehlerverhalten](../contracts/rest-api.md#payloads) unterscheiden ungültige oder unbekannte Status-/Rollenwerte mit `400` von bekannten, aber unzulässigen Änderungen mit `409`; fehlerhafte Anfragen ändern den Zustand nicht.
- **CHK031 (erneut geprüft, erfüllt)**: [Spec FR-018 und FR-022](../spec.md#functional-requirements) begrenzen protokollierte Zugangsdaten und den Zugriff auf Kontodaten; der [API-Vertrag §Benutzerverwaltung und §Fehlerverhalten](../contracts/rest-api.md#benutzerverwaltung) schützt fremde Kontodaten und schließt personenbezogene Daten ohne Berechtigung aus Fehlermeldungen aus.
- **CHK034 (erneut geprüft, erfüllt)**: [Spec FR-011–FR-012](../spec.md#functional-requirements) legen deutsche Oberflächen- und Fehlermeldungen, das Datumformat TT.MM.JJJJ, 24-Stunden-Uhrzeit und Europe/Berlin fest; die [Quickstart-Abnahmeszenarien](../quickstart.md#end-to-end-abnahmeszenarien) prüfen diese Darstellung.
- **T001-Review (positiv, 2026-11-23)**: Spec und API-Vertrag wurden erneut gegen die Feature-Checkliste geprüft. US1-Szenario 3 suggerierte zuvor eine Anmeldung unmittelbar nach der administrativen Kontoanlage; es wurde so präzisiert, dass der Benutzer zuvor sein erstes Kennwort über den gültigen Einladungslink festlegt. Damit stimmt das Szenario mit FR-003, US1-Szenarien 27–30 und dem API-Vertrag überein. Nach der Korrektur sind keine blockierenden Anforderungen- oder Vertragswidersprüche verblieben; die 38 Checklistenpunkte sind geprüft und erfüllt.

## Hinweise

- Neue Prüfpunkte bleiben ungeprüft, bis Reviewer die jeweilige Anforderungsqualität bewertet haben.
- Befunde und fehlende Entscheidungen bei Bedarf mit Verweisen auf die betroffenen Artefakte dokumentieren.
- `/speckit-implement` berücksichtigt den Checklistenstatus, ändert aber keine Markierungen.
- `checklists/requirements.md` hat einen separaten, von `/speckit-specify` und `/speckit-clarify` verwalteten Lebenszyklus.
