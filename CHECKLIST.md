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
      deferred: '[>]'
      partial: '[~]'
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

Status-Legende:
[ ] offen / nicht gestartet
[x] erledigt / abgeschlossen
[>] deferred / verschoben (spätere Version, kein aktueller Fokus)
[~] teilweise umgesetzt / Basis fertig, Feinschliff offen

## Fokus & Prioritäten (Snapshot)

Legende Prioritäten: P1 = kritisch für v5 Cutover, P2 = hoch für Bridge (v4.5→v5), P3 = sinnvoll vor Release, P4 = nachgelagert / Nice-to-have.

Aktueller Fokus – Update 2025-10-18:

API-KONZEPT Umsetzung & Entscheidungsfindung (siehe `DeveloperBranchUseOnly-API-CONCEPT.md`). Kernpunkte jetzt im Fokus statt früherer Liste 1–9:

1. (P1) DbContext Methoden-Signaturen Generator (Interface Slicing: Schema-spezifische Partial Interfaces) – [x] Implementations-Skizze erstellt (Extension-Ansatz entschieden; Interface Slicing als zukünftige Option dokumentiert)
2. (P1) Interceptor Interface (`ISpocRProcedureInterceptor`) finalisieren + erste Logging/Timing Implementierung – [x] Interface & NoOp Implementierung erstellt; Execution integriert (SetInterceptor + Pre/After Hooks mit Duration in `ProcedureExecutor`)
3. (P1) Entscheidung Methodennamen Konfliktstrategie (Schema-Präfix bei Doppelungen?) dokumentieren – [x] Strategie dokumentiert (Namespaces per Schema + optionaler Schema-Präfix bei seltenen Konflikten; keine Overloads)
   note: Vorgaben bestätigt: 1) Unterschiedliche Schemas erzeugen auch unterschiedliche Namespaces (SchemaPascalCase eingebunden) → reduziert natürliche Konflikte. 2) Bereinigung ersetzt nur nicht pfad-/klassennamen-kompatible Sonderzeichen durch '\_' (kein Entfernen/Normalisieren zur Deduplikation, keine aggressive Vereinheitlichung). 3) Prozedurnamen bleiben (abgesehen von Sonderzeichen-Ersatz) unverändert; kein Plural-Singulär Rewriting, kein Suffix Strip. 7) Zugriffskonzepte evaluiert: a) Extensions via using Namespace (Standard) b) db.[SchemaName].[ProcName]Async() via verschachtelte Schema-Accessor Proxy c) Mehrere injizierbare schema-spezifische DbContexts. Empfehlung: Start mit einfachem DbContext + Namespaces (Option a) → spätere Erweiterung: optionaler Schema Accessor (Option b) wenn Discoverability Bedarf steigt. Separate DbContexts pro Schema (Option c) aktuell verworfen (Fragmentierung / DI Overhead).
   Kollisionslösung: Erst normaler Methodenname; falls Dublette trotz Namespace (selten bei identischem SchemaPascalCase + Name) → Schema-Präfix an Methodennamen anhängen (SalesGetOrderAsync). Keine Overloads.
   Optional Anschluss-Schritte (Interceptor & Invocation Ausbau):
   - LoggingProcedureInterceptor (structured logging: duration ms, success flag, error) implementieren [x]
   - DEVELOPMENT.md Abschnitt "Interceptors" (Registration, Best Practices, Fehlerhandling) ergänzen [x]
   - Reflection-Test für Extension Präsenz (<ProcName>Async + Wrapper Bridge) hinzufügen [x]
   - Doku Hinweis: Globaler statischer Interceptor vs. mögliche zukünftige DI-scoped Variante (Trade-offs dokumentieren: Einfachheit vs. Request-Korrelation) [x]
   - Beispiel-Code Snippet im README (Interceptor Registrierung während Startup) [x]
4. (P1) Aggregat Rückgabe-Konvention – Entscheidung BESTÄTIGT: Immer Unified Aggregate (kein Shortcut bei Single ResultSet) – [x] dokumentiert (API-CONCEPT.md Referenz + README Abschnitt ergänzt 19.10.2025)
5. (P2) Streaming API Flags & Snapshot Erweiterungen (ResultSetStreamingKind, IsJsonPayload) – Spezifikation festziehen (Implementierung deferred v5)
6. (P2) JSON Dual Mode Methoden-Suffixe fixieren (`JsonRawAsync`, `JsonDeserializeAsync`, `JsonElementsAsync`, `JsonStreamAsync`) – Naming Freeze
7. (P2) ProcedureEndpoints Generator Opt-In Flag definieren (`SPOCR_GENERATE_API_ENDPOINTS` / CLI `--api-endpoints`) – Entscheidung + README TODO
8. (P2) Wrapper vs. Static Low-Level Doku (Static Wrapper als Low-Level kennzeichnen) – Draft Erstellen
9. (P2) Fluent Command Builder Abgrenzung (nur komplexe Szenarien; Flag) – Evaluations-Notiz + Entscheidung ob v5 oder später
10. (P2) Decision Liste aus Abschnitt "Zu planende Entscheidungen" priorisieren & markieren (siehe unten konsolidierte Auswahl)

Depriorisiert (jetzt außerhalb unmittelbarer Fokusliste): Coverage Schwellen Eskalation, Template Edge-Case Tests, allgemeine Logging Verfeinerungen – bleiben beobachtet.

