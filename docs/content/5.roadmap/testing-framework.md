---
title: Testing Framework
description: Comprehensive testing infrastructure for automated validation and KI-Agent integration
---

# Testing Framework

## Overview

SpocR's Testing Framework provides a comprehensive multi-layer testing architecture designed for both KI-Agent automation and CI/CD pipeline integration. The framework ensures code quality, validates generated output, and enables automated testing workflows.

## Architecture

### 🔄 Self-Validation Layer (KI-Agent)
- **Generated Code Validation** - Automatic syntax and compilation checking
- **Schema Consistency Tests** - Database schema validation and change detection  
- **Regression Detection** - Automated detection of breaking changes
- **Rollback Mechanisms** - Safe recovery from failed generations

### 🧪 Integration Test Layer (CI/CD)
- **Database Schema Tests** - Full schema validation with SQL Server
- **End-to-End Scenarios** - Complete generation pipeline testing
- **Performance Benchmarks** - Code generation and runtime performance
- **Cross-Platform Testing** - Multi-framework validation (.NET 8.0/9.0)

### 🏗️ Unit Test Layer (Development)
- **Manager & Service Tests** - Core business logic validation
- **Code Generator Tests** - Roslyn-based generation testing
- **Configuration Tests** - SpocR configuration validation
- **Extension Method Tests** - Utility function testing

## Features

### Commands

```bash
# Execute all tests
spocr test

# Validate generated code only
spocr test --validate

# Run performance benchmarks
spocr test --benchmark

# Execute with rollback on failure
spocr test --rollback

# CI-friendly mode with reports
spocr test --ci --output junit.xml
```

### Test Types

#### 1. **Generated Code Validation**
- Syntax validation of generated C# classes
- Compilation testing with Roslyn
- Type safety verification
- Namespace and naming convention checks

#### 2. **Database Integration Tests**
- SQL Server schema validation using Testcontainers
- Stored procedure metadata accuracy
- Connection string validation
- Multi-database environment testing

#### 3. **Performance Benchmarks**
- Code generation speed measurements
- Memory usage profiling
- Database query performance
- Large schema handling tests

#### 4. **Snapshot Testing**
- Generated code snapshot comparisons
- Schema change detection
- Breaking change alerts
- Version compatibility testing

## Implementation Details

### Project Structure

```
src/
├── SpocR/                     # Main project
├── SpocR.Tests/               # Unit tests
│   ├── Managers/
│   ├── Services/
│   ├── CodeGenerators/
│   └── Extensions/
├── SpocR.IntegrationTests/    # Integration tests
│   ├── DatabaseTests/
│   ├── EndToEndTests/
│   └── PerformanceTests/
└── SpocR.TestFramework/       # Shared test infrastructure
    ├── Fixtures/
    ├── Helpers/
    └── Assertions/
```

### Dependencies

- **xUnit** - Primary test framework
- **FluentAssertions** - Enhanced assertion syntax
- **Testcontainers** - Docker-based SQL Server testing
- **Microsoft.Extensions.Testing** - Dependency injection in tests
- **Verify** - Snapshot testing for generated code
- **BenchmarkDotNet** - Performance benchmarking

### CI/CD Integration

#### GitHub Actions
```yaml
name: SpocR Test Suite
on: [push, pull_request]
jobs:
  test:
    strategy:
      matrix:
        os: [ubuntu-latest, windows-latest]
        dotnet: ['8.0', '9.0']
    runs-on: ${{ matrix.os }}
    services:
      sqlserver:
        image: mcr.microsoft.com/mssql/server:2022-latest
    steps:
      - name: Run Unit Tests
        run: dotnet test SpocR.Tests
      - name: Run Integration Tests  
        run: dotnet test SpocR.IntegrationTests
      - name: Run SpocR Self-Tests
        run: dotnet run --project SpocR -- test --ci
```

#### Azure DevOps
- Pipeline integration with `dotnet test`
- Test result publishing
- Code coverage reports
- Performance trend tracking

## Benefits

### For KI-Agents
- **Automatic Quality Assurance** - Immediate feedback on code changes
- **Self-Correcting Workflows** - Rollback mechanisms prevent broken states
- **Iterative Improvements** - Test-driven development cycles
- **Confidence in Changes** - Comprehensive coverage for safe refactoring

### For CI/CD Pipelines  
- **Native Integration** - Standard `dotnet test` compatibility
- **Parallel Execution** - Fast test execution with isolated environments
- **Detailed Reporting** - JUnit XML, coverage reports, trend analysis
- **Regression Detection** - Automatic detection of breaking changes

## Roadmap

### Phase 1: Foundation (v4.1)
- [x] Test project structure
- [x] Basic unit test framework
- [x] TestCommand implementation
- [ ] Core manager/service tests

### Phase 2: Integration (v4.2)  
- [ ] Testcontainers SQL Server setup
- [ ] End-to-end generation tests
- [ ] Schema validation tests
- [ ] Performance benchmarking

### Phase 3: Advanced Features (v4.3)
- [ ] Snapshot testing with Verify
- [ ] Self-validation framework
- [ ] CI/CD pipeline templates
- [ ] Advanced reporting

### Phase 4: KI-Agent Integration (v5.0)
- [ ] Automated rollback mechanisms
- [ ] Real-time validation feedback
- [ ] Adaptive test selection
- [ ] Machine learning insights

## Getting Started

### For Developers
```bash
# Clone and setup
git clone https://github.com/nuetzliches/spocr.git
cd spocr

# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### For CI/CD
```bash
# Integration with existing pipelines
dotnet tool install --global SpocR
spocr test --ci --output results.xml
```

### For KI-Agents
```bash
# Validate changes automatically
spocr test --validate --rollback
```

---

*The Testing Framework is designed to grow with SpocR's complexity while maintaining simplicity and reliability for all users - from individual developers to enterprise CI/CD systems and AI-driven development workflows.*