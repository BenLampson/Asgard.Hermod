using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Services;

/// <summary>宿主协调系统设置和子编辑窗体的所有者，界面只依赖标签服务。</summary>
public sealed class SettingsEditorService : ISettingsEditor
{
    private readonly Func<Window?> _getOwner;
    private readonly ITagManagementService _tags;
    private readonly ILanguagePreferenceStore _languages;

    /// <summary>延迟获取应用主窗口，让设置窗口及其子窗体形成正确的模态层级。</summary>
    public SettingsEditorService(Func<Window?> getOwner, ITagManagementService tags, ILanguagePreferenceStore languages)
    {
        ArgumentNullException.ThrowIfNull(getOwner);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(languages);
        _getOwner = getOwner;
        _tags = tags;
        _languages = languages;
    }

    /// <inheritdoc />
    public async Task OpenAsync()
    {
        SettingsWindow? window = null;
        var dialogs = new WindowsTagDialogService(() => window ?? _getOwner(), _tags);
        using var model = new SettingsViewModel(_tags, dialogs, _languages);
        await model.InitializeAsync();
        window = new SettingsWindow(model) { Owner = _getOwner() };
        window.ShowDialog();
    }
}
