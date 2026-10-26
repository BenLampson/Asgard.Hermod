# SSH connections

SSH is an independent instance type. Its first page selects Direct, SSH jump host,
Interactive bastion or TCP proxy, with Direct selected by default. Next opens a separate connection
information page. Testing and confirmation follow; automatic save location is
shown at confirmation. Its four-step create and edit flows reuse the common type
picker, connection wizard, field validation, connection test, encrypted settings
and confirmation. English and Simplified Chinese are available in both the
wizard and terminal workspace.

## Connection options

| Route | Supported configuration |
| --- | --- |
| Direct | Host, port and login identity |
| SSH jump host | One jump host with its own port, user and authentication |
| Interactive bastion | Bastion address, port and login identity, plus the server's numeric menu selection |
| TCP proxy | SOCKS5 or HTTP CONNECT; optional username and password; local or remote DNS for SOCKS5 |

Each SSH identity supports password, a local private-key file with optional
passphrase, or Windows OpenSSH Agent. Only the selected authentication method is
used. Password and passphrase whitespace is preserved. Inactive credentials are
omitted from saved configuration. Private keys are imported before testing (UTF-8,
up to 32 KiB); saving retains that exact tested content. Local settings protect it
with Windows DPAPI, and account synchronization encrypts it with the synchronization
password. Other devices can connect without the original file. Terminal sessions,
jump hosts and SSH tunnels load the saved key in memory without temporary files.
Existing path-based configurations remain compatible; edit, test and save on a
device with the file to import them. Imported content is masked in the wizard.
Windows Agent keys remain device-local and are never copied.

RabbitMQ's SSH connection uses the same private-key import before testing and
saving. Its `ssh.key` content and `ssh.passphrase`, together with the RabbitMQ
password, stay inside the encrypted cloud configuration. On another device,
unlock account synchronization with the same synchronization password; no copy of
the original key file is needed. Existing RabbitMQ configurations that only saved
a key path must be edited, tested and saved once on a device where that file exists.
The new device still verifies the SSH host fingerprint independently.

The default port is 22 and the connection timeout is 15 seconds. The test opens
and closes an actual interactive PTY through the same protocol factory as the
workspace. Saving remains blocked until the current configuration passes.
Changing any connection field invalidates that result. The shared workflow
derives device or account saving from sign-in state.

## Interactive bastion sessions

Use **Interactive bastion** for gateways that display a server-selection menu.
Only one SSH login identity and the numeric selection are required; this is not
the TCP-forwarding jump-host route. Set the bastion's actual SSH port.
Advanced settings default the menu prompt to `[vbh]>` and
recognize target shell prompts ending in `$` or `#`; other literal prompt suffixes
can be configured. Hermod waits for the menu before sending the number once.
It waits for the target shell before exposing the console. Rejected selections,
closed sessions, cancellation and bounded startup timeouts fail without retrying
another number or sending tool commands to the menu. Menu numbers can change:
review the configured number when the gateway's asset list changes. This route
does not independently verify the selected target's SSH fingerprint; host trust
authenticates the bastion, and the gateway selects the target.

Every new console and tool command opens its own PTY and repeats the configured
selection. Monitoring, Docker and Kubernetes commands therefore run in the selected server's
POSIX shell, without injecting commands into the user's console. Command input
and output use `base64` and random framing markers, preserving output bytes across
terminal newline transformations. Both `sh` and `base64` must already be available
on the target. No helper service, Python installation or gateway change is needed.
Output is bounded to 64 KiB; commands are never automatically retried. A lost or
malformed response after dispatch means the command may already have executed.

File management, transfers and TCP port forwarding are unavailable for this route.
Their entry points explain the limitation, and the protocol adapter rejects such
requests rather than forwarding them to the bastion. MySQL-over-terminal relay
is not implemented. Real gateway compatibility still requires verifying menu and
shell prompts, multiple PTY channels, and command availability in that environment.

The opt-in `SshBastionPtyTests` run against the local `debian:12` Docker image with
networking disabled. Set `HERMOD_BASTION_PTY_TEST=1` when running the SSH tests to
exercise real PTY echo, long commands, Unicode, exit status and output limits.

