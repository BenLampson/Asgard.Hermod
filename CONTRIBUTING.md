# Contributing

Contributions to Asgard Hermod are welcome. English is the primary language for repository documentation, issues, and pull requests. The application will support multiple languages.

## Workflow

1. Discuss substantial features in a GitHub issue before implementation.
2. Fork the repository and create a focused branch.
3. Install the .NET 10 SDK and build on Windows.
4. Make focused changes and update relevant documentation.
5. Run `dotnet build Asgard.Hermod.slnx -c Release` and `dotnet format Asgard.Hermod.slnx --verify-no-changes`.
6. For UI changes, launch the application and verify resizing and keyboard navigation. Attach screenshots to your pull request; do not commit screenshots, design previews, or rendered design exports. Keep editable design changes in the relevant source under `design/`: `Hermod-NewVersion.pen`, `DBWindow.pen`, or `SSHWindow.pen`.
7. Describe the problem, resulting behavior, and verification performed in your pull request.

## Conventions

- Follow `.editorconfig`; use nullable reference types and C# 14.
- Keep one class per file and files under 400 lines where practical.
- Follow `AGENTS.md` and [the localization guide](docs/localization.md). All application-owned display text must use shared resources, with English and Chinese translations and English fallback.
- Document public APIs and non-obvious behavior following Asgard code conventions.
- Do not commit credentials, local settings, build outputs, or personal data.
- Add meaningful automated tests when introducing business behavior. New .NET unit tests should use xUnit v3.

By contributing, you agree that your contributions are licensed under the repository's Apache License 2.0 and that you will follow the Code of Conduct.
