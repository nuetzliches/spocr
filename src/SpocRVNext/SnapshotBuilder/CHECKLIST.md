# Snapshot Builder Checklist (EPIC-E015)

> Abgestimmt mit `../CHECKLIST.md` („SpocR v5 Roadmap“); Migrationsthemen werden dort gespiegelt.

## Statusüberblick (Stand 2025-10-27)

- Kernpipeline (`SnapshotBuildOrchestrator` inkl. Collect/Analyze/Write/Cache/Diagnostics) läuft in `src/SpocRVNext/SnapshotBuilder` und treibt das CLI-`pull`.
- Deterministische Artefaktformate (Writer, Cache, Index) samt Telemetrie sind aktiv; Legacy-Pfad dient nur noch als Bridge.
- ENV-first Konfiguration ersetzt `spocr.json`; `.env`-Bootstrapper liefert Schema/Namespace-Defaults, Fallback warnend.
- Performance-Baseline dokumentiert (`src/SpocRVNext/SnapshotBuilder/README.md`, Messwerte vom 2025-10-26).

## Offene Arbeit vor Migration

- [ ] Diagnose- und Typauflösungs-Läufe dokumentieren (`--no-cache --verbose`, Vergleichsläufe mit Cache, zielgerichtete `--procedure`-Runs, Diffs unter `debug/test-summary.json`).
- [ ] Kommentarpfade und Fallbacks für `FOR JSON` verifizieren; verbleibende Sonderfälle in Analyzer-Stories aufnehmen.
- [ ] Beispiel-Trace und Verbose-Ausgabe für Fehlersuche aufbereiten und im README referenzieren.
- [ ] Telemetrie-Ausgaben (CLI, `SPOCR_SNAPSHOT_SUMMARY[_PATH]`) konsolidieren und Monitoring-Checkliste ergänzen.

## Migration-gekoppelte Aufgaben (koordiniert mit `../CHECKLIST.md`)

- [ ] Tests und Golden Snapshots in `SpocR.Tests` auf neue Artefakte anheben (`SchemaSnapshotService`, Determinism-Checks).
- [ ] Obsolete Snapshot-Felder (`SnapshotResultColumn.JsonPath`, `JsonResult`, `DeferredJsonColumns`) entfernen und Downstream-Konsumenten migrieren.
- [ ] Eigenständige JSON-/Aggregate-Analyzer finalisieren (AVG/SUM/COUNT, Nested JSON, Exec Forwarding ohne Legacy-Parser).
- [ ] Abschlusskriterien erfüllen: vollständige Test-Suite reaktivieren, Legacy-Brücke abbauen, Determinism-Checks grün.
- [ ] Snapshot-spezifische Schritte in `migration-v5.instructions` dokumentieren (ENV-Migration, neue Artefakte, CLI-Hinweise).

## Entscheidungen & Referenzen

- Cache bleibt JSON-basiert und diffbar; Snapshots referenzieren nur `TypeRef` statt redundanter Typdaten.
- Legacy-Output wird über eine Bridge beliefert, bis alle Konsumenten auf den neuen Generator umgestellt sind.
- Konzept für Default-Schema-Normalisierung bei schema-losen `EXEC`-Aufrufen ist noch offen.
- Zuordnung ScriptDom → `ProcedureModel.ResultSets` für JSON-/Aggregat-Flags benötigt noch ein belastbares Mapping.

## Artefakte

- README: `src/SpocRVNext/SnapshotBuilder/README.md`.
- Diagnostics & Summary: CLI `pull` mit `SPOCR_SNAPSHOT_SUMMARY[_PATH]`.
- Debugdaten: `debug/.spocr/schema`, `debug/test-summary.json`.

> Offene Punkte bitte mit dem Gesamtplan in `../CHECKLIST.md` (Abschnitt „SnapshotBuilder & StoredProcedureContentModel“) synchron halten.
