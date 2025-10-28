---
version: 1
schema: checklist/v1
description: SpocR CLI Delivery Checklist (feature branch)
generated_for: ai-agent
branch_scope:
  note: "Branch-spezifisch (feature/vnext); vor Merge in master entfernen"
status_conventions:
  open: "[ ]"
  done: "[x]"
  deferred: "[>]"
  partial: "[~]"
categories:
  - roadmap
  - migration
  - snapshotbuilder
  - json
  - quality
  - documentation
  - release
  - automation
depends_naming: "ID Referenzen in depends Feld"
---

> Hinweis: Diese Checkliste ist branch-spezifisch (`feature/vnext`) und muss vor einem Merge in `master` entfernt oder archiviert werden.

Status-Legende: `[ ]` offen, `[x]` erledigt, `[>]` deferred, `[~]` teilweise umgesetzt.

## Überblick 2025-10-27

- SnapshotBuilder pipeline treibt `spocr pull`; Detailplanung liegt in `src/SpocRVNext/SnapshotBuilder/CHECKLIST.md`.
- Forced-Upgrade-Plan v5 wird in `src/SpocRVNext/CHECKLIST.md` geführt; dieses Dokument bündelt den Gesamtstatus.
- JSON Typisierung läuft AST-first, Restarbeiten und Tests sind unten verlinkt.

## Abgeschlossene Eckpunkte

- [x] Legacy-Generator v4.5 eingefroren (Sentinel `legacy-freeze.txt`, EPIC E001).
- [x] Parallelbetrieb alter/neuer Output abgeschlossen; deterministische Hashes sichern den vereinheitlichten Generator.
- [x] `.env`-Migration samt Bootstrap und Warnpfad umgesetzt (README aktualisiert).
- [x] Golden-Hash-Pipeline & Diff-Reporting aktiv (Relaxed Mode, CLI-Befehle `write-golden`/`verify-golden`).
- [x] Sample `samples/restapi` baut und besteht CRUD-Smoke über die neue Pipeline.
- [x] Generator-Mode-Toggles entfernt; CLI, Tests und Doku arbeiten ausschließlich im next-only Modus (2025-10-29).

## Roadmap & Migration (Abgleich mit `src/SpocRVNext/CHECKLIST.md`)

- [ ] Zielarchitektur v5 (Abhängigkeiten, Optimierungs-Backlog) final dokumentieren und hier verlinken.
- [~] Inventar `DataContext/` + `spocr.json` Konsumenten schließen; Ablösepfad nachhalten. (Legacy `DataContext/` Artefakte entfernt, verbleibende `spocr.json` Prüfrunden offen)
  - 2025-10-29: CLI ignoriert `spocr.json`; Tests angepasst, Rest-Inventar (Skripte/CI) offen.
  - 2025-10-29: Entwickler-Dokus & determinism workflow auf Projektpfad (`-p <dir>`) aktualisiert; verbleibende Legacy-Referenzen in Roadmap-Dokumenten prüfen.
- [ ] Neue CLI (`init`, `pull`, `build`, `rebuild`) finalisieren und Kommunikationspaket vorbereiten.
- [ ] Teststrategie v5 definieren (Smoke/Integration vs. Legacy-Abschaltung) und CI entsprechend planen.
- [ ] Forced-Upgrade Kommunikation (Zeitplan, Beta-Programm, Supportkanäle) aufsetzen.
- [ ] DbContext-Implementierung zu schlankem DB-Adapter für die `spocr pull`-Pipeline umbauen (Basis für `src/SpocRVNext/Templates/DbContext`).
- [ ] Guardrails für DbContext-Oberflächen definieren (interner Kontext darf Ad-hoc/Diagnostics, generierter Kontext nur Execute-Aufrufe) und Tests/Docs ableiten.
- [ ] Klare Trennung „SpocR Source“ vs. „SpocR Runtime“ ausarbeiten (Packages/Namespaces/Deploymentpfade) und im Architektur-Abschnitt dokumentieren.
- [~] Post-Migration Repo-Aufspaltung planen: neues Repository `nuetzliches/xtraq` mit Namespace `Xtraq`, Startversion `1.0.0`, keine historischen SpocR-Verweise; SpocR wird bei v4.5 eingefroren und verweist auf Xtraq.
  - `CHANGELOG.md`, `README.md`, `MIGRATION_SpocRVNext.md` und `migration-v5.instructions` dokumentieren den Xtraq-Nachfolger (2025-10-29).