Entscheidungs-Priorisierung aus "Zu planende Entscheidungen" (Snapshot 18.10.2025):
P1: Datums-/Zeitformat (UTC vs. lokal; Format Standard) · TwoWay Binding Inputs<->Outputs (DTO Reuse) · JSON ResultSet Dual Mode (Flags + Artefakte) · Namespace Fehlerfall ([spocr namespace] Fallback) Bereinigung.
P2: Converters Modell (Attribute vs. zentraler Registry) · Output Cleanup nicht mehr vorhandener Artefakte · TemplateEngine erweiterte Schleifen · Collision Test für vorgeschlagene Namen · Parser Caching Strategie.
P3: CTE Support (v5) · FOR JSON PATH root alias extraction · Performance Micro-Benchmark · Mixed Case Normalisierung final · Strict-Diff Aktivierung.
P4: SPOCR_JSON_SPLIT_NESTED Entfernung Bewertung · Erweiterte Interceptor Hooks (OnJsonDeserialized) · Optional Compression.

Hinweis: Implementierungsstart für DbContext Methoden (P1) erst nach finaler Bestätigung Punkte 1–4. Streaming & JSON Dual Mode bewusst v5 (Vorbereitung aber dokumentiert jetzt für frühe Review).

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

Status-Update (2025-10-17): Internationalisierung weiterhin vollständig (alle Code-Kommentare Englisch). Neue Änderungen seit 16.10:

- BuildSchemas Allow-List (SPOCR_BUILD_SCHEMAS) greift jetzt für Procedures UND TableTypes (zentrale Filterung)
- Hyphen-Unterstützung für Schemanamen (Regex erweitert, Sanitizing zu PascalCase)
- Output Duplikat-Cleanup entfernt alte unsuffixed Dateien wenn suffixed \*Output.cs existiert
- Purge-Mechanismus für Legacy DataContext bewusst NICHT eingeführt (Anforderung zurückgezogen)
- Regressionstest hinzugefügt: BuildSchemasFilteringTests (stellt Allow-List Verhalten sicher)
  Offene Kernpunkte: Cross-Schema EXEC Forwarding, Abschnittsreihenfolge-Test UnifiedProcedure, TableTypes Allow-List Test.

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

### Qualität & Tests (Update 2025-10-19)

- [x] Alle bestehenden Unit- & Integrationstests grün (Tests.sln)
- [ ] Neue Tests für SpocRVNext (Happy Path + Fehlerfälle + Regression für entfernte Heuristiken)
- [>] (Optional) Info-Diff zwischen Legacy und neuem Output generiert (kein Paritäts-Zwang) – DEFERRED v5
- [~] Automatisierte Qualitäts-Gates (eng/quality-gates.ps1) vorhanden (Script aktiv; CI Integration & README Verlinkung offen)
- [ ] Test-Hosts nach Läufen bereinigt (eng/kill-testhosts.ps1) – kein Leak mehr
- [~] Code Coverage Mindestschwelle Strategie: Bridge Phase = Reporting (Threshold Parameter optional, Fail abgeschaltet <60%). Eskalationspfad: ≥60% Coverage → Aktivierung Strict Golden Hash + Coverage Gate; Ziel v5 ≥80%.
  note: Script unterstützt `-CoverageThreshold`; CI Badge & Doku fehlen.
- [ ] Negative Tests für ungültige ENV Kombinationen (spocr.json Fallback, invalid MODE, fehlende DB) – spocr.json spezifische Fälle ersetzt
- [x] Test: TableTypes Name Preservation (`PreservesOriginalNames_NoRenaming`) sichert unveränderte UDTT Bezeichner
- [x] Entfernte Suffix-Normalisierung für TableTypes (Regression abgesichert)
- [x] Konsolidierte Prozedur-Datei Test (keine Duplikate Input/Output + deterministischer Doppel-Lauf)
- [x] ResultSet Rename + Collision Tests (Resolver Basis abgesichert)
- [x] BuildSchemas Filtering Test (Procedures) (`BuildSchemasFilteringTests`)
- [x] TableTypes Allow-List Filter Test (SPOCR_BUILD_SCHEMAS)
      note: Abgedeckt durch `Filters_TableTypes_By_BuildSchemas_AllowList` in `TableTypesGeneratorTests` (prüft Interface + gefilterte Schema-Ausgabe)
- [x] Golden Hash CLI Commands Tests (`GoldenHashCommandsTests`) – Write & Verify & Strict-Verhalten (Exit Codes reserviert) validiert
- [x] Integration Test: `UserListProcedure` Roundtrip – stabiler End-to-End Aufruf bestätigt
- [>] Erweiterte Golden Hash Tests: Multi-File Änderungen + Allow-List Interplay (`.spocr-diff-allow`) – DEFERRED v5.0
- [>] Negative Golden Hash Verify Test: Manipulierte Datei → erwartete Diff-Meldung (Relaxed Mode) – DEFERRED v5.0

### Codegenerierung / SpocRVNext

