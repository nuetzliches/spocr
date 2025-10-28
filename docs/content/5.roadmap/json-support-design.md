# JSON Support Design

Status: In Progress (Typed + raw helpers default; streaming/nested in design)
Target Version: v5 lifecycle (incremental releases)

## 1. Current State

- SnapshotBuilder flags JSON procedures via `ResultSets[].ReturnsJson`, including shape hints (`ReturnsJsonArray`, `ReturnsJsonWithoutArrayWrapper`).
- Generated DbContext helpers return typed collections by default: `Task<IReadOnlyList<FooModel>> FooAsync(...)`.
- Raw JSON helpers (`FooRawAsync`) ship alongside typed helpers by default.
- JSON model generation is unconditional when metadata exists; empty models include a doc comment explaining missing columns.
- Runtime access flows through generated extensions; manual use of `AppDbContextPipe` is no longer required.

## 2. Goals

1. Keep typed helpers as the baseline experience while offering raw/streaming variants.
2. Maintain deterministic output across `.env` configurations.
3. Expand metadata and generation to support nested JSON payloads without manual intervention.
4. Prepare for multi-result JSON scenarios by evolving SnapshotBuilder (`ResultSets[]`).

## 3. Proposed API Evolution

### 3.1 Typed Helper Baseline

```csharp
// default output (always emitted)
Task<IReadOnlyList<FooModel>> FooAsync(CancellationToken cancellationToken = default);
```

- Typed methods remain the primary surface; cancellation tokens and namespace overrides are honored.
- Raw strings are no longer the default return type.

### 3.2 Dual Mode (Default)

```csharp
// generated in addition to typed helper
Task<string> FooRawAsync(CancellationToken cancellationToken = default);
```

- Both methods share parameter lists and leverage the same command pipeline.
- Generated DbContext options expose `JsonMaterialization = JsonMaterialization.Dual` to align runtime expectations.
- Snapshot metadata records `JsonFeatures.Dual = true` for determinism checks.

### 3.3 Streaming Mode (Future)

```csharp
// future enhancement: stream JSON documents for large payloads
IAsyncEnumerable<JsonDocument> FooStreamAsync(CancellationToken cancellationToken = default);
```

- Prototype relies on incremental `SqlDataReader` reading and minimal buffering.
- Requires coordinating `ExecuteReaderAsync` pipeline and disposal semantics.

### 3.4 Nested JSON Models (Future)

```csharp
public sealed class FooModel
{
  public string OrdersJson { get; set; } = string.Empty;
  public OrdersPayload? Orders { get; set; } // populated when auto-deserialize enabled
}
```

- Companion models (`OrdersPayload`, `OrderItemPayload`) generated when nested metadata exists.
- Auto-deserialize remains a future enhancement; activation approach TBD (no `.env` toggle planned).

### 3.5 ResultSets (Multi-Result Support – Internal Future Capability)

Problem: Single `Output` array limits model expressiveness for multi result scenarios.

Possible future internal snapshot structure (illustrative; not yet emitted in `.spocr/schema`):

```jsonc
{
  "StoredProcedures": [
    {
      "Name": "FooMulti",
      // "ResultSets": [ /* potential future multi-set description */ ],
    },
  ],
}
```

Mapping / semantics (future intent):

- Existing `Output` continues to function unchanged.
- Internal abstraction may later treat `Output` as a single logical result set.
- If/when multiple result sets are supported, generators can emit a composite container without changing existing configurations.

### 3.4 Migration & Versioning

| Aspect                 | Strategy                                             |
| ---------------------- | ---------------------------------------------------- |
| Typed helper baseline  | Maintain `<Name>Async` with `IReadOnlyList<T>`       |
| Raw helper (default)   | `<Name>RawAsync` emitted alongside typed helper      |
| Streaming (future)     | Emit `<Name>StreamAsync` once feature ships          |
| JSON models            | Always generated (empty models doc-commented)        |
| Multi-result (future)  | Internal evolution, no external deprecation          |

## 4. Implementation Plan (Phased)

Phase 1 (Done):

- Typed helper baseline established; JSON models generated unconditionally.
- Raw helper ships by default alongside typed helper.
- XML docs for raw + typed helpers and empty models.

Phase 2 (In progress):

- Add snapshot markers (`JsonFeatures.*`) capturing active JSON capabilities.
- Provide generated DbContext option binding for dual mode.
- Draft sandbox integration tests covering typed vs. raw outputs.

Phase 3 (Planned):

- Prototype streaming helper; validate resource usage and disposal semantics.
- Document streaming helper usage and CLI guidance once feature lands.
- Extend `TestCommand` scenarios to exercise streaming behavior.

Phase 4 (Future):

- Nested JSON model generation and optional auto-deserialization.
- Parser enhancements for multi-result detection.
- Documentation and changelog updates once features graduate from preview.

## 5. Edge Cases & Risks

| Case                                                            | Mitigation                                             |
| --------------------------------------------------------------- | ------------------------------------------------------ |
| JSON helper name collision (RawAsync)                           | Fallback to `FooToJsonAsync` suffix                    |
| Empty JSON model confusing                                      | Doc comment + console info once per generation run     |
| Multi result complexity (different column counts per execution) | Scope: Only static shapes supported in generation      |
| Performance overhead generating extra overloads                 | Minimal (source gen incremental)                       |

## 6. Open Questions

- Should raw helpers accept `JsonSerializerOptions`? (Maybe expose in dual mode.)
- When streaming is enabled, should typed helpers adopt streaming internally (breaking change risk)?
- How should future nested model work handle partial metadata (mixed typed/raw columns)?

## 7. Next Actions

Completed This Iteration:

- [x] Typed helper baseline in generated DbContext.
- [x] Dual-mode helpers (`*Async` + `*RawAsync`) emitted by default.
- [x] XML documentation for typed + raw helpers; empty models annotated.

Pending / Planned:

- [ ] Snapshot tests validating typed + dual outputs (hash determinism).
- [ ] Integration test exercising JSON dual mode for sandbox procedures.
- [ ] Add optional helper methods (`IsJsonArray`, `IsJsonSingle`) for consumers.
- [ ] Draft internal `ResultSetModel` abstraction (multi-result groundwork).
- [ ] Prototype streaming helper and document sandbox findings.

Nice-To-Have / Stretch:

- [ ] Optional `JsonSerializerOptions` parameter overloads without breaking base API.
- [ ] Runtime helper for on-the-fly model validation (json schema inference).

---

(End of Draft)
