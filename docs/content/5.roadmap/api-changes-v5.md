---
title: API Changes (v5 Preview)
description: Anticipated API modifications and removals in v5.
version: 5.0
---

# API Changes (v5 Preview)

> Placeholder for upcoming v5 API adjustments.

Planned modifications:

- Removal of `SpocR` legacy DataContext generator API surface.
- Consolidation of invocation helpers under a unified streaming/JSON abstraction.
- Possible introduction of CTE-aware ResultSet naming.
- Optional JSON alias extraction for `FOR JSON PATH` procedures.

Removed / Deprecated:

- Deprecated config keys (see Migration Guide).
- Legacy namespace fallback path.

New helper candidates:

- Streaming extensions (`UserListStreamAsync`, etc.).
- JSON raw vs typed dual-mode specialized methods.

Open questions:

- Interceptor naming convention finalization.
- Unified error model for streaming cancellation vs transport errors.

Timeline: Content will be updated as PRs merge into `feature/vnext`.
