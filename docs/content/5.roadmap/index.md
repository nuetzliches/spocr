---
title: Roadmap
description: SpocR development roadmap and future features
---

# SpocR Roadmap

This section contains the development roadmap, planned features, and ongoing work for SpocR.

## Current Focus Areas

- **Testing Framework & Quality Assurance** - Comprehensive testing infrastructure for KI-Agents and CI/CD
- JSON Procedure Model Generation
- Enhanced Output Strategies
- Performance Optimizations
- Developer Experience Improvements

## Roadmap Sections

- [Testing Framework](/roadmap/testing-framework) - Comprehensive testing infrastructure for automated validation
- [JSON Procedure Models](/roadmap/json-procedure-models) - Next-generation JSON handling
- [Output Strategies](/roadmap/output-strategies) - Flexible data serialization approaches
- [Development Tasks](/roadmap/development-tasks) - Current development priorities
- [Optional Features](/roadmap/optional-features) - Configurable functionality enhancements

## Consolidated Planned Features (High-Level)

| Category          | Feature                                    | Status   | Notes                                                                                       |
| ----------------- | ------------------------------------------ | -------- | ------------------------------------------------------------------------------------------- |
| Testing           | CI mode JSON + per-suite stats             | Done     | `test-summary.json` with nested suite metrics, durations, failures                          |
| Testing           | TRX robust parsing & retries               | Done     | Sequential orchestration + retry loop                                                       |
| Testing           | Granular exit sub-codes (41/42/43)         | Done     | Unit / Integration / Validation failure precedence                                          |
| Testing           | Console failure summary                    | Done     | Top failing tests (<=10) printed                                                            |
| Testing           | Single-suite JUnit XML export              | Done     | `--junit` flag outputs aggregate suite                                                      |
| Testing           | JUnit multi-suite reporting                | Planned  | See Remaining Open Items (Testing Framework)                                                |
| Testing           | Benchmark integration (`--benchmark`)      | Deferred | Placeholder flag; implementation later                                                      |
| Testing           | Rollback mechanism (`--rollback`)          | Planned  | Requires snapshot + transactional file operations                                           |
| CLI               | Exit code specialization (spaced blocks)   | Done     | Spaced categories + sub-codes implemented                                                   |
| Versioning        | Dynamic publish workflow MinVer extraction | Planned  | Transition workflow to derive version from `dotnet minver` output instead of csproj parsing |
| Output Strategies | Hybrid JSON materialization                | Design   | See Optional Features document                                                              |
| Performance       | Structured benchmark baselines             | Planned  | Compare generation & runtime metrics across versions                                        |

Progress in this table should remain synchronized with the README Exit Codes and Testing sections and the Testing Framework document.

## Version Planning

### v4.x (Current)

- **Testing Framework Implementation** - Multi-layer testing architecture with KI-Agent integration
- Stable CLI interface
- Core generation pipeline
- Basic JSON support

### v5.x (Planned)

- **Advanced Testing Features** - Self-validation, CI/CD integration, performance benchmarks
- Enhanced JSON procedure models
- Flexible output strategies
- Improved configuration system
- Performance optimizations

### Future Versions

- Plugin system
- Advanced customization
- Extended database support
