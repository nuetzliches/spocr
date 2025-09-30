# SpocR Documentation Concept (spocr-docs)

## 1. Ziele der spocr-Dokumentation

- Bereitstellung einer klar strukturierten, versionierten und erweiterbaren Produkt- und Entwicklerdokumentation
- Reduktion der Einarbeitungszeit für neue Nutzer & Contributor
- Unterstützung automatisierter Weiterentwicklung durch AI-Agents (Code- & Wissensextraktion)
- Single Source of Truth für:
  - Funktionsumfang & Architektur
  - Naming & Konventionen
  - CLI-Befehle & Optionen
  - Konfigurationsschema
  - Erweiterbarkeit / Plugin-Szenarien
  - Migrations- und Upgrade-Hinweise zwischen Versionen

## 2. Technologische Basis

- Framework: [Nuxt Content](https://content.nuxt.com/docs/getting-started) (statischer Export möglich, Markdown + MDC)
- Struktur: `docs/` Root als zukünftiges Nuxt-Projekt (zunächst nur inhaltliches Konzept)
- Build-Optionen:
  - Statischer Export für GitHub Pages (z.B. `/docs-site` Branch oder `gh-pages`)
  - Optional Containerisierung (Node 20 Alpine) für reproduzierbare Builds
- SEO & DX: Autogenerierte Sidebar, Inhaltsverzeichnis, Volltextsuche (Nuxt Content Search / Algolia optional)

## 3. Geplante Inhaltsstruktur

```
/docs
  |-- README.md (dieses Konzept)
  |-- nuxt.config.ts (später)
  |-- content/
        |-- index.md (Landing / Übersicht)
        |-- getting-started/
        |     |-- installation.md
        |     |-- quickstart.md
        |-- concepts/
        |     |-- architecture-overview.md
        |     |-- naming-conventions.md
        |     |-- configuration-model.md
        |     |-- generator-pipeline.md
        |     |-- deployment-models.md (Default / Library / Extension)
        |-- cli/
        |     |-- index.md (Übersicht & Globale Optionen)
        |     |-- commands/
        |            |-- create.md
        |            |-- pull.md
        |            |-- build.md
        |            |-- rebuild.md
        |            |-- remove.md
        |            |-- version.md
        |            |-- config.md
        |            |-- project.md
        |            |-- schema.md
        |            |-- sp.md
        |-- guides/
        |     |-- integrating-into-ci.md
        |     |-- customizing-generation.md
        |     |-- working-with-json-procedures.md
        |     |-- troubleshooting.md
        |     |-- performance-tuning.md
        |-- reference/
        |     |-- configuration-schema.md (Machine-readable JSON schema + Erläuterungen)
        |     |-- enums.md
        |     |-- attributes.md
        |     |-- extension-points.md
        |-- upgrade/
        |     |-- migration-4.x-to-5.x.md (Template)
        |-- roadmap/
        |     |-- index.md
        |-- meta/
              |-- contributing.md
              |-- security.md
              |-- release-process.md
              |-- glossary.md
```

## 4. Versionierungskonzept der Dokumentation

- Verzeichnis pro Major-Version: `content/v4/`, `content/v5/` usw.
- `content/latest` als Symlink oder Kopie der höchsten stabilen Version
- Gemeinsame, versionierte JSON Schema Files unter `content/shared/schemas/`
- Automatisierter Sync-Skript (Node) für:
  - Diff zwischen Versionen (Changelog-Generierung)
  - Markierung von Deprecated Inhalten mittels Frontmatter (`deprecated: true` + Hinweisblock)
- Kennzeichnung experimenteller Features mit Frontmatter Flag `experimental: true`

### Frontmatter-Standards

```yaml
---
title: Build Command
description: Führt Code-Generierung basierend auf konfigurativem Schema aus.
versionIntroduced: 4.0.0
versionDeprecated: null
experimental: false
authoritative: true # Quelle gilt als maßgeblich
aiTags: [cli, build, generation, pipeline]
---
```

## 5. AI-Agent Readiness

Ziel: Dokumentation maschinenlesbar machen, um:

- Automatisch Tests / Validierungen zu generieren
- API/CLI-Verhalten gegen Implementation zu prüfen
- Prompt-Optimierung für Chatbots (Fehleranalyse, Vorschläge)

### Maßnahmen

1. Strukturiertes Frontmatter mit domänenspezifischen Feldern
2. JSON-/YAML-Artefakte pro Command & Config-Schema (leicht parsbar)
3. Konsistente Begriffsdefinitionen in `glossary.md`
4. Embeddings-Vorbereitung: Segmentierung in sinnvolle Chunks (<= 1.5k Tokens)
5. Tagging-System (`aiTags`) zur Clustering-Optimierung
6. Maschinenlesbares Mapping: Stored Procedure Name -> Generierte Klassen -> Dateipfade
7. Abschnitt "Behavior Contracts" pro Command mit:
   - Inputs (Parameter + Typ + Pflicht)
   - Outputs (Files / Console / Exit Codes)
   - Fehlerfälle & Exit Codes

### Beispiel Behavior Contract (Build Command)

```json
{
  "command": "build",
  "inputs": {
    "--project": { "type": "string", "required": false },
    "--force": { "type": "boolean", "required": false },
    "--generators": { "type": "string[]", "required": false },
    "--verbose": { "type": "boolean", "required": false }
  },
  "outputs": {
    "files": ["/Output/**.cs"],
    "console": ["SummaryTable", "Warnings", "Errors"],
    "exitCodes": {
      "0": "Success",
      "1": "ValidationError",
      "2": "GenerationError"
    }
  }
}
```

## 6. Geplanter Migrationspfad (Phasen)

1. Phase (Jetzt): Konzept (dieses Dokument) + Validierung mit Maintainer
2. Phase: Nuxt Content Grundgerüst + Landing + Getting Started + CLI Übersicht
3. Phase: Vollständige CLI Referenz + Konfigurations-Referenz (inkl. Machine-readable JSON Schema)
4. Phase: Architektur & Erweiterbarkeit + Behavior Contracts
5. Phase: Versionierung (v4 Snapshot) + Upgrade/Migration Templates
6. Phase: AI-Enrichment (Tags, JSON Artefakte, Embeddings-Strategie Doku)
7. Phase: Automatisierte Tests (Docs Linter, Frontmatter Validator, Broken Link Check)
8. Phase: Veröffentlichung (GitHub Pages / Deployment Pipeline)

## 7. Qualitätssicherung & Tooling

- Pre-commit Checks: Markdown Lint, Link Checker, Schema Validator
- CI Pipeline Jobs:
  - Build + Lint
  - Frontmatter Scan (Pflichtfelder)
  - Konsistenzprüfung: CLI Commands vs. Program.cs Reflektion
  - Optional: Dead File Detector
- Automatischer Changelog Generator basierend auf Git Tags + Conventional Commits

## 8. Erweiterbarkeit & Zukunft

- Option: Interaktive Playground-Seite (Parameter -> Generierter Code Preview)
- Option: Live Diff Viewer zwischen Versionen einer Prozedur-Generierung
- Option: Plugin Registry Seite (Community Erweiterungen)
- Option: "AI Query" Endpoint: Q/A über Dokumentation + Code

## 9. Nächste direkte Schritte (Empfehlung)

- Feedback zum Konzept einholen
- Verzeichnisstruktur initial anlegen (Phase 2 Start)
- Frontmatter-Standard festschreiben und Linter definieren

## 10. Offene Fragen

- Welche Major-Version als erste Snapshot-Basis? (4.1.x?) Ja
- Exit Codes final definieren? Weiß nicht
- Umfang Behavior Contracts: Nur Commands oder auch Generatoren intern? Nur Commands.

---

Stand: 2025-09-30
