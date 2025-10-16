---
version: 1
schema: checklist/v1
description: SpocR Entwicklungs- & Migrations-Checkliste für Übergang zu SpocRVNext
generated_for: ai-agent
branch_scope:
      note: 'Branch-spezifisch (feature/vnext); vor Merge in master entfernen'
status_conventions:
	open: '[ ]'
	done: '[x]'
categories:
	- epics
	- quality
	- migration
	- documentation
	- release
	- automation
depends_naming: 'ID Referenzen in depends Feld'
---

> HINWEIS: Diese Checkliste ist BRANCH-SPEZIFISCH (`feature/vnext`) und soll VOR dem Merge in `master` GELÖSCHT oder ARCHIVIERT werden.

## Fokus & Prioritäten (Snapshot)

Legende Prioritäten: P1 = kritisch für v5 Cutover, P2 = hoch für Bridge (v4.5→v5), P3 = sinnvoll vor Release, P4 = nachgelagert / Nice-to-have.

Aktueller Fokus (Top 10 P1/P2) – Update 2025-10-15:

1. (P1) E014 End-to-End Nutzung mind. einer Stored Procedure im Sample – Roundtrip stabilisieren (Timeout/Ping Optimierung)
2. (P1) E013 Test-Suite Ausbau: Multi-Result / unparsable SQL / Abschnittsreihenfolge Tests
3. (P1) ResultSet Naming Dokumentation & Beispiele (Resolver already always-on)
4. (P1) Golden Hash Strict Mode Entscheid + README / Policy (derzeit relaxed)
5. (P1) Coverage Gate Anhebung (Roadmap: 30%→50%→60%+; Ziel Core ≥80%) + CI Enforcement
6. (P2) E005 Template Engine Edge-Case Tests & Scope Freeze (keine Direktiven vor v5)
7. (P2) E006 DbContext Stabilisierung & Logging Verbesserung im Sample
8. (P2) E008 Konfig-Bereinigung: Mapping Tabelle Env vs. Legacy finalisieren + CHANGELOG Removed
9. (P2) E010 Cutover Plan konkretisieren (Timeline + Abhängigkeiten) – README/Migration synchronisieren
10. (P2) Quality-Gates Script CI Integration + Badges (Smoke/Determinism/Coverage/Quality)

Kurzfristig depriorisiert (Beispiele P3/P4): Performance Profiling, Nested Template Loops, Strict-Diff Eskalation, Minor Nullability Phase 2.

---

# Testing

Das Testing erfolgt aktuell aus den SQL-Daten: samples\mssql\init
Daraus wird mit dem Befehl:

```bash
dotnet run --project src/SpocR.csproj -- rebuild -p samples/restapi/spocr.json
```

das Schema samples\restapi.spocr\schema produziert.
Daraus ensteht dann der Output in samples\restapi\SpocR

Dann den Build prüfen mit:

```bash
dotnet build samples/restapi/RestApi.csproj -c Debug
```

Legende: `[ ]` offene Aufgabe · `[x]` erledigt

Status-Update (2025-10-16): Alle Quellcode-Kommentare/Bezeichner auf Englisch vereinheitlicht (Internationalisierung abgeschlossen). Diese Checkliste bleibt bewusst deutsch bis zum Merge.

EPICS Übersicht (oberste Steuerungsebene)

- [x] EPIC-E001 LEGACY-FREEZE v4.5
      id: E001
      goal: Generator-Code für bisherigen DataContext einfrieren (nur kritische Bugfixes)
      acceptance: - Keine funktionalen Änderungen an Legacy-Generator nach Freeze-Datum - Nur sicherheits-/stabilitätsrelevante Fixes - Dokumentierter Freeze in CHANGELOG
      depends: []
      note: Freeze-Datum & Sentinel (legacy-freeze.txt) gesetzt; CHANGELOG Eintrag vorhanden

- [x] EPIC-E002 SAMPLE-GATE Referenz-Sample stabil (P1) (smoke-test.ps1 + CI + DB Workflow + determinism verify vorhanden)
      id: E002
      goal: Referenz-Sample `samples/restapi` validiert jeden Entwicklungsschritt
      acceptance: - Build + CRUD Smoke Test automatisiert - Nutzung aktueller `spocr.json` - Läuft in CI Pipeline
      depends: []

- [x] EPIC-E003 Neuer Generator Grundstruktur
      id: E003
      goal: Ordner `src/SpocRVNext` + Basis-Architektur steht
      acceptance: - Projektstruktur vorhanden - Einstiegspunkt Codegenerierung implementiert - Minimaler End-to-End Durchlauf erzeugt leeren oder Basis-Output
      depends: [E001]
      note: Ordner + Einstieg (DualGenerationDispatcher, Generator) + Template Engine initial vorhanden

