# Use Hermod through MCP

Hermod provides scoped tools to a local AI client using MCP over standard input
and output. One client entry can use all supported types of saved connections.
Hermod handles the existing credentials, host verification, TLS and configured
routes. You do not need to set up an AI model in Hermod to use this feature.

1. Open **System settings → AI tools (MCP)** in Hermod. Access starts off.
2. Select an instance, enter its resource scope and choose **Add grant**. Leave
   the content and transfer options off to share only metadata or diagnostics. Repeat for the
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
| Alibaba Cloud OSS, Tencent COS, Volcengine TOS, AWS S3, MinIO and S3-compatible services | Exact Bucket and optional literal key prefix | Paginated object names, sizes and timestamps; exact object checks. Separate options allow reading up to 16 KiB of UTF-8 text per request or starting file transfers. |
| MySQL | Exact database and optional exact table | Table names, columns and indexes. Sampling rows requires an exact InnoDB table and explicit content access. |
| PostgreSQL | Exact database and schema, optional exact table | Table names, columns and indexes. Sampling rows requires an exact ordinary table and explicit content access. Child tables are excluded from row samples. |
| SSH | Saved host, with an optional exact systemd unit | Uptime and load, disk, memory and selected unit status. Recent journal lines for that unit require explicit log access. |
| Redis | Exact database number and optional literal key prefix | Paginated text keys, types and TTL. Empty scan pages can still have a continuation; topology changes can require a fresh scan. |
| RabbitMQ | Exact virtual host | Queue depth, ready and unacknowledged counts, and consumers. No message retrieval or acknowledgement is performed. |

For a storage folder, include its trailing slash, for example `exports/`.
An empty prefix grants metadata for the entire selected Bucket or Redis
database. SQL row tools accept a row limit and offset, with up to 25 rows per
request and additional size caps. They generate a qualified query in an
independent server-enforced read-only transaction. They accept no free-form SQL
or filters. Text, logs and sampled rows can contain sensitive data; an external
client can pass the results to its model provider. Connection secrets, Redis
values, storage signing links, arbitrary shell commands, deletion and database
writes are not tool capabilities.

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
   file name; a single upload requires the relative source file path.
3. The start call returns a `task_id` immediately. Call
   `hermod_storage_transfer_status` with that ID to see `queued`, `running`,
   `completed`, or `failed`, along with the number of completed files. The total
   is available after scanning the source. Between polls, wait at least the
   returned `poll_after_ms`.

The first terminal status query removes the task. Unclaimed results expire five
minutes after finishing. Tasks live only in the running Hermod process and can
be queried after reconnecting with the same authorization. Revoking or changing
the MCP policy, switching workspace, or closing Hermod stops affected work.
Files already transferred are kept. Transfers never overwrite existing files or
follow local links. Each task can transfer up to 1,000 files, with two tasks
running at once and a two-hour limit including queue time.

## Revoke access

Use **Revoke** to remove a grant and **Turn off access** to disconnect clients
and invalidate their configuration. Reenabling generates a new token. Grant
updates disconnect existing sessions; reconnect the client to refresh discovery
and discard old cursors. The grants apply to the active Hermod workspace on the
device. Replacing or editing a saved connection invalidates its previous grants.
Grants and the token are protected with Windows user encryption in the local
database and do not sync with your account. No network port is opened for MCP.
