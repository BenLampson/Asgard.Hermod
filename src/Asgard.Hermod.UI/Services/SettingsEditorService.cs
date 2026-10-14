using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Services;

/// <summary>宿主协调系统设置和子编辑窗体的所有者，界面只依赖标签服务。</summary>
public sealed class SettingsEditorService : ISettingsEditor
{
    private readonly Func<Window?> _getOwner;
    private readonly ITagManagementService _tags;
    private readonly ILanguagePreferenceStore _languages;
    private readonly IFileOpenRuleStore? _fileRules;
    private readonly ITemporaryFileStorage? _temporaryFiles;
    private readonly IAccountService? _accounts;
    private readonly IAccountEditor? _accountEditor;

    /// <summary>延迟获取应用主窗口，让设置窗口及其子窗体形成正确的模态层级。</summary>
    public SettingsEditorService(Func<Window?> getOwner, ITagManagementService tags, ILanguagePreferenceStore languages,
        IFileOpenRuleStore? fileRules = null, ITemporaryFileStorage? temporaryFiles = null,
        IAccountService? accounts = null, IAccountEditor? accountEditor = null)
    {
        ArgumentNullException.ThrowIfNull(getOwner);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(languages);
        _getOwner = getOwner;
        _tags = tags;
        _languages = languages;
        _fileRules = fileRules;
        _temporaryFiles = temporaryFiles;
        _accounts = accounts;
        _accountEditor = accountEditor;
    }

    /// <inheritdoc />
    public Task OpenAsync() => OpenCategoryAsync("general");
    /// <inheritdoc />
    public Task OpenTagsAsync() => OpenCategoryAsync("tags");
    private async Task OpenCategoryAsync(string category)
    {
        SettingsWindow? window = null;
        var dialogs = new WindowsTagDialogService(() => window ?? _getOwner(), _tags);
        var storageDialogs = new WindowsStorageSettingsDialogs(() => window ?? _getOwner());
        using var model = new SettingsViewModel(_tags, dialogs, _languages, _fileRules, _temporaryFiles, storageDialogs,
            _accounts, _accountEditor is null ? null : () => _accountEditor.OpenAsync(window));
        await model.InitializeAsync();
        model.SelectCategoryCommand.Execute(model.Categories.First(item => item.Id == category));
        window = new SettingsWindow(model) { Owner = _getOwner() };
        window.ShowDialog();
    }
}
