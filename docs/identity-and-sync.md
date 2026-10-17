# Heimdall identity and workspace synchronization

Hermod signs in through the system browser using the official
`Asgard.Heimdall.Client` 5.6.0 SDK, Authorization Code and S256 PKCE. The desktop
is a public native client and has no client secret. Passwords, MFA and account
selection remain on Heimdall's hosted pages.

The title bar and account window show the verified access token's `name` claim.
For Heimdall tenant users this is the username. Email, display name from another
API and `sub` are not substituted for a missing username. Authorization and
workspace ownership use stable identifiers, independently of the displayed name.

## Desktop behavior

Open **Account and synchronization** from the title bar account menu. The same management window
is available from **Settings → Account**. Opening the window does not start browser
authentication. Signed-out users can choose **Sign in to your account** or
**Register account**; only an explicit sign-in action opens the browser.
Settings show the current verified
username, sign-in status and workspace ownership, updating when the account
changes. Signed-in accounts show **Cloud workspace** even when synchronization
still needs a password or is offline; this label does not report sync success.
On first sign-in, Hermod asks for a synchronization password. After validation,
it is protected with current-user Windows DPAPI in SQLite `app_setting`,
separately for each issuer/application/tenant/user. Reopening Hermod restores the
saved password and resumes synchronization without another password prompt.
Existing signed-in installations show **Set synchronization password** until
unlocked once. Cancelling password setup keeps changes locally and shows that
cloud synchronization still needs a password.

The instance creation/editing wizard does not offer a save-location choice.
Its save-location step and confirmation describe the result determined by the
current account: signed-out configurations remain local; signed-in configurations
belong to the account and have a protected local offline copy. When the password
is not configured, the hint explicitly describes this pending state rather than
claiming that an upload succeeded. Saving locally never requires token refresh or
network access. A draft opened under a different account cannot be submitted into
the replacement account.

- Instance and tag changes queue synchronization with a two-second delay. The
  background service also checks for other devices every 30 seconds. Offline or
  failed requests retain local changes and retry. **Sync now** checks immediately.
- The title bar account menu shows a compact synchronization badge beside the
  sign-in badge: password required, pending, synchronizing, synchronized, conflict,
  or failed. Its tooltip shows the full status message. The account window shows
  the last fully successful synchronization time.
- Each instance has its own cloud status mark. Signed-out local mode and the
  built-in local terminal have no cloud mark. A green cloud check means its
  configuration and related tags match confirmed cloud content, using the last
  successful baseline until a fresh cloud read completes. New or edited content
  remains pending until confirmed. An unrelated instance's pending edit does not
  clear an already synchronized instance's mark. Tooltips distinguish password
  required, pending, synchronizing, failed, and conflict states.
- Each device keeps a DPAPI-protected last synchronized baseline. The client
  compares baseline, current local content and latest cloud content by stable IDs
  and fields. Independent changes merge automatically; identical final values
  do not conflict. Modify/delete and differing changes to the same field require
  a per-item decision. Connection parameter previews remain hidden.
- **Resolve synchronization conflicts** lists the actual conflicting entries.
  Every item requires a choice before **Merge and synchronize** is enabled.
  Cancelling preserves both sides. A changed cloud revision or changed local
  content invalidates previous choices and recalculates conflicts. If combined
  tag names, hierarchy or associations are invalid, the dialog explicitly asks
  which complete configuration to retain.
- Before uploading or applying a merge, Hermod validates the complete snapshot.
  Conditional restoration compares the current local content inside the write
  transaction, so an edit made during network requests cannot be overwritten.
  Failed application does not advance the shared merge baseline.
- Switching accounts saves the departing account's protected offline copy and
  restores the arriving account's own copy. A new account does not inherit a
  different signed-in account's data. First sign-in imports the signed-out local
  configuration while preserving that separate local workspace. Signing out
  returns to the signed-out workspace. Ownership and workspace replacement commit
  in the same SQLite transaction.
