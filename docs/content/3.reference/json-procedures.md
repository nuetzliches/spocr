# JSON Stored Procedure Generation (Alpha)

Status: The JSON result set parser is in **alpha** – core generation paths are stable, but advanced inference (nested objects, multi-result JSON chaining) is still evolving. Breaking refinements (property typing upgrades, model shape stabilization) may occur in upcoming minor releases.

Detection is based solely on parsed metadata of each `ResultSet` (`ResultSets[i].ReturnsJson == true`). Naming conventions (suffixes like `AsJson`) are treated as hints only during pull heuristics – not as a hard requirement for generation.

## Generated Methods (Current Alpha Capabilities)

For every stored procedure whose first result set (`ResultSets[0]`) is JSON, two method variants (pipe + context) are generated per access mode:

| Purpose                          | Method Pattern                    | Return Type                 |
| -------------------------------- | --------------------------------- | --------------------------- |
| Raw JSON payload                 | `<ProcedureName>Async`            | `Task<string>`              |
| Typed model (array JSON)         | `<ProcedureName>DeserializeAsync` | `Task<List<ProcedureName>>` |
| Typed model (single JSON object) | `<ProcedureName>DeserializeAsync` | `Task<ProcedureName>`       |

If a generated deserialize method name would collide with an existing one, a fallback `<ProcedureName>ToModelAsync` is used automatically.

> Roadmap: Optional overloads with `JsonSerializerOptions` + streaming (`IAsyncEnumerable<T>`) for very large arrays.

### Example

Given a procedure `UserList` whose first result set returns JSON array and a procedure `UserFind` returning a single JSON object:

```csharp
// Raw JSON (array)
var rawUsers = await context.UserListAsync(ct);

// Typed list
var users = await context.UserListDeserializeAsync(ct);

// Raw JSON (single)
var rawUser = await context.UserFindAsync(ct);

// Typed single
var user = await context.UserFindDeserializeAsync(ct);
```

Internally the typed method calls the raw JSON method and then executes a `System.Text.Json.JsonSerializer.Deserialize<T>` call. For array JSON a null fallback to an empty list is applied:

```csharp
JsonSerializer.Deserialize<List<UserList>>(await UserListAsync(ct)) ?? new List<UserList>();
```

## Models

When `ResultSets[0].ReturnsJson` is true a model type with the same base name as the procedure is generated (e.g. `UserList` / `UserFind`). If property inference is impossible (dynamic SQL, wildcard selection) an empty model is still emitted with an explanatory XML doc comment. This allows consistent referencing in API annotations.

### Column Typing (Heuristics v3 / v4)

JSON result set column SQL types are assigned via a two-stage enrichment pipeline during `spocr pull`:

1. UDTT Stage: Columns matched against table-type input parameters (first match wins; avoids ambiguity).
2. Base Table Stage: Remaining unresolved columns matched via provenance fields (`SourceSchema`, `SourceTable`, `SourceColumn`).

If both stages (v3) fail to determine a concrete type, a fallback `nvarchar(max)` is assigned so downstream generators can rely on presence of `SqlTypeName`.

Parser v4 adds an opportunistic upgrade step: previously persisted fallback `nvarchar(max)` JSON columns are re-checked and replaced with a concrete type if resolvable via updated metadata. Log tags:

- `[json-type-table]` detailed per-column resolutions (Detailed mode only)
- `[json-type-upgrade]` fallback -> concrete upgrade events
- `[json-type-summary]` per-procedure aggregate (new vs upgrades)
- `[json-type-run-summary]` run-level aggregate (always shown unless `jsonTypeLogLevel=Off`)

## Null & Fallback Semantics

- Array JSON: `null` literal ⇒ empty list (`[]` equivalent at call site)
- Single JSON object: `null` literal ⇒ returned reference is `null`

## Limitations (Alpha)

| Area | Current Behavior | Planned / Notes |
| ---- | ---------------- | --------------- |
| Multiple JSON result sets | Only first (`ResultSets[0]`) exposed via helpers | Later: per-result accessor methods or unified wrapper |
| Deep nested objects | Flattening not attempted; raw JSON preserved | Potential optional projection generator |
| `JSON_QUERY` nullability | Conservative: may mark columns nullable broadly | Refined provenance + join analysis |
| Custom serializer options | Not exposed | Overload `DeserializeAsync(opts)` planned |
| Streaming large arrays | Entire payload buffered | Future: `Utf8JsonReader` incremental + `IAsyncEnumerable<T>` |
| Mixed scalar + JSON in first set | JSON branch wins – scalar columns ignored for typed path | Warning emission (planned) |
| Upgrades of fallback types | Occurs silently during subsequent pulls | Will emit structured diff summary |

### Known Edge Cases

- Dynamic SQL with shape variance between executions may lead to unstable models – consider stabilizing with `SELECT ... FOR JSON` fixed projections.
- Procedures returning an empty JSON literal (`''`) instead of `null` or `[]` are treated as deserialization failures; prefer `SELECT '' FOR JSON PATH` (returns `[]`).
- Legacy `Output` metadata has been removed; tooling must read `ResultSets[0].Columns`.

## CLI Integration

Use the CLI to introspect which stored procedures were identified as JSON-capable:

```bash
spocr sp ls --schema core --json
```

Returned array is derived from the current `spocr.json` snapshot. If a procedure is missing, run a fresh pull:

```bash
spocr pull --no-cache --verbose
```

## Troubleshooting

| Symptom                                  | Cause                   | Mitigation                                                                           |
| ---------------------------------------- | ----------------------- | ------------------------------------------------------------------------------------ |
| Empty generated model                    | Columns not inferable   | Provide explicit column list or accept empty type                                    |
| Deserialize returns null (single object) | JSON literal was `null` | Add null-check or fallback in caller                                                 |
| Missing `System.Text.Json` using         | No JSON SPs detected    | Confirm `ResultSets[0].ReturnsJson` in snapshot JSON                                 |
| Fallback `nvarchar(max)` persists        | Source metadata absent  | Ensure base table / UDTT accessible; run with `--no-cache --verbose` to inspect logs |

---

_Applies to branch `feature/json-proc-parser` (alpha parser)._ 
