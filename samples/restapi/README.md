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

## Endpoints

The API defines several endpoints that can be accessed once the application is running. Refer to the `Program.cs` file for details on the available endpoints.

## Contributing

Contributions are welcome! Please feel free to submit a pull request or open an issue for any suggestions or improvements.
