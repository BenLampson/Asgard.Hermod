# Agent instructions for Asgard Hermod

## Internationalization is required

- Hermod is a multilingual Windows application. English (`en`) and Simplified Chinese (`zh-Hans`) are supported initially.
- The default is to follow the current Windows user's **display language**. A saved manual choice takes priority. Restore system following by saving an empty preference.
- **Unsupported languages fall back to English. Missing translations also fall back to the English resource.** Never fall back to Chinese or display a resource key as user-facing text.
- Store the language preference through `ILanguagePreferenceStore` in the local SQLite `app_setting` table. Apply it before constructing views and provider descriptors. Changes are saved immediately and apply after restarting Hermod; clearly communicate this in settings.
- Put every application-owned user-facing string in the shared resources under `src/Asgard.Hermod.Abstractions/Localization/`: pages, headings, buttons, menus, placeholders, tooltips, accessibility labels, statuses, validation, confirmations, progress and error messages.
- Use `Text.Get` / `Text.Format` in C#. Use `{loc:Loc ResourceKey}` in XAML, including binding `StringFormat`. Do not hard-code Chinese or English display text in views, view models, controls or services.
- Use complete sentence templates with numbered placeholders. Do not assemble translated sentences from fragments. Keep placeholder indices and formatting consistent across languages, allowing translators to reorder arguments.
- Business errors carry stable resource keys and safe arguments. Use `Text.Exception` for business validation and `ObjectStorageException.FromResource` for storage errors; render through `Text.Error`. Do not display raw `Exception.Message`, SDK responses, connection strings or credentials.
- Business logic must use stable IDs, error codes or enums; never compare translated labels to control behavior. Do not translate protocol fields, object keys, user-created names/tags, credentials, logs or output received from terminal processes.
- UI language is separate from regional formatting. Keep machine-readable timestamps, protocol numbers and persisted identifiers invariant.
- Add a new language by providing `Strings.<culture>.resx` and registering its ID, native name and Windows language prefixes in `Text.Languages`. Settings must derive its choices from that registry.
- Update both English and Chinese resources when adding or changing text. Verify language selection, persistence, English fallback, resource placeholders, safe errors, and the affected WPF views. Tests must not depend on the developer machine's display language.

## Repository conventions

- Work directly on `dev` unless the user explicitly requests otherwise. Keep only `dev` and `main` as local branches; do not create task branches or worktrees without an explicit request.
- Maintain the UI in its XAML, shared resources and behavior tests. Do not add retired design drafts, temporary plans, screenshots or rendered design exports to the repository. Keep shipped icons and other application assets in their owning modules.

- Use .NET 10 / C# 14, UTF-8 and CRLF, following `.editorconfig`. Document public APIs and non-obvious behavior with Chinese comments.
- Respect the existing module boundaries: shared contracts/resources in Abstractions, WPF presentation in UI modules, persistence in Persistence.Sqlite, provider protocols in provider modules, application composition in the executable.
- Read `CONTRIBUTING.md` and the relevant architecture documentation. Preserve unrelated work already in the workspace; keep prerequisite changes separate from the requested feature commit.
- Run the required build, formatting checks and meaningful xUnit v3 tests before committing.