## Host identity

Every connection checks the server's algorithm and SHA256 fingerprint. A new
identity requires explicit confirmation. A changed identity shows both full
fingerprints and requires the user to verify through a trusted channel before
updating trust. The jump host and final target are verified independently.

Trust belongs to the device and is stored in SQLite `app_setting`; it is separate
from synchronized workspace configuration. Host normalization and the port
define the identity. Compare-and-swap updates reject stale confirmations.
Network attempts are disposed before asking for verification, so the network
timeout does not limit the user's verification time. Cancellation closes the
pending verification dialog and does not save trust.

## SSH workspace

The independent SSH window uses a 44-pixel caption and 28-pixel status bar.
The caption exposes Monitoring and Agent. The 52-pixel instance tool rail opens
Files (320 pixels), Port forwarding (480 pixels) or Transfers (360 pixels), while
the 38-pixel console tab strip holds independent PTY consoles on one verified
transport. Closing one console retains its output and does not stop instance
tools, other shells, forwarding or transfers. Reconnect creates one new console
and retains ended buffers without replaying input or restoring the old shell cwd.

Agent opens a separate nonmodal window bound to the originating console.
It defaults to 1120×1000, with a 44-pixel caption, 84-pixel source band,
48-pixel conversation toolbar and 182-pixel minimum composer. Its body column is
960 pixels at the default width, grows to 1120 on wide windows and uses 24-pixel
side insets at 640 pixels. Closing only hides it; tasks, drafts, history and
position survive. Repeated entry restores the same window. Source location and
explicit terminal insertion return to the bound console. Source closure leaves
messages readable while disabling new actions. The UI does not fabricate token
streaming; it displays actual preparation, model, tool and approval phases.
Tool evidence starts collapsed, and an active approval stays expanded.
Each console has an independent bounded conversation using the configured model.
The model must support function tools for automatic troubleshooting.

Sending a troubleshooting request permits fixed read-only diagnostics: host and
uptime, disk usage, memory, processes (without command-line arguments), listening
ports, systemd service status and the latest 100 journal lines, container lists
and the latest 100 container log lines. Service/container names are validated
against a restricted identifier grammar. Unknown operations, extra/duplicate
arguments, shell metacharacters, paths and option-like targets are rejected
before execution; a model-provided "read-only" label never authorizes shell.
All other commands, including changes and restarts, require confirmation showing
the current host and exact execution command. Approval is checked again against
the connection generation after the dialog closes. Confirmation, execution and
model requests share one busy state, preventing another task or command from
entering while approval is pending.

Commands run on an independent noninteractive exec channel, starting in `/`,
with a two-minute timeout and 64 KiB output limit. They do not inherit the user's
interactive terminal directory, exports, aliases or privilege state. Results,
exit codes and truncation flags are returned to the model automatically so it
can diagnose further and verify approved changes. Only one tool runs at a time,
with at most eight calls per task and an earlier analysis-only finish when the
context budget is reached. Declined commands, uncertain execution outcomes and
failed changes close tool access for the rest of the task. The model may then
summarize but cannot retry them. Stopping cancels pending work; it does not undo
changes already applied or guarantee that remote effects have stopped.

Host identity is public context. Terminal history is never scraped: terminal
text is sent only when the user attaches a selection and opts to include it
(up to 8 KiB of characters). The Agent context explains that diagnostic/command
results are sent to the configured model. Saved passwords, private keys and
connection options are never included. Each tool result is limited to 4,000
characters for model feedback and explicitly marked if truncated. Raw tool
outputs are retained only within that task's request chain, not replayed in
subsequent tasks; later requests retain the user requests and final analyses.
Tool output is treated as untrusted data and cannot create executable command
cards. Text-only shell suggestions retain Insert and Run actions: insertion
does not add a return, and Run requires confirmation before execution and
returns its result for model analysis. Reconnecting or disconnecting cancels
old work, invalidates suggestions and clears the internal request history while
preserving visible messages and drafts.

