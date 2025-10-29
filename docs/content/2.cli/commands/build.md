---
title: build
description: Executes code generation based on current configuration.
versionIntroduced: 4.0.0
experimental: false
authoritative: true
aiTags: [cli, build, generation]
---

# build

Generates the vNext runtime artifacts (currently table-type builders and related helpers) using the metadata stored under `.spocr/`. JSON helpers ship enabled by default—no preview flags required.

## Usage

```bash
spocr build [options]
```

## Requirements

- A `.env` file seeded via `spocr init` that defines `SPOCR_NAMESPACE`, `SPOCR_GENERATOR_DB`, and optional output tweaks.
- A fresh snapshot in `.spocr/` created by `spocr pull` (or `spocr rebuild`).

## Command-Specific Options

| Option | Description |
| ------ | ----------- |
| `--generators <list>` | Optional comma-separated filter (`TableTypes,Inputs,Models,StoredProcedures`). Only `TableTypes` is active in the current v5 toolchain; additional generators will light up as the pipeline expands. |

> Global flags such as `-p/--path`, `-d/--dry-run`, `-f/--force`, `-v/--verbose`, and `--no-cache` are documented on the [CLI overview](../index.md) and apply to this command as well.

## Behavior Contract (Draft)

```json
{
  "command": "build",
  "inputs": {
    "--generators": {
      "type": "string",
      "required": false,
      "format": "comma-list"
    }
  },
  "reads": [".env", ".spocr/schema/**/*.json"],
  "writes": ["<OutputDir>/**/*.cs"],
  "exitCodes": {
    "0": "Success",
    "1": "ValidationError",
    "2": "GenerationError"
  }
}
```

## Examples

```bash
# Generate artifacts for the current directory
spocr build

# Target a sandbox project configured under debug/
spocr build -p debug --verbose

# Explicitly limit the generator pipeline (future-proof)
spocr build --generators TableTypes
```
