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
- Generated models expose raw JSON strings and typed list helpers by default; nested materialization remains in planning.

## Objectives

- Deliver nested JSON models once metadata coverage and runtime hooks are ready.
- Preserve raw payload access for streaming or pass-through APIs.
- Keep configuration environment-first and deterministic; features become available without extra toggles.

## Implementation Approach

### 1. Inventory & Classification

- SnapshotBuilder already records nested JSON metadata (`Columns`, `JsonShape`).
- Generator surfaces metadata via `JsonFeatures` nodes so deterministic snapshots capture feature usage.

### 2. Model Generation

- Baseline keeps `string` payload properties (current behavior).
- Planned work generates companion models (`OrdersPayload`, `OrderItemPayload`) when nested columns exist.
- Output models will expose both raw (`OrdersJson`) and typed (`Orders`) properties once nested materialization ships.
- Potential use of `JsonSerializable` attributes considered for high-throughput scenarios.

### 3. Deserialization Hook

- Runtime helper (`JsonPayloadFactory`) parses nested payloads when nested materialization is enabled.
- Auto-materialization will be controlled by generator defaults and documented runtime options (no `.env` toggle planned).
- Generated DbContext options will mirror the runtime setting to maintain parity.

### 4. Generator/Configuration Changes

- Features land via generator updates; no additional `.env` toggles are planned.
- Documentation and CLI help will call out milestone releases and required checklist updates.

### 5. Test Plan

- Add sandbox procedure `UserOrderHierarchyJson` to validate nested model generation.
- Snapshot tests covering baseline vs. nested materialization outputs.
- Integration tests verifying `JsonPayloadFactory` behavior when auto-deserialize is active.
- Benchmark nested materialization vs. raw to measure overhead.

## Recommendations

1. Ship features incrementally: dual mode → nested models → auto-deserialize → streaming.
2. Align runtime options with generator defaults to avoid divergence.
3. Update docs/samples when features graduate; reference `CHECKLIST.md` for evidence.

## Status

- **Current Phase**: Preview design for nested models and streaming.
- **Dependencies**: JSON procedure metadata, optional features roadmap.
- **Target Release**: Opt-in during v5 lifecycle after validation; defaults remain unchanged until flagged otherwise.
