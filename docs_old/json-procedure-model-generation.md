# JSON Procedure Model Generation

## Background & Goals

- Extend SpocR to scaffold strongly-typed models from stored procedures that return JSON payloads (currently treated as raw strings).
- Support two JSON source strategies:
  - Pull JSON from live database execution (current DB connection).
  - Load JSON by scanning SQL files within a solution directory to avoid runtime DB access during build.
- Reuse existing generator architecture (Input/Output/Model) with minimal disruption.

## Design Overview

### spocr.json

Add new configuration section:

`
"JsonProcedures": {
  "Mode": "Database" | "SqlFiles",
  "SqlFiles": {
    "RootPath": "./sql",
    "SearchPatterns": ["**/*.sql"]
  },
  "Schema": {
    "DefaultNamespace": "MyCompany.Data.JsonProcedures",
    "Naming": "PascalCase"
  }
}
`

Mode controls source of JSON payloads. Additional options for schema namespace/naming available regardless of mode.

### Source Retrieval

- Database mode: create manager to execute stored procedures, capture JSON sample (optionally limited to N rows). Leverage existing DB context infrastructure.
- SqlFiles mode: parse .sql files. Option discussions:
  - Use Roslyn: unsuitable for T-SQL. Rejected.
  - Custom helper: possible but error-prone for complex scripts.
  - Microsoft.SqlServer.TransactSql.ScriptDom parser: robust, open-source compatible NuGet package (evaluate size/license). Preferred.

### JSON Sample Handling

- For each configured JSON procedure:
  - Retrieve sample JSON (with retry/timeouts). Support explicit sample file fallback in spocr.json (e.g., SampleFile property).
  - Use System.Text.Json + JsonNode to inspect schema or integrate NJsonSchema for advanced inference (handles enums, arrays, nullable detection).
  - Generate class per root entity; nested classes for nested objects; record structs optional.
  - Provide configuration toggles:
    - CollectionsAsIEnumerable: default true.
    - InferNullability: default true.
    - NumericPrecision: allow decimal vs double selection.

### Code Generation Flow

1. JsonProcedureManager processes configuration and resolves sources.
2. JsonSchemaService normalizes JSON samples, builds internal schema representation.
3. JsonModelGenerator emits C# types via Roslyn SyntaxFactory similar to existing generators.
4. Output path default DataContext/JsonModels; configurable via Project.Output.JsonModels.Path.

Integrate into CodeGenerationOrchestrator with new flag GeneratorTypes.JsonModels. Update CLI --generators parsing.

### CLI & Commands

- Add spocr json pull command to populate sample cache without generating code (useful for preview/testing).
- Extend spocr build/ebuild workflows to respect new configuration.
- Provide --json-mode database|sqlfiles|cached override on CLI for quick switches.

### Additional Ideas

- Allow developers to pin schema versions: maintain .schema.json snapshots to detect breaking changes.
- Provide lint warnings when schema inference detects inconsistent arrays (e.g., mixed object structures).
- Offer post-generation hooks (e.g., partial classes, annotations) via configuration.
- Consider optional integration with JsonDocument for streaming scenarios (large payloads) while still providing typed models.

### Open Points

- Evaluate ScriptDom dependency footprint; if heavy, offer optional installation or fallback to simple parser for basic scenarios.
- How to authenticate DB access in CI/CD? Possibly support spocr.json sample caching to avoid DB hits.
- Should we allow manual schema definitions (YAML/JSON) to bypass inference? Likely yes for deterministic builds.

### Next Steps

1. Validate spocr.json schema addition and document default values.
2. Spike ScriptDom parsing vs. custom helper for SQL file mode.
3. Implement JSON schema inference proof of concept (cover primitives, arrays, nested objects).
4. Update documentation and CLI help; gather feedback before implementation.
