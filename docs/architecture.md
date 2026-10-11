# Architecture planning

Hermod is an open-source Windows management terminal in the Asgard ecosystem, built with C# and .NET 10+ using WPF.

English is the primary language of repository documentation. The application will support multiple languages.

The broader application architecture remains under discussion. The current solution contains the WPF application shell, the independent terminal component, local SQLite storage, and their tests.

## Local storage

The WPF application loads `app.yaml` through Asgard and registers the SQLite
provider with its service container. Startup creates the per-user database and
transactionally initializes its schema before opening the main window. Shutdown
disposes the database connection pool. See the [local storage guide](local-storage.md)
for configuration, schema versions, and persistence boundaries.

## Terminal decision

`Asgard.Hermod.WinTerminal` is a Windows-only WPF library compiled from source in
this repository. It starts from selected VirtualTerminal engine and rendering
code, with Hermod's own control, session boundary, and ConPTY host. The application
references it directly. No WebView2, xterm.js, Microsoft Terminal repackaging,
Avalonia, or upstream VirtualTerminal NuGet dependency is installed.

The terminal accepts raw output bytes, emits input bytes and grid-size changes,
and maintains screen state. Service SDKs, connection profiles, persistence, and
cloud synchronization remain outside this component. Future connectors can attach
their streams without making the renderer depend on their protocols.

See the [component guide](../src/Asgard.Hermod.WinTerminal/README.md).

## Topics for discussion

- Application structure and presentation patterns.
- Localization approach, initial languages, fallback behavior, and language selection.
- Supported services and management workflows.
- Authentication, authorization, and connection configuration.
- Testing, accessibility, and distribution.

No presentation framework, service integration, or localization mechanism has been selected yet.
