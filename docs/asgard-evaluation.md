# Asgard integration assessment

Hermod references `Asgard.Core` 6.0.3, which transitively references
`Asgard.Abstractions` 6.0.3. WPF startup now loads Asgard YAML configuration and
registers local SQLite storage through `AddDatabase`. No external infrastructure
is enabled. See the [local storage guide](local-storage.md).

The assessment used the published package metadata and local Asgard source at
commit `c23202dde46254d69519b7dea1234c9c93d6f118`, matching the package repository
commit. The WPF application builds successfully with this dependency.

## Reuse decisions

| Concern | Existing capability | Hermod direction |
| --- | --- | --- |
| Configuration | AsgardConfigurationBuilder, AsgardConfigurationRoot, YAML provider, ConfigPath binding | Use Asgard YAML configuration and typed settings; application code still owns saving user preferences. |
| Logging | AddAsgardSerilog, LogConfig, file sinks and Microsoft logging integration | Reuse file logging; use a writable per-user data directory. |
| Service registration | Service/repository scanners and module registration tracking | Reuse appropriate registrations instead of creating a new scanning framework. |
| Shared capabilities | AddAsgardContext and scoped AbsAsgardContext | Register required modules first; use explicit operation scopes rather than capturing the context in application singletons. |
| Scheduled jobs | AddJobScheduler and QuartzJobScheduler | Reuse for persistent application scheduling needs; startup and shutdown must explicitly manage its lifecycle. |
| Security and serialization | Encryption, key generation, compression, JSON converters and identity contracts | Reuse matching contracts and utilities. Encryption utilities alone do not provide Windows credential storage or an interactive login flow. |
| Plugins | PluginBase, PluginManager, loader and lifecycle contracts | Evaluate for service adapter modules; WPF view registration and desktop lifecycle integration remain application responsibilities. |
| Data | FreeSql configuration and repositories | Local SQLite is registered through Asgard; the matching provider and transactional schema initialization are installed. |
| Redis, RabbitMQ and distributed locking | Core implementations and registration methods | Available but deferred until a concrete desktop workflow requires direct access. |

## Desktop boundaries

`Asgard.Core` is a net10.0 library and supports independent service containers.
Hermod can retain WPF Application as its entry point. The ASP.NET Core Yggdrasil
host is not required to consume core services. A Generic Host remains an option
for lifecycle orchestration, not a replacement for Asgard configuration or logging.

Core does not provide WPF MVVM, window chrome, docking, tab navigation, theme
resources or UI localization. Those decisions remain open. Do not treat a backend
plugin architecture as a ready-made desktop presentation framework.

The locally cached `Asgard.Heimdall.Client` 0.1.0-preview.1 SDK must be evaluated
against Hermod's intended workflows before implementing custom service clients.
Its README describes a local prerelease, service credentials and read-only
directory APIs, not WPF interactive login or management CRUD. Publication and
deployed protocol compatibility are not established by this assessment. It is
not installed in Hermod.

## Next implementation slice

1. Extend the existing WPF service lifecycle as additional Asgard modules are selected.
2. Initialize per-user logging alongside the existing Asgard configuration and SQLite storage.
3. Implement the Pencil application shell and choose only the missing presentation dependencies.
4. Evaluate service client contracts, authentication and adapter module boundaries.

## Validation

- Release build: passed, zero warnings and errors.
- Formatting verification: passed.
- The original dependency assessment introduced no runtime registration. Local storage
  has since been integrated into startup and shutdown; its verification is documented
  in the [local storage guide](local-storage.md).
