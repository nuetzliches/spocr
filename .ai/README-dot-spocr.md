# The `.spocr` Directory

A reserved local workspace folder for SpocR runtime and augmentation data. This directory is NOT meant to be committed and should be added to your `.gitignore`.

## Current Structure

```
.spocr/
  cache/                # JSON cache snapshots (per database fingerprint)
```

## Rationale

- Keeps ephemeral & environment‑specific artifacts out of `spocr.json`
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

Contents (conceptual model – actual implementation may lag):

```jsonc
{
  "Fingerprint": "...",
  "CreatedUtc": "2025-10-04T12:34:56Z",
  "Procedures": [
    { "Schema": "dbo", "Name": "GetUsers", "ModifiedTicks": 638000000000000000 }
  ]
}
```

## Planned Uses Beyond Caching

| Feature Idea                  | Usage of `.spocr`                                                                                                                                                    |
| ----------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Schema/config split           | Store extracted schema (procedures, tables, types) as granular JSON (e.g. `.spocr/schema/<schema>/<proc>.json`) while keeping user editable settings in `spocr.json` |
| Diff assistance               | Maintain last two snapshot manifests to generate change reports before regeneration                                                                                  |
| Failure diagnostics           | Persist last error context (SQL text fragments) for post-mortem without polluting stdout                                                                             |
| Partial rebuild orchestration | Track dependency graph of generated files to enable selective regeneration                                                                                           |
| Experimental plugins          | Drop-in extension metadata or feature toggles without altering main config                                                                                           |

## .gitignore Recommendation

Add (or ensure) the following lines:

```
# SpocR runtime workspace
.spocr/
```

If granular control is desired, whitelist nothing—treat the entire directory as disposable.

## Safety & Cleanup

- Files are small JSON blobs; periodic manual cleanup is safe
- A future CLI command `spocr cache clear` may automate removal
- Corruption or deserialization failure is auto-treated as a cache miss

## Evolution Guidelines

When adding new items under `.spocr/`:

1. Use clear subdirectories (e.g. `schema/`, `diagnostics/`)
2. Avoid storing secrets (connection strings, credentials)
3. Prefer JSON for transparency unless binary size is a concern
4. Document new subfolder purpose here

---

Last update: 2025-10-04
