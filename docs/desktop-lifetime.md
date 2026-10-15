# Desktop lifetime and notification area

Hermod has one primary process, main window and notification icon in each Windows
user login session. Ownership is acquired before DI registration, SQLite, provider
descriptors or views. A dedicated thread holds the scoped kernel mutex throughout
cleanup. A second launch grants foreground permission, sends a fixed activation
request through a current-user named pipe, and exits without loading configuration
or initializing storage. The existing instance restores its window and shows a
localized information notice that Hermod is already running. Repeated requests
reuse the current notice; an active business modal keeps its inputs and receives
the notice only after it closes. The listener additionally verifies the impersonated user
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

The main window's close button and Alt+F4 ask whether to hide to the notification
area or exit Hermod. Asking is enabled by default. Cancel, Escape and closing the
question leave all windows and preferences unchanged. Unless “Do not ask again”
is checked when an action is selected, every close asks again. A remembered choice
atomically persists both the action and disabled asking in SQLite `app_setting`
under `ui.mainWindow.close`, using stable `hide`/`exit` IDs. Missing, malformed or
unknown settings default to asking with hide as the default action.

General settings can change “Ask when closing the main window” and the default
action independently. Changes affect the next close immediately and survive restart.
The close tooltip and accessibility name follow asking, action and tray availability.
An unavailable tray disables hiding and explains why in the question; a saved hide
action also asks in that situation rather than leaving an inaccessible application.
Repeated closes and existing modal/preflight operations cannot stack questions.

Hiding removes only the main window's taskbar entry and keeps synchronization,
connections and work windows alive. Normal minimization retains the taskbar entry.
The first hide immediately persists `ui.tray.hideHintShown` and requests one system
balloon. Suppressed Windows notifications do not cause repeat balloons.

The native tray uses one deterministic GUID per executable path, version-four callbacks and Windows notification
area theme icons. Left click, double click and keyboard activation show/restore the
main window. The menu has only show/hide and exit actions, with visible localized
labels and a separator, with no heading or initially selected item. Actual mouse
hover adds a background; Tab/arrow navigation adds a keyboard focus outline and
Enter executes the focused action. Escape and clicking outside close the menu.
Its arrow follows the actual `Shell_NotifyIconGetRect` rectangle, native window DPI
and monitor work area. Explicit tray hide/exit actions bypass the close question.
`TaskbarCreated` and periodic health checks restore the same GUID after Explorer
rebuilds its notification area. Registration failure restores the main window and
taskbar entry and gives a localized warning headed "Notification area icon unavailable",
explaining that the application remains usable; it cannot silently execute hiding.
The GUID includes the executable path because Windows binds notification identities
to that path. This also avoids the legacy GUID previously claimed by a test host.
Native tests always pass isolated random GUIDs and target their own message HWND;
they never register, delete or restore the production identity. No Windows notification
registry settings are cleared or edited by the application.

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
It skips the close question and interactive confirmation and gives cleanup a four-second Dispatcher budget,
then allows the original OS request to continue. Windows may terminate a process
whose external resource cleanup exceeds that budget. Tests exercise this boundary
without logging off or restarting the workstation.

## Verification

Use Release builds and run these sequentially on Windows with Explorer available.
These tests create real windows and can activate them even when screenshots use
transparent windows. Run desktop suites only after the user explicitly confirms
the desktop is available; stop them immediately when the user resumes computer use.
The native and full-host tests skip if a user Hermod process is already running, to
avoid interfering with its desktop interaction. Do not run these suites concurrently with
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
  UI Automation, cancels the first close question, hides without remembering and is
  asked again, remembers hiding, proves the secondary's database is never created,
  cancels and accepts tray exit, and restarts with the saved close behavior.
  All native/UIA operations target only the test PID; no remote server is used.
- `MainWindowCloseChoiceTests` and `MainWindowClosePreferenceTests`: real dialogs,
  immediate General settings, all cancellation paths, work-window preservation,
  tray failure, system ending and malformed SQLite values. Exit choices retain the
  existing global preflight and module draft protection.
- `TrayMenuInteractionTests`: default cases check ordinary appearance, native Tab,
  arrows and Enter, and no initial selection without moving the desktop pointer.
  Physical-pointer cases additionally verify actual hover and capture all three
  states in both languages and themes. They restore the pointer before closing the
  menu and require explicit user authorization plus
  `HERMOD_RUN_DESKTOP_POINTER_TESTS=1`. Native/UIA suites run serially.
- `MessageDialogPresentationTests`, `RepeatedLaunchNoticeTests`, `TrayIconIdentityTests`:
  offscreen bilingual warning/information content, repeated activation coalescing,
  preservation of active modal inputs, suppression during exit and deterministic
  path-specific identities, without creating a desktop window or registering an icon.
- `WindowsTrayIconTests`: isolated test GUID registration and rectangle retrieval, deletion
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

Workspace startup is coordinated once per page before activation. A connection page
that has never connected is removed and released after failed or cancelled startup;
its safe error is shown in the main workbench. Closed pages and application exit
cannot be reactivated by late initialization. Established connection loss retains
the page and local drafts. The connection overlay offers window close, edit, retry
and viewing already loaded content. Explicit MySQL/Redis reconnect releases old
sessions, retains document and value drafts, and never replays SQL or writes.