- [x] EPIC-E004 Neuer Output & Dual Generation
      id: E004
      goal: Neuer Output-Pfad + parallele Erzeugung mit Legacy
      acceptance: - Neuer Output-Ordner erzeugt deterministische Dateien - Alter Output unverändert - Flag / Schalter aktiviert Dual Mode
      depends: [E003]
      note: Dual Mode via ENV Flag SPOCR_GENERATOR_MODE=dual; Timestamp neutralisiert; Doppel-Write entfernt

- [ ] EPIC-E005 Eigene Template Engine (P2)
      id: E005
      goal: Roslyn Unabhängigkeit & eigene schlanke Template Pipeline
      acceptance: - Template Parser / Renderer modular - Unit Tests für Placeholder/Substitutions - Standardisierter Header (auto-generated Block) integriert
      depends: [E003]

- [ ] EPIC-E006 Moderner DbContext & APIs (P2)
      id: E006
      goal: `SpocRDbContext` + Minimal API Extensions
      acceptance: - DI Registrierung (IServiceCollection) vorhanden - Minimal API Mappings generierbar - Beispiel-Endpunkt im Sample funktioniert
      depends: [E003]

- [ ] EPIC-E007 Heuristik-Abbau (P3)
      id: E007
      goal: Entfernung restriktiver Namens-/Strukturheuristiken
      acceptance: - Liste entfernte / geänderte Heuristiken dokumentiert (Dokumentation ausreichend, kein vollständiger Audit) - Regressionstests schützen kritische Fälle
      depends: [E003]

- [ ] EPIC-E008 Konfig-Bereinigung (P2)
      id: E008
      goal: Entfernte Properties aus `spocr.json` offiziell deklariert
      acceptance: - CHANGELOG Abschnitt "Removed" - Upgrade Guide Eintrag
      depends: [E004]

- [x] EPIC-E009 Auto Namespace Ermittlung
      id: E009
      goal: Automatisierte Namespace Generierung + Fallback
      acceptance: - 90%+ Fälle ohne manuelle Angabe korrekt - Fallback Logik dokumentiert
      depends: [E003]

- [ ] EPIC-E010 Cutover Plan v5.0 (P2)
      id: E010
      goal: Plan zur Entfernung Legacy DataContext
      acceptance: - README / ROADMAP Eintrag - Timeline + Migrationsschritte
      depends: [E004, E008]

- [ ] EPIC-E014 Erweiterte Generatoren (Inputs/Outputs/Results/Procedures) (P1)
      id: E014
      goal: Generatoren für Eingabe-/Ausgabe- und Prozedur-Artefakte
      acceptance: - Templates für Inputs, Outputs, Results, Stored Procedures vorhanden - Basis-Generatoren erzeugen konsistente Namespaces - Mindestens 1 End-to-End Beispiel im Sample eingebunden
      depends: [E003, E005]

- [ ] EPIC-E011 Obsolete Markierungen (P3)
      id: E011
      goal: Alte Outputs als [Obsolet] markiert mit Klartext-Hinweis
      acceptance: - Alle Legacy Artefakte dekoriert / kommentiert - Build Warnungen optional aktivierbar
      depends: [E010]

- [ ] EPIC-E012 Dokumentations-Update (P1)
      id: E012
      goal: Vollständige Doku für neuen Generator
      acceptance: - Architektur, Migration, CLI Referenz - Samples verlinkt & aktuell
      depends: [E004, E005, E006, E007, E008, E009]

- [ ] EPIC-E013 Test-Suite Anpassung (P1)
      id: E013
      goal: Tests spiegeln neue Architektur & schützen Migration
      acceptance: - Snapshot / Golden Master - Cover ≥ 80% Core
      depends: [E005, E006, E007, E009]

---

### Operative Tasks aus EPICS (Detail-Aufgaben folgen unter thematischen Sektionen)

### Qualität & Tests (Update 2025-10-15)

- [x] Alle bestehenden Unit- & Integrationstests grün (Tests.sln)
- [ ] Neue Tests für SpocRVNext (Happy Path + Fehlerfälle + Regression für entfernte Heuristiken)
- [ ] (Optional) Info-Diff zwischen Legacy und neuem Output generiert (kein Paritäts-Zwang)
- [ ] Automatisierte Qualitäts-Gates (eng/quality-gates.ps1) lokal und in CI erfolgreich
- [ ] Test-Hosts nach Läufen bereinigt (eng/kill-testhosts.ps1) – kein Leak mehr
- [ ] Code Coverage Mindestschwelle definiert und erreicht (Ziel: >80% Core-Logik)
      note: Aktuell nur initiale niedrige Schwelle dokumentiert; Eskalation & Durchsetzung offen
- [ ] Negative Tests für ungültige spocr.json Konfigurationen
- [x] Test: TableTypes Name Preservation (`PreservesOriginalNames_NoRenaming`) sichert unveränderte UDTT Bezeichner
- [x] Entfernte Suffix-Normalisierung für TableTypes (Regression abgesichert)
- [x] Konsolidierte Prozedur-Datei Test (keine Duplikate Input/Output + deterministischer Doppel-Lauf)
- [x] ResultSet Rename + Collision Tests (Resolver Basis abgesichert)

