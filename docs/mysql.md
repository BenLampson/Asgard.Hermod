# MySQL

MySQL is an independent protocol module and WPF module. The application opts in
through `AddMySql()` and `AddMySqlUi()`. MySqlConnector owns the wire protocol;
shared UI owns the shell, connection wizard, field controls, tools, page layout,
tabs, table styles, code editor and child-window chrome.

## Connections

Choose direct TCP or SSH forwarding in the shared five-step wizard. Database and
SSH credentials remain separate. The default port is 3306, with 15-second connect
and 30-second command timeouts. An empty default database allows choosing one later.
The existing SQLite and DPAPI configuration services save credentials and tags;
SQL documents and result data are not automatically synchronized.

Verified TLS is the default and checks both the chain and the logical database
hostname, including through SSH forwarding. Windows trust or a custom PEM CA and
optional client PFX authentication are supported. Required encryption without
identity verification and disabled TLS are explicit choices with a warning.
SSH reuses the existing authentication and host-key trust services. Changing a
saved connection requires an explicit reconnect to affect an open workspace.

## Objects and SQL

The real object tree loads each branch independently: databases, tables, views,
procedures, functions, events, columns, indexes, foreign keys, triggers and routine
parameters. Branches use distinct type icons, bounded pages and explicit empty,
loading and retry states. Search filters loaded objects while retaining ancestors;
it does not scan the whole server. A known table can be opened by database/name
when object listing is unavailable. Object tabs are deduplicated by database,
kind and original name. Tabs show the object name; the content header and tooltip
show its database/schema context. Tree selection and keyboard focus style only
the current row, while ancestors retain their expansion indicators.
The object-tree header has refresh only. Opening an object by its known name is
available from the tree menu and does not imply creating a database object.
Empty loaded branches have no expansion button. Tab title tooltips use WPF hover
timing and disappear after three seconds; keyboard focus retains the full
accessible name without leaving a floating title over the workspace.
Structure sections separately show columns, indexes, foreign keys, triggers and
object definitions. Views and routines expose only their applicable sections.
Definitions use the full content area and remain read-only. Section changes keep
the session, loaded results and unsaved data. UI names use Inter with the explicit
Chinese fallback; protocol types and SQL use JetBrains Mono. Soft horizontal rules
replace the default metadata grid lines.

Each SQL tab has its own physical session and database context. Selecting an
object in the tree does not change a query tab's context. Ctrl+Enter executes the
selection or caret statement; Ctrl+Shift+Enter executes the complete document.
The parser respects MySQL quotes, comments, SQL_MODE and standalone DELIMITER
directives. A SQL_MODE change stops a batch before remaining statements can be
interpreted using the old mode. SQL file saving does not execute its contents.

Results retain duplicate column names by ordinal. Row numbers remain fixed;
headers and rows share native horizontal scrolling, while column widths remain
readable and resizable. Rows and columns are virtualized. A result preview retains
at most 1,000 rows or 10 MiB. Reaching the limit stops execution rather than
rewriting SQL; later statements/results are not run, and writes already executed
are not rolled back. Export includes only the current loaded rows. Table browsing
uses explicit 200-row pages without inventing an exact table row count.

Cancellation requests are not rollback guarantees. Earlier results remain after
a later statement fails. Autocommit is read from the server; transaction state is
reported as unknown when performance_schema telemetry is unavailable or disabled.
Closing an active or unknown transaction offers an explicit rollback. MySQL DDL
and previously committed writes cannot be undone by that action. Connections do
not automatically reconnect or replay SQL.

Opening or reconnecting a workspace shows the shared centered connection overlay
with cancellation. Failures replace the waiting state with a safe message and
close, edit-connection and retry actions. Connection errors do not occupy the
page notification row. The body keeps its size and pauses interaction while the
overlay is open. Cancellation disposes late sessions instead of activating them.

## Database catalog and script Sessions

The instance connects in the main window before its work window opens. Database selection and all script Sessions stay in that same work window.
One fully loaded database can be used directly. With multiple databases, users
choose the scope or explicitly select all, then enter a script Session in place. New Session uses the same picker; cancel returns to the previous Session.
Every Session is fixed to one instance, has independent SQL, execution history,
results, transaction state and AI conversation, and retains at least one database.
Object views belong to their script Session; Return to script preserves their drafts.

A single database scope popup manages included databases. Add databases lists only the current instance,
keeps included rows read-only and preserves the execution target. One eligible
candidate is used directly; multiple candidates start unselected. Removing a chip
always confirms the exact instance, Session, database and effects. It closes that
Session's object views for the database and revokes its AI metadata access without
deleting the server database. The last chip cannot be removed. When removing the
execution target, one replacement is fixed; multiple replacements require an
explicit choice before confirmation. Cancel preserves scope, target and content.

The SQL execution target is separate from scope. A unique target is fixed; multiple
targets start unselected and execution remains disabled until the user chooses.
Tree selection never changes it. Adding a database does not switch it. Database
switches retain draft and transaction guards and open a fresh physical connection.
Closing the last Session returns to the in-window picker.

## Completion and execution plans

The shared editor suggests SQL keywords, current-database tables and views, and
fields loaded on demand for basic FROM/JOIN aliases. Explicit database qualifiers
can request only included databases in the same Session. Ctrl+Space opens suggestions;
Up/Down selects, Tab/Enter inserts, and Escape closes the popup. Inserting a
suggestion replaces the current token or selection and preserves native undo.
Metadata failures fall back to keyword suggestions without exposing SDK errors.

