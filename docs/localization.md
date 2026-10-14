# Localization

Hermod supports English (`en`) and Simplified Chinese (`zh-Hans`). Startup reads
the current Windows user's display language and then the saved `ui.language`
preference from the local SQLite `app_setting` table. Manual selection takes
priority. Empty preferences mean **Follow Windows**. Unsupported system languages
or obsolete manual language IDs fall back to English.

Select **Settings → General → Language** to change the preference. It is saved
immediately and applies after restarting Hermod. Existing terminal sessions and
object transfers continue until the user closes the application. The page shows
the current language and explains when the saved preference takes effect.

## Shared resources

After loading the preference, the desktop host calls `Text.SetApplicationCulture`
to keep resource lookup stable across Dispatcher and asynchronous callbacks.
Windows regional number and date formatting remains independent. Tests can clear
this application override to exercise contextual resource lookup in isolation.

All modules use `Asgard.Hermod.Abstractions.Localization.Text`. The neutral
`Strings.resx` contains English; `Strings.zh-Hans.resx` contains Chinese. Standard
.NET satellite resource lookup falls back to English for missing translations.

```csharp
var title = Text.Get("Language");
var label = Text.Format("ObjectsSelected", count);
```

```xml
xmlns:loc="clr-namespace:Asgard.Hermod.UI.Localization;assembly=Asgard.Hermod.UI"
Text="{loc:Loc Language}"
Text="{Binding CheckedCount, StringFormat={loc:Loc ObjectsSelected}}"
```

Within the UI assembly itself, omit `;assembly=Asgard.Hermod.UI` from the namespace.
Use complete sentence templates so each language can reorder the placeholders.
Do not translate user data, identifiers, object keys, protocol codes or terminal
process output. UI resource culture does not override regional formatting culture.

## Errors and future languages

Use `Text.Exception(resourceKey, arguments)` for business validation and
`ObjectStorageException.FromResource(resourceKey, errorCode, arguments)` for
storage errors. Presentation boundaries call `Text.Error` to display translated
business messages and use translated fallback messages for unknown exceptions.
Service error codes remain unchanged and can be included through the shared
`MessageWithErrorCode` template.

To add another language, add `Strings.<culture>.resx` and a `LanguageOption` in
`Text.Languages`. Register its native name and matching Windows language prefixes.
The settings choices are generated from that registry. Missing entries fall back
to English; keep translated placeholder indices and formats equivalent. Add
language resolution and WPF rendering checks to the existing xUnit v3 suite.