- [x] Template Engine Grundgerüst fertig (ohne Roslyn Abhängigkeiten)
- [x] Ermittlung des Namespaces automatisiert und dokumentierte Fallback-Strategie vorhanden
- [x] Zentrale Positive Schema Allow-List (SPOCR_BUILD_SCHEMAS) für Procedures & TableTypes implementiert
- [ ] Entfernte Spezifikationen/Heuristiken sauber entfernt und CHANGELOG Eintrag erstellt
- [ ] Neuer `SpocRDbContext` implementiert inkl. moderner DI Patterns & Minimal API Extensions
- [x] Grundgerüst via Template-Generator (Interface, Context, Options, DI) – aktiviert in `SPOCR_GENERATOR_MODE=dual|next`
- [x] DbContext Optionen (ConnectionString / Name / Timeout / Retry / Diagnostics)
- [x] Scoped Registration Validierung (Startup Probe entfernt)
- [x] Minimal API Mapper Beispiel (Health Endpoint `/spocr/health/db`)
- [~] Integration ins Sample (Code registriert & Endpoint gemappt; laufender Prozess beendet sich noch früh – Stabilisierung ausstehend / Doku fehlt)
- [x] Parallel-Erzeugung alter (DataContext) und neuer (SpocRVNext) Outputs in v4.5 (Demo/Beobachtungsmodus) implementiert
- [x] Legacy CLI ruft bei `SPOCR_GENERATOR_MODE=dual` zusätzlich vNext Dispatcher (nur .env / EnvConfiguration, ohne spocr.json Nutzung) auf
- [x] Schalter/Feature-Flag zum Aktivieren des neuen Outputs vorhanden (CLI Parameter oder Konfig)
- [x] Konsistenz-Check für generierte Dateien (Determinismus pro Generator; keine Legacy-Paritäts-Pflicht) – Hash Manifeste vorhanden (noch keine harte Policy)
- [x] Timestamp-Zeile neutralisiert (Regex Normalisierung)
- [x] Doppelter Schreibpfad Outputs/CrudResult entfernt (Skip base copy)
      note: Konsistenz-Check für generierte Dateien (Determinismus pro Generator; keine Legacy-Paritäts-Pflicht) – Hash Manifeste vorhanden (noch keine harte Policy); Timestamp-Zeile neutralisiert (Regex Normalisierung); Doppelter Schreibpfad Outputs/CrudResult entfernt (Skip base copy)
- [x] TableTypes: Always-On Generation (Interface `ITableType` einmalig, Records je Schema unter `SpocR/<schema>/`) integriert in Build (dual|next)
- [x] TableTypes: Timestamp `<remarks>` Zeile eingefügt und beim Hashing ignoriert (DirectoryHasher Filter)
- [x] TableTypes: Original Snapshot Namen vollständig beibehalten (nur Sanitizing) – keine erzwungene \*TableType Suffix Ergänzung

Streaming & Invocation (vNext API / Verschoben zu v5)

- [>] Erweiterung ResultSetMapping um StreamingKind / Delegates (DEFERRED v5)
- [>] ProcedureStreamingHelper implementiert (Rows + Json) (DEFERRED v5)
- [ ] Extension-Methoden für mind. 1 Prozedur generiert (Prototype)
- [ ] Snapshot Flag / Erkennung FOR JSON Payload (IsJsonPayloadProcedure)
- [>] Unit Tests: Row Streaming (Mehrere Rows), JSON Streaming (Chunk), Cancellation Abbruch (DEFERRED v5)
- [>] Doku Abschnitt "Procedure Invocation Patterns" inkl. Streaming Beispiele (DEFERRED v5)
- [>] Interceptor Erweiterung (optional) für PreExecute/PostExecute Streaming Pfade (DEFERRED v5)
- [ ] Entscheidung: Naming-Konvention Stream Methoden (ResultXStreamAsync vs. StreamResultXAsync) dokumentiert und fixiert
- [ ] FOR JSON Dual Mode: Raw + Lazy Deserialization Methoden (JsonRawAsync / JsonDeserializeAsync / JsonElementsAsync / JsonStreamAsync)
- [ ] ProcedureJsonHelper implementiert
- [ ] Aggregate Lazy JSON Cache (JsonLazy<T>) integriert
- [>] Tests: Raw + Deserialize + Elements Streaming + Invalid JSON + Cancellation (DEFERRED v5)
- [>] Doku: Dual Mode JSON Nutzung & Best Practices (Wann Raw? Wann Lazy? Wann Streaming?) (DEFERRED v5)
- [ ] Snapshot: pro ResultSet Flag IsJsonPayload (nicht nur pro Procedure)
- [ ] Generator: Erzeuge JsonRawAsync, JsonDeserializeAsync<T>, JsonElementsAsync<T>, JsonStreamAsync
- [>] Incremental Parsing: Utf8JsonReader basierte Implementation für Elements Streaming (DEFERRED v5)
- [ ] Fallback wenn Root nicht Array → InvalidOperationException Test
- [>] Performance Smoke: Großer JSON Payload (≥5MB) Vergleich Raw vs. Streaming (Messung dokumentieren) (DEFERRED v5)
- [ ] Interceptor Erweiterung evaluieren (OnJsonDeserialized Hook) – Entscheidung dokumentieren

#### Optional: Wrapper & Snapshot Referenzen

