# Prompt: Consume SpocR CI JSON Test Summary

Use this prompt when you want to add or modify CI pipeline logic that reacts to SpocR test results without parsing console output.

## Context

SpocR writes `.artifacts/test-summary.json` when the `--ci` flag is supplied to `spocr test` (validation-only or full suite). Structure:

```jsonc
{
  "mode": "validation-only" | "full-suite",
  "timestampUtc": "2025-10-02T12:34:56Z",
  "validation": { "total": 3, "passed": 3 },
  "tests": { "total": 26, "passed": 26 },
  "success": true
}
```

## Goals

- Fail early if validation fails
- Annotate PR or upload artifact if tests fail
- Branch different steps depending on `mode`

## Example GitHub Actions Snippet

```yaml
- name: Run SpocR validation
  run: spocr test --validate --ci

- name: Parse JSON summary
  id: summary
  run: |
    node -e "const fs=require('fs');const p='.artifacts/test-summary.json';const j=JSON.parse(fs.readFileSync(p,'utf8'));console.log('mode='+j.mode);console.log('success='+j.success);if(!j.success){process.exit(40);}"

- name: Conditional full test suite
  if: steps.summary.outputs.success == 'true'
  run: dotnet test tests/Tests.sln --configuration Release --no-build
```

## Example PowerShell Local Consumption

```powershell
$summary = Get-Content .artifacts/test-summary.json | ConvertFrom-Json
if (-not $summary.success) { Write-Error "Validation/Test failed"; exit 40 }
Write-Host "Validation Passed: $($summary.validation.passed)/$($summary.validation.total)"
```

## Exit Code Guidance

Use 40 for test failures, 10 for validation (if separated in future), 80 for internal errors. Treat other reserved codes as future-proof—avoid hard assumptions.

## Prompt Template

```
You are updating the CI pipeline to react to SpocR's `.artifacts/test-summary.json`. Provide:
1. Parsing logic (tooling: bash, pwsh, node) extracting: mode, success, validation.passed/total, tests.passed/total.
2. Conditional failure if success=false (exit 40).
3. Optional PR annotation suggestion.
4. Idempotent behavior on reruns.
```

---

Last Updated: 2025-10-02