### Codegenerierung / SpocRVNext

- [x] Template Engine Grundgerüst fertig (ohne Roslyn Abhängigkeiten)
- [x] Ermittlung des Namespaces automatisiert und dokumentierte Fallback-Strategie vorhanden
- [ ] Entfernte Spezifikationen/Heuristiken sauber entfernt und CHANGELOG Eintrag erstellt
- [ ] Neuer `SpocRDbContext` implementiert inkl. moderner DI Patterns & Minimal API Extensions - [x] Grundgerüst via Template-Generator (Interface, Context, Options, DI) – aktiviert in `SPOCR_GENERATOR_MODE=dual|next` (ehem. Flag `SPOCR_GENERATE_DBCTX` entfernt) - [x] DbContext Optionen (ConnectionString / Name / Timeout / Retry / Diagnostics / ValidateOnBuild) implementiert - [x] Scoped Registration Validierung (Connection Open Probe optional via `ValidateOnBuild`) - [x] Minimal API Mapper Beispiel (Health Endpoint `/spocr/health/db`) - [~] Integration ins Sample (Code registriert & Endpoint gemappt; laufender Prozess beendet sich noch früh – Stabilisierung ausstehend / Doku fehlt)
- [x] Parallel-Erzeugung alter (DataContext) und neuer (SpocRVNext) Outputs in v4.5 (Demo/Beobachtungsmodus) implementiert
- [x] Legacy CLI ruft bei `SPOCR_GENERATOR_MODE=dual` zusätzlich vNext Dispatcher (nur .env / EnvConfiguration, ohne spocr.json Nutzung) auf
- [x] Schalter/Feature-Flag zum Aktivieren des neuen Outputs vorhanden (CLI Parameter oder Konfig)
- [x] Konsistenz-Check für generierte Dateien (Determinismus pro Generator; keine Legacy-Paritäts-Pflicht) – Hash Manifeste vorhanden (noch keine harte Policy) - [x] Timestamp-Zeile neutralisiert (Regex Normalisierung) - [x] Doppelter Schreibpfad Outputs/CrudResult entfernt (Skip base copy)
      note: Konsistenz-Check für generierte Dateien (Determinismus pro Generator; keine Legacy-Paritäts-Pflicht) – Hash Manifeste vorhanden (noch keine harte Policy); Timestamp-Zeile neutralisiert (Regex Normalisierung); Doppelter Schreibpfad Outputs/CrudResult entfernt (Skip base copy)
- [x] TableTypes: Always-On Generation (Interface `ITableType` einmalig, Records je Schema unter `SpocR/<schema>/`) integriert in Build (dual|next)
- [x] TableTypes: Timestamp `<remarks>` Zeile eingefügt und beim Hashing ignoriert (DirectoryHasher Filter)
- [x] TableTypes: Original Snapshot Namen vollständig beibehalten (nur Sanitizing) – keine erzwungene \*TableType Suffix Ergänzung

      Template Root Vereinfachung
      - [x] Entfernt: `TemplateRootResolver` + ENV Override (`SPOCR_TEMPLATES_ROOT`) – Templates werden jetzt deterministisch aus `ApplicationRoot/src/SpocRVNext/Templates` geladen
      - [x] Warn-Logging angepasst: Meldung bei fehlender `UnifiedProcedure.spt` zeigt nur noch "Templates-Pfad prüfen" (keine veralteten Resolver-Hinweise)

      Standardisierung Header / Timestamp
      - [x] Gemeinsames Header-Template (`_Header.spt`) mit `// <auto-generated/>` + Hinweis Bridge Phase v4.5
      - [x] Alle bestehenden Templates nutzen Header-Include / Präfix (TableTypes + neue Artefakte)
      - [x] Entscheidung Timestamp: verbleibt nur in `<remarks>` Zeilen; nicht im Header (Hash-stabil)

      Erweiterte Generatoren (E014)
      - [x] Template + Generator: Inputs (Parameter-DTOs / Value Objects)
      - [x] Template + Generator: Outputs (DTO/Records)
      - [x] Template + Generator: Results (Operation Result Types)
      - [x] Template + Generator: StoredProcedure Wrapper (Execution Stubs Grundgerüst)
      - [x] Per-Schema Dateilayout für neue Artefakte umgesetzt (`samples/restapi/[schema]/[Proc]Input.cs|Output.cs|Result.cs|Aggregate.cs|Plan.cs|Procedure.cs` + Row Sets `_ResultSetNameResult.cs`)
      - [x] High-Level Result Records in per-Schema Layout verschoben (`[Proc]Result.cs`)
      - [x] Execution Logic ADO.NET (ResultSets Mapping) implementiert (ProcedureExecutionPlan + ProcedureExecutor)
      - [x] Metadata Provider Implementierung (DB Schema → Descriptors) produktiv (SchemaMetadataProvider)
      - [~] CLI Integration (`spocr generate` nutzt neue Generatoren) – Legacy Orchestrator ruft vNext Generator jetzt in dual|next auf; eigene vNext CLI Ergänzungen folgen
      - [~] Sample nutzt mindestens eine generierte Stored Procedure (Endpoints implementiert, noch Fehler 500 bei UserList)
      - [ ] ResultSet Naming Strategie dokumentiert (Resolver aktiv; Beispiele ergänzen)
      - [~] Tests: Snapshot / Determinismus für neue Artefakte (Basis vorhanden: Golden + Konsolidierte Procs; ausstehend: RowSet / Konfliktfälle)
      - [ ] Interaktive .env Bootstrap CLI (separate vNext Kommando) – Basis EnvBootstrapper vorhanden, noch kein dedizierter Befehl

      Hinweis: ResultSetNameResolver aktiv (always-on) – nutzt persistiertes `Sql` Feld; ersetzt nur generische Namen kollisionsfrei.

      TODO entfernt: Performance Messung (nicht mehr erforderlich)

