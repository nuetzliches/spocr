---
title: Quickstart
description: From zero to first generated code in minutes.
version: 5.0
---

# Quickstart

Follow these steps to install the SpocR CLI, initialize configuration, and generate your first client code.

## Prerequisites

- .NET 8 SDK (or newer) available on the PATH
- Network access to the SQL Server instance that hosts your stored procedures

## 1. Install or Update the CLI

```bash
dotnet tool update -g spocr
```

`dotnet tool update` installs the tool if it is missing and upgrades it when a newer version ships.

## 2. Initialize Configuration

```bash
spocr init --namespace Demo.Data --connection "Server=.;Database=AppDb;Trusted_Connection=True;" --schemas core,identity
```

This command creates (or updates) a project-scoped `.env` file with generator settings such as `SPOCR_NAMESPACE`, `SPOCR_GENERATOR_DB`, and `SPOCR_BUILD_SCHEMAS`. Re-running `spocr init` safely updates only the keys you specify.

## 3. Pull Database Metadata

```bash
spocr pull
```

`spocr pull` downloads the latest stored procedure metadata and prepares it for the generator. End users do not interact with the repository's `debug/` artifacts; everything needed for your project remains alongside the `.env` file.

## 4. Generate Code

```bash
spocr build
```

Generated files land in the `SpocR/` directory by default. Override the location by setting `SPOCR_OUTPUT_DIR` in `.env` or by passing `--output` on the command line.

## 5. Run Tests (Optional)

```bash
spocr test
```

Use the test command to execute generated integration tests once you wire them into your solution.

## Next Steps

- Review and commit the contents of the `SpocR/` directory.
- Customize the `.env` file with additional settings from [Environment Bootstrap & Configuration](../3.reference/env-bootstrap.md).
- Explore additional commands in the [CLI Reference](../2.cli/index.md).
