# Main window

The main window follows the approved Overview and instance-table design. It starts
at 1360 × 860 logical pixels when the monitor permits. Overview, All instances,
MySQL, Redis, SSH and object storage are fixed navigation entries. Overview shows
actual type counts and the six most recent local accesses. Configuration tables
combine name/address search, tag filters, provider selection and recent/name
sorting. Pagination fits the available height, with at most nine rows per page.
At narrow widths the account button retains its avatar, tooltip, accessibility
name and keyboard focus while its text folds away.

`MainNavigationTabs` and `MainNavigationTabChrome` are public shared UI controls.
Their resource dictionary owns the selection template: top rounded corners,
outward bottom flares, no outline or inner scroll viewer, and the same background
as the body. The selected shape extends one physical pixel below the caption to
avoid DPI seams. Normal independent work-window title bars retain their existing
shared template and divider.

A single click selects a table row. Double-click, Enter and More → Open use the
existing workspace registry, creating one independent window per instance and
activating it on subsequent opens. More → Rename edits the name and tags in one
metadata transaction. More → Edit connection opens the existing provider editor.
Deletion is available there and requires the exact localized confirmation phrase;
it deletes only the local configuration and tag associations. The synchronized
service rechecks the original account identity inside its serialized local-change
operation before deleting. Already opened work pages retain their normal lifetime.

`InstanceSummary.ConnectionAddress` is a safe, separate projection from provider
identity. Known protocol fields yield host/port values or an HTTP(S) scheme and
server; credentials, paths, queries and fragments are excluded. Invalid or unknown
addresses remain empty. SQLite configuration loading caches this projection by
ciphertext, so metadata edits do not decrypt connection parameters again.

Local history keeps one last-access fact per instance in `workspace.recent`.
The six-row limit applies only to Overview; All instances uses complete history
for timestamps and sorting. Deleted records are pruned on catalog refresh. Access
means opening a work page and does not claim a successful remote connection.

The footer derives account and synchronization status from existing services.
Signed-out state uses the neutral local-device icon; pending, synchronizing,
conflict, failure and synchronized states retain their own localized text and
icons. Theme, language, account, tags and connection settings use the existing
services, and all display text comes from shared English/Chinese resources.

Verification uses real WPF windows and isolated SQLite data. `MainShellDesignTests`
captures Overview, All instances, type, menu, narrow and empty states in both
languages and themes. It checks a zero-scroll six-row Overview, actual pagination
buttons and the compact account button. `MainNavigationTabsTests` checks seamless
selected-tab pixels at 100%, 150% and 200% output DPI. Behavior tests cover native
double-click, menu Escape/Open, window reuse, tag drafts, history across restart,
combined metadata saving, safe address projection and guarded deletion. The actual
application process test opens a saved instance and exercises single-instance
activation, tray hiding, cancellation and confirmed exit.
