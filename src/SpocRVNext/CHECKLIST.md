# SpocR v5 Roadmap Checklist

## Statusüberblick (Stand 2025-10-27)

- SnapshotBuilder pipeline läuft produktiv (Details siehe `src/SpocRVNext/SnapshotBuilder/CHECKLIST.md`) und setzt ENV-first Konfiguration um.
- Forced-Upgrade-Zielbild auf `.NET 8` bleibt bestehen; Legacy-Pfade werden erst mit Migration deaktiviert.

## Leitplanken v5.0

- [ ] Forced Upgrade auf `.NET 8`, keine Legacy-Modi mehr, Dokumentation nur noch als IST-Stand.
- [~] `DataContext/`, `spocr.json`, `spocr.global.json` ablösen und neue Projektstruktur in `src/SpocRVNext` verankern.
  - 2025-10-31: CLI weist bei Legacy-Artefakten (spocr.json, spocr.user.\*.json, spocr.global.json, DataContext/) Warnungen aus; Debug-Repo enthält keine spocr.json mehr.
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
- [~] Post-Migration Zielbild finalisieren: neues Repository `nuetzliches/xtraq` (Namespace `Xtraq`, Version `1.0.0`, ohne SpocR-Historie) und Freeze-Hinweis aus SpocR v4.5 auf den Nachfolger.
  - Dokumentation aktualisiert (`MIGRATION_SpocRVNext.md`, `migration-v5.instructions`, `README.md`, `CHANGELOG.md`) mit Xtraq-Hand-off (2025-10-29).

## Bereinigung & Umsetzung

- [ ] `DataContext/`-Abhängigkeiten entfernen oder ersetzen; verbleibende Nutzung markieren.
  - 2025-10-29: SnapshotBuilder nutzt `SpocRVNext.Data.DbContext`; CLI Buildpfad setzt kein `SpocR.DataContext` mehr voraus (SetConnectionString entfernt).
  - 2025-10-31: DI-Registrierung verzichtet auf `SpocR.DataContext.DbContextServiceCollectionExtensions`; Legacy-Ordner `src/DataContext/` für kontrollierte Stilllegung vormerken.
- [x] CLI-Manager auf `.env`-Only Betriebsmodus bringen (Config-File FileManager entfernen).
  - 2025-10-31: SnapshotSchemaMetadataProvider konsumiert `.env`/Environment statt `FileManager<ConfigurationModel>`.
  - 2025-10-31: `SpocrManager` entfernt `FileManager<ConfigurationModel>` Abhängigkeit; Pull/Build laufen rein über EnvConfiguration, Remove weist auf Legacy-/Manualpfad hin.
  - 2025-10-31: `spocr schema` Kommando deaktiviert; SchemaManager/Commands nur noch leere Platzhalter ohne Legacy-Fallback.
- [~] `.env`-Pfad finalisieren, Migration `spocr.json` → `.env` über `spocr init` absichern.
  - Debug-Sandbox `.env` bereinigt; `SPOCR_GENERATOR_MODE` entfernt, next-only Verhalten bestätigt (2025-10-29).
  - Generator liest keine `spocr.json`-Fallbacks mehr; `spocr init`/CI Inventory bleibt zu aktualisieren (2025-10-29).
  - 2025-10-30: EnvConfiguration & EnvBootstrapper entfernen Legacy-Scans; ProceduresGenerator nutzt ausschließlich ENV Overrides.
  - Tests aktualisiert (`SpocR.Tests`, `SpocR.IntegrationTests`), um neue Pflichtwerte zu setzen (2025-10-29).
  - CI & Entwickler-Dokus auf Projektpfad-Kommandos umgestellt (`-p <dir>`); Roadmap-Referenzen folgen (2025-10-29).
  - 2025-10-30: CLI `--path` verarbeitet nur noch Projektverzeichnisse/.env; Legacy `spocr.json`-Automatik entfernt, Project Manager Prompts aktualisiert, `SPOCR_CONFIG_PATH`/`SPOCR_PROJECT_ROOT` normalisieren auf `.env`.
  - 2025-10-30: Projektverwaltung speichert `.env`-Ziele und Fehlermeldungen fordern `SPOCR_GENERATOR_DB` statt `spocr.json`-Mutationen ein.
  - 2025-10-31: DbContextGenerator leitet Namespace & Ausgabeverzeichnis direkt aus `.env` ab; `spocr.json` bleibt lediglich für Legacy-Diagnosen verfügbar.
  - 2025-10-29: Unbenutzte Inputs/Outputs/Results Generator-Stubs in `SpocRVNext` gelöscht; konsolidierte Procedures-Generierung bleibt alleiniger Pfad.
- [x] Generator-Mode-Fallbacks entfernen (`SPOCR_GENERATOR_MODE`, `--mode`); next-only Verhalten dokumentiert und getestet.
- [~] Legacy-Code nach `src/SpocRVNext` verschieben oder entfernen; Projektstruktur bereinigen.
  - 2025-10-29: Unbenutzte Inputs/Outputs/Results Generator-Stubs entfernt; verbleibende Konsolidierungsschritte folgen mit Procedures-/TableType-Pipeline.
- [ ] Legacy-Tests deaktivieren, Nachfolge-Tests (Smoke/Integration) vorbereiten.
- [ ] Deployment-/Release-Pipeline für v5 aufsetzen; parallele Betriebsfähigkeit sicherstellen.

## SnapshotBuilder & Analyzer (siehe `src/SpocRVNext/SnapshotBuilder/CHECKLIST.md`)

