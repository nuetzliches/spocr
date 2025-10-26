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
- [~] ProcedureCollector
  - DB-Enumeration (Schema-Filter, Wildcards). _(Implemented via `DatabaseProcedureCollector`; cache/decision logic aktiv)_
  - Cache-Entscheid (ModifyDate + Content-Hash persistieren). _(File-basiertes Cache `FileSnapshotCache` entscheidet Analyze/Reuse; Hash-Persistenz folgt Autor-Writer)_
  - Ausgabe: Liste verarbeitbarer Prozeduren mit Status (Reuse, Refresh, Skip). _(Reuse/Analyze umgesetzt; Skip noch offen)_
- [~] ProcedureAnalyzer
  - Integration `StoredProcedureContentModel` (AST only). _(DatabaseProcedureAnalyzer zieht Definition aus DB, parsed AST & extrahiert Dependencies)_
  - Übergabe an Postprocessor (CTE/TableVar/JSON Binding). _(Dependency-Metadaten-Queries korrigiert; `modify_date`-freie Pfade validiert)_
  - Rückgabe: `AnalyzedProcedure` DTO inkl. Typinformationen.
- [x] Writer: Procedures + Index
  - Streaming-Write mit `Utf8JsonWriter`. _(ExpandedSnapshotWriter erzeugt deterministische Procedure-Dateien)_
  - Hash-basierte Change Detection (temp-Datei → swap). _(Hash-Vergleich + `File.Replace`/Fallback aktiv, 16-Byte Fingerprint für Cache)_
  - Index-Aktualisierung nur bei Änderungen. _(Index-Hash & Fingerprint werden nur bei Delta geschrieben)_
- [~] Cache-Modul
  - Persistenter Cache (z. B. `debug/.spocr/cache/procedures.json`). _(FileSnapshotCache speichert Fingerprints & ModifyDate nur lokal pro Entwickler; Schema-Artefakte bleiben diff-frei)_
  - Shared Table-Metadata Cache (Thread-safe, lazy load, TTL).
- [ ] Parallelisierung
  - Konfigurierbare MaxDegreeOfParallelism.
  - Thread-sichere Nutzung von Analyzer/Writers (z. B. `SemaphoreSlim`).
- [~] Telemetrie & Logging
  - Laufzeiten (Collect, Analyze, Write) und Spaltenmetriken.
  - Aggregierte Summary nach Run; Persistenz optional. _(CLI fasst aktuell Analyzed/Reuse/Write zusammen; Timing fehlt noch)_
- [~] Config-Übergang
  - ENV-only Pfad sicherstellen, `spocr.json` nur für Warnung/Kompat. _(Pull-Flow nutzt jetzt `EnvConfiguration` als Primärquelle; Warnung beim Fallback bleibt aktiv)_
  - Bootstrapper anpassen (Inputs aus `.env` priorisieren).
- [~] Legacy Cleanup
  - Snapshot-Code aus `SpocrManager` entfernen/weiterleiten. _(CLI `pull` ruft ausschließlich `SnapshotBuildOrchestrator` auf)_
  - Tests aktualisieren (`SpocR.Tests`, Golden Snapshots).
- [x] TableType Normalisierung
  - Expanded SnapshotWriter reduziert TableType/Parameter Felder auf `TypeRef` + relevante Metadaten.
  - TableTypeMetadataProvider/TableTypesGenerator nutzen Resolver für SQL-Signaturen (JsonDocument-Dispose Bug gefixt).
- [ ] Performance-Baseline & Tests
  - Szenarien definieren (Warm/Cold Cache, diffierende Schemas).
  - Ergebnisse dokumentieren (README, Metrics-Tabelle).
  - Testlauf-Befehl: `dotnet run --project src/SpocR.csproj -- pull -p debug`

## Offene Fragen / Entscheidungsbedarf

- Persistenzformat Cache (JSON vs. simple key/value). _Entscheidung: weiter JSON-Datei pro Cache-Segment (menschlich lesbar, diffbar), strukturierte Map im selben Dokument statt separatem KV-Store._
- Umgang mit Prozedur-Abhängigkeiten (Exec-Forwarding) in neuer Pipeline. _Entscheidung: AST + Dependency-Metadata bleiben Quelle, Referenzliste wird zusätzlich im Index abgelegt; keine heuristische Auflösung._
- Einbettung von Function/UDTT-Analyzern in Phase 1 oder spätere Iteration? _Entscheidung: UDTTs sind abgedeckt; Functions/Views/Tables folgen nach Collector-Erweiterung als nächste Iteration._
- debug\.spocr\schema\procedures\cluster.ClusterAddNode.json: Sollten die `Inputs` nicht besser `Parameters` heißen? _Entscheidung: SnapshotBuilder vNext schreibt `Parameters`; Legacy-Consumer erhalten einen Alias, sodass bestehende Parser ohne Breaking Change weiterlaufen._
- Sollten wir die DataTypes bereits auflösen oder einfach nur referenzieren und im Output erst auflösen (Vor/Nachteile ...)? _Entscheidung: Snapshots referenzieren Schemanamen/Typnamen, die eigentliche Auflösung passiert erst in den Konsumenten (auch für JSON/No-JSON Procs, Functions, Views, Tables)._
- Legacy Output Anbindung. _Entscheidung: Output-Bridge bereitstellen, die neue Artefakte auf das bisherige Layout mappt, bis alle Downstream-Abhängigkeiten auf vNext migriert sind._

## Artefakte

- README-Abschnitt für SnapshotBuilder (Kurzbeschreibung, Usage, Options, Telemetrie-Ausgabe).
- Beispiel-Trace (Verbose) zur Fehlersuche.
- Integrationspfad in CLI (`spocr pull` → SnapshotBuilder).

> Dieses Dokument wird fortlaufend aktualisiert, bis EPIC-E015 abgeschlossen ist.
