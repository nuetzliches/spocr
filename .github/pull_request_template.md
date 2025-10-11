# Pull Request Checklist

Bitte vor dem Merge (Squash & Merge bevorzugt) folgende Punkte prüfen und abhaken.

## Überblick
- [ ] Zweck des PR klar in der Beschreibung erläutert (Warum? Was ändert sich?)
- [ ] Breaking Changes dokumentiert (README / MIGRATION / CHANGELOG)

## Code & Tests
- [ ] Build lokal erfolgreich: `dotnet build`
- [ ] Relevante Tests hinzugefügt oder angepasst
- [ ] Alle Tests grün (lokal oder CI)
- [ ] Keine neuen Compiler-Warnungen / Analyzers geprüft

## Sicherheit & Sensitive Inhalte
- [ ] Keine Secrets / Passwörter / Connection Strings im Code oder in Markdown-Dateien
- [ ] Datei(en) mit rein lokalem Zweck ("dev only") wurden NICHT eingecheckt
- [ ] Falls temporär benötigt: In `debug/` oder als `*.example` Vorlage abgelegt
- [ ] Secret-Scan (lokal oder via CI) ohne Funde

## Dokumentation
- [ ] README / Referenz-Doku aktualisiert (falls notwendig)
- [ ] Öffentliche APIs / CLI Parameter dokumentiert
- [ ] Changelog-Eintrag hinzugefügt (oder nicht erforderlich begründet)

## Konfiguration / Daten
- [ ] Keine lokalen Pfade oder personenbezogenen Daten committed
- [ ] Konfigurationsänderungen erklärt (`spocr.json`, `debug/` Beispiele)

## Housekeeping
- [ ] Unbenutzte Dateien / TODO-Kommentare entfernt oder in Issue überführt
- [ ] PR Titel folgt Konvention `<scope>: <kurze Beschreibung>` (z.B. `cli: add diff command`)

## Speziell für dev-only Dateien
Falls zuvor eine Datei wie `DeveloperBranchUseOnly.md` existierte:
- [ ] Datei wurde entfernt oder umbenannt nach `DEVELOPMENT.md` (mit nur generischen Informationen)
- [ ] Sensible Inhalte (z.B. echte Servernamen, Datenbank-Snapshots) sind nicht mehr im Git-Verlauf des PR (Rebase / Squash / Amend genutzt)

---
**Hinweis:** Wird versehentlich Sensitives gemerged, sofort Maintainer informieren. Zur Entfernung aus Historie: `git filter-repo` oder BFG + neue Tags pushen.
