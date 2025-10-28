# SpocR v5 Roadmap Checklist

## Statusüberblick (Stand 2025-10-27)

- SnapshotBuilder pipeline läuft produktiv (Details siehe `src/SpocRVNext/SnapshotBuilder/CHECKLIST.md`) und setzt ENV-first Konfiguration um.
- Forced-Upgrade-Zielbild auf `.NET 8` bleibt bestehen; Legacy-Pfade werden erst mit Migration deaktiviert.

## Leitplanken v5.0

- [ ] Forced Upgrade auf `.NET 8`, keine Legacy-Modi mehr, Dokumentation nur noch als IST-Stand.
- [ ] `DataContext/`, `spocr.json`, `spocr.global.json` ablösen und neue Projektstruktur in `src/SpocRVNext` verankern.
- [ ] Neue CLI (`init`, `pull`, `build`, `rebuild`) als einziger Pfad; Legacy-CLI abschalten.
- [ ] Parallelen Betrieb ermöglichen, bis Migration abgeschlossen ist; Optimierungen danach priorisieren.
- [ ] Guardrails für DbContext-Oberflächen definieren (interner Kontext darf Ad-hoc/Diagnostics, generierter Kontext nur Execute-Aufrufe) und Tests/Docs ableiten.
- [ ] Klare Trennung „SpocR Source“ vs. „SpocR Runtime“ ausarbeiten (Packages/Namespaces/Deploymentpfade) und im Architektur-Abschnitt dokumentieren.

## Aktuelle Schwerpunkte

- [ ] Zielarchitektur und Migrationspfad dokumentieren (Abhängigkeitsmatrix, Optimierungs-Backlog).
- [ ] Inventar der `DataContext/`- und `spocr.json`-Verbraucher erstellen, Ablösekette planen.
- [ ] CLI-Konzept aktualisieren (Command-Matrix, UX-Flows, Telemetrie, Breaking-Change-Kommunikation).
- [ ] Teststrategie festziehen: neue Suite definieren, Legacy-Abschaltung planen, Smoke/Integration festhalten.
- [ ] Stakeholder-Kommunikation vorbereiten (Forced Upgrade, Beta-Programm, Support-Kanäle).
- [ ] Post-Migration Zielbild finalisieren: neues Repository `nuetzliches/xtraq` (Namespace `Xtraq`, Version `1.0.0`, ohne SpocR-Historie) und Freeze-Hinweis aus SpocR v4.5 auf den Nachfolger.

## Bereinigung & Umsetzung

- [ ] `DataContext/`-Abhängigkeiten entfernen oder ersetzen; verbleibende Nutzung markieren.
- [~] `.env`-Pfad finalisieren, Migration `spocr.json` → `.env` über `spocr init` absichern.
  - Debug-Sandbox `.env` bereinigt; `SPOCR_GENERATOR_MODE` entfernt, next-only Verhalten bestätigt (2025-10-29).
- [x] Generator-Mode-Fallbacks entfernen (`SPOCR_GENERATOR_MODE`, `--mode`); next-only Verhalten dokumentiert und getestet.
- [ ] Legacy-Code nach `src/SpocRVNext` verschieben oder entfernen; Projektstruktur bereinigen.
- [ ] Legacy-Tests deaktivieren, Nachfolge-Tests (Smoke/Integration) vorbereiten.
- [ ] Deployment-/Release-Pipeline für v5 aufsetzen; parallele Betriebsfähigkeit sicherstellen.

## SnapshotBuilder & Analyzer (siehe `src/SpocRVNext/SnapshotBuilder/CHECKLIST.md`)

- [ ] Tests und Golden Snapshots anheben, Legacy-Brücke abbauen.
- [~] Obsolete Snapshot-Felder entfernen und Konsumenten migrieren.
  - SchemaSnapshotService & SchemaMetadataProvider nutzen nur noch flattening; Writer/Tests noch offen.
- [ ] Analyzer-Verbesserungen & Diagnose-Läufe durchführen, Ergebnisse dokumentieren.
- [ ] Abschlusskriterien bestätigen (Determinism, vollständige Test-Suite).

## Dokumentation & Kommunikation

- [ ] Doku auf IST-Stand konsolidieren (README, Developer Guides, CLI-Hilfe, Release Notes).
- [ ] Migrationstipps bereitstellen (`MIGRATION-V5.md`, `migration-v5.instructions`, `.NET 8` Hinweis).
- [ ] Kommunikationsplan für Kunden/Partner erstellen (Zeitplan, Forced-Upgrade-Botschaft, Supportkanäle).
- [ ] Feedbackschleifen etablieren (Pilotkunden, Beta, Telemetrieauswertung).
- [ ] SpocR Freeze-Kommunikation vorbereiten: v4.5 finalisiert, deutet auf `nuetzliches/xtraq` (Namespace `Xtraq`, Version `1.0.0`).

## Nachlauf (Legacy v4.5)

- [ ] Branch `v4.5` aus `master` erstellen, Auto-Updater deaktivieren und Warnhinweis platzieren.
- [ ] Letzte Legacy-Version dokumentieren, Auto-Update stoppen, Migrationspfad klarstellen.
- [ ] Sicherstellen, dass v5-Installationen Legacy-`DataContext/` nicht mehr aktualisieren; neue Registrierungs- und Deployment-Pfade beschreiben.
- [x] `migration-v5.instructions` befüllen (inkl. SnapshotBuilder-Hinweisen aus `src/SpocRVNext/SnapshotBuilder/CHECKLIST.md`). (Siehe `migration-v5.instructions`)

> Fortschritt der SnapshotBuilder-Migration bitte dort pflegen; dieses Dokument bündelt die Gesamtplanung für v5.
