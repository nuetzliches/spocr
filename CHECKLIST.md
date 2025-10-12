- [ ] : offene Aufgabe
- [x] : erledigte Aufgabe

- [ ] LEGACY-FREEZE v4.5: Generator-Code für bisherigen DataContext einfrieren (nur kritische Bugfixes). Siehe Abschnitt "Migration / Breaking Changes".
- [ ] SAMPLE-GATE: Referenz-Sample `samples/restapi` nutzt aktuelle Konfiguration (`spocr.json`) und wird in CI gebaut (Abschnitt "Samples / Demo").
- [ ] NEUER GENERATOR: Ordner `src/SpocRVNext` angelegt + Grundstruktur (Abschnitt "Codegenerierung / SpocRVNext").
- [ ] NEUER OUTPUT-ORDNER: Neuer Output-Pfad (Arbeitsname) erstellt; parallele Erzeugung aktiv (Abschnitt "Codegenerierung / SpocRVNext").
- [ ] TEMPLATE ENGINE: Eigene Template Engine implementiert (kein Roslyn) – Abnahme-Kriterien in Abschnitt "Codegenerierung / SpocRVNext".
- [ ] DUAL-GENERATION v4.5: Alter + neuer Output gleichzeitig generiert (Abschnitt "Codegenerierung / SpocRVNext").
- [ ] MODERNER DBKONTEXT: `SpocRDbContext` + Minimal API Extensions vorhanden (Abschnitt "Codegenerierung / SpocRVNext").
- [ ] HEURISTIK-ENTFALL: Restriktive Namens-/Strukturheuristiken entfernt; Regressionstests vorhanden (Abschnitt "Qualität & Tests").
- [ ] KONFIG-BEREINIGUNG: Entfernte Properties aus `spocr.json` dokumentiert (Abschnitt "Migration / Breaking Changes").
- [ ] AUTO-NAMESPACE: Namespace-Auto-Ermittlung implementiert + Fallback dokumentiert (Abschnitt "Codegenerierung / SpocRVNext").
- [ ] CUTOVER PLAN v5.0: Entfernen DataContext + Verschieben neuer Generator Inhalt nach `/src` beschrieben (Abschnitt "Migration / Breaking Changes").
- [ ] OBSOLETE-MARKER: Alte Outputs als [Obsolet] gekennzeichnet inkl. Migrationshinweis (Abschnitt "Migration / Breaking Changes").
- [ ] DOKU-UPDATE: Relevante Doku-Abschnitte markiert/ergänzt (Abschnitt "Dokumentation").
- [ ] TEST-UPDATE: Test-Suite an neuen Generator angepasst (Abschnitt "Qualität & Tests").

### Qualität & Tests

- [ ] Alle bestehenden Unit- & Integrationstests grün (Tests.sln)
- [ ] Neue Tests für SpocRVNext (Happy Path + Fehlerfälle + Regression für entfernte Heuristiken)
- [ ] Snapshot-/Golden-Master-Vergleich für generierten Code (alter DataContext vs. neuer Output) aktualisiert
- [ ] Automatisierte Qualitäts-Gates (eng/quality-gates.ps1) lokal und in CI erfolgreich
- [ ] Test-Hosts nach Läufen bereinigt (eng/kill-testhosts.ps1) – kein Leak mehr
- [ ] Code Coverage Mindestschwelle definiert und erreicht (Ziel: >80% Core-Logik)
- [ ] Negative Tests für ungültige spocr.json Konfigurationen

### Codegenerierung / SpocRVNext

- [ ] Template Engine Grundgerüst fertig (ohne Roslyn Abhängigkeiten)
- [ ] Ermittlung des Namespaces automatisiert und dokumentierte Fallback-Strategie vorhanden
- [ ] Entfernte Spezifikationen/Heuristiken sauber entfernt und CHANGELOG Eintrag erstellt
- [ ] Neuer `SpocRDbContext` implementiert inkl. moderner DI Patterns & Minimal API Extensions
- [ ] Parallel-Erzeugung alter (DataContext) und neuer (SpocRVNext) Outputs in v4.5 stabil
- [ ] Schalter/Feature-Flag zum Aktivieren des neuen Outputs vorhanden (CLI Parameter oder Konfig)
- [ ] Konsistenz-Check für generierte Dateien (deterministische Generierung: gleiche Eingabe => gleiche Ausgabe)
- [ ] Performance Messung: Generierungsdauer dokumentiert (Baseline vs. Neuer Ansatz)

### Migration / Breaking Changes

- [ ] Alle als [Obsolet] markierten Typen enthalten klaren Hinweis & Migrationspfad
- [ ] Dokumentierter Cut für v5.0 (Entfernung DataContext) in README / ROADMAP
- [ ] Liste entfallener Konfig-Properties (Project.Role.Kind, RuntimeConnectionStringIdentifier, Project.Output) im Changelog
- [ ] Upgrade Guide (docs/5.roadmap oder eigener Pfad) erstellt
- [ ] SemVer Bewertung durchgeführt (Minor vs. Major Bump begründet)

### Konfiguration & Artefakte

- [ ] Beispiel `spocr.json` im Sample aktualisiert (ohne entfallene Properties)
- [ ] Validierungsskript/Schema für spocr.json hinzugefügt oder aktualisiert
- [ ] Debug-Konfigurationen (debug/\*.json) konsistent mit neuen Pfaden
- [ ] Output-Pfade (Output/, Output-v5-0/, etc.) aufgeräumt / veraltete entfernt sofern Version >=5.0 (post-migration)

### Dokumentation

- [ ] docs Build läuft (Bun / Nuxt) ohne Fehler
- [ ] Neue Seiten für SpocRVNext (Architektur, Unterschiede, Migration) hinzugefügt
- [ ] Referenzen (CLI, Konfiguration, API) aktualisiert
- [ ] README Quick Start an neuen Generator angepasst
- [ ] CHANGELOG.md Einträge für jede relevante Änderung ergänzt (Added/Changed/Removed/Deprecated/Migration Notes)
- [ ] DEVELOPMENT.md um Build-/Test-Flows für neuen Generator erweitert
- [ ] Samples/README verlinkt auf aktualisierte Doku

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

### Sonstiges

- [ ] Konsistenter Stil der Commit Messages (Konvention definiert, z.B. Conventional Commits)
- [ ] Offene TODO Kommentare bewertet / priorisiert / entfernt falls nicht mehr nötig
- [ ] Issue Tracker Abgleich: Alle Items dieses Releases geschlossen oder verschoben
- [ ] Technische Schuldenliste aktualisiert

... (bei Bedarf weiter ergänzen) ...