- **Forget saved password** removes the remembered password and pauses automatic
  synchronization without deleting local configurations or the merge baseline.
  It works offline. Use the same 12–128 character password on every device. The
  password is never sent to the server. Forgetting the local password does not
  reset the cloud password; this version does not offer password rotation.
- Synchronization includes instances, connection parameters, tags and their
  associations. Language preferences, terminal output, logs, object contents and
  OAuth credentials are excluded.
- The old manual snapshot endpoints remain for pre-migration clients. Once an
  account migrates to detailed storage, old uploads return 409 instead of replacing
  the new data. The old encrypted snapshot is decrypted and migrated by the client
  that knows its password, never by the server.

The account window shows the verified JWT username and **Signed in** status;
the sign-in action becomes **Switch account**. The browser callback shows a
localized Hermod result page only after token validation and local session storage
complete. Failed or cancelled authentication shows safe localized messages,
without protocol fields or raw exceptions.

OAuth sessions use current-user DPAPI and the local SQLite `app_setting` table.
Restoration validates the saved issuer/client binding and JWT, or refreshes an
expired session with the original `sub`/`sid`. Refresh rotation is saved before
publishing the new account. Login, refresh, sign-out and synchronization are
serialized so a late result cannot change the operation's account.

The SDK validates signature, issuer, audience, client, tenant, application, token
type and lifetime. PKCE transactions are kept in memory and consumed once by the
loopback listener. The listener binds only the registered IPv4 loopback URI,
checks state and method, preserves duplicate callback fields for SDK rejection,
times out after three minutes and is cancelled when the account window closes.

The desktop calibrates small clock drift using the configured HTTPS issuer's
Date response for the SDK's time provider. It keeps JWT lifetime validation
enabled; drift above two minutes is rejected. Regional formatting and Windows
system time are not changed.

## Registered production identity

| Setting | Value |
| --- | --- |
| Tenant | Hermod, `a922e624-c532-43ae-b4a5-c4f9153f24cf` |
| Application | `hermod`, `083f96e2-a9d7-4300-9386-f7ef470d2378` |
| Authority | `https://idp.mudou.tech/a922e624-c532-43ae-b4a5-c4f9153f24cf` |
| Desktop client | `8d793a47-0790-4a1d-a535-4382e32bc1c7`, public/native |
| Desktop callback | `http://127.0.0.1:43821/callback/` |
| Desktop scopes | `openid profile offline_access hermod.sync` |
| API audience | `hermod-api` |
| Server client | `34c7b580-1f77-4753-b7f5-654b856170fb`, confidential |
| Server grant / scopes | `client_credentials` / `heimdall.directory.read`, `heimdall.users.create` |
| Separate server audiences | `heimdall-directory-api`, `heimdall-users-api` |
| Application permission / ordinary default role | `hermod.sync` / `hermod.user` |
| Resource API | `https://hermod.mudou.tech` |

The tenant, active RSA signing key, application manifest version 1 and both
clients were created through the authorized Heimdall management tools. An initial
tenant account named `hermod-admin` was provisioned. Its password and the server
secret were generated cryptographically; the local provisioning copy is protected
with current-user DPAPI at
`%LOCALAPPDATA%\Asgard\Hermod\provisioning-secrets.dpapi`, outside the repository.
Use Heimdall's authorized account management to provision additional users or
reset the initial account's password. Hermod's account window also offers **Create
account** for ordinary users, using the separate server-side provisioning flow below.

## Account registration

Signed-out users can choose **Create account** in **Account and synchronization**.
The form collects a username, email and 12–128 character account password, with a
local password confirmation. This account password is separate from the configuration
synchronization password. Neither registration password field is persisted locally;
both are cleared after each submission and when the dialog is disposed. Registration
does not sign in or replace a session. The user explicitly chooses **Sign in to your
account** to continue through the existing browser OIDC flow.

The desktop sends a single HTTPS `POST /api/account/register` to Hermod. The public
API accepts only `username`, `email` and `password`, rejects unknown fields and limits
requests to 8 KiB and twenty registration attempts per minute across the instance.
Successful responses contain only the normalized username. Validation, duplicate
accounts, rate limits and unavailable provisioning are rendered with shared English
and Chinese resources; raw Heimdall responses and exceptions never reach the UI.
Uncertain network results disable resubmission and advise trying sign-in first.
Neither the desktop HTTP pipeline nor the official SDK automatically retries creation.

