title: Roadmap (v5)
description: Focus areas and planned work for the environment-first SpocR CLI.
---

# SpocR Roadmap (v5)

This section highlights the roadmap for the modern v5 CLI. It aligns with branch checklists, documenting how we phase out legacy `spocr.json`/`DataContext/` outputs while hardening the environment-first toolchain.

## Current Focus Areas

- Environment-first configuration (`.env`/`SPOCR_*`) rollout and legacy artefact warnings.
- SnapshotBuilder determinism, JSON typing coverage, and diagnostics improvements.
- Output strategy evolution (streaming, dual JSON mode, nested models).
- Quality gates (coverage, golden manifests, strict diff enforcement).
- Developer experience across CLI, samples, and documentation.

## Roadmap Sections

- [Testing Framework](/roadmap/testing-framework) - Comprehensive testing infrastructure for automated validation
- [JSON Procedure Models](/roadmap/json-procedure-models) - Next-generation JSON handling
- [Output Strategies](/roadmap/output-strategies) - Flexible data serialization approaches
- [Development Tasks](/roadmap/development-tasks) - Current development priorities
- [Optional Features](/roadmap/optional-features) - Configurable functionality enhancements
- [Removed Heuristics](/roadmap/removed-heuristics-v5) - Determinism clean-up log
- [API Changes](/roadmap/api-changes-v5) - Runtime surface adjustments for consumers
- [Migration Guide](/roadmap/migration-v5) - Cutover checklist toward the v5 steady state

## Consolidated Planned Features (High-Level)

| Category          | Feature                                    | Status   | Notes                                                                                       |
| ----------------- | ------------------------------------------ | -------- | ------------------------------------------------------------------------------------------- |
| Testing           | CI mode JSON + per-suite stats             | Done     | `test-summary.json` with nested suite metrics, durations, failures                          |
| Testing           | TRX robust parsing & retries               | Done     | Sequential orchestration + retry loop                                                       |
| Testing           | Granular exit sub-codes (41/42/43)         | Done     | Unit / Integration / Validation failure precedence                                          |
| Testing           | Console failure summary                    | Done     | Top failing tests (<=10) printed                                                            |
| Testing           | Single-suite JUnit XML export              | Done     | `--junit` flag outputs aggregate suite                                                      |
| Testing           | JUnit multi-suite reporting                | Planned  | Awaiting CI pipeline refactor; tracked in the Testing Framework doc                         |
| Testing           | Benchmark integration (`--benchmark`)      | Deferred | Blocked until deterministic baselines finalized (see Quality checklist)                     |
| Testing           | Rollback mechanism (`--rollback`)          | Planned  | Requires snapshot + transactional file operations                                           |
| CLI               | Exit code specialization (spaced blocks)   | Done     | Spaced categories + sub-codes implemented                                                   |
| CLI               | `.env` bootstrap & legacy warnings          | Done     | `spocr init` template refresh, legacy artefact detection                                    |
| Versioning        | Dynamic publish workflow MinVer extraction | Planned  | Transition workflow to derive version from `dotnet minver` output instead of csproj parsing |
| Output Strategies | Hybrid JSON materialization                | Design   | Controlled via `.env` preview keys; see Optional Features document                          |
| Performance       | Structured benchmark baselines             | Planned  | Compare generation & runtime metrics across versions                                        |

Progress in this table should remain synchronized with the README Exit Codes and Testing sections and the Testing Framework document.

## Version Planning

### v4.5 (Frozen Bridge Release)

- Legacy `spocr.json` + `DataContext/` pipeline maintained via the `spocrv4` tool.
- Determinism reporting available, but strict exit codes disabled.
- Minimal JSON typing with heuristic fallbacks.

### v5 (Current Focus)

- Environment-first configuration (`.env`, CLI overrides, environment variables) as the only configuration surface.
- Snapshot-driven generation with deterministic ordering and golden manifest workflows.
- JSON procedure models with explicit metadata and roadmap for nested/streaming helpers.
- Upgrade messaging and dual-tool strategy documented in migration guides.

### Future Streams

- Xtraq successor repository (post-cutover) hosting ongoing innovations.
- Plugin hooks for extended database support once v5 stabilizes.
- Advanced customization (streaming, analyzers, telemetry) governed by preview flags.