Files use SFTP channels on the existing verified transport, without shell
interpolation. Browsing is nonrecursive and bounded to 10,000 entries. Names
are filtered locally; folder navigation also supports Enter. Upload, download,
new folder, rename and confirmed file/empty-folder deletion are available.
Uploads stage a unique remote temporary file before publishing and do not
overwrite existing targets. Downloads stage locally and replace the selected
destination only after successful completion. Cancellation preserves existing
destination files. Recent transfer results are bounded to 100 tasks; disconnect
and window disposal cancel ongoing operations. Deletion is never recursive.

Monitoring opens from the caption; there is no permanent summary row. The bottom
pane supports mouse and keyboard resizing, remembers its requested height while
collapsed, and reserves terminal input space in small windows. Overview, Processes,
Docker, Network and Disks have dedicated views with English/Chinese resources and
shared light/dark themes. Existing SSH and Agent content remains alive.

A serial, read-only exec channel samples Linux `/proc` every five seconds with
a ten-second deadline. CPU and per-interface network rates use counter deltas;
the first sample, reset counters and newly discovered interfaces have no rate.
The overview charts CPU, memory and aggregate non-loopback traffic against actual
timestamps over two minutes. Percent charts use a fixed 0–100 scale; traffic uses
a shared automatic scale, distinct solid/dashed curves and exact hover values.
Failed samples add gaps and preserve the last successful snapshot with its update
time. Pause affects monitoring only; explicit refresh remains available.

Processes use structured PID, name, user, state, CPU, memory percentage and resident
bytes. CPU is the `ps` lifetime average normalized by logical processor count,
which is explained in the view. Process arguments are never requested. Docker
includes stopped containers and separates runtime state from health checks.
Container logs open in an independent, resizable nonmodal window with its own
taskbar entry. Requests use only a validated full hexadecimal ID from the current
list. The reader defaults to the latest 500 timestamped lines and offers bounded
100/500/1000-line ranges. Explicit refresh replaces the snapshot and reports
output truncation independently. Search (Ctrl+F, F3/Shift+F3), wrapping, selection,
copying and user-initiated export are local operations. Closing the window
preserves monitor selection and filters. Logs remain untranslated process output; failed
reads never expose raw stderr. Missing Docker, insufficient access, empty results
and search misses have separate localized states.

Network exposes interface selection, per-interface rates, IPv4 addresses, MTU and
cumulative counters. Missing `ip` leaves counter sampling available. Disks display
mount points, devices, filesystem types and byte capacities with numerical sorting
and usage bars; over 85% has a textual warning. Persistent totals deduplicate block
devices and exclude tmpfs. Detail lists are bounded to 500 rows, support local
search and numerical column sorting, and retain previous rows on read failure.
No remote Hermod daemon or additional authentication is required.

The Docker table contains data columns only. Selecting a row updates the bottom
action bar; View logs and View details operate on that selection. The bar stays
available when a narrow window replaces the list with the drawer. Details show
the collected name, full container ID, image, state, health and port mappings
without an additional remote request. Filtering out the selection or removing a
container clears its actions. Each open log window retains its original container
identity regardless of selection or monitoring section changes. Opening the same
container again recalls its current window. Disconnect cancels outstanding reads
and retains the last successful snapshot; after reconnecting, opening logs creates
a document tied to the new connection. Closing the SSH workspace disconnects
existing log documents but leaves their independent snapshot readers available.

Each log window uses a 48-pixel caption, 40-pixel single-row toolbar and
24-pixel status bar. The read-only text fills the remaining body; local search,
selection, wrapping, copying, font adjustment and explicit file export do not
alter the captured log. The Agent entry opens a separate log-bound conversation
without reserving body space. It prepares a frozen snapshot attachment, or asks
the user to choose snapshot versus a real nonempty selection. Preparing evidence
never sends a model request. Each attachment records the original document,
full container ID, capture time, requested lines, exact visible character/line
range and truncation state. Refresh cannot change an attached draft or an active
request. At most the trailing 32,000 characters are shared. Raw old logs are not
retained in the model's question/answer history. Log analysis has no execution
tools. A disconnected reader can still analyze its retained snapshot; closing
the log document cancels analysis and leaves existing Agent history readable.

