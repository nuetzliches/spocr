# SpocR AI Contribution Hub

Resources in this directory keep AI agents aligned with the `feature/vnext-only` roadmap. Start here before editing code, docs, or planning artifacts.

## File Map

- `guidelines.md` — End-to-end standards (development flow, language policy, validation).
- `README-debug.md` — Focused walkthrough for local generator diagnostics.
- `README-dot-spocr.md` — Runtime cache and workspace notes for `.spocr/`.
- `prompts/` — Reusable prompt snippets; refresh them when workflows change.

## Working Agreements

1. Open the checklists first. Align `CHECKLIST.md`, `src/SpocRVNext/CHECKLIST.md`, and `src/SpocRVNext/SnapshotBuilder/CHECKLIST.md` with the work you plan to touch. Keep statuses synchronized and log blockers under `Review-Findings`.
2. Review the guardrails. `.github/instructions/spocr-v5-instructions.instructions.md` defines the branch workflow (scope, status markers, validation). Re-read whenever you change processes or automation.
3. Plan deliverables in English. All new or updated comments, docs, prompts, and checklist notes stay English-only.
4. Document behavioral changes immediately. Update docs, changelog entries, and checklist follow-ups in the same PR that moves the code.
5. Record roadmap implications. Any SnapshotBuilder or DbContext adjustments must be echoed in the vNext roadmap checklist.

## Core Validation Loop

```cmd
:: Generator-affecting work (schema pull refresh)
dotnet run --project src\SpocR.csproj -- pull -p debug\spocr.json --no-cache --verbose

:: Structural validation mode
dotnet run --project src\SpocR.csproj -- test --validate

:: Full suite (tests solution)
dotnet test tests\Tests.sln

:: Build gate
dotnet build src\SpocR.csproj
```

```powershell
# Quality gates (toggle coverage as needed)
powershell -ExecutionPolicy Bypass -File eng/quality-gates.ps1
# Or enforce minimum coverage
powershell -ExecutionPolicy Bypass -File eng/quality-gates.ps1 -CoverageThreshold 60
```

After generator output changes, refresh golden assets via the snapshot scripts referenced in the guardrails (`write-golden`, `verify-golden`). Capture results in the relevant checklist rows.

## Artifacts & Telemetry

- Prefer `.artifacts/test-summary.json` (produced with `--ci`) for machine-readable validation outcomes.
- Keep `debug/` assets tidy; regenerate via `README-debug.md` commands when comparing outputs.
- Note removed dependencies or major CLI behavior shifts in both the checklist and `CHANGELOG.md`.

## Docs & Prompts

- Docs run on Bun + Nuxt: `cd docs && bun install && bun run dev`.
- Sync any `.ai/prompt` edits with updated workflow language; link back to the guardrails when helpful.

---

**Last Updated:** November 5, 2025  
**Maintainer:** vNext AI workflow
