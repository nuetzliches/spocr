# JSON Procedure Model Generation Roadmap

## Phase 0 – Research & Validation

- Confirm feasibility of SQL file parsing with Microsoft.SqlServer.TransactSql.ScriptDom (performance, licensing, packaging).
- Investigate JSON schema inference libraries (System.Text.Json vs NJsonSchema) and benchmark against representative payloads.
- Interview stakeholders regarding desired defaults, output structure, and caching expectations.

## Phase 1 – Configuration & Infrastructure

- Extend spocr.json schema to include JsonProcedures section with mode, file settings, schema settings, and sample caching options.
- Implement configuration validation + new POCOs.
- Create interfaces: IJsonProcedureSource, IJsonSchemaService, IJsonModelGenerator for dependency-injection-friendly architecture.

## Phase 2 – Source Providers

- Database provider: execute procedures safely (parameter support, top N rows, timeout, error handling).
- SQL file provider: integrate ScriptDom parser to locate stored procedure definitions and extract embedded JSON (consider JSON_QUERY/FOR JSON patterns).
- Optional cache provider: read stored sample JSON files when offline.

## Phase 3 – Schema Inference & Model Generation

- Implement schema inference engine (nullable detection, arrays, numbers, nested objects).
- Generate Roslyn syntax trees for models; support partial classes, attributes, and namespace configuration.
- Add generator tests with sample JSON fixtures.

## Phase 4 – CLI & UX Enhancements

- Extend spocr build to include JsonModels generator and update --generators help.
- New command spocr json pull to fetch/cache samples without full build.
- Provide verbose logging for inference decisions, caching operations, and warnings on schema drift.

## Phase 5 – Documentation & Samples

- Update README with new feature overview, configuration examples, and CLI usage.
- Add docs/json-procedure-model-generation.md as detailed concept (done) and create sample project demonstrating JSON procedure workflow.
- Record known limitations (e.g., dynamic JSON, large payloads, authentication requirements).

## Future Considerations

- Schema diffing: highlight changes between latest inference and cached schema.
- Custom transformers: allow users to inject code to post-process generated models.
- Support for other serialization formats (XML) if demand arises.
