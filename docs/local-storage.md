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

This table stores user-level application settings. The shared
`IThemePreferenceStore` contract saves the selected theme immediately under
`ui.theme`, using the stable values `light` and `dark`. Startup restores it before
constructing the shell, windows or tray icon. Missing or unknown values retain
the default light theme. Theme choices stay local to this device and do not
require a schema migration. A failed save keeps the current theme and displays
a localized error.

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

Schema version 4 adds `instance_configuration.hosting_provider_id` with an empty
default for self-hosted instances. The migration leaves connector IDs, encrypted
connection parameters, creation times and tag associations unchanged. Metadata edits
write the brand, name and tags atomically without decrypting credentials. Workspace
export and restore preserve the brand; per-instance cloud sync carries it inside the
existing encrypted secrets envelope so the wire format and server remain compatible.

The main window stores each instance's last access ID and UTC timestamp
in `app_setting` under `workspace.recent`, through `IRecentInstanceStore`. Names,
endpoints and credentials are not copied into this history. The table resolves
names from current instance snapshots and prunes records for deleted instances.
Overview displays only the six most recent entries; all-instance sorting retains
older access facts, so visiting a seventh instance does not erase the first one's
last-opened time. Existing bounded history remains readable without migration.
The built-in local terminal remains available without a saved instance profile.
History is specific to this device and is not included in workspace sync.
Opening a page is recorded as an access, not as a successful remote connection.

`Asgard.Hermod.Persistence.Sqlite` owns database configuration, migrations,
repositories and local instance/tag services. `AddLocalStorage` registers these
services and Asgard's singleton `IFreeSql`. The host separately calls
`AddHermodCore` for the instance registry and workspace source, and
`AddWindowsSecretProtection` to select DPAPI through `ISecretProtector`.
Cloud providers, their HTTP clients, and object storage operations are registered
by their own modules. Storage consumers use the same database instance.
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