The API uses `HeimdallClientFactory.CreateUserProvisioning("hermod-registration")`
from the official server SDK. This named client requests only `heimdall.users.create`
with audience `heimdall-users-api`. The existing `hermod-directory` client continues
to request only `heimdall.directory.read` with its own audience and Token cache. Both
read the existing server credential through `IHeimdallCredentialProvider`; no service
secret is shipped to the desktop or included in the image or publish payload.

Before enabling the flow in production, explicitly grant the registered server Client
the provisioning scope, configure a Tenant role with `isUserDefault=true` and
`isDefault=false` in the current application Manifest, and synchronize the tenant
application. For Hermod, the ordinary role is `hermod.user` with only `hermod.sync`.
Heimdall determines ownership and roles from its verified service Token and current
application RBAC; the registration request cannot supply those fields. A missing or
disabled role, scope or binding fails closed. There is no separate Hermod registration
switch or registration credential: deploying the API exposes this constrained public
entry point, and the existing private `.env` supplies its service credential.

The executable loads desktop settings from `identity` in `app.yaml`, with
`HERMOD_` environment and command-line overrides. `identity.apiServerAddress` is
an optional IP routing override used during DNS propagation. The request still
uses the API URL's host and SNI and performs ordinary TLS certificate validation.
Leave it empty to use system DNS. Production API routes are never inferred from
the issuer address.

## Resource API

Current-account and synchronization endpoints require a verified user Bearer token from the registered desktop
client and the exact configured tenant/application. The server rejects service
tokens and performs a fresh, authenticated Heimdall directory permission check
using its separate server client. Disabled users, missing permission and directory
failures fail closed. No caller-provided tenant or user identity determines storage.

| Method | Route | Result |
| --- | --- | --- |
| POST | `/api/account/register` | Public, rate-limited ordinary account creation; normalized username, without credentials or user Token |
| GET | `/api/me` | Current stable identity and JWT username |
| GET | `/api/sync/configuration` | Collection revision, individually versioned instances/tags, encrypted password verification envelope, UTC update time; legacy ciphertext when awaiting migration |
| PUT | `/api/sync/configuration` | Atomically commit the complete merged collection when collection and entry versions match |
| GET / PUT | `/api/sync/workspace` | Legacy encrypted snapshot protocol, uploads blocked after detailed migration |

Responses use Asgard's `Response<T>` envelope. An empty workspace has revision
`"0"`; subsequent collection and entry versions are opaque strings generated by
the server. Uploads send `{ "expectedRevision": "...", "configuration": { ... } }`.
The server binds storage to the verified identity; ownership and audit fields are
server controlled. A SQLite write transaction covers version reads, all upserts,
removals, and collection revision updates. Unmodified entry versions and creation
audits are preserved. Any stale version rejects the complete upload with HTTP 409.

Schema version 2 adds `sync_configuration`, `sync_instance`, and `sync_tag`. Each
instance and tag is a separate readable JSON record with its own version and
server create/update audit timestamps. Names, types, providers, tag relationships
and parameters explicitly declared non-sensitive by a registered connector are
readable. Sensitive fields, secret input fields and undeclared parameters are
encrypted on the client. Access key IDs marked sensitive stay encrypted too.

Credential envelopes use PBKDF2-SHA256 with 600,000 iterations and AES-256-GCM.
A password verification envelope supplies the random 16-byte salt; the client
validates it and derives a shared key once per operation. Each instance envelope
uses a fresh random 12-byte nonce and 16-byte authentication tag. Authenticated
data binds issuer/application/tenant/user, instance ID, type and provider, so an
envelope cannot be moved to another instance or connector. Unchanged instances
reuse their original envelope to avoid artificial changes. Ordinary uploads
cannot replace an existing verification envelope. Legacy format-1 whole-snapshot
encryption remains readable for migration.

