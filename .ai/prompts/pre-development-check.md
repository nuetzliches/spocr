# Pre-Development Package Verification Prompt

Use this prompt before making any code changes to ensure the environment is current and compliant.

## Prompt Template

```
Before implementing any code changes for SpocR, please perform the following verification steps:

1. **Package Analysis**
   - Check all PackageReference entries in .csproj files for latest compatible versions
   - Verify no security vulnerabilities in current dependencies
   - Ensure target frameworks (.NET 8.0/9.0) are appropriate and current

2. **Style Guide Verification**
   - Look for .editorconfig and apply formatting rules
   - Review CONTRIBUTING.md for project-specific standards
   - Check existing code patterns for consistency

3. **Development Environment Check**
   - Verify dotnet SDK version compatibility
   - Ensure all required tooling is available and current
   - Check for any new analyzers or quality tools

4. **Documentation Review**
   - Read .ai/guidelines.md for AI agent specific requirements
   - Review any relevant documentation for the component being modified
   - Check for any recent changes in project standards

5. **Quality Gate Preparation**
   - Plan to run `dotnet run --project src/SpocR.csproj -- test --validate`
   - Ensure `dotnet test tests/Tests.sln` will pass
   - Prepare to update documentation if needed

Only after completing these steps, proceed with the implementation.
```

## Usage Example

```
I need to add a new feature for SQL parameter validation. Before I start coding, let me verify the current environment:

[Run through the verification steps above]

Now I can proceed with implementing the feature following SpocR standards.
```