## SnapshotBuilder & Analyzer (vgl. `src/SpocRVNext/SnapshotBuilder/CHECKLIST.md`)

- [ ] Tests und Golden Snapshots auf neuen Artefakt-Output heben (`SpocR.Tests`, Determinism).
- [~] Obsolete Snapshot-Felder (`JsonPath`, `JsonResult`, `DeferredJsonColumns`) entfernen, Konsumenten migrieren.
  - SchemaSnapshotService & SchemaMetadataProvider bereinigt; Writer/Tests folgen.
- [x] Analyzer-Verbesserungen dokumentieren und Telemetrie-Läufe (`--no-cache --verbose`) archivieren. (Referenz: `src/SpocRVNext/SnapshotBuilder/README.md` Abschnitt "Diagnostics & Type Resolution Runs"/"Snapshot Summary Payload")
- [x] Snapshot-Schritte für Migration (`migration-v5.instructions`) ergänzen. (Siehe `migration-v5.instructions`)
- [ ] Legacy-Bridge abbauen, sobald Abschlusskriterien (grüne Tests, deterministischer Pull) erfüllt sind.

## JSON Typisierung & AST

- [x] Dokumentation "JSON Procedure Handling" (Deserialisierung, Flags, `JSON_QUERY`-Konvention) schreiben. (Siehe `docs/content/3.reference/json-procedures.md`)
- [ ] AST-Nacharbeiten: `identity.RecordAsJson` Heuristik entfernen, `FOR JSON` Strict Mode Flag finalisieren.
- [ ] JSON ColumnRef Binding verbessern (Alias->Quelle) und unresolved-Logs reduzieren.
- [ ] JSON Metrics sammeln (resolve vs. fallback) und optional in `debug/test-summary.json` persistieren.
- [ ] Performance-Vergleich Raw vs. Aggregation bewerten (optional).

## Qualität & Tests

- [ ] Coverage-Baseline >=60 % messen und Reporting einschalten (Vorbereitung Strict Golden Hash).
- [x] Negative Tests für ENV-Kombinationen (z.B. fehlende DB-Verbindung) ergänzen. (2025-10-29)
- [ ] `eng/quality-gates.ps1` in CI integrieren oder dokumentieren (inkl. `eng/kill-testhosts.ps1`).
- [ ] Namespace-Kollisionstests für konsolidierte Outputs (Multi-ResultSet) ergänzen.
- [ ] Test-Hosts Cleanup in Doku/CI verankern.
- [ ] Review-Findings (Konzeptfehler, unsauberer Code, Unschärfen, fehlende Tests/Qualität) laufend pflegen und priorisieren.
  - [x] RestApi-Sample kompiliert nach Result-Typ-Refresh (`dotnet build samples/restapi/RestApi.csproj -c Debug`, 2025-10-28). Endpunkte bleiben bewusst per `#if false` deaktiviert, bis JSON-Aggregate finalisiert sind.
  - [x] Debug-Sandbox `.env` bereinigt; `SPOCR_GENERATOR_MODE` entfernt, next-only Standard bestätigt (2025-10-29).

## Dokumentation & Kommunikation

- [x] Rewrite `README.md` to remove historical context, focus on the SpocR CLI value proposition (DB admins enabling BI layers; developers working with stored procedures only), and push deep technical documentation to the GitHub Pages site. (2025-10-27)
- [ ] Docs/content: keep GitHub Pages articles focused on current CLI behavior; migrate historical or migration notes to the legacy stream.
  - 2025-10-29: MIGRATION-Seiten auf Zielzustand gebracht; restliche `docs/content` Artikel noch auf IST kürzen.
  - 2025-10-29: `docs/content/3.reference/configuration-schema.md` auf `.env`/`SPOCR_*` umgestellt; Roadmap-Seiten mit legacy Fokus offen.
  - 2025-10-29: MIGRATION Docs auf Zielzustand gesetzt, Legacy-Narrative in Hauptseiten noch zu kürzen.
- [x] README/Docs: Namespace-Ableitung & Override mit Beispiel diff ergänzen. (docs/content/3.reference/env-bootstrap.md)
- [ ] CHANGELOG v4.5-rc/v5 vorbereiten (Removed Keys, neue CLI, Bridge Policy).
- [ ] Migration Guide `MIGRATION-V5.md` + `migration-v5.instructions` synchronisieren.
  - 2025-10-29: `migration-v5.instructions` auf SOLL-Status eingeschränkt, MIGRATION Guide auf Zielzustand fokussiert; finale Abnahme ausstehend.