Each encrypted payload is limited to 4 MiB and HTTP request/response bodies to
8 MiB. Validation rejects duplicate identifiers, invalid tag hierarchies and
associations, malformed envelopes and excessive payloads before storage. Unknown
server/SDK errors use safe localized desktop messages; raw response messages are
never shown.

Schema version 3 adds nullable `sync_configuration.agent_models_json` for the
separately encrypted model-profile collection. An omitted field from an older
client preserves the stored envelope; an encrypted empty collection explicitly
clears the profiles. The desktop requires the API to acknowledge the exact
uploaded envelope before advancing its baseline. A schema-2 server cannot
acknowledge these profiles, so the desktop retains local changes and reports a
synchronization failure until the server is upgraded.

The desktop and API must be upgraded together for the detailed protocol. The
October 13 production deployment below includes the detailed API; older desktop
clients remain compatible until their account migrates to detailed storage.

## Docker deployment

The API runs independently of the desktop through `Asgard.Hermod.Server` and
`Asgard.Hermod.Server.Starter`, using Yggdrasil and the shared response/identity
contracts. It does not reference WPF or desktop SQLite persistence.

Build the Linux payload, place it in a Docker build context's `publish` directory,
and copy [the Dockerfile](../Docker/server/Dockerfile) alongside it:

```powershell
dotnet publish src/Asgard.Hermod.Server.Starter -c Release -r linux-x64 --self-contained false -o artifacts/publish/server-linux-x64
```

Use [the Compose file](../Docker/server/compose.yaml) and an uncommitted mode-600
`.env` containing `HERMOD_SERVER_IMAGE`, `HERMODSERVER__SERVICECLIENTSECRET`,
`ASGARD__ENCRYPTION__KEY` and `ASGARD__ENCRYPTION__IV`. The latter two are required
Asgard host settings and do not decrypt user workspace snapshots. Persist `/data`
and give its host directory to UID 1654. The service joins the existing
`CoreServer` network with alias `hermod`; port 5086 is not published to the host.
Nginx terminates TLS through [the domain configuration](../Docker/server/hermod.mudou.tech.conf).

The server secret is injected at container runtime through that private `.env`, never
through a Docker build argument, image layer, source file or desktop setting. Keep
`HERMODSERVER__SERVICECREDENTIALVERSION` synchronized with deliberate secret rotations;
the named SDK clients then stop reusing the previous version's cached Tokens.

The production directory is `/root/deploy/hermod` on MD-Core (`47.109.145.141`).
Its container is `asgard-hermod-starter`, with persistent data in `data/hermod-sync.db`.
The verified initial image is `asgard-hermod:0.1.0-20261012-heimdall-sync-r4`,
content digest `sha256:a1c248bcd578a51e4ab6602eedfc425ca3b8a0c954978d9d4a9fa32c450306b9`.
It was built on MD-Core; no registry publication is claimed.
The detailed synchronization API was deployed on October 13, 2026, at 14:49
(Asia/Shanghai) as `asgard-hermod:0.1.0-20261013-autosync-r1`, content digest
`sha256:3e012b47ec2f97e67b88751d23e759ab08f4628b50b45d9f924136681756b2b5`.
The Linux publish payload was built from this feature's source and the image was
built on MD-Core; no registry publication is claimed. Its payload archive SHA-256
is `3824aa6dd09cb68baff4b3c5bfb468b1bab1d433fd8a0349f6f8821fe09bd669`.

Before replacement, the old container was stopped and a temporary schema-1
SQLite backup was made. Startup migrated the database to schema 2; integrity
checks passed and the original legacy record matched that temporary backup
exactly before client migration. The new container uses the existing UID 1654,
volume, network, reverse proxy and credentials.

After production verification, the owner requested removal of retained backups
and rollback images. The server's temporary database/environment backup, stopped
rollback container, all four superseded Hermod image tags, transferred archives
and build directories were removed. Only the current running Hermod image is
retained. Cleanup compared the business table contents before and after removal:
they were identical, database integrity was `ok`, and the current container was
not restarted. Non-secret release and verification metadata remain in
`/root/deploy/hermod/release.json`.

