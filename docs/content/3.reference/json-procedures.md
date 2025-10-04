# JSON Stored Procedure Generation

This reference explains how SpocR handles stored procedures that produce JSON result sets. Detection is based solely on the parsed metadata of each `ResultSet` (`ResultSets[i].ReturnsJson == true`). Naming conventions of the stored procedure (e.g. suffixes) are NOT used for detection.

## Generated Methods

For every stored procedure whose first result set (`ResultSets[0]`) is JSON, two method variants (pipe + context) are generated per access mode:

| Purpose                          | Method Pattern                    | Return Type                 |
| -------------------------------- | --------------------------------- | --------------------------- |
| Raw JSON payload                 | `<ProcedureName>Async`            | `Task<string>`              |
| Typed model (array JSON)         | `<ProcedureName>DeserializeAsync` | `Task<List<ProcedureName>>` |
| Typed model (single JSON object) | `<ProcedureName>DeserializeAsync` | `Task<ProcedureName>`       |

If a generated deserialize method name would collide with an existing one, a fallback `<ProcedureName>ToModelAsync` is used automatically.

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

## Null & Fallback Semantics

- Array JSON: `null` literal ⇒ empty list (`[]` equivalent at call site)
- Single JSON object: `null` literal ⇒ returned reference is `null`

## Limitations (Current)

- Only the first JSON result set is surfaced in method generation (multi-result groundwork exists internally via `ResultSets` but not yet user-facing)
- No overloads yet for passing custom `JsonSerializerOptions`

## Troubleshooting

| Symptom                                  | Cause                   | Mitigation                                               |
| ---------------------------------------- | ----------------------- | -------------------------------------------------------- |
| Empty generated model                    | Columns not inferable   | Provide explicit column list or accept empty type        |
| Deserialize returns null (single object) | JSON literal was `null` | Add null-check or fallback in caller                     |
| Missing `System.Text.Json` using         | No JSON SPs detected    | Confirm `ResultSets[0].ReturnsJson` flag in `spocr.json` |

---

_Applies to branch `feature/json-proc-parser`._
