---
title: Installation
description: Installing SpocR and basic requirements.
---

# Installation

## Prerequisites

- .NET SDK (6.0 or higher, recommended 8.0+)
- Access to SQL Server instance
- Git (optional for project integration)

## Global Installation

```bash
dotnet tool install --global SpocR
```

Update:

```bash
dotnet tool update --global SpocR
```

Check version:

```bash
spocr version
```

## Local (project-bound) Installation

```bash
dotnet new tool-manifest
dotnet tool install SpocR
```

Execute (local):

```bash
dotnet tool run spocr version
```

## Next Step

Continue to [Quickstart](/getting-started/quickstart).
