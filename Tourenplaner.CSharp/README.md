# Tourenplaner.CSharp

.NET 8 WPF migration of the GAWELA Tourenplaner using a layered architecture.

## Solution layout

- `src/Tourenplaner.CSharp.App` - WPF shell, navigation, views, view models
- `src/Tourenplaner.CSharp.Application` - use cases, service logic, abstractions
- `src/Tourenplaner.CSharp.Domain` - core entities and value objects
- `src/Tourenplaner.CSharp.Infrastructure` - JSON repositories and persistence
- `tests/Tourenplaner.CSharp.Tests` - unit tests for services and repositories

## Start

```powershell
cd Tourenplaner.CSharp
dotnet build Tourenplaner.CSharp.sln
```

Run app:

```powershell
cd Tourenplaner.CSharp
dotnet run --project src/Tourenplaner.CSharp.App/Tourenplaner.CSharp.App.csproj
```
