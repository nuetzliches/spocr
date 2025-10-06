# Debug Artifacts

This folder collects transient diagnostic outputs produced during development.

## Files

- `model-diff.json` – Raw diff data between current generated models and a reference tree (see below).
- `diff-stats.json` – Machine-readable summary of aggregate diff statistics.
- `model-diff-report.md` – Human-readable summary & risk assessment.

## Model Diff Scripts

### `eng/compare-models.ps1`

Compares two model directory trees.

Inputs:

- `-CurrentPath` : Path to current (new) model root (e.g. `./debug/DataContext/Models`).
- `-ReferencePath`: Path to reference/legacy model root.
- `-OutputJson` (optional): File name or path for output JSON. If only a filename is given, it is written into `./debug/`.

Normalization:

1. Strip block (`/* ... */`) & line (`// ...`) comments.
2. Collapse whitespace.
3. Extract first `namespace` and first `class` declaration name.
4. Collect public auto-properties `public <Type> <Name> { get; set; }`.
5. Create SHA-256 hash over ordered `Type Name` pairs.

Classification:

- Added: Exists only in current.
- Removed: Exists only in reference.
- Changed: Same relative path but class name or property hash differs.
- Unchanged: Identical class name and property hash.

Limitations:

- Ignores methods, attributes, nested classes, non-auto or expression-bodied properties.
- Only first class per file considered (adequate for generator output).
- Property order changes will count as Changed.

### `eng/diff-stats.ps1`

Reads a `model-diff.json` (auto-resolved inside `debug/` if only a filename) and emits aggregate statistics plus a JSON summary (`diff-stats.json`).

## Reproduce Example

```
pwsh -File ./eng/compare-models.ps1 -CurrentPath ./debug/DataContext/Models -ReferencePath <legacy-root>/DataContext/Models -OutputJson model-diff.json
pwsh -File ./eng/diff-stats.ps1 -DiffFile model-diff.json
```

Outputs will appear in `./debug/` automatically when a bare filename is supplied.

## Housekeeping

These artifacts are not meant for long-term version control except where useful for audit; keep large JSONs pruned when no longer necessary.
