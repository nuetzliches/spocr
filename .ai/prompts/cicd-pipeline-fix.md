# CI/CD Pipeline Fix Prompt

Use this prompt when working on GitHub Actions workflows for SpocR.

## Current Issues Identified

- test.yml references old Testcontainers setup
- dotnet.yml tests wrong paths (src/SpocR.csproj instead of tests/)
- Workflows not aligned with new test structure (tests/Tests.sln)

## Prompt Template

```
I need to fix the SpocR CI/CD pipeline. Current issues:

1. **GitHub Actions Status**
   - test.yml workflow is outdated (references Testcontainers)
   - dotnet.yml has incorrect test paths
   - Need alignment with new test structure

2. **Required Verification Steps**
   - Check current .NET SDK versions in workflows
   - Verify test paths match new structure (tests/Tests.sln)
   - Ensure self-validation is included
   - Validate NuGet publishing workflow

3. **Expected Workflow Structure**
   - Build: `dotnet build src/SpocR.csproj`
   - Test: `dotnet test tests/Tests.sln`
   - Self-Validation: `dotnet run --project src/SpocR.csproj -- test --validate`
   - Package: Only on releases

4. **Quality Requirements**
   - Matrix testing on Ubuntu/Windows
   - .NET 8.0+ support
   - Proper caching for dependencies
   - Security scanning if possible

Please analyze the current workflows and provide fixes that align with SpocR standards.
```

## Key Files to Review

- `.github/workflows/test.yml`
- `.github/workflows/dotnet.yml`
- `tests/Tests.sln`
- `src/SpocR.csproj`
