# AI Agent Guidelines for SpocR

These guardrails mirror the `feature/vnext-only` working agreement. Follow them whenever producing code, docs, or planning updates for SpocR.

## 1. Intake & Planning

- **Sync the checklists.** Update `CHECKLIST.md`, `src/SpocRVNext/CHECKLIST.md`, and `src/SpocRVNext/SnapshotBuilder/CHECKLIST.md` before changing code. Keep status markers (`[ ]`, `[x]`, `[>]`, `[~]`, `[?]`, `[!]`) consistent across all three files.
- **Review the workflow instructions.** `.github/instructions/spocr-v5-instructions.instructions.md` defines branch scope, validation, and documentation guardrails. Re-read it whenever the process changes.
- **Capture scope drift.** Log design gaps, missing tests, and follow-ups under the `Review-Findings` item in the root checklist as soon as you discover them.
- **Plan in English.** Write checklist updates, design notes, commit messages, docs, and comments in clear English only.

## 2. Implementation Guardrails

- **Respect branch scope.** Deliver vNext-only improvements; avoid reviving legacy features unless the roadmap checklist explicitly calls for them.
- **Mirror roadmap and SnapshotBuilder status.** When DbContext, SnapshotBuilder, or artifact flows change, update the matching items in every checklist.
- **Document CLI or pipeline changes immediately.** Adjust docs, changelog entries, and checklist notes in the same PR that alters commands, telemetry, or exit codes.
- **Prefer additive diagnostics.** Use existing verbosity switches (`--verbose`, `Verbose(...)`) for temporary tracing. Remove one-off logging before merge unless the checklist tracks a follow-up task.

## 3. Validation Matrix

```cmd
:: Refresh schema cache when generator or parser logic changes
dotnet run --project src\SpocR.csproj -- pull -p debug\spocr.json --no-cache --verbose

:: Run structural validation pass (no tests)
dotnet run --project src\SpocR.csproj -- test --validate

:: Execute full tests (solution-level)
dotnet test tests\Tests.sln

:: Ensure build stays green
dotnet build src\SpocR.csproj
```

```powershell
# Aggregate gate (fails fast on build/test issues)
powershell -ExecutionPolicy Bypass -File eng/quality-gates.ps1

# Coverage-enforced variant
powershell -ExecutionPolicy Bypass -File eng/quality-gates.ps1 -CoverageThreshold 60
```

- Refresh golden snapshots (`write-golden`, `verify-golden`) whenever generator output changes and record the outcome in the checklists.
- Prefer `.artifacts/test-summary.json` (produced by `--ci`) for machine-readable validation results instead of scraping console logs.
- Mark slow-running tests with `[Trait("Category","Slow")]` to keep the validation surface predictable.

## 4. Language & Style Expectations

- Translate any remaining German (or non-English) comments, docstrings, or prompts to concise English in the same commit and remove the previous text.
- Follow `.editorconfig` conventions for naming, spacing, and file headers. Enable nullable reference types (`#nullable enable`) in new files.
- Add comments only when they clarify intent, invariants, or non-obvious decisions.

```csharp
#nullable enable

/// <summary>
/// Generates strongly typed wrappers for SQL Server stored procedures.
/// </summary>
public sealed class ProcedureGenerator
{
    // Implementation elided for brevity.
}
```

## 5. Testing & Quality Gates

- Cover new behavior with unit and/or integration tests using the `Method_Scenario_ExpectedResult` naming pattern.
- Treat warnings as failures; resolve analyzer feedback instead of suppressing it.
- Ensure `dotnet run --project src/SpocR.csproj -- test --validate` passes before finalizing work.
- Keep `.artifacts/test-summary.json` schema updates synchronized with documentation and consumers if the format changes.

## 6. Documentation & Prompts

- Update `docs/content/` alongside code so CLI flags, configuration options, and migration guidance stay current.
- Docs run on Bun + Nuxt:

```bash
cd docs
bun install
bun run dev
```

- When editing `.ai/prompts`, align language with these guidelines and link to the guardrails or checklists as needed.

## 7. Dependency & Version Hygiene

- Use explicit package versions; avoid unconstrained wildcards (`*`). Note removed dependencies in both the checklist and `CHANGELOG.md`.
- MinVer controls assembly versions. Do not add `<Version>` properties to project files. Tag releases with `v<MAJOR>.<MINOR>.<PATCH>` when shipping.

## 8. Snapshot & Cache Expectations

- Keep `.spocr/` ephemeral. Cache files are keyed by database fingerprint; corruption should degrade gracefully (treat as cache miss).
- Do not persist schema status in snapshots. New enrichment stages must be deterministic and idempotent when replayed from cached artifacts.
- If fingerprint semantics change, update the roadmap checklist and amend `README-dot-spocr.md` with the new structure.

## 9. Exit Codes (Reference)

| Code | Category      | Description                               |
| ---- | ------------- | ----------------------------------------- |
| 0    | Success       | Operation completed without issues        |
| 10   | Validation    | Structural or semantic validation failure |
| 20   | Generation    | Code generation pipeline error (reserved) |
| 30   | Dependency    | External dependency failure (reserved)    |
| 40   | Testing       | Test suite failure                        |
| 50   | Benchmark     | Benchmark harness failure (reserved)      |
| 60   | Rollback      | Recovery routine failed (reserved)        |
| 70   | Configuration | Configuration parsing or validation error |
| 80   | Internal      | Unhandled or unknown failure              |
| 99   | Reserved      | Future experimental use                   |

Do not codify additional subcodes until the roadmap documents them.

---

**Last Updated:** November 5, 2025  
**Guideline Version:** 2.0  
**Applies to:** SpocR vNext branch