Explain uses the selection or caret statement and displays the server's EXPLAIN
result in the existing result grid. Analyze requires explicit confirmation, only
accepts a single SELECT without file output, variable assignments or locking
clauses, and requires MySQL 8.0.18+ with EXPLAIN ANALYZE support. It actually executes
the SELECT in this tab's physical session; functions may have side effects.
The execution trace keeps the latest 50 query/plan attempts, elapsed time, SQL
snapshots and plan or safe error details. Traces remain in memory until tab close.

## SQL assistant and local model settings

Configure an OpenAI-compatible model service in System settings → AI assistant.
The independent `Asgard.Hermod.Agent` project handles model requests and bounded
conversation history; it has no database execution capability. The service URL
and model name are stored only in local SQLite settings and do not synchronize.
API keys are held in process memory only, never written to SQLite or configuration
files, and must be re-entered after restarting Hermod. Requests go directly to the
configured service. HTTPS is required except for loopback HTTP model services;
redirects and automatic retries are disabled.

Each script Session keeps its own assistant conversation, also used by its object
views. The caption Agent action opens a separate nonmodal window bound to that
script Session. Its X hides it, preserving the conversation, draft, task and window
position; repeated entry restores the same window. Other active Sessions do not
redirect it. New conversation archives the current page in a bounded in-memory
history. Closing the source retains readable messages and disables new actions.
Accepted questions appear immediately and clear only their own draft. Failed
questions can be restored explicitly without overwriting a newly edited draft.
The database explorer can also be collapsed and restored without changing its
expanded branches, selected object, Session scope or execution target.
Sending a question reads bounded metadata only from its included databases
using same-instance metadata connections, without choosing an execution target, and includes editor
SQL only when selected. Result data is excluded by default; the explicit sharing
checkbox includes at most 20 loaded rows, 16 columns and 8 KiB. The panel explains
this before sending. No connection credentials or connection options enter the
model context. Users should review editor SQL and shared result samples for
sensitive content before sending them to their chosen model service.

Generated SQL can be inserted at the editor's caret/selection without execution.
Review and run adds a single-use inline approval card showing the complete SQL,
instance, endpoint, original Session and target database before using the same
query execution path and session, including existing draft and transaction guards.
Scope, execution target or physical connection changes invalidate old execution
suggestions. Future model requests exclude conversation from earlier scope revisions;
visible conversation history stays available until explicitly cleared or closed. Cancelling a model request keeps the input draft, and closing a tab
cancels its request and clears its conversation. Neither conversations nor result
samples are persisted or synchronized by Hermod.

## Editing

Double-click or F2 edits short values inline. Enter/Tab accepts the local draft;
Escape restores the current cell. The cell expand action, Alt+Enter and its context
menu open a resizable field window for this row's field. Long text and JSON retain
the full value. SQL NULL is distinct from an empty string. Hex and Base64 preserve
binary bytes; invalid JSON or typed values remain in the editor.

Applying a field window updates only the local grid draft. Save changes explicitly
opens a separate database session and commits the grid's changed fields. Editing
requires a verified InnoDB base table, all primary-key columns, complete bounded
values and UPDATE permission for each changed field. Views, generated columns,
aggregates, joins and unverified projections remain read-only. A simple SQL result
must also match the server's physical source identity. Finish a draft in one result
set before editing another.

Paging and edit actions share one 32px footer row. The left side shows the table's
row range or pending-change count; full editing guidance is available on the info
icon. Arrow buttons have localized previous/next tooltips and accessibility names.
Narrow windows truncate status text while keeping paging, revert, save and any
required verification action on the same row. Query results have no page buttons.
Empty results keep their column headers and show a localized no-data message,
without an insert row, edit actions or an editing hint. Read-only results hide
edit actions. The first empty table page hides paging; an empty later page keeps
the previous-page action so the user can return to existing rows.

The save transaction locks each original row, compares its complete typed baseline
and commits the batch only if all comparisons succeed. Conflicts show original,
current-server and draft values and retain the draft. Unknown dispatched-write
outcomes block resubmission until reconnect-and-verify confirms the baseline or
submitted values. A mixed/different state requires resolving the differences.
Closing, refreshing or requerying confirms save/discard/cancel for pending edits.

## Verification

Run the solution build, format check and xUnit v3 suite. Pure tests need no server.
Optional live tests use an isolated MySQL 8.4 server; set
`HERMOD_MYSQL_TEST_PORT` to its loopback port. A server certificate issued by a
test CA and valid for 127.0.0.1, localhost and mysql-test.local allows the additional
TLS/SSH-transport check by setting `HERMOD_MYSQL_TEST_CA` to the CA PEM path.
The fixture creates unique temporary databases/users and removes them afterwards.
Do not point it at a production server.

WPF tests exercise both languages/themes, resizing, actual inline editing, fixed
row headers, horizontal scrolling, cached views, field windows, conflicts and
unknown-outcome recovery. `HERMOD_UI_PREVIEW_DIR` optionally saves PNG previews;
previews and test certificates are ignored and are not committed. Editable database
designs live in `design/DBWindow.pen`, treated as binary and accessed through
Pencil tools without copies in audit or test directories.


The production Agent keeps its task busy while an approval is pending. Review and
execute approves only the displayed operation; Decline and Insert into editor do
not execute it. Stopping or changing the source database/scope cancels the approval.
Old cards remain readable but cannot authorize another operation. Cancellation
after dispatch is reported as an unknown outcome, never as an undo or a retry.
If a provider ignores cancellation, the Agent stops waiting while the original
Session continues tracking that operation; late completion cannot rewrite the
Agent's retained unknown-outcome record.
