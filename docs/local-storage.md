# Local SQLite storage

Hermod initializes local storage before opening its main window. The default
database is `%LOCALAPPDATA%\Asgard\Hermod\hermod.db`. Startup creates missing
parent directories and the database file. Repeated startup preserves saved data.

## Configuration

The application ships `app.yaml` beside its executable, including published
builds. It uses Asgard configuration and `DatabaseConfig` with the following
defaults:

```yaml
database:
  enabled: true
  provider: sqlite
  connectionString: "Data Source=hermod.db;Version=3;Pooling=False;Default Timeout=5;Journal Mode=Wal;"
```

Relative database paths resolve against the user data directory, independently
of the executable directory and current working directory. Absolute paths are
also supported. Configuration precedence is YAML, `HERMOD_` environment
variables, then command-line arguments. For example:

```powershell
$env:HERMOD_DATABASE__CONNECTIONSTRING = 'Data Source=D:\HermodData\hermod.db;Pooling=False;Default Timeout=5;Journal Mode=Wal;'
dotnet run --project src/Asgard.Hermod

dotnet run --project src/Asgard.Hermod -- '--database.connectionString=Data Source=D:\HermodData\other.db;Pooling=False;Default Timeout=5;Journal Mode=Wal;'
```

Local storage must be enabled and use the SQLite provider with a writable file.
In-memory databases and connection URIs are rejected. Invalid configuration,
inaccessible paths, corrupt databases, and migration errors produce a startup
dialog and exit the application with a nonzero code.

## Initial schema and migrations

`LocalDatabaseInitializer` reads SQLite's application-owned `PRAGMA user_version`.
Version 0 is an uninitialized database. Version 1 creates `app_setting` with these
columns:

| Column | Storage | Purpose |
| --- | --- | --- |
| `id` | TEXT primary key | Unique setting key. |
| `value` | TEXT, required | Serialized setting value. |
| `update_time` | TEXT, required | UTC update timestamp, written by the consuming service. |

This table stores user-level application settings. Startup initializes the
storage infrastructure; instance profiles and UI preferences still need their
own persistence integration.

Initialization acquires a SQLite write lock before checking the version. Schema
changes and the version update share one transaction. Failed migrations roll
back, and concurrent startup waits for the write lock. A database with a newer
version is rejected. Existing version-1 databases must contain the required
table and columns. Runtime automatic schema synchronization is disabled.

For future schema changes, add ordered migrations from the previous version and
increment `CurrentSchemaVersion`. Preserve existing data and advance
`user_version` only after the corresponding migration succeeds. Do not edit the
version-1 baseline to represent a later schema.

## Service lifetime and verification

`AddLocalStorage` registers configuration, Asgard's singleton `IFreeSql`, and
`LocalDatabaseInitializer`. Storage consumers use that same database instance.
The application service container disposes the FreeSql connection pool on exit.
The default connection string disables native driver pooling so file handles are
released when the application closes. SQLite files and journal/WAL sidecars are
excluded from Git.

The implementation uses `FreeSql.Provider.Sqlite` 3.5.311, matching the FreeSql
version referenced by `Asgard.Core` 6.0.3. SQLite is embedded; no external database
service is required. See the [FreeSql provider documentation](https://github.com/dotnetcore/FreeSql)
and [SQLite PRAGMA reference](https://www.sqlite.org/pragma.html#pragma_user_version).

Integration tests use isolated temporary files and the production service
registration to verify creation, restart persistence, concurrent startup,
unsupported versions, migration failures, corrupt files, configuration, and
file-handle cleanup:

```powershell
dotnet test --project tests/Asgard.Hermod.UI.Tests/Asgard.Hermod.UI.Tests.csproj --configuration Release -- --filter-class '*LocalDatabaseTests'
```
