> **Deprecation notice:** SpocR is no longer maintained. Please migrate to [Xtraq](https://github.com/nuetzliches/xtraq) using the guide at https://nuetzliches.github.io/xtraq/getting-started/migrating-from-spocr. The CLI now emits a warning for every invocation and future updates will only happen in Xtraq.

# SpocR [![Publish NuGet](https://github.com/nuetzliches/spocr/actions/workflows/dotnet.yml/badge.svg)](https://github.com/nuetzliches/spocr/actions/workflows/dotnet.yml) [![NuGet Badge](https://img.shields.io/nuget/v/SpocR.svg)](https://www.nuget.org/packages/SpocR/)

- Scaffolds your Stored Procedures and Models to C# Files
- Easily managed through a CLI interface
- Scalable and extensible architecture
- No rigid dependencies for maximum flexibility

# How SpocR works

SpocR extracts your database schema via a provided ConnectionString and stores it in a `spocr.json` configuration file.
This configuration file is highly customizable, allowing you to select which schemas to include or exclude.

SpocR generates a complete DataContext folder structure with all required C# code for your .NET application (App, API, or Services).

The tool is designed for flexibility. You can:

- Build it as a standalone project (Default mode)
- Use it as a library to integrate into other projects (Library mode)
- Create extensions to enhance existing SpocR libraries (Extension mode)

SpocR supports User-Defined Table Functions and various parameter types. The results of your Stored Procedures will be automatically mapped to strongly-typed models or as List<Model>. It also supports pure JSON-string results from Stored Procedures without building additional model classes.

## Generated Folder Structure

```
./DataContext/
  ├── Models/[schema]/[StoredProcedureName].cs
  ├── StoredProcedures/[schema]/[EntityName]Extensions.cs
  ├── TableTypes/[schema]/[TableTypeName].cs
  ├── AppDbContext.cs
  ├── AppDbContextExtensions.cs
  ├── SqlDataReaderExtensions.cs
  └── SqlParameterExtensions.cs
```

## Using the generated SpocR code

### Step 1: Register the context

Register `IAppDbContext` in your application's dependency injection container:

```csharp
// .NET 6+ in Program.cs
builder.Services.AddAppDbContext();

// Or in Startup.cs for older versions
services.AddAppDbContext();
```

### Step 2: Inject the context

Inject `IAppDbContext` into your business logic components:

```csharp
private readonly IAppDbContext _dbContext;

public MyManager(IAppDbContext dbContext)
{
    _dbContext = dbContext;
}
```

### Step 3: Call stored procedures

Use the generated extension methods to call your stored procedures:

```csharp
public Task<List<UserList>> ListAsync(CancellationToken cancellationToken = default)
{
    return _dbContext.UserListAsync(User.Id, cancellationToken);
}
```

# Naming Conventions

## StoredProcedure Naming Pattern

#### `[EntityName][Action][Suffix]`

- **EntityName** (required): Name of the base SQL table
- **Action** (required): Create | Update | Delete | (Merge, Upsert) | Find | List
- **Suffix** (optional): WithChildren | [custom suffix]

## Required Result Format for CRUD Operations

For Create, Update, Delete, Merge, and Upsert operations, stored procedures should return:

- `[ResultId] INT`: Operation result status
- `[RecordId] INT`: ID of the affected record

# Technical Requirements

- **Database**: SQL Server version 2012 or higher
- **Framework**: .NET Core / .NET 6+ (supports down to .NET Core 2.1)
- **Current Version**: 4.0.0 (as of April 2025)

## Required .NET Packages

- Microsoft.Data.SqlClient
- Microsoft.Extensions.Configuration

# Installation Guide

First, ensure you have the [.NET SDK](https://dotnet.microsoft.com/download) installed (latest version recommended)

## Option A: Install from NuGet (Recommended)

```
dotnet tool install --global SpocR
```

## Option B: Install from GitHub Source

```
# Clone the repository
git clone https://github.com/nuetzliches/spocr.git

# Uninstall previous versions if needed
dotnet tool uninstall -g spocr

# Build and install from source
cd src
(dotnet msbuild -t:IncrementVersion)
dotnet pack --output ./ --configuration Release
dotnet tool install -g spocr --add-source ./
```

# Using SpocR

## Quick Start

To quickly set up your project:

```
# Create and configure spocr.json
spocr create

# Pull schemas and build DataContext
spocr rebuild
```

## Step-by-Step Approach

If you prefer more control:

```
# Step 1: Pull database schemas and update spocr.json
spocr pull

# Step 2: Build DataContext folder
spocr build
```

## Removing SpocR

To remove SpocR configuration and/or generated code:

```
spocr remove
```

# Advanced Configuration

## Project Role Types in spocr.json

### Project.Role.Kind

- **Default**: Creates a standalone project with all dependencies
- **Lib**: Creates a SpocR library for integration into other projects, including AppDbContext and dependencies
- **Extension**: Creates an extensible project without AppDbContext and dependencies to extend an existing SpocR library. Requires configuring the namespace (Project.Role.LibNamespace) to resolve the SpocR library

# Sample Implementation

For a complete example project with stored procedures and API implementation, visit:
https://github.com/nuetzliches/nuts

# Additional Resources

- [Roslyn Quoter](http://roslynquoter.azurewebsites.net/) - Useful for understanding code generation
- [.NET Global Tools](https://natemcmaster.com/blog/2018/05/12/dotnet-global-tools/) - Information about .NET global tools

# Known Issues and Limitations

- SQL Server cannot reliably determine the nullable property for computed columns. For cleaner models, wrap computed columns in `ISNULL({computed_expression}, 0)` expressions.
- When using complex types as parameters, ensure they follow the required table type structure.
