title: API Changes (v5)
description: Confirmed API additions, removals, and migration notes for the v5 CLI and generated surface.
version: 5.0
---

# API Changes (v5)

The v5 release streamlines the generated runtime surface and clarifies how host applications integrate stored procedure access. Use this page to understand what changed and where follow-up work remains.

## Removed or Breaking Changes

- **Legacy DataContext**: The generated `SpocR.DataContext` classes synonymous with the v4.5 pipeline were removed. Projects must reference the new `SpocR` outputs (or the directory specified by `SPOCR_OUTPUT_DIR`).
- **Configuration APIs**: Helper extensions that read `spocr.json` (e.g. `SpocRConfigurationExtensions.AddSpocRFromJson`) were deleted. Consumers call `AddSpocRDbContext(configuration)` and supply connection strings via standard .NET configuration.
- **Namespace fallbacks**: Implicit namespace injection is gone. The generated code expects `SPOCR_NAMESPACE` to be present and validated.
- **Disable toggles**: Flags that suppressed specific procedure wrappers or JSON models were removed. Teams should refactor procedures or consume the generated types explicitly.

## Additions & Adjustments

- **DbContext scaffolding**: `AddSpocRDbContext(IServiceCollection, Action<SpocROptions>?)` now exposes hooks for supplying runtime connection strings, toggling diagnostics, and plugging in telemetry.
- **JSON helpers**: Generated JSON result classes expose explicit `ReturnsJson` metadata and typed accessor patterns. Dual-mode generation (raw + materialized) is controlled through `.env` keys (`SPOCR_ENABLE_JSON_DUAL`) for feature branches.
- **Snapshot-driven enums**: Procedure status enums derive directly from snapshot metadata rather than post-processing scripts, making them predictable across environments.
- **Logger integration**: Generated code uses `ILogger<SpocRDbContext>` for tracing rather than bespoke logging helpers.

## Pending Follow-up

- Streaming helpers (`IAsyncEnumerable<T>` wrappers) live behind preview keys and remain in feature validation.
- Analyzer surfacing (`SPOCR_ENABLE_ANALYZER_WARNINGS`) ties into the Roslyn package refresh; documentation will expand once the package ships.

## Migration Checklist

1. Remove references to `SpocR.DataContext` and update dependency injection setup to the new `AddSpocRDbContext` signature.
2. Replace any `spocr.json` configuration loaders with modern `.env`/configuration binding.
3. Regenerate code via `spocr build` and update namespaces according to `SPOCR_NAMESPACE`.
4. Review generated JSON helpers and adjust calling code to consume typed projections.

Raise questions under the `api-surface-v5` label if additional clarifications are required. Document your adoption steps in `CHECKLIST.md` to keep parity with the roadmap.
