# Tests

Dieser Ordner bündelt alle Test-bezogenen Artefakte von SpocR.

## Struktur
```
tests/
  SpocR.Tests/            # Aktive Unit Tests (net8)
  SpocR.IntegrationTests/ # (Geplant) Integration / DB Tests
  SpocR.TestFramework/    # Gemeinsame Test-Hilfen & Validatoren
  docs/                   # Test-Dokumentation & Status
```

## Schneller Start
```bash
# Self-Validation (Generator + Syntax)
spocr test --validate

# Unit Tests
 dotnet test tests/SpocR.Tests
```

## Zielsetzung
1. Schnelle Rückmeldung (Self-Validation) vor jedem Commit
2. Stabiler Ausbau Unit Tests → dann Integration Layer
3. Später erweiterbar: Benchmarks, Coverage, Rollback, JUnit/XML Output

## Roadmap Kurzfassung
- [x] Migration nach /tests
- [x] Minimaler grüner Unit Test
- [ ] Reaktivierung ursprünglicher Unit Tests
- [ ] LocalDB / vereinfachtes DB Fixture
- [ ] Erster Integration Test
- [ ] JUnit/XML Ausgabe für CI
- [ ] Coverage aktivieren

Weitere Details: `docs/TESTING.md`
