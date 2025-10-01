---
title: Quickstart
description: From zero to first generated code in minutes.
---

# Quickstart

## 1. Prepare Project

```bash
mkdir DemoSpocr
cd DemoSpocr
dotnet new classlib -n Demo.Data
```

## 2. Configure SpocR

```bash
spocr create --project Demo.Data
```

This creates a `spocr.json` among other files.

## 3. Pull Stored Procedures from Database

```bash
spocr pull --connection "Server=.;Database=AppDb;Trusted_Connection=True;"
```

## 4. Generate Code

```bash
spocr build
```

Generated files can be found in the `Output/` directory.

## 5. Example Usage (pseudocode)

```csharp
var ctx = new GeneratedDbContext(connectionString);
var result = await ctx.MyProcedureAsync(new MyProcedureInput { Id = 5 });
```

## 6. Refresh Changes

```bash
spocr rebuild
```

## Further Reading

- [CLI Overview](/cli/)
- [Configuration](/reference/configuration-schema)
