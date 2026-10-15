# PostgreSQL

PostgreSQL uses independent protocol and WPF modules, registered by
`AddPostgreSql()` and `AddPostgreSqlUi()`. Npgsql owns the wire protocol; the
existing shared controls provide the wizard, query editor, grid, workspace
windows, connection overlay and field dialogs.

## Connections and databases

The connection wizard supports direct TCP and SSH forwarding. The default port
is 5432 and the initial database is `postgres`. Verified TLS is the default;
system trust or a custom CA and optional client certificate can be configured.
Database and SSH credentials use the existing encrypted configuration storage.
Hosting brands remain display metadata and do not alter the connection route.

The instance catalog opens script Session workspaces using explicitly included
databases of the same instance. One eligible database is used directly; multiple
databases require selection, with an explicit Select all action. A Session retains
at least one database. Scope changes and removal confirmations follow the shared
[Session rules](mysql.md#database-catalog-and-script-sessions). Databases contain schemas, and
schemas contain tables, views, routines and their metadata. Object identity
includes database, schema, kind and original name. Filtering searches loaded
objects without scanning the server. Opening a table by name is available when
directory listing is restricted.

Every query tab has a separate physical connection and transaction context.
PostgreSQL connections belong to one database: opening another database creates
a new connection. Changing a tree selection does not change an existing query's
database. Cross-database access requires server-side facilities configured by
the user; Hermod does not emulate MySQL's `USE` statement.

## SQL and results

Ctrl+Enter runs selected SQL or the statement at the caret; Ctrl+Shift+Enter runs
the document. The parser handles PostgreSQL double-quoted identifiers, escape
strings, tagged dollar quotes, nested block comments and SQL `BEGIN ATOMIC`
bodies. String escaping follows the connected server's
`standard_conforming_strings` setting. SQL files are local documents; saving a
file does not execute it.

Completion uses PostgreSQL keywords and the current database's loaded metadata.
The execution-plan action requests `EXPLAIN (FORMAT JSON)` for one statement;
it does not add `ANALYZE`. Plans and query results use the existing result view.

Results preserve duplicate names by column ordinal. Query previews are bounded
to 1,000 rows or 10 MiB. Table pages use an explicit page size and a parameterized
filter, shown on demand from the table toolbar. Removing a filter respects
unsaved changes and collapses its editor. Reaching a query preview limit stops
the remaining batch. Cancellation
does not undo completed statements; an explicit transaction can be rolled back.
No connection silently reconnects or replays SQL.

Table browsing permits edits only with a verified base table, a complete primary
key, supported complete values and UPDATE permission. Views and arbitrary SQL
results remain read-only. Changes stay in the local grid until explicitly saved
through a dedicated writer connection. Saving locks and compares the original
row before applying changes in one transaction. Conflicts retain drafts. An
unconfirmed dispatched write blocks retries until reconnect-and-verify resolves
the outcome. SQL NULL, empty text and binary bytes remain distinct.

The optional side assistant shares the separate Agent module and local system
settings with MySQL. The caption Agent action restores a resizable split pane;
its X hides it while retaining conversation, draft and width. The database explorer
can be collapsed and restored while retaining expanded branches and selection.
Conversations belong to script Sessions, including their
object views. It receives PostgreSQL dialect and bounded metadata only for databases
in the current Session, along with explicitly selected editor SQL and result samples.
Changing scope invalidates earlier execution suggestions and stops forwarding old
scope conversation to the model; visible history is retained until confirmed clearing. Suggested SQL can be inserted into the editor or explicitly executed
through the active query session. API keys are held only in process memory.

## Verification

The xUnit v3 protocol tests cover syntax boundaries, configuration, safe errors
and filters; WPF tests cover workspace context and interaction. Optional live
tests use a disposable local PostgreSQL server selected explicitly through
`HERMOD_POSTGRESQL_TEST_PORT`. Do not point these tests at production data.
Run the solution Release build, format verification and related tests before
committing. All application text is provided in English and Simplified Chinese.

PostgreSQL lexical behavior follows the official
[SQL lexical structure documentation](https://www.postgresql.org/docs/current/sql-syntax-lexical.html).
