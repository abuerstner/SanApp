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
| Version | Undurchsichtige Änderungsversion | Bei Anlage erzeugt und bei jeder gespeicherten Daten- oder Statusänderung erneuert |

Die UI verwendet für Eingabe und Anzeige Europe/Berlin unabhängig von der Gerätezeitzone. Eine bei der Sommerzeitumstellung nicht existierende lokale Zeit wird verständlich abgewiesen; für jeden doppelt vorkommenden Zeitpunkt ist die Auswahl von Sommer- oder Winterzeit erforderlich. Beim Anzeigen und erneuten Bearbeiten müssen die beiden möglichen Zeitpunkte unterscheidbar bleiben. Die API überträgt die Berliner Ortszeit mit dem gültigen, gegebenenfalls ausgewählten ISO-8601-Offset; der Server prüft Ortszeit und Offset anhand der Zeitzonenregeln und normalisiert den eindeutigen Zeitpunkt auf UTC. Ende und Start werden als tatsächliche Zeitpunkte verglichen. Der fachliche Vertrag definiert keine maximale Textlänge; keine zusätzliche fachliche Feldlängenbegrenzung wird erfunden.

Neue Veranstaltungen müssen mit „In Planung“ angelegt werden; jeder andere angegebene Anfangsstatus wird abgewiesen. Für Bearbeitung, Statusänderung und Löschung ist die zuletzt gelesene Version erforderlich. Versions- und Statusprüfung sowie Änderung oder Löschung erfolgen atomar. Eine veraltete Aktion wird ohne Änderung abgewiesen; ein Konflikthinweis bietet das erneute Laden der aktuellen Fassung an, soweit sie noch existiert und zugänglich ist.

### Status und Übergänge

Die folgende, vom User bestätigte Tabelle führt alle erlaubten Statuswechsel abschließend auf. Alle anderen Wechsel werden ohne Zustandsänderung abgewiesen.

| Von | Erlaubte Übergänge |
|---|---|
| In Planung | Geplant, Abgesagt |
| Geplant | Aktiv, Abgesagt |
| Aktiv | Abgeschlossen, Abgesagt |
| Abgeschlossen | Keine; Endzustand |
| Abgesagt | Keine; Endzustand |

Sobald „Geplant“ erreicht ist, ist „In Planung“ dauerhaft nicht mehr zulässig. Der Status kann nur durch Sandienstverantwortliche oder Einsatzleitungen geändert werden. Datenfelder außer Status dürfen Sandienstverantwortliche nur in „In Planung“ oder „Geplant“ ändern. Löschen ist ausschließlich in „In Planung“ erlaubt.

## Benutzer

ASP.NET-Core-Identity-Konto. Die E-Mail ist die Anmeldeidentität und muss eindeutig behandelt werden: vor Identitäts-, Anmelde- und Duplikatvergleichen führende und nachfolgende Leerzeichen entfernen und ohne Beachtung der Groß-/Kleinschreibung vergleichen. Dieselbe Normalisierung gilt für Bootstrap-E-Mail-Adressen und administrativ angelegte Konten. Konten entstehen ausschließlich durch administrative Anlage oder den initialen, idempotenten Bootstrap-Administrator. Selbstregistrierung ist ausgeschlossen. Ein administrativ angelegtes Konto bleibt bis zum erstmaligen Festlegen eines Kennworts über einen gültigen Link gesperrt; Anmeldefehler bleiben gegenüber unbekannten Konten neutral.

## Erstkennwort-Einladung

Für jedes administrativ angelegte Konto wird ein konto- und zweckgebundener Einmal-Link zur selbstständigen Festlegung des ersten Kennworts ausgestellt. Der Link läuft eine Stunde nach Ausstellung ab. Ein neu ausgestellter Link macht alle vorherigen Links dieses Kontos sofort ungültig; gleichzeitige oder wiederholte Einlösung darf höchstens eine Kennwortfestlegung bewirken. Kennwortregel und serverseitige Validierung entsprechen FR-001/FR-002. Erst nach erfolgreichem Kennwortsetzen wird das Konto für Anmeldungen freigegeben.

Ein abgelaufener Link löscht das Konto nicht und gibt dessen eindeutige Anmelde-E-Mail nicht frei. Administratoren können für nicht aktivierte Konten einen neuen Link anfordern oder das Konto ausdrücklich entfernen. Ist der E-Mail-Versand nicht möglich, bleibt das Konto gesperrt und der Administrator erhält einen klaren Fehler; ein späterer Neuversand bleibt möglich. Klartext-Links und Kennwörter werden nicht dauerhaft gespeichert oder protokolliert.

