# SpocR Sample Web API (Modern Mode)

This sample demonstrates how SpocR (v4 modern mode / implicit net10+) generates a strongly typed DataContext alongside a minimal manually coded context used only as a temporary bridge.

> Modern mode is implicitly enabled for `net10+` target frameworks. You do **not** need to set special flags – the generator infers defaults for namespace & folder structure when `Project.Output` is empty.

## Project Structure (Key Folders)

| Path | Purpose |
|------|---------|
| `DataContext/` | Generated types: Inputs, Models, TableTypes, StoredProcedure extension methods. |
| `ManualData/` | Temporary hand-written minimal db context (`SpocRDbContext`) – will be removed once the modern generated context ships. |
| `Output*` folders | Legacy template roots (kept for comparison / transition). `Output-modern` is a placeholder for future embedded templates. |
| `.spocr/` | Snapshot metadata (schema/result set fingerprints). |
| `spocr.json` | Minimal configuration (modern mode infers missing Output.* fields). |

`Program.cs` wires up the manual context today. Migration step (when available) will switch to the generated pipeline (`IAppDbContext` / pipe pattern).

## Getting Started

### 1. Configure SQL Server
Adjust the connection string in `spocr.json` (and optionally `appsettings.*.json`). A local dev SQL Server / container is fine. The sample expects a database containing the stored procedures referenced by the generated files (see names under `DataContext/StoredProcedures/*`).

### 2. Pull & Generate
You can run the generator from the repo root using the local source:

```bash
dotnet run --project src/SpocR.csproj --framework net10.0 -- pull   -p samples/web-api/spocr.json --no-auto-update --verbose
dotnet run --project src/SpocR.csproj --framework net10.0 -- build  -p samples/web-api/spocr.json --no-auto-update --verbose
```

After a successful `build` you will see updated / newly created C# artifacts in `DataContext/`.

### 3. Running the API
```bash
dotnet run
```
This still uses the manual context (`ManualData/`) for now.

### Optional: Local Helper Script (planned)
Once `eng/run-local-spocr.ps1` is added you will be able to call:
```powershell
pwsh eng/run-local-spocr.ps1 build samples/web-api/spocr.json
```
which wraps both build and framework selection.

1. Clone the repository:
   ```
   git clone <repository-url>
   ```

2. Navigate to the project directory:
   ```
   cd samples/web-api
   ```

3. Restore the dependencies:
   ```
   dotnet restore
   ```

4. Run the application:
   ```
   dotnet run
   ```

## Generated vs Manual Context

| Aspect | Manual (`ManualData/`) | Generated (`DataContext/`) |
|--------|------------------------|----------------------------|
| Lifetime | Scoped (DI configured) | Files only (execution context WIP) |
| Methods | `ExecuteScalarAsync`, `ExecuteNonQueryAsync` | Strongly typed `*Async` / `*DeserializeAsync` SP wrappers |
| Transactions | Basic `BeginTransactionAsync` wrapper | Planned integration with generated pipe/context |
| JSON support | Manual (consumer responsibility) | Generated: raw JSON + typed deserialization |

The manual context is a temporary bridge – do not build new features atop it; prefer the generated SP extension layer once the execution context lands.

## Modern Mode Behavior

Modern mode (net10+):
1. Auto-infers root namespace from the project (`WebApi`) unless explicitly set in `spocr.json`.
2. Fills missing `Project.Output.*` fields (namespace & subpaths) if omitted.
3. Ignores deprecated `project.dataBase.runtimeConnectionStringIdentifier` (uses `DefaultConnection`).
4. Templates for v10+ are sourced via the embedded template engine (`src/CodeGenerators/Templates/ITemplateEngine.cs`). The legacy `Output-*` folders are used only as a fallback during transition.

## Deprecations Highlighted Here

| Item | Status | Replacement |
|------|--------|-------------|
| `project.role.kind` | Deprecated (will be removed in v5) | Remove node (default behavior is implicit) |
| `runtimeConnectionStringIdentifier` (modern mode) | Ignored | Provide connection string directly; future: DI options |
| Explicit `output` block (when only namespace missing) | Optional | Omit to use inference |

See root `README.md` / `CHANGELOG.md` for full deprecation notes.

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| `Template source directory ... Output-modern` warning | Placeholder modern template folder not present | Safe to ignore – dynamic stubs used |
| Empty / missing generated methods | Procedures not discoverable or snapshot outdated | Re-run `pull` (maybe with `--no-cache`) |
| JSON model empty | Inference could not map columns | Check procedure FOR JSON shape / add stable column aliases |
| Role warnings in console | `role.kind` still in config | Remove the `role` section if not needed |

## Roadmap (Excerpt)
Short-term: Replace stubs with fully generated modern execution context + DI registration helpers.

## Original Minimal API Notes

Legacy minimal API description retained below for context:

## Contributing

Contributions welcome: open issues for modern template gaps or migration feedback.