Port forwarding listens only on `127.0.0.1` and reuses the same verified SSH
client. Each forward has an explicit stop action. Disconnect, reconnect and
workspace disposal release all forwarding handles. The application does not
expose listening ports on external interfaces.

New console creates an independent PTY tab in the same instance window.
Closing an active console asks for confirmation; cancel keeps it alive.
Disconnect and remote exit retain read-only terminal output. Reconnect rereads
the saved configuration and creates one new shell without replaying old input.
All SDK and configuration failures use safe localized errors.

The portable protocol project uses Tmds.Ssh 0.26.0 (MIT). Its transitive API
publishing analyzer is excluded precisely in `Directory.Build.targets`, leaving
the repository's SDK and test analyzers enabled. SOCKS5 and HTTP CONNECT use a
single-use loopback bridge to the library's encrypted SSH transport; host
verification always uses the original target identity.

## Validation

Run the normal Release build, formatting check and xUnit v3 solution tests.
`SshLayoutTests` can export local previews with `HERMOD_UI_PREVIEW_DIR`; preview
images are ignored and should not be committed.

Docker management regressions use offscreen WPF controls and in-memory SSH
responses by default. Optional `SshDockerLocalIntegrationTests` accept only
`HERMOD_TEST_DOCKER_SSH_PORT`; the host is fixed to `127.0.0.1` and trust is kept
in memory. Use a dedicated Docker-in-Docker engine with no host Docker socket or
user directories mounted. Its disposable fixture provides user `tester` with
password `hermod-test-only`, containers `hermod-ux-web`,
`hermod-ux-unhealthy`, `hermod-ux-stopped`, and the project
`/tmp/hermod-compose/compose.yaml`. Never point these tests at a user server.
Remove the dedicated outer container and its anonymous volumes after testing.
The optional native confirmation regression additionally requires
`HERMOD_TEST_DOCKER_WINDOWS=1`; it uses fake connections, closes its windows and
never changes keyboard layouts. Leave this unset while the desktop is in use.

The network tests, including `SshLiveWorkspaceTests` for SFTP, exec, monitoring
and local forwarding, require a dedicated local OpenSSH fixture. Configure
`HERMOD_TEST_SSH_PORT`, `HERMOD_TEST_SSH_JUMP_PORT` and `HERMOD_TEST_SSH_KEY`.
The target is always `127.0.0.1`, with fixture user `tester`, password
`hermod-test-only`, and encrypted test-key passphrase `test-key-passphrase`.
The jump test assumes its sshd can reach port 22 in the same isolated environment.

For the Windows Agent test, configure `HERMOD_TEST_SSH_AGENT_CONTAINER`. The
dedicated Docker container must have `socat` and an OpenSSH Agent listening on
`/tmp/hermod-agent.sock` with a fixture key loaded. The test creates a unique
Windows named pipe and restores its test-process `SSH_AUTH_SOCK` afterward;
it never starts or changes the user's Windows Agent service. Unconfigured live
tests are skipped. Configured tests cover password, encrypted key, Agent,
jump, authenticated proxies, PTY resize, UTF-8, exit codes and host trust changes.


Production Agent command approvals are inline cards rather than native modal
dialogs. The full independent-channel command and fixed SSH target are visible,
and a pending card cannot be collapsed. Approval authorizes only that command.
Decline feeds a denied result to the model with further actions disabled for the
task. Stop, disconnect, source closure and reconnect invalidate the pending card.
Once a command has been dispatched, cancellation retains an unknown outcome and
never retries it. The UI can stop waiting for an uncooperative command client
without claiming that remote side effects were undone. Tool elapsed time starts
at dispatch, excluding the time spent waiting for the user's review.

## Docker resources and command library

