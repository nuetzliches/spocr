---
title: JSON Procedure Handling
description: How SpocR detects, snapshots, and generates typed access for stored procedures that emit JSON payloads.
version: 5.0
aiTags: [json, stored-procedures, generator]
---

# JSON Procedure Handling

SpocR recognises procedures that project JSON via `FOR JSON` or `JSON_QUERY` and keeps enough metadata to generate strongly typed clients. This guide explains how the metadata is captured during `spocr pull`, how it turns into generated code during `spocr build`, and what conventions keep the experience predictable.

## Detection During `spocr pull`

The SnapshotBuilder reparses every stored procedure definition with ScriptDom. Whenever it encounters a top-level `SELECT … FOR JSON`, the first result set is flagged as JSON. Nested `FOR JSON` subqueries and `JSON_QUERY` calls are marked so that the metadata describes the full JSON tree.

- Only the first result set participates in JSON materialisation. Additional sets are treated as ordinary tabular projections.
- `FOR JSON PATH, ROOT('...')` populates the `RootProperty` flag so downstream tooling can reproduce the same hierarchy.
- `WITHOUT_ARRAY_WRAPPER` turns the payload into a single JSON object. Without this option SpocR assumes an array payload.
- `JSON_QUERY` is treated as a JSON container even if it mixes with scalar projections. This keeps nested JSON from being double-encoded.

### Snapshot Flags

| Field                                            | Scope      | Meaning                                                                  |
| ------------------------------------------------ | ---------- | ------------------------------------------------------------------------ |
| `ResultSets[i].ReturnsJson`                      | Result set | `true` when the set materialises JSON payloads.                          |
| `ResultSets[i].ReturnsJsonArray`                 | Result set | `true` when the payload is an array; omitted/`false` for single objects. |
| `ResultSets[i].JsonRootProperty`                 | Result set | Root element supplied via `ROOT('name')`; omitted when absent.           |
| `ResultSets[i].Columns[j].IsNestedJson`          | Column     | Column represents a nested JSON container inside a larger payload.       |
| `ResultSets[i].Columns[j].ReturnsJson`           | Column     | Column originates from a JSON expression (`FOR JSON` or `JSON_QUERY`).   |
| `ResultSets[i].Columns[j].ReturnsJsonArray`      | Column     | Nested column produces an array result (`true` when array).              |
| `ResultSets[i].Columns[j].JsonRootProperty`      | Column     | Nested column root alias; omitted when absent.                           |
| `ResultSets[i].Columns[j].DeferredJsonExpansion` | Column     | Column is backed by a function returning JSON and can be expanded later. |
| `ResultSets[i].Columns[j].FunctionRef`           | Column     | Fully-qualified reference to the function that produced the JSON blob.   |

The snapshot file keeps the JSON structure, which makes it reproducible across pulls and diffable in source control:

```jsonc
{
  "Schema": "identity",
  "Name": "UserListAsJson",
  "ResultSets": [
    {
      "ReturnsJson": true,
      "ReturnsJsonArray": true,
      "Columns": [
        { "Name": "userId", "TypeRef": "core._id" },
        { "Name": "record.rowVersion", "TypeRef": "sys.bigint" },
        {
          "Name": "roles",
          "ReturnsJson": true,
          "ReturnsJsonArray": true,
          "IsNestedJson": true,
          "Columns": [
            { "Name": "roleId", "TypeRef": "core._id" },
            { "Name": "displayName", "TypeRef": "core._label" },
          ],
        },
      ],
    },
  ],
}
```

### Nested JSON and `JSON_QUERY`

- Aliases such as `AS [record.rowVersion]` or `AS [gender.displayName]` become dotted names in the snapshot. The generator converts these aliases into nested record structs.
- `JSON_QUERY` is the recommended way to embed arrays or objects inside another JSON payload. The analyzer marks the column with `ReturnsJson` + `IsNestedJson` (and `ReturnsJsonArray` when appropriate), which prevents double escaping and preserves the nested structure in the snapshot.
- When a JSON payload comes from a user-defined function, the snapshot stores `FunctionRef` and sets `DeferredJsonExpansion`. During generation the function can be expanded into flat columns if metadata is available.

## Generation During `spocr build`

