title: Optional Features
description: Design notes for extended JSON materialization in the v5 CLI.
versionIntroduced: 5.0.0
experimental: true
authoritative: true
aiTags: [roadmap, optional, deserialization, json, performance]
---

# Optional JSON Materialization

## Current Situation

- The generated `SpocRDbContext` emits typed JSON helpers (`Task<IReadOnlyList<T>>`) derived from SnapshotBuilder metadata.
- Raw JSON helpers (`Task<string> FooRawAsync`) now ship alongside typed helpers by default for pass-through scenarios.
- Streaming and nested materialization remain in design and will land incrementally.

## Target Vision

- JSON results available as typed models, raw payloads, or streaming sequences based on environment-first configuration.
- Dual mode is enabled by default; future streaming/nested capabilities will follow once validated (no `.env` toggles planned).
- Runtime overrides surface through `AddSpocRDbContext` options, keeping generated code immutable.

## Architecture Proposal

### 1. Configurable Materialization Strategy

- Typed + raw helpers are emitted together by default (dual mode baseline).
- Streaming helpers will be exposed once validated; activation is expected to rely on generator version/capabilities rather than manual toggles.
- Nested model materialization and auto-deserialize will follow the same pattern: enabled when stable, documented in release notes.

### 2. Generated DbContext Options

`AddSpocRDbContext` exposes an options callback so host applications can align runtime behavior with preview keys:

```csharp
services.AddSpocRDbContext(configuration, options =>
{
    options.JsonMaterialization = JsonMaterialization.Dual; // Typed, Raw, Dual (default dual)
    options.EnableJsonStreaming = false;                    // Future: set true once streaming support ships
});
```

### 3. Return Value API Form

- Typed default: `Task<IReadOnlyList<CustomerModel>> CustomersAsync(...)`
- Raw helper (dual mode baseline): `Task<string> CustomersRawAsync(...)`
- Streaming candidate: `IAsyncEnumerable<JsonDocument> CustomersStreamAsync(...)` (future)

### 4. Generator Adjustments

- Ensure generator output stays deterministic as capabilities expand.
- Capture active JSON features in snapshots (`JsonFeatures.Dual`, `JsonFeatures.Streaming`) to maintain determinism.
- Keep typed helpers as the baseline output even as additional methods ship.

### 5. Migration and Compatibility

- Defaults now produce typed and raw helpers; future enhancements such as streaming will ship disabled until the generator version flips the capability on.
- Document usage and rollback plans when adopting new JSON capabilities in shared environments.
- CLI warnings should continue to flag early capabilities so teams capture findings in `CHECKLIST.md`.

### 6. Performance Strategy

- Unit tests comparing typed vs. raw helpers; future coverage for streaming once implemented.
- Integration tests using sandbox procedures to observe payload size and throughput impacts.
- BenchmarkDotNet experiments planned post-implementation to quantify materialization overhead.

## Status

- **Current Phase**: Dual mode GA; streaming/nested still in design.
- **Dependencies**: output strategies roadmap, SnapshotBuilder JSON metadata.
- **Target Release**: Incremental updates during v5 lifecycle; typed + raw default already available.