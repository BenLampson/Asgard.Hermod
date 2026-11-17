# Use Hermod through MCP

Hermod provides scoped tools to a local AI client using MCP over standard input
and output. One client entry can use all supported types of saved connections.
Hermod handles the existing credentials, host verification, TLS and configured
routes. You do not need to set up an AI model in Hermod to use this feature.

1. Open **System settings → AI tools (MCP)** in Hermod. Access starts off.
2. Select an instance, enter its resource scope and choose **Add grant**. Leave
   the content, transfer and object-operation options off to share only metadata or diagnostics. Repeat for the
   instances you want the client to use together.
3. Turn on access and choose **Copy client configuration**. Use the `hermod`
   entry in your local client's MCP settings. The copied JSON uses an
   `mcpServers` object, `command` and `env`; use your client's equivalent fields
   if its settings use another format. `HERMOD_MCP_TOKEN` is a local access
   credential. Keep it in trusted client settings and out of source control.
4. Keep Hermod running in the same Windows user session. In **Try asking**,
   choose **Copy prompt** and paste it into your AI chat to discover what you
   can ask about the granted instances. The prompt contains no credentials.
   You can also ask a question directly, such as “Use Hermod to check whether
   the test export service is running and the latest file reached storage.”

Builds include the `Hermod.Mcp.exe` launcher. An unpackaged build uses the
launcher in the application directory. MSIX registers a Windows app execution
alias, which the copied configuration uses across package updates. If the
launcher cannot start in the packaged build, check the Hermod alias in Windows
**App execution aliases**. Run the client at the same privilege level as Hermod.
Connect interactively in Hermod first to verify a new or changed SSH host key.

| Instance type | Required grant scope | What the client can read |
| --- | --- | --- |
| Alibaba Cloud OSS, Tencent COS, Volcengine TOS, AWS S3, MinIO and S3-compatible services | Exact Bucket and optional literal key prefix | Paginated object names, sizes and timestamps; exact object checks. Separate options allow reading up to 16 KiB of UTF-8 text per request, transferring files, creating folders, deleting objects, or creating temporary read-only links. |
| MySQL | Exact database and optional exact table | Table names, columns and indexes. Sampling rows requires an exact InnoDB table and explicit content access. |
| PostgreSQL | Exact database and schema, optional exact table | Table names, columns and indexes. Sampling rows requires an exact ordinary table and explicit content access. Child tables are excluded from row samples. |
| SSH | Saved host, with an optional exact systemd unit | Uptime and load, disk, memory and selected unit status. Recent journal lines for that unit require explicit log access. |
| Redis | Exact database number and optional literal key prefix | Paginated text keys, types and TTL. Empty scan pages can still have a continuation; topology changes can require a fresh scan. |
| RabbitMQ | Exact virtual host | Queue depth, ready and unacknowledged counts, and consumers. No message retrieval or acknowledgement is performed. |

For a storage folder, include its trailing slash, for example `exports/`.
For Alibaba Cloud OSS, Hermod discovers the granted Bucket's region and uses
it for browsing, file reads, uploads, and downloads. This keeps the connection's
public, internal, or dual-stack endpoint mode and does not change the saved
connection. If OSS cannot disclose the region with the current credentials,
Hermod can still use the configured endpoint for data operations.

An empty prefix grants metadata for the entire selected Bucket or Redis
database. SQL row tools accept a row limit and offset, with up to 25 rows per
request and additional size caps. They generate a qualified query in an
independent server-enforced read-only transaction. They accept no free-form SQL
or filters. Text, logs and sampled rows can contain sensitive data; an external
client can pass the results to its model provider. Connection secrets, Redis
values, arbitrary shell commands and database writes are not tool capabilities.
Storage deletion, folder creation and signing links each require a separate
grant option. Existing grants do not acquire these permissions automatically.
Anyone who obtains a temporary link can read its object until the link expires.

## Transfer files with a task ID

In **AI tools (MCP)**, select an object storage instance. Enter its Bucket and
a nonempty prefix ending with a slash, such as `exports/`. Check the transfer
direction you want to allow, choose a local folder, and add the grant.
Existing grants do not acquire transfer permission. Reconnect the AI client
after changing grants.

For example, tell the client: “Download the contents of `exports/daily/` into
`daily` under the local folder I granted, then check when it finishes.”

1. `hermod_list_instances` returns the `grant_id` and the allowed directions.
2. `hermod_storage_start_transfer` takes `grant_id`, `direction` (`download`
   or `upload`), the full object `key`, and an optional `local_path` relative
   to the granted folder. A `key` ending in `/` transfers the directory contents
   recursively and preserves subdirectories. For a directory, omit `local_path`
   to use the granted folder itself. A single download defaults to the object's
   file name; a single upload requires the relative source file path. Existing
   destinations are preserved by default. If overwrite is separately enabled
   on the grant and the user confirms, pass `conflict: "overwrite"` to replace
   them.
3. The start call returns a `task_id` immediately. Call
   `hermod_storage_transfer_status` with that ID to see `queued`, `running`,
   `completed`, or `failed`, along with the number of completed files. The total
   is available after scanning the source. Between polls, wait at least the
   returned `poll_after_ms`.

The first terminal status query removes the task. Unclaimed results expire five
minutes after finishing. Tasks live only in the running Hermod process and can
be queried after reconnecting with the same authorization. Revoking or changing
the MCP policy, switching workspace, or closing Hermod stops affected work.
Files already transferred are kept. Transfers do not follow local links. Each
task can transfer up to 1,000 files, with two tasks
running at once and a two-hour limit including queue time.

## Manage storage objects

Select the independent options for folder creation, deletion, and temporary
links when adding a storage grant. The chosen Bucket and literal prefix apply
to every operation. An empty prefix includes the whole Bucket. Revoke the old
grant if it should no longer be available, and reconnect the AI client to
discover the changed permissions.

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
   `next_offset` to inspect all pages. Ask the user to confirm that list.
2. After confirmation, call `hermod_storage_execute_delete` with the `plan_id`.
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

Use **Revoke** to remove a grant and **Turn off access** to disconnect clients
and invalidate their configuration. Reenabling generates a new token. Grant
updates disconnect existing sessions; reconnect the client to refresh discovery
and discard old cursors. The grants apply to the active Hermod workspace on the
device. Replacing or editing a saved connection invalidates its previous grants.
Grants and the token are protected with Windows user encryption in the local
database and do not sync with your account. No network port is opened for MCP.
