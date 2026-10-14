# Desktop lifetime and notification area

Hermod has one primary process, main window and notification icon in each Windows
user login session. Ownership is acquired before DI registration, SQLite, provider
descriptors or views. A dedicated thread holds the scoped kernel mutex throughout
cleanup. A second launch grants foreground permission, sends a fixed activation
request through a current-user named pipe, and exits without loading configuration
or initializing storage. The listener additionally verifies the impersonated user
SID and client process session. Startup requests are retained until the main window
is ready. A launch during committed exit waits for ownership to be released.

`WorkspaceWindowHost` routes instance navigation, recent items, local terminals and
new sessions to independent `WorkspaceWindow` instances. They reuse existing module
views, connections, editors and close confirmations. Each work window has its own
taskbar entry and no main-window Owner. Reopening an instance activates its existing
window. Closing the final work window leaves Hermod running. All shared and module
dialog services resolve the active work window; exit preflight supplies an explicit
owner. Main-window activation finds the effective thread modal chain, including
dialogs owned by independent work windows.

With an available tray, the main window's close button and Alt+F4 hide it and remove
its taskbar entry. Normal minimization retains the taskbar entry. Closing or hiding
does not dispose the shell, stop synchronization, disconnect sessions or close work
windows. The first hide immediately persists `ui.tray.hideHintShown` in SQLite
`app_setting` and requests one system balloon. Suppressed Windows notifications do
not cause repeat prompts. The close tooltip and accessibility name describe hiding.

The native tray uses one stable GUID, version-four callbacks and Windows notification
area theme icons. Left click, double click and keyboard activation show/restore the
main window. The menu has only show/hide and exit actions, with visible localized
labels. Its arrow follows the actual `Shell_NotifyIconGetRect` rectangle, native
window DPI and monitor work area. Escape and keyboard navigation are supported.
`TaskbarCreated` and periodic health checks restore the same GUID after Explorer
rebuilds its notification area. Registration failure restores the main window and
taskbar entry, gives a safe localized message, and restores normal close-to-exit.

Exit first confirms the impact of open/closing work sessions. Existing modal dialogs
or single-window close preflight are recalled instead of starting duplicate checks.
Navigation and page input are frozen while all pages prepare their close decisions.
No page is disposed until every page agrees. MySQL stop-query, discard and rollback
decisions are deferred in `WorkspaceClosePlan`; a later cancellation preserves earlier
drafts, running queries, transactions and connections. Explicit user-selected saves
can still take effect. After approval, Hermod rejects new activation, commits the
decisions, stops synchronization, waits for all page cleanup (including pages already
removed by a single-window close), removes the tray and releases host services, IPC
and instance ownership. Repeated exit requests cannot reenter this flow.

System logoff/shutdown does not veto Windows by setting `SessionEnding.Cancel`.
It skips interactive confirmation and gives cleanup a four-second Dispatcher budget,
then allows the original OS request to continue. Windows may terminate a process
whose external resource cleanup exceeds that budget. Tests exercise this boundary
without logging off or restarting the workstation.

## Verification

Use Release builds and run these sequentially on Windows with Explorer available.
The native and full-host tests skip if a user Hermod process is already running, to
avoid sharing its production tray GUID. Do not run these suites concurrently with
manual Hermod acceptance or another copy of the native suite.

```powershell
dotnet build Asgard.Hermod.slnx -c Release
dotnet format Asgard.Hermod.slnx --verify-no-changes
dotnet test --project tests/Asgard.Hermod.UI.Tests/Asgard.Hermod.UI.Tests.csproj -c Release
dotnet test --project tests/Asgard.Hermod.MySql.UI.Tests/Asgard.Hermod.MySql.UI.Tests.csproj -c Release
dotnet test --project tests/Asgard.Hermod.Redis.UI.Tests/Asgard.Hermod.Redis.UI.Tests.csproj -c Release
```

Focused xUnit v3 cases:

- `SingleInstanceProcessTests`: six simultaneous processes, queued activation before
  readiness, repeat activation, committed-exit handoff and crash recovery. The probe
  executable only uses isolated instance names and never starts application services.
- `ApplicationProcessTests`: launches the real desktop executable with isolated
  SQLite and `HERMOD_TEMPORARY_DIRECTORY`, opens a real PowerShell work window via
  UI Automation, closes/hides and recalls the main window, proves the secondary's
  database is never created, cancels and accepts tray exit, and restarts successfully.
  All native/UIA operations target only the test PID; no remote server is used.
- `WindowsTrayIconTests`: real GUID registration and rectangle retrieval, deletion
  and `TaskbarCreated` restoration of the test icon, removal on disposal, localized
  light/dark menus, Escape, and placement at 100/150/200% scale with negative coordinates.
- `MainWindowTrayTests`: actual navigation into work windows, main hide/restore,
  pending async single-window disposal, failure fallback, independent modal ownership
  and recall, exit cancellation, and system-session cleanup.
- `ExitPreflightTests`: actual MySQL workspace drafts/transactions and running queries
  remain intact while another close decision cancels; side effects occur only on commit.
- `TrayPreferenceTests`, `LocalizationTests`, `LanguagePreferenceTests`: real SQLite
  persistence, English/Chinese catalogs, equivalent placeholders and English fallback.

Set `HERMOD_UI_PREVIEW_DIR` to an artifact directory to export menu screenshots.
Previews and screenshots are verification artifacts and are not committed. The
environment override `HERMOD_TEMPORARY_DIRECTORY` changes only the launched process's
managed file-cache root; the default user directory remains unchanged.
