# Snapshot Builder vNext

This folder hosts the modular snapshot pipeline that powers `spocr pull` in vNext. The
builder orchestrates Collect → Analyze → Write stages and emits deterministic schema
artifacts under `.spocr/schema`.

## Performance Baseline

The following scenarios capture the baseline timings recorded on 2025-10-26 using the
`samples/restapi` database snapshot (`dotnet run --project src/SpocR.csproj -- pull -p debug`).
All runs were executed with `SPOCR_LOG_LEVEL=info` to surface per-phase timings.

### Scenarios

| Scenario        | Description                                                                   | Command                                                                                                     |
| --------------- | ----------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| Cold Cache      | End-to-end run without cache reuse; forces full analysis and artifact hashing | `dotnet run --project src/SpocR.csproj -- pull -p debug --no-cache`                                         |
| Warm Cache      | Repeated run leveraging cached analysis results                               | `dotnet run --project src/SpocR.csproj -- pull -p debug`                                                    |
| Procedure Delta | Targeted refresh for a single procedure to validate incremental behavior      | `dotnet run --project src/SpocR.csproj -- pull -p debug --procedure workflow.WorkflowListAsJson --no-cache` |

### Metrics Snapshot

| Scenario        | Analyzed | Reused | Written | Unchanged | Total (ms) | Collect (ms) | Analyze (ms) | Write (ms) |
| --------------- | -------- | ------ | ------- | --------- | ---------- | ------------ | ------------ | ---------- |
| Cold Cache      | 55       | 0      | 0       | 146       | 7832       | 260          | 7319         | 242        |
| Warm Cache      | 50       | 5      | 0       | 141       | 9238       | 3465         | 5493         | 227        |
| Procedure Delta | 1        | 0      | 0       | 3         | 645        | 246          | 185          | 202        |

#### Notes

- Runs target the pre-generated `debug/.spocr` snapshot directory to avoid touching user
  workspaces.
- Warm cache timing reflects the current local environment; values fluctuate with ambient
  load and database responsiveness.
- The procedure delta scenario highlights the expected sub-second update cost when
  refreshing a single procedure after cache invalidation.
- Re-run the commands after significant pipeline changes to keep the table current.
- Set `SPOCR_SNAPSHOT_SUMMARY_PATH=<file>` (or `SPOCR_SNAPSHOT_SUMMARY=1` to use `snapshot-summary.json`)
  to persist per-run metrics alongside console output.
