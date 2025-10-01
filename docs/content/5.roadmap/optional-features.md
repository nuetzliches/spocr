---
title: Optional Features
description: Configurable functionality enhancements and optional deserialization strategies
versionIntroduced: 5.0.0
experimental: true
authoritative: true
aiTags: [roadmap, optional, deserialization, json, performance]
---

# Optional JSON Deserialization Concept

## Current Situation

- `AppDbContextPipe.ReadJsonAsync<T>` always deserializes results via `JsonSerializer.Deserialize<T>`
- Generator automatically replaces JSON procedures with `ReadJsonAsync<T>`, creating fixed model classes and losing the raw JSON stream
- Callers who want to pass JSON directly to clients (e.g., HTTP Response Streaming) pay the cost of deserialization and re-serialization

## Target Vision

- JSON results should optionally be available as `string` or `JsonDocument` without forced deserialization
- Configurable via `spocr.json` and at runtime (`AppDbContextOptions` or Pipe)
- Generators remain deterministic: identical output for identical configuration

## Architecture Proposal

### 1. Configurable Materialization Strategy

New setting in `spocr.json` (`jsonMaterialization`):

```jsonc
{
  "project": {
    "output": {
      "dataContext": {
        "jsonMaterialization": "Deserialize" // Options: "Deserialize", "Raw", "Hybrid"
      }
    }
  }
}
```

- `Deserialize` (Default): current behavior maintained
- `Raw`: Generator creates methods with `Task<string>` (or `Task<JsonDocument>`). No model output; consumer uses JSON directly
- `Hybrid`: Generator provides both variants (e.g., `Task<string> ExecuteFooRawAsync(...)` plus `Task<Foo> ExecuteFooAsync(...)`)

### 2. Runtime Switch in DataContext

`AppDbContextOptions` extended with `JsonMaterializationMode` (same enum as configuration). Pipe gets corresponding property plus fluent API:

```csharp
public enum JsonMaterializationMode { Deserialize, Raw }

public class AppDbContextOptions
{
    public int CommandTimeout { get; set; } = 30;
    public JsonMaterializationMode JsonMaterializationMode { get; set; } = JsonMaterializationMode.Deserialize;
}

public interface IAppDbContextPipe
{
    JsonMaterializationMode? JsonMaterializationOverride { get; set; }
}

public static IAppDbContextPipe WithJsonMaterialization(this IAppDbContext context, JsonMaterializationMode mode)
    => context.CreatePipe().WithJsonMaterialization(mode);
```

### 3. Return Value API Form

- `Deserialize` mode: unchanged `Task<T>`
- `Raw` mode: Generator replaces `ReadJsonAsync<T>` with `ReadJsonRawAsync` and method type becomes `Task<string>`
- `Hybrid` mode: Generator creates both methods (typed and raw). Naming suggestion: `ExecuteFooAsync` (typed) and `ExecuteFooRawAsync` (raw)

### 4. Generator Adjustments

- `StoredProcedureGenerator` reads `jsonMaterialization` and sets `returnType` plus `returnExpression` accordingly
- For `Hybrid`, generator uses template duplicate for raw variant, parameter and pipe setup share code
- Models only generated when needed

### 5. Migration and Compatibility

- Default remains `Deserialize`, existing projects get identical output
- `AppDbContextOptions` maintains default value for existing `IOptions` configurations
- Callers can set `context.WithJsonMaterialization(JsonMaterializationMode.Raw)` at runtime

### 6. Performance Strategy

- Unit tests for `ReadJsonAsync<T>` and `ReadJsonRawAsync` with all mode combinations
- Integration tests with sample procedures for typed vs. raw
- Performance measurement via BenchmarkDotNet: compare `Deserialize` vs. `Raw` with large JSON

## Status

- **Current Phase**: Design & Concept
- **Dependencies**: Output Strategies implementation
- **Target Release**: v5.0.0