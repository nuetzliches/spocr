# The `.spocr` Directory

Local runtime workspace for the SpocR CLI. Treat everything inside as disposable state tied to the current developer environment. Confirm `.spocr/` stays ignored in git before checking in related changes.

## Current Structure

```
.spocr/
  cache/                # JSON cache snapshots (per database fingerprint)
  (future folders)      # diagnostics/, schema/, or other planner-approved data
```

## Rationale

- Keeps ephemeral & environment‑specific artifacts out of tracked configuration (`.env`)
- Allows incremental evolution without breaking existing configs
- Provides a staging area for future separation of configuration vs. extracted schema metadata

## Cache Files

Each cache file lives at:

```
.spocr/cache/<fingerprint>.json
```

`<fingerprint>` is a stable string composed of:

- Server (normalized)
- Database name
- Included schemas (sorted, comma-joined)

(Exact format may evolve; treat as opaque.)

Contents (current implementation – extend only when the roadmap checklist records the change):

```jsonc
{
  "Fingerprint": "...",
  "CreatedUtc": "2025-10-04T12:34:56Z",
  "Procedures": [
    { "Schema": "dbo", "Name": "GetUsers", "ModifiedTicks": 638000000000000000 }
  ],
  "Tables": [
    { "Schema": "dbo", "Name": "Users", "ColumnCount": 12, "ColumnsHash": "7D2F9E8A1B3C4D5E" }
  ]
}
```

Table fingerprints now live inside the schema cache fingerprint itself (`.spocr/cache/<fingerprint>.json`, SchemaCache v3). Each snapshot entry records only `Schema`, `Name`, `ColumnCount`, and `ColumnsHash`, leaving detailed column descriptors exclusively in `.spocr/schema/tables`.

## Growth Areas (Track in Checklist)

| Idea                          | Potential Usage                                                                                                                                                |
| ----------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Schema/config split           | Store extracted schema (procedures, tables, types) as granular JSON (e.g. `.spocr/schema/<schema>/<proc>.json`) while keeping user-editable settings in `.env` |
| Diff assistance               | Persist snapshot manifests to support `debug/model-diff-report.md` before regeneration                                                                         |
| Failure diagnostics           | Cache last error context (SQL text fragments) without polluting stdout                                                                                         |
| Partial rebuild orchestration | Track dependency graph of generated files to enable selective regeneration                                                                                     |
| Experimental plugins          | Host extension metadata or feature toggles without touching primary configs                                                                                    |

## .gitignore Recommendation

Add (or ensure) the following lines:

```
# SpocR runtime workspace
.spocr/
```

If granular control is desired, whitelist nothing—treat the entire directory as disposable.

## Safety & Cleanup

- Files are small JSON blobs; periodic manual cleanup is safe.
- Future CLI command `spocr cache clear` (live in roadmap checklist) may automate removal.
- Corruption or deserialization failure is auto-treated as a cache miss.
- Never store secrets (connection strings, credentials); rely on secure config sources instead.

## Evolution Guidelines

When adding new items under `.spocr/`:

1. Use clear subdirectories (e.g. `schema/`, `diagnostics/`)
2. Avoid storing secrets (connection strings, credentials)
3. Prefer JSON for transparency unless binary size is a concern
4. Document new subfolder purpose here

## Auto-Update Skip Reference

Environment variables influencing update behavior (checked very early):

```
SPOCR_SKIP_UPDATE=1      # or true/yes/on
SPOCR_NO_UPDATE=true     # alias
```

The v5 CLI no longer exposes dedicated flags to skip update prompts. Existing environment variables remain honored for compatibility (set them in CI if update checks must stay disabled). Record any changes to this behavior in the guardrail checklists.

---

Last update: 2025-11-05
