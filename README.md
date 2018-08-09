# spocr
- Scaffolds your StoredProcedures into a C# DataContext structure. Be supriesed by many more features.
- Simply managed by the console (ComandLineInterface/CLI)

# How it works
- spocr generates the DataContex-Folder with all required C# Code for your Web-API
./DataContext
./DataContext/Models/[StoredProcedureName].cs
./DataContext/StoredProcedures/[EntityName]Extensions.cs
./DataContext/AppDbContext.cs
./DataContext/SqlDataReaderExtensions.cs

# Restrictions (TODO: define the restrictions)
## StoredProcedure-Naming
- `[EntityName][Action][Suffix]`

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
> git clone https://github.com/nuetzliches/spocr.git
> dotnet pack --output ./
> dotnet tool install -g spocr --add-source ./

### b. From NPM (TODO: Upload NPM-Package)

# Use spocr

### 1. Create your spocr.json and configure it
> spocr create

### 2. Pull Database Schema (always after changes)
> spocr pull

### 3. (Re-)build your DataContext-Folder
> spocr build
