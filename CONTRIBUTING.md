# Contributing

Contributions to Asgard Hermod are welcome. English is the primary language for repository documentation, issues, and pull requests. The application supports English and Simplified Chinese.

## Workflow

1. Discuss substantial features in a GitHub issue before implementation.
2. Fork the repository and base application changes on `dev`, which contains the current source. Create a focused branch in your fork.
3. Install the .NET 10 SDK and build on Windows.
4. Make focused changes and update relevant documentation.
5. Run `dotnet build Asgard.Hermod.slnx -c Release` and `dotnet format Asgard.Hermod.slnx --verify-no-changes`.
6. For UI changes, launch the application and verify resizing and keyboard navigation. Attach screenshots to your pull request; do not commit screenshots, design previews, temporary plans, or rendered design exports. Maintain the UI in its XAML, resources and behavior tests.
7. Describe the problem, resulting behavior, and verification performed in your pull request.

Merge validated release changes into `main` before publishing. GitHub Releases
are created only by the **Release MSIX** workflow running from `main`; `dev` is
for development. See the [release and Store packaging guide](docs/microsoft-store.md#build).

## Conventions

- Follow `.editorconfig`; use nullable reference types and C# 14.
- Keep one class per file and files under 400 lines where practical.
- Follow [AGENTS.md](https://github.com/BenLampson/Asgard.Hermod/blob/dev/AGENTS.md) and [the localization guide](https://github.com/BenLampson/Asgard.Hermod/blob/dev/docs/localization.md). All application-owned display text must use shared resources, with English and Chinese translations and English fallback.
- Document public APIs and non-obvious behavior following Asgard code conventions.
- Do not commit credentials, local settings, build outputs, or personal data.
- Add meaningful automated tests when introducing business behavior. New .NET unit tests should use xUnit v3.

By contributing, you agree that your contributions are licensed under the repository's Apache License 2.0 and that you will follow the Code of Conduct.

The complete project license is in [LICENSE](LICENSE). Preserve [NOTICE](NOTICE),
[third-party notices](THIRD-PARTY-NOTICES.md), retained licenses and modification
attribution when changing derived code or bundled assets. See [SUPPORT.md](SUPPORT.md)
for questions and [SECURITY.md](SECURITY.md) for private vulnerability reports.
