# Contributing Guide

Vielen Dank für dein Interesse an SpocR! Dieses Projekt freut sich über Issues und Pull Requests.

## Grundprinzipien
- Kleine, fokussierte Änderungen sind leichter zu reviewen.
- Bevor du ein größeres Feature beginnst: Issue eröffnen und abstimmen.
- Kein direktes Committen auf `main` – arbeite über Branches.

## Branch Namenskonvention
```
feature/<kurzbeschreibung>
fix/<bug-id-oder-kurzbeschreibung>
docs/<thema>
refactor/<bereich>
```

## Entwicklungs-Setup
Voraussetzungen:
- .NET 8 SDK (9 optional für Hauptprojekt Multi-Target)

Restore & Build:
```bash
dotnet restore
dotnet build src/SpocR.csproj
```

Schneller Qualitätscheck (Self-Validation):
```bash
spocr test --validate
```

Unit Tests ausführen:
```bash
dotnet test tests/SpocR.Tests
```

(Integration Tests werden später unter `tests/SpocR.IntegrationTests` wieder aktiviert.)

## Pull Request Checkliste
- [ ] Build erfolgreich (`dotnet build`)
- [ ] `spocr test --validate` ohne Fehler
- [ ] Falls neue Funktion: README / passende Doku ergänzt
- [ ] Keine unnötigen Debug-Ausgaben / Console.WriteLine
- [ ] Keine toten Dateien / nicht verwendeten Usings

## Code Stil
- C# `latest` Features erlaubt, aber pragmatisch einsetzen.
- Nullability aktiv: Warnungen ernst nehmen.
- Sinnvolle Benennungen – keine Abkürzungen außer weithin bekannt (`db`, `sql`).

## Commit Messages
Empfohlenes Muster (imperativ):
```
feat: fügt einfachen Integration Test Skeleton hinzu
fix: behebt NullReference in SchemaManager
refactor: vereinfacht StoredProcedure Query Logik
docs: ergänzt Testing Abschnitt
chore: aktualisiert Abhängigkeiten
```

## Versionierung
Patch-Version wird automatisch beim Build hochgezählt (MSBuild Target). Größere Versionserhöhungen bitte im PR erwähnen.

## Sicherheit / Secrets
Keine Zugangsdaten in Commits. Für lokale Tests: `.env` oder User Secrets (nicht im Repo).

## Kontakt / Diskussion
Nutze Issues oder Diskussionen auf GitHub. Für größere Architekturänderungen bitte RFC-Issue anlegen.

Viel Erfolg & danke für deinen Beitrag! 🙌
