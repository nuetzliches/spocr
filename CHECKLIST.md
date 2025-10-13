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

Legende: `[ ]` offene Aufgabe · `[x]` erledigt

EPICS Übersicht (oberste Steuerungsebene)

- [x] EPIC-E001 LEGACY-FREEZE v4.5
      id: E001
      goal: Generator-Code für bisherigen DataContext einfrieren (nur kritische Bugfixes)
      acceptance: - Keine funktionalen Änderungen an Legacy-Generator nach Freeze-Datum - Nur sicherheits-/stabilitätsrelevante Fixes - Dokumentierter Freeze in CHANGELOG
      depends: []
      note: Freeze-Datum & Sentinel (legacy-freeze.txt) gesetzt; CHANGELOG Eintrag vorhanden

- [ ] EPIC-E002 SAMPLE-GATE Referenz-Sample stabil
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

- [ ] EPIC-E005 Eigene Template Engine
      id: E005
      goal: Roslyn Unabhängigkeit & eigene schlanke Template Pipeline
      acceptance: - Template Parser / Renderer modular - Unit Tests für Placeholder/Substitutions - Performance Messung dokumentiert
      depends: [E003]

- [ ] EPIC-E006 Moderner DbContext & APIs
      id: E006
      goal: `SpocRDbContext` + Minimal API Extensions
      acceptance: - DI Registrierung (IServiceCollection) vorhanden - Minimal API Mappings generierbar - Beispiel-Endpunkt im Sample funktioniert
      depends: [E003]

- [ ] EPIC-E007 Heuristik-Abbau
      id: E007
      goal: Entfernung restriktiver Namens-/Strukturheuristiken
      acceptance: - Liste entfernte Heuristiken dokumentiert - Regressionstests schützen kritische Fälle
      depends: [E003]

- [ ] EPIC-E008 Konfig-Bereinigung
      id: E008
      goal: Entfernte Properties aus `spocr.json` offiziell deklariert
      acceptance: - CHANGELOG Abschnitt "Removed" - Upgrade Guide Eintrag
      depends: [E004]

- [x] EPIC-E009 Auto Namespace Ermittlung
      id: E009
      goal: Automatisierte Namespace Generierung + Fallback
      acceptance: - 90%+ Fälle ohne manuelle Angabe korrekt - Fallback Logik dokumentiert
      depends: [E003]

- [ ] EPIC-E010 Cutover Plan v5.0
      id: E010
      goal: Plan zur Entfernung Legacy DataContext
      acceptance: - README / ROADMAP Eintrag - Timeline + Migrationsschritte
      depends: [E004, E008]

- [ ] EPIC-E011 Obsolete Markierungen
      id: E011
      goal: Alte Outputs als [Obsolet] markiert mit Klartext-Hinweis
      acceptance: - Alle Legacy Artefakte dekoriert / kommentiert - Build Warnungen optional aktivierbar
      depends: [E010]

- [ ] EPIC-E012 Dokumentations-Update
      id: E012
      goal: Vollständige Doku für neuen Generator
      acceptance: - Architektur, Migration, CLI Referenz - Samples verlinkt & aktuell
      depends: [E004, E005, E006, E007, E008, E009]

- [ ] EPIC-E013 Test-Suite Anpassung
      id: E013
      goal: Tests spiegeln neue Architektur & schützen Migration
      acceptance: - Snapshot / Golden Master - Cover ≥ 80% Core
      depends: [E005, E006, E007, E009]

---

### Operative Tasks aus EPICS (Detail-Aufgaben folgen unter thematischen Sektionen)

### Qualität & Tests

- [ ] Alle bestehenden Unit- & Integrationstests grün (Tests.sln)
- [ ] Neue Tests für SpocRVNext (Happy Path + Fehlerfälle + Regression für entfernte Heuristiken)
- [ ] (Optional) Info-Diff zwischen Legacy und neuem Output generiert (kein Paritäts-Zwang)
- [ ] Automatisierte Qualitäts-Gates (eng/quality-gates.ps1) lokal und in CI erfolgreich
- [ ] Test-Hosts nach Läufen bereinigt (eng/kill-testhosts.ps1) – kein Leak mehr
- [ ] Code Coverage Mindestschwelle definiert und erreicht (Ziel: >80% Core-Logik)
- [ ] Negative Tests für ungültige spocr.json Konfigurationen
- [x] Test: TableTypes Name Preservation (`PreservesOriginalNames_NoRenaming`) sichert unveränderte UDTT Bezeichner
- [x] Entfernte Suffix-Normalisierung für TableTypes (Regression abgesichert)