Open **Monitoring → Docker** for container state, health, CPU, memory use/limit,
network receive/send rates, disk read/write rates and process/thread counts.
Sampling uses bounded `docker stats --no-stream --no-trunc` JSON output on an
independent exec channel. CPU follows Docker's logical-CPU convention and can
exceed 100%. Rates are estimates derived from consecutive cumulative CLI counters;
the first sample, a reset or a gap has no rate. Missing or stopped-container data
is shown as unavailable rather than zero. Failures retain the last successful
values with an explicit warning and leave gaps in the selected container's
two-minute history. Pausing and reconnecting reset rate baselines. Small monitors
prioritize the table; enlarge the panel to see both resource charts.

The left instance rail and selected-container action bar open **Docker management**.
Select a container, then click Logs, Resources, Details, Start, Restart or Stop.
The page initially asks for a target rather than silently selecting the first
container. A monitor shortcut preserves the selected full container ID.
Stopped containers offer Start; running containers offer Restart and Stop.
Logs run immediately, Resources returns to the monitor with the same container,
and Details displays the structured identity and ports. Start, Restart and Stop
ask for confirmation describing the target and effect, then run the chosen action
without a second generic Execute step. Successful changes have a readable result.

The library detects client/engine versions and prefers `docker compose`, falling
back to detected `docker-compose`. Base command templates support Docker CLI 20.10+
and Compose 1.29+. Older or unidentified versions are disabled with an explanation.
Unsupported structured stats or missing Compose
disable the corresponding operation with a localized explanation. Re-detection
refreshes capabilities and the current container list.

The built-in catalog covers container listing, bounded logs, one-shot stats,
state details, start/stop/restart, images, image pulls, and Compose status,
startup, restart and logs. Container commands use validated full IDs. Compose
requires an explicit absolute remote project directory and configuration path;
paths are shell-quoted, optional service names and image references are validated.
Compose startup uses `up -d --no-build`, without implicit image assembly.

Log options, image/Compose tools and advanced command details are collapsed by
default. Users do not need to read a command to manage containers. Advanced details
retain a read-only preview; **Insert into terminal** adds no return.
Actions dispatch the generated plan on an independent channel with a
two-minute timeout and 64 KiB output limit. Read-only operations require no change
confirmation. Changes require explicit confirmation of the host, target and effect;
confirmation and execution share a busy state and retain the same immutable plan. Disconnect, source closure
and reconnect invalidate pending approval. A canceled or truncated dispatched
operation reports an unknown outcome and is never retried automatically. Successful
changes refresh monitoring and the library's container list. Log following opens a
new independent PTY, where Ctrl+C stops following; it never appends a command to an
existing shell's partial input. Results retain their executed command and target
even after the form changes, and support local copying and explicit export.


## Infrastructure tools beside the terminal

Docker and Kubernetes share the instance tool rail with Files, Forwarding and
Transfers. Opening a tool preserves the active SSH console and its output. The
management pane initially uses half the available width; drag the divider with
the mouse or keyboard to resize it. Each tool remembers its requested width for
the workspace lifetime. Double-click restores the default size. Narrow windows
stack management above the terminal and remember a separate requested height;
small tool panes scroll without covering the terminal.

Docker initializes its client/engine/Compose capabilities and container list on
first entry. Containers, Images and Compose have separate tabs. Container lists
support local name/image and state filters, and show real sampled CPU and memory
values. A separate lower pane displays resource charts, action output or identity
details. Its divider resizes the list and detail areas independently. Sampling
pauses when leaving Docker, while an explicit operation can finish in the
background. Log following opens a new PTY beside the management pane. Existing
confirmation and connection-generation checks still guard every change.

Kubernetes initializes kubectl, contexts, the current context's configured
namespace (or `default`), the namespace list and the resource list in one sequence.
Context, namespace and resource-type changes load automatically; editable namespace
input is briefly coalesced. Namespace-list permissions do not prevent loading a
known namespace. A safe warning explains the manual fallback. Initialization can
be canceled or retried with one button. Successful initialization is reused until
reconnection or explicit reinitialization. Old connection responses cannot change
a new connection's busy state, scope or results. Pods expose container readiness,
restart counts and waiting reasons instead of hiding failures behind a Running
phase. Details, bounded logs and events remain tied to the executed target; raw
process output is retained verbatim. Deployment restarts still require confirmation.
