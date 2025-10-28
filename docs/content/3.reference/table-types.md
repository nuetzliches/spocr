---
title: TableTypes Generation
position: 330
version: 5.0
status: draft
---

# Table Types (UDTT) Generation

SpocR generates strongly-typed records for each user-defined table type (UDTT) discovered in the snapshot. This enables safe passing of structured values (lists / sets) to stored procedures without manual `DataTable` plumbing.

## Goals

- Preserve original UDTT names (only minimal sanitizing)
- Deterministic hashing (timestamp line ignored during diff/hash)
- Single shared interface `ITableType` for all generated table types
- Per-schema folder layout under `SpocR/<SchemaPascalCase>/`

## Naming & Preservation

- Original snapshot name is retained exactly (case preserved) except for sanitizing invalid C# identifier characters.
- No forced `*TableType` suffix is added.
- Hyphenated schema names are normalized to PascalCase at folder level (e.g. `user-admin` schema -> `UserAdmin`).

## Timestamp Handling

Each generated file may include a `<remarks>` line with a generation timestamp. This line is ignored by the hash manifest logic so repeated deterministic generation produces identical effective hashes.

## Interface & Structure

```csharp
public interface ITableType {}

// Example generated record (simplified)
public sealed record UserContactTableType(
    int UserId,
    string Email,
    string Phone
) : ITableType;
```

Records are emitted as `sealed record` types for value semantics and deconstruction support.

## Usage Example

```csharp
var contacts = new List<UserContactTableType>
{
    new(1, "alice@example.com", "+1-555-0100"),
    new(2, "bob@example.com", "+1-555-0101")
};

await context.CreateUserBatchAsync(new CreateUserBatchInput { Contacts = contacts });
```

Internally the executor binds UDTT parameters using a structured parameter binder (ADO.NET `SqlParameter` with `SqlDbType.Structured`).

## Allow-List Filtering

When `SPOCR_BUILD_SCHEMAS` is set, only table types in those schemas are generated. This central positive filtering aligns with stored procedure generation (single configuration surface).

## Hash & Determinism

Hash manifests filter out the `<remarks>` timestamp line and any benign whitespace differences, ensuring stable hashes across repeated generation runs given unchanged inputs.

## Roadmap / Open Points

- Additional validation tests for Allow-List filtering
- Documentation of binder customization hooks
- Optional analyzer to ensure `ITableType` usage aligns with expected schema parameters

## FAQ

**Why not classes?** Records provide concise syntax and built-in equality useful for test assertions.

**Can I extend a generated table type?** Use partial records or composition in domain layer; generated files may be overwritten so avoid direct edits.

**Will names ever be auto-suffixed?** No; stability of original names is prioritized, changes would be a breaking change and require migration guidance.

---

Status: Draft – will be updated as Allow-List tests and binder docs are added.
