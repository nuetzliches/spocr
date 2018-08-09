# spocr
- Scaffolds your StoredProcedures into a C# DataContext structure. Be supriesed by many more features.
- Simply managed by the console (ComandLineInterface/CLI)

# How it works
- spocr generates the DataContex-Folder with all required C# Code for your Web-API
- spocr parse all StoredProcedures from a given ConnectionString
<br>
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
public Task<List<UserList>> ListAsync(CancellationToken cancellationToken = default(CancellationToken))
{
    return _dbContext.UserListAsync(User.Id, cancellationToken);
}
```

# Restrictions (TODO: define restrictions and the effects)

## StoredProcedure-Naming
#### `[EntityName][Action][Suffix]`
- EntityName (required): Name of base SQL-Table
- Action (required): Create | Update | Delete | Merge | FindBy | List
- Suffix: WithChildren | (custom suffix)

## First param in every StoredProcedure
- @UserId INT

## Required result for CRUD-Actions (Create, Update, Delete, Merge)
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
> git clone https://github.com/nuetzliches/spocr.git<br>
> dotnet pack --output ./<br>
> dotnet tool install -g spocr --add-source ./<br>

### b. From NPM (TODO: Upload NPM-Package)

# Use spocr

### 1. Create your spocr.json and configure it
> spocr create

### 2. Pull Database Schema (always after changes)
> spocr pull

### 3. (Re-)build your DataContext-Folder
> spocr build

# TODO: Demo-Project with StoredProcedures and API-Implementation