The code generator inspects the JSON flags and produces record structs and projectors that deserialize the SQL payload with `System.Text.Json`.

- Each stored procedure outputs a single aggregate class (for example `UserListAsJsonResult`) that contains one `IReadOnlyList<T>` per JSON result set. The typed helper (`ProcedureNameAsync`) is always emitted.
- The generator produces typed and raw helpers by default. Raw helpers (`ProcedureNameRawAsync`) return the JSON payload exactly as emitted by SQL, while typed helpers materialize objects via the schema metadata.
- JSON result sets use `JsonSupport.Options`, a shared `JsonSerializerOptions` instance that enables case-insensitive binding, tolerates numbers stored as strings, and provides lenient converters for nested payloads.
- SQL is expected to return exactly one row per JSON result set. For arrays the row should contain a JSON array literal; for single objects use `WITHOUT_ARRAY_WRAPPER` so SpocR reads a single JSON object.
- When SQL returns `NULL`, the list stays empty. Single-object payloads are skipped when deserialization returns `null`.

### Deserialization Flow

```csharp
new("ResultSet1", async (reader, ct) =>
{
    var list = new List<object>();
    if (await reader.ReadAsync(ct).ConfigureAwait(false) && !reader.IsDBNull(0))
    {
        var json = reader.GetString(0);
        var records = JsonSerializer.Deserialize<List<UserListAsJsonResultSet1Result>>(json, JsonSupport.Options);
        if (records != null)
        {
            foreach (var item in records)
            {
                list.Add(item);
            }
        }
    }
    return list;
})
```

### Nested Records from Aliases

- Dotted aliases (`alias.property`) produce nested record structs (for example `UserListAsJsonResultSet1GenderResult`).
- Nested JSON columns without dotted aliases (for example arrays returned via `JSON_QUERY`) are currently surfaced as raw `string` properties. The snapshot still tracks their schema so that future generators or downstream tooling can project them.
- When `DeferredJsonExpansion` is set, the generator attempts to expand the referenced function into individual columns if the snapshot contains its JSON schema.

## Runtime Expectations

- Generated extensions expose a typed async method per procedure (`ProcedureNameAsync`) that returns the aggregate result. The aggregate includes `Success`, `Error`, optional output parameter records, and the JSON-backed result lists.
- Raw helpers (`ProcedureNameRawAsync`) and typed methods are emitted together. Both share the same command pipeline and respect cancellation tokens.
- Streaming and nested model helpers remain future work items. They are not gated by `.env` toggles anymore; once implemented they will be documented alongside their stability status.
- Raw JSON strings are emitted alongside typed helpers; consumers can still serialize the typed list (`JsonSerializer.Serialize(result.ResultSets[0])`) if they want to avoid the raw helper.

## Authoring Guidelines

- Prefer `FOR JSON PATH` for deterministic property names. `AUTO` is supported but more likely to produce schema drift.
- Use `WITHOUT_ARRAY_WRAPPER` when the procedure should return a single object rather than an array.
- Alias nested properties with dotted names to receive nested record structs in the generated code.
- Wrap nested arrays or complex objects with `JSON_QUERY` so the analyzer flags them as JSON containers instead of varchar literals.
- Keep column names stable – schema drift results in regenerated record structs and downstream compile breaks.

- `project.jsonTypeLogLevel` (or the environment variable `SPOCR_JSON_TYPE_LOG_LEVEL`) controls how much JSON type enrichment logging reaches the console: `Detailed` (default), `SummaryOnly`, or `Off`.
- Set `SPOCR_JSON_AST_DIAG=1` to emit detailed ScriptDom findings during analysis; combine with `SPOCR_LOG_LEVEL=debug` for verbose tracing.
- `SPOCR_JSON_AUDIT=1` writes a `debug/json-audit.txt` report after generation that summarises every JSON result set, including any future experimental features once they land.
- JSON output is now part of the default surface. Capture any issues in the checklist under review findings so we can track regressions while the feature stabilizes.
- Run `spocr pull --no-cache --verbose` when SQL changes are not reflected in the snapshot – this forces a fresh parse and re-emits JSON diagnostics.

Keeping these conventions in place ensures that JSON-heavy procedures stay deterministic, diffable, and easy to consume from the generated C# surface.
