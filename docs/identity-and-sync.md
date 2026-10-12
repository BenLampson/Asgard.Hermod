# Heimdall identity and workspace synchronization

Hermod signs in through the system browser using the official
`Asgard.Heimdall.Client` 5.5.3 SDK, Authorization Code and S256 PKCE. The desktop
is a public native client and has no client secret. Passwords, MFA and account
selection remain on Heimdall's hosted pages.

The title bar and account window show the verified access token's `name` claim.
For Heimdall tenant users this is the username. Email, display name from another
API and `sub` are not substituted for a missing username. Authorization and
workspace ownership use stable identifiers, independently of the displayed name.

## Desktop behavior

Open **Account and synchronization** from the title bar account menu. Sign-in
keeps the local workspace. Instances are still created and edited locally;
uploading and downloading are explicit actions in the account window.

- Upload includes instances, connection parameters and their credentials, tags
  and instance/tag associations. Language preferences, local terminal output,
  logs, object contents and OAuth credentials are excluded.
- Use the same 12–128 character synchronization password on all devices. The
  password is not saved or sent to the server. Losing it makes the cloud copy
  unreadable. Uploading with another password replaces the cloud copy's password.
- A download replaces all local instances and tags. Close open connections first
  and check the explicit replacement confirmation. Decryption, schema, tag
  hierarchy and association validation all finish before the database is changed.
  Restoration is transactional and connection parameters receive new local DPAPI
  protection. Local settings and the login session are preserved.
- A stale device receives HTTP 409 instead of overwriting another device's
  changes. The first upload on a new device also conflicts if the cloud already
  contains data. Preserve any local changes before downloading the cloud copy.
  This first version provides full snapshots, rather than automatic merging.
- Ordinary sign-out clears this application's saved session and attempts to
  revoke its refresh and access tokens. A failed remote revocation is reported;
  it does not restore the local session. Other Heimdall applications stay signed in.

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
| Server grant / scope | `client_credentials` / `heimdall.directory.read` |
| Server audience | `heimdall-directory-api` |
| Application permission / default role | `hermod.sync` / `hermod.user` |
| Resource API | `https://hermod.mudou.tech` |

The tenant, active RSA signing key, application manifest version 1 and both
clients were created through the authorized Heimdall management tools. An initial
tenant account named `hermod-admin` was provisioned. Its password and the server
secret were generated cryptographically; the local provisioning copy is protected
with current-user DPAPI at
`%LOCALAPPDATA%\Asgard\Hermod\provisioning-secrets.dpapi`, outside the repository.
Use Heimdall's authorized account management to provision additional users or
reset the initial account's password. Hermod does not implement account creation.

The executable loads desktop settings from `identity` in `app.yaml`, with
`HERMOD_` environment and command-line overrides. `identity.apiServerAddress` is
an optional IP routing override used during DNS propagation. The request still
uses the API URL's host and SNI and performs ordinary TLS certificate validation.
Leave it empty to use system DNS. Production API routes are never inferred from
the issuer address.

## Resource API

All endpoints require a verified user Bearer token from the registered desktop
client and the exact configured tenant/application. The server rejects service
tokens and performs a fresh, authenticated Heimdall directory permission check
using its separate server client. Disabled users, missing permission and directory
failures fail closed. No caller-provided tenant or user identity determines storage.

| Method | Route | Result |
| --- | --- | --- |
| GET | `/api/me` | Current stable identity and JWT username |
| GET | `/api/sync/workspace` | Revision, encrypted snapshot and UTC update time |
| PUT | `/api/sync/workspace` | Save only when `expectedRevision` matches |

Responses use Asgard's `Response<T>` envelope. An empty workspace has revision
`"0"`; subsequent revisions are opaque strings. Uploads send
`{ "expectedRevision": "...", "workspace": { ... } }`. The server does not accept
a synchronization password or plaintext connection parameters. Errors are safe
resource-backed messages; the desktop localizes known status codes without
displaying server or SDK exception messages.

Format 1 uses PBKDF2-SHA256 with 600,000 iterations, a random 16-byte salt and
AES-256-GCM with a random 12-byte nonce and a 16-byte authentication tag. Additional
authenticated data binds the issuer, application, tenant and user. Plaintext is
limited to 4 MiB, and HTTP upload bodies to 8 MiB. The server saves only the
encrypted envelope, stable ownership, revision and UTC audit timestamps in its
dedicated SQLite database. A write transaction covers the version read and update.

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

The production directory is `/root/deploy/hermod` on MD-Core (`47.109.145.141`).
Its container is `asgard-hermod-starter`, with persistent data in `data/hermod-sync.db`.
The verified initial image is `asgard-hermod:0.1.0-20261012-heimdall-sync-r4`,
content digest `sha256:a1c248bcd578a51e4ab6602eedfc425ca3b8a0c954978d9d4a9fa32c450306b9`.
It was built on MD-Core; no registry publication is claimed.
Record the image content hash for each deployment. Keep a stopped copy of the
previous image and back up the database before future schema upgrades; rollback
must use a version compatible with the persisted schema.

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
