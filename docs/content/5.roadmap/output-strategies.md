title: Output Strategies
description: Roadmap for nested JSON handling and dual-output features in the v5 CLI.
versionIntroduced: 5.0.0
experimental: true
authoritative: true
aiTags: [roadmap, output, json, strategies, nested]
---

# Nested JSON Output Strategy

## Current State

- SnapshotBuilder marks procedures with `ReturnsJson`, nested column metadata, and JSON shape hints.
- Generated models expose raw JSON strings; typed nested models remain in planning.
- Preview keys (`SPOCR_ENABLE_JSON_DUAL`, `SPOCR_ENABLE_JSON_STREAMING`) exist but default to off.

## Objectives

- Allow opt-in generation of nested JSON models when metadata captures relationships.
- Preserve raw payload access for streaming or pass-through APIs.
- Keep configuration environment-first and deterministic (no legacy JSON files or manual toggles in generated code).

## Implementation Approach

### 1. Inventory & Classification

- SnapshotBuilder already records nested JSON metadata (`Columns`, `JsonShape`).
- `.env` preview keys will activate nested model generation (`SPOCR_ENABLE_JSON_DUAL` as prerequisite, future `SPOCR_ENABLE_JSON_MODELS`).
- Generator surfaces metadata via `JsonFeatures` nodes so deterministic snapshots capture feature usage.

### 2. Model Generation

- Baseline keeps `string` payload properties (current behavior).
- Preview work generates companion models (`OrdersPayload`, `OrderItemPayload`) when nested columns exist.
- Output models expose both raw (`OrdersJson`) and typed (`Orders`) properties under dual mode.
- Potential use of `JsonSerializable` attributes considered for high-throughput scenarios.

### 3. Deserialization Hook

- Runtime helper (`JsonPayloadFactory`) parses nested payloads when preview keys demand it.
- `.env` flag (future `SPOCR_ENABLE_JSON_AUTODESERIALIZE`) would control auto-materialization vs. manual parsing.
- Generated DbContext options mirror the `.env` setting to maintain runtime parity.

### 4. Generator/Configuration Changes

- Preview keys:

```dotenv
# Enable nested JSON model generation (builds on dual mode)
SPOCR_ENABLE_JSON_MODELS=0

# Auto-deserialize nested payloads into typed properties (planned)
SPOCR_ENABLE_JSON_AUTODESERIALIZE=0
```

- Defaults keep these features disabled to match today’s output.
- Documentation and CLI help must highlight that enabling any preview key should be reflected in team checklists.

### 5. Test Plan

- Add sandbox procedure `UserOrderHierarchyJson` to validate nested model generation.
- Snapshot tests for each preview flag combination.
- Integration tests verifying `JsonPayloadFactory` behavior when auto-deserialize is active.
- Benchmark nested materialization vs. raw to measure overhead.

## Recommendations

1. Ship features incrementally: dual mode → nested models → auto-deserialize → streaming.
2. Align runtime options with `.env` preview keys to avoid divergence.
3. Update docs/samples when preview features turn on; reference `CHECKLIST.md` for evidence.

## Status

- **Current Phase**: Preview design for nested models and streaming.
- **Dependencies**: JSON procedure metadata, optional features roadmap.
- **Target Release**: Opt-in during v5 lifecycle after validation; defaults remain unchanged until flagged otherwise.
