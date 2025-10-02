# SpocR Documentation Concept (spocr-docs)

## 1. Documentation Goals

- Provide a clearly structured, versioned, extensible product & developer documentation set
- Reduce onboarding time for new users & contributors
- Enable automated evolution via AI agents (code & knowledge extraction)
- Single Source of Truth for:
  - Feature scope & architecture
  - Naming & conventions
  - CLI commands & options
  - Configuration schema
  - Extensibility / plugin scenarios
  - Migration & upgrade guidance across versions

## 2. Technology Stack

- Framework: [Nuxt Content](https://content.nuxt.com/docs/getting-started) (static export possible; Markdown + MDC)
- Structure: `docs/` root as future Nuxt project (currently concept only)
- Build options:
  - Static export for GitHub Pages (e.g. `/docs-site` branch or `gh-pages`)
  - Optional containerization (Node 20 Alpine) for reproducible builds
- SEO & DX: auto-generated sidebar, table of contents, full‑text search (Nuxt Content Search / Algolia optional)
- Using Nuxt UI documentation from https://ui.nuxt.com/llms.txt
- Follow complete Nuxt UI guidelines from https://ui.nuxt.com/llms-full.txt

## 3. Planned Content Structure

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

## 4. Documentation Versioning Concept

- Directory per major version: `content/v4/`, `content/v5/`, etc.
- `content/latest` as symlink or copy of highest stable version
- Shared, versioned JSON schema files under `content/shared/schemas/`
- Automated sync script (Node) for:
  - Diff between versions (changelog generation)
  - Marking deprecated content via frontmatter (`deprecated: true` + notice block)
- Mark experimental features with frontmatter flag `experimental: true`
- Set up https://content.nuxt.com/docs/integrations/llms

### Frontmatter Standards

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

Goal: Make documentation machine-consumable to:

- Auto-generate tests / validation scripts
- Validate API/CLI behavior against implementation
- Improve chatbot prompts (error analysis, suggestion quality)

### Measures

1. Structured frontmatter with domain-specific fields
2. JSON/YAML artifacts per command & config schema (easy to parse)
3. Consistent terminology definitions in `glossary.md`
4. Embeddings preparation: chunk segmentation (<= 1.5k tokens)
5. Tagging system (`aiTags`) for clustering
6. Machine-readable mapping: Stored procedure name -> generated classes -> file paths
7. "Behavior Contracts" section per command including:

- Inputs (parameters + type + required)
- Outputs (files / console / exit codes)
- Error cases & exit codes

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

## 6. Planned Migration Path (Phases)

1. Concept (this document) + maintainer validation
2. Nuxt Content scaffold + landing + getting started + CLI overview
3. Complete CLI reference + configuration reference (incl. machine-readable JSON schema)
4. Architecture & extensibility + behavior contracts
5. Versioning (v4 snapshot) + upgrade/migration templates
6. AI enrichment (tags, JSON artifacts, embeddings strategy doc)
7. Automated tests (docs linter, frontmatter validator, broken link check)
8. Publication (GitHub Pages / deployment pipeline)

## 7. Quality Assurance & Tooling

- Pre-commit checks: markdown lint, link checker, schema validator
- CI pipeline jobs:
  - Build + lint
  - Frontmatter scan (required fields)
  - Consistency check: CLI commands vs. reflection of `Program.cs`
  - Optional: dead file detector
- Automatic changelog generator based on git tags + conventional commits

## 8. Extensibility & Future Ideas

- Interactive playground (parameters -> generated code preview)
- Live diff viewer across versions of a procedure generation
- Plugin registry page (community extensions)
- "AI Query" endpoint: Q/A across docs + code

### Local Development

Prerequisite: Node.js (>= 18 LTS)

```
cd docs
bun install
bun run dev
```

Then open in browser: http://localhost:3000

## 10. Open Questions

- Which major version as first snapshot baseline? `content/v4/`
- Finalize exit codes set?
- Behavior contracts coverage: only commands (current plan)?

---

---

Note: This document was translated from German on 2025-10-02 to comply with the English-only language policy.

Status: 2025-09-30 (original conceptual date)