### Migration / Breaking Changes (Update 2025-10-15)

note: Konfig-Keys `Project.Role.Kind`, `RuntimeConnectionStringIdentifier`, `Project.Output.*` sind ab 4.5 als obsolet markiert – tatsächliche Entfernung erfolgt erst mit v5. (Siehe Deprecation Timeline in `MIGRATION_SpocRVNext.md`)

- [ ] Alle als [Obsolet] markierten Typen enthalten klaren Hinweis & Migrationspfad
- [ ] Dokumentierter Cut für v5.0 (Entfernung DataContext) in README / ROADMAP
- [ ] Liste entfallener Konfig-Properties (Project.Role.Kind, RuntimeConnectionStringIdentifier, Project.Output) im Changelog
      note: CHANGELOG enthält bislang keinen Removed-Abschnitt für diese Keys
- [x] Migration von `spocr.json` auf `.env` / Environment Variablen dokumentiert (Mapping Tabelle)
      note: Precedence aktualisiert (CLI > ENV > .env > spocr.json Fallback nur in dual|next wenn SPOCR_GENERATOR_DB fehlt). Fallback & Override implementiert in EnvConfiguration.
- [ ] Upgrade Hinweise in README + CHANGELOG integriert (kein separater Guide in dieser Phase)
- [ ] SemVer Bewertung durchgeführt (Minor vs. Major Bump begründet)
      note: Entscheidungskriterium: Entfernen Legacy DataContext + Identifier Fallback = Major (v5); v4.5 nur Bridge.

### Ziel-Framework spezifische Features

- [ ] Gating: `SpocRDbContextEndpoints` nur für `net10.0` kompilieren (Analyzer/Conditional Compilation) – Dokumentation verlinken
      note: Ältere TFs (net8/net9) erhalten nur DbContext + HealthCheck optional via manuelle Registrierung
- [ ] README Abschnitt "Target Framework Matrix" (Endpoint Availability) ergänzen

### Migration Bootstrap (.env Erst-Erstellung)

- [x] Erster v4.5 Build ohne `.env` / ohne jeden `SPOCR_` Key zeigt interaktive Migration-Warnung
- [x] Nutzer-Bestätigung erforderlich bevor `.env` geschrieben wird (Abbruch bei "nein")
- [x] `.env` wird aus `samples/restapi/.env.example` (Template) + Werten aus `spocr.json` gemerged
- [x] `.env.example` als autoritative Vorlage gekennzeichnet (Kommentar Kopfzeile)
- [x] Logging: Klarer Hinweis auf nächste Schritte (Namespace prüfen, Generator Mode optional anpassen)

### Logging & Messaging Alignment

- [ ] Log Message "vNext (dual) – TableTypes" konsolidieren → Ein einheitlicher Block: `vNext: Generating TableTypes` oder in Gesamtdauer-Zusammenfassung integrieren
      note: Doppelung/Verlust Kontext aktuell – gehört in Sequenz zu anderen Generator Steps

### Konfiguration & Artefakte (Update 2025-10-15)

- [ ] Beispiel `spocr.json` im Sample aktualisiert (ohne entfallene Properties)
- [ ] Validierungsskript/Schema für spocr.json hinzugefügt oder aktualisiert
- [ ] Debug-Konfigurationen (debug/\*.json) konsistent mit neuen Pfaden
- [ ] Output-Pfade (Output/, Output-v5-0/, etc.) aufgeräumt / veraltete entfernt sofern Version >=5.0 (post-migration)
- [x] `.env` Beispieldatei hinzugefügt (Pfad: `samples/restapi/.env.example`) inkl. aller relevanten SPOCR\_\* Keys
      note: Enthält SPOCR_GENERATOR_MODE, SPOCR_EXPERIMENTAL_CLI, Bridge Policy Flags – `.env` ausschließlich Generator-Scope (kein Runtime Connection String). Entfernte Idee eines dedizierten Runtime DB Env Vars ersatzlos gestrichen. Precedence dokumentiert (README aktualisiert). Namespace / Output Dir Prefill via `.env` Bootstrap weiterhin möglich.
