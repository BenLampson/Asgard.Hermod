# SSH connections

SSH is an independent instance type. Its first page selects Direct, SSH jump host
or TCP proxy, with Direct selected by default. Next opens a separate connection
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

The default port is 22 and the connection timeout is 15 seconds. The test opens
and closes an actual interactive PTY through the same protocol factory as the
workspace. Saving remains blocked until the current configuration passes.
Changing any connection field invalidates that result. The shared workflow
derives device or account saving from sign-in state.

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

The independent window retains Hermod's shared 72-pixel caption and 28-pixel
status bar. Its caption exposes Edit configuration, Tools and Agent. There is
no duplicate asset navigation. Terminal, Files and Port forwarding use the
same verified connection and keep their content alive when switching sections.
Both English and Simplified Chinese and the shared light/dark themes apply to
all sections. Search, copy, paste, details and local font/display tools remain
available. Multiline terminal paste still requires confirmation.

Agent opens as a right-hand `WorkspaceSplitView`, with a mouse- and
keyboard-adjustable divider. It starts hidden. Closing the pane keeps the
conversation, draft and pane width; replies never reopen it or steal terminal
focus. Each workspace has an independent bounded conversation using the user's
configured model client. Host identity is public context; terminal text is sent
only when the user attaches a selection and opts to include it (up to 8 KiB of
characters). Passwords, keys, connection options and automatic terminal history
are never included. Explicit shell code blocks expose Insert and Run actions.
Insertion does not add a return. Running requires confirmation showing the
current host and full command; it uses a separate exec channel, bounded to
64 KiB and two minutes, and shows its output in the conversation. Reconnecting
invalidates old suggestions and the request history while preserving visible
messages and drafts.

Files use SFTP channels on the existing verified transport, without shell
interpolation. Browsing is nonrecursive and bounded to 10,000 entries. Names
are filtered locally; folder navigation also supports Enter. Upload, download,
new folder, rename and confirmed file/empty-folder deletion are available.
Uploads stage a unique remote temporary file before publishing and do not
overwrite existing targets. Downloads stage locally and replace the selected
destination only after successful completion. Cancellation preserves existing
destination files. Recent transfer results are bounded to 100 tasks; disconnect
and window disposal cancel ongoing operations. Deletion is never recursive.

A compact monitoring row reads Linux `/proc` through an independent exec channel
every five seconds with an eight-second timeout. It reports CPU, memory, the root
disk and aggregate non-loopback network throughput. Initial CPU/network rates
remain unavailable until a second sample; resets never create negative rates.
Missing `/proc`, permissions or unsupported systems show a localized unavailable
state. A collapsible bottom panel provides a two-minute sample history and
on-demand, read-only process, Docker, interface and mount details. Missing Docker
or `ip` support does not prevent terminal use. These features require no remote
Hermod daemon or additional authentication.

Port forwarding listens only on `127.0.0.1` and reuses the same verified SSH
client. Each forward has an explicit stop action. Disconnect, reconnect and
workspace disposal release all forwarding handles. The application does not
expose listening ports on external interfaces.

New session continues to open an independent workspace window. Closing an active
session asks for confirmation; cancel keeps it alive. Disconnect and remote exit
retain read-only terminal output. Reconnect rereads the saved configuration and
replaces the buffer only after successful connection; it never replays old input.
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
