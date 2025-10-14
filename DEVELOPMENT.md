# Restapi Development

```bash
dotnet run --project src/SpocR.csproj -- rebuild  -p samples/restapi/spocr.json --no-auto-update
dotnet run --project samples/restapi/RestApi.csproj
```

## vNext Namespace-Regel

Ab vNext gilt für generierten Code das konsistente Muster:

   <RootNamespace>.SpocR.<SchemaPascalCase>

Konfiguration:

- `.env` im Projektroot (z.B. `samples/restapi/.env`) enthält `SPOCR_NAMESPACE=RestApi` (nur Root, ohne `.SpocR`).
- `SPOCR_OUTPUT_DIR` Standard ist `SpocR` und wird einmalig an den RootNamespace angehängt.
- Jeder Generator hängt ausschließlich das Schema (PascalCase) an. Keine zusätzlichen Segmente wie `.Inputs`, `.Outputs`, `.Results`, `.Procedures` mehr im Namespace.

Beispiele:

```
RestApi.SpocR.Samples.CreateUserWithOutputInput
RestApi.SpocR.Samples.OrderListAsJsonResult
RestApi.SpocR.Dbo.UserContactSyncInput
```

Rationale:

- Eindeutige Zuordnung pro Schema; Artefakttyp ergibt sich aus Typnamen-Suffix (Input, Result, Aggregate, Output, Plan, Procedure).
- Verhindert inflationäre Namespace-Tiefe und verringert Merge-Konflikte.
- Konsistente Ableitung erleichtert Refactoring & tooling.

Override (optional):

Wer einen anderen Root verwenden möchte, setzt `SPOCR_NAMESPACE=MyCompany.Project`. Das System ergänzt weiterhin `.SpocR` + Schema.

Hinweis: Legacy-Generator bleibt unverändert; vNext läuft in `dual` Mode parallel zur Validierung.

# Nuts Demo Test

```bash
dotnet run --project src/SpocR.csproj -- rebuild  -p C:/Projekte/GitHub/Nuts/Libs/Nuts.DbContext/spocr.json --no-auto-update
dotnet run --project src/SpocR.csproj -- rebuild  -p C:/Projekte/GitHub/Nuts/Libs/Nuts.History/spocr.json --no-auto-update
dotnet run --project src/SpocR.csproj -- rebuild  -p C:/Projekte/GitHub/Nuts/Libs/Nuts.Identity/spocr.json --no-auto-update
dotnet run --project src/SpocR.csproj -- rebuild  -p C:/Projekte/GitHub/Nuts/Libs/Nuts.Identity.Organization/spocr.json --no-auto-update
dotnet run --project src/SpocR.csproj -- rebuild  -p C:/Projekte/GitHub/Nuts/Libs/Nuts.Logger/spocr.json --no-auto-update
dotnet run --project src/SpocR.csproj -- rebuild  -p C:/Projekte/GitHub/Nuts/Libs/Nuts.Notification/spocr.json --no-auto-update
dotnet run --project src/SpocR.csproj -- rebuild  -p C:/Projekte/GitHub/Nuts/Demo/Nuts.Demo.RestApi/spocr.json --no-auto-update
dotnet run --project C:/Projekte/GitHub/Nuts/Demo/Nuts.Demo.RestApi/Nuts.Demo.RestApi.csproj
```

# TEK Test

```bash
dotnet run --project src/SpocR.csproj -- rebuild  -p C:/Projekte/GitHub/tek-portal/TEK.Admin.WebApi/spocr.json --no-auto-update
dotnet run --project C:/Projekte/GitHub/tek-portal/TEK.Admin.WebApi/TEK.Admin.WebApi.csproj
```
---

## Nullable Reference Types – Stepwise Escalation (Phase 1)

Global `<Nullable>enable</Nullable>` ist aktiv. Eskalation erfolgt in kontrollierten Phasen, um Warnungsrauschen gering zu halten.

Phasenplan:

1. Baseline (aktuell): Alle Nullable-Warnungen bleiben Warnungen. Opportunistisches Fixing.
2. Fokus-Warnungen (lokal oder CI opt-in):
   - CS8602 Dereference of a possibly null reference
   - CS8603 Possible null reference return
3. Erweiterung: Weitere relevante Warnungen (z.B. CS8618) nach Reduktion der Hotspots.
4. CI Hard Gate: `SPOCR_STRICT_NULLABLE=1` dauerhaft setzen.

Lokaler Test (temporär, nicht committen):

```ini
# .editorconfig (lokal)
[*.cs]
dotnet_diagnostic.CS8602.severity = error
dotnet_diagnostic.CS8603.severity = error
```

CI Eskalation Beispiel:

```powershell
# Windows lokal (optional)
setx SPOCR_STRICT_NULLABLE 1
```

```yaml
# GitHub Actions Beispiel
env:
	SPOCR_STRICT_NULLABLE: 1
```

Rationale:

- Minimiert Big-Bang Refactor.
- Frühzeitige Absicherung vor echten Null-Deref Bugs.
- Klare, dokumentierte Eskalationsleiter.

Aufräumhinweis: Vor Commits lokale experimentelle .editorconfig Anpassungen entfernen.