### Codegenerierung / SpocRVNext

- [x] Template Engine Grundgerüst fertig (ohne Roslyn Abhängigkeiten)
- [x] Ermittlung des Namespaces automatisiert und dokumentierte Fallback-Strategie vorhanden
- [ ] Entfernte Spezifikationen/Heuristiken sauber entfernt und CHANGELOG Eintrag erstellt
- [ ] Neuer `SpocRDbContext` implementiert inkl. moderner DI Patterns & Minimal API Extensions
      - [x] Grundgerüst via Template-Generator (Interface, Context, Options, DI) – aktiviert in `SPOCR_GENERATOR_MODE=dual|next` (ehem. Flag `SPOCR_GENERATE_DBCTX` entfernt)
      - [x] DbContext Optionen (ConnectionString / Name / Timeout / Retry / Diagnostics / ValidateOnBuild) implementiert
      - [x] Scoped Registration Validierung (Connection Open Probe optional via `ValidateOnBuild`)
      - [x] Minimal API Mapper Beispiel (Health Endpoint `/spocr/health/db`)
      - [~] Integration ins Sample (Code registriert & Endpoint gemappt; laufender Prozess beendet sich noch früh – Stabilisierung ausstehend / Doku fehlt)
- [x] Parallel-Erzeugung alter (DataContext) und neuer (SpocRVNext) Outputs in v4.5 (Demo/Beobachtungsmodus) implementiert
- [x] Legacy CLI ruft bei `SPOCR_GENERATOR_MODE=dual` zusätzlich vNext Dispatcher (nur .env / EnvConfiguration, ohne spocr.json Nutzung) auf
- [x] Schalter/Feature-Flag zum Aktivieren des neuen Outputs vorhanden (CLI Parameter oder Konfig)
- [x] Konsistenz-Check für generierte Dateien (Determinismus pro Generator; keine Legacy-Paritäts-Pflicht) – Hash Manifeste vorhanden (noch keine harte Policy) - [x] Timestamp-Zeile neutralisiert (Regex Normalisierung) - [x] Doppelter Schreibpfad Outputs/CrudResult entfernt (Skip base copy)
      note: Konsistenz-Check für generierte Dateien (Determinismus pro Generator; keine Legacy-Paritäts-Pflicht) – Hash Manifeste vorhanden (noch keine harte Policy); Timestamp-Zeile neutralisiert (Regex Normalisierung); Doppelter Schreibpfad Outputs/CrudResult entfernt (Skip base copy)
- [x] TableTypes: Always-On Generation (Interface `ITableType` einmalig, Records je Schema unter `SpocR/<schema>/`) integriert in Build (dual|next)
- [x] TableTypes: Timestamp `<remarks>` Zeile eingefügt und beim Hashing ignoriert (DirectoryHasher Filter)
- [x] TableTypes: Original Snapshot Namen vollständig beibehalten (nur Sanitizing) – keine erzwungene *TableType Suffix Ergänzung

      TODO entfernt: Performance Messung (nicht mehr erforderlich)

### Migration / Breaking Changes

- [ ] Alle als [Obsolet] markierten Typen enthalten klaren Hinweis & Migrationspfad
- [ ] Dokumentierter Cut für v5.0 (Entfernung DataContext) in README / ROADMAP
- [ ] Liste entfallener Konfig-Properties (Project.Role.Kind, RuntimeConnectionStringIdentifier, Project.Output) im Changelog
- [ ] Migration von `spocr.json` auf `.env` / Environment Variablen dokumentiert (Mapping Tabelle)
- [ ] Upgrade Guide (docs/5.roadmap oder eigener Pfad) erstellt
- [ ] SemVer Bewertung durchgeführt (Minor vs. Major Bump begründet)

### Konfiguration & Artefakte

- [ ] Beispiel `spocr.json` im Sample aktualisiert (ohne entfallene Properties)
- [ ] Validierungsskript/Schema für spocr.json hinzugefügt oder aktualisiert
- [ ] Debug-Konfigurationen (debug/\*.json) konsistent mit neuen Pfaden
- [ ] Output-Pfade (Output/, Output-v5-0/, etc.) aufgeräumt / veraltete entfernt sofern Version >=5.0 (post-migration)
- [x] `.env` Beispieldatei hinzugefügt (Pfad: `samples/restapi/.env.example`) inkl. aller relevanten SPOCR\_\* Keys
      note: Enthält SPOCR_GENERATOR_MODE, SPOCR_EXPERIMENTAL_CLI, Bridge Policy Flags
