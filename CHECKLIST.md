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
      question: '[?]'
      fact: '[!]'
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

## Fokus & Prioritäten

- [x] JSON Model Typkorrektur für WorkflowListAsJson (workflowId → int) – Mapping aktiv
- [~] Audit weiterer JSON ResultSets: Erste Korrekturen (33 Felder, überwiegend rowVersion → byte[]) erfolgt; numerische/bool/datetime Fälle prüfen
- [x] Generator: Mapping-Layer für ReturnsJson ResultSets implementiert (SQL Typname → C# Property Typ)
- [x] Warnung/Aggregation aktiv (JsonTypeMapping Logs + optional JsonAudit Report)
- [ ] Tests: JSON Deserialisierung numeric, bool, datetime Felder ohne Lenient Converter (Converter nur für Mischfälle) – AUSSTEHEND
- [ ] Dokumentation Abschnitt "vNext JSON Procedure Handling" (Deserialisierungspfad, Flags, Typableitung, Fallback Strategie) – AUSSTEHEND
- [x] Entfernte temporäre Komplexität: Keine Schleifen/Aggregation bei Single NVARCHAR JSON Spalte (verifiziert)
- [ ] Performance Mikro-Test: Direkte Deserialisierung vs. vorherige Aggregation (optional, DEFERRED)
- [x] Verschachtelte JSON Feld-Namen (Dot & Underscore) erzeugen jetzt hierarchische Sub-Records (Generator Anpassung 21.10.2025)
- [x] Trailing Comma in verschachtelten Record Parametern entfernt (21.10.2025)
- [x] Fallback SqlTypeName Marker 'json' → NVARCHAR & 'rowversion'/'timestamp' → VarBinary implementiert (Enum.Parse Schutz)
- [x] Debug Logging für nested-json Gruppen entfernt (nur temporär für Verifikation genutzt)
- [x] Kompatibilitätsentscheidung: Kein Feature Toggle für verschachtelte Records – immer aktiv (Dokumentation ergänzen)

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
- [>] Code Coverage Mindestschwelle Strategie: Bridge Phase = Reporting (Threshold Parameter optional, Fail abgeschaltet <60%). Eskalationspfad: ≥60% Coverage → Aktivierung Strict Golden Hash + Coverage Gate; Ziel v5 ≥80%.
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
- [>] Entfernte Spezifikationen/Heuristiken sauber entfernt und CHANGELOG Eintrag erstellt
- [~] Neuer `SpocRDbContext` implementiert inkl. moderner DI Patterns & Minimal API Extensions
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
      note: Nested JSON Sub-Struct Generation aktiv (immer an) – künftige Doku: Segment Case-Preservation / Mapping Regeln

Streaming & Invocation (vNext API / Verschoben zu v5)

- [>] Erweiterung ResultSetMapping um StreamingKind / Delegates (DEFERRED v5)
- [>] ProcedureStreamingHelper implementiert (Rows + Json) (DEFERRED v5)
- [>] Extension-Methoden für mind. 1 Prozedur generiert (Prototype)
- [>] Unit Tests: Row Streaming (Mehrere Rows), JSON Streaming (Chunk), Cancellation Abbruch (DEFERRED v5)
- [>] Doku Abschnitt "Procedure Invocation Patterns" inkl. Streaming Beispiele (DEFERRED v5)
- [>] Interceptor Erweiterung (optional) für PreExecute/PostExecute Streaming Pfade (DEFERRED v5)
- [>] Entscheidung: Naming-Konvention Stream Methoden (ResultXStreamAsync vs. StreamResultXAsync) dokumentiert und fixiert
- [ ] FOR JSON Dual Mode: Raw + Lazy Deserialization Methoden (JsonRawAsync / JsonDeserializeAsync / JsonElementsAsync / JsonStreamAsync)
- [?] ProcedureJsonHelper implementiert
- [?] Aggregate Lazy JSON Cache (JsonLazy<T>) integriert
- [>] Tests: Raw + Deserialize + Elements Streaming + Invalid JSON + Cancellation (DEFERRED v5)
- [>] Doku: Dual Mode JSON Nutzung & Best Practices (Wann Raw? Wann Lazy? Wann Streaming?) (DEFERRED v5)
- [>] Generator: Erzeuge JsonRawAsync, JsonDeserializeAsync<T>, JsonElementsAsync<T>, JsonStreamAsync
- [>] Incremental Parsing: Utf8JsonReader basierte Implementation für Elements Streaming (DEFERRED v5)
- [?] Fallback wenn Root nicht Array → InvalidOperationException Test
- [>] Performance Smoke: Großer JSON Payload (≥5MB) Vergleich Raw vs. Streaming (Messung dokumentieren) (DEFERRED v5)
- [?] Interceptor Erweiterung evaluieren (OnJsonDeserialized Hook) – Entscheidung dokumentieren

### Funktionen & TVFs Snapshot / Mapping (Aktualisiert 2025-10-21)

Ziel (Phase 1 IST-Zustand): Minimaler Snapshot für Scalar & Table-Valued Functions zur Analyse (künftige Invocation / Dependency Graph), unabhängig von BuildSchemas Allow-List. KEINE Speicherung von Definition, Hash oder ModifiedDate mehr – bewusst schlank für deterministische Diffs.

Persistierte Felder je Funktion (aktuell):

- schema, name
- isTableValued
- returnSqlType (leer für TVF)
- returnMaxLength (nur wenn >0, nur scalar)
- returnIsNullable (nur wenn true, nur scalar)
- parameters[] (vereinheitlicht mit StoredProcedure Inputs Modell; Rückgabe-Pseudo-Parameter wird extrahiert und NICHT als Parameter gespeichert)
- columns[] (nur TVF, leer wird entfernt)
- returnsJson / returnsJsonArray / jsonRootProperty (FOR JSON Heuristik via Regex; nur gesetzt wenn erkannt)
- isEncrypted (nur true → verschlüsselte Funktion ohne Definition)

Umgesetzte Punkte:

- [x] Batch Collect Queries (functions, params, tvf_cols) – 1 Roundtrip
- [x] Rückgabe-Pseudo-Parameter Erkennung (leerer Name) für Scalar Functions → ReturnSqlType + Length/Nullable gefüllt
- [x] TVF Columns Erfassung inkl. Kollisions-Suffix (Name, Name1, Name2 …)
- [x] Entfernte persistente Felder: definition, definitionHash, modifiedDateUtc (nicht mehr im Modell)
- [x] IsEncrypted nur setzen wenn Definition fehlt (ansonsten weggelassen)
- [x] Parameter-Modell vereinheitlicht mit StoredProcedure Inputs (IsOutput immer false bei Functions)
- [x] Leere Columns Arrays entfernt (Scalar + TVF ohne Rows)
- [x] FOR JSON Erkennung (returnsJson / returnsJsonArray / jsonRootProperty) per Regex (ROOT Alias & WITHOUT_ARRAY_WRAPPER berücksichtigt)
- [x] FunctionsVersion Kennzeichnung (snapshot.FunctionsVersion = 1)
- [x] Sortierung deterministisch (Schema + Name ASC)

Nicht (mehr) Bestandteil Phase 1 (entfernt / verworfen):

- Definition / Hash / Truncation (>4000) – entfällt zugunsten Minimalität
- ModifiedDateUtc – nicht benötigt für Generator
- Return Type Parsing via `RETURNS <type>` Regex – Rückgabetyp wird direkt aus Pseudo-Parameter entnommen
- Parameter Default-Werte / hasDefaultValue Flag – DEFERRED (später für Overloads)
- CLR Typ Mapping & Codegen für Functions – DEFERRED (Generator noch nicht aktiv für Functions)
- Encrypted Definition Volltext Speicherung – entfällt (Flag reicht)

Neu geplante Erweiterungen (Phase 2):

- [?] Dependencies Erfassen (Function → Function Referenzen) via `sys.sql_expression_dependencies` (nur FN/IF/TF) → neues Feld `dependencies[]` (Canonical: schema.functionName)
- [?] Zyklus-Erkennung / Markierung (optional: `[fn-dependency-cycle]` Log falls self-referencing oder Ring entdeckt)
- [?] Descriptor Angleichen (`FunctionDescriptor`) an schlankes Snapshot-Modell (Definition-Felder optional/entfernt)
- [?] Dokumentation Abschnitt "Funktionen & TVFs" mit aktualisiertem Feldschema + Dependency Graph Hinweis
- [?] CHANGELOG Eintrag „Preview: Function Snapshot (minimal)“

Deferred Items (später/v5):

- [>] Generator: Scalar Function Async Methoden (ExecuteScalar) + Nullability Mapping
- [>] Generator: TVF Streaming (`IAsyncEnumerable<RowRecord>`) + Materialize Helper
- [>] CLR Typ Mapping Utility + Tests (SQL → C#) für Functions
- [>] Default Parameter Handling & optionale Argumente
- [>] Analyzer Warnung bei ungenutzten Function Methoden

Risiken & Hinweise:

- Verschlüsselte Funktionen: Nur Flag `isEncrypted=true`, keine weitere Metadatenableitung möglich.
- TVF komplexe Expressions: sys.columns liefert generierte Namen – akzeptiert; keine heuristische Umbenennung.
- FOR JSON Regex kann False Positives erzeugen bei kommentierten Codeblöcken – später Kommentar-Stripping (DEFERRED).
- FOR JSON Parser Limitierungen (aktueller Stand vNext Regex-Heuristik): - Kein vollständiges SQL AST: Es wird die erste/letzte Fundstelle `FOR JSON` mit vorausgehendem `SELECT` erfasst; komplexe CTE-Ketten oder mehrere SELECTs vor RETURN können zu Fehlzuordnungen führen. - Keine explizite RETURN Statement Analyse: Wir suchen nur das Muster `SELECT ... FROM ... FOR JSON`; wenn das finale RETURN auf eine Variable verweist (z.B. `RETURN @payload`) wird die SELECT-Liste nicht erkannt. - Kommentarinhalte (`-- inline`, `/* block */`) werden nicht entfernt → "FOR JSON" in Kommentaren kann fälschlich erkannt werden (False Positive Risiko). - Nested Subqueries: Verschachtelte `(SELECT ... FOR JSON ...)` innerhalb der SELECT-Liste werden als JSON verschachtelte Property (`SqlTypeName = json`) markiert, aber weitere innere Properties werden nicht rekursiv extrahiert. - Aliaserkennung eingeschränkt auf Muster `alias = expr` oder `expr AS alias`; komplexe Ausdrücke ohne Alias fallen auf heuristische Ableitung (letzter Identifier nach Punkt oder letztes Token) zurück. - Typableitung minimal: Nur CAST/CONVERT Zieltypen werden übernommen; JSON_VALUE / JSON_QUERY erhalten `nvarchar(max)`; sonst Default `nvarchar(max)`. - Keine Präzisions-/Skalenanalyse für decimal/numeric außerhalb eines direkten CAST/CONVERT. - Duplikate in der SELECT-Liste werden im Snapshot durch Suffixe (`id`, `id1`, `id2` ...) aufgelöst; die Heuristik entscheidet rein nach bereits gesehenen Namen. - Kein Entfernen von TOP-Level Klammerausdrücken, dadurch können leading `(` im Alias-Fallback verbleiben (geringe Auswirkung, später säubern). - Performance: Ein einzelner Regex mit `Singleline` über die gesamte Funktionsdefinition; bei sehr großen Definitionen > (mehrere 100 KB) potenziell langsam – aktuell akzeptiert (Functions selten so groß). - Erweiterungen (geplant): Kommentar-Stripping, bessere Auswahl des RETURN-nahen SELECT, robustere Tokenisierung, optionales Deaktivieren bei `SPOCR_STRICT_FUNCTION_JSON=1`.

Open Design Fragen (aktualisiert):

1. Dependencies nur Funktionen oder auch Prozeduren einbeziehen? (Aktuell Fokus: reine Function→Function Kanten)
2. Nullability von ReturnTyp: Aktuell nur true gesetzt; sollen wir false ebenfalls persistieren (Konsistenz)? → Entscheidung offen.
3. Sollen ignorierte Schemas auch für Dependencies berücksichtigt werden? (Vorschlag: Ja, wie bei Capture.)
4. Stored Procedures als Dependency: Nicht erforderlich – Funktionen können keine Stored Procedures direkt ausführen (EXEC in Function nicht erlaubt) → aus Scope gestrichen.

Tracking: Erweiterungen oben als einzelne Checklist Tasks gepflegt.

### Views Snapshot / Mapping (Preview Planung – Deferred v5.0)

Ziel v5: Analoge Erfassung von Views (Schema, Name, Spalten, zugrundeliegende Basis-Tabellen/Objekt-Referenzen) zur Unterstützung besserer Typableitung / Impact-Analysen.

Geplanter Minimalumfang:

- [>] Snapshot Felder: schema, name, columns[] (Name, SqlTypeName, IsNullable, MaxLength), isIndexed? (optional), isMaterialized? (nur wenn unterstützt)
- [>] Dependencies: Liste referenzierter Basis-Objekte (Tabellen, andere Views, Funktionen) via sys.sql_expression_dependencies
- [>] Keine Persistenz vollständiger Definition (wie bei Functions) – evtl. Option `SPOCR_INCLUDE_VIEW_DEFINITION` (DEFERRED)
- [>] Generator Vorbereitung: Spätere Unterstützung für strongly typed View Queries (SELECT \* FROM View) – aktuelles Scope nur Analyse

Open Punkte (v5 Entscheidung):

- View Column Name Normalisierung notwendig oder 1:1 Übernahme? (Präferenz: 1:1)
- Umgang mit Schemas außerhalb Allow-List: Gleiches Modell wie Functions (immer aufnehmen, Generation optional)
- Performance Auswirkungen bei sehr vielen Views – ggf. separate CLI Flag `--include-views` (Opt-In)

Begründung für Deferral: Kein unmittelbarer Nutzen für v4.5 Bridge; Fokus aktuell auf Procedures, TableTypes, Functions & JSON Typisierung.

### Migration / Breaking Changes (Update 2025-10-15)

note: Konfig-Keys `Project.Role.Kind`, `RuntimeConnectionStringIdentifier`, `Project.Output.*` sind ab 4.5 als obsolet markiert – tatsächliche Entfernung erfolgt erst mit v5. (Siehe Deprecation Timeline in `MIGRATION_SpocRVNext.md`)

- [?] Alle als [Obsolet] markierten Typen enthalten klaren Hinweis & Migrationspfad
- [>] Dokumentierter Cut für v5.0 (Entfernung DataContext) in README / ROADMAP
- [>] Vollständige Entfernung der verbleibenden Laufzeit-/Build-Abhängigkeit zu `spocr.json` (reiner .env / ENV Betrieb). Falls in v5 noch eine `spocr.json` gefunden wird: WARNUNG ausgeben (Hinweis auf Aufräumen) – keine harte Nutzung mehr.
- [ ] Liste entfallener Konfig-Properties (Project.Role.Kind, RuntimeConnectionStringIdentifier, Project.Output) im Changelog
      note: CHANGELOG enthält bislang keinen Removed-Abschnitt für diese Keys
- [x] Migration von `spocr.json` auf `.env` / Environment Variablen dokumentiert (Mapping Tabelle)
      note: Precedence aktualisiert (CLI > ENV > .env > spocr.json Fallback nur in dual|next wenn SPOCR_GENERATOR_DB fehlt). Fallback & Override implementiert in EnvConfiguration.
- [x] SemVer Bewertung durchgeführt (Minor vs. Major Bump begründet)
      note: Entscheidungskriterium: Entfernen Legacy DataContext + Identifier Fallback = Major (v5); v4.5 nur Bridge.

### Ziel-Framework spezifische Features

- [>] Gating: `SpocRDbContextEndpoints` nur für `net10.0` kompilieren (Analyzer/Conditional Compilation) – Dokumentation verlinken
  note: Ältere TFs (net8/net9) erhalten nur DbContext + HealthCheck optional via manuelle Registrierung
- [>] README Abschnitt "Target Framework Matrix" (Endpoint Availability) ergänzen

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
- [>] docs/content Referenzen (CLI, Konfiguration, API) aktualisiert
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

### Samples / Demo (samples/restapi)

- [x] Sample baut mit aktuellem Generator (dotnet build)
- [x] Sample führt grundlegende DB Operationen erfolgreich aus (CRUD Smoke Test) – Roundtrip & Ping stabil (Timeout/Ping Fix abgeschlossen 18.10.2025)
      note: Optional: zusätzlicher CreateUser Roundtrip + README Beispiel ergänzen
- [~] Automatisierter Mini-Test (skriptgesteuert) prüft Generierung & Start der Web API (smoke-test.ps1 vorhanden, CI Integration fehlt)
- [x] Sample beschreibt Aktivierung des neuen Outputs (Feature Flag) im README (Abschnitt vorhanden 19.10.2025)
- [!] Schema Rebuild Pipeline (`dotnet run --project src/SpocR.csproj -- rebuild -p samples/restapi/spocr.json --no-auto-update`) erzeugt deterministisch `samples/restapi/.spocr/schema`
- [~] Generierter Output in `samples/restapi/SpocR` deterministisch (Golden Hash Feature implementiert, CI Verify offen) - Golden Write/Verify verfügbar, noch nicht in CI
- [x] Namespace-Korrektur: `samples/restapi/SpocR/ITableType.cs` → `namespace RestApi.SpocR;`
- [ ] (noch offen?) Namespace-Korrektur: Dateien unter `samples/restapi/SpocR/samples/` → `namespace RestApi.SpocR.samples;`

### Sicherheit & Compliance

- [ ] Keine geheimen Verbindungsstrings / Secrets committed (Review via Suche nach "Password=" / ";User Id=")
- [ ] Abhängigkeiten aktualisiert (dotnet list package --outdated geprüft) – sicherheitsrelevante Updates eingespielt
- [ ] Lizenz-Hinweise unverändert kompatibel (LICENSE, verwendete NuGet Packages)
- [ ] Minimale Berechtigungen für DB Tests (Least Privilege Account)

### Performance & Wartung

- [>] Start-zu-Generierungszeit gemessen & dokumentiert
- [>] Speicherverbrauch während Codegeneration einmal profiliert (nur grober Richtwert)
- [>] Kein übermäßiger Dateichurn (idempotenter Output)
- [>] Logging reduziert auf sinnvolle Defaults (kein unnötiger Lärm im CI)

### Release Vorbereitung

- [~] Version in `src/SpocR.csproj` und ggf. weiteren Projekten angehoben
- [~] Tag / Release Notes vorbereitet (Aus CHANGELOG generiert)
- [~] Git Clean Status vor Tag (keine uncommitted Changes)
- [~] CI Pipeline für Release Branch erfolgreich durchgelaufen
- [~] NuGet Paket lokal gebaut & installiert (Smoke Test CLI)
- [~] Signierung/Authentizität geprüft (falls relevant)

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
- [>] Aktivierung reservierter Exit Codes (21–23) nach Coverage ≥60% & stabiler Allow-List (v5 Ziel)
  -- [x] Dokumentation: Anleitung zur Pflege der Allow-List (`.spocr-diff-allow`) (README Abschnitt enthält Workflow & Beispiel Globs)
- [>] Optionaler "strict-diff" Modus über ENV / CLI Flag getestet
- [x] Snapshot-Timestamp (`GeneratedUtc`) aus Persistenz entfernt (deterministische Hashes / keine Timestamp-Diffs)
- [x] Hash-Filter erweitert: Ignoriere dynamische `Generated at` Zeilen aus vNext Output-Dateien
      note: Strict Mode Aktivierungskriterium: Kern-Coverage ≥60% & stabile Allow-List; README Abschnitt vorhanden (Determinism & Golden Hash, 18.10.2025)
- [x] Golden Hash Manifest Mechanismus aktiv (`debug/golden-hash.json` bestätigt)
- [>] CI Durchsetzung Strict Golden Hash (Exit Codes) – abhängig von Coverage ≥60% & stabiler Allow-List (Policy Draft offen)
- [>] Erweiterte Diff Tests: ≥3 manipulierte Dateien → aggregierter Report & korrekter Relaxed Exit Code – offen

### Sonstiges

- [>] Konsistenter Stil der Commit Messages (Konvention definiert, z.B. Conventional Commits)
- [ ] Offene TODO Kommentare bewertet / priorisiert / entfernt falls nicht mehr nötig
- [!] (Regel) Implementierung IN CODE vollständig auf Englisch (Kommentare, öffentliche/ interne Bezeichner) – Ausnahme: `CHECKLIST.md` bleibt deutsch
- [!] (Regel) Keine "VNext" Namensbestandteile in Klassen / Dateien / Properties – Trennung ausschließlich über Ordner & Namespace `SpocRVNext`
- [!] (Prinzip) Qualität & Wartbarkeit des neuen Outputs > strikte Rückwärtskompatibilität (Breaking Changes sind erlaubt)
- [ ] XML Kommentare auf den vnext Outputs optimieren.
- [x] Result1 und die Modellklassen sollen bei "Result" ohne Nummer beginnen, erst das zweite ResultSet bekommt die "1" (also 0-based Indexierung und 0 = "").
- [x] Finale Vereinheitlichung: Entferntes trailing "Result" bei Record-Typen (jetzt `...ResultSet`, `...ResultSet1`, ...) dokumentiert (DEVELOPMENT.md & README Abschnitt aktualisiert)
      note: README & DEVELOPMENT.md aktualisiert 19.10.2025 – Integrationstests angepasst (UserList: Result statt Result1). Kein weiterer Code-Refactor offen.

## Optionale Erweiterungen (neu hinzugefügt 2025-10-20)

Diese Liste sammelt jüngst identifizierte optionale Verbesserungen rund um JSON Handling, Alias/Keyword Verarbeitung und Diagnostik des vNext Outputs.

// Entfernte Debug Artefakte (SPOCR_DUMP_FIRST_ROW) – kein Wiedereinbau geplant

- [x] Entfernung SPOCR_DUMP_FIRST_ROW & zugehöriger DumpFirstRow Codepfad (Generator & Templates)
- [>] JSON-Erkennung verfeinern: Heuristik statt "alle Ordinals < 0" zusätzlich (a) exakt 1 physische Spalte, (b) Spaltenname unbekannt / generisch, (c) Muster für FOR JSON PATH Payload (Leading '[' oder '{').
- [>] Streaming JSON Parser: Implementierung auf Basis `Utf8JsonReader` für sehr große Arrays (≥5MB) – vermeidet vollständiges Puffern; liefert `IAsyncEnumerable<T>`.
- [>] Dual Mode JSON Methoden: Generator erzeugt `JsonRawAsync`, `JsonDeserializeAsync<T>`, `JsonElementsAsync`, `JsonStreamAsync` (bereits als P2/P5 konzeptionell geführt – hier konsolidiert).
- [>] Non-Destructive FirstRow Dump: Ersetzen von `DumpFirstRow(r)` durch Peek-Mechanismus (Lesen der aktuellen Row ohne Cursor-Fortschritt oder Zwischenspeichern und Re-Emit), um Diagnose ohne Datenverlust zu ermöglichen.
- [>] Keyword Escaping Strategie konfigurierbar: Alternative zum '@' Prefix (z.B. Suffix '\_' oder vollständige Umbenennung mit Mapping-Dictionary) – Flag `SPOCR_KEYWORD_ESCAPE_STYLE`.
- [>] Nested Alias Mapping: Aliase mit Punkt (z.B. `record.rowVersion`) optional als verschachtelte Record-Struktur generieren statt Unterstrich-Ersatz – Flag `SPOCR_NESTED_ALIAS_STRUCTS`.
  note: Basis-Verschachtelung ohne Toggle bereits umgesetzt; Item würde nur optionalen Toggle/Erweiterungsstrategie abdecken.
- [>] Strict Missing Columns Mode: Wenn erwartete Spalten fehlen und keine JSON-Heuristik greift → Exception statt silent Default; Flag `SPOCR_STRICT_COLUMNS`.
- [>] JSON Root Type Erkennung: Unterschiedliche Pfade für Array vs. Object Root mit präziser Fehlermeldung bei Mixed Root.
- [>] JSON Cache Layer: Lazy Deserialisierung mit internem Zwischenspeicher (einmaliges Parse, mehrfacher Zugriff) – Typ `JsonLazy<T>` im Aggregate.
- [>] Performance Benchmark Harness: Mikro-Benchmarks für (a) Raw vs. Deserialize vs. Stream, (b) Column Ordinal Suche vs. Fallback JSON.
- [>] Analyzer für direkte Record Initialisierung bei TableTypes (Builder/Factory Enforcement) – ersetzt zukünftige #warning.
- [>] Erweiterter TVP Helper: Präzise SqlMetaData (Length, Precision, Scale) gemappt aus Snapshot-Metadaten statt generische Typannahmen.
- [>] Interceptor Hook `OnJsonDeserialized`: Nach Abschluss der JSON Verarbeitung, vor Rückgabe des Aggregates.
- [>] Flexible Property-Kollisionsstrategie: Bei mehrfach auftretenden Aliases nicht nur numerische Suffixe, sondern schema-basiertes Präfix optional.
- [>] Konfigurierbare Default-Fallbacks: Anpassbare Werte für fehlende Spalten (z.B. `<missing>` statt leerer String) via `SPOCR_MISSING_STRING_VALUE` etc.

Hinweis: Einige Punkte überschneiden sich mit bereits vorhandenen Deferred v5 Items; diese Liste dient als feingranulare Ergänzung für Priorisierung im Branch.

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
- [x] Template-Datei `.env.example` anreichert (Erklär-Kommentare für Modus/Flags/Namespace vorhanden)
- [>] CLI Befehl/Bootstrap: `spocr init` (ersetzt `create`)
- [x] (OBSOLET) ResultSet Datei-Benennung vereinheitlichen (durch Konsolidierung in eine Prozedur-Datei nicht mehr relevant)
      Hinweis: Einzelne RowSet-Dateien existieren nicht mehr; alle Records (Inputs/Outputs/ResultSets/Aggregate/Plan/Executor) liegen in einer konsolidierten `<Proc>.cs`.
      Folgeaufgaben (aktualisiert): - [x] Test: Konsolidierte Datei enthält erwartete Abschnitte in Reihenfolge (Header→Inputs→Outputs→ResultSets→Aggregate→Plan→Executor) - [ ] Test: Kein doppelter Record-Name bei mehreren ResultSets (Multi-Table) - [x] Aktivierungs-Test Resolver (generische Namen ersetzt) - [x] Negative Test: Unparsable SQL → Fallback (kein Crash) - [ ] Multi-ResultSet Szenario (nur erste Tabelle benannt, weitere generisch) - [ ] Mixed Case Tabellenname Normalisierung
      note: Ordering Tests (Single & Multi) implementiert in `UnifiedProcedureOrderingTests` (18.10.2025)
- [x] Auto-Namespace Fallback für samples/restapi implementiert (erzwingt Basis `RestApi`)
- [ ] Ergänzender Test für WorkingDir = `samples/restapi` (Folgetask – aktuell indirekt durch Integration abgedeckt)
- [ ] README / docs: Abschnitt "Namespace Ableitung & Override" inkl. Beispiel diff - Fallback / Erzwingung via Smoke Script aktiv, Doku fehlt
- [x] Einheitliche Klein-/Großschreibung Schema-Ordner
- [ ] Dateinamen & Determinismus zusätzliche Tests
- [x] Grundlegende deterministische Hash Tests (Golden Snapshot) vorhanden
- [x] Konsolidierte UnifiedProcedure Tests (Hash & IO Single Definition)
- [?] Erweiterung: spezifische Artefakt-Typen (StoredProcedure Wrapper Section, ResultSet Records innerhalb Konsolidierungs-Datei)
- [ ] Dateinamens-Konflikt Test (zwei Procs mit ähnlichen Namen + Suffix Handling) - Hash Manifest aktiv; Strict Mode (Fail Fast) offen
- [?] Dispatcher next-only Pfad: Gleiches Full Generation Set wie dual
- [?] Prüfen Codepfad (`SpocRGenerator` / Dispatcher)
- [>] Test: MODE=next erzeugt identische Artefakte wie dual (ohne Legacy) – DEFERRED v5 (Paritätstest automatisieren)
- [x] Sicherstellen, dass samples/restapi/.env nicht in git landet (`.gitignore` aktualisiert)
- [ ] src\SpocRVNext\Templates_Header.spt optimieren (<auto-generated/> Block vereinheitlichen)
- [ ] SPOCR_ENFORCE_TABLETYPE_BUILDER migrieren zu SPOCR_SUPPRESS_WARNINGS
- [ ] Keine Regex Fallbacks für AST-Parsing, alle Stellen durch pure AST-Navigation ersetzen
- [ ] Keine JSON Models in legacy und keine SP Extension mit Model (using auf model entfernen)
- [?] -q|--quiet gibt noch logs aus, bzw. müsste hier --json nicht --quit forcen (Regeln neu bestimmen)?

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

### Deferred v5 Items – TableType Validation & Construction Enhancements

- [>] TableTypes: Automatische FluentValidation Rule-Emission (Nullability, Länge, Bereich) aus Snapshot-Metadaten
- [>] TableTypes: Performance Toggle für Validierung (SPOCR_SKIP_VALIDATION) mit schnellem Pfad ohne Rule-Ausführung
- [>] TableTypes: Analyzer statt #warning für direkte Record-Initialisierung (Erkennung von `new <TypeName>()` außerhalb Builder/Factory)
- [>] TableTypes: TVP Binding Helper (Konvertierung Liste<ITableType> -> DataTable / SqlParameter mit SqlDbType.Structured)
- [>] TableTypes: Partial Validator Erweiterungs-Hooks (Generator erzeugt partial Validator Klasse zur Nutzerergänzung)
- [>] TableTypes: Nullable String Correctness Phase 2 (string? für IsNullable Columns, NonNullable Enforcement via Validation Rule)
- [>] TableTypes: Optionales Caching der Validator Instanz (Singleton vs. statisch) Performance Messung vor Aktivierung
- [>] TableTypes: Factory Overloads für häufige Pflicht-Kombinationen (ergibt schlankere Aufrufe ohne Builder)

# Zu planende Entscheidungen

- [x] Das Parameter -p|--path soll auch direkt den Pfad samples/restapi anstelle von samples/restapi/spocr.json akzeptieren.
- [ ] ResultSets mit Typ Json sollen deserialisiert und raw produziert werden können. Per Service Config global, und auf jeder Prozedur separat
- [>] TemplateEngine optimieren (z.B: verschachtelte for each ermöglichen)
- [~] ResultSetNameResolver Improvements (geplant)
- [>] CTE support (erste Basis-Tabelle aus finaler Query, wenn kein direkter NamedTableReference)
- [x] FOR JSON PATH root alias extraction (Alias als Name nutzen)
- [x] Dynamic SQL detection -> skip (implementiert 18.10.2025)
- [>] Collision test für vorgeschlagene Namen (Edge Cases Mehrere Tabellen, gleiche Basisnamen)
- [>] Parser Performance Micro-Benchmark & Caching Strategie (TSql150Parser Reuse)
- [x] Snapshot Integration: Prozedur-SQL Felder vollständiger erfassen (`Sql`/`Definition`) beim `spocr pull`
- [x] "HasSelectStar": false, Columns: [] (leer), "ResultSets": [] (leer) nicht ins schema json schreiben.
- [x] Die Snapshots StoredProcedures.Inputs und Functions.Parameters sollen eine gemeinsame Modelbasis haben und `IsOutput` gilt nur für SPs. Für `false` Values (z.B.: IsNullable, IsOutput, HasDefaultValue oder auch MaxLength=0) ausgeblendet werden.

# Aktueller Fokus (2025-10-21): JSON Typisierung abgeschlossen, nächste Prioritäten

**Status:** JSON Mapping Layer erfolgreich implementiert (33 `rowVersion` → `byte[]` Korrekturen). Neue Fokussierung auf Tests & Stabilisierung vor RC.

## Sofortige Prioritäten (P1 - Diese Woche)

### Prio 1

- [x] Wir benötigen noch .spocr/schema/[tables|views|types] um beim AST Parsing die Typen auflösen zu können. (Implementiert: Verzeichnisse + Writer + Tests)
- [x] Sicherstellen, dass die .spocr/schema/tabletypes bereits die korrekten UDT-Typen beinhalten, Properties ergänzen, wenn nötig. (Alias/BaseSqlTypeName, Precision/Scale, Identity & Pruning aktiv)
- [x] `types` müssen zuerst abgerufen werden, damit andere Objekte darauf referenzieren können, bzw. ihre finalen Typen bestimmen können. (Ordering Guard + frühe Function-Sammlung angepasst)
- [~] AST Parsing für vnext Output sauber implementieren und Heuristiken zu Typen-Auflösung entfernen. (Snapshot Basis & Normalisierung vorhanden; Parser-Heuristik Ablösung noch offen)
- [x] Functions werden jetzt VOR Tables/Views gesammelt (frühe Signaturen für künftige Dependency-Graphen), Ordering Kommentar aktualisiert.
- [x] Unbenutzte Guard-Variable bereinigt (phaseFunctionsDone in früherer Variante entfernt; neue Sequenz konsolidiert).
- [ ]`ActionListAsJsonResult` besitzt groß geschriebene Properties, werden die Namen nicht aus den Aliasen abgeleitet (soll der Quelle entsprechen, keine Modifikation)?
  Zudem wird wohl noch Heuristik existieren `EndsWith("Id")` oder ähnliches. Das muss alles in der AST Pipeline durch die tatsächlichen DataTypes ersetzt werden (Ähnliche Ableitungen ebenfalls ersetzen).
  Quelle: C:\Projekte\GitHub\AdvoNeo_soapNEO\AdvoNeo\AdvoNeo.SqlDb\StoredProcedures\workflow\ActionListAsJson.sql
  Ergebnis: C:\Projekte\GitHub\spocr\debug\SpocR\Workflow\ActionListAsJson.cs
- [ ] Hier wird noch der dot-name in z.B. `sourceAccount_accountId` gemapped, das muss ein sub-struct `sourceAccount` mit einer `accountId` Property werden (warum funktioniert das nur teilweise - es muss eine gemeinsame Codebasis vorhanden sein).
      C:\Projekte\GitHub\spocr\debug\SpocR\Soap\PaymentInitiationFindAsJson.cs
- [ ] Legacy Output soll keine JSON-Models erzeugen, SP-Extensions ohne JSON-Model (using auch beachten / entfernen).
- [!] Rebuild: dotnet run --project src/SpocR.csproj -- rebuild -p C:\Projekte\GitHub\spocr\debug
- [~] StoredProcedure Regex Audit abgeschlossen (Regex-Fallbacks entfernt), offene Spezialfälle: WITHOUT_ARRAY_WRAPPER Erkennung & identity.RecordAsJson Flag (Tests rot)
- [ ] Fix fehlende JSON Sets Spezialfälle (StoredProcedure AST)  
       sub: - [ ] WITHOUT_ARRAY_WRAPPER setzt ReturnsJsonArray=false (Regression nach Heuristik-Entfernung) - [ ] identity.RecordAsJson (schema-qualifizierter Funktionsaufruf) wieder als IsRecordAsJson markieren
- [ ] Env Fallback Flag Planung (`SPOCR_JSON_REGEX_FALLBACK`): Entscheidung treffen (derzeit nicht implementiert zugunsten deterministischem AST)

### 1. JSON Deserialisierung Tests abschließen

- [x] Test Infrastructure (`JsonResultSetTypeMappingTests`, `JsonResultSetAuditTests`)
- [x] **Array vs Single Object Test** (ReturnsJsonArray=true/false → korrekte Deserialisierung)
- [x] **Boolean/DateTime Roundtrip Test** (ohne Lenient Converter Fallback)
- [x] **byte[] rowVersion Test** (Base64 vs direkte Bytes, JSON Token Handling)

### 2. Generator Test Coverage erweitern

- [x] **Cross-Schema EXEC Forwarding Tests** (Wrapper, Mixed, ignorierte Schemas)
- [x] **Multi-ResultSet Konflikt Test** (doppelte Namen + Suffix Logic)
- [x] **Dynamic SQL Skip Test** (EXEC(@sql) → ResultSetNameResolver Skip)

### 3. Sample Stabilisierung

- [x] **UserList Endpoint 500 → 200** (SQL Connection/Timeout Fix)
- [x] **CreateUser Roundtrip Test** (vollständiger CRUD Cycle)
- [>] **README Endpoint Beispiele** (vNext DbContext Usage) – DEFERRED v5.0

## Kurzfristige Prioritäten (P2 - Nächste 2 Wochen)

### 4. Determinismus & Coverage

- [x] **Golden Hash Pipeline** (Rebuild → deterministische Hashes)
- [ ] **Coverage Baseline messen** (≥60% Ziel, Reporting aktivieren)
- [>] **CI Badges konsolidieren** (Smoke, DB-Smoke, Determinism Status)

### 5. Dokumentation RC-ready

- [ ] **Namespace Doku** (SPOCR_NAMESPACE Override + Beispiele)
- [ ] **CHANGELOG v4.5-rc** (Features, Removed Keys, v5 Preview Hinweise)
- [ ] **Migration Guide** (spocr.json → .env Übergang)

## Mittelfristig (P3 - Pre-RC)

### 6. Architektur-Verbesserungen

- [ ] **IsTableType computed getter** (aus `"TableType*"` Pattern ableiten)
- [ ] **SPOCR_JSON_SPLIT_NESTED audit** (obsolet? Entfernen wenn Überbleibsel)
- [ ] **Nested JSON Sub-Structs Casing** (Original-Segmente vs. PascalCase)

### 7. Configuration Modernisierung

- [ ] **spocr.json Abhängigkeiten prüfen** (vollständiger .env Übergang möglich?)
- [ ] **JsonSerializerOptions Integration** (SpocrDbContextOptions → JSON Defaults)
- [ ] **.env Template Verbesserung** (Kommentare aus .env.example übernehmen)

### 8. Advanced Features (Deferred/v5 Vorbereitung)

- [ ] **TwoWay Binding evaluation** (Inputs ↔ Outputs Reuse Strategy)
- [ ] **DateTime Strategy** (UTC vs. Local, ISO Format Standard)
- [ ] **Custom Converters Framework** (JsonConverter Attributes, Property-Level)
- [ ] **Output Cleanup** (obsolete Schema-Objekte automatisch entfernen)

## Entscheidungsblöcke (Klärung erforderlich)

1. **Converter Integration:** Sollen JSON Deserializer die `SpocrDbContextOptions` als Default nutzen?
2. **Configuration Migration:** Ab wann ist `spocr.json` vollständig optional (nur .env)?
3. **Schema Cleanup:** Automatisches Entfernen nicht mehr vorhandener Artefakte aktivieren?
4. **Test Data:** Verwendung samples/mssql SQL-Typen in allen Tests statt synthetische?

---

**Tracking:** Erfolg bei 33 JSON `rowVersion` Korrekturen zeigt Mapping-Layer funktional. Hauptfokus jetzt: Tests ausbauen, Sample stabilisieren, RC-Dokumentation.
