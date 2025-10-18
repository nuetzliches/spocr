---
title: Removed Heuristics (v5 Preview)
description: Heuristic removals and rationale for simplification in v5.
version: 5.0
---

# Removed Heuristics (v5 Preview)

> Placeholder documenting heuristics slated for removal or adjustment.

Targets:

- Legacy DataContext naming quirks (suffix normalization removed earlier; full removal confirmed in v5).
- Disable flag concept (decided: no disable for ResultSet naming; always-on remains).
- Legacy role-based generation path (`Project.Role.Kind`).

Evaluation list:

- Any implicit column trimming logic still present?
- Hidden ordering heuristics in consolidated procedure file generation.

Rationale:
Simpler, deterministic generation reduces diff noise, accelerates review cycles, and lowers maintenance overhead.

Impact Mitigation:

- Unit tests added for ordering & naming stability.
- Strict diff mode (post coverage gate) will highlight any residual churn.

Feedback: File issues with label `heuristics-removal` including examples of remaining heuristics you consider problematic.