- [ ] Tests und Golden Snapshots anheben, Legacy-Brücke abbauen.
- [~] Obsolete Snapshot-Felder entfernen und Konsumenten migrieren.
  - SchemaSnapshotService & SchemaMetadataProvider nutzen nur noch flattening; Writer/Tests noch offen.
- [~] Tabellen-Metadaten schreiben und Analyzer anbinden.
  - 2025-10-29: Schema-Writer legt `.spocr/schema/tables` ohne technische IDs an; SchemaCache v3 (`.spocr/cache/<fingerprint>.json`) trägt Table-Summaries inkl. Column-Hash. Das historische `.spocr/cache/tables`-Verzeichnis ist entfernt. Debug-Datenbank liefert noch keine Tabellen, Seed/Analyzer-Aufgaben bleiben offen.
- [ ] Analyzer-Verbesserungen & Diagnose-Läufe durchführen, Ergebnisse dokumentieren.
- [ ] Abschlusskriterien bestätigen (Determinism, vollständige Test-Suite).

## Dokumentation & Kommunikation

- [>] Doku auf IST-Stand konsolidieren (README, Developer Guides, CLI-Hilfe, Release Notes).
  - README & MIGRATION Guide aktualisiert (2025-10-27/29); Roadmap & Referenzseiten folgen.
- [>] Migrationstipps bereitstellen (`MIGRATION-V5.md`, `migration-v5.instructions`, `.NET 8` Hinweis).
  - 2025-10-29: `migration-v5.instructions` auf SOLL-Zustand begrenzt, MIGRATION Guide auf Zielarchitektur aktualisiert; Veröffentlichung via Docs offen.
- [>] Docs/content: GitHub Pages auf aktuellen CLI-Zustand fokussieren, historische Erzählungen in Legacy-Stream verschieben.
  - 2025-10-29: MIGRATION Inhalte bereinigt, verbleibende Seiten in `docs/content` benötigen Kürzung.
  - 2025-10-29: `docs/content/3.reference/configuration-schema.md` auf `.env`/`SPOCR_*` Zielzustand angepasst.
  - 2025-10-29: Roadmap-Seiten (`development-tasks`, `optional-features`, `output-strategies`, `json-support-design`, `json-procedure-models`) neu aufgesetzt.
  - 2025-10-29: `docs/content/5.roadmap/migration-v5.md` beschreibt den Cutover-Prozess vollständig.
  - 2025-10-29: `docs/content/5.roadmap/removed-heuristics-v5.md`, `v5-differences.md`, `api-changes-v5.md` ohne Platzhalter überarbeitet.
  - 2025-10-29: Roadmap Landing Page (`docs/content/5.roadmap/index.md`) an v5 Fokus angepasst.
  - 2025-10-29: `docs/content/5.roadmap/testing-framework.md` bündelt Phasen/Artefakte & Backlog.
  - 2025-10-29: `docs/content/5.roadmap/development-tasks.md` auf generated DbContext/`.env` Fokus gebracht; Legacy DataContext-Referenzen entfernt.
  - 2025-10-29: `docs/content/5.roadmap/json-support-design.md` auf typed+raw Default überführt; Preview-Flag-Hinweise entfernt.
  - 2025-10-29: `docs/content/3.reference/json-procedures.md` & `env-bootstrap.md` dokumentieren JSON-Default ohne `.env`-Toggles.
  - 2025-10-29: `samples/restapi/.env.example` bereinigt (keine JSON Preview Keys mehr).
  - 2025-10-30: CLI-Hilfen/Roadmap notieren `--path` Normalisierung, `SPOCR_CONFIG_PATH`/`SPOCR_PROJECT_ROOT` spiegeln `.env` Pfade.
  - 2025-10-28: CLI Hilfetext & `spocr init` Output auf JSON-Default ausgerichtet (keine separaten JSON-Toggles mehr).
  - 2025-10-28: CLI `pull`/`build`/`rebuild` Hilfetexte auf `.env`-Kontext ohne Preview-Toggles umgestellt.
  - 2025-10-31: CLI Test-Doku auf Entfernung des `spocr test` Verbs angepasst; Übergangshinweis auf `dotnet test` ergänzt.
  - 2025-10-31: `docs/content/2.cli` Overview/pull/build Seiten auf `.env` Defaults, neue Optionen und Legacy-Platzhalter aktualisiert.
- [>] Kommunikationsplan für Kunden/Partner erstellen (Zeitplan, Forced-Upgrade-Botschaft, Supportkanäle).
- [>] Feedbackschleifen etablieren (Pilotkunden, Beta, Telemetrieauswertung).
- [>] SpocR Freeze-Kommunikation vorbereiten: v4.5 finalisiert, deutet auf `nuetzliches/xtraq` (Namespace `Xtraq`, Version `1.0.0`).
  - Kommunikationspfad in README, CHANGELOG und Migration-Anleitungen hinterlegt (2025-10-29).

## Nachlauf (Legacy v4.5)

- [ ] Branch `v4.5` aus `master` erstellen, Auto-Updater deaktivieren und Warnhinweis platzieren.
- [ ] Letzte Legacy-Version dokumentieren, Auto-Update stoppen, Migrationspfad klarstellen.
- [ ] Sicherstellen, dass v5-Installationen Legacy-`DataContext/` nicht mehr aktualisieren; neue Registrierungs- und Deployment-Pfade beschreiben.
- [x] `migration-v5.instructions` befüllen (inkl. SnapshotBuilder-Hinweisen aus `src/SpocRVNext/SnapshotBuilder/CHECKLIST.md`). (Siehe `migration-v5.instructions`)

> Fortschritt der SnapshotBuilder-Migration bitte dort pflegen; dieses Dokument bündelt die Gesamtplanung für v5.