- [ ] Docs Build (Nuxt/Bun) verifizieren und Deployment-Workflow (`docs-pages`) planen.
- [~] TableType/JSON Änderungen in Doku nachziehen (Verweis auf neue Artefakte).
  - JSON Snapshot Flags aktualisiert (`docs/content/3.reference/json-procedures.md`).
- [~] SpocR Freeze-Kommunikation dokumentieren: v4.5 weist auf `nuetzliches/xtraq` (Namespace `Xtraq`, Version `1.0.0`) als Nachfolger hin.
  - README, CHANGELOG, Migration Guide & Instructions um Hinweis auf Xtraq ergänzt (2025-10-29).
- [ ] Inhalte aus `src/SpocRVNext` eine Ebene höher ziehen und konsolidierte Struktur dokumentieren.
- [ ] `.ai/` Inhalte nach jeder relevanten Änderung prüfen und synchronisieren (Guidelines, Prompts, README).

## CI, Automatisierung & Release

- [ ] Diff-/Model-Report Workflow (`debug/model-diff-report.md`) stabilisieren und in Builds referenzieren.
- [ ] Dokumentation "Golden Hash Update Workflow" (manuelles `write-golden`) ergänzen.
- [ ] NuGet/Bun Cache-Strategie für CI optimieren.
- [ ] Release-Vorbereitung: Version bump, Release Notes, NuGet Smoke Test, Clean Git Status orchestrieren.
- [ ] Upgrade-Safety Tests erweitern (Minor erlaubt, Direct-Major mit Override protokolliert) & Env Override Doku ergänzen.

## Sicherheit & Wartung

- [ ] Secret-Scan (Connection Strings) automatisieren oder dokumentieren.
- [ ] `dotnet list package --outdated` Review durchführen und sicherheitsrelevante Updates planen.
- [ ] Lizenzprüfung der genutzten NuGet-Pakete durchführen.
- [ ] DB-Testnutzer auf Least-Privilege prüfen und festhalten.
- [ ] Roslyn-, `McMaster.Extensions.CommandLineUtils`-, `Microsoft.AspNet.WebApi.Client`- und `Microsoft.CodeAnalysis.CSharp`-Abhängigkeiten auf vNext-Relevanz prüfen und nach Möglichkeit entfernen.

## EPIC Status (Kurzfassung)

- [x] E001 Legacy-Freeze v4.5
- [x] E002 Sample-Gate Referenz-Sample stabil
- [x] E003 Neue Generator-Grundstruktur
- [x] E004 Neuer Output & Dual Generation
- [ ] E005 Eigene Template Engine (Restarbeiten)
- [ ] E006 Moderner DbContext & APIs (Sample-Stabilisierung, Doku)
- [x] E007 Heuristik-Abbau abgeschlossen (Dokumentation finalisieren)
- [ ] E008 Konfig-Bereinigung (Removed-Abschnitt offen)
- [x] E009 Auto Namespace Ermittlung aktiv
- [ ] E010 Cutover Plan v5.0
- [ ] E011 Obsolete Markierungen
- [ ] E012 Dokumentations-Update
- [ ] E013 Test-Suite Anpassung
- [ ] E014 Erweiterte Generatoren
- [x] E015 SnapshotBuilder Basis fertig - Restarbeiten siehe Abschnitt oben

## Deferred / v5-Backlog (Kurzliste)

- [>] Streaming-APIs & JSON Dual Mode (`JsonRawAsync`, `JsonStreamAsync`, ...).
- [>] Functions/Views Snapshot Erweiterungen (Dependencies, Dokumentation).
- [>] Strict Golden Hash & Diff Exit Codes nach Coverage-Gating.
- [x] Entfernung verbleibender `spocr.json`-Fallbacks und Namespace Overrides. (2025-10-29)
- [>] TableType Validation/Builder Verbesserungen (FluentValidation, Factory Overloads).
- [>] Procedure Invocation Patterns & Streaming Doku.

## Debugging / Dev

- Schema rebuild: `dotnet run --project src/SpocR.csproj -- rebuild -p debug`

## Testing Quick Reference

- Schema rebuild: `dotnet run --project src/SpocR.csproj -- rebuild -p samples/restapi`
- Sample build: `dotnet build samples/restapi/RestApi.csproj -c Debug`

> Fortschritt bitte regelmäßig mit `src/SpocRVNext/CHECKLIST.md` und `src/SpocRVNext/SnapshotBuilder/CHECKLIST.md` abgleichen; dieses Dokument bündelt die Gesamtübersicht für den Branch `feature/vnext`.
