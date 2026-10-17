# Data protection servicing

The server pins `Microsoft.AspNetCore.DataProtection.StackExchangeRedis` to
`10.0.13`. Asgard.PluginSdk 6.0.3 otherwise brings the 10.0.12 package through
Asgard.Yggdrasil.AspNetCore. The direct servicing pin also resolves DataProtection,
System.Security.Cryptography.Xml and System.Security.Cryptography.Pkcs to 10.0.13
in both Server.Starter and Sync.Tests. No audit suppression or warning downgrade
is used.

The patched versions are documented in Microsoft's
[October .NET release notes](https://github.com/dotnet/core/blob/main/release-notes/10.0/10.0.13/10.0.13.md)
and the [Pkcs advisory](https://github.com/advisories/GHSA-846f-f764-76mx) and
[Xml advisory](https://github.com/advisories/GHSA-7hqp-7c5m-4vq6).

Verify the resolved chain and current audit with:

```powershell
dotnet restore Asgard.Hermod.slnx
dotnet nuget why src/Asgard.Hermod.Server.Starter/Asgard.Hermod.Server.Starter.csproj System.Security.Cryptography.Pkcs
dotnet list Asgard.Hermod.slnx package --vulnerable --include-transitive --no-restore
```

The default Release build and format check pass with audit enabled. The sync test
suite verifies server composition, encryption, authentication and workspace APIs
without changing production services or data.
