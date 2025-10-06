# SpocR Project AI Assistant Setup

This directory contains configuration and guidelines for AI agents working on the SpocR project.

## 📋 Files Overview

- **`guidelines.md`** - Comprehensive AI agent guidelines for code quality, standards, and workflows
- **`mcp-config.json`** - Model Context Protocol configuration (future)
- **`prompts/`** - Reusable prompt templates for common tasks

## 🤖 For AI Agents

When working on SpocR, always:

1. **Read `guidelines.md` first** - Understand project standards and requirements
2. **Run self-validation early**: `dotnet run --project src/SpocR.csproj -- test --validate`
3. **Use quality gates script** before proposing release changes
4. **Update docs & changelog** alongside behavioral changes
5. Prefer parsing `.artifacts/test-summary.json` (when using `--ci`) over console scraping

## 🔧 Quick Setup Verification

```bash
# .NET toolchain
dotnet --version   # Expect 8.x

# Build main project
dotnet build src/SpocR.csproj

# Run structural validation only
dotnet run --project src/SpocR.csproj -- test --validate

# Full test suite (uses tests solution)
dotnet test tests/Tests.sln
```

Quality gates (build + validate + tests + optional coverage):

```powershell
powershell -ExecutionPolicy Bypass -File eng/quality-gates.ps1 -SkipCoverage
```

Add coverage threshold (example 60):

```powershell
powershell -ExecutionPolicy Bypass -File eng/quality-gates.ps1 -CoverageThreshold 60
```

## 🧾 CI JSON Summary

In CI mode (`--ci` flag), a machine-readable summary is written to `.artifacts/test-summary.json` describing validation/test counts and success state. Use this for conditional workflow steps instead of regex parsing on logs.

## 🧪 Slow Tests

Reflection/version stability tests are marked with `[Trait("Category","Slow")]`. You can filter them if needed using xUnit traits.

## 📖 Key Resources

- [Contributing Guidelines](../CONTRIBUTING.md)
- [Testing Documentation](../tests/docs/TESTING.md) _(additions planned)_
- [Project README](../README.md)
- [AI Guidelines](./guidelines.md)

## 🛠 Docs Development

The documentation site uses Bun + Nuxt:

```bash
cd docs
bun install
bun run dev
```

## ⚙ Exit Codes

See overview in main `README.md` (Exit Codes section). Treat non-zero values per category; avoid hardcoding future subcodes in tests.

---

**Last Updated:** October 2, 2025  
**Version:** 1.1
