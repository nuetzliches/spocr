---
title: Installation
description: Installation von SpocR und Grundvoraussetzungen.
---

# Installation

## Voraussetzungen

- .NET SDK (6.0 oder höher, empfohlen 8.0+)
- Zugriff auf SQL Server Instanz
- Git (optional für Projektintegration)

## Globale Installation

```bash
dotnet tool install --global SpocR
```

Aktualisieren:

```bash
dotnet tool update --global SpocR
```

Version prüfen:

```bash
spocr version
```

## Lokale (projektgebundene) Installation

```bash
dotnet new tool-manifest
dotnet tool install SpocR
```

Ausführen (lokal):

```bash
dotnet tool run spocr version
```

## Nächster Schritt

Weiter zu [Quickstart](/getting-started/quickstart).
