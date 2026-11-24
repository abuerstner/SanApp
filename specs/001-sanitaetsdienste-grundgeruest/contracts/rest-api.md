# REST API Contract: Grundgerüst Sanitätsdienste

Base path: `/api`. Alle Endpunkte erfordern eine authentifizierte Sitzung, sofern nicht anders angegeben. Selbstregistrierung ist nicht vorhanden. Autorisierung wird serverseitig geprüft. JSON-Antworten verwenden `application/json`; Fehler verwenden `application/problem+json` gemäß RFC 9457.

## Veranstaltungen

| Methode | Pfad | Berechtigung | Verhalten |
|---|---|---|---|
| GET | `/api/veranstaltungen` | Administrator, Sandienstverantwortlicher, Einsatzleitung, Helfer | Übersicht; Benutzer ausschließlich mit der Rolle Helfer erhalten nur Geplant/Aktiv/Abgeschlossen |
| GET | `/api/veranstaltungen/{id}` | Gleiche Rollen | Einzelabruf; für Benutzer ausschließlich mit der Rolle Helfer sind In Planung und Abgesagt nicht auffindbar/offengelegt |
| POST | `/api/veranstaltungen` | Sandienstverantwortlicher | Anlegen mit Vorgangskennung; Pflichtfelder einschließlich Status |
| GET | `/api/veranstaltungsanlagen/{vorgangskennung}` | Sandienstverantwortlicher | Autorisierte Statusprüfung des Anlagevorgangs |
| PUT | `/api/veranstaltungen/{id}` | Sandienstverantwortlicher | Daten außer Status aktualisieren; nur bei In Planung oder Geplant |
| DELETE | `/api/veranstaltungen/{id}` | Sandienstverantwortlicher | Löschen ausschließlich bei In Planung |
| PATCH | `/api/veranstaltungen/{id}/status` | Sandienstverantwortlicher, Einsatzleitung | Statusübergang gemäß Zustandsautomat |

Bei mehreren Rollen gelten deren vereinigte Rechte. Insbesondere können Benutzer mit Helfer und zusätzlich Administrator, Sandienstverantwortlicher oder Einsatzleitung Veranstaltungen aller Statuswerte über beide GET-Endpunkte lesen. Schreib- und Statusrechte ergeben sich aus den jeweils zusätzlich berechtigenden Rollen.

### Payloads

Veranstaltungsrepräsentation enthält `id`, `name`, `startzeitpunkt`, `endzeitpunkt`, `ort`, `auftraggeber`, `status` und eine undurchsichtige `version`. Zeitwerte werden als Berliner Ortszeit mit ihrem gültigen ISO-8601-Offset übertragen; Statuswerte sind `In Planung`, `Geplant`, `Aktiv`, `Abgeschlossen` oder `Abgesagt`.

`POST` und `PUT` enthalten `name`, `startzeitpunkt`, `endzeitpunkt`, `ort` und `auftraggeber`. `POST` enthält außerdem `status`, der ausschließlich `In Planung` sein darf; ein fehlender oder unbekannter Statuswert sowie jeder andere Anfangsstatus werden mit `400` abgewiesen und nicht gespeichert. `PUT` enthält zusätzlich die zuletzt gelesene `version`; `PATCH` enthält `status` und diese `version`. Ein fehlender oder unbekannter Statuswert ergibt `400`; ein bekannter, aber nach dem Zustandsautomaten unzulässiger Wechsel ergibt `409` ohne Änderung. `DELETE` enthält die zuletzt gelesene `version` als Abfrageparameter `version`. Eine fehlende oder ungültige Version ergibt `400`. Jede erfolgreiche Daten- oder Statusänderung liefert die neue Repräsentation mit neuer Version; eine erfolgreiche Löschung liefert `204`.

Für jeden Zeitpunkt prüft der Server die lokale Uhrzeit und den übermittelten Offset gegen Europe/Berlin. Nicht existierende Zeiten sowie fehlende oder für die lokale Zeit ungültige Offsets ergeben `400`. Bei doppelt vorkommenden Zeiten legt der übermittelte gültige Offset den ausdrücklich ausgewählten Zeitpunkt fest; die UI verlangt diese Auswahl für jeden betroffenen Zeitpunkt. Das Ende muss als tatsächlicher Zeitpunkt nach dem Start liegen.

