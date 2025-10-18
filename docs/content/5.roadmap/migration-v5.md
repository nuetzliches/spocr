---
title: Migration Guide (v5 Preview)
description: High-level migration steps from bridge release v4.5 to v5.
version: 5.0
---

# Migration Guide (v5 Preview)

> Placeholder. Details will be added as the v5 cutover features stabilize.

Planned steps:

1. Remove legacy DataContext references (use only SpocRVNext outputs).
2. Eliminate deprecated config keys (`Project.Role.Kind`, legacy namespace fallback).
3. Adopt stricter determinism (Golden Hash strict mode; handle exit codes 21/23).
4. Verify coverage gate ≥80% passes locally before CI enforcement.
5. Update any custom tooling relying on old snapshot fields (e.g. removed `Output` array).

Potential breaking areas:

- Namespace fallback removal.
- ResultSet naming refinement (CTE support, JSON alias extraction).
- Removed heuristics altering generated model order.

Action checklist (to be finalized):

- [ ] Remove legacy generation flags from CI.
- [ ] Clean up obsolete configuration nodes.
- [ ] Run `spocr generate --write-golden` (once strict mode activated) to update manifest intentionally.
- [ ] Address any diff allow-list pruning.
