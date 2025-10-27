# Snapshot Builder vNext Checklist (EPIC-E015)

## Zielbild

- Modularen Snapshot-Build-Prozess in `src/SpocRVNext/SnapshotBuilder` verankern.
- Legacy-Implementierung aus `SpocrManager` ablösen und ausschließlich die neue Pipeline nutzen.
- Sämtliche Heuristiken vermeiden; AST + Snapshot-Metadaten sind einzige Wahrheitsquelle.

## Architektur-Skizze

- **SnapshotBuildOrchestrator**
  - Koordiniert Collect → Analyze → Write → Cache → Telemetrie.
  - Akzeptiert Optionen (Schemas, Wildcards, NoCache, MaxDOP) und CancellationToken.
- **Collectors/**
  - `ProcedureCollector`: listet Kandidaten, bewertet Cache (ModifyDate + Hash), liefert Arbeitsliste.
  - Erweiterbar für Tabellen/UDTTs falls spätere Etappen nötig.
- **Analyzers/**
  - `ProcedureAnalyzer`: setzt `StoredProcedureContentModel.Parse`, übernimmt CTE/TableVar-Propagation und projiziert auf internes DTO.
  - Schnittstelle für weitere Analyzer (Functions, Tables) vorbereiten.
- **Writers/**
  - `ExpandedSnapshotWriter`: Streaming via `Utf8JsonWriter`, schreibt nur bei Content-Änderung (Hash-Vergleich temp → final).
  - `IndexWriter`: aktualisiert `index.json` deterministisch.
- **Cache/**
  - `SnapshotCache`: verwaltet Fingerprints, ModifyDate/Hash, Shared Table-Metadata Cache.
  - Unterstützt globale Invalidation (z. B. `--no-cache`).
- **Diagnostics/**
  - Zentrale Telemetrie (Stopwatch-Messungen, Resolved/Upgrades, Bytes geschrieben).
  - Log-Gates für Verbose/Trace.

## Arbeitspakete

- [x] Skeleton & DI
  - Projektstruktur anlegen (`Orchestrator.cs`, Unterordner, Interfaces). _(Stage-Skelette + Placeholder-Stages registriert; Implementierungen folgen)_
  - Dienstregistrierung in `SpocRVNext` ergänzen. _(Extension `AddSnapshotBuilder` registriert und via CLI aktiviert)_
- [x] ProcedureCollector
  - DB-Enumeration (Schema-Filter, Wildcards). _(Implemented via `DatabaseProcedureCollector`; cache/decision logic aktiv)_
  - Cache-Entscheid (ModifyDate + Content-Hash persistieren). _(File-basiertes Cache `FileSnapshotCache` entscheidet Analyze/Reuse; Hash-Persistenz folgt Autor-Writer)_
  - Ausgabe: Liste verarbeitbarer Prozeduren mit Status (Reuse, Refresh, Skip). _(Skip-Tracking ergänzt; Filter-treffer außerhalb Seeds werden jetzt als `Decision=Skip` protokolliert)_
- [x] ProcedureAnalyzer
  - Integration `StoredProcedureContentModel` (AST only). _(DatabaseProcedureAnalyzer zieht Definition aus DB, parsed AST & extrahiert Dependencies)_
  - Übergabe an Postprocessor (CTE/TableVar/JSON Binding). _(Dependency-Metadaten-Queries korrigiert; `modify_date`-freie Pfade validiert)_
  - Rückgabe: `AnalyzedProcedure` DTO inkl. Typinformationen. _(Dependency-Erkennung deckt jetzt auch Tabellen-Referenzen ab; fehlende `--procedure` Treffer werden als Skip markiert.)_
- [x] Writer: Procedures + Index
  - Streaming-Write mit `Utf8JsonWriter`. _(ExpandedSnapshotWriter erzeugt deterministische Procedure-Dateien)_
  - Hash-basierte Change Detection (temp-Datei → swap). _(Hash-Vergleich + `File.Replace`/Fallback aktiv, 16-Byte Fingerprint für Cache)_
  - Index-Aktualisierung nur bei Änderungen. _(Index-Hash & Fingerprint werden nur bei Delta geschrieben)_
- [x] Cache-Modul
  - Persistenter Cache (z. B. `debug/.spocr/cache/procedures.json`). _(FileSnapshotCache speichert Fingerprints & ModifyDate nur lokal pro Entwickler; Schema-Artefakte bleiben diff-frei)_
  - [x] Shared Table-Metadata Cache (Thread-safe, lazy load, TTL). _(Gemeinsamer `TableMetadataCache` liefert lazy geladene Tabellenmetadaten mit Änderungsüberwachung und TTL-Invalidierung, Provider teilen sich denselben Snapshot.)_
  - Cache pruned stale procedure entries on flush when running ungefilterte Pulls; gefilterte Läufe behalten Bestand.
- [x] Parallelisierung
  - Konfigurierbare MaxDegreeOfParallelism.
  - Thread-sichere Nutzung von Analyzer/Writers (z. B. `SemaphoreSlim`).
  - `ExpandedSnapshotWriter` nutzt `Parallel.ForEachAsync` mit deterministischer Ergebnisreihenfolge und konsolidierter Fehlerbehandlung.
- [x] Telemetrie & Logging
  - [x] Laufzeiten (Collect, Analyze, Write). _(Per-Phase-Laufzeiten werden via Diagnostics & CLI ausgewiesen.)_
  - [x] Spaltenmetriken. _(Parameter-/ResultSet-Kennzahlen werden gesammelt und als CLI-Metrik ausgegeben.)_
  - [x] Aggregierte Summary nach Run; Persistenz optional. _(CLI-Ausgabe enthält per-Phase-Laufzeiten, Diagnostics geben Timing-Übersicht aus; optionaler JSON Export via `SPOCR_SNAPSHOT_SUMMARY[_PATH]` umgesetzt.)_
- [x] Config-Übergang
  - ENV-only Pfad sicherstellen, `spocr.json` nur für Warnung/Kompat. _(Pull-Flow nutzt `EnvConfiguration` als Primärquelle; Fallback warnt und bridged nur die Connection)_
  - Bootstrapper anpassen (Inputs aus `.env` priorisieren). _(EnvBootstrapper füllt `.env` inkl. `SPOCR_BUILD_SCHEMAS`/Namespace aus und wird beim CLI-Init genutzt)_
- [ ] Legacy Cleanup
  - [x] Snapshot-Code aus `SpocrManager` entfernen/weiterleiten. _(CLI `pull` ruft ausschließlich `SnapshotBuildOrchestrator` auf)_
  - [ ] C:\Projekte\GitHub\spocr\src\Services\SchemaSnapshotService.cs: Tests aktualisieren (`SpocR.Tests`, Golden Snapshots) und auf neuen Snapshot-Output anheben. _(Aufschub: erst nach Abschluss der Migration unter "## Migration".)_
- [x] TableType Normalisierung
  - Expanded SnapshotWriter reduziert TableType/Parameter Felder auf `TypeRef` + relevante Metadaten.
  - TableTypeMetadataProvider/TableTypesGenerator nutzen Resolver für SQL-Signaturen (JsonDocument-Dispose Bug gefixt).
- [x] Performance-Baseline & Tests
  - Szenarien definieren (Warm/Cold Cache, diffierende Schemas).
  - Ergebnisse dokumentieren (README, Metrics-Tabelle).
  - Testlauf-Befehl: `dotnet run --project src/SpocR.csproj -- pull -p debug`
  - Baseline unter `src/SpocRVNext/SnapshotBuilder/README.md` mit Messwerten vom 2025-10-26 hinterlegt.

## Prioritäten (Stand 2025-10-27)

1. Abhängigkeiten ablösen (StoredProcedureContentModel endgültig entfernen, Analyzer direkt im SnapshotBuilder verankern).
2. Diagnose & Typauflösung iterativ verbessern (`--no-cache`, `--verbose`, `--procedure` Läufe; JSON/AVG/EXISTS verifizieren).
3. Deferred JSON/ProcedureRef Serialization finalisieren (offene Punkte aus Writer-Aufteilung schließen).
4. Abschlussarbeiten: Testsuite (SpocR.Tests), Determinism-Checks und Golden Snapshots aktualisieren.

## Offene Fragen / Entscheidungsbedarf

- Persistenzformat Cache (JSON vs. simple key/value). _Entscheidung: weiter JSON-Datei pro Cache-Segment (menschlich lesbar, diffbar), strukturierte Map im selben Dokument statt separatem KV-Store._
- Umgang mit Prozedur-Abhängigkeiten (Exec-Forwarding) in neuer Pipeline. _Entscheidung: AST + Dependency-Metadata bleiben Quelle, Referenzliste wird zusätzlich im Index abgelegt; keine heuristische Auflösung._
- Einbettung von Function/UDTT-Analyzern in Phase 1 oder spätere Iteration? _Entscheidung: UDTTs sind abgedeckt; Functions/Views/Tables folgen nach Collector-Erweiterung als nächste Iteration._
- debug\.spocr\schema\procedures\cluster.ClusterAddNode.json: Sollten die `Inputs` nicht besser `Parameters` heißen? _Entscheidung: SnapshotBuilder vNext schreibt `Parameters`; Legacy-Consumer erhalten einen Alias, sodass bestehende Parser ohne Breaking Change weiterlaufen._
- Sollten wir die DataTypes bereits auflösen oder einfach nur referenzieren und im Output erst auflösen (Vor/Nachteile ...)? _Entscheidung: Snapshots referenzieren Schemanamen/Typnamen, die eigentliche Auflösung passiert erst in den Konsumenten (auch für JSON/No-JSON Procs, Functions, Views, Tables)._
- Legacy Output Anbindung. _Entscheidung: Output-Bridge bereitstellen, die neue Artefakte auf das bisherige Layout mappt, bis alle Downstream-Abhängigkeiten auf vNext migriert sind._
- Zuordnung ScriptDom → `ProcedureModel`: Wie ordnen wir Query/Column-Fragmente zuverlässig den `ProcedureModel.ResultSets` zu, damit JSON- und Aggregat-Flags sitzen? Bitte Konzept ausarbeiten _(Offen)_
- EXEC Schema-Normalisierung: Sollen schema-lose `EXEC`-Aufrufe automatisch auf das Prozedur-Schema abgebildet werden, um Doppel-Einträge zu vermeiden? Fehlt uns hier generell noch ein Konzept, um das konfigurierte `Default-Schema` zu sicher? _(Offen)_
- Aggregat-Erkennung in verschachtelten ResultSets: Reicht das aktuelle Alias-Matching oder brauchen wir zusätzliche Kontextinformationen? Prüfschritte organisieren _(Offen)_

## Optimierungen

- [x] debug\.spocr\schema redundanz minimieren
  - [x] MaxLength/Precision/Scale bei TypeRef → UDTT oder konstanten sys-Typen unterdrücken.
  - [x] `IsNullable`-Spiegelung zum zugrunde liegenden TypeRef auflösen (nur Fälle mit abweichender Semantik beibehalten).
  - [x] Weitere sys-Typen mit festen Längen prüfen und ggf. streichen.
  - [x] `"IsTableType": true` brauchen wir auch nicht, wenn TypeRef auf eine UDTT zeigt, oder?
- [x] debug\.spocr\schema\procedures\workflow.ActionFindAsJson.json: Das Feld `"Name": "record"` sollte, wie alle anderen Felder eine `TypeRef` haben. _(Writer unterdrückt `TypeRef` für JSON-Payloads)_
- [x] debug\.spocr\schema\FE4854D2932B7F32.json: Legacy-Fingerprint-Snapshots liegen nun unter `.spocr/cache/schema`, `schema/` trägt nur noch deterministische Artefakte (inkl. `index.json`).
- [x] debug\.spocr\schema\tabletypes\core.ComparisonCalculationType.json: TableTypes prunen jetzt MaxLength/Precision/Scale analog zu StoredProcedures; `UserTypeId` wird nicht mehr persistiert.
- [x] Die SQL-Queries (Abfragen für den Snapshot) auf die Felder (und Joins) reduzieren, die erforderlich sind. _(Parameter-/UDTT-Abfragen liefern jetzt nur noch benötigte Spalten, überflüssige Joins entfernt.)_
- [x] Sollten wir nur die Types laden (oder im Snapshot speichern), die auch über die `TypeRef`s benötigt werden? Passt das in unser Konzept oder eher nachteilhaft?
- [x] JsonResultSetTypeEnricher von `StoredProcedureContentModel` entkoppeln (neue Parser-Outputs verwenden).
- [x] Column-Level JSON-Emission prüfen (`Json`-Block vs. Flattening), sobald Konsumenten aktualisiert sind.
- [x] C:\Projekte\GitHub\spocr\src\Services\SchemaSnapshotService.cs:ResolveLegacySchemaDir(): Kommentar ergänzt – Pfad bleibt für deterministische Artefakte/Legacy-Fallback erhalten.
- [x] debug\.spocr\schema\procedures\workflow-state.TransitionFindAsJson.json `SqlTypeName` scheint redundant zu sein, können wir die Property komplett entfernen, da alles über `TypeRef` ableitbar? [x] leeres `"Json": {},` vermeiden.
- [x] debug\.spocr\schema\procedures\workflow.NodeListAsJson.json: "FunctionRef": "identity.RecordAsJson" dürfte kein Array sein, da die referenzierte Funktion kein Array liefert. Siehe debug\[workflow]_[NodeListAsJson].sql und debug\[identity]_[RecordAsJson].sql (benötigen wir hier die `Json` Property überhaupt, wenn wir `FunctionRef` haben? Oder kann diese Eigenschaft in anderen Fällen abweichen?)
- [x] src\SpocRVNext\SnapshotBuilder\Metadata: TableType- und UDT-Queries als Provider ausgekoppelt (`DatabaseTableTypeMetadataProvider`, `DatabaseUserDefinedTypeMetadataProvider`); StoredProcedures laufen weiterhin über Collector/Analyzer.
- [x] debug\.spocr\cache\schema hier werden noch alle Daten, die eigentlich aus dem Snapshot hervorgehen redundant gespeichert. Sollten hier nicht nur Metadaten in den Cache? _(Cache persistiert jetzt ein schlankes Dokument mit Fingerprint, Schemaliste und sonstigen Metadaten; Parameter/ResultSets verbleiben ausschließlich im Snapshot.)_

## Migration `StoredProcedureContentModel`

- [ ] **Abhängigkeiten ablösen**
  - [x] SnapshotBuilder vollständig von `StoredProcedureContentModel` lösen und AST-/Metadata-Pipeline direkt im SnapshotBuilder verankern (ScriptDom-Builder + Analyzer nutzen eigene Metadata-Resolver, keine statischen Delegates mehr).
  - [ ] Eigenständige Analyzer für JSON-ResultSets aufbauen (AVG/SUM/COUNT Detection, Nested JSON, FunctionRefs) und Regressionen aus den aktuellen Tests adressieren (`avg` Aggregat-Flag, Exec Forwarding, comment-only FOR JSON Fälle).
  - [x] `ProcedureModel` eingeführt, Analyzer/Writer konsumieren kein `StoredProcedureContentModel` mehr nach außen.
  - [x] Übergangsweise Aggregat-Propagation in `StoredProcedureContentModel` gefixt (Derived Columns setzen `IsAggregate` jetzt auch für umhüllte Ausdrücke).
  - [x] Postprocessor ergänzt Aggregat-Heuristik direkt auf `ProcedureModel` (stellt AVG/SUM Flags und Standardtypen sicher, bis neue Analyzer stehen).
  - [x] ScriptDom-basierte Aggregat-Analyse (`ProcedureModelAggregateAnalyzer`) re-parst Definitionen und setzt AggregateFlags unabhängig vom Legacy-Parser.
  - [x] Exec-Forwarding via `ProcedureModelExecAnalyzer` (ScriptDom) erkannt – Dependencies basieren nicht mehr auf Legacy-Daten.
  - [x] ScriptDom-basierte JSON-Analyse (`ProcedureModelJsonAnalyzer`) setzt ResultSet- und Nested-JSON-Flags ohne Legacy-Modell.
  - [x] Unit Tests für Aggregate-, Exec- und JSON-Analyzer decken Alias-Matching, Literal-Flags, Deduplication und Nested-JSON-Erkennung ab.
  - [x] ScriptDom-Analyzer liefern Parität zum Legacy-Parser (Aggregat-Flags, JSON-Metadaten, EXEC-Deduplizierung); Regressionstests (`AggregateTypingExtendedTests`, `JournalMetricsTypingTests`, `ProcedureModelAnalyzerTests`) reparieren.
    - `ProcedureModelAggregateAnalyzer` erkennt Derived-Table Aggregates inkl. EXISTS-Prädikaten; `AggregateTypingExtendedTests` und die ergänzten Analyzer-Unit-Tests laufen wieder grün.
- [x] **ExpandedSnapshotWriter modularisieren**
  - [x] Datei in klar abgegrenzte Writer/Formatter-Komponenten aufteilen (`ProcedureSnapshotDocumentBuilder`, `SchemaArtifactWriter`, `SnapshotIndexWriter`, `LegacySnapshotBridge`).
  - [ ] Im Zuge der Aufteilung Deferred JSON/ProcedureRef Serialization final klären.
- [ ] **Diagnose & Typauflösung iterativ verbessern**
  - Iterationen über Pull-Läufe mit und ohne Cache (`--no-cache`, `--verbose`, `--procedure`) durchführen, bis sämtliche Typen (insbesondere JSON/AVG/EXISTS) deterministisch gebunden sind.
  - Kommentarbereinigung für Fallbacks (`FOR JSON PATH` ohne AST-Bindung) verifizieren und ggf. in neue Analyzer überführen.
- [ ] **Abschlusskriterien**
  - Wenn Architektur und Analyzer stehen, komplette Test-Suite (SpocR.Tests, Determinism) wieder aktivieren und erwartete Artefakte aktualisieren.
  - Abschließend Legacy Tests & Golden Snapshots (siehe "Legacy Cleanup") modernisieren.

## Artefakte

- README-Abschnitt für SnapshotBuilder (Kurzbeschreibung, Usage, Options, Telemetrie-Ausgabe).
- Beispiel-Trace (Verbose) zur Fehlersuche.
- Integrationspfad in CLI (`spocr pull` → SnapshotBuilder).

> Dieses Dokument wird fortlaufend aktualisiert, bis EPIC-E015 abgeschlossen ist.
