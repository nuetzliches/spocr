---
title: build
description: Executes code generation based on current configuration.
versionIntroduced: 4.0.0
experimental: false
authoritative: true
aiTags: [cli, build, generation]
---

# build

The `build` command generates all configured artifacts (input/output models, DbContext parts, mappings, etc.).

## Usage

```bash
spocr build [Optionen]
```

## Options (Excerpt)

| Option                | Type   | Description                                       |
| --------------------- | ------ | ------------------------------------------------- |
| `--project <name>`    | string | Override target project                           |
| `--force`             | flag   | Overwrite existing files if necessary             |
| `--generators <list>` | string | Comma-separated list to limit specific generators |
| `--verbose`           | flag   | More verbose logging                              |

## Behavior Contract (Draft)

```json
{
  "command": "build",
  "inputs": {
    "--project": { "type": "string", "required": false },
    "--force": { "type": "boolean", "required": false },
    "--generators": {
      "type": "string",
      "required": false,
      "format": "comma-list"
    },
    "--verbose": { "type": "boolean", "required": false }
  },
  "outputs": {
    "writes": ["Output/**/*.cs"],
    "console": ["SummaryTable", "Warnings", "Errors"],
    "exitCodes": {
      "0": "Success",
      "1": "ValidationError",
      "2": "GenerationError"
    }
  }
}
```

## Examples

```bash
spocr build
spocr build --verbose
spocr build --generators Inputs,Outputs

---
Note: This document was translated from German on 2025-10-02 to comply with the English-only language policy.
```
