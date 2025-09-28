# SpocR [![Publish NuGet](https://github.com/nuetzliches/spocr/actions/workflows/dotnet.yml/badge.svg)](https://github.com/nuetzliches/spocr/actions/workflows/dotnet.yml) [![NuGet Badge](https://img.shields.io/nuget/v/SpocR.svg)](https://www.nuget.org/packages/SpocR/) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

> **Sp**ent **o**n **c**ode **r**eduction - A modern C# code generator for SQL Server stored procedures

## Features

- Automatically scaffolds SQL Server stored procedures and models into C# files
- Intuitive CLI interface for seamless integration into your workflow
- Strongly-typed models with full IntelliSense support
- Flexible architecture supporting multiple deployment scenarios
- Async-first approach with full Task-based API generation
- Support for multiple .NET versions (NET 6.0, 8.0, 9.0)

# How SpocR works

SpocR extracts your database schema via a provided ConnectionString and stores it in a `spocr.json` configuration file.
This configuration file is highly customizable, allowing you to select which schemas to include or exclude.

SpocR generates a complete DataContext folder structure with all required C# code for your .NET application (App, API, or Services).

## Deployment Models

SpocR offers three deployment models to fit your specific needs:

- **Default Mode**: Standalone project with all dependencies included
- **Library Mode**: Integrate SpocR into other projects with AppDbContext and dependencies
- **Extension Mode**: Extend existing SpocR libraries without AppDbContext duplication

## Key Capabilities

- **User-Defined Table Types**: Full support for complex SQL parameter types
- **Strongly-Typed Models**: Automatic mapping to C# types with proper nullability
- **Multiple Result Sets**: Handle procedures returning lists or complex hierarchical data
- **JSON Support**: Direct handling of JSON string results without additional model classes
- **Async Operations**: First-class async/await support with CancellationToken handling

# Generated Project Structure

```
DataContext/
  |- Models/
  |  |- [schema]/
  |  |  |- [StoredProcedureName].cs      # Output model classes
  |- Inputs/
  |  |- [schema]/
  |  |  |- [InputType].cs               # Input model classes
  |- StoredProcedures/
  |  |- [schema]/
  |  |  |- [EntityName]Extensions.cs    # Extension methods
  |- TableTypes/
  |  |- [schema]/
  |  |  |- [TableTypeName].cs           # Table type definitions
  |- AppDbContext.cs                      # Core database context
  |- AppDbContextExtensions.cs            # General extensions
  |- ServiceCollectionExtensions.cs       # DI registration
  |- SqlDataReaderExtensions.cs           # Data reader utilities
  `- SqlParameterExtensions.cs            # Parameter utilities

```

# Integration with Your Application

## Register the DbContext

Register `IAppDbContext` in your dependency injection container:

```csharp
// Program.cs (.NET 6+)
builder.Services.AddAppDbContext(options => {
    options.ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
});

// Startup.cs (legacy)
services.AddAppDbContext(options => {
    options.ConnectionString = Configuration.GetConnectionString("DefaultConnection");
});
```

## Inject and Use the Context

```csharp
public class UserService
{
    private readonly IAppDbContext _dbContext;

    public UserService(IAppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<User> GetUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        // Calls the generated extension method for UserFind stored procedure
        return await _dbContext.UserFindAsync(userId, cancellationToken);
    }

    public async Task<List<User>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        // Calls the generated extension method for UserList stored procedure
        return await _dbContext.UserListAsync(cancellationToken);
    }

    public async Task<int> CreateUserAsync(string name, string email, CancellationToken cancellationToken = default)
    {
        // Calls the generated extension method for UserCreate stored procedure
        var result = await _dbContext.UserCreateAsync(name, email, cancellationToken);
        return result.RecordId; // Returns the new user ID
    }
}
```

# Naming Conventions

## StoredProcedure Naming Pattern

```
[EntityName][Action][Suffix]
```

- **EntityName** (required): Base SQL table name (e.g., `User`)
- **Action** (required): `Create`, `Update`, `Delete`, `Merge`, `Upsert`, `Find`, `List`
- **Suffix** (optional): `WithChildren`, custom suffix, etc.

## CRUD Operation Result Schema

For Create, Update, Delete, Merge, and Upsert operations, your stored procedures should return:

| Column     | Type | Description                  |
| ---------- | ---- | ---------------------------- |
| `ResultId` | INT  | Operation result status code |
| `RecordId` | INT  | ID of the affected record    |

# Technical Requirements

- **Database**: SQL Server 2012 or higher
- **Framework**: .NET 6.0+ (with backward compatibility to .NET Core 2.2)
- **Current Version**: 4.1.35 (September 2025)

## Dependencies

| Package                              | Purpose                  |
| ------------------------------------ | ------------------------ |
| Microsoft.Data.SqlClient             | SQL Server connectivity  |
| Microsoft.Extensions.Configuration   | Configuration management |
| Microsoft.CodeAnalysis.CSharp        | Code generation          |
| McMaster.Extensions.CommandLineUtils | CLI interface            |

# Installation

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) (latest recommended)
- SQL Server 2012+ database with stored procedures

## Option A: Install via NuGet (Recommended)

```bash
dotnet tool install --global SpocR
```

## Option B: Install from Source

```bash
# Clone the repository
git clone https://github.com/nuetzliches/spocr.git

