---
title: Willkommen zu SpocR
description: Einstieg und Überblick über das SpocR Code-Generierungs-Ökosystem.
layout: doc
---

# SpocR Dokumentation

SpocR ist ein Code-Generator für SQL Server Stored Procedures und erzeugt stark typisierte C# Klassen für Inputs, Outputs und Ausführung.

## Ziele

- Schnelle Integration in bestehende .NET Lösungen
- Minimierung manueller Boilerplate
- Konsistentes Naming & Struktur
- Erweiterbarkeit und Automatisierbarkeit

## Was dich hier erwartet

- Getting Started (Installation & Quickstart)
- CLI Referenz
- Architektur & Konzepte
- Konfigurations-Referenz
- Erweiterung & Anpassung

## Quick Preview

```bash
spocr create --project MyProject
spocr pull --connection "Server=.;Database=AppDb;Trusted_Connection=True;"
spocr build
```

## Nächste Schritte

Gehe zu [Getting Started](/getting-started/installation).
