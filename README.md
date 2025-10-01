# SpocR

[![NuGet](https://img.shields.io/nuget/v/SpocR.svg)](https://www.nuget.org/packages/SpocR)
[![NuGet Downloads](https://img.shields.io/nuget/dt/SpocR.svg)](https://www.nuget.org/packages/SpocR)
[![License](https://img.shields.io/github/license/nuetzliches/spocr.svg)](LICENSE)
[![Build Status](https://img.shields.io/github/actions/workflow/status/nuetzliches/spocr/test.yml?branch=main)](https://github.com/nuetzliches/spocr/actions)
[![Code Coverage](https://img.shields.io/badge/coverage-check%20actions-blue)](https://github.com/nuetzliches/spocr/actions)

**SpocR** is a powerful code generator for SQL Server stored procedures that creates strongly typed C# classes for inputs, outputs, and execution. Eliminate boilerplate data access code and increase type safety in your .NET applications.

## ✨ Features

- **🛡️ Type Safety**: Generate strongly typed C# classes that catch errors at compile time
- **⚡ Zero Boilerplate**: Eliminate manual mapping code and data access layers
- **🚀 Fast Integration**: Integrate into existing .NET solutions within minutes
- **🔧 Extensible**: Customize naming conventions, output structure, and generation behavior
- **📊 JSON Support**: Handle complex JSON return types with optional deserialization strategies
- **🏗️ CI/CD Ready**: Seamlessly integrate into build pipelines and automated workflows

## 🚀 Quick Start

### Installation

Install SpocR as a global .NET tool:

```bash
dotnet tool install --global SpocR
```

### Basic Usage

```bash
# Initialize project
spocr create --project MyProject

# Connect to database and pull stored procedures
spocr pull --connection "Server=.;Database=AppDb;Trusted_Connection=True;"

# Generate strongly typed C# classes
spocr build
```

### Example Generated Code

**Before SpocR** (manual, error-prone):

```csharp
var command = new SqlCommand("EXEC GetUserById", connection);
command.Parameters.AddWithValue("@UserId", 123);
var reader = await command.ExecuteReaderAsync();
// ... manual mapping code
```

**With SpocR** (generated, type-safe):

```csharp
var context = new GeneratedDbContext(connectionString);
var result = await context.GetUserByIdAsync(new GetUserByIdInput {
    UserId = 123
});
```

## 📖 Documentation

For comprehensive documentation, examples, and advanced configuration:

**[📚 Visit the SpocR Documentation](https://nuetzliches.github.io/spocr/)**

## ✅ Testing & Quality

SpocR enthält einen mehrschichtigen Qualitäts-/Test-Ansatz:

| Layer                 | Zweck                                   | Aufruf                                     |
| --------------------- | --------------------------------------- | ------------------------------------------ |
| Self-Validation       | Syntax & Generator Validierung (Roslyn) | `spocr test --validate`                    |
| Unit Tests            | Logik / Services / Extensions           | `dotnet test tests/SpocR.Tests`            |
| (geplant) Integration | DB & End-to-End Flows                   | `dotnet test tests/SpocR.IntegrationTests` |

Schneller Vor-Commit Check:

```bash
spocr test --validate
```

Details & Roadmap siehe `tests/docs/TESTING.md`.

## 🛠️ Requirements

- .NET SDK 6.0 or higher (8.0+ recommended)
- SQL Server (2016 or later)
- Access to SQL Server instance for metadata extraction

## 🎯 Use Cases

- **Enterprise Applications**: Reduce data access layer complexity
- **API Development**: Generate type-safe database interactions
- **Legacy Modernization**: Safely wrap existing stored procedures
- **DevOps Integration**: Automate code generation in CI/CD pipelines

## 📦 Installation Options

### Global Tool (Recommended)

```bash
dotnet tool install --global SpocR
```

### Project-local Tool

```bash
dotnet new tool-manifest
dotnet tool install SpocR
dotnet tool run spocr --version
```

### Package Reference

```xml
<PackageReference Include="SpocR" Version="4.1.*" />
```

## 🔧 Configuration

SpocR uses a `spocr.json` configuration file to customize generation behavior:

```json
{
  "project": {
    "name": "MyProject",
    "connectionString": "Server=.;Database=AppDb;Trusted_Connection=True;",
    "output": {
      "directory": "./Generated",
      "namespace": "MyProject.Data"
    }
  }
}
```

## 🤝 Contributing

We welcome contributions! A lightweight contributor guide is available in `CONTRIBUTING.md` (Root).

Engineering infrastructure lives under `eng/` (e.g., `eng/quality-gates.ps1`). Transient test & coverage artifacts are written to the hidden directory `.artifacts/` to keep the repository root clean.

- 🐛 **Bug Reports**: [Create an issue](https://github.com/nuetzliches/spocr/issues/new?template=bug_report.md)
- 💡 **Feature Requests**: [Create an issue](https://github.com/nuetzliches/spocr/issues/new?template=feature_request.md)
- 🔧 **Pull Requests**: See `CONTRIBUTING.md`
- 🤖 **AI Agents**: See `.ai/guidelines.md` for automated contribution standards

## 📝 License

This project is licensed under the [MIT License](LICENSE).

## 🙏 Acknowledgments

- Built with [Roslyn](https://github.com/dotnet/roslyn) for C# code generation
- Inspired by modern ORM and code generation tools
- Community feedback and contributions

---

**[Get Started →](https://nuetzliches.github.io/spocr/getting-started/installation)** | **[Documentation →](https://nuetzliches.github.io/spocr/)** | **[Examples →](samples/)**
