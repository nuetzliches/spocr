---
title: Output Strategies
description: Flexible approaches for handling nested JSON and complex data structures
versionIntroduced: 5.0.0
experimental: true
authoritative: true
aiTags: [roadmap, output, json, strategies, nested]
---

# Nested JSON Output Strategy

## Current State Assessment

- Output models currently generate flat classes that don't explicitly model `FOR JSON` results
- Nested JSON (e.g., `Orders` within `Users`) ends up as `string` without structured support
- Generators don't distinguish between JSON strings and relational result sets

## Objectives

- Nested JSON should optionally be generated as strongly typed objects (`NestedOrder`, `OrderItem`, etc.)
- A variant must remain that keeps the original JSON as `string` (for streaming/raw scenarios)
- Configuration must control whether nested objects are available as models ("Flatten") or separate `JsonPayload` property

## Implementation Approach

### 1. Inventory & Classification

- `.env` / environment variables: introduce `SPOCR_JSON_MODELS_GENERATE` and related flags
- Detection per Stored Procedure whether `ReturnsJson` and whether nested JSON `Columns` (via `StoredProcedureContentModel.Columns` – renamed from JsonColumns in v7)
- New flags in Definition.Model (`HasJsonPayload`, `JsonShape`)

### 2. Model Generation

- For JSON columns: generate `public string OrdersJson { get; set; }` (existing behavior)
- If `generateNestedJsonModels = true`:
  - Generate additional classes (`OrdersPayload`, `OrderItemPayload`)
  - Output model gets two properties: `public string OrdersJson { get; set; }` and `public OrdersPayload Orders { get; set; }`
  - Optionally introduce `JsonSerializable` attributes
- Template extension in Model Generator: iterate JSON column list, use `JsonSchemaService`

### 3. Deserialization Hook

- New helpers in Output layer: `JsonPayloadFactory.Parse<T>(string json)`
- Option `autoDeserializeNestedJson`: bool
  - When true: `SqlDataReaderExtensions` calls `JsonPayloadFactory` and fills `Orders` in `ConvertToObject<T>`
  - When false: Property remains null, consumer can use `Factory.Parse` manually

### 4. Generator/Configuration Changes

Configuration example:

```dotenv
# Enable generation of nested JSON models alongside raw payload
SPOCR_JSON_MODELS_GENERATE=true
# Control automatic deserialization into nested models (optional)
SPOCR_JSON_MODELS_AUTODESERIALIZE=false
```

- Engine reads the `SPOCR_JSON_MODELS_*` flags and influences template processing
- CLI documentation adjustments, default `generateNestedModels=false` (no breaking changes)

### 5. Test Plan

- Integration test with example procedure `UserOrderHierarchyJson`
- Snapshot test for generated models with/without flag
- Unit test: `SqlDataReaderExtensions.ConvertToObject` with JSON column → with active `autoDeserialize`, nested object gets filled
- Performance test: Compare `autoDeserialize=true` vs. `false` (benchmark)

## Recommendations

1. **Incremental implementation**: first model generation (optional), then auto-deserialization
2. **Integration interface**: leverage optional JSON deserialization concepts
3. **Documentation**: README extension + example code for new payload objects

## Status

- **Current Phase**: Design & Planning
- **Dependencies**: JSON Procedure Models (Phase 3)
- **Target Release**: v5.0.0
