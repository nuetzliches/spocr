# Snapshot-Only Migration Plan

## Ziel

Ablösung des Schema-Knotens in `spocr.json` zugunsten eines kanonischen Snapshots unter `.spocr/schema/<fingerprint>.json`.

## Aktueller Stand (Snapshot-Only produktiv)

- Pull erzeugt Snapshot (Procedures, ResultSets, UDTTs, Schemas, ParserInfo, Stats)
- Build konsumiert ausschließlich Snapshot (kein Fallback auf `config.Schema`)
- Legacy `schema` Knoten wird nach Migration entfernt (nur einmaliger automatischer Transfer von IGNORE → `project.ignoredSchemas`)
- JSON ResultSets nur noch via UDTT-Abgleich (Heuristik vollständig entfernt)
- Multi-ResultSets mit Suffix `_1`, `_2`, ...
- Globale Ignorierliste: `project.ignoredSchemas` ersetzt Statusliste

## Deprecation Abschluss

Die ursprünglich geplante mehrstufige Abschaltung des Schema-Knotens ist abgeschlossen. Anstelle eines manuellen Migrationstools erfolgt die Migration opportunistisch beim ersten `pull` nach dem Upgrade:

1. Sammle alle Schemas mit Status `Ignore` aus `schema[]`
2. Schreibe deren Namen als eindeutige Liste nach `project.ignoredSchemas`
3. Setze `schema` auf `null` und speichere die Datei
4. Nachfolgende Pulls/Builds ignorieren den Knoten vollständig

Ein optionaler finaler Schritt (Entfernen der Property aus dem Modell) bleibt vorerst zurückgestellt, um ältere Configs mit explizitem Knoten robust zu lesen (Backward Compatibility / Soft Landing). Die Property wird nur noch serialisiert, falls nicht `null`.

## CLI Workflow (neu)

| Aktion                     | Befehl                 | Beschreibung                            |
| -------------------------- | ---------------------- | --------------------------------------- |
| Erstinitialisierung        | `spocr pull`           | Lädt DB-Metadaten und erstellt Snapshot |
| Generieren                 | `spocr build`          | Nutzt letzten Snapshot                  |
| Aktualisieren + Generieren | `spocr rebuild`        | Pull + Build kombiniert                 |
| Snapshot bereinigen        | `spocr snapshot clean` | Löscht alte Snapshots                   |

## Branching Modell

- `main`: stabile Releases
- `develop`: Integrations-/Vorbereitungsbranch für nächste Minor / Major
- `feature/*`: isolierte Features, Merge -> `develop`
- Optional: `hotfix/*` direkt gegen `main` (anschließend Merge zurück in `develop`)

## Technische Komponenten

| Bereich           | Typ                       | Notizen                                                                              |
| ----------------- | ------------------------- | ------------------------------------------------------------------------------------ |
| Snapshot Service  | `ISchemaSnapshotService`  | Speichern / Laden nach Fingerprint                                                   |
| Metadata Provider | `ISchemaMetadataProvider` | Snapshot→Runtime Mapping (einmalig, gecached)                                        |
| Cache             | `ILocalCacheService`      | Änderungsprüfung (modify_date) für schnelle Pulls                                    |
| Heuristik         | intern                    | JSON Column Typableitung (Id, Is*/Has*, Date, Code/Type/Status, Message/Description) |

## Offene Punkte

- Tests für Snapshot-Ladefehler / defekten JSON (korruptes File → Fallback / Fehlerbild)
- CI-Doku: Wann ist ein Re-Pull erforderlich (DB-Migration Commits / geänderte UDTTs)
- Optionale Komprimierung für historisierte Snapshots (Prio niedrig)
- Feinere Parser-Versionsmarkierung (ResultSet Parser vs. Tool Version)

## Risiken

| Risiko                                       | Mitigation                                          |
| -------------------------------------------- | --------------------------------------------------- |
| Veralteter Snapshot führt zu falschen Models | Rebuild im CI erzwingen bei DB-Migrations-Commits   |
| Heuristik liefert unpassenden Typ            | Flag + Manual Override später                       |
| Großes Snapshot File Wachstum                | Optional: Komprimierung / Pruning älterer Snapshots |

## Nächste Schritte

1. Ergänzende Tests für beschädigte Snapshot-Dateien (korruptes JSON → klare Fehlermeldung)
2. Erweiterte Dokumentation: Decision Log zur Entfernung der Heuristik
3. Optionale CLI: `spocr snapshot info` (Anzeige des aktiven Fingerprints)
4. Performance Telemetrie (ms pro Phase) im Verbose Output konsolidieren
5. Evaluierung: Entfernen der verbleibenden `schema` Property aus dem Config-Modell in Major Release

---

Dieses Dokument dient als Arbeitsgrundlage für die Migration und wird mit jedem Schritt aktualisiert.
