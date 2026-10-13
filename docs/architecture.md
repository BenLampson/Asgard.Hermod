# Module architecture

Hermod is a Windows management terminal built with C# 14, .NET 10 and WPF.
Each capability is compiled as a separate project. `Asgard.Hermod` is the
composition root: it chooses implementations, loads resources, initializes local
storage and owns application startup and shutdown.

## Project responsibilities

| Project | Responsibility | Direct implementation dependencies |
| --- | --- | --- |
| `Asgard.Hermod` | Application entry point, main window, module composition and lifetime | Selected modules, DI container |
| `Asgard.Hermod.Abstractions` | Instance, configuration, tag and workspace contracts; shared models; secret protection contract; shared localization resources | None |
| `Asgard.Hermod.Core` | Instance provider registry and configuration-backed workspace data source | Shared contracts, DI abstractions |
| `Asgard.Hermod.UI` | WPF shell, shared controls, configuration and tag dialogs, workspace factory | Shared contracts, terminal, DI abstractions |
| `Asgard.Hermod.Persistence.Sqlite` | SQLite configuration, migrations, repository, local instance and tag services | Shared contracts, Asgard.Core, FreeSql SQLite |
| `Asgard.Hermod.Security.Windows` | Windows current-user DPAPI protection | Shared contracts, ProtectedData, DI abstractions |
| `Asgard.Hermod.Identity` | Heimdall desktop login, protected sessions, automatic configuration synchronization and conflict merging | Shared contracts, official Heimdall SDK |
| `Asgard.Hermod.Server` | Independent sync API plugin, online identity checks and encrypted server persistence | Shared contracts, identity module, Asgard PluginSdk, Microsoft.Data.Sqlite |
| `Asgard.Hermod.Server.Starter` | Yggdrasil API composition and process lifetime | Sync API plugin |
| `Asgard.Hermod.WinTerminal` | VT engine, rendering, WPF terminal control, ConPTY connections and local session adapter | Windows desktop framework |
| `Asgard.Hermod.Ssh` | SSH authentication, verified host identity, jump routing and SOCKS5/HTTP CONNECT transport | Shared contracts, Tmds.Ssh, DI abstractions |
| `Asgard.Hermod.Ssh.UI` | SSH configuration content, host verification and independent terminal workspace pages | Shared UI, SSH protocol module |
| `Asgard.Hermod.ObjectStorage.Abstractions` | Object storage models, service and client contracts | Shared contracts |
| `Asgard.Hermod.ObjectStorage` | Resource client factory, connection probe, transfers, previews and deletion policies | Object storage contracts, DI abstractions |
| `Asgard.Hermod.ObjectStorage.UI` | Object browser, feature dialogs, view models and page template | Shared UI, object storage contracts, DI abstractions |
| `Asgard.Hermod.Providers.AliyunOss` | OSS signing, SDK clients, multipart upload and provider-specific HTTP policies | Object storage module, AlibabaCloud.OSS.V2, HTTP resilience |
| `Asgard.Hermod.Providers.S3` | Shared S3 SDK client, streaming, multipart transfers, safe errors and narrow protocol adapters; generic S3-compatible profile | Object storage module, AWSSDK.S3, HTTP resilience |
| `Asgard.Hermod.Providers.AwsS3` | AWS endpoint profile and independent registration | S3 provider module |
| `Asgard.Hermod.Providers.TencentCos` | COS endpoint, bucket-list dialect and conditional-write profile | S3 provider module |
| `Asgard.Hermod.Providers.VolcengineTos` | TOS S3 endpoint and conditional-write profile | S3 provider module |
| `Asgard.Hermod.Providers.Minio` | MinIO custom endpoint and addressing profile | S3 provider module |

Shared contracts, core services, SQLite persistence, and object storage contracts,
operations and all storage connectors target `net10.0` without WPF. Presentation,
terminal and DPAPI projects target `net10.0-windows`. No module references the
application executable. Shared UI does not reference object storage, persistence
or cloud connectors.

Password fields use the shared UI `SecretInput` control. It masks values by
default and provides a localized eye button to show or hide the password.
Both editors preserve the same two-way binding and maximum length. Hiding the
password, hiding or closing the window, and reapplying the template clear the
plain-text editor; its undo history is disabled. Visibility is never persisted.

Instance navigation puts the registered type icon before the instance name and
the provider brand after it. The collapsed rail retains the type icon with small
brand and cloud status marks. Tooltips and accessibility labels identify each
role. The creation wizard uses one outer scroll viewer for its body; type and
provider card lists do not intercept the mouse wheel with nested scroll viewers.
Save notifications and creation callbacks update navigation by stable instance
ID, preserving the existing item and preventing duplicate rows.