- [ ] `spocr pull` überschreibt lokale Konfiguration nicht mehr (nur interne Metadaten)

### Dokumentation (Update 2025-10-15)

- [ ] docs Build läuft (Bun / Nuxt) ohne Fehler
- [ ] Neue Seiten für SpocRVNext (Architektur, Unterschiede, Migration) hinzugefügt
- [ ] Referenzen (CLI, Konfiguration, API) aktualisiert
- [ ] README Quick Start an neuen Generator angepasst
      note: Quick Start Beispiel zeigt alten DataContext Stil; vNext Beispiel ergänzen
- [ ] Doku: TableTypes Abschnitt (Naming-Preservation, Timestamp `<remarks>` & Hash-Ignore, Interface `ITableType`, Schema-Unterordnerstruktur) in docs/3.reference oder 2.cli verlinkt
- [ ] CHANGELOG.md Einträge für jede relevante Änderung ergänzt (Added/Changed/Removed/Deprecated/Migration Notes)
- [ ] DEVELOPMENT.md enthält und pflegt kuratierte Entwicklungs-Commands (Build, Codegen, Tests, Diffs, Cleanup) – Liste aktuell und wird vor PR zum master bereinigt.
- [ ] Samples/README verlinkt auf aktualisierte Doku
- [ ] Docs aktualisiert für v4.5 als Übergangsrelease (Kennzeichnung 'v4.5 (Bridge Phase)')
- [ ] Version-Schalter (docs/content config) vorbereitet: aktuelle v4.5 + zukünftige v5 Platzhalter
- [ ] Inhalte mit Versions-Hinweisen versehen (Abschnitt 'Gilt für: v4.5' / 'Ändert sich in v5')
- [ ] Platzhalter-Seiten für v5 Unterschiede angelegt (Migration, API Changes, Entfernte Heuristiken)
- [ ] content.config.ts erweitert um Versions-Metadaten (z.B. versions: ['4.5','5.0'])
- [ ] Hinweisbanner in v4.5 Seiten: "Sie lesen die v4.5 Dokumentation – v5 in Vorbereitung"

### Docs Deployment (GitHub Pages) – Planung

- [ ] Nuxt Static Generation konfigurieren (`bun run generate`) erzeugt vollständiges Prerender ohne SSR-Abhängigkeiten
- [ ] `nuxt.config.ts`: `nitro.static` / `routeRules` prüfen; `app.baseURL` für Pages (`/spocr/`) setzen falls kein CNAME
- [ ] Build-Workflow `.github/workflows/docs-pages.yml` anlegen (Branch `gh-pages` Deploy)
- [ ] Cache (bun) + Node Version (>= 20) in Workflow
- [ ] Artefakt-Publish: `docs/.output/public` oder `.dist` Ordner je nach Nuxt Version verifizieren
- [ ] 404 Handling (`404.html`) erzeugen (Nuxt auto oder manuell) für SPA History Fallback
- [ ] Link-Check Schritt (externe + interne) vor Deploy
- [ ] Badge im README (Docs Status / Pages URL)
- [ ] Checklist Update: Deployment aktiviert & erster erfolgreicher Publish

### Samples / Demo (samples/restapi) (Update 2025-10-15)

- [x] Sample baut mit aktuellem Generator (dotnet build)
      note: Build auf Branch erfolgreich (15.10.2025)
- [~] Sample führt grundlegende DB Operationen erfolgreich aus (CRUD Smoke Test) – Script vorhanden - Vorher: 500 UserList (InvalidCast) → behoben - Aktuell: DB-Ping Timeout blockiert vor Prozedur-Aufruf
- [~] Automatisierter Mini-Test (skriptgesteuert) prüft Generierung & Start der Web API (smoke-test.ps1 vorhanden, CI Integration fehlt)
- [ ] Sample beschreibt Aktivierung des neuen Outputs (Feature Flag) im README
- [ ] Schema Rebuild Pipeline (`dotnet run --project src/SpocR.csproj -- rebuild -p samples/restapi/spocr.json --no-auto-update`) erzeugt deterministisch `samples/restapi/.spocr/schema`
- [~] Generierter Output in `samples/restapi/SpocR` deterministisch (Golden Hash Feature implementiert, CI Verify offen) - Golden Write/Verify verfügbar, noch nicht in CI
- [x] Namespace-Korrektur: `samples/restapi/SpocR/ITableType.cs` → `namespace RestApi.SpocR;`
- [ ] Namespace-Korrektur: Dateien unter `samples/restapi/SpocR/samples/` → `namespace RestApi.SpocR.samples;`
- [x] Namespace-Korrektur: Dateien unter `samples/restapi/SpocR/samples/` → vereinheitlicht zu `namespace RestApi.SpocR.<SchemaPascalCase>` (ohne Kategorie-Segmente)

### Sicherheit & Compliance

