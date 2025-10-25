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

- [ ] Skeleton & DI
  - Projektstruktur anlegen (`Orchestrator.cs`, Unterordner, Interfaces). _(Stage-Skelette + Placeholder-Stages registriert; Implementierungen folgen)_
  - Dienstregistrierung in `SpocRVNext` ergänzen. _(Extension `AddSnapshotBuilder` angelegt; Integration in CLI steht aus)_
- [ ] ProcedureCollector
  - DB-Enumeration (Schema-Filter, Wildcards).
  - Cache-Entscheid (ModifyDate + Content-Hash persistieren).
  - Ausgabe: Liste verarbeitbarer Prozeduren mit Status (Reuse, Refresh, Skip).
- [ ] ProcedureAnalyzer
  - Integration `StoredProcedureContentModel` (AST only).
  - Übergabe an Postprocessor (CTE/TableVar/JSON Binding).
  - Rückgabe: `AnalyzedProcedure` DTO inkl. Typinformationen.
- [ ] Writer: Procedures + Index
  - Streaming-Write mit `Utf8JsonWriter`.
  - Hash-basierte Change Detection (temp-Datei → swap).
  - Index-Aktualisierung nur bei Änderungen.
- [ ] Cache-Modul
  - Persistenter Cache (z. B. `.spocr/cache/procedures.json`).
  - Shared Table-Metadata Cache (Thread-safe, lazy load, TTL).
- [ ] Parallelisierung
  - Konfigurierbare MaxDegreeOfParallelism.
  - Thread-sichere Nutzung von Analyzer/Writers (z. B. `SemaphoreSlim`).
- [ ] Telemetrie & Logging
  - Laufzeiten (Collect, Analyze, Write) und Spaltenmetriken.
  - Aggregierte Summary nach Run; Persistenz optional.
- [ ] Config-Übergang
  - ENV-only Pfad sicherstellen, `spocr.json` nur für Warnung/Kompat.
  - Bootstrapper anpassen (Inputs aus `.env` priorisieren).
- [ ] Legacy Cleanup
  - Snapshot-Code aus `SpocrManager` entfernen/weiterleiten.
  - Tests aktualisieren (`SpocR.Tests`, Golden Snapshots).
- [ ] Performance-Baseline & Tests
  - Szenarien definieren (Warm/Cold Cache, diffierende Schemas).
  - Ergebnisse dokumentieren (README, Metrics-Tabelle).

## Offene Fragen / Entscheidungsbedarf

- Persistenzformat Cache (JSON vs. simple key/value).
- Umgang mit Prozedur-Abhängigkeiten (Exec-Forwarding) in neuer Pipeline.
- Einbettung von Function/UDTT-Analyzern in Phase 1 oder spätere Iteration?

## Artefakte

- README-Abschnitt für SnapshotBuilder (Kurzbeschreibung, Usage, Options, Telemetrie-Ausgabe).
- Beispiel-Trace (Verbose) zur Fehlersuche.
- Integrationspfad in CLI (`spocr pull` → SnapshotBuilder).

> Dieses Dokument wird fortlaufend aktualisiert, bis EPIC-E015 abgeschlossen ist.
