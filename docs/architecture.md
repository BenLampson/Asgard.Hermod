# Module architecture

Hermod is a Windows management terminal built with C# 14, .NET 10 and WPF.
SSH, MySQL, PostgreSQL and Redis have independent hosting-brand metadata. Their provider IDs
continue to identify connection methods or topology; changing a brand never changes
protocol settings, credentials or active sessions. The title pencil edits the name,
hosting provider and tags together. The creation wizard offers the same brand choice
on confirmation. List icons and provider filters use the saved brand, defaulting to
self-hosted for existing instances. Object-storage providers retain their protocol-specific
connection forms and tests.

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
| `Asgard.Hermod.Ssh` | Verified SSH transport, PTY, SFTP, bounded exec, Linux monitoring and loopback forwarding | Shared contracts, Tmds.Ssh, DI abstractions |
| `Asgard.Hermod.Ssh.UI` | SSH configuration, terminal/files/forwarding workspace, monitoring and collapsible Agent | Shared UI, SSH protocol module, Agent module |
| `Asgard.Hermod.Redis` | Redis 7+ topology discovery, verified TLS and routed TCP transport, bounded reads and guarded writes | Shared contracts, SSH forwarding contract, StackExchange.Redis |
| `Asgard.Hermod.Redis.UI` | Redis connection forms, lazy key trees, per-key tabs and value editors | Shared UI, Redis protocol module |
| `Asgard.Hermod.MySql` | Verified MySQL connections, object metadata, bounded SQL results and transactional row updates | Shared contracts, SSH forwarding contract, MySqlConnector |
| `Asgard.Hermod.MySql.UI` | Connection forms, lazy object trees, SQL tabs, inline editing and field windows | Shared UI, MySQL protocol module |
| `Asgard.Hermod.PostgreSql` | PostgreSQL connections, database/schema metadata, bounded SQL results, estimated plans and transactional table updates | Shared contracts, SSH forwarding contract, Npgsql |
| `Asgard.Hermod.PostgreSql.UI` | PostgreSQL connection forms, focused database workspaces, SQL tabs and field editing | Shared UI, PostgreSQL protocol module, Agent module |
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

The main window uses fixed Overview, All instances, MySQL, PostgreSQL, Redis, SSH and object
storage navigation above a configuration table. Type icons, safe connection
addresses, provider identities and tags have separate columns. Tooltips and
accessibility labels preserve names at narrow sizes. The creation wizard uses one outer scroll viewer for its body; type and
provider card lists do not intercept the mouse wheel with nested scroll viewers.
Save notifications and creation callbacks update navigation by stable instance
ID, preserving the existing item and preventing duplicate rows.

Every connection flow starts with a separate provider, method or topology page.
Selecting an option stays on that page; Next opens connection information. The
information page never embeds or collapses the selection cards. SSH defaults to
direct connection, retains common fields when changing its route, and invalidates
the verified test. Its create and edit flows show four steps: connection method,
connection information, connection test and confirmation. Automatic save location
appears on confirmation. Step identities and visible sequence numbers are separate.

Instance creation begins with the independent `InstanceTypePicker`, which lays
out registered types in two columns. This entrance has no steps or total count.
Selecting a type opens its connection flow; returning from the first flow step
clears the previous connection fields, name and test result before showing the
type entrance again. Editing retains the saved type and permits selecting another
registered provider within that type. Switching providers invalidates the connection
test and uses the selected provider's fields. Cloud credentials are cleared; types
may explicitly retain common fields when changing connection methods.

`InstanceWizard` is a shared presentation container with a caller-supplied title,
description, step collection and content. It does not prescribe five steps or
perform navigation or validation. The storage connection flow is its first
consumer: `InstanceConnectionWizardContent` renders provider-declared fields,
connection tests and confirmation, while `NewInstanceViewModel` owns the guarded
connection workflow and persistence. Future type-specific views can reuse the
same picker and container with their own step collections and content without
putting their protocol behavior into these shared controls.