- [ ] Wrapper Referenz-Snapshot Format finalisieren (nur ExecSource Platzhalter ohne Columns) – Logging + Tests
- [ ] Test: Wrapper mit leerem Placeholder -> Snapshot enthält 1 Referenz-ResultSet
- [ ] Test: Non-Wrapper + EXEC + eigenem SELECT -> Snapshot behält eigene + forwarded referenzierte Sets korrekt
- [ ] Review & Dokumentation Filter-Heuristik für leere Sets (Platzhalterentfernung) – optional Verbose Logging aktivieren
- [ ] Doku Abschnitt: Unterschied forwarded (ExecSource) vs. direkte ResultSets (Columns/JSON)

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
            note: Basis aktiv (dual/next Trigger). Offen: eigener vNext-only Befehl (z.B. `spocr vnext generate`), Help/Usage Doku, Param Validation.
      - [~] Sample nutzt mindestens eine generierte Stored Procedure (Endpoints implementiert, noch Fehler 500 bei UserList)
            note: UserList Roundtrip Integration Test grün; offen: Timeout/Ping Stabilisierung, README Endpoint Beispiel, zusätzliche CRUD (CreateUser) Test.
      - [x] ResultSet Naming Strategie dokumentiert (Abschnitt enthält: Basis-Tabelle, Duplicate Suffix, Dynamic SQL Skip, Deferred Items) – Abschluss 18.10.2025
      - [x] Erweiterung Quick Start Abschnitt für vNext DbContext + Stored Procedure Invocation Beispiel – Abschluss 18.10.2025
      - [~] Tests: Snapshot / Determinismus für neue Artefakte (Basis vorhanden: Golden + Konsolidierte Procs; ausstehend: RowSet / Konfliktfälle)
            note: Abgedeckt: Golden Hash Commands Tests, UnifiedProcedureOrderingTests. Offen: Multi-ResultSet Konflikt-Namen, manipulierter Mehrfach-Datei Diff (≥3), Strict Mode Aktivierung später.
      - [ ] Interaktive .env Bootstrap CLI (separate vNext Kommando) – Basis EnvBootstrapper vorhanden, noch kein dedizierter Befehl

      Hinweis: ResultSetNameResolver aktiv (always-on) – nutzt persistiertes `Sql` Feld; ersetzt nur generische Namen kollisionsfrei. Kein Disable-Schalter vorgesehen (Designentscheidung für Konsistenz & einfache Tests).
      Update 2025-10-18: Dynamische SQL Erkennung (EXEC(@sql) / sp_executesql) implementiert – Resolver liefert in diesen Fällen bewusst kein Basis-Tabellen-Namensvorschlag (Tests: ResultSetNameResolverDynamicSqlTests).

      TODO entfernt: Performance Messung (nicht mehr erforderlich)

### Migration / Breaking Changes (Update 2025-10-15)

note: Konfig-Keys `Project.Role.Kind`, `RuntimeConnectionStringIdentifier`, `Project.Output.*` sind ab 4.5 als obsolet markiert – tatsächliche Entfernung erfolgt erst mit v5. (Siehe Deprecation Timeline in `MIGRATION_SpocRVNext.md`)

- [ ] Alle als [Obsolet] markierten Typen enthalten klaren Hinweis & Migrationspfad
- [ ] Dokumentierter Cut für v5.0 (Entfernung DataContext) in README / ROADMAP
- [ ] (v5) Vollständige Entfernung der verbleibenden Laufzeit-/Build-Abhängigkeit zu `spocr.json` (reiner .env / ENV Betrieb). Falls in v5 noch eine `spocr.json` gefunden wird: WARNUNG ausgeben (Hinweis auf Aufräumen) – keine harte Nutzung mehr.
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

### Konfiguration & Artefakte (Update 2025-10-15)

- [x] `.env` Beispieldatei hinzugefügt (Pfad: `samples/restapi/.env.example`) inkl. aller relevanten SPOCR\_\* Keys
      note: Enthält SPOCR_GENERATOR_MODE, SPOCR_EXPERIMENTAL_CLI, Bridge Policy Flags – `.env` ausschließlich Generator-Scope (kein Runtime Connection String). Entfernte Idee eines dedizierten Runtime DB Env Vars ersatzlos gestrichen. Precedence dokumentiert (README aktualisiert). Namespace / Output Dir Prefill via `.env` Bootstrap weiterhin möglich.
- [x] `spocr pull` überschreibt lokale Konfiguration nicht mehr (nur interne Metadaten)

### Dokumentation (Update 2025-10-18)

- [ ] docs Build läuft (Bun / Nuxt) ohne Fehler
- [ ] Neue Seiten für SpocRVNext (Architektur, Unterschiede, Migration) hinzugefügt
- [ ] Referenzen (CLI, Konfiguration, API) aktualisiert
- [x] README Quick Start an neuen Generator angepasst
      note: vNext DbContext Beispiel ergänzt & ValidateOnBuild entfernt (18.10.2025)
- [x] Doku: TableTypes Abschnitt (Naming-Preservation, Timestamp `<remarks>` & Hash-Ignore, Interface `ITableType`, Schema-Unterordnerstruktur) in docs/3.reference oder 2.cli verlinkt
      note: Abgedeckt durch bestehende `docs/content/3.reference/table-types.md` (Review 18.10.2025) – Inhalt beschreibt Preservation & Hash-Ignore.
- [>] Doku (DEFERRED): Procedure Invocation Patterns (DbContext Methoden, Static Wrapper Low-Level, Streaming, JSON Payload, Interceptor Hooks)
  subtasks (werden am Ende gesammelt erstellt): - Übersichtstabelle Methoden/Formen - Codebeispiel synchrone Invocation - Streaming Beispiel (IAsyncEnumerable) - JSON Payload Beispiel & Mapping - Interceptor Hook Beispiel (Before/After Command)
