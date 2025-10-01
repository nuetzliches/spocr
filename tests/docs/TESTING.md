# SpocR Testing Framework

🧪 **Comprehensive testing infrastructure for automated validation and KI-Agent integration**

## Quick Start

### Running Tests

```bash
# Run all tests
spocr test

# Validate generated code only
spocr test --validate

# CI-friendly mode with reports
spocr test --ci --output junit.xml

# Run performance benchmarks
spocr test --benchmark
```

### Test Structure (aktualisiert)

```
tests/
├── SpocR.Tests/               # Unit tests (net8)
├── SpocR.IntegrationTests/    # (geplant) Integration tests
├── SpocR.TestFramework/       # Shared test infrastructure
└── docs/                      # Test-Dokumentation (diese Datei)
```

Produktionscode verbleibt in `src/`.

## Features

### ✅ **Unit Testing**
- Manager und Service Tests
- Extension Method Tests
- Konfigurations-Validierung
- Dependency Injection Setups

### 🔗 **Integration Testing** (Reaktivierung geplant)
- SQL Server / LocalDB Szenarien
- Schema Validierung
- End-to-End Codegenerierung

### 🔍 **Self-Validation Framework**
- Generierter C# Code Syntax Validierung (Roslyn)
- Kompilierungsprüfung
- Qualitäts-Hooks
- (Geplant) Breaking Change Detection

### 📊 **CI/CD Integration**
- GitHub Actions Workflow
- (Geplant) JUnit XML Output
- (Geplant) Coverage & Benchmarks

## Architektur-Layer

```
🔄 Self-Validation
🧪 Integration Tests (später)
🏗️ Unit Tests (aktiv)
```

## Aktueller Fokus (01.10.2025)

- Minimaler grüner Unit Test erreicht
- Integration Tests deaktiviert bis Fixture vereinfacht
- Testcontainers entfernt (Komplexität reduziert)
- Ziel: Schrittweiser Ausbau Unit Layer → dann Integration

## Beispiel: Developer Workflow

```bash
dotnet test tests/SpocR.Tests
spocr test --validate
```

## Roadmap (gekürzt)

1. Mehr Unit Tests reaktivieren
2. Einfaches DB-Fixture (LocalDB) hinzufügen
3. Integration Test (Connection + einfache Query)
4. JUnit/XML Export implementieren
5. Coverage aktivieren
6. Performance Benchmark optional

---
Aktualisiert nach Migration der Test-Artefakte aus `src/` → `tests/`.