Every connection form uses `InstanceConfigurationEditorViewModel` in shared UI:
the generic storage form and the MySQL, Redis and SSH adapters share its field
projection, visibility tracking and attach/detach lifecycle. Modules declare pane
membership and update their own summaries through hooks. The editor exposes fixed
read-only observable collections and applies member changes incrementally without
Reset notifications. Typing, validation and status updates never replace an
ItemsSource or rebuild the field editors. New connection forms must reuse this
editor rather than implement field-list refresh or subscriptions themselves.

The connection wizard uses its current monitor's work area and DPI when it opens
or changes provider-specific size. It centers on its owner while keeping the
caption and action footer inside the work area. A modal failure cancels pending
work and closes the wizard before the shell displays the safe error, including
failures during a save. Connection editors retain their field collection during
ordinary value and validation updates, so native selection and typing do not
rebuild the control currently handling input.

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

`WorkspaceHeader` provides a 44-DIP single-line independent window caption with module-owned
`IdentityContent`, `StatusContent` and `ActionsContent` slots and fixed window controls.
The window host transfers the same header element out of the module body, setting its
data context before detachment to preserve two-way selections. All five workspace kinds
use it; ordinary pages have a live-bound title fallback. All workspace kinds use the same
read-only `WorkspaceIdentity`: a 22-DIP icon, 13-DIP name and 11-DIP safe address. The
address yields in compact captions. The optional environment badge is 18 DIP high;
normal captions do not repeat a connected/running badge. Failure provides a compact
reconnect action in the same caption. Read-only names and empty caption areas remain
draggable, with actions explicitly hit-testable in WindowChrome. Redis database selection
and key creation occupy the body toolbar; name and configuration editing belong to the main window.
The main window title bar is unchanged.

`WorkspacePageLayout` provides the shared content layout: a toolbar with a 56-pixel minimum height,
optional notification, fill-sized body, 300-pixel details panel and 28-pixel status
bar. Below 760 pixels the details overlay the body rather than reducing its width.
SSH supplies only slot content and terminal actions. The main shell owns instance
navigation and themes; the desktop workspace host opens actual instance pages in
independent windows and retains the existing page registry and cleanup contracts.
Module views must not recreate window chrome or duplicate the shared details layout.

SSH exposes terminal tools, monitoring and Agent through its shared workspace caption. The shared wizard
permits provider changes within the saved type: SSH permits route changes on the separate
method-selection page and retains common target fields. Types independently declare
their save-location step. Editing preserves the instance ID, protected configuration storage and current
connection; a verified save applies when the user explicitly reconnects.

SSH's primary body switches between terminal, SFTP files and loopback forwarding.
Its compact monitoring notification samples Linux through separate read-only exec
channels; detailed monitoring occupies a collapsible bottom region. The same
verified transport supplies these channels without injecting terminal input.
The shared `WorkspaceSplitView` owns native resizable tool columns. SSH and
SQL Agent entries open independent `AgentChatWindow` instances keyed by their
originating console or database Session. Closing the chat hides it and preserves
its position, task, draft and bounded conversation history. Source closure revokes
operations while retaining readable evidence. Container log readers attach an
explicit frozen snapshot or selection and never execute tools.
SSH, MySQL and PostgreSQL compose the same `AgentPanel`, `AgentMessageView`
and `AgentCodeSuggestion` controls. Enter sends and Shift+Enter inserts a newline;
IME composition does not submit. SQL scope, result sharing and execution, and SSH
selection, diagnostics and connection generation checks remain in their modules.
Inline approval cards freeze the full command and target, are single use and
become invalid on cancellation, source changes or reconnection. Cancellation
after dispatch records an unknown outcome without retrying the operation.

