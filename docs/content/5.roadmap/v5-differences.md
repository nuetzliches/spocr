---
title: v5 Differences (Preview)
description: Placeholder for upcoming v5 changes compared to bridge release v4.5.
version: 5.0
---

# v5 Differences (Preview)

> This page is a placeholder. Content will be populated as the v5 cutover approaches.

Planned high-level changes:

- Removal of legacy DataContext generation path (only SpocRVNext remains)
- Activation of Golden Hash Strict Mode (diff exit codes 21/23)
- Coverage gate escalation (≥80% core logic enforced)
- CTE-aware ResultSet naming enhancement
- Potential FOR JSON PATH root alias extraction for ResultSet names
- Deprecation removal: legacy `project.output.namespace` fallback, `Project.Role.Kind`, other obsolete keys

Tracking & Status:

| Area             | Bridge (v4.5)                | v5 Target           |
| ---------------- | ---------------------------- | ------------------- |
| DataContext      | Dual (legacy + vNext)        | vNext only          |
| Determinism      | Relaxed (reporting)          | Strict (exit codes) |
| Coverage Gate    | Incremental raise (30→50→60) | ≥80% enforced       |
| ResultSet Naming | Base table + suffix          | + CTE, JSON alias   |
| Config Cleanup   | Deprecated keys present      | Removed             |

Feedback welcome – open issues with label `v5-planning` to influence prioritization.
