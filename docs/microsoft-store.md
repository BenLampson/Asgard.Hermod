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
localization services. The initial public package version is `1.0.0.0`. Increment the
version for each submission and keep the fourth component at zero, as required
by the Store. The initial minimum Windows build is `10.0.19045.0`.

## Privacy policy URL

The English and Simplified Chinese privacy policy is maintained together in
[`PRIVACY.md`](../PRIVACY.md). It explains that local use does not require a
Hermod account, that workspace configurations stay local without signing in,
and that cloud configuration synchronization requires signing in and setting a
synchronization password. Remote connections and AI requests still send the data
needed for those operations to the services selected by the user.

In Partner Center, choose **Provide a privacy policy URL** and enter:

<https://github.com/BenLampson/Asgard.Hermod/blob/main/PRIVACY.md>

The repository and this document must remain public so customers and certification
reviewers can read the policy without signing in. Maintain policy changes on
`dev` and publish the updated document to `main` before submitting changes to the
Store. Keep the policy aligned with the submitted application's data handling
behavior. Privacy requests go to
`cn.benlampson@gmail.com`.

For **Website**, use <https://github.com/BenLampson/Asgard.Hermod>. For **Support
contact info**, use `cn.benlampson@gmail.com`. The public [support guide](../SUPPORT.md)
collects the same contacts and explains how to report a problem without exposing
credentials.

## Build

Develop on `dev`, then merge the validated release changes into `main`. Only
`main` may publish a GitHub Release. In **Actions → Release MSIX → Run workflow**:

1. Select branch **main** and enter a new tag such as `v1.0.1`.
2. Leave **Publish a GitHub Release after all checks pass** unchecked to run a
   rehearsal. This builds an Actions artifact without creating a tag or Release.
3. Check that option when ready to publish. The workflow converts the tag to
   package version `1.0.1.0`, checks out the exact `main` commit selected for the
   run, verifies it is still in remote `main`, then runs build/format/test gates
   and creates the x64 package. Only after every gate passes does it create the
   tag and Release together with the package and generated release notes.

The owner must start or rerun the workflow. Selecting `dev` skips the release
job. The `release` environment independently permits only branch `main`, with
no tag deployment rules. `tools/Test-ReleaseSource.ps1` rejects another branch,
an unmerged commit, an unexpected checkout or an existing publication tag. It
rechecks the source immediately before publishing. A failed build creates no
Release. Published Release events and tag pushes do not trigger this workflow,
and existing release attachments are never overwritten by it.

This automation publishes the GitHub Release. It does not log in to Partner Center or
submit a Store update. Upload the generated `.msix` after reviewing the package.
The package is unsigned and intended for Store submission, not direct installation.

## Repository access and review

The personal repository currently has only `BenLampson` as a write-capable user.
GitHub's Release UI has no per-branch publishing restriction. The active
**Release tags via main workflow** tag ruleset therefore restricts creation and
updates of all tags to the GitHub Actions app. Human accounts cannot create a
new tag from `dev` through Git, the API or the Release UI. Existing tags must
already point to commits merged into `main`; version `v1.0.0` satisfies this
condition. Use the main-only workflow above for new releases.

Keep write access limited to the owner. Repository administrators can change
these settings, and write-capable workflows can change tags, so adding
collaborators, integrations or release workflows requires reviewing this policy.

`.github/CODEOWNERS` assigns every file to `@BenLampson`. Branch protection for
`main` and `dev` requires one approval from the code owner and dismisses stale
approvals after new changes. Owner/admin bypass remains enabled because GitHub
cannot accept a person's approval of their own PR. Contributors must submit PRs
for owner review; the owner can explicitly bypass protection for their own changes.

## Local build

Install the .NET 10 SDK and a Windows SDK containing `MakeAppx.exe`. From the
repository root, run:

```powershell
powershell.exe -NoProfile -File packaging/Asgard.Hermod/Build-Msix.ps1
```

The script publishes a self-contained, untrimmed x64 application, retains the
Chinese satellite resources, derives package icons from the existing brand PNG,
includes the project license, copyright notices, privacy policy and the retained
upstream terminal license, and packs the output with manifest validation enabled.
No separately installed
.NET Desktop Runtime is required on the user's machine.

To select a version or build a separate ARM64 package:

```powershell
powershell.exe -NoProfile -File packaging/Asgard.Hermod/Build-Msix.ps1 -PackageVersion 1.0.1.0
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
