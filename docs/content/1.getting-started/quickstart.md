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

## 7. Update / Install the CLI from the local repository

If you have cloned the SpocR repository and want to build & use the current source as a global .NET tool:

```bash
cd src
# Build a NuGet package (Release)
dotnet pack -c Release -o ./nupkg
# Remove existing global installation (ignores error if not installed)
dotnet tool uninstall -g spocr
# Install or update from the freshly built local package source
dotnet tool update -g spocr --add-source ./nupkg
```

After that:

```bash
spocr --version
```

Notes:

- `dotnet tool update` acts as install if the tool was removed.
- Repeat the pack & update steps whenever you change the source.
- To force a specific version (e.g. during testing): `dotnet pack -c Release -o ./nupkg /p:Version=4.1.35-local`

## Further Reading

- [CLI Overview](/cli/)
- [Configuration](/reference/configuration-schema)
