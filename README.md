# Folder Task Tracker

Simple file-backed desktop task tracker built with C# and Avalonia.

## Storage

- The app asks for a data folder on first start and remembers the last folder.
- Each group is stored as one `*.group.json` file in that folder.
- Every group or task state change rewrites the corresponding group file.

## Build

```powershell
dotnet restore FolderTaskTracker.sln --configfile NuGet.Config
dotnet build FolderTaskTracker.sln -c Release
dotnet run --project TaskTracker.App\TaskTracker.App.csproj
```

## Tests

```powershell
dotnet run -c Release --project TaskTracker.Tests\TaskTracker.Tests.csproj
```

## Self-contained publish examples

```powershell
dotnet publish TaskTracker.App\TaskTracker.App.csproj -c Release -r win-x64 --self-contained true -o publish\win-x64
dotnet publish TaskTracker.App\TaskTracker.App.csproj -c Release -r osx-arm64 --self-contained true -o publish\osx-arm64
dotnet publish TaskTracker.App\TaskTracker.App.csproj -c Release -r linux-x64 --self-contained true -o publish\linux-x64
```