Instance creation begins with the independent `InstanceTypePicker`, which lays
out registered types in two columns. This entrance has no steps or total count.
Selecting a type opens its connection flow; returning from the first flow step
clears the previous connection fields, name and test result before showing the
type entrance again. Editing retains the saved type and provider.

`InstanceWizard` is a shared presentation container with a caller-supplied title,
description, step collection and content. It does not prescribe five steps or
perform navigation or validation. The storage connection flow is its first
consumer: `InstanceConnectionWizardContent` renders provider-declared fields,
connection tests and confirmation, while `NewInstanceViewModel` owns the guarded
connection workflow and persistence. Future type-specific views can reuse the
same picker and container with their own step collections and content without
putting their protocol behavior into these shared controls.

Instance tagging is shared by every connection flow and appears only on its
final confirmation step. The optional selector opens an independent searchable
checkbox draft; selecting a parent does not select its descendants. Confirming
the popup updates the wizard, while cancelling, Escape, leaving the step or
clicking outside discards unconfirmed changes. Confirmed tags survive backward
navigation and save failures without invalidating the connection test. Editing
loads stable tag IDs directly from the configuration service. Configuration and
tag associations commit in one SQLite transaction; explicit replacement can
clear all tags, while older connection-only updates preserve their associations.

The editable Pencil source separates public connection components and rules
in region 04 from storage connection screens and states in region 05A. Storage
and other type-specific regions reference the common container; browsing,
transfers and previews remain in the object storage regions.

`SegmentedSelector` implements the shared Pencil `ui/segmented` control. Both
provider-declared choice fields and SSH target/jump/proxy pane selection reference
it; modules supply stable IDs and localized labels instead of copying selection
templates. It retains native ListBox keyboard navigation without an inner scroll
viewer.

`WorkspacePageLayout` provides the shared content layout: a 64-pixel toolbar,
optional notification, fill-sized body, 300-pixel details panel and 28-pixel status
bar. Below 760 pixels the details overlay the body rather than reducing its width.
SSH supplies only slot content and terminal actions. The main shell continues to
own instance navigation, themes and outer tabs; its content host owns page reuse
and cleanup. Module views must not recreate those shell elements or duplicate the
shared details layout.

SSH exposes configuration editing only in its Content toolbar. The shared wizard
uses type-declared edit capabilities: SSH permits route changes and has four edit
steps, while other types retain their existing provider lock and save-location
step. Editing preserves the instance ID, encrypted credentials and current
connection; a verified save applies when the user explicitly reconnects.

`WorkspaceTools` is a shared filtered popup with observable `WorkspaceTool`
entries. Modules provide stable IDs, localized titles/groups, icons and existing
commands; the control handles scrolling, Escape and focus restoration. SSH
registers output search, font size, local display clearing and connection details.
`Ctrl+Shift+F` opens output search; `Ctrl+F` continues to reach the remote terminal.
Compact toolbars preserve Edit and Tools and move secondary actions into these
shared entry points. The tools never inject shell commands.

## Composition

The application calls the independently owned registration entry points:

```csharp
services.AddHermodCore();
services.AddWindowsSecretProtection();
services.AddLocalStorage(configuration);
services.AddHermodIdentity(identityOptions);
services.AddObjectStorage();
services.AddAliyunOss();
services.AddTencentCos();
services.AddVolcengineTos();
services.AddAwsS3();
services.AddMinio();
services.AddS3Connector<S3CompatibleStorageProvider>();
services.AddSsh();
services.AddSingleton<ITerminalSessionFactory, LocalTerminalSessionFactory>();
services.AddHermodUi(() => MainWindow, Resources);
services.AddObjectStorageUi(() => MainWindow);
services.AddSshUi(() => MainWindow);
```

`AddLocalStorage` registers SQLite and local configuration/tag services. The host
separately selects the `ISecretProtector` and `IInstanceProviderRegistry`
implementations. Storage registration does not install cloud providers, HTTP
clients, remote operations or presentation components.

`AddAliyunOss` registers the OSS instance and resource providers, plus their named
HTTP clients and resilience policies. Other providers can be registered alongside
it using the same contracts. Provider keys are indexed by instance type and
provider identity; duplicate registrations fail during startup validation.

The S3-based connectors share the official AWS SDK for signing, XML serialization,
requests and presigned links. Each vendor module owns its profile and registration.
The S3 module owns client configuration, bucket discovery, transfers, connection
probes and safe error translation in separate files. COS bucket-list field names
and COS/TOS conditional-write headers are small adapters around the SDK. These
details do not enter object browser view models, persistence or shared contracts.
Provider brand images are embedded WPF resources in shared UI; provider descriptors
carry stable icon IDs, with no UI assembly dependency. See the
[provider guide](object-storage-providers.md) and [asset sources](../src/Asgard.Hermod.UI/Assets/Providers/README.md).

