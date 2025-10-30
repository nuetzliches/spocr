---
title: CLI Overview
description: Overview of SpocR command-line interface and global options.
---

# CLI Overview

The SpocR CLI operates against `.env` files that declare connection strings, namespaces, and generator flags. Run `spocr init` once to scaffold the configuration, then reuse the generated `.env` across `pull`, `build`, and future commands.

## Global Options

| Option | Description |
| ------ | ----------- |
| `-p, --path <dir>` | Override the working directory (must contain the target `.env`). |
| `-v, --verbose` | Emit detailed logging (pipeline steps, timings, cache hints). |
| `--debug` | Use the debug environment wiring (mirrors legacy `--debug` switch). |
| `--no-cache` | Force a full snapshot rebuild in `pull`, ignoring cached metadata. |
| `--procedure <schema.proc>` | Limit `pull` (and future generators) to matching stored procedures (wildcards supported). |

## Core Commands

| Command   | Purpose                                                                 |
| --------- | ----------------------------------------------------------------------- |
| `init`    | Bootstrap `.env` configuration and namespace metadata                   |
| `pull`    | Read stored procedures & schema into `.spocr` using `.env` credentials  |
| `build`   | Generate runtime artifacts (table types, helpers) from the current snapshot |
| `rebuild` | Run `pull` and `build` in sequence for a clean refresh                   |
| `remove`  | Legacy placeholder (prints deprecation notice)                          |
| `version` | Display installed and latest CLI versions                               |
| `config`  | Manage `.env` defaults and template paths                               |
| `project` | List or modify registered project roots                                 |
| `schema`  | Legacy placeholder retained for compatibility (no active subcommands)   |

## Examples

```bash
spocr init --namespace Acme.Product.Data --connection "Server=.;Database=AppDb;Trusted_Connection=True;"
spocr pull -p debug --no-cache --verbose
spocr build -p debug --generators TableTypes,StoredProcedures
dotnet test tests/Tests.sln
```
