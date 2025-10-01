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

### Test Structure

```
src/
├── SpocR.Tests/               # Unit tests
├── SpocR.IntegrationTests/    # Integration tests with SQL Server
├── SpocR.TestFramework/       # Shared test infrastructure
└── SpocR/Commands/Test/       # Test command implementation
```

## Features

### ✅ **Unit Testing**
- Manager and Service class tests
- Extension method validation
- Configuration testing
- Dependency injection validation

### 🔗 **Integration Testing**
- SQL Server testing with Testcontainers
- Database schema validation
- End-to-end code generation testing
- Multi-database environment support

### 🔍 **Self-Validation Framework**
- Generated C# code syntax validation
- Compilation checking with Roslyn
- Code quality analysis
- Breaking change detection

### 📊 **CI/CD Integration**
- GitHub Actions workflows
- Test result reporting (JUnit XML)
- Code coverage analysis
- Performance benchmarking

## Architecture

### Multi-Layer Testing
```
🔄 Self-Validation (KI-Agent)
├── Generated Code Validation
├── Syntax & Compilation Tests
└── Regression Detection

🧪 Integration Tests (CI/CD)
├── Database Schema Tests
├── End-to-End Scenarios
└── Performance Benchmarks

🏗️ Unit Tests (Development)
├── Manager & Service Tests
├── Code Generator Tests
└── Configuration Tests
```

### Test Framework Components

- **SpocrTestBase** - Base class for all tests with DI setup
- **SqlServerFixture** - Testcontainers-based SQL Server testing
- **GeneratedCodeValidator** - Roslyn-based code validation
- **TestCommand** - CLI integration for automated testing

## Development Workflow

### For Developers
```bash
# Run unit tests during development
dotnet test src/SpocR.Tests

# Run integration tests
dotnet test src/SpocR.IntegrationTests

# Validate changes with SpocR
spocr test --validate
```

### For KI-Agents
```bash
# Automated validation with rollback
spocr test --validate --rollback

# Full test suite with CI output
spocr test --ci --output results.xml
```

### For CI/CD Pipelines
```yaml
# GitHub Actions Integration
- name: Run SpocR Tests
  run: |
    dotnet test src/SpocR.Tests
    dotnet test src/SpocR.IntegrationTests
    dotnet run --project src/SpocR -- test --ci
```

## Test Categories

### Unit Tests (`SpocR.Tests`)
- **Services**: ConsoleService, OutputService validation
- **Managers**: SpocrConfigManager, SchemaManager testing
- **Extensions**: StringExtensions, utility methods
- **Commands**: Command execution and validation

### Integration Tests (`SpocR.IntegrationTests`)
- **Database**: SQL Server connection and query testing
- **Generation**: End-to-end code generation workflows
- **Performance**: Benchmarking and optimization

### Self-Validation Tests
- **Code Quality**: Generated code syntax and compilation
- **Schema Validation**: Database metadata consistency
- **Regression**: Breaking change detection

## Configuration

### Test Settings
```json
{
  "TestMode": true,
  "Environment": "Test",
  "ConnectionString": "Data Source=localhost;Initial Catalog=TestDb;Integrated Security=true"
}
```

### Test Dependencies
- **xUnit** - Primary test framework
- **FluentAssertions** - Enhanced assertions
- **Testcontainers** - Docker-based SQL Server testing
- **Moq** - Mocking framework
- **BenchmarkDotNet** - Performance benchmarking

## Benefits

### 🤖 **For KI-Agents**
- **Automatic Quality Assurance** - Immediate feedback on code changes
- **Self-Correcting Workflows** - Rollback mechanisms prevent broken states
- **Iterative Improvements** - Test-driven development cycles
- **Confidence in Changes** - Comprehensive coverage for safe refactoring

### 🚀 **For CI/CD Pipelines**
- **Native Integration** - Standard `dotnet test` compatibility
- **Parallel Execution** - Fast test execution with isolated environments
- **Detailed Reporting** - JUnit XML, coverage reports, trend analysis
- **Regression Detection** - Automatic detection of breaking changes

### 👨‍💻 **For Developers**
- **Fast Feedback** - Quick validation during development
- **Easy Setup** - Minimal configuration required
- **Comprehensive Coverage** - Unit, integration, and validation tests
- **Debugging Support** - Detailed error messages and stack traces

## Roadmap

### ✅ **Phase 1: Foundation (v4.1)**
- [x] Test project structure
- [x] Basic unit test framework
- [x] TestCommand implementation
- [x] Core manager/service tests

### 🚧 **Phase 2: Integration (v4.2)**
- [x] Testcontainers SQL Server setup
- [x] End-to-end generation tests
- [x] Schema validation tests
- [ ] Performance benchmarking

### 🔮 **Phase 3: Advanced Features (v4.3)**
- [ ] Snapshot testing with Verify
- [x] Self-validation framework
- [x] CI/CD pipeline templates
- [ ] Advanced reporting

### 🤖 **Phase 4: KI-Agent Integration (v5.0)**
- [ ] Automated rollback mechanisms
- [ ] Real-time validation feedback
- [ ] Adaptive test selection
- [ ] Machine learning insights

## Contributing

The testing framework is designed to be extensible and maintainable:

1. **Add new tests**: Follow existing patterns in `SpocR.Tests`
2. **Extend validation**: Implement new validators in `SpocR.TestFramework`
3. **Improve CI/CD**: Enhance workflows in `.github/workflows`
4. **Document changes**: Update this README and roadmap documentation

---

*The SpocR Testing Framework ensures reliable, automated testing for all users - from individual developers to enterprise CI/CD systems and AI-driven development workflows.*