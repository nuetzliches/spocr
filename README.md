# spocr [![Build Status](https://travis-ci.org/nuetzliches/spocr.svg?branch=master)](https://travis-ci.org/nuetzliches/spocr) [![NuGet Badge](https://buildstats.info/nuget/spocr)](https://www.nuget.org/packages/SpocR/)

- Scaffolds your StoredProcedures into a C# DataContext structure. Be supriesed by many more features.
- Simply managed by the console (ComandLineInterface/CLI)

# How it works
spocr generates the DataContex-Folder with all required C# Code for your Web-API<br>
spocr parse all StoredProcedures from a given ConnectionString and creates models and extension methods<br>

### Generated Folder and Files
./DataContext<br>
./DataContext/Models/[StoredProcedureName].cs<br>
./DataContext/StoredProcedures/[EntityName]Extensions.cs<br>
./DataContext/AppDbContext.cs<br>
./DataContext/SqlDataReaderExtensions.cs<br>

- Register AppDbContex in Startup.cs
```csharp
services.AddTransient<AppDbContext>();
```

- Inject AppDbContext in your Managers
```csharp
private readonly AppDbContext _context;
constructor MyManager(AppDbContext context) 
{ 
    _context = context;
}
```

- Run StoredProcedure in a Manager-Method
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

### a. From GitHub
Clone and Download Repository

`> git clone https://github.com/nuetzliches/spocr.git`<br>
`> cd src`<br>
`> dotnet pack --output ./ --configuration Release`<br>
`> dotnet tool install -g spocr --add-source ./`<br>
`> (dotnet tool uninstall -g spocr)`<br>

### b. From NPM (TODO: Upload NPM-Package)

# Use spocr

### 1. Create your spocr.json and configure it
> spocr create

### 2. Pull Database Schema (always after changes)
> spocr pull

### 3. (Re-)build your DataContext-Folder
> spocr build

# TODO: Demo-Project with StoredProcedures and API-Implementation

# Resources
- http://roslynquoter.azurewebsites.net/
- https://natemcmaster.com/blog/2018/05/12/dotnet-global-tools/


# Example for vscode launch.json
```
{
   "version": "0.2.0",
   "configurations": [
        {
            "name": ".NET Core Launch (console)",
            "type": "coreclr",
            "request": "launch",
            "preLaunchTask": "build",
            "program": "${workspaceFolder}/src/bin/Debug/netcoreapp2.1/SpocR.dll",
            // awailable commands: "create", "pull", "build", "rebuild", "remove", options: "-d|--dry-run"
            "args": ["create", "-d"], 
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
    ,]
}
```