- [>] CHANGELOG.md Einträge (DEFERRED bis funktionale Features stabil)
- [>] DEVELOPMENT.md Kuratierte Commands (DEFERRED: finalisieren vor Release Freeze)
- [>] Samples/README Doku-Links (DEFERRED: nach Fertigstellung Kernfeatures)
- [>] Docs v4.5 Übergangsrelease Text (DEFERRED: final wording nach letztem Feature Merge)
- [x] Version-Schalter vorbereitet (content.config.ts + Frontmatter version Felder, meta collection) – Abschluss 18.10.2025
- [x] Inhalte mit Versions-Hinweisen versehen (Banner integriert in Layout `docs/app/layouts/docs.vue`)
      note: Component `VersionBanner.vue` aktiv für 4.5 & 5.0 (18.10.2025)
- [x] Platzhalter-Seiten für v5 Unterschiede: `v5-differences.md`, `migration-v5.md`, `api-changes-v5.md`, `removed-heuristics-v5.md` erstellt (18.10.2025)
      note: Platzhalter mit Frontmatter `version: 5.0` – Inhalte folgen vor Release.
- [x] content.config.ts erweitert um Versions-Metadaten (meta collection, version Feld in Schema)
- [x] Hinweisbanner in v4.5 Seiten: "Sie lesen die v4.5 Dokumentation – v5 in Vorbereitung" (Komponente + auto Anzeige anhand Frontmatter version)
      note: Implementiert via VersionBanner + Frontmatter version (18.10.2025)
      Zusatzaufgaben Versionierung (DEFERRED): - [>] Versionsauswahl-Komponente (Selector für 4.5 / 5.0) - [>] README Links zu Migration & Differences - [>] Jede Platzhalterseite enthält klaren Preview-Hinweis - [>] Build Gate warnt bei fehlender version Frontmatter

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
- [x] Sample führt grundlegende DB Operationen erfolgreich aus (CRUD Smoke Test) – Roundtrip & Ping stabil (Timeout/Ping Fix abgeschlossen 18.10.2025)
      note: Optional: zusätzlicher CreateUser Roundtrip + README Beispiel ergänzen
- [~] Automatisierter Mini-Test (skriptgesteuert) prüft Generierung & Start der Web API (smoke-test.ps1 vorhanden, CI Integration fehlt)
- [x] Sample beschreibt Aktivierung des neuen Outputs (Feature Flag) im README (Abschnitt vorhanden 19.10.2025)
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

### Automatisierung / CI (Update 2025-10-19)

- [ ] Pipeline Schritt: Codegen Diff (debug/model-diff-report.md) aktuell und verlinkt
- [ ] Fail-Fast bei unerwarteten Generator-Änderungen (Diff Threshold)
- [ ] QA Skripte (eng/\*.ps1) in README oder DEVELOPMENT.md referenziert (smoke-test.ps1 erwähnen; Golden Hash Feature optional separat dokumentieren)
      -- [x] CI: Smoke Test Schritt (smoke-test.ps1) integriert (`smoke.yml`)
      -- [x] CI: DB Smoke (SQL Service Container) integriert (`db-smoke.yml`)
      -- [x] CI: Determinism Verify separater Workflow (`determinism.yml`)
      -- [ ] (Optional) Golden Hash Strict Mode aktivieren (Policy Eskalation)
      -- [ ] CI: Manual Trigger / Dokumentation für Golden Hash Aktualisierung (-WriteGolden) vorhanden
- [>] CI: Golden Hash Verify Schritt (Aufruf `verify-golden`) integriert – Relaxed Mode – DEFERRED v5.0 (Bridge Phase nur manuelles Write; Verify erst mit Coverage ≥60%)
- [ ] CI Dokumentation: "Golden Hash Update Workflow" (manuelles `write-golden`, Review, Commit) – offen
- [x] Golden Hash CLI Befehle implementiert (`write-golden`, `verify-golden`) – Funktion & Tests bestätigt (18.10.2025)
- [ ] CHANGELOG Ergänzung: Reservierte Exit Codes (21–23) dokumentieren (README Abschnitt existiert; formaler Eintrag fehlt)
- [ ] Caching/Restore Mechanismen (NuGet, Bun) effizient konfiguriert
- [>] ENV/CLI Flag für Generation definiert (`SPOCR_GENERATOR_MODE=dual|legacy|next` + `spocr generate --mode`) – DEFERRED v5 für vollständige CLI Param-Doku; v4.5 belässt Default dual
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

### Observability / Diff (Ergänzung) (Update 2025-10-19)

- [x] Hash-Manifeste erzeugt (SHA256) pro Output
- [x] Diff-Report + Allow-List Mechanismus vorhanden
- [ ] Aktivierung reservierter Exit Codes (21–23) nach Coverage ≥60% & stabiler Allow-List (v5 Ziel)
      -- [x] Dokumentation: Anleitung zur Pflege der Allow-List (`.spocr-diff-allow`) (README Abschnitt enthält Workflow & Beispiel Globs)
