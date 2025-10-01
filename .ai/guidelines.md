# AI Agent Guidelines for SpocR

This document provides standardized guidelines for AI agents working on the SpocR project to ensure consistency, quality, and adherence to project standards.

## 🔍 Pre-Development Checklist

### Package & Environment Verification

- [ ] **Check latest package versions** - Verify all Packages are up-to-date
- [ ] **Validate .NET SDK version** - Ensure target frameworks are current and supported
- [ ] **Review dependency compatibility** - Check for version conflicts and security vulnerabilities
- [ ] **Verify tooling versions** - Ensure MSBuild, analyzers, and development tools are current

### Style & Standards Verification

- [ ] **EditorConfig compliance** - Check for `.editorconfig` and follow defined styles
- [ ] **Coding standards** - Reference and apply project-specific coding guidelines
- [ ] **Naming conventions** - Follow established C# and project naming patterns
- [ ] **Documentation standards** - Ensure XML documentation and README compliance

## 🛠️ Development Standards

### Code Quality Requirements

```csharp
// ✅ Good: Nullable reference types enabled
#nullable enable

// ✅ Good: Proper XML documentation
/// <summary>
/// Generates strongly typed C# classes for SQL Server stored procedures
/// </summary>
/// <param name="connectionString">Database connection string</param>
/// <returns>Generated code result</returns>
public async Task<GenerationResult> GenerateAsync(string connectionString)
{
    // Implementation
}

// ❌ Bad: No documentation, unclear naming
public async Task<object> DoStuff(string cs)
{
    // Implementation
}
```

### Testing Requirements

- [ ] **Unit tests** - All new functionality must have corresponding unit tests
- [ ] **Integration tests** - Database-related code requires integration test coverage
- [ ] **Test naming** - Use descriptive test method names: `Method_Scenario_ExpectedResult`
- [ ] **Self-validation** - Run `dotnet run --project src/SpocR.csproj -- test --validate` before commits

### Build & CI Compliance

- [ ] **Local build success** - `dotnet build src/SpocR.csproj` must pass
- [ ] **All tests pass** - `dotnet test tests/Tests.sln` must be green
- [ ] **No warnings** - Treat warnings as errors in development
- [ ] **Clean code analysis** - Address all analyzer suggestions

## 📦 Package Management

### NuGet Package Guidelines

```xml
<!-- ✅ Good: Explicit version ranges for stability -->
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.*" />

<!-- ✅ Good: Security-focused package selection -->
<PackageReference Include="Microsoft.Data.SqlClient" Version="5.1.*" />

<!-- ❌ Bad: Wildcard versions that can break builds -->
<PackageReference Include="SomePackage" Version="*" />
```

### Version Management

- **Patch versions** - Auto-incremented by MSBuild target
- **Minor versions** - Manually updated for feature additions
- **Major versions** - Breaking changes, requires RFC discussion

## 🔧 Architecture Guidelines

### Dependency Injection Pattern

```csharp
// ✅ Good: Constructor injection with interface
public class CodeGenerator
{
    private readonly IDbContextFactory _contextFactory;
    private readonly ILogger<CodeGenerator> _logger;

    public CodeGenerator(IDbContextFactory contextFactory, ILogger<CodeGenerator> logger)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
}
```

### Error Handling Standards

```csharp
// ✅ Good: Specific exceptions with context
throw new InvalidOperationException($"Unable to connect to database: {connectionString}");

// ✅ Good: Proper async error handling
try
{
    await ProcessAsync();
}
catch (SqlException ex) when (ex.Number == 2) // Timeout
{
    _logger.LogWarning("Database timeout occurred, retrying...");
    throw new TimeoutException("Database operation timed out", ex);
}
```

## 📝 Documentation Standards

### Code Documentation

- **Public APIs** - Must have XML documentation
- **Complex logic** - Inline comments explaining business rules
- **Configuration** - Document all configuration options
- **Examples** - Provide usage examples for new features

### Commit Message Format

```
feat: add stored procedure parameter validation
fix: resolve connection timeout in SqlDbHelper
docs: update README with new CLI commands
test: add integration tests for schema generation
chore: update NuGet packages to latest versions
```

## 🚦 Quality Gates

### Before Code Changes

1. **Research phase** - Understand existing patterns and conventions
2. **Planning phase** - Document approach and breaking changes
3. **Implementation phase** - Follow established patterns
4. **Validation phase** - Test thoroughly and validate against standards

### Before Commits

1. **Self-validation** - `dotnet run --project src/SpocR.csproj -- test --validate`
2. **Test execution** - `dotnet test tests/Tests.sln`
3. **Build verification** - `dotnet build src/SpocR.csproj`
4. **Documentation update** - Update relevant documentation

## 🔗 Resources

### Essential Documentation

- [CONTRIBUTING.md](../CONTRIBUTING.md) - Development setup and contribution guidelines
- [tests/docs/TESTING.md](../tests/docs/TESTING.md) - Testing framework documentation
- [.editorconfig](../.editorconfig) - Code formatting rules (if exists)

### External Standards

- [Microsoft C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- [.NET API Design Guidelines](https://docs.microsoft.com/en-us/dotnet/standard/design-guidelines/)
- [NuGet Package Guidelines](https://docs.microsoft.com/en-us/nuget/create-packages/package-authoring-best-practices)

---

**Last Updated:** October 1, 2025  
**Version:** 1.0  
**Applies to:** SpocR v4.1.x and later