- [ ] Keine geheimen Verbindungsstrings / Secrets committed (Review via Suche nach "Password=" / ";User Id=")
- [ ] Abhängigkeiten aktualisiert (dotnet list package --outdated geprüft) – sicherheitsrelevante Updates eingespielt
- [ ] Lizenz-Hinweise unverändert kompatibel (LICENSE, verwendete NuGet Packages)
- [ ] Minimale Berechtigungen für DB Tests (Least Privilege Account)

### Performance & Wartung

- [ ] Start-zu-Generierungszeit gemessen & dokumentiert
- [ ] Speicherverbrauch während Codegeneration einmal profiliert (nur grober Richtwert)
- [ ] Kein übermäßiger Dateichurn (idempotenter Output)
- [ ] Logging reduziert auf sinnvolle Defaults (kein unnötiger Lärm im CI)

### Release Vorbereitung

- [ ] Version in `src/SpocR.csproj` und ggf. weiteren Projekten angehoben
- [ ] Tag / Release Notes vorbereitet (Aus CHANGELOG generiert)
- [ ] Git Clean Status vor Tag (keine uncommitted Changes)
- [ ] CI Pipeline für Release Branch erfolgreich durchgelaufen
- [ ] NuGet Paket lokal gebaut & installiert (Smoke Test CLI)
- [ ] Signierung/Authentizität geprüft (falls relevant)

### Nach dem Release

- [ ] Veröffentlichung auf GitHub (Release + Tag) erfolgt
- [ ] Paket im NuGet Index sichtbar & Version abrufbar
- [ ] Quick Start Schritt-für-Schritt mit neuer Version einmal frisch durchgespielt
- [ ] Erste Issues / Feedback-Kanal beobachtet (24-48h Monitoring)
- [ ] Roadmap

### Automatisierung / CI (Update 2025-10-15)

- [ ] Pipeline Schritt: Codegen Diff (debug/model-diff-report.md) aktuell und verlinkt
- [ ] Fail-Fast bei unerwarteten Generator-Änderungen (Diff Threshold)
- [ ] QA Skripte (eng/\*.ps1) in README oder DEVELOPMENT.md referenziert (smoke-test.ps1 erwähnen; Golden Hash Feature optional separat dokumentieren)
      -- [x] CI: Smoke Test Schritt (smoke-test.ps1) integriert (`smoke.yml`)
      -- [x] CI: DB Smoke (SQL Service Container) integriert (`db-smoke.yml`)
      -- [x] CI: Determinism Verify separater Workflow (`determinism.yml`)
      -- [ ] (Optional) Golden Hash Strict Mode aktivieren (Policy Eskalation)
      -- [ ] CI: Manual Trigger / Dokumentation für Golden Hash Aktualisierung (-WriteGolden) vorhanden
- [ ] Caching/Restore Mechanismen (NuGet, Bun) effizient konfiguriert
- [ ] ENV/CLI Flag für Generation definiert (`SPOCR_GENERATOR_MODE=dual|legacy|next` + `spocr generate --mode`) – Default in v4.5 = `dual` (README Abschnitt "Configuration Precedence" ergänzt; CLI Param-Doku noch offen)
- [x] Allow-List Datei `.spocr-diff-allow` unterstützt (Glob) – optional, nur Noise-Reduktion
- [x] SHA256 Hashing der generierten Dateien implementiert (Determinismus-Nachweis)
- [x] CI Policy: Diffs aktuell rein informativ (Relaxed Mode) – zukünftige Eskalation geplant
- [x] Diff Report erweitert (diff-summary.txt) legacy vs. neuer Output
- [x] Exit Codes für Diff-/Integritäts-Ergebnisse reserviert (21–23) & dokumentiert (noch nicht aktiviert im Relaxed Mode)
- [x] Experimentelles System.CommandLine CLI Flag (`SPOCR_EXPERIMENTAL_CLI`) dokumentiert & getestet

### Auto-Update / Upgrade Safety

- [x] Major-Bridge Policy implementiert (Block direkte Major-Sprünge ohne `SPOCR_ALLOW_DIRECT_MAJOR`)
- [x] README Hinweis zur Bridge Policy ergänzen (CHANGELOG Eintrag vorhanden)
- [x] Testfall für geblocktes Major Update + Override hinzugefügt (`BridgePolicyTests`)
- [ ] Weitere Tests: Minor Update erlaubt, SkipVersion respektiert, Direkt-Major mit Override protokolliert
- [ ] Dokumentation Env Override Beispiele (`SPOCR_ALLOW_DIRECT_MAJOR=1`) in README / MIGRATION Guide

- [x] Tests für EnvConfiguration Precedence & Invalid Mode vorhanden
      note: Zusätzlich Fallback-Test (spocr.json ConnectionString genutzt wenn SPOCR_GENERATOR_DB fehlt) & Override-Test (ENV gewinnt) ergänzt.
- [ ] Test: Experimental CLI Flag (`SPOCR_EXPERIMENTAL_CLI`) aktiviert neuen Parser nur bei gesetztem Flag

### Nullable & Codequalität (Ergänzung)

