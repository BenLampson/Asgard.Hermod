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
previews and test certificates are ignored and are not committed. The sole editable
design source remains `design/Hermod.pen`, treated as binary and accessed through
Pencil tools without copies in audit or test directories.