Model profiles belong to the active workspace snapshot. SQLite stores the entire
profile collection under `agent.models` using Windows user protection; corrupt
ciphertext fails closed without replacing it with an empty collection. Profile
changes participate in workspace compare-and-swap and owner-switch transactions.
Cloud configuration uses a separate AES-GCM envelope bound to account and model
purpose, with the existing sync key and an independent nonce. Server schema 3
stores only this envelope; the immutable password-check envelope is unchanged.
A missing upload field preserves existing models for old clients, while an
encrypted empty collection propagates explicit deletion. Clients require the
server to acknowledge the exact envelope before applying the returned snapshot.
Three-way merge treats endpoint, model and key as one connection; conflict
previews mask credentials and deleted defaults cannot resurrect deleted profiles.
`AgentModelCatalog` migrates the legacy address/model preference once, then
tracks the current account workspace. `AgentModelSession` pins a profile per
originating Session; changing the default only seeds future Sessions. A task
freezes endpoint, model, key and display identity for its entire context/tool
loop. Changing or deleting that profile, closing the source, or changing account
cancels the frozen task without replay. Busy selection requires an inline stop
confirmation and waits for the source task to exit before switching.
The settings manager uses the 1020x800 frame with four primary navigation entries;
existing tag/storage/file-opening/about settings remain reachable from General.
The 660x800 profile editor never fills a saved key into its input. A fixed-length
placeholder indicates a retained key, changing endpoint discards that retention,
and explicit key clearing commits immediately. Each edit invalidates a real,
cancellable minimal connection test; stale replies cannot restore its status.
Old editors compare both original profile and owner epoch before saving.

SQL adds a Manual/Automatic selector in the shared composer's options slot.
Manual remains the default and retains insertion and explicit execution
confirmation. Automatic enables at most four read-only query tool calls per
task, feeding bounded result samples back into the model. Only the explicitly
selected database in the current session scope is eligible. Each query uses a
fresh connection and a server-enforced read-only transaction, with no commit
and no effect on an existing editor transaction. Results appear as read-only
grids and tool records; cancellation, target changes and unknown outcomes stop
the task. Writes and DDL remain fenced suggestions requiring user confirmation.
Automated tools reject transaction control, executable comments, multiple
statements and file operations; unsupported SQL stays on the manual path.
SSH uses a separate bounded `SshAgentConversation`, with explicitly attached
terminal selections, fixed automatic diagnostics and confirmed command actions.
The Agent module owns the bounded function-tool loop and diagnostic templates;
the SSH UI owns approval, connection-generation checks and exec dispatch.
Results return to the model within the current task, enabling iterative analysis.
Agent replies in SSH, MySQL and PostgreSQL use the shared `MarkdownView`, which
maps Markdig syntax into selectable native WPF documents. Headings, tables,
lists, emphasis and code blocks follow the current theme and available width.
User messages and tool output remain literal text. HTML, image loading and link
navigation are disabled; rendering never dispatches commands. Shell and SQL
suggestions retain their separate insertion and confirmation actions.
Reconnection invalidates old
suggestions and transport-bound tasks. See [SSH](ssh.md) for lifecycle and limits.

`WorkspaceTools` is a shared filtered popup with observable `WorkspaceTool`
entries. Modules provide stable IDs, localized titles/groups, icons and existing
commands; the control handles scrolling, Escape and focus restoration. SSH
registers output search, font size, local display clearing and connection details.
`Ctrl+Shift+F` opens output search; `Ctrl+F` continues to reach the remote terminal.
Compact toolbars preserve Edit and Tools and move secondary actions into these
shared entry points. The tools never inject shell commands.

Redis reuses the five-step connection flow, shared field editors and protected
configuration storage. Standalone, Sentinel and Cluster are independent provider
IDs. Sentinel discovery has separate authentication and TLS settings from data
nodes. Direct and SOCKS5/HTTP CONNECT routes support every topology; SSH forwarding
is standalone-only and uses the SSH module's existing host trust and credentials.
Custom CA trust checks certificate chains, validity, usage and hostnames; it does
not require a private CA to publish online revocation information. Windows client
certificates use temporary user key containers released with the connection.

Redis key browsing uses bounded SCAN batches and independent node/prefix cursors.
Only expanded prefixes load children; values are read after opening a key. Empty
SCAN pages retain their cursor. The public `WorkspaceTreeView`, `WorkspaceTabStrip`,
`SectionSelector` and `CodeEditor` controls provide native keyboard navigation,
shared theme resources and the Pencil workspace layout. Key tabs are identified
by database and raw key bytes, measure key headers at the current font and DPI, abbreviate their middle when
space is limited while retaining a distinguishing suffix, and retain independent drafts and view state. Redis does not supply a
key creation timestamp, so the metadata view does not invent one.

