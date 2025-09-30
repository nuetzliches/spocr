---
title: Quickstart
description: Von Null zum ersten generierten Code in wenigen Minuten.
---

# Quickstart

## 1. Projekt vorbereiten

```bash
mkdir DemoSpocr
cd DemoSpocr
dotnet new classlib -n Demo.Data
```

## 2. SpocR konfigurieren

```bash
spocr create --project Demo.Data
```

Dies erzeugt u.a. eine `spocr.json`.

## 3. Stored Procedures aus Datenbank ziehen

```bash
spocr pull --connection "Server=.;Database=AppDb;Trusted_Connection=True;"
```

## 4. Code generieren

```bash
spocr build
```

Erzeugte Dateien findest du im `Output/` Verzeichnis.

## 5. Beispiel-Aufruf (pseudocode)

```csharp
var ctx = new GeneratedDbContext(connectionString);
var result = await ctx.MyProcedureAsync(new MyProcedureInput { Id = 5 });
```

## 6. Änderungen erneuern

```bash
spocr rebuild
```

## Weiterführend

- [CLI Übersicht](/cli/)
- [Konfiguration](/reference/configuration-schema)
