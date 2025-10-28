title: Optional Features
description: Preview configuration flags for JSON materialization in the v5 CLI.
versionIntroduced: 5.0.0
experimental: true
authoritative: true
aiTags: [roadmap, optional, deserialization, json, performance]
---

# Optional JSON Materialization

## Current Situation

- The generated `SpocRDbContext` emits typed JSON helpers (`Task<IReadOnlyList<T>>`) derived from SnapshotBuilder metadata.
- Some consumers still need raw JSON (`string` or `JsonDocument`) for streaming or pass-through scenarios.
- Preview work explores dual-mode and streaming outputs without reintroducing legacy configuration files.

## Target Vision

- JSON results available as typed models, raw payloads, or streaming sequences based on environment-first configuration.
- `.env` preview keys (`SPOCR_ENABLE_JSON_DUAL`, `SPOCR_ENABLE_JSON_STREAMING`) guard the new behavior; defaults remain typed-only.
- Runtime overrides surface through `AddSpocRDbContext` options, keeping generated code immutable.

## Architecture Proposal

### 1. Configurable Materialization Strategy

Preview `.env` keys:

```dotenv
# Enable dual output (raw + typed) for JSON procedures (preview)
SPOCR_ENABLE_JSON_DUAL=0

# Enable streaming helpers for large JSON payloads (preview)
SPOCR_ENABLE_JSON_STREAMING=0
```

- `SPOCR_ENABLE_JSON_DUAL=1` emits both typed and raw helper methods.
- `SPOCR_ENABLE_JSON_STREAMING=1` will introduce streaming APIs once implemented (currently scoped to design).
- Leaving keys unset keeps the baseline typed helper experience.

### 2. Generated DbContext Options

`AddSpocRDbContext` exposes an options callback so host applications can align runtime behavior with preview keys:

```csharp
services.AddSpocRDbContext(configuration, options =>
{
    options.JsonMaterialization = JsonMaterialization.Typed; // Typed, Raw, Dual (preview)
    options.EnableJsonStreaming = false;                     // Maps to SPOCR_ENABLE_JSON_STREAMING when enabled
});
```

### 3. Return Value API Form

- Typed default: `Task<IReadOnlyList<CustomerModel>> CustomersAsync(...)`
- Raw helper (dual mode): `Task<string> CustomersRawAsync(...)`
- Streaming preview candidate: `IAsyncEnumerable<JsonDocument> CustomersStreamAsync(...)`

### 4. Generator Adjustments

- Respect preview keys consistently across templates, runtime helpers, and diagnostics output.
- Capture active preview features in snapshots (`JsonFeatures.Dual`, `JsonFeatures.Streaming`) to maintain determinism.
- Keep typed helpers as the baseline output even when preview flags toggle additional methods.

### 5. Migration and Compatibility

- Defaults produce typed helpers only; enabling preview keys requires explicit `.env` opt-in tracked in `CHECKLIST.md`.
- Document usage and rollback plans before activating preview features in shared environments.
- CLI surfaces warnings when preview flags are active to encourage teams to log findings.

### 6. Performance Strategy

- Unit tests comparing typed vs. raw helpers; future coverage for streaming once implemented.
- Integration tests using sandbox procedures to observe payload size and throughput impacts.
- BenchmarkDotNet experiments planned post-implementation to quantify materialization overhead.

## Status

- **Current Phase**: Preview design (`SPOCR_ENABLE_JSON_DUAL`, `SPOCR_ENABLE_JSON_STREAMING`).
- **Dependencies**: output strategies roadmap, SnapshotBuilder JSON metadata.
- **Target Release**: Opt-in during v5 lifecycle with typed-only default.