Startup validates the service container, initializes the database off the UI
thread, applies the saved language preference and opens the main window. Windows
display language is the default; unsupported languages fall back to English.
Each launch opens only Home and does not save or restore the previous tabs.
Home stays open and has no close action. Other tabs, including tag pages, can
be closed. Closing the last work tab returns to Home.
The Home recent-instance history remains persisted separately from open tabs.
See [the localization guide](localization.md) for resource and extension rules.
Closing the application waits for workspace
resources to be released before disposing the service container.

The main window's 1360 × 860 logical-pixel size is a preferred startup size.
After WPF selects the startup monitor and creates the HWND, shared UI uses that
monitor's taskbar-excluded work area and the window's DPI to limit and center the
initial bounds. Small work areas also lower the window's minimum size, so the
caption and footer remain visible. The executable declares Per-Monitor V2 DPI
awareness; WPF handles scaling when the window moves between monitors.

## Adding capabilities

Desktop login and the independent API are described in the
[identity and synchronization guide](identity-and-sync.md). The public desktop
Client and confidential server Client remain separate. The API stores encrypted
configuration details with encrypted credentials; local instance editing continues through the desktop storage
module. Neither the server nor shared UI references the WPF executable.

A new instance connector references `Asgard.Hermod.Abstractions` and implements
`IInstanceProvider`, including its metadata, configuration fields and connection
check. Its project owns its SDK and registration method. The application opts
into the connector by referencing the project and calling that method.

An object storage connector additionally implements `IObjectStorageProvider` and
`IObjectStorageClient` from the object storage contracts project. It can reuse the
generic object storage module without depending on SQLite, WPF or the executable.

A feature with its own UI supplies an `IInstancePageProvider` and a resource
dictionary with a `DataTemplate` keyed by its workspace view model type. The host
merges that resource dictionary. `WorkspaceContentHost` creates and caches the
view through the template, then releases it when its page closes. The shared
shell calls the common page initialization contract and observes title/close
notifications without knowing the feature's concrete types.

## Persistence and terminal

Object browser search matches literal text anywhere in immediate file and folder
names, ignoring case. Queries containing `*` or `?` instead match the whole name:
`*` matches zero or more characters and `?` matches exactly one. For example,
`*.png` matches PNG names but excludes `image.png.bak`. Other characters are literal.
It lists the current directory with the provider delimiter
and filters metadata locally, continuing through pages without matches. Matching
pages retain the provider cursor for Load more. Search does not recurse, download
contents, or create an index; sparse matches can require many listing requests.
Changing the query or navigating cancels the previous search.

The object browser can manually scan the current prefix and all descendants to
sort direct children by modification time. A folder's date is the latest file
date anywhere below it; directory placeholder dates are ignored. The generic
object storage service lists metadata in batches of 1,000 without a delimiter,
retaining only direct child entries and folder aggregates. It does not download
file contents, persist results, or maintain a cache or index. Beyond 10,000
files, scanning pauses for an inline Continue or Cancel decision, including when
the threshold is crossed on the final page. Progress reports scanned files,
without estimating an unknown total or percentage.

Only a completed scan replaces and sorts the current list, including children
outside its originally loaded pages. Cancellation and failures retain the prior
list and pagination cursor. Refresh, search, navigation, and page closure cancel
the operation and invalidate its results. The Loaded footer counts immediate
rows separately from the recursive file count. Empty folders have no file date;
listing multiple pages reflects the data returned during the scan, rather than
a point-in-time storage snapshot.

The SQLite module retains the existing per-user database path, schema migrations
and encrypted connection parameters. Windows DPAPI is selected through the
separate credential protection module. See the [local storage guide](local-storage.md).

Core manages isolated temporary files through `ITemporaryFileStorage`, including
cross-process leases, usage snapshots and safe cleanup. Shared UI presents the
Storage settings page, while object storage pages hold a managed file lease until
viewing finishes or transfer it to the application for external opening. See the
[storage settings guide](storage-settings.md).

The terminal accepts raw output bytes, emits input bytes and grid-size changes,
and maintains screen state. Its independent session adapter owns the local
ConPTY process and terminal buffer. Cloud SDKs, connection profiles and
persistence remain outside the terminal project. See the
[terminal component guide](../src/Asgard.Hermod.WinTerminal/README.md).

## Verification

Run the solution build, format verification and test suite documented in the
repository README. Module composition tests verify local startup without cloud
providers, independent connector registration, and assembly dependency boundaries.
Workspace tests exercise feature templates, cached page reuse, lazy loading,
connection editing and resource cleanup across project boundaries.
S3 protocol tests run the real SDK against controlled HTTP responses, covering
signing, regional endpoints, pagination, conditional writes, multipart cancellation,
partial deletion failures and version-aware probe cleanup. Live cloud accounts are
not required by the test suite.
