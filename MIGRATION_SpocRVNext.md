# Migration to SpocRVNext (Draft)

> Status: Draft – references EPICs E001–E013 in `CHECKLIST.md`.

## Goals

- Transition from legacy DataContext generator to SpocRVNext
- Dual generation until v5.0
- Remove legacy code in v5.0 following cutover plan

## Phases

1. Freeze legacy (E001)
2. Core structure & dual generation (E003/E004)
3. Template engine & modernization (E005/E006/E007/E009)
4. Configuration cleanup & documentation (E008/E012)
5. Cutover & obsolete markings (E010/E011)
6. Test hardening & release preparation (E013 + release tasks)

## Configuration Changes

Removed (planned / already removed):

- `Project.Role.Kind`
- `Project.Role.DataBase.RuntimeConnectionStringIdentifier`
- `Project.Output`

## Namespace Resolution

Automatic derivation from project root & (if present) explicit overrides.
Fallback: Default namespace `SpocR.Generated` (tbd).

## Dual Generation

- Legacy output remains deterministic (no refactoring)
- New output generated in parallel and validated via snapshot diffs

## Tests

- Snapshot / golden master ensures functional parity
- Regression tests for removed heuristics
- Coverage target: >= 80% core components (template engine, parser, orchestrator)

## Risks & Mitigation

| Risk                        | Description                       | Mitigation                                |
| --------------------------- | --------------------------------- | ----------------------------------------- |
| Legacy vs. next divergence  | Undetected behavioral differences | Automated diff step in CI                 |
| Unclear migration           | Users confused about steps        | This guide + README / ROADMAP updates     |
| Template engine limitations | Missing features => workarounds   | Early scope definition + extension points |
| Namespace mis-resolution    | Wrong namespaces break builds     | Fallback + logging + tests                |

## Cutover (v5.0)

- Remove legacy generator & output
- Move stable next modules into `src/`
- Remove obsolete markers

## Open Points (To Refine)

- Final default namespace strategy
- Exact CLI flags for activation / force new output
- Parser for more complex template directives (if required)

---

Last updated: 2025-10-12
