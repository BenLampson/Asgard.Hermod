# Asgard Hermod

**English** | [简体中文](README.zh-CN.md)

A Windows desktop workspace for developers and operators. Manage SSH sessions,
databases, message queues and object storage in one application, with optional
AI assistance and configuration synchronization across devices.

[Source code](https://github.com/BenLampson/Asgard.Hermod/tree/dev) ·
[Documentation](https://github.com/BenLampson/Asgard.Hermod/tree/dev/docs) ·
[Privacy policy](PRIVACY.md) · [Support](SUPPORT.md) ·
[Apache License 2.0](LICENSE)

## Features

| Workspace | Capabilities |
| --- | --- |
| Local terminal | Windows ConPTY sessions, terminal tabs, text selection and keyboard navigation. |
| SSH | Terminal sessions, SFTP file transfers, port forwarding, Linux monitoring and container administration. |
| MySQL and PostgreSQL | Database object browsing, independent SQL sessions, query results, table editing and execution plans. |
| Redis | Redis 7+ connections, topology discovery, key browsing and value editors. |
| RabbitMQ | Management API connections, resource administration and explicit message inspection. |
| Object storage | Alibaba Cloud OSS, Tencent COS, Volcengine TOS, AWS S3, MinIO and custom S3-compatible services; uploads, downloads, previews and temporary links. |
| AI assistant | Your own compatible model service for SQL assistance, log analysis and SSH troubleshooting, with confirmation for operations that require approval. |
| Personal workspace | Instance organization, tags, light and dark themes, English and Simplified Chinese, and optional encrypted credential synchronization. |

## Use without an account

**A Hermod account is optional.** You can use local and remote resource management
features, including your configured AI assistant, without registering or signing in.
The remote services you connect to still require their own credentials.

Without signing in, workspace configurations stay on your device and are not
uploaded to Hermod's account or configuration synchronization services. Sharing
those configurations across devices through Hermod cloud synchronization requires
signing in and setting a synchronization password.

Remote connections, file transfers and AI requests send the data needed for your
chosen operation to the services you configure. The [privacy policy](PRIVACY.md)
explains local storage, account services, synchronization and AI data handling.

## Availability and requirements

Microsoft Store publication is being prepared. See
[GitHub Releases](https://github.com/BenLampson/Asgard.Hermod/releases) for published
downloads when available, or build the development version from source below.

- Windows 10 version 22H2 (build 19045) or later, including Windows 11, for the
  current desktop package. WPF requires Windows.
- The current Store package targets x64 and includes the .NET runtime.
- Remote workspaces require access to your own servers or cloud services.
- AI features require a compatible model endpoint, model name and credentials
  where required by your provider.

The application supports English and Simplified Chinese, follows your Windows
display language by default, and remembers a manual language choice. Unsupported
languages and missing translations fall back to English. UI language is separate
from regional formatting.

## Build from source

The current application source and detailed development documentation are on
[`dev`](https://github.com/BenLampson/Asgard.Hermod/tree/dev). Install the .NET 10 SDK
on Windows; the SDK selection policy is defined in `global.json` on that branch.
Visual Studio with the .NET desktop development workload is optional.

```powershell
git clone --branch dev https://github.com/BenLampson/Asgard.Hermod.git
cd Asgard.Hermod
dotnet restore Asgard.Hermod.slnx
dotnet build Asgard.Hermod.slnx -c Release --no-restore
dotnet run --project src/Asgard.Hermod --configuration Release --no-build
```

Development builds keep local configuration in
`%LOCALAPPDATA%\Asgard\Hermod\hermod.db`. Packaged installations can use Windows
package-specific storage paths. See the
[local storage guide](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/local-storage.md).

For a framework-dependent publish, users need the .NET 10 Desktop Runtime. The
[Microsoft Store packaging guide](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/microsoft-store.md)
describes self-contained MSIX builds, package identity and submission checks.

## Documentation

| Topic | Guide |
| --- | --- |
| Architecture and project boundaries | [Module architecture](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/architecture.md) |
| SSH, SFTP, monitoring and containers | [SSH workspaces](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/ssh.md) |
| SQL workspaces | [MySQL](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/mysql.md), [PostgreSQL](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/postgresql.md) |
| Cloud storage connections | [Object storage providers](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/object-storage-providers.md) |
| Preview, external opening and temporary files | [File opening](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/file-opening.md), [storage settings](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/storage-settings.md) |
| Accounts and cross-device configuration | [Identity and synchronization](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/identity-and-sync.md) |
| Language selection and translations | [Localization](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/localization.md) |
| Terminal embedding and upstream attribution | [Terminal component](https://github.com/BenLampson/Asgard.Hermod/blob/dev/src/Asgard.Hermod.WinTerminal/README.md) |

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) and the
[agent instructions](https://github.com/BenLampson/Asgard.Hermod/blob/dev/AGENTS.md)
before changing the application. Repository documentation, issues and pull requests
use English by default. Application-owned display text requires English and
Simplified Chinese resources with English fallback.

Run the repository checks from `dev` before submitting code:

```powershell
dotnet build Asgard.Hermod.slnx -c Release
dotnet format Asgard.Hermod.slnx --verify-no-changes --no-restore
dotnet test --solution Asgard.Hermod.slnx --configuration Release --no-build
```

## Support, privacy and security

- Bugs and feature requests: [GitHub Issues](https://github.com/BenLampson/Asgard.Hermod/issues).
- Support and private inquiries: [cn.benlampson@gmail.com](mailto:cn.benlampson@gmail.com).
- Data handling and deletion requests: [Privacy Policy](PRIVACY.md).
- Vulnerabilities and sensitive reports: [Security Policy](SECURITY.md).
- Community participation: [Code of Conduct](CODE_OF_CONDUCT.md).

Do not include passwords, private keys, API keys or unredacted connection strings
in public issues. See [SUPPORT.md](SUPPORT.md) for useful diagnostic information.

## License

Copyright 2026 BenLampson and contributors.

Original project work is licensed under the [Apache License, Version 2.0](LICENSE).
See [NOTICE](NOTICE) for project attribution and
[Third-Party Notices](THIRD-PARTY-NOTICES.md) for bundled components and assets.
Third-party components retain their respective licenses and attribution notices.
Third-party names and logos belong to their respective owners; the project license
does not grant rights to their trademarks.