Editable values require a complete bounded server baseline. Lua compare-and-save
preserves absolute expiry and detects conflicts; hash writes compare only changed
fields, list writes guard index drift, and streams append without trimming old
records. A dispatched write with an unknown outcome is never replayed automatically.
Closing a key, instance or application confirms unsaved drafts. Read-only ACL
fallback permits known-key inspection without exposing credentials or raw SDK
errors. Larger or incomplete results remain previews.

The Redis content page always remains read-only. Creating keys and editing values
use separate shared dialogs; failed writes retain the open dialog and its inputs.
Creation supports all six types, lossless text/JSON/Hex/Base64 string formats,
multi-row collection input, and permanent or timed expiry. A create-if-missing
Lua script validates write and expiry permissions before atomically creating the
value and initial TTL; it never deletes an existing key. Successful creation opens
its key tab and reveals only that known prefix path in the lazy tree.

The labeled database selector shows `DB number (key count)` from primary-node
DBSIZE replies, aggregating primaries in Cluster. Unavailable counts are explicit
and never replaced with invented zeroes. Count refresh updates existing option
objects, preserving database selection and tree cursors. Shared table styles and
selector containers cover both themes without detached-container binding errors.
Redis tree clicks stop at their nearest row so selecting a key does not toggle an
ancestor. The focused row uses a solid theme border without default dotted adorners.
Key tabs and subordinate selectors use local rounded selection styles on the shared
surface. Instance rename commands inherit an explicit metadata scope in both the
shell and independent workspace windows, retaining the same page, session and drafts.
The main title bar theme toggle uses the shell's application-wide theme service.

MySQL table and view tabs keep an independent page size and temporary WHERE condition.
The condition editor is hidden initially and opens through Filter. Remove filter
confirms removal first, then respects existing draft decisions and hides the editor.
Sizes 50/100/200/500 reach the protocol reader; one look-ahead row determines HasMore.
Filters accept catalog column comparisons, LIKE, IN, IS NULL, AND/OR, NOT and parentheses.
Every value is parameterized, identifiers are quoted, and statements, subqueries and
functions are rejected. Paging/filter changes reuse save/discard/cancel for cell drafts.
SQL documents retain independent execution databases and expose current/selection and
whole-document execution through the shared split Run button. Database captions, Session strips, document tabs,
SQL editor and output use the scoped tokens from `DBWindow.pen`. `DatabaseSqlWorkspace`
provides the 48-DIP toolbar, a resizable editor and one mutually exclusive output slot
for results, safe messages, complete plan text and recent execution history. Plans retain
the previous query results and their request duration. Restoring history only restores
SQL after the existing save/discard/cancel guard; it never executes it.

MySQL and PostgreSQL share database-specific Session, document and output tab styles;
Redis retains the generic internal tab resources. Middle-click
passes the pointed tab to the existing guarded close command. Shared grid scrolling
uses Shift+wheel only on the grid viewport and preserves editors and ordinary wheel
handling. Empty tool groups do not reserve an extra line. The tree container intercepts
its own bring-into-view request before the [WPF base class handler](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/TreeViewItem.cs),
then brings only its row into view, preserving expanded branches.

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
services.AddRedis();
services.AddMySql();
services.AddPostgreSql();
services.AddSingleton<ITerminalSessionFactory, LocalTerminalSessionFactory>();
services.AddHermodUi(() => MainWindow, Resources);
services.AddObjectStorageUi(() => MainWindow);
services.AddSshUi(() => MainWindow);
services.AddRedisUi(() => MainWindow);
services.AddMySqlUi(() => MainWindow);
services.AddPostgreSqlUi(() => MainWindow);
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
Each launch opens Overview and does not restore previous work windows.
Overview shows five type counts and a six-row recent-access table. All instances
and type pages use the same configuration catalog, search, tag and provider
filters, sorting and pagination. Selection does not open a connection; double-click,
Enter or the row menu opens or activates its independent work window.
Settings → About owns the version, author, open-source license and
repository link; the complete information is visible without expanding a panel.
Local access history remains persisted separately from open windows. It retains
each instance's last UTC access time; only the Overview projection is limited to
six rows. All-instance sorting uses the complete history. Existing history needs
no migration, and deleted instances are pruned. See [the main-window guide](main-window.md)
for public navigation controls, table behavior and verification.
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

