# Storage and temporary files

Settings → Storage follows the Pencil storage and cleanup confirmation designs.
It shows actual file counts and sizes, cleanable space, files in use, and a
breakdown of temporary downloads, cached files and processing files. Refresh
runs a new background scan. The storage location is read-only; Open folder opens
that exact directory in Windows Explorer.

Only Hermod's dedicated `%LOCALAPPDATA%\Asgard\Hermod\file-cache` directory is
managed. Local workspaces, the SQLite configuration database, account settings,
user-selected download destinations and remote objects are outside this scope.
There is no database migration or new path preference. Legacy GUID request
directories from the previous file opening implementation are recognized.

## Protection and cleanup

Every download or preview receives an isolated GUID directory and an exclusive
file lease. Both the current process and another Hermod process skip leased
requests. External opening transfers ownership of the lease to the application
service until Hermod exits: programs such as editors may release their file
handles while still using a document, so these caches are conservatively
protected for the entire Hermod session. After restarting Hermod, files still
locked by another program remain protected by operating-system lock checks.

Clean temporary files scans again before showing the confirmation with its
estimated space. Cancel performs no deletion. After confirmation, cleanup checks
leases and file locks again and reports the actual deleted files, released bytes
and skipped items. The page refreshes its statistics afterward. Inaccessible or
linked items are preserved and partial statistics are clearly labeled.

Cleanup accepts only direct GUID request directories, checks their resolved
boundary and rejects reparse points and symbolic links. It deletes individual
regular files and removes empty request directories without recursive deletion.
Internal lease and category markers are excluded from payload sizes and counts.
Processing files are recognized by the download service's GUID `.download`
temporary filename pattern. Startup maintenance follows the same protection
rules and only removes inactive requests older than seven days.

## Module ownership and verification

Abstractions owns the storage, managed file, snapshot and cleanup contracts. Core
owns directory allocation, cross-process leases, scanning and cleanup. Shared UI
owns settings presentation, confirmation and Explorer interaction. ObjectStorage.UI
holds the managed file throughout downloading and viewing. Provider modules do
not depend on WPF or temporary storage presentation.

Tests use isolated filesystem roots. They cover actual byte counts, category
breakdown, active transfers, external cache retention, reopening, file-lock
changes, cancellation, path boundaries, legacy caches, confirmation cancellation,
safe errors and window closure. WPF checks include both languages and themes,
confirmation dialogs and reduced window dimensions.
