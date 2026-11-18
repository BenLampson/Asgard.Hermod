# Use Hermod through MCP

Hermod provides scoped tools to a local AI client using MCP over standard input
and output. One client entry can use all supported types of saved connections.
Hermod handles the existing credentials, host verification, TLS and configured
routes. You do not need to set up an AI model in Hermod to use this feature.

1. Open **System settings → AI tools (MCP)** in Hermod. Local MCP access starts off.
2. All saved instances and their resources are available by default. Clear an
   instance checkbox or add a prohibited location to exclude it. Newly added
   instances are available automatically.
3. Turn on access and choose **Copy client configuration**. Use the `hermod`
   entry in the local client's MCP settings. The configuration includes a local
   access token; keep it in trusted client settings and out of source control.
4. Keep Hermod running. By default, the first resource request on each connection
   opens a confirmation window. To grant access ahead of time, select **Always
   allow** beside that instance in **AI tools (MCP)**. The choice applies to
   paired clients across connections and restarts for this local workspace.
   Both choices cover all permitted locations and supported operations,
   including transfers and deletion. Changing connection details requires fresh
   approval. Clearing **Always allow** restores the first-use prompt. Changing
   the setting disconnects active MCP sessions, so reconnect the client once.

Listing instances does not open a confirmation window. If an instance has
`scope.requires_confirmation: true`, the client should tell you in chat to open
Hermod, then send the first resource request once. That request opens **Confirm
MCP access**. Review the instance and choose **Allow access** or **Deny**.
Hermod waits up to 30 seconds for that decision. If the wait expires, the
client receives a confirmation timeout and the resource operation has not started.
A confirmed object metadata query has its own 20-second timeout. Clients that
request MCP progress can show whether Hermod is waiting for you or querying
storage. Keep the same MCP connection for the transfer and follow-up queries.

Builds include the `Hermod.Mcp.exe` launcher. An unpackaged build uses the
launcher in the application directory. MSIX registers a Windows app execution
alias, which the copied configuration uses across package updates. If the
launcher cannot start in the packaged build, check the Hermod alias in Windows
**App execution aliases**. Run the client at the same privilege level as Hermod.
Connect interactively in Hermod first to verify a new or changed SSH host key.

| Instance type | Location supplied by the client | Supported MCP operations |
| --- | --- | --- |
| Alibaba Cloud OSS, Tencent COS, Volcengine TOS, AWS S3, MinIO and S3-compatible services | Exact Bucket and optional literal key prefix | Paginated object names, sizes and timestamps; exact object checks. Read up to 16 KiB of UTF-8 text per request, transfer files, create folders, delete objects, or create temporary read-only links. |
| MySQL | Exact database and optional exact table | Table names, columns and indexes. Sampling rows requires an exact InnoDB table. |
| PostgreSQL | Exact database and schema, optional exact table | Table names, columns and indexes. Sampling rows requires an exact ordinary table. Child tables are excluded from row samples. |
| SSH | Saved host, with an optional exact systemd unit | Uptime and load, disk, memory and selected unit status. Read recent journal lines for that unit. |
| Redis | Exact database number and optional literal key prefix | Paginated text keys, types, TTL and bounded value pages. Binary values use Base64. Empty scan pages can still have a continuation; topology changes can require a fresh scan. |
| RabbitMQ | Exact virtual host | Queue depth, ready and unacknowledged counts, and consumers. No message retrieval or acknowledgement is performed. |

For a storage folder, use its complete prefix and trailing slash, for example `exports/`.
For Alibaba Cloud OSS, Hermod discovers the granted Bucket's region and uses
it for browsing, file reads, uploads, and downloads. This keeps the connection's
public, internal, or dual-stack endpoint mode and does not change the saved
connection. If OSS cannot disclose the region with the current credentials,
Hermod can still use the configured endpoint for data operations.

An empty storage or Redis prefix refers to the whole selected Bucket or
Redis database. SQL row reads are paginated and run in a server-enforced
read-only transaction. Results are bounded and do not include connection
secrets. The available tools are the operations shown in the table; confirming
an instance does not add tools that Hermod has not implemented.

## Exclude instances or locations

All instances are checked by default. Uncheck an instance to exclude it, or use
**Prohibit a location** to specify a Bucket and prefix, database and table,
PostgreSQL schema, Redis prefix, RabbitMQ virtual host or queue, or SSH service.
An empty resource excludes the entire instance; an empty lower-level field covers
all items below that level. Excluded names are filtered from lists, direct access
is refused, and recursive transfers, statistics or deletions cannot cross into
an excluded location. Changing exclusions disconnects existing MCP sessions;
reconnect the client to refresh discovery and confirmations.

