# JSON Support Design

Status: In Progress (Raw + Deserialize implemented, XML docs added)
Target Version: Next Minor

## 1. Current State

- Stored procedures with a JSON first result set are detected via parser flags (`ResultSets[0].ReturnsJson`, plus shape flags `ReturnsJsonArray`, `ReturnsJsonWithoutArrayWrapper`).
- The generated method for JSON returning procedures keeps returning `Task<string>` (raw JSON) e.g. `Task<string> FooListAsync(...)`.
- JSON model generation was recently added. Models are generated when a procedure returns JSON and we inferred columns or produce an empty class as fallback.
- Runtime JSON access happens via `ReadJsonAsync` / `ReadJsonAsync<T>` in `AppDbContextExtensions.base.cs`.
- Potential breaking behavior: Existing JSON-returning procedures now yield typed return values where previously a raw string may have been expected.

## 2. Goals

1. Avoid breaking changes: Keep existing methods returning `string` for JSON payloads while adding typed overloads.
2. Always generate JSON models (no config flag) so they can be referenced in e.g. `[ProducesResponseType]` even if consumer uses raw string.
3. Prepare (optional) future structure for multiple result sets via an internal `ResultSets` concept (no deprecation of `Output` in config, only internal evolution).
4. Provide clear, non-breaking evolution without introducing deprecated markers into consumer-facing configuration.

## 3. Proposed API Evolution

### 3.1 StoredProcedureExtensions Overloads (Raw + Deserialize)

Current (raw JSON method to keep):

```csharp
Task<string> FooListAsync(...)
```

Added typed deserialize overload (array case):

```csharp
Task<List<FooList>> FooListDeserializeAsync(...)
```

Single-object JSON (WITHOUT ARRAY WRAPPER):

```csharp
Task<FooFind> FooFindDeserializeAsync(...)
```

Implementation notes:

- Keep raw method name exactly as-is to avoid breaking change.
- Add `DeserializeAsync` suffix for the typed variant.
- Internally call raw method then `JsonSerializer.Deserialize<T>`.
- Generate both pipe-based and context-based overloads consistent with existing pattern.
- Collision handling: If `FooListDeserializeAsync` already exists, fall back to `FooListToModelAsync`.

### 3.2 Always Generate Models

Change: Remove conditional guards; generate a model class whenever `ReturnsJson == true` even if zero columns detected.

- Empty model remains valid.
- Add XML doc comment: `// Generated JSON model (no columns detected; raw structure not introspectable at generation time).`

### 3.3 ResultSets (Multi-Result Support – Internal Future Capability)

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
| Raw JSON method naming | Keep existing `<Name>Async` returning `Task<string>` |
| Typed JSON overload    | Add `<Name>DeserializeAsync` (array/single)          |
| JSON models            | Always generated (no config flag)                    |
| Multi-result (future)  | Internal evolution, no external deprecation          |

## 4. Implementation Plan (Phased)

Phase 1 (Current Sprint):

- Add `DeserializeAsync` overload generation for JSON procedures (keep raw method intact).
- Ensure models always generated (remove any conditional remnants).
- Add XML doc comments to JSON models (especially empty models).

Phase 2:

- Introduce internal model classes `ResultSetModel` & adapt schema parsing.
- Backwards mapping from `Output` -> single `ResultSet`.
- Adjust generators to handle N result sets (composite model + enumerations).

Phase 3:

- Parser enhancements to statically infer multiple result sets (if feasible with ScriptDom or fallback heuristics).
- Add tests (snapshot + runtime) for multi result consumption.

Phase 4:

- (Optional) Introduce internal experimentation for multi-result container generation.
- Documentation & changelog updates.

## 5. Edge Cases & Risks

| Case                                                            | Mitigation                                             |
| --------------------------------------------------------------- | ------------------------------------------------------ |
| JSON proc name collision with DeserializeAsync suffix existing  | Fallback to `ToModelAsync` suffix                      |
| Empty JSON model confusing                                      | Add doc comment + console info once per generation run |
| Multi result complexity (different column counts per execution) | Scope: Only static shapes supported in generation      |
| Performance overhead generating extra overloads                 | Minimal (source gen incremental)                       |

## 6. Open Questions

- Should RawAsync overload also surface `JsonSerializerOptions` parameter? (Optional future)
- Provide `CancellationToken` consistently for all overloads (yes, align with existing pattern).
- Need a feature switch to suppress RawAsync? (Probably unnecessary.)

## 7. Next Actions

Completed This Iteration:

- [x] `StoredProcedureGenerator` emits `*DeserializeAsync` overloads for JSON procedures (pipe + context versions).
- [x] Typed overload calls raw JSON method internally and deserializes via `System.Text.Json.JsonSerializer`.
- [x] XML documentation for raw JSON and deserialize overload methods.

Pending / Planned:

- [ ] XML doc comments for generated JSON model classes (empty model explanation).
- [ ] Snapshot tests validating Raw + Deserialize method generation pattern.
- [ ] Integration test exercising real JSON proc -> typed model path.
- [x] Evaluate removal or deprecation notice for `JsonGenerationOptionsModel` (options marked obsolete; defaults no longer assign values explicitly).
- [ ] Helper methods (`IsJsonArray`, `IsJsonSingle`) for potential consumer ergonomics (optional).
- [ ] Draft internal `ResultSetModel` abstraction (future multi-result groundwork).

Nice-To-Have / Stretch:

- [ ] Optional `JsonSerializerOptions` parameter overloads without breaking base API.
- [ ] Config toggle to suppress typed overload generation (only if requested by users).

---

(End of Draft)
