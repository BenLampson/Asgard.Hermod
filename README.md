# Asgard Hermod

An open-source Windows management terminal for the Asgard ecosystem, built with C# 14, .NET 10, and Windows Presentation Foundation (WPF).

English is the primary language for repository documentation, issues, and pull requests. The application will support multiple languages; localization details will be decided during architecture planning.

## Status

The solution contains a minimal WPF startup window and the native Windows terminal component `Asgard.Hermod.WinTerminal`. Application architecture, product UI, localization, and service integration remain under discussion.

## Requirements

- Windows with support for .NET 10 desktop applications.
- .NET 10 SDK (the SDK policy is defined in `global.json`).
- Optional: a Visual Studio installation supporting .NET 10 with the .NET desktop development workload.

## Getting started

```powershell
git clone https://github.com/BenLampson/Asgard.Hermod.git
cd Asgard.Hermod
dotnet restore Asgard.Hermod.slnx
dotnet build Asgard.Hermod.slnx -c Release --no-restore
dotnet run --project src/Asgard.Hermod
```

WPF requires Windows to run. Open `Asgard.Hermod.slnx` in your IDE to develop the application.

Startup initializes a local SQLite database at
`%LOCALAPPDATA%\Asgard\Hermod\hermod.db`. See the [local storage guide](docs/local-storage.md)
for configuration and schema initialization.

## Repository layout

```text
.github/                 Community templates
docs/                    Architecture and roadmap
src/Asgard.Hermod/       WPF application
src/Asgard.Hermod.WinTerminal/  Windows WPF terminal library
tests/                  Terminal behavior and Windows integration tests
```

## Development

```powershell
dotnet format Asgard.Hermod.slnx --verify-no-changes
dotnet test --solution Asgard.Hermod.slnx --configuration Release
dotnet publish src/Asgard.Hermod -c Release -r win-x64 --self-contained false -o artifacts/publish/win-x64
```

The published application requires the .NET 10 Desktop Runtime. Terminal tests include real local cmd.exe and PowerShell processes. See the [terminal component guide](src/Asgard.Hermod.WinTerminal/README.md) for its API, ownership rules, source attribution, and current scope.

Read the [architecture](docs/architecture.md), [roadmap](docs/roadmap.md), and [contribution guide](CONTRIBUTING.md) before making changes.

## Community and security

Use [GitHub Issues](https://github.com/BenLampson/Asgard.Hermod/issues) for bugs and feature requests. Follow the [Code of Conduct](CODE_OF_CONDUCT.md). For vulnerabilities, see [SECURITY.md](SECURITY.md).

## License

Licensed under the [Apache License 2.0](LICENSE).