The client calls `hermod_list_instances` and then `hermod_list_resources` to
discover a target. It supplies the relevant `bucket`, `database`, `schema`,
`vhost` or `service` when calling a tool. Hermod shows the instance and requested
location when confirmation is required. Blocking an entire instance also clears
its **Always allow** selection.

## Transfer files with a task ID

The client specifies the Bucket and the object key or folder. Uploads supply
an absolute path to the local source file or folder. Choose the default download
folder when first confirming a storage instance. Selecting **Always allow** in
settings uses the Windows download folder as the default. A later download uses that
folder when no destination is supplied, or the absolute destination supplied by
the client. There is no additional transfer confirmation. The Bucket root can
be used unless it or the requested location is prohibited.

1. `hermod_list_instances` returns an instance's `grant_id`. The client can
   call `hermod_list_resources` to discover its Buckets.
2. `hermod_storage_start_transfer` takes that `grant_id`, `bucket`, `direction`
   (`download` or `upload`), the full object `key`, and an optional `local_path`.
   A `key` ending in `/` transfers directory contents recursively and preserves
   subdirectories; use `/` to transfer the entire Bucket. An upload requires an
   absolute source path. A download uses the instance's chosen default folder or
   the client's absolute destination. Existing destinations are preserved by
   default; `conflict: "overwrite"` requests replacement.
3. The start call returns a `task_id` immediately. Call
   `hermod_storage_transfer_status` with that ID to see `queued`, `running`,
   `completed`, or `failed`, along with the number of completed files. The total
   is available after scanning the source. Between polls, wait at least the
   returned `poll_after_ms`.

The first terminal status query removes the task. Unclaimed results expire five
minutes after finishing. Tasks live only in the running Hermod process and can
be queried after reconnecting with the same authorization. Revoking or changing
the MCP policy, switching workspace, or closing Hermod stops affected work.
Files already transferred are kept. Uploads can follow local links; download
paths do not follow links. Cycles in an upload directory are rejected. Each task can transfer up to 1,000 files, with two tasks
running at once and a two-hour limit including queue time.

## Manage storage objects

Storage operations are available by default for non-excluded locations.
Hermod uses the instance approval from settings or the current connection. Deletion plans do not request
another confirmation. An empty prefix includes the whole Bucket.

`hermod_storage_create_folder` creates a folder marker without overwriting an
existing object. `hermod_storage_create_temporary_link` creates a shareable
read-only link for 15 or 60 minutes. It is returned to the AI client, so use
this permission only with clients and model providers you trust.
`hermod_storage_folder_statistics` recursively counts files, folders and bytes
and reports the latest file modification time. It uses metadata permission and
returns a complete result or an error; scans are limited to 100 pages, 100,000
entries and the normal MCP request timeout.

For one or more objects or folders, deletion has two steps:

1. Call `hermod_storage_prepare_delete` with exact `keys` and/or folder
   `prefixes` ending in `/`. Prefixes are recursively enumerated, and their
   folder marker keys are included even if the provider omits them. The response
   contains a frozen list of keys; use `hermod_storage_delete_plan` and each
   `next_offset` to inspect all pages.
2. Call `hermod_storage_execute_delete` with the `plan_id` for the user's requested deletion.
   The plan belongs to that connection and grant, expires after five minutes,
   and can be submitted only once. Each plan is limited to 1,000 distinct keys
   and 100 listing pages. Larger ranges must be divided into smaller plans. A
   successful response reports the submitted key count, including any marker
   or other key that was already absent.

Only the listed keys are deleted; objects created later under a selected folder
are not added to the plan, although its listed marker key is included. Providers do not guarantee that an object at a listed
key remains the same between preparation and deletion, so a replacement at the
same key can be deleted. A failure or interruption may mean that some or all
keys were deleted. Check the objects before preparing another plan; do not
blindly retry. Versioned Buckets follow their provider's ordinary delete
behavior; older versions may remain. Provider and connection errors never
expose credentials.

## Revoke access

Uncheck an instance or add a prohibited location in **AI tools (MCP)**. Use
**Turn off access** to disconnect clients and invalidate their configuration;
reenabling generates a new token. Policy changes disconnect existing sessions
and discard their confirmed instances and cursors. The policy and token are
protected with Windows user encryption in the local database, apply to the
active workspace and do not sync. MCP opens no network port. When upgrading
from individual grants, Hermod keeps those grants in an encrypted backup and
clears the old active-grant list. Older versions cannot enforce the new
exclusions, so they do not inherit access from that old list. Changing MCP
settings in an older version can discard newer exclusion rules.
