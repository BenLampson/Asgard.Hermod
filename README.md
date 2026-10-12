# Asgard Hermod

An open-source Windows management terminal for the Asgard ecosystem, built with C# 14, .NET 10, and Windows Presentation Foundation (WPF).

English is the primary language for repository documentation, issues, and pull requests. The application supports English and Simplified Chinese, follows the Windows display language by default, and remembers manual language choices. Unsupported languages and missing translations fall back to English. See the [localization guide](docs/localization.md).

## Status

The application combines independently compiled modules for the WPF shell, local
configuration and tags, Windows terminal sessions, and object storage. Alibaba
Cloud OSS is the first object storage connector. The executable owns startup and
composition; provider SDKs and persistence dependencies belong to their respective
implementation projects. See the [architecture guide](docs/architecture.md).

Heimdall browser sign-in and manual encrypted workspace upload/download are
available from the account menu. The separate Docker API checks user access with
its own Heimdall service client. See the [identity and synchronization guide](docs/identity-and-sync.md).

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
src/Asgard.Hermod/                    Application entry point and composition
src/Asgard.Hermod.Abstractions/       Shared contracts and models
src/Asgard.Hermod.Core/               Instance registry and workspace data source
src/Asgard.Hermod.UI/                 WPF shell and shared controls
src/Asgard.Hermod.Persistence.Sqlite/  SQLite storage and configuration services
src/Asgard.Hermod.Security.Windows/    Windows DPAPI credential protection
src/Asgard.Hermod.WinTerminal/         Windows terminal engine, control and sessions
src/Asgard.Hermod.ObjectStorage.Abstractions/  Object storage contracts
src/Asgard.Hermod.ObjectStorage/       Provider-independent operations and factory
src/Asgard.Hermod.ObjectStorage.UI/    Object storage pages and dialogs
src/Asgard.Hermod.Providers.AliyunOss/  Alibaba Cloud OSS connector and SDK
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