- [ ] Optionaler "strict-diff" Modus über ENV / CLI Flag getestet
- [x] Snapshot-Timestamp (`GeneratedUtc`) aus Persistenz entfernt (deterministische Hashes / keine Timestamp-Diffs)
- [x] Hash-Filter erweitert: Ignoriere dynamische `Generated at` Zeilen aus vNext Output-Dateien
      note: Strict Mode Aktivierungskriterium: Kern-Coverage ≥60% & stabile Allow-List; README Abschnitt vorhanden (Determinism & Golden Hash, 18.10.2025)
- [x] Golden Hash Manifest Mechanismus aktiv (`debug/golden-hash.json` bestätigt)
- [ ] CI Durchsetzung Strict Golden Hash (Exit Codes) – abhängig von Coverage ≥60% & stabiler Allow-List (Policy Draft offen)
- [ ] Erweiterte Diff Tests: ≥3 manipulierte Dateien → aggregierter Report & korrekter Relaxed Exit Code – offen

### Sonstiges

- [ ] Konsistenter Stil der Commit Messages (Konvention definiert, z.B. Conventional Commits)
- [ ] Offene TODO Kommentare bewertet / priorisiert / entfernt falls nicht mehr nötig
- [ ] Issue Tracker Abgleich: Alle Items dieses Releases geschlossen oder verschoben
- [ ] Technische Schuldenliste aktualisiert
- [x] (Regel) Implementierung IN CODE vollständig auf Englisch (Kommentare, öffentliche/ interne Bezeichner) – Ausnahme: `CHECKLIST.md` bleibt deutsch
      note: Abgeschlossen am 2025-10-16 (SchemaManager, SpocrManager, Snapshot/Layout Services, Provider, Orchestrator)
- [ ] (Regel) Keine "VNext" Namensbestandteile in Klassen / Dateien / Properties – Trennung ausschließlich über Ordner & Namespace `SpocRVNext`
- [ ] (Prinzip) Qualität & Wartbarkeit des neuen Outputs > strikte Rückwärtskompatibilität (Breaking Changes sind erlaubt, sofern dokumentiert und migrierbar)
- [ ] XML Kommentare auf den vnext Outputs optimieren.
- [x] Result1 und die Modellklassen sollen bei "Result" ohne Nummer beginnen, erst das zweite ResultSet bekommt die "1" (also 0-based Indexierung und 0 = "").

... (bei Bedarf weiter ergänzen) ...

# Fixes für zwischendurch

- [x] samples\restapi\SpocR\samples Namespace-Korrektur (Generator + manuelle Files bereinigt)
- [x] samples\restapi\SpocR\ITableType.cs Namespace = RestApi.SpocR ✅
- [x] Progress Anzeige: Doppelter 100% Balken entfernt & Abschlussformatierung (Leerzeile + Separator) korrigiert (ConsoleService + SchemaManager Anpassung)
- [x] BuildSchemas Filtering zentral (Procedures & TableTypes)
- [x] Hyphen-Support für Schema-Namen (Validation & Sanitizing)
- [x] Output Duplikat-Cleanup (unsuffixed vs suffixed \*Output.cs)
- [~] Cross-Schema EXEC Forwarding (A) teilweise (Forwarding & Append implementiert; volle Testabdeckung offen)
- [~] A: Cross-Schema EXEC Forwarding / Append
  id: A
  goal: ResultSets von EXEC-Zielprozeduren werden auch dann weitergeleitet bzw. angehängt, wenn das Ziel-Schema auf Ignore steht oder nicht aktiv geladen wurde.
  acceptance: - Wrapper (nur EXEC, keine eigenen konkreten Sets) übernimmt vollständige ResultSets des Ziels (inkl. ExecSource* Metadaten) ✔ - Non-Wrapper mit eigenen Sets hängt Ziel-Sets hinten an ✔ - Fallback greift über Expanded Snapshot (snapshotProcMap) auch wenn Ziel nicht in procLookup enthalten ✔ - Logging Tags: [proc-forward-xschema] / [proc-exec-append-xschema] ✔ - Beispiel: soap.PaymentInitiationFindAsJson -> banking.InitiationFindAsJson (ExecSourceSchemaName / ExecSourceProcedureName korrekt gesetzt) – Edge-Case Tests (ignoriertes Schema / Duplikat / Allow-List) offen
  depends: [E004, E014]
  note: Brackets in ExecSource* optional (keine Änderung bestehender Tests); Fokus auf Vorhandensein / Merge
  plan: 1) Snapshot-Erweiterung: Vollständige Proc-Liste inkl. ignorierter Schemas cachen (snapshotProcMap) 2) Analysephase: Parser erkennt Wrapper (nur EXEC) vs. Mixed (EXEC + eigener SELECT) 3) Forwarding Merge: Wrapper = komplette Übernahme fremder ResultSets; Mixed = Append ans Ende (Erhalt eigener Reihenfolge) 4) Duplikat-Prüfung: Key (ExecSourceSchemaName, ExecSourceProcedureName, ForwardedResultSetName) 5) Metadaten anreichern (ExecSource\* Felder) 6) Logging implementieren ([proc-forward-xschema] / [proc-exec-append-xschema]) 7) Tests: a) Wrapper Forward b) Mixed Append c) Ignoriertes Schema trotzdem forwarded d) Duplikat-Verhinderung e) Allow-List Interaktion.