Bearbeitung, Statusänderung und Löschung prüfen die übermittelte Version und die Statusregeln atomar; eine veraltete Version ergibt `409` ohne Änderung. Der Client bietet einen erneuten autorisierten Abruf an; wenn die Veranstaltung zwischenzeitlich gelöscht wurde, liefert dieser `404`. Die zulässigen Statuswechsel sind in der abschließenden Tabelle im [Datenmodell](../data-model.md#status-und-übergänge) definiert; alle anderen Wechsel ergeben `409` ohne Zustandsänderung.

## Benutzerverwaltung

| Methode | Pfad | Berechtigung | Verhalten |
|---|---|---|---|
| GET | `/api/users/me` | Authentifizierter Benutzer | Eigenes Konto anhand des serverseitigen Principals abrufen; keine Benutzerkennung aus der Anfrage übernehmen |
| POST | `/api/admin/users` | Administrator | Gesperrtes Benutzerkonto mit Vorgangskennung und erlaubten Rollen anlegen und Erstkennwort-Link senden |
| POST | `/api/admin/users/{id}/initial-password-link` | Administrator | Neuen Erstkennwort-Link für ein nicht aktiviertes Konto senden und vorherige Links ungültig machen |
| GET | `/api/admin/benutzeranlagen/{vorgangskennung}` | Administrator | Autorisierte Statusprüfung des Anlagevorgangs |
| GET | `/api/admin/users` | Administrator | Konten mit `id`, `email`, `roles`, `rolesVersion` und Aktivierungsstatus auflisten |
| PUT | `/api/admin/users/{id}/roles` | Administrator | Vollständige Rollenmenge anhand der vorgelegten Version atomar ersetzen |

Die Benutzerliste ist ausschließlich Administratoren zugänglich. `GET /api/users/me` liefert nur das Konto des angemeldeten Benutzers (`id`, `email`, `roles` und Aktivierungsstatus); die Zielidentität stammt aus dem serverseitigen Principal. Nicht-Administratoren können keine fremden Konten adressieren oder abrufen. Administratoren dürfen die vollständige Benutzerliste ausschließlich über den Admin-Endpunkt abrufen.

`PUT` erwartet `roles` als nicht leeres Array eindeutiger Rollen aus dem Katalog sowie die zuletzt gelesene `rolesVersion`. Fehlende, leere, doppelte oder unbekannte Rollenwerte ergeben `400`. Bei Erfolg wird die gespeicherte Rollenmenge mit neuer `rolesVersion` zurückgegeben. Ein veralteter Versionswert oder die Entfernung der letzten Administratorzuweisung ergibt `409`, jeweils ohne Teiländerung. Ein unbekanntes Konto ergibt `404`. Parallele Änderungen dürfen weder ein rollenloses Konto noch den Verlust der letzten Administratorzuweisung verursachen. Die aktualisierten Rechte gelten nach Commit spätestens beim nächsten geschützten Zugriff ohne erneute Anmeldung; dies gilt auch für laufende Sitzungen und interaktive Seiten/Komponenten.

Die Kontoanlage vergleicht Anmelde-E-Mail-Adressen nach Entfernen führender und nachfolgender Leerzeichen ohne Beachtung der Groß-/Kleinschreibung; dieselbe Normalisierung gilt für Login und Eindeutigkeitsprüfung. Ist die normalisierte Adresse bereits vergeben, liefert der neue Anlagevorgang `409 Conflict` mit einem sicheren, eindeutigen Fehlercode und einem Hinweis auf die bereits vergebene E-Mail. Das vorhandene Konto bleibt unverändert. Die Kontoanlage liefert andernfalls `201` mit dem gespeicherten Konto, `rolesVersion`, `activationState=pending` und dem Ergebnis des Linkversands. Bei einem synchron festgestellten Versandfehler bleibt die Kontoanlage erfolgreich und das Konto gesperrt; die Antwort enthält einen klaren, sicheren Versandfehlercode für den Administrator, ohne Link oder Kennwort auszugeben. Der Ausfall darf nicht als erfolgreiche Linkzustellung dargestellt werden. Ein späterer Neuversand erfolgt über den oben genannten Admin-Endpunkt; er ist nur für nicht aktivierte Konten zulässig, erzeugt einen neuen Einmal-Link und macht ältere Links sofort ungültig. Ein bereits aktiviertes Konto kann diesen Neuversand-Endpunkt nicht verwenden.

Der Benutzer öffnet den Link auf einer statisch serverseitig gerenderten Identity-Seite und sendet das neue Kennwort per geschütztem POST ab. Der Link ist konto- und zweckgebunden, eine Stunde gültig, einmalig und unterliegt der Kennwortregel. Erst nach erfolgreichem Setzen des Kennworts wird das Konto zur Anmeldung freigegeben. Ein abgelaufener Link sperrt nur den Link, nicht das Konto: es bleibt gesperrt und seine E-Mail belegt, bis ein Administrator erneut sendet oder das Konto entfernt. Login-Fehler für nicht aktivierte Konten sind identisch zu unbekanntem Konto/falschem Kennwort: „E-Mail oder Kennwort ist falsch“.

Es gibt keinen öffentlichen Registrierungsendpunkt. Anmelde- und Identitätsverwaltungsseiten werden durch serverseitige Identity-UI bereitgestellt; deren Kontoänderungsfunktionen müssen durch Authentifizierung und Autorisierung geschützt sein.

## Anlagevorgänge und unklare Ergebnisse

Beide Anlage-POSTs benötigen das Feld `vorgangskennung`, eine vor dem Absenden clientseitig erzeugte UUID. Fehlende oder ungültige Kennungen ergeben `400`. Die Kennung ist kein Zugangsnachweis: bei jedem POST und Status-GET werden Anmeldung und aktuelle Rollen serverseitig geprüft. Jeder aktuell berechtigte Sandienstverantwortliche beziehungsweise Administrator darf den entsprechenden Status abrufen, auch wenn eine andere Person den Vorgang gestartet hat. Nach Authentifizierung, Berechtigung, technischem Lesen der Anfrage und Prüfung der Kennung wird der Vorgang atomar reserviert oder als Wiederholung behandelt. Erst eine neu reservierte Anfrage durchläuft fachliche FluentValidation und Ausführung; die Mediator-Pipeline darf diese Validierung nicht vor die Wiederholungserkennung ziehen. Jeder neue Command bleibt vor Ausführung validiert; spätere Benutzeranlage-Eingaben dürfen keine neue fachliche Ausführung auslösen.

Ein Status-GET liefert `200` mit `vorgangskennung`, `art` (`veranstaltung` oder `benutzer`) und `zustand`: `erfolgreich`, `abgelehnt`, `inBearbeitung`, `unklar` oder für gelöschte Veranstaltungen `geloescht`. `erfolgreich` enthält `ressourcenId`, `abgelehnt` einen sicheren Fehlercode; bei `geloescht` werden keine Veranstaltungsdaten zurückgegeben. `inBearbeitung` erfordert einen belastbaren Nachweis laufender Verarbeitung, sonst gilt `unklar`. Auch eine unbekannte Kennung liefert `unklar`, nicht Fehlschlag: eine Anfrage könnte noch eintreffen. Bei Verbindungsfehlern zeigt der Client ebenfalls einen unklaren Status und bietet erneute Prüfung an. Die UI übersetzt `inBearbeitung` als „In Bearbeitung“ und `unklar` als „Status unklar“.

Bei der ersten erfolgreichen Anlage liefert POST `201` und den belegten Vorgangsstatus. Für eine Wiederholung eines nachweislich laufenden Vorgangs wird `202` mit Status zurückgegeben; für andere bekannte Vorgänge `200` mit Status, sofern nicht nachfolgend ausdrücklich `409` festgelegt ist. Ein nachgewiesener Validierungs- oder fachlicher Fehler der ersten Anfrage liefert ProblemDetails `400` beziehungsweise `409` und wird, soweit sicher festgeschrieben, als `abgelehnt` abrufbar. Ein fehlgeschlagener Transport oder eine nicht beweisbare Speicherung wird nicht als Erfolg oder Fehlschlag interpretiert. Ein zurückgemeldeter Vorgangsstatus ersetzt keine automatische Wiederholung.

Für Veranstaltungsanlagen werden kanonische fachliche Eingaben einschließlich fehlender oder ungültiger Werte atomar mit der Reservierung vor der fachlichen Validierung gespeichert. Derselbe Schlüssel und gleiche Eingaben liefern auch bei laufendem oder abgelehntem Vorgang den bestehenden Status; abweichende Eingaben ergeben `409`. Ist die Anfrage technisch nicht lesbar oder die Reservierung nicht nachweisbar, darf kein unbelegter Vorgangsstatus behauptet werden. Nach Löschung liefert eine erneute Anlage `409` mit sicherem Hinweis auf den abgeschlossenen Vorgang und die nicht mehr vorhandene Veranstaltung; der Status-GET liefert `geloescht`. Für Benutzeranlagen führt jede wiederholte oder gleichzeitige Anfrage derselben Kennung unabhängig von ihren Eingaben keinen weiteren Versuch aus und liefert den bestehenden Status; die Eingaben, insbesondere Kennwörter, werden für diesen Vergleich nicht gespeichert. Eine nach Entfernen äußerer Leerzeichen und ohne Beachtung der Groß-/Kleinschreibung bereits vergebene E-Mail bei einem neuen Benutzeranlagevorgang ergibt `409` mit sicherem Fehlercode, ohne Änderung am bestehenden Konto. Die Duplikatprüfung und der eindeutige Datenbank-Constraint müssen dieselbe Normalisierung atomar durchsetzen, auch bei konkurrierenden Anlageversuchen. Die bloße Existenz eines Kontos mit dieser E-Mail belegt keinen bestimmten Anlageversuch.

## Formularentwürfe

| Methode | Pfad | Berechtigung | Verhalten |
|---|---|---|---|
| POST | `/api/formularentwuerfe` | Angemeldetes Konto mit aktueller Berechtigung für das Formular | Tabgebundenen Entwurf für dieses Konto erstellen; undurchsichtige Kennung zurückgeben |
| GET | `/api/formularentwuerfe/{id}` | Eigentümer mit aktueller Formularberechtigung | Bestätigte nicht geheime Eingaben und Version abrufen |
| PUT | `/api/formularentwuerfe/{id}` | Eigentümer mit aktueller Formularberechtigung | Erlaubte Felder und Version prüfen und Entwurf speichern |
| DELETE | `/api/formularentwuerfe/{id}` | Eigentümer mit aktueller Formularberechtigung | Entwurf löschen |
| POST | `/api/formularentwuerfe/{id}/erfolgsbestaetigungen` | Eigentümer mit aktueller Formularberechtigung | Belegten eigenen Erfolg und zugeordnete Entwurfsversion bestätigen; bedingte idempotente Bereinigung |

Der Server prüft Eigentum und aktuelle Formularberechtigung; eine Kennung allein gewährt keinen Zugriff. Anonym `401`, fremde oder nicht berechtigte Entwürfe geben keine Inhalte preis. `PUT` verlangt die zuletzt bestätigte Version; konkurrierende oder verzögerte Updates ergeben `409` ohne Überschreiben. Der Client warnt bei ausstehender oder fehlgeschlagener Synchronisation; wiederhergestellt wird nur der bestätigte Stand. Zugangsdaten werden nie als Entwurfsdaten gespeichert. Ablauf ist eine Stunde nach letzter bestätigter Aktualisierung, ab dann kein Abruf; Löschung innerhalb von fünf Minuten nach Ablauf beziehungsweise nach Wiederherstellung der Verfügbarkeit. Bei nachgewiesenem Ablauf zeigt die UI einen Hinweis und bietet ein leeres Formular an, ohne einen unbekannten Zustand als Ablauf auszugeben. Bei entzogener Formularberechtigung bleibt der Entwurf bis zum Ablauf verborgen.

Bei fachlichem Absenden wird die bestätigte Entwurfsversion mit dem konkreten Speicherversuch serverseitig korreliert. Nach eigener Anzeige des belegten Erfolgs sendet der Eigentümer am ACK-Endpunkt die Vorgangskennung und Version; der Server prüft Eigentum, aktuelle Berechtigung und tatsächlichen Erfolg sowie die gespeicherte Zuordnung. Solange spätere lokale Änderungen noch nicht bestätigt sind, sendet der Client kein löschendes ACK und zeigt den ungesicherten Zustand an; eine erfolgreiche Synchronisation neuerer Änderungen muss vor dem ACK abgeschlossen sein. Status-GET durch eine andere berechtigte Person ist kein ACK. ACK und bedingte Löschung erfolgen atomar: `200` bestätigt die Verarbeitung und unterscheidet gelöschten von wegen neuerer Version erhaltenem Entwurf; eine Wiederholung nach verlorener Antwort liefert dasselbe Ergebnis anhand eines begrenzten, inhaltsfreien Belegs. Bei fehlendem oder nicht belegtem Erfolg sowie abweichender Zuordnung `409`, keine Löschung. Bei unklarem Ergebnis bleibt der Entwurf bis zum Ablauf bestehen; fehlgeschlagene Bereinigung wird nicht als bestätigt ausgegeben. Ein verspätetes `PUT` darf einen gelöschten Entwurf nicht neu erstellen.

Bei Kontowechsel wird im statischen Identity-Anmeldeablauf nach Prüfung der neuen Anmeldedaten, aber vor Ausstellung der neuen Sitzung, eine CSRF-geschützte, serverseitig korrelierte Löschung ausgeführt. Sie verlangt eine bei Entwurfserstellung erzeugte hochzufällige Löschbefugnis und gibt keine Inhalte oder Eigentümerinformationen zurück. Der Server vergleicht den Hash mit der hinterlegten Tab-Zuordnung, beschränkt die Aktion auf diesen Entwurf und bindet sie an den konkreten Anmeldeversuch; eine frei manipulierbare Kennung allein genügt nicht. Bestätigte Löschung oder bereits bestätigtes Löschergebnis wird idempotent als Erfolg gewertet, bei Fehler oder unklarem Ergebnis wird keine neue Sitzung ausgestellt und ein erneuter Versuch angeboten. Der Nachweis wird nach Abschluss widerrufen; Umgang mit kopierten Tabs, Replay und Session-Fixation ist ein Sicherheits- und Implementierungsblocker bis zum Nachweis. Das bestehende reguläre `DELETE` bleibt dem Eigentümer vorbehalten.

Produktentscheidung: Fehlen sowohl Identitätssitzung als auch sämtliche Browserhinweise, ist eine neue Anmeldung zulässig. Ein Server kann diesen Fall nicht von einem vollständig verlorenen Browserzustand unterscheiden; der Kontowechselschutz kann in diesem Fall nicht zugesichert werden.

## Kennwortwiederherstellung

Die anonym zugänglichen serverseitigen Identity-Formulare verwenden antifälschungsgeschützte POSTs und liefern für bekannte und unbekannte Adressen dieselbe neutrale Antwort. Nur bestehende Konten erhalten einen Link. Bei unabhängig vom Konto festgestellter globaler Dienststörung liefern beide denselben allgemeinen Fehler mit erneuter Anforderung; kontospezifische oder erst nach Antwort bekannte Versandfehler dürfen keine Kontoexistenz offenbaren. Provider-Annahme belegt keine Zustellung.

Der Link ist ab Ausstellung eine Stunde gültig, nur für sein Konto und seinen Zweck nutzbar und nach erfolgreichem Reset verbraucht. Neue Ausstellung invalidiert frühere Links auch bei fehlgeschlagener Zustellung. Ausstellung und Verbrauch werden pro Konto über Serverinstanzen hinweg atomar koordiniert; gleichzeitige Einlösungen desselben Links ergeben höchstens eine erfolgreiche Änderung. Ungültige, abgelaufene und verwendete Links zeigen denselben Hinweis und die Möglichkeit einer neuen Anforderung. Nach erfolgreichem Reset verlieren alle bestehenden Sitzungen spätestens beim nächsten geschützten Zugriff ihre Berechtigung, einschließlich laufender interaktiver Circuits. Token, Links, Kennwörter und E-Mail-Adressen dürfen nicht protokolliert oder über Referer an Dritte übertragen werden.

Links verwenden HTTPS und eine konfigurierte vertrauenswürdige öffentliche Basis-URL statt ungeprüfter Host-Header; Reset-Seiten vermeiden Drittressourcen, sind nicht cachebar und setzen eine restriktive Referrer-Policy. Missbrauchsschutz begrenzt Anforderungen kontoneutral ohne permanente Kontosperre; konkrete Schwellen sind Betriebsparameter und vor Freigabe festzulegen. Timing, HTTP-Status und Fehlermeldungen für bekannte und unbekannte Adressen dürfen kein praktikables Unterscheidungsmerkmal liefern. Die Ermittlung des globalen Dienstzustands erfolgt unabhängig vom angefragten Konto. Versand- und Missbrauchsschutz, zuverlässige kontoneutrale Störungserkennung und konkrete Identity-Integration sind vor Freigabe zu prüfen.

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

- `401 Unauthorized`: keine gültige Anmeldung. Ungültige Anmeldedaten ergeben die neutrale Meldung „E-Mail oder Kennwort ist falsch“; bei abgelaufener Sitzung weist die Oberfläche auf die erneute Anmeldung hin und bietet sie an.
- `403 Forbidden`: angemeldet, aber Aktion nicht erlaubt; die Oberfläche zeigt „Sie haben für diese Aktion keine Berechtigung“.
- `404 Not Found`: Ressource existiert nicht; ebenso für nicht sichtbare Veranstaltungseinzelabrufe durch Benutzer ausschließlich mit der Rolle Helfer, um deren Existenz nicht offenzulegen.
- `400 Bad Request`: FluentValidation-Fehler sowie fehlende oder unbekannte Statuswerte und fehlende, leere, doppelte oder unbekannte Rollenwerte, mit `errors`-Einträgen im ProblemDetails-Objekt; die Oberfläche ordnet verständliche Meldungen den betroffenen Feldern zu.
- `409 Conflict`: veraltete Veranstaltungsversion; bekannter, aber ungültiger Statusübergang oder gesperrte Datenänderung/Löschung aufgrund des aktuellen Lebenszyklus; veraltete Rollenversion oder Versuch, die letzte Administratorzuweisung zu entfernen; bereits vergebene E-Mail bei neuem Vorgang; abweichende oder nach Löschung wiederverwendete Veranstaltungs-Vorgangskennung. Die Oberfläche nennt die sichere Fehlerkategorie und bietet, soweit sinnvoll, das erneute Laden des aktuellen Stands an.
- `503 Service Unavailable`: nicht verfügbare Abhängigkeit; die Oberfläche kennzeichnet den Fehler als vorübergehend und bietet einen erneuten Abruf an. Bei Schreibvorgängen darf daraus allein kein sicherer Fehlschlag abgeleitet werden; der Status richtet sich nach FR-017. Bei nicht zustellbarem Erstkennwort-Link wird die erfolgreiche Kontoanlage mit ausstehendem Aktivierungsstatus und explizitem Versandfehler zurückgegeben; ein Neuversand-Endpunkt meldet einen Versandfehler ausdrücklich, während das Konto gesperrt bleibt.
- Andere oder nicht klassifizierte technische Fehler werden dem Benutzer mit einer allgemeinen, sicheren Meldung angezeigt; interne Fehlerdetails werden nicht offengelegt.

ProblemDetails enthält mindestens `type`, `title`, `status`, `detail` (nur soweit sicher und für die nächste Handlung nützlich) und `instance`; Validierungsfehler werden feldbezogen strukturiert. Der Client bildet Fehlerkategorien auf verständliche deutsche Meldungen ab und zeigt keine ungeprüften rohen Fehlerdetails an. Fehler dürfen keine Geheimnisse, personenbezogenen Daten ohne Berechtigung oder internen Stacktraces enthalten. Protokolle enthalten keine E-Mail-Adressen, Kennwörter oder andere Zugangsdaten; technische Benutzerkennungen und Fehlerarten sind erlaubt.

## Identity und Bootstrap

Identity-Login und initiale Administratoreinrichtung sind serverseitig. Es gibt keinen anonymen Benutzeranlagepfad. Initiale Bootstrap-Zugangsdaten werden nie als API-Antwort offengelegt oder in Repository-Konfiguration gespeichert.
