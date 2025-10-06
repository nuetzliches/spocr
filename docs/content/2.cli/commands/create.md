---
title: create
description: Initialize SpocR configuration and directory structure.
versionIntroduced: 4.0.0
experimental: false
authoritative: true
aiTags: [cli, create, init]
---

# create

Initialize a project for use with SpocR and create a `spocr.json` among other files.

## Usage

```bash
spocr create [Options]
```

## Behavior Contract (Draft)

```json
{
  "command": "create",
  "inputs": {},
  "outputs": {
    "writes": ["spocr.json"],
    "console": ["CreatedConfig"],
    "exitCodes": { "0": "Success", "1": "AlreadyExists" }
  }
}
```

## Examples

```bash
spocr create --project Demo.Data
```
