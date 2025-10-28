---
title: ResultSet Naming
position: 320
version: 5.0
status: stable
updated: 2025-10-18
---

# ResultSet Naming

The generator deterministically replaces generic placeholder names (`ResultSet1`, `ResultSet2`, …) with meaningful, table-based names – without any parity requirement with historical output. The resolver is always-on (no feature flag) and optimized for stability over aggressiveness.

## When is a rename applied?

A rename happens only if ALL of the following conditions hold:

1. The original name starts with `ResultSet` (generic placeholder)
2. The stored SQL text (`Sql` field in the snapshot) yields an unambiguous base table for that result set (first SELECT / primary FROM source)
3. The proposed name does not collide with an already assigned name in the same procedure

If any condition fails, the generic name is preserved (determinism > aggressive heuristics).

## Examples

| SQL Fragment                                                | Before     | After      | Reason                                                                                         |
| ----------------------------------------------------------- | ---------- | ---------- | ---------------------------------------------------------------------------------------------- |
| `SELECT * FROM dbo.Users`                                   | ResultSet1 | Users      | First base table `Users` detected                                                              |
| `SELECT u.Id, r.Name FROM dbo.Users u JOIN dbo.Roles r ...` | ResultSet1 | Users      | First FROM source wins (not `Roles`)                                                           |
| `SELECT 1 AS X`                                             | ResultSet1 | ResultSet1 | No table → no rename                                                                           |
| `SELECT * FROM #Temp`                                       | ResultSet1 | ResultSet1 | Temporary / non-qualified table ignored                                                        |
| `WITH C AS (SELECT * FROM dbo.Orders) SELECT * FROM C`      | ResultSet1 | Orders     | Planned: derive from CTE base table – currently still generic until CTE support is implemented |

(CTE / complex scenarios still WIP; see roadmap below.)

## Collision handling & duplicates

Previously, on a name collision (another result set from the same base table) no rename occurred. The current behavior:

1. The first occurrence of a base table receives the plain name (`Users`).
2. Additional result sets from the same table are suffixed numerically: `Users1`, `Users2`, …

This preserves determinism while improving clarity over plain `ResultSetX` placeholders.

## Multiple result sets

Each result set is processed independently:

- Different tables: each generic name is replaced with its table name (if resolvable & valid)
- Same table repeated: suffix scheme as above (`Users`, `Users1`, `Users2`)
- Not resolvable / unparsable: generic name (`ResultSetN`) remains

## Non-goals / exclusions

- Dynamic SQL (`EXEC(@sql)`) → ignored (no reliable base table inference)
- Complex UNION / deep CTE cascades → remain generic for now
- JSON outputs (`FOR JSON`) do not influence naming; focus stays on tabular structure

## Roadmap / open items

See central checklist (E014 / streaming). Planned or evaluated enhancements:

Short-term (v5 focus):

- CTE support (derive base table in final SELECT) – currently generic
- Explicit tests for dynamic SQL skip (`EXEC(@sql)`) for visibility
- Negative tests for deliberately invalid SQL (parser robustness) – fallback stays generic
- Finalize & document streaming naming convention (see preview below)

Mid-term:

- `FOR JSON PATH` root alias extraction (use alias as name for pure JSON payload)
- Parser caching / performance profiling (only if needed)
- Optional disable / override metadata (only if community demand arises)

Not prioritized / dropped:

- Recursive heuristics across complex UNION / CTE cascades (instability risk)
- Aggressive multi-source naming (non-deterministic outcomes)

## Test coverage (status)

Already covered:

- Simple SELECT from base table (rename)
- Duplicate base tables: suffixes (`Users`, `Users1`)
- Multi-result: only resolvable sets renamed
- Unparsable SQL → generic fallback
- Mixed case table names → normalized comparison (case-insensitive)

Planned / pending:

- CTE structure (first real base table in final SELECT)
- Dynamic SQL (`EXEC(@sql)`) explicit skip test
- `FOR JSON PATH` alias extraction (root alias)
- Section ordering in unified procedure file (stable naming despite reordering)

## Streaming naming convention (preview)

For upcoming streaming APIs (row / JSON streaming) we propose one method per result set. Two candidate patterns:

1. `ResultXStreamAsync(...)`
2. `StreamResultXAsync(...)`

Current favorite (2025-10-18): `StreamResultXAsync` – groups all streaming methods alphabetically and improves IntelliSense discoverability. Final decision will appear in the "Procedure Invocation Patterns" doc once the streaming helper ships. This page will then gain concrete examples like:

```csharp
await procedure.StreamResultUsersAsync(db, row => { /* ... */ }, cancellationToken);
```

Until finalized this section remains a preview.

## FAQ

**Why ignore every JOIN target except the first FROM source?**  
Determinism and uniqueness – multiple candidates could yield unstable names, so only the primary FROM source is used.

**Can I force a specific name?**  
Currently no. A future override / disable metadata hook may be added if needed.

**Does this affect hashing / determinism?**  
Only when the heuristic triggers. Identical SQL produces identical naming, so behavior is deterministic.

---

Status: Stable – updates occur only when the resolver gains new capabilities.
