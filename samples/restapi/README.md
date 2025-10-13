# Minimal Web API Project

This project is a minimal API built using .NET 8. It serves as a starting point for developing web applications with a focus on simplicity and performance.

## Project Structure

The project contains the following files:

- **Program.cs**: The entry point of the application. It configures the minimal API, defines endpoints, and starts the web server.
- **RestApi.csproj**: The project file for the C# project. It includes information about dependencies, the target version of the .NET framework, and other project settings.
- **Properties/launchSettings.json**: Contains configurations for the application's startup behavior, including environment variables and profiles for different launch options.
- **appsettings.json**: General configuration settings for the application, such as connection strings and other configuration elements.
- **appsettings.Development.json**: Contains specific configuration settings for the development environment, which can override settings in `appsettings.json`.
- **.gitignore**: Lists files and directories that should be ignored by Git, such as build artifacts and temporary files.
- **.editorconfig**: Defines code formatting rules for the project to ensure consistent formatting across the team.

## Getting Started

To get started with this project, follow these steps:

1. Clone the repository:

   ```
   git clone <repository-url>
   ```

2. Navigate to the project directory:

   ```
   cd samples/restapi
   ```

3. Restore the dependencies:

   ```
   dotnet restore
   ```

4. Run the application:
   ```
   dotnet run
   ```

## SpocR Integration (Bridge Phase)

This sample participates in the SpocR v4.5 → v5 bridge phase. You can experiment with the next generation generator (`SpocRVNext`) without breaking existing flow.

### Environment Configuration

Environment precedence (highest wins):

CLI > Environment Variables > `.env` file > `spocr.json` (legacy fallback – will be removed in v5).

Copy the example file and adjust values:

```
cp .env.example .env
```

Relevant keys:

| Variable | Purpose | Typical Value |
|----------|---------|---------------|
| `SPOCR_GENERATOR_MODE` | Controls generation pipeline | `legacy` / `dual` (DEFAULT) / `next` |
| `SPOCR_EXPERIMENTAL_CLI` | Enables new System.CommandLine parser | `1` to enable |
| `SPOCR_DB_DEFAULT` | Connection string used at runtime | `Server=localhost;...` |
| `SPOCR_STRICT_NULLABLE` | Escalate nullable warnings | `1` optional |
| `SPOCR_STRICT_DIFF` | Activate strict diff policy (future) | `1` optional |

Recommended for local exploration:

```
SPOCR_GENERATOR_MODE=dual
```

This produces legacy + next output side-by-side (comparison / diff tooling can consume hash manifests).

### Generating Code

From repository root (ensures tool available):

```
dotnet tool restore
dotnet tool run spocr pull
dotnet tool run spocr generate --mode dual
```

Or using environment variable only:

```
set SPOCR_GENERATOR_MODE=dual & dotnet tool run spocr generate
```

### Switching Modes

| Mode | Effect |
|------|--------|
| `legacy` | Only legacy DataContext generated (status quo) |
| `dual` | Legacy + next output (observation mode) |
| `next` | Only new pipeline output (preview) |

Mode defaults to `dual` in the bridge phase for internal test environments (subject to change before v5 release).

### Reporting Issues

If the next pipeline output differs unexpectedly or is missing artifacts, open an issue and include:

1. Generator mode (`legacy|dual|next`)
2. Hash manifest diff snippet (if available)
3. Simplified reproduction (stored procedure signature)

This accelerates stabilization of `SpocRVNext` before the v5 cutover.

## Endpoints

The API defines several endpoints that can be accessed once the application is running. Refer to the `Program.cs` file for details on the available endpoints.

## Contributing

Contributions are welcome! Please feel free to submit a pull request or open an issue for any suggestions or improvements.