The final database contains one account configuration, one original instance and
three tags, with no legacy snapshot or probe instance. No server backup or
rollback container is retained for this deployment. Automated approval review
blocked removal of the two local verification databases and protected snapshot
under `artifacts/production-sync-probe/data`; those ignored local files still
require manual cleanup. They are not used by the running service.

### Model configuration deployment: October 17, 2026

At 00:10 (Asia/Shanghai), the API was upgraded to
`asgard-hermod:0.1.0-20261017-model-sync-r1`, built from server source at commit
`9e751be059df5b56f90efe90b1f4f115ee657735`. Its image digest is
`sha256:a48993cdefaab401cd8a8ec5ac4c26ca45149e89be883491ac269231c6689885`;
the Linux payload archive SHA-256 is
`25125a777981a335c7e822f3a19113db01e977ed85b8550f8067e8a74d180e4d`.
The image was built on MD-Core and was not published to a registry.

Startup migrated the production SQLite database from schema 2 to schema 3.
Integrity checks passed before and after migration; the existing one account
configuration, eight instances and three tags remained. HTTPS `/health` returned
200, and unauthenticated `/api/sync/configuration` returned 401, with ordinary TLS
certificate validation enabled. The Release solution build, formatting check,
all 59 synchronization tests and Linux publish passed before deployment. The
solution build used a separate output directory because the desktop was running.

After deployment, the already-running desktop automatically uploaded a model
configuration envelope, observed in the previously empty new column. A read-only
comparison against the stopped-database backup confirmed that all existing
instance and tag rows, account ownership and password-protection envelopes were
unchanged. The new container remained running with zero restarts.

The stopped-database backup and previous configuration are retained under
`/root/deploy/hermod/backups/20261017-model-sync-r1`; the previous image is also
retained. Restoring that database would discard subsequent writes, so preserve
new cloud data before any rollback. Current non-secret deployment and verification
metadata are recorded in `/root/deploy/hermod/release.json`.

### Synchronization deployment: October 17, 2026, 10:29

After the desktop card and SQL fixes were committed and pushed, the API was
redeployed from commit `f7626de345c8927e2b924fdce503ff0e199fa180`. The Linux
payload was published from a source export of that commit, with no uncommitted
workspace files included. The production image is
`asgard-hermod:0.1.0-20261017-sync-f7626de`, content digest
`sha256:0633a24a27765b928775573198ce2505fc73ed21afcb935a03360e653ad06051`.
The transferred payload archive SHA-256 is
`7ba8f82993196806cb9fc8af7c5edc3f5bd2152edc66da99525c660ac71590c1`.
The image was built on MD-Core and was not published to a registry.

This deployment retains schema 3 and the existing cloud synchronization
protocol. MQ SSH private-key file import is implemented in the desktop; the
server stores its encrypted credential envelope. Existing path-only MQ
configurations must be tested and saved once using the updated desktop on the
device that has the original key file, then synchronized.

The Release Linux publish and all 60 synchronization tests passed. Before
replacement, the existing service was stopped and its SQLite database was
backed up through SQLite's backup API. Integrity checks passed and complete
business-table hashes matched before and after replacement: one account
configuration, nine instances and three tags remained unchanged. Both the
server-side reverse-proxy checks and an external HTTPS check returned 200 for
`/health` and 401 for unauthenticated `/api/sync/configuration`. The checks used
an explicit address override for the domain while retaining normal TLS
certificate validation; they do not verify public DNS resolution. The new
container was running with zero restarts.

The database backup and previous environment, Compose configuration and release
record are retained under
`/root/deploy/hermod/backups/20261017-sync-f7626de`, with access restricted to
root. The previous `20261017-model-sync-r1` image is retained for rollback. The
current non-secret release metadata, including source commit, image identity,
payload checksum and database verification hashes, is recorded in
`/root/deploy/hermod/release.json`. Roll back the image/configuration first;
restoring the database backup would discard subsequent synchronization writes.

### Current production deployment: October 17, 2026, 12:52

