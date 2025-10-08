Mark completed items with `- [x]` (was `- [ ]`).

- [x] Verify `samples/web-api/spocr.json` uses `"TargetFramework": "net10.0"` (line 3) or fix discrepancies.

- [x] Execute SpocR in the web-api directory
  - Ran: `dotnet run --project src/SpocR.csproj -- rebuild -p samples/web-api/spocr.json --no-auto-update --no-cache`
  - Improved: default TFM selection for `dotnet run` (no `--framework` needed). Fixed typo in option (`--no-cache`).

- [x] Output namespace shall be `SpocR.Samples.WebApi.DataContext` and avoid duplicate `.DataContext` segments.

- [x] Clarify “manual context”
  - Explained `samples/web-api/ManualData` as a temporary bridge; plan is to rely on generated modern context.

- [x] Explain `global.json` purpose and options (SDK pinning, roll-forward, allowPrerelease)
  - Documentation added; adding the file is optional.

- [x] Upgrade OpenAPI package to stable 10.x
  - `Microsoft.AspNetCore.OpenApi` → `10.0.0` in `samples/web-api/WebApi.csproj`.

- [x] Add example endpoints using generated SP wrappers
  - Endpoints added to `samples/web-api/Program.cs` (including JSON deserialization examples).

- [x] Add more Stored Procedures (UDT/UDTT/OUTPUT)
  - Added OUTPUT procedures (`CreateUserWithOutput`, `SumWithOutput`) into `samples/mssql/init/07-create-procedures.sql` and regenerated wrappers/DTOs.

- [x] Document v10+ template source via `ITemplateEngine` (no Output folder required)
  - Updated documentation to reflect modern template sourcing; legacy Output-* folders are transition fallbacks.

- [x] Add a docs/content section for `.gitignore` recommendations
  - New guide: `docs/content/guides/gitignore.md`.

- [x] Ensure new/changed comments and docs are in English
  - Generator comments and new docs written in English.

- [x] Consider duplication of namespace inference logic; extract shared logic
  - Introduced `Utils/ProjectNamespaceHelper` and changed `FileManager` to use it when saving/cleaning config.

---

Documentation (English)

- Modern Mode (net10+)
  - Default Target Framework is `net10.0`; the CLI runs without `--framework` by selecting the first TFM.
  - Namespaces are file-scoped and normalized; `.DataContext` is appended only once (no duplicates).
  - Inputs are generated as positional records (constructor parameters), one parameter per line, with no outer blank lines.
  - TableTypes are generated as positional records, one parameter per line, with `[property: MaxLength(n)]` emitted for NVARCHAR columns.
  - Models and Outputs remain classes, because the DB writes values into these instances at runtime.
  - OUTPUT stored procedures were added into the sample DB init script; corresponding wrappers/DTOs are generated and ready to consume.
  - Template strategy: v10+ relies on the embedded template engine (`ITemplateEngine`). Legacy `Output-*` folders are used as a fallback only.

- New Guide
  - `.gitignore` recommendations: see `docs/content/guides/gitignore.md` for practical ignores for repo root, the sample web API, and the docs app.

- Refactoring
  - `ProjectNamespaceHelper` centralizes root namespace inference (from csproj `RootNamespace`/`AssemblyName` or folder name). `FileManager` now uses this helper during configuration save/cleanup to avoid duplicating logic.