- [ ] Precedence dokumentiert: CLI > ENV > .env > (legacy) spocr.json (Fallback bis v5.0, danach entfernt)
- [ ] `spocr pull` überschreibt lokale Konfiguration nicht mehr (nur interne Metadaten)

### Dokumentation

- [ ] docs Build läuft (Bun / Nuxt) ohne Fehler
- [ ] Neue Seiten für SpocRVNext (Architektur, Unterschiede, Migration) hinzugefügt
- [ ] Referenzen (CLI, Konfiguration, API) aktualisiert
- [ ] README Quick Start an neuen Generator angepasst
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

### Samples / Demo (samples/restapi)

- [ ] Sample baut mit aktuellem Generator (dotnet build)
- [ ] Sample führt grundlegende DB Operationen erfolgreich aus (CRUD Smoke Test)
- [ ] Automatisierter Mini-Test (skriptgesteuert) prüft Generierung & Start der Web API
- [ ] Sample beschreibt Aktivierung des neuen Outputs (Feature Flag) im README

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
- [ ] Roadmap aktualisiert (nächste Meilensteine eingetragen)

### Automatisierung / CI

- [ ] Pipeline Schritt: Codegen Diff (debug/model-diff-report.md) aktuell und verlinkt
- [ ] Fail-Fast bei unerwarteten Generator-Änderungen (Diff Threshold)
- [ ] QA Skripte (eng/\*.ps1) in README oder DEVELOPMENT.md referenziert
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
- [ ] Test: Experimental CLI Flag (`SPOCR_EXPERIMENTAL_CLI`) aktiviert neuen Parser nur bei gesetztem Flag

### Nullable & Codequalität (Ergänzung)

- [x] Globale Nullable aktiviert + Legacy-Unterdrückung via `.editorconfig`
- [x] Selektive Reaktivierung für `SpocRVNext` und neue CLI Entry Points
- [ ] Phase 2: Schrittweises Entfernen alter Suppressions (Tracking Liste)
- [ ] Phase 3: CI Eskalation (`SPOCR_STRICT_NULLABLE=1`) dokumentiert & aktiviert

### Observability / Diff (Ergänzung)

- [x] Hash-Manifeste erzeugt (SHA256) pro Output
- [x] Diff-Report + Allow-List Mechanismus vorhanden
- [ ] Aktivierung reservierter Exit Codes (21–23) bei Policy-Eskalation implementieren
- [ ] Dokumentation: Anleitung zur Pflege der Allow-List (`.spocr-diff-allow`)
- [ ] Optionaler "strict-diff" Modus über ENV / CLI Flag getestet
- [x] Snapshot-Timestamp (`GeneratedUtc`) aus Persistenz entfernt (deterministische Hashes / keine Timestamp-Diffs)
- [x] Hash-Filter erweitert: Ignoriere dynamische `Generated at` Zeilen aus vNext Output-Dateien

### Sonstiges

- [ ] Konsistenter Stil der Commit Messages (Konvention definiert, z.B. Conventional Commits)
- [ ] Offene TODO Kommentare bewertet / priorisiert / entfernt falls nicht mehr nötig
- [ ] Issue Tracker Abgleich: Alle Items dieses Releases geschlossen oder verschoben
- [ ] Technische Schuldenliste aktualisiert
- [ ] (Regel) Implementierung IN CODE vollständig auf Englisch (Kommentare, öffentliche/ interne Bezeichner) – Ausnahme: `CHECKLIST.md` bleibt deutsch
- [ ] (Regel) Keine "VNext" Namensbestandteile in Klassen / Dateien / Properties – Trennung ausschließlich über Ordner & Namespace `SpocRVNext`
- [ ] (Prinzip) Qualität & Wartbarkeit des neuen Outputs > strikte Rückwärtskompatibilität (Breaking Changes sind erlaubt, sofern dokumentiert und migrierbar)
- [ ] XML Kommentare auf den vnext Outputs optimieren. Gehört GeneratedAt in die // <auto-generated/> Zeile?
 
... (bei Bedarf weiter ergänzen) ...