## Rolle

Eine Identity-Rolle, die Berechtigungen bündelt. Der Anwendungskatalog enthält exakt: Administrator, Sandienstverantwortlicher, Einsatzleitung und Helfer. Jedes Konto muss eine oder mehrere dieser Rollen besitzen; seine Berechtigungen ergeben sich aus der Vereinigung der aktuell zugewiesenen Rollenrechte. Ausschließlich Administratoren dürfen Rollen bestehender Konten hinzufügen oder entfernen.

Die vollständige Rollenmenge eines Benutzers besitzt eine undurchsichtige Änderungsversion. Eine Rollenänderung wird atomar geprüft und gespeichert; die letzte Rolle eines Kontos und die letzte Administratorzuweisung dürfen auch bei parallelen Änderungen nicht entfernt werden. Änderungen gelten nach Commit spätestens beim nächsten geschützten Zugriff, auch in bestehenden Sitzungen.

## Anlagevorgang

Für jede Veranstaltungs- und administrative Benutzeranlage wird vor dem Absenden eine zufällige, ausreichend große Vorgangskennung erzeugt. Eine Datenbank-Eindeutigkeitsbedingung auf Kennung und Vorgangsart sowie eine atomare Reservierung stellen sicher, dass auch konkurrierende Anfragen verschiedener Serverinstanzen höchstens einen Versuch starten. Der Vorgang speichert Art, Kennung, belegbaren Zustand und gegebenenfalls die Identität des Ergebnisses; er enthält weder Kennwörter noch andere Zugangsdaten. Identitätsdaten und Ereignisdetails werden nicht unnötig im Vorgangsprotokoll dupliziert.

Ein erfolgreicher Ergebniszustand und das angelegte Konto samt Rollen beziehungsweise die Veranstaltung werden in derselben PostgreSQL-Transaktion festgeschrieben. Ein belegbarer fachlicher oder Validierungsfehler darf als abgelehntes Ergebnis vermerkt werden; scheitert dessen Speicherung oder ist ein Commit nicht nachweisbar, bleibt der Status unklar. Eine reservierte, nach Absturz nicht sicher abgeschlossene Bearbeitung wird nicht automatisch erneut ausgeführt und darf nicht allein aufgrund ihres Alters als laufend oder fehlgeschlagen gelten. Eine laufende Bearbeitung wird nur bei belastbarem Nachweis angezeigt. Fehlender Datensatz beweist nicht, dass eine verzögerte Anfrage nicht noch eintreffen kann.

Bei technisch lesbaren Veranstaltungsanfragen werden kanonische Anlageeingaben aller fachlichen POST-Felder einschließlich Status und Zeitpunkten mit der Reservierung in derselben Transaktion geschützt gespeichert, auch wenn sie fachlich ungültig sind. Fehlende und ungültige Werte müssen für den Vergleich eindeutig repräsentiert werden. Wiederholungen werden bei laufenden, abgestürzten, abgelehnten und erfolgreichen Vorgängen mit den ursprünglichen Eingaben verglichen; abweichende Eingaben werden abgelehnt. Nach erfolgreicher Löschung entfernt dieselbe Transaktion diese Eingaben und ersetzt den Ergebnisverweis durch einen dauerhaften minimalen Verbrauchsvermerk. Diese Kennung darf nie erneut eine Veranstaltung erzeugen. Bei Benutzeranlagen gilt die erste reservierte Anfrage unabhängig von später abweichenden Eingaben; kein Kennwortvergleich oder Speichern eines Kennworts für die Wiederholung ist erforderlich. Vorgangseinträge für Benutzeranlagen bleiben zum Schutz vor wiederholter Ausführung und für die Statusprüfung erhalten; Löschung oder Verkürzung dieser Aufbewahrung ist nicht Teil dieses Features.

## Formularentwurf