Instance rows offer independent rename and tag editing actions. Tag management
also offers a name-only action. These shared dialogs retain failed drafts and
cancel without writes. Metadata transactions update only names or associations;
they never decrypt, validate, test, or rewrite connection credentials. Stable
instance and tag IDs, sessions and internal drafts remain. Open instance titles
and recent navigation refresh immediately. Tag renaming validates within the
write transaction and retains the current parent and color.

Keyboard navigation skips presentation wrappers and list containers around
operable cards. Segmented selectors and module tab strips use one Tab entry
with native arrow-key selection; modal editors cycle within the window and
restore focus to their entry point on close. Interactive controls retain their
visible focus treatment. WPF test hosts use the desktop application's selection
rendering switch and verify rendered text above opaque selection backgrounds.
Module tab title hints use the WPF tooltip service with a bounded hover duration;
focus does not manually open a popup. Complete titles remain in automation names.
SSH, object storage, MySQL and Redis share `WorkspaceConnectionOverlay` and its
presentation state for the first connection, failures and unexpected disconnections.
`WorkspacePageLayout` reserves an overlay slot spanning the existing page rows;
loading and failure do not shift the body. Modules own cancellation, session
cleanup and retries, and provide safe localized errors. Closing a failure keeps
a short disconnected badge, and retry resets the error. Underlying controls pause
while the overlay is visible; tree templates retain the current theme when disabled.
Unexpected disconnections retain existing terminal output, object lists, query
results and drafts. Dismissing an interruption does not reconnect or repeatedly
show the same failure. Retry is explicit and never replays writes. MySQL reports
physical session loss, including independent query sessions, and probes the idle
metadata session every 15 seconds. Redis reports transport failures and authentication
expiry; ordinary object permissions remain operation errors. Stateless object storage
reports credential expiry and network failures when an actual request detects them,
including detail, scan and transfer requests. Provider responses are mapped to stable
safe resource errors before reaching the shared popup.

Instance type icons share one mapping across the picker, navigation and workspace:
MySQL uses `database`, storage uses `box`, Redis uses `layers`, and SSH uses
`terminal`. Their silhouettes remain distinct in monochrome. The shared
`InstanceNameButton` places a visible pencil beside workspace titles and uses the
shell metadata command; the main table's Rename dialog saves name and tags together. Renaming
preserves stable IDs, connections and drafts. MySQL selects an adjacent internal
tab before removal, and shows a localized query/object guide after the last tab
closes while keeping its metadata connection and object tree.

The desktop host enables independent workspace windows through `WorkspaceWindowHost`.
Actual instance navigation, recent items, local terminals and new sessions share the
existing workspace page registry and module templates. Each window has its own taskbar
entry and no main-window Owner; closing it uses the existing module confirmation and
awaits page cleanup. Reopening an instance activates the same page and window. The
main shell retains its overview, search, configuration and account navigation.
Module dialogs resolve the active work window as their owner.

`MainNavigationTabs` owns the main-window navigation layout and selection styling.
Each localized label reserves the maximum space measured by WPF for its normal
and semibold font weights. Only one label is visible; the measuring labels remain
hidden. Selecting a tab changes its font weight and background without moving or
resizing any tab, icon or label. Main-window pages do not calculate tab widths.
Regression tests compare every tab's actual bounds across selections, languages,
themes, layout DPI values and wide/narrow main-window layouts.

## Database script Session scope

