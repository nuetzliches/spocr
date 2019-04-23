# SpocR [![Build Status](https://travis-ci.org/nuetzliches/spocr.svg?branch=master)](https://travis-ci.org/nuetzliches/spocr) [![NuGet Badge](https://buildstats.info/nuget/spocr)](https://www.nuget.org/packages/SpocR/)

- Scaffolds your StoredProcedures and Models to C# Files
- Easy managed by CLI
- Skallable and expandable
- no rigid dependencies

# How SpocR works
SpocR pulls the DB scheme over a given ConnectionString into spocr.json
The spocr.json is configurable (e.g. You can choose which scheme to build or ignore)
SpocR generates the DataContext-Folder with all required C# Code for your .net core Application (App, API or Services)<br>
SpocR is highly scalable. You can build it as Library, Extension or Default (both together as single Project)

You can use UserDefinedTableFunctions or single Values as Parameters.
The result of your StoredProcedures will be mapped as Model or List<Model>
SpocR also is supporting pure JSON-String Result from StoredProcedure without building any Models.

## Generated Folder Structure

./DataContext
./DataContext/Models/[StoredProcedureName].cs
./DataContext/Params/[StoredProcedureName].cs
./DataContext/StoredProcedures/[EntityName]Extensions.cs
./DataContext/AppDbContext.cs
./DataContext/AppDbContextExtensions.cs
./DataContext/SqlDataReaderExtensions.cs
./DataContext/SqlParameterExtensions.csmeterExtensions.cs

## Use the generated SpocR code

- Register `IAppDbContext` in Startup.cs

```csharp
services.AddAppDbContext();
```

- Inject IAppDbContext into your business logic, e.g. your managers or services.
  
```csharp
private readonly IAppDbContext _context;

constructor MyManager(IAppDbContext context) 
{ 
    _context = context;
}
```

- Call a stored procedure method
  
```csharp
public Task<List<UserList>> ListAsync(CancellationToken cancellationToken = default)
{
    return _dbContext.UserListAsync(User.Id, cancellationToken);
}
```

# Restrictions (TODO: define restrictions and the effects)

## StoredProcedure-Naming
#### `[EntityName][Action][Suffix]`
- EntityName (required): Name of base SQL-Table
- Action (required): Create | Update | Delete | (Merge, Upsert) | FindBy | List
- Suffix: WithChildren | (custom suffix)

## First param in every StoredProcedure
- @UserId INT

## Required result for CRUD-Actions (Create, Update, Delete, Merge, Upsert)
- [ResultId] INT, [RecordId] INT

# Requirements
- Database:     SQL-Server Version 2012
- Web-API:      [ASP.NET Core 2](https://docs.microsoft.com/en-us/aspnet/core/tutorials/first-web-api?view=aspnetcore-2.1)

# Required .NET Core Packages for Web-API
- Newtonsoft.Json
- System.Data.SqlClient

# Installation
- Install [.NET Core 2.1](https://www.microsoft.com/net/download)

### a. From NPM

`> dotnet tool install --global SpocR`<br>

### b. From GitHub
Clone and Download Repository

`> git clone https://github.com/nuetzliches/spocr.git`<br>
`> cd src`<br>
`> dotnet pack --output ./ --configuration Release`<br>
`> dotnet tool install -g spocr --add-source ./`<br>
`> (dotnet tool uninstall -g spocr)`<br>

# Use spocr

### 1. Create spocr.json and configure it
> spocr create

### 2. Pull schemes & Build DataContext-Folder
> spocr rebuild

## Or in single steps

### 2.1 Pull Database Schemes and update spocr.json
> spocr pull

### 2.2 Build DataContext-Folder 
> spocr build

### Remove SpocR (config and or DataContext)
> spocr remove

# TODO: Demo-Project with StoredProcedures and API-Implementation

# Resources
- http://roslynquoter.azurewebsites.net/
- https://natemcmaster.com/blog/2018/05/12/dotnet-global-tools/


# Example for vscode launch.json

- available commands for args:  "create", "pull", "build", "rebuild", "remove", 
- options: "-d|--dry-run"

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "Debug spocr (console)",
            "type": "coreclr",
            "request": "launch",
            "preLaunchTask": "build",
            "program": "${workspaceFolder}/src/bin/Debug/netcoreapp2.1/SpocR.dll",
            
            "args": ["${input:command}", "${input:option}"],
            "cwd": "${workspaceFolder}/src",
            "console": "integratedTerminal",
            "stopAtEntry": false,
            "internalConsoleOptions": "openOnSessionStart"
        },
        {
            "name": ".NET Core Attach",
            "type": "coreclr",
            "request": "attach",
            "processId": "${command:pickProcess}"
        }
    ],
    "inputs": [
        {
            "id": "command",
            "description": "spocr command",
            "default": "version",
            "options": ["create", "pull", "build", "rebuild", "remove", "version"],
            "type": "pickString"
        },
        {
            "id": "option",
            "description": "spocr options",
            "default": "",
            "options": ["", "--dry-run"],
            "type": "pickString"
        }
    ]
}
```