Der Entwurf enthält Kennung, serverseitig bestimmtes Eigentümerkonto, Formularart und -ziel, bestätigte Version, Zeit der letzten bestätigten Aktualisierung, Ablaufzeit und ausschließlich erlaubte nicht geheime Eingaben. Ein separater, zufälliger Tab-Nachweis für die Löschung bei Kontowechsel wird serverseitig nur als Hash gespeichert; er ist ein Geheimnis und gewährt niemals Lese- oder Schreibzugriff. Reguläre Operationen prüfen Anmeldung, Eigentum und aktuelle Formularberechtigung. Der Browser hält Kennung und Tab-Nachweis nur tabgebunden; Kennwörter und Zugangsdaten werden nicht als Entwurfsinhalte gespeichert. Unsynchronisierte Eingaben sind nicht wiederherstellungsgarantiert und dürfen nicht als wiederhergestellt dargestellt werden.

Eine Stunde nach der letzten bestätigten Aktualisierung ist der Entwurf gesperrt; bei verfügbaren Komponenten wird er innerhalb von fünf Minuten nach Ablauf physisch gelöscht, nach Ausfall innerhalb von fünf Minuten nach Wiederherstellung. Beim Löschen wegen Ablauf wird atomar ein minimaler serverseitiger Ablaufvermerk ohne Formularinhalte mit zufälliger Referenz und Eigentümerbindung hinterlassen. Nur das wiederangemeldete, aktuell formularberechtigte Eigentümerkonto darf den Ablauf anhand der Tab-Referenz erfahren; ein fehlender Vermerk beweist keinen Ablauf. Aktualisierung, andere Löschgründe und Ablauf müssen unter Konkurrenz korrekt unterschieden werden. Datenschutzkonforme Aufbewahrung und Bereinigung dieses Vermerks sind vor Freigabe zu entscheiden; nach seiner Entfernung darf kein Ablauf behauptet werden. Bei entzogener Formularberechtigung bleibt der nicht abgelaufene Entwurf bis zum Ablauf erhalten und unzugänglich; erneute Berechtigung erlaubt Wiederherstellung, sofern er nicht anderweitig gelöscht wurde.

Ein gespeicherter fachlicher Stand und seine Entwurfsversion werden eindeutig korreliert. Erst eine bestätigte Kenntnisnahme des Erfolgs durch den Benutzer erlaubt Löschung; diese erfolgt bedingt auf unveränderte Version. Spätere Änderungen bleiben erhalten, auch bei verzögerten Antworten und parallelen Synchronisationen. Bei unklarem oder fehlgeschlagenem Ergebnis bleibt der Entwurf bis Ablauf erhalten; Kontowechsel hat Vorrang. Beim Kontowechsel ist die bestätigte serverseitige Löschung Voraussetzung für die neue Sitzung. Die sichere tabgebundene Löschbefugnis und Wiederholbarkeit bei unklarem Ergebnis müssen vor Freigabe nachgewiesen werden.

## Kennwortwiederherstellung

Ein kontogebundener Wiederherstellungsvorgang umfasst eine zufällige, vertrauliche und zweckgebundene Einmal-Kennung beziehungsweise ihren sicheren Nachweis, Ausstellungszeit, Ablaufzeit (eine Stunde), Generation und Verbrauchszustand. Neue Ausstellung invalidiert frühere Generationen. Ausstellung, Validierung und Verbrauch werden pro Konto transaktional koordiniert; Kennwortänderung, Verbrauch und Invalidierung bestehender Sitzungen erfolgen atomar. E-Mail-Adresse, Klartext-Link und Kennwort werden nicht in Logs oder dauerhaften Vorgangsdaten gespeichert. Zustellung erfolgt über einen konfigurierten Dienst; Provider-Annahme ist kein Beweis der Zustellung. Ein nicht zugestellter neuer Link reaktiviert ältere nicht.

## Beziehungen und Berechtigungsregeln

- Benutzer und Rolle stehen in einer Viele-zu-viele-Beziehung.
- Eine Veranstaltung hat einen einzelnen Statuswert.
- Administratoren dürfen Benutzer verwalten und Veranstaltungen lesen; zusätzliche Veranstaltungsrechte bestehen nur, wenn dem Konto eine weitere berechtigende Rolle zugewiesen ist.
- Benutzer ausschließlich mit der Rolle Helfer dürfen nur Veranstaltungen in „Geplant“, „Aktiv“ oder „Abgeschlossen“ lesen. „In Planung“ und „Abgesagt“ dürfen ihnen auch über Einzelabrufe nicht offengelegt werden. Eine zusätzlich zugewiesene Rolle kann weitergehende Rechte gewähren.
- Einsatzleitungen dürfen Statusänderungen durchführen; die Rolle allein berechtigt nicht zur Pflege oder Löschung von Veranstaltungen.
