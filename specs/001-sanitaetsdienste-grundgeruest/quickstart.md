# Quickstart: Validierung Grundgerüst Sanitätsdienste

## Voraussetzungen

- .NET 11 SDK und Container-Runtime, die mit .NET Aspire verwendet werden kann.
- Restore der Solution-Pakete.
- Für den lokalen Erststart konfigurierte Identity-Bootstrap-Secrets. Keine echten Zugangsdaten in versionierte Dateien eintragen.

## Lokale Einrichtung und Start

1. Bootstrap-Administratorwerte als User Secrets für den Server konfigurieren (die Implementierung legt die Schlüssel `Bootstrap:AdminEmail` und `Bootstrap:AdminPassword` fest):

   ```powershell
   dotnet user-secrets set "Bootstrap:AdminEmail" "admin@example.test" --project SanApp/SanApp/SanApp.csproj
   dotnet user-secrets set "Bootstrap:AdminPassword" "<lokales-starkes-kennwort>" --project SanApp/SanApp/SanApp.csproj
   ```

2. PostgreSQL-Ressource und SanApp über Aspire starten:

   ```powershell
   dotnet run --project SanApp.AppHost/SanApp.AppHost.csproj
   ```

3. In Development wendet der Server nach erfolgreicher Datenbankverbindung die versionierten EF-Core-Migrationen an. Warte auf die Bereitschaftsmeldung im Aspire-Dashboard und melde dich anschließend mit dem Bootstrap-Administrator an. Produktionsmigrationen werden als kontrollierter Deployment-Schritt vor dem Hochfahren der Anwendung angewandt.

## End-to-End-Abnahmeszenarien

1. Melde dich mit dem initialen Bootstrap-Administrator an. Prüfe, dass Benutzeranlage und Rollenzuweisung funktionieren und kein Selbstregistrierungszugang vorhanden ist.
2. Lege je einen Benutzer mit den Rollen Sandienstverantwortlicher, Einsatzleitung und Helfer an. Prüfe API-seitig und in der UI sowohl erlaubte als auch verbotene Aktionen.
3. Lege eine Veranstaltung mit allen Pflichtfeldern an. Prüfe, dass leere/Whitespace-Pflichtfelder, fehlende Zeitpunkte und Ende <= Start keine Speicherung bewirken.
4. Prüfe Statusübergänge In Planung → Geplant → Aktiv → Abgeschlossen sowie Abbruch nach Abgesagt. Verifiziere, dass der Rückweg nach In Planung nach Erreichen von Geplant blockiert bleibt und Endzustände keine weiteren Übergänge haben.
5. Prüfe, dass Bearbeitung/Löschung ab Aktiv und Löschung ab Geplant verweigert werden; Bearbeitung ist bis einschließlich Geplant zulässig.
6. Prüfe, dass Helfer nur Geplant/Aktiv/Abgeschlossen sehen, einschließlich direkter Einzelabrufe, und dass Abgesagt verborgen bleibt.
7. Prüfe, dass jede Validierungs- und fachliche Regelverletzung als ProblemDetails vom Client verständlich angezeigt wird.
8. Prüfe, dass die Navigation alle drei Folge-Module als nicht ausführbare Platzhalter darstellt.

## Automatisierte Validierung

```powershell
dotnet test SanApp.slnx
```

Erwartetes Ergebnis: alle xUnit Unit- und Integrationstests sowie bUnit-Komponententests bestehen. Bei späterer CI-/Deployment-Einrichtung sind Migrationen als kontrollierter Schritt vor dem Anwendungshochlauf zu validieren.