- [ ] samples\restapi\.env aus Template mit Kommentaren generieren (Bootstrap synchronisiert Kommentar-Blöcke aus `.env.example`; identische Reihenfolge & zukünftige v5 Preview Keys optional übernommen)
- [x] Template-Datei `.env.example` anreichert (Erklär-Kommentare für Modus/Flags/Namespace vorhanden)
- [ ] CLI Befehl/Bootstrap: `spocr env init` (optional) evaluieren
- [ ] (OBSOLET) ResultSet Datei-Benennung vereinheitlichen (durch Konsolidierung in eine Prozedur-Datei nicht mehr relevant)
      Hinweis: Einzelne RowSet-Dateien existieren nicht mehr; alle Records (Inputs/Outputs/ResultSets/Aggregate/Plan/Executor) liegen in einer konsolidierten `<Proc>.cs`.
      Folgeaufgaben (aktualisiert): - [x] Test: Konsolidierte Datei enthält erwartete Abschnitte in Reihenfolge (Header→Inputs→Outputs→ResultSets→Aggregate→Plan→Executor) - [ ] Test: Kein doppelter Record-Name bei mehreren ResultSets (Multi-Table) - [x] Aktivierungs-Test Resolver (generische Namen ersetzt) - [x] Negative Test: Unparsable SQL → Fallback (kein Crash) - [ ] Multi-ResultSet Szenario (nur erste Tabelle benannt, weitere generisch) - [ ] Mixed Case Tabellenname Normalisierung
      note: Ordering Tests (Single & Multi) implementiert in `UnifiedProcedureOrderingTests` (18.10.2025)
- [x] Auto-Namespace Fallback für samples/restapi implementiert (erzwingt Basis `RestApi`)
- [ ] Ergänzender Test für WorkingDir = `samples/restapi` (Folgetask – aktuell indirekt durch Integration abgedeckt)
- [ ] .env Override Nutzung (SPOCR_NAMESPACE) dokumentieren & Beispiel ergänzen
- [ ] README / docs: Abschnitt "Namespace Ableitung & Override" inkl. Beispiel diff - Fallback / Erzwingung via Smoke Script aktiv, Doku fehlt
- [ ] Einheitliche Klein-/Großschreibung Schema-Ordner
- [ ] Normalisierung (Entscheidung: Beibehalt Original vs. PascalCase)
- [ ] Test: Mixed Case Snapshot → generierter Ordner konsistent - Status: Implementiert als PascalCase (Generator), Dokumentation noch offen
- [ ] Dateinamen & Determinismus zusätzliche Tests
- [x] Grundlegende deterministische Hash Tests (Golden Snapshot) vorhanden
- [x] Konsolidierte UnifiedProcedure Tests (Hash & IO Single Definition)
- [ ] Erweiterung: spezifische Artefakt-Typen (StoredProcedure Wrapper Section, ResultSet Records innerhalb Konsolidierungs-Datei)
- [ ] Dateinamens-Konflikt Test (zwei Procs mit ähnlichen Namen + Suffix Handling) - Hash Manifest aktiv; Strict Mode (Fail Fast) offen
- [ ] Dispatcher next-only Pfad: Gleiches Full Generation Set wie dual
- [ ] Prüfen Codepfad (`SpocRGenerator` / Dispatcher)
- [>] Test: MODE=next erzeugt identische Artefakte wie dual (ohne Legacy) – DEFERRED v5 (Paritätstest automatisieren)
- [x] Sicherstellen, dass samples/restapi/.env nicht in git landet (`.gitignore` aktualisiert)
- [ ] src\SpocRVNext\Templates_Header.spt optimieren (<auto-generated/> Block vereinheitlichen)

## Nächste Kurzfristige Actions (veraltet – ersetzt durch neue Sofort-Prioritäten weiter unten)

- (Erledigt) Skript `eng/test-db.ps1` (ehem. `scripts/test-db.ps1`) implementiert & in Smoke integriert
- (Erledigt) CI Smoke Schritt vorhanden
- (Ersetzt) DB Connect Timeout Optimierung durch Kill-Skript + Retries – Feintuning optional verschoben

## Zwischenstand Zusammenfassung (aktualisiert)

Connectivity gesichert (test-db Script + CI Integration). Offene Kernpunkte: Stabiler erfolgreicher Stored Procedure Roundtrip (UserList), Resolver Erweiterungen (Dynamic SQL / CTE), Coverage & Golden Hash Policy Eskalation.

## Aktuelle Sofort-Prioritäten (neu / validiert 2025-10-15)

1. Coverage Threshold & Enforcement (≥80% Kernlogik) – CI Gate implementieren
2. Dynamic SQL Detection Konzept (Resolver Skip) + erster Testfall – ERLEDIGT (18.10.2025)
3. CTE Support Vorarbeit (Parsing Strategie definieren, Minimal-Implementierung planen) – DEFERRED auf v5.0 (aktuell kein unmittelbarer Mehrwert für Bridge Phase)
4. Doku Konsolidierung: TableTypes + ResultSet Naming + README Verlinkungen + Badge Sektion
5. Golden Hash Strict Mode Entscheid (Policy Flags, Exit Codes Eskalation) & README Abschnitt
6. CI Badges (Smoke / Determinism / Quality / Coverage) + Kill-Skript überall referenzieren
7. Sample Roundtrip Stabilisierung (Timeout / Startsequenz Analyse, Logging Verbesserung)
8. Abschnittsreihenfolge Test für konsolidierte Prozedur-Dateien (Header→Inputs→Outputs→ResultSets→Aggregate→Plan→Executor)
9. Namespace Override / Ableitung Doku + Beispiel diff
10. Dispatcher next-only Pfad Paritätstest (MODE=next vs dual ohne Legacy) automatisieren – DEFERRED v5