The registration API code was deployed to the existing MD-Core directory from commit
`5e51d2fab04163625898a7b19cadc4a17ff3c1d8` as
`asgard-hermod:0.1.0-20261017-registration-r1`. Its image ID is
`sha256:ffb2903b71f8a7cc7c079d55d928bedef23be89bd1f85244a2a7297897862eea`;
the transferred payload SHA-256 is
`cdc8c14fe585afc1c934372ef951b1051bbd8a0668fdec3be89fe11dbec958f5`.
The image was built on that server and was not published to a registry.

The container is running with zero restarts. Verified HTTPS responses are health
200, anonymous current-account and configuration access 401, invalid registration
and role injection 400, and oversized registration 413. SQLite remains schema 3
with integrity `ok`, one account configuration, nine instances and three tags.
All four business tables have identical content hashes before and after replacement.
The private environment, Compose file, volume, network and existing identity settings
were retained; only the image selection changed. The stopped-database backup,
previous environment and previous image are retained under
`/root/deploy/hermod/backups/20261017-registration-r1` for recovery.

Release build and the full formatting check passed. All 87 synchronization and
registration tests and 38 related WPF/localization tests passed, including official
C# SDK service-token validation, failure classification, no POST retries and real
English/Chinese window layouts. A separate actual Yggdrasil/Kestrel process confirmed
the same HTTP input and authentication boundaries before deployment.

Production identity configuration was approved and applied at 12:53. Manifest v2
marks `hermod.user` as `isDefault=false`, `isUserDefault=true`, retaining only
`hermod.sync`. The tenant application is Enabled and synchronized to v2 with
authorization version 1. The existing confidential server Client explicitly has both
directory-read and user-create scopes; its default scope remains directory-read,
and its secret and desktop OIDC registration were not changed.

At 12:53:49, a separate official C# SDK 5.6.0 probe validated a genuine provisioning
Token, including signature, issuer, audience, service type, scope and tenant/application
binding. An invalid-email create request then returned HTTP 400. No production user
was created. The public API checks, zero restarts and unchanged business-table hashes
were reconfirmed. Successful creation is covered by Hermod's local signed-protocol
tests and Heimdall 5.6.0's isolated real-image acceptance, rather than a production
test account. Deployment and verification metadata are recorded in
`/root/deploy/hermod/release.json`.

## Verification

Automated tests cover real RS256 token validation, PKCE exchange, cancellation,
session restore/refresh/identity changes, API boundary rejection, online permission
failures, competing uploads, DPAPI persistence, invalid snapshot preservation and
real English/Chinese WPF window layouts. The production registration is also built
with Yggdrasil, independently of the HTTP test host.

The October 12, 2026 integration run verified a real hosted Heimdall PKCE login,
the `hermod-admin` JWT username, server-client directory authorization, HTTPS
current-user response and encrypted upload/download with actual local SQLite
restoration. The API connection used the configured server IP while preserving
the domain's certificate verification; public DNS propagation is a separate check.

The October 13, 2026 production verification used the existing test account and
two isolated local SQLite directories to simulate devices. Both used genuine
hosted Heimdall Authorization Code + PKCE authentication with the official SDK,
JWT validation, real DPAPI persistence and the production HTTPS API. The existing
one-instance workspace migrated without changing its logical content. The run
verified automatic uploads after instance creation and editing, cross-device
decryption, independent name/credential merging, and same-field conflict handling
through the real WPF account and merge view models/windows. Cancelling retained
both versions; explicit cloud selection merged and synchronized successfully.
Recreating the client service container restored the login and saved password and
resumed background synchronization without another password prompt. Only the
newly created probe instance was removed afterwards; the original instance and
all three tags remained, and database integrity passed with no container restart.
Credentials, synchronization passwords and local verification artifacts are not
part of the repository or image.

The verification connection used the configured production IP while preserving
the `hermod.mudou.tech` hostname, SNI and certificate validation. This workstation's
local proxy resolves the hostname to a virtual address that failed its direct TLS
handshake; verification therefore does not claim that the proxy's DNS route works.