# Navigate to source directory
cd spocr/src

# Uninstall previous versions (if needed)
dotnet tool uninstall -g spocr

# Build and install
dotnet pack --output ./ --configuration Release
dotnet tool install -g spocr --add-source ./
```

# Usage Guide

## Quick Start

```bash
# Create and configure your project
spocr create

# Pull schemas and build in one step
spocr rebuild
```

## Step-by-Step Workflow

```bash
# 1. Pull database schemas
spocr pull

# 2. Build DataContext folder
spocr build
```

## All Available Commands

| Command   | Description                                             |
| --------- | ------------------------------------------------------- |
| `create`  | Creates initial configuration file (spocr.json)         |
| `pull`    | Extracts database schema to update configuration        |
| `build`   | Generates DataContext code based on configuration       |
| `rebuild` | Combines pull and build in one operation                |
| `remove`  | Removes SpocR configuration and/or generated code       |
| `version` | Displays current version information                    |
| `config`  | Manages configuration settings                          |
| `project` | Project-related commands (create, list, update, delete) |
| `schema`  | Schema-related commands (list, update)                  |
| `sp`      | Stored procedure related commands (list)                |

## Advanced Command Options

```bash
# Selectively rebuild only certain generators
spocr build --generators TableTypes,Models,StoredProcedures

# Test mode (no file changes)
spocr build --dry-run

# View detailed logs
spocr build --verbose

# Get help for any command
spocr [command] --help
```

## Cleanup

```bash
# Remove SpocR configuration/generated code
spocr remove
```

# Configuration

## Project Role Types

The `spocr.json` file defines your project's behavior with three possible role types:

| Role          | Description                                                    | Use Case                                             |
| ------------- | -------------------------------------------------------------- | ---------------------------------------------------- |
| **Default**   | Creates standalone project with all dependencies               | Standard application with direct database access     |
| **Lib**       | Creates a SpocR library for reuse                              | Shared library to be referenced by multiple projects |
| **Extension** | Creates an extensible project without duplicating dependencies | Extending an existing SpocR library                  |

For Extension mode, you'll need to configure the namespace (Project.Role.LibNamespace) to resolve the SpocR library.

## Complete Configuration Schema

```json
{
  "Project": {
    "Role": {
      "Kind": "Default",
      "LibNamespace": "YourCompany.YourLibrary"
    },
    "Output": {
      "DataContext": {
        "Path": "./DataContext",
        "Models": {
          "Path": "Models"
        },
        "StoredProcedures": {
          "Path": "StoredProcedures"
        },
        "Inputs": {
          "Path": "Inputs"
        },
        "Outputs": {
          "Path": "Outputs"
        },
        "TableTypes": {
          "Path": "TableTypes"
        }
      }
    },
    "TargetFramework": "net8.0"
  },
  "ConnectionStrings": {
    "Default": "Server=.;Database=YourDatabase;Trusted_Connection=True;Encrypt=False"
  },
  "Schema": [
    {
      "Name": "dbo",
      "Path": "Dbo",
      "Status": "Build",
      "StoredProcedures": []
    }
  ]
}
```

# Examples and Resources

## Sample Implementation

For a complete example with stored procedures and API implementation:
[Sample Project Repository](https://github.com/nuetzliches/nuts)

### Debugging Tips

- Run with `--dry-run` to see what changes would be made without actually writing files
- Check your `spocr.json` file for proper configuration
- Ensure your stored procedures follow the naming convention requirements
- For specific issues, try running only one generator type at a time with the `--generators` option

## Development Resources

- [Roslyn Quoter](http://roslynquoter.azurewebsites.net/) - Helpful for understanding code generation patterns
- [.NET Global Tools Documentation](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools) - Learn more about .NET global tools
- [SQL Server Stored Procedures Best Practices](https://learn.microsoft.com/en-us/sql/relational-databases/stored-procedures/create-a-stored-procedure) - Microsoft's guidance on stored procedures

## Known Limitations

- **Computed Columns**: SQL Server cannot reliably determine nullable property for computed columns. Wrap computed columns in `ISNULL({computed_expression}, 0)` for cleaner models.
- **Complex Parameters**: When using table-valued parameters, ensure they follow the required table type structure.
- **JSON Procedures**: For stored procedures returning JSON data, no explicit output models are generated. You'll need to deserialize the JSON string manually or use the raw string result.
- **System-Named Constraints**: Some system-generated constraint names may cause naming conflicts; use explicit constraint names when possible.
- **Naming Conventions**: The code generator relies on specific naming patterns for stored procedures. Deviating from these patterns may result in less optimal code generation.
- **Large Schema Performance**: For very large database schemas with many stored procedures (more than 1000 stored procedures), the initial pull operation may take significant time to complete.

## Contributing

We welcome contributions! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

```

```