- [x] Globale Nullable aktiviert + Legacy-Unterdrückung via `.editorconfig`
- [x] Selektive Reaktivierung für `SpocRVNext` und neue CLI Entry Points
- [ ] Phase 2: Schrittweises Entfernen alter Suppressions (Tracking Liste)
- [ ] Phase 3: CI Eskalation (`SPOCR_STRICT_NULLABLE=1`) dokumentiert & aktiviert

### Observability / Diff (Ergänzung)

- [x] Hash-Manifeste erzeugt (SHA256) pro Output
- [x] Diff-Report + Allow-List Mechanismus vorhanden
- [ ] Aktivierung reservierter Exit Codes (21–23) bei Policy-Eskalation implementieren (geplant erst ab v5.0)
- [ ] Dokumentation: Anleitung zur Pflege der Allow-List (`.spocr-diff-allow`)
- [ ] Optionaler "strict-diff" Modus über ENV / CLI Flag getestet
- [x] Snapshot-Timestamp (`GeneratedUtc`) aus Persistenz entfernt (deterministische Hashes / keine Timestamp-Diffs)
- [x] Hash-Filter erweitert: Ignoriere dynamische `Generated at` Zeilen aus vNext Output-Dateien

### Sonstiges

- [ ] Konsistenter Stil der Commit Messages (Konvention definiert, z.B. Conventional Commits)
- [ ] Offene TODO Kommentare bewertet / priorisiert / entfernt falls nicht mehr nötig
- [ ] Issue Tracker Abgleich: Alle Items dieses Releases geschlossen oder verschoben
- [ ] Technische Schuldenliste aktualisiert
- [x] (Regel) Implementierung IN CODE vollständig auf Englisch (Kommentare, öffentliche/ interne Bezeichner) – Ausnahme: `CHECKLIST.md` bleibt deutsch
      note: Abgeschlossen am 2025-10-16 (SchemaManager, SpocrManager, Snapshot/Layout Services, Provider, Orchestrator)
- [ ] (Regel) Keine "VNext" Namensbestandteile in Klassen / Dateien / Properties – Trennung ausschließlich über Ordner & Namespace `SpocRVNext`
- [ ] (Prinzip) Qualität & Wartbarkeit des neuen Outputs > strikte Rückwärtskompatibilität (Breaking Changes sind erlaubt, sofern dokumentiert und migrierbar)
- [ ] XML Kommentare auf den vnext Outputs optimieren. Gehört GeneratedAt in die // <auto-generated/> Zeile?

... (bei Bedarf weiter ergänzen) ...

# Fixes für zwischendurch

- [x] samples\restapi\SpocR\samples Namespace-Korrektur (Generator + manuelle Files bereinigt)
- [x] samples\restapi\SpocR\ITableType.cs Namespace = RestApi.SpocR ✅
- [ ] samples\restapi\.env aus Template mit Kommentaren generieren - [x] Template-Datei `.env.example` anreichert (Erklär-Kommentare für Modus/Flags/Namespace vorhanden) - [ ] CLI Befehl/Bootstrap: `spocr env init` (optional) evaluieren
- [ ] (OBSOLET) ResultSet Datei-Benennung vereinheitlichen (durch Konsolidierung in eine Prozedur-Datei nicht mehr relevant)
      Hinweis: Einzelne RowSet-Dateien existieren nicht mehr; alle Records (Inputs/Outputs/ResultSets/Aggregate/Plan/Executor) liegen in einer konsolidierten `<Proc>.cs`.
      Folgeaufgaben (aktualisiert): - [ ] Test: Konsolidierte Datei enthält erwartete Abschnitte in Reihenfolge (Header→Inputs→Outputs→ResultSets→Aggregate→Plan→Executor) - [ ] Test: Kein doppelter Record-Name bei mehreren ResultSets (Multi-Table) - [x] Aktivierungs-Test Resolver (generische Namen ersetzt) - [ ] Negative Test: Unparsable SQL → Fallback (kein Crash) - [ ] Multi-ResultSet Szenario (nur erste Tabelle benannt, weitere generisch) - [ ] Mixed Case Tabellenname Normalisierung