## Deferred v5 Items (Consolidated)

Status-Legende: [>] deferred (v5 Ziel) – Querverweis auf README / Roadmap Abschnitte.

- [>] Entferne Fallback `project.output.namespace` (README "Namespace Derivation & Override" – Cutover v5)
- [>] Analyzer Warnung bei widersprüchlichem Namespace (CLI vs ENV vs .env) (README Namespace Abschnitt – zukünftige Hint)
- [>] Strict Diff / Golden Hash Aktivierung (Exit Codes 21–23) nach Coverage ≥60% (README Exit Codes + Determinism Abschnitt)
- [>] Coverage Eskalationspfad: 60% Strict Mode aktiv, 80% v5 Ziel, 85%+ Post-Cutover (README Coverage Policy verfeinern, Checklist spiegeln)
- [>] Streaming & JSON Dual Mode (Row / JSON IAsyncEnumerable + Lazy Cache) (README Preview / JSON Stored Procedures Alpha)
- [>] Erweiterte JSON Methoden: JsonRawAsync / JsonDeserializeAsync<T> / JsonElementsAsync<T> / JsonStreamAsync (Generator-Erweiterung)
- [>] Snapshot: pro ResultSet Flag IsJsonPayload (feinkörnige Kennzeichnung)
- [>] Forwarded ResultSets Doku (ExecSource\* Meta-Felder detailliert) (UnifiedProcedure Erweiterung)
- [>] JUnit Multi-Suite XML (getrennte suites für unit/integration/validation) (README JUnit Abschnitt Verbesserung)
- [>] FOR JSON PATH root alias extraction (ResultSetNameResolver Verbesserungen)
- [>] CTE Support (Basis-Tabelle aus finaler Query in CTE Fällen) (ResultSetNameResolver geplante Erweiterung)
- [>] TwoWay Binding Inputs<->Outputs (DTO Reuse) – Entscheidung & Mapping Strategie
- [>] Entfernte Heuristiken vollständige Doku (EPIC E007) – CHANGELOG Removed Abschnitt
- [>] Obsolete Konfig Keys endgültige Entfernung (Project.Role.Kind, RuntimeConnectionStringIdentifier, Project.Output.\*) – v5 Cutover
- [>] Analyzer Konflikt-Reporting für doppelte generierte Methodennamen (Namespace Präfix Strategie) – Post-Cutover

# Zu planende Entscheidungen

- [x] Das Parameter -p|--path soll auch direkt den Pfad samples/restapi anstelle von samples/restapi/spocr.json akzeptieren.
- [ ] Darf die spocr.json bereits gelöscht werden, wenn eine .env existiert oder existieren in v4.5 noch Abhängigkeiten dorthin?
- [ ] Wie handhaben wir Datums-/Zeitangaben. (z.B. UTC, lokale Zeit, Formatierung)
- [ ] Wie bringen wir standard/custom Converters unter? (z.B. JsonConverter Attribute, AutoTrimmed Properties, andere Property oder Class Level Converters)
- [ ] ResultSets mit Typ Json sollen deserialisiert und raw produziert werden können. Per Service Config global, und auf jeder Prozedur separat
- [ ] Objekte, die nicht mehr im .spocr/schema enthalten sind aus dem Output löschen
- [ ] TemplateEngine optimieren (z.B: verschachtelte for each ermöglichen)
- [ ] Refactoring und Optimierung der SpocRVNext und vnext-Outputs durchführen
- [ ] ResultSetNameResolver Improvements (geplant)
- [ ] CTE support (erste Basis-Tabelle aus finaler Query, wenn kein direkter NamedTableReference) – verschoben zu v5.0
- [ ] FOR JSON PATH root alias extraction (Alias als Name nutzen)
- [x] Dynamic SQL detection -> skip (implementiert 18.10.2025)
- [ ] Collision test für vorgeschlagene Namen (Edge Cases Mehrere Tabellen, gleiche Basisnamen)
- [ ] Parser Performance Micro-Benchmark & Caching Strategie (TSql150Parser Reuse)
- [ ] Snapshot Integration: Prozedur-SQL Felder vollständiger erfassen (`Sql`/`Definition`) beim `spocr pull`
- [ ] Warum sind die Inputs vom Typ Output nicht in den Inputs enthalten? Wir brauchen TwoWay Binding
- [ ] .env.example: Nur gültige verwenden und Kommentare ergänzen
- [ ] die erzeugte .env soll mit denselben Kommentaren wie die .env.example angereichert werden (.env.example dient als dem Generator als Vorlage?)

- [ ] Das muss noch ein Fehler sein: [spocr namespace] No .csproj found upward. Using directory-based base name.
- [ ] "HasSelectStar": false, Columns: [] (leer), "ResultSets": [] (leer) nicht ins schema json schreiben.
- [ ] SPOCR_JSON_SPLIT_NESTED (bzw. SplitNestedJsonSets) ist wozu erforderlich?
      Wenn das ein Überbleibsel unserer fixes ist, bitte entfernen.