PostgreSQL and MySQL share `DatabaseSessionScope`, database selection and removal
confirmations in the presentation module. `DatabaseIdentity` pairs the stable
instance ID with the original database name. Scope additions are validated as a
batch; a foreign instance rejects the entire operation. Existing scopes cannot
become empty. Removal confirmations carry immutable revision snapshots and are
revalidated after asynchronous draft and transaction guards.

The instance window presents `DatabaseSessionPicker` before a multi-database Session
is created. New Session reuses this picker in the same window and cancellation returns
to the previous Session with its draft intact. Switching SQL and object documents within
the same scope preserves the tree data source, selected row and scroll position.

`DatabaseScopeEditor` is the single scope-management popover. Its checked rows form
a local draft, with included, pending-addition and pending-removal states. Canceling
never changes the Session. Applying freezes the original scope revision and selected
identities. Both engines collect every removal confirmation and object/transaction
close preflight before committing any scope change. A later cancellation leaves the
entire original scope intact. The final batch preserves an available execution target;
removing that target requires an explicit replacement when several candidates remain.
Stale drafts, foreign instances and empty scopes are rejected.

A script Session owns its object documents, SQL execution history and AI conversation.
Scope is distinct from the physical connection's execution target. Unique candidates
are fixed; ambiguous choices are left to the user. Tree navigation and adding scope
never silently switch the target. PostgreSQL still opens one physical database per
connection and does not emulate cross-database SQL. Model metadata uses bounded,
same-instance reads; scope revision changes invalidate execution suggestions and
exclude old-scope conversation from subsequent model requests.

## Execution plan reading

MySQL and PostgreSQL parse already returned plan evidence in their protocol modules.
The shared immutable `DatabaseExecutionPlan` keeps the complete original text, SQL,
database and client request duration. Traditional MySQL EXPLAIN remains a field table;
only native ANALYZE indentation or PostgreSQL JSON parent-child relationships create
a tree. Unsupported formats and ambiguous duplicate JSON fields fall back to raw text.
Parsers retain server numeric precision and distinguish missing values from zero.
Estimated costs are never labeled as elapsed milliseconds. Actual per-loop inclusive
times are not added or converted into exclusive timings.

`DatabasePlanReader` presents that snapshot in the existing SQL output slot, using
the database design's 40-pixel toolbar, 68-pixel rows and 296-pixel details panel.
Switching modes, expanding nodes, searching, locating evidence and opening details
never issue database requests. Narrow layouts initially collapse details. Raw copy
and export always use the full original text. Running a plan does not replace query
results or their elapsed time; a subsequent failed request retains the previous plan
with an explicit previous-result label. Offscreen WPF tests measure actual geometry
and bindings without constructing desktop windows.

## SSH instance and console ownership

One SSH window owns one authenticated transport. `ISshConsoleConnection` owns only
one remote PTY; disposing it cannot dispose the shared client, SFTP, forwarding or
other Shell channels. The factory establishes the transport and first Shell before
the main shell opens the work window. Further consoles use that same transport.
The SDK's transport-disconnected token is observed independently of Shell EOF.

Each `SshConsoleViewModel` owns its terminal buffer, bounded input queue, I/O loops
and assistant. Switching consoles does not replay input or reset Shell state.
Closing the last console is allowed and leaves instance tools running. Whole-window
close confirmation states the counts of consoles, forwards and active transfers.
Transport loss stops every old PTY and revokes tool permissions while retaining
all old buffers for reading. Explicit reconnect creates one new Shell and does not
attempt to restore old processes, working directories or commands.

The SSH layout uses a 44-pixel caption, 52-pixel instance-tool rail, independently
resizable toolboxes and a 38-pixel console strip. Monitoring occupies a separate
580-pixel region including its 8-pixel divider. A weak per-buffer view-state cache
preserves selection, local search, scroll position and font settings. Remote terminal
resizing moves removed top rows into local history, while local ConPTY retains its
existing system-reflow behavior. Explicit terminal line height and PTY grid sizing
use the same DIP metrics. Offscreen layout tests cover both languages and themes,
toolbox expansion, multiple consoles and the zero-console state.
