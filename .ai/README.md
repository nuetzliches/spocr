# SpocR Project AI Assistant Setup

This directory contains configuration and guidelines for AI agents working on the SpocR project.

## 📋 Files Overview

- **`guidelines.md`** - Comprehensive AI agent guidelines for code quality, standards, and workflows
- **`mcp-config.json`** - Model Context Protocol configuration (future)
- **`prompts/`** - Reusable prompt templates for common tasks

## 🤖 For AI Agents

When working on SpocR, always:

1. **Read `guidelines.md` first** - Understand project standards and requirements
2. **Verify package versions** - Check for updates before making changes
3. **Follow quality gates** - Run validation before committing changes
4. **Update documentation** - Keep docs in sync with code changes

## 🔧 Quick Setup Verification

```bash
# Verify environment is ready
dotnet --version  # Should be 8.0+
dotnet build src/SpocR.csproj
dotnet test tests/Tests.sln
dotnet run --project src/SpocR.csproj -- test --validate
```

## 📖 Key Resources

- [Contributing Guidelines](../CONTRIBUTING.md)
- [Testing Documentation](../tests/docs/TESTING.md)
- [Project README](../README.md)