- [x] Auto-Namespace Fallback für samples/restapi implementiert (erzwingt Basis `RestApi`) - [ ] Ergänzender Test für WorkingDir = `samples/restapi` (Folgetask – aktuell indirekt durch Integration abgedeckt)
- [ ] .env Override Nutzung (SPOCR_NAMESPACE) dokumentieren & Beispiel ergänzen - [ ] README / docs: Abschnitt "Namespace Ableitung & Override" inkl. Beispiel diff - Fallback / Erzwingung via Smoke Script aktiv, Doku fehlt
- [ ] Einheitliche Klein-/Großschreibung Schema-Ordner - [ ] Normalisierung (Entscheidung: Beibehalt Original vs. PascalCase) - [ ] Test: Mixed Case Snapshot → generierter Ordner konsistent - Status: Implementiert als PascalCase (Generator), Dokumentation noch offen
- [ ] Dateinamen & Determinismus zusätzliche Tests - [x] Grundlegende deterministische Hash Tests (Golden Snapshot) vorhanden - [x] Konsolidierte UnifiedProcedure Tests (Hash & IO Single Definition) - [ ] Erweiterung: spezifische Artefakt-Typen (StoredProcedure Wrapper Section, ResultSet Records innerhalb Konsolidierungs-Datei) - [ ] Dateinamens-Konflikt Test (zwei Procs mit ähnlichen Namen + Suffix Handling) - Hash Manifest aktiv; Strict Mode (Fail Fast) offen
- [ ] Dispatcher next-only Pfad: Gleiches Full Generation Set wie dual - [ ] Prüfen Codepfad (`SpocRGenerator` / Dispatcher) - [ ] Test: MODE=next erzeugt identische Artefakte wie dual (ohne Legacy) - Bisher nur manuelle Stichproben, automatischer Vergleich fehlt
- [x] Sicherstellen, dass samples/restapi/.env nicht in git landet (`.gitignore` aktualisiert)
- [ ] src\SpocRVNext\Templates_Header.spt optimieren (<auto-generated/> Block vereinheitlichen + GeneratedAt Handling)

## Nächste Kurzfristige Actions (veraltet – ersetzt durch neue Sofort-Prioritäten weiter unten)

- (Erledigt) Skript `scripts/test-db.ps1` implementiert & in Smoke integriert
- (Erledigt) CI Smoke Schritt vorhanden
- (Ersetzt) DB Connect Timeout Optimierung durch Kill-Skript + Retries – Feintuning optional verschoben

## Zwischenstand Zusammenfassung (aktualisiert)

Connectivity gesichert (test-db Script + CI Integration). Offene Kernpunkte: Stabiler erfolgreicher Stored Procedure Roundtrip (UserList), Resolver Erweiterungen (Dynamic SQL / CTE), Coverage & Golden Hash Policy Eskalation.

## Aktuelle Sofort-Prioritäten (neu / validiert 2025-10-15)

1. Coverage Threshold & Enforcement (≥80% Kernlogik) – CI Gate implementieren
2. Dynamic SQL Detection Konzept (Resolver Skip) + erster Testfall
3. CTE Support Vorarbeit (Parsing Strategie definieren, Minimal-Implementierung planen)
4. Doku Konsolidierung: TableTypes + ResultSet Naming + README Verlinkungen + Badge Sektion
5. Golden Hash Strict Mode Entscheid (Policy Flags, Exit Codes Eskalation) & README Abschnitt
6. CI Badges (Smoke / Determinism / Quality / Coverage) + Kill-Skript überall referenzieren
7. Sample Roundtrip Stabilisierung (Timeout / Startsequenz Analyse, Logging Verbesserung)
8. Abschnittsreihenfolge Test für konsolidierte Prozedur-Dateien (Header→Inputs→Outputs→ResultSets→Aggregate→Plan→Executor)
9. Namespace Override / Ableitung Doku + Beispiel diff
10. Dispatcher next-only Pfad Paritätstest (MODE=next vs dual ohne Legacy) automatisieren

# Zu planende Entscheidungen

- [ ] Wie handhaben wir Datums-/Zeitangaben. (z.B. UTC, lokale Zeit, Formatierung)
- [ ] Wie bringen wir standard/custom Converters unter? (z.B. JsonConverter Attribute, AutoTrimmed Properties, andere Property oder Class Level Converters)
- [ ] ResultSets mit Typ Json sollen deserialisiert und raw produziert werden können. Per Service Config global, und auf jeder Prozedur separat
- [ ] Objekte, die nicht mehr im .spocr/schema enthalten sind aus dem Output löschen
- [ ] TemplateEngine optimieren (z.B: verschachtelte for each ermöglichen)
- [ ] Refactoring und Optimierung der SpocRVNext und vnext-Outputs durchführen
- [ ] ResultSetNameResolver Improvements (geplant) - [ ] CTE support (first base table inside final query if no direct NamedTableReference) - [ ] FOR JSON PATH root alias extraction (use alias as name) - [ ] Dynamic SQL detection -> explicit skip marker - [ ] Collision test for suggested names - [ ] Parser performance micro-benchmark & caching - [ ] Optional config flag to disable resolver (SPOCR_DISABLE_RS_NAME_RESOLVER) - [ ] Snapshot Integration: Prozedur-SQL Felder erfassen (`Sql` oder `Definition` Key) beim `spocr pull` - [ ] Aktivierungs-Flag dokumentieren & Minimal-Doku (intern) erstellen
- [ ] Warum sind die Inputs vom Typ Output nicht in den Inputs enthalten? Wir brauchen TwoWay Binding
- [ ] .env.example: Nur gültige verwenden und Kommentare ergänzen
- [ ] die erzeugte .env soll mit denselben Kommentaren wie die .env.example angereichert werden (.env.example dient als dem Generator als Vorlage?)

- [ ] Das muss noch ein Fehler sein: [spocr namespace] No .csproj found upward. Using directory-based base name.
