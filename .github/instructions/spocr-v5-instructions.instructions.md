---
applyTo: "**"
---

# SpocR CLI Checklist Guardrails

These instructions support the current CLI work on the `feature/vnext-only` branch. Follow this flow before pushing code or docs. It keeps `CHECKLIST.md`, `src/SpocRVNext/CHECKLIST.md`, and `src/SpocRVNext/SnapshotBuilder/CHECKLIST.md` aligned.

## 1. Before starting work

- Review all three checklists and mark the items you intend to touch. Ensure cross references stay in sync.
- Skim `.ai/README.md` and prompts if you plan to rely on tooling; refresh them when the guidance changes.
- Confirm that any planned work respects the branch scope: focus on the modern CLI surface, avoid reviving legacy features.

## 2. While implementing changes

- Keep status conventions: `[ ]`, `[x]`, `[>]`, `[~]`, `[?]`, `[!]`. Do not invent new markers.
- When you add or close work that spans multiple sections, update every affected checklist in the same PR.
- For SnapshotBuilder or DbContext updates, ensure the roadmap checklist reflects the same completion state.
- Capture findings (design gaps, missing tests, quality debt) under the "Review-Findings" item in the root checklist.
- If you touch CLI or pipeline behavior, document the change immediately in the docs section tasks.
- Describe the CLI as it exists today. Drop the "vNext" label and avoid reintroducing historical bridge narratives.
- Run generators against the sandbox under `debug/` (e.g. `dotnet run --project src/SpocR.csproj -- rebuild -p debug ...`). Do not revive `DataContext/` output paths outside the sandbox.

## 3. After editing functionality or docs

- Re-read the root checklist and adjust statuses, add follow-up bullets, or link to new documents.
- Update the roadmap checklist with structural or architectural decisions.
- Update the SnapshotBuilder checklist if determinism, telemetry, or artifact format changes.
- Re-run the `.ai` review item: sync guidelines, prompts, or README to match the new flow.
- Note removed dependencies (Roslyn, McMaster, WebApi Client, CodeAnalysis) in both the checklist and changelog.
- Keep `docs/content` focused on the current CLI (v5) behavior. Move historical or migration notes to the legacy documentation stream instead of the primary site.

## 4. Validation requirements

- Run `dotnet run --project src/SpocR.csproj -- pull` after generator-affecting work.
- Execute `eng/quality-gates.ps1` (with coverage if touched) and record outcomes when updating quality tasks.
- Refresh golden snapshots (`write-golden`, `verify-golden`) whenever SnapshotBuilder output changes.
- Ensure migration instructions (`migration-v5.instructions`) stay current with any step you add.

## 5. Pre-merge hygiene

- The branch-specific `CHECKLIST.md` must be removed or archived before merging to `master`.
- If a checklist item stays open, add a short rationale (one sentence) so the next iteration knows the blocker.
- Confirm `.github/instructions/spocr-v5-instructions.instructions.md` still matches the workflow; update if process changes.

Following these guardrails keeps the checklists actionable and prevents drift between planning and implementation.
