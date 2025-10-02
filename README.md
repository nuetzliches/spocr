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

SpocR uses a layered quality strategy:

| Layer             | Purpose                                     | Command / Entry Point                          |
| ----------------- | ------------------------------------------- | ---------------------------------------------- |
| Self-Validation   | Static / structure validation (Roslyn)      | `spocr test --validate` or `dotnet run -- ...` |
| Unit Tests        | Logic, helpers, generators, extensions      | `dotnet test tests/SpocR.Tests`                |
| Integration (WIP) | DB & end-to-end stored procedure roundtrips | `dotnet test tests/SpocR.IntegrationTests`     |

Quick pre-commit check:

```bash
spocr test --validate
```

Full local quality gates (build + validate + tests + coverage):

```powershell
powershell -ExecutionPolicy Bypass -File eng/quality-gates.ps1 -CoverageThreshold 60
```

Artifacts (test results & coverage) are written to the hidden `.artifacts/` directory and excluded from version control.

See `tests/docs/TESTING.md` (future) for extended strategy details.

## 🚢 Release & Publishing

Releases are published automatically to NuGet when a GitHub Release is created with a tag matching the pattern:

```
v<semantic-version>
```

Example: `v4.1.36` will publish package version `4.1.36` if not already present on NuGet.

Key safeguards:

- Tag/version match validation
- Skip if version already published
- Deterministic build flags (`ContinuousIntegrationBuild=true`, `Deterministic=true`)
- SBOM generation (CycloneDX) uploaded as artifact

### Dry Run (Manual Test of Pipeline)

You can test the release workflow without publishing:

1. GitHub → Actions → `Publish NuGet`
2. Run workflow (leave `dry-run=true`)
3. (Optional) Set `override-version` (e.g. `9.9.9-local`) to simulate a different output

The workflow builds, validates and tests but skips the publish step.

### Local Pre-Release Validation

```powershell
powershell -ExecutionPolicy Bypass -File eng/quality-gates.ps1 -SkipCoverage
```

Then create a tag & release once green:

```bash
git tag v4.1.36
git push origin v4.1.36
```

### Versioning

Semantic versions are derived from Git tags using [MinVer](https://github.com/adamralph/minver).

Tag format:

```
v<MAJOR>.<MINOR>.<PATCH>
```

Examples:

| Git Tag   | NuGet Package Version |
| --------- | --------------------- |
| `v4.1.36` | 4.1.36                |
| `v5.0.0`  | 5.0.0                 |

If you create a pre-release tag (e.g. `v4.2.0-alpha.1`), that version flows into the package.

Workflow:

1. Ensure tests & validation are green (`eng/quality-gates.ps1`).
2. Decide version bump (SemVer): MAJOR (breaking), MINOR (features), PATCH (fixes).
3. Create & push tag: `git tag vX.Y.Z && git push origin vX.Y.Z`.
4. Draft GitHub Release using that tag (or let automation publish on tag if configured in future).

The project file no longer auto-increments version numbers; builds are reproducible from tags.

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

All code, comments, commit messages and documentation are authored in English (see Language Policy in `CONTRIBUTING.md`).

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
