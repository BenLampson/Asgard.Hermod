# Microsoft Store packaging

Hermod is packaged as a full-trust WPF desktop application using the Windows SDK
command-line tools. The manifest is stored in
`packaging/Asgard.Hermod/Package.appxmanifest`; a Visual Studio packaging project
is not required.

## Product identity

These public values were assigned by Partner Center and must match the manifest:

| Manifest field | Value |
| --- | --- |
| Identity Name | `32960BenLampson.AsgardHermod` |
| Identity Publisher | `CN=7BDCFD81-1900-42BB-9976-ED316E4C3A15` |
| PublisherDisplayName | `BenLampson` |

The Store display name is `Asgard Hermod`. The package declares English and
Simplified Chinese; the application still selects its language through the shared
localization services. The initial package version is `0.1.0.0`. Increment the
version for each submission and keep the fourth component at zero, as required
by the Store. The initial minimum Windows build is `10.0.19045.0`.

## Build

For repeatable builds, `.github/workflows/release-msix.yml` runs on a Windows
GitHub-hosted runner. Push a tag such as `v0.1.1` after merging the changes; the
workflow converts it to package version `0.1.1.0`, builds the x64 package, stores
it as a workflow artifact, and attaches it to a GitHub Release. You can also run
the workflow manually from **Actions → Release MSIX → Run workflow** and provide
an explicit four-part package version. Manual runs create an artifact but do not
create a GitHub Release.

This automation publishes the GitHub Release only. It does not log in to Partner
Center or submit a Store update. Upload the generated `.msix` to the existing
Partner Center submission after reviewing the package and Store listing. A future
Store submission can be automated separately with a Partner Center service account
and repository secret; do not put those credentials in the repository.

Install the .NET 10 SDK and a Windows SDK containing `MakeAppx.exe`. From the
repository root, run:

```powershell
powershell.exe -NoProfile -File packaging/Asgard.Hermod/Build-Msix.ps1
```

The script publishes a self-contained, untrimmed x64 application, retains the
Chinese satellite resources, derives package icons from the existing brand PNG,
and packs the output with manifest validation enabled. No separately installed
.NET Desktop Runtime is required on the user's machine.

To select a version or build a separate ARM64 package:

```powershell
powershell.exe -NoProfile -File packaging/Asgard.Hermod/Build-Msix.ps1 -PackageVersion 0.1.1.0
powershell.exe -NoProfile -File packaging/Asgard.Hermod/Build-Msix.ps1 -Architecture arm64
```

Each architecture produces a separate `.msix` in `artifacts/msix/`. Upload only
architectures that have been tested. The script retains a unique staging folder
for inspection; all build outputs are ignored by Git. If SDK discovery does not
match your installation, pass `-MakeAppxPath` with the full path to the tool.

## Installation testing and submission

The generated package is unsigned. Microsoft Store signs the package when
publishing it; no production signing certificate or private key is stored in the
repository. For local installation, sign a copy using a trusted test certificate
whose subject exactly matches the manifest Publisher, then use `Add-AppxPackage`.
Keep private keys, `.pfx` files and passwords outside the repository.

Before submission, test the installed package on a clean Windows machine:

- Launch from Start and verify English, Chinese, language preference persistence
  and English fallback for unsupported display languages.
- Verify SQLite creation and persistence, DPAPI-protected credentials, browser
  login and its loopback callback, object transfers, previews and external opening.
- Verify local ConPTY terminal sessions and package update behavior. Packaged
  desktop apps may virtualize per-user file paths; verify migration from an
  existing unpackaged installation rather than assuming both share a database.
- Run Windows App Certification Kit and review its results.

In Partner Center, select **Start submission**, upload the `.msix` under
**Packages**, and complete availability, properties, age ratings, privacy details,
and the English/Chinese Store listings with screenshots. The `runFullTrust`
restricted capability is needed for the WPF process, terminal and local file
operations; explain these functions if the submission asks for justification.
Generating a package does not submit or publish the app.

References: [manual desktop packaging](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-manual-conversion),
[MakeAppx](https://learn.microsoft.com/en-us/windows/msix/package/create-app-package-with-makeappx-tool),
[Store package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements).
