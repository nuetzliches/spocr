---
title: CLI Overview
description: Overview of SpocR command-line interface and global options.
---

# CLI Overview

The SpocR CLI provides commands for project initialization, synchronization, and code generation.

## Global Options (Excerpt)

| Option      | Description     |
| ----------- | --------------- |
| `--help`    | Show help       |
| `--verbose` | Verbose logging |

## Core Commands

| Command   | Purpose                                        |
| --------- | ---------------------------------------------- |
| `create`  | Initialize project structure and configuration |
| `pull`    | Read stored procedures & schema from database  |
| `build`   | Execute code generation                        |
| `rebuild` | Clean and regenerate                           |
| `remove`  | Remove generated artifacts                     |
| `test`    | Run tests and validations                      |
| `version` | Show version                                   |
| `config`  | Manage `spocr.json`                            |
| `project` | Project-related operations                     |
| `schema`  | Work with database schema                      |
| `sp`      | Single stored procedure operations             |

## Examples

```bash
spocr build --verbose
spocr pull --connection "Server=.;Database=AppDb;Trusted_Connection=True;"
spocr test --validate
```
