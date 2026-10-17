namespace Asgard.Hermod.UI.ViewModels;

/// <summary>通用设置立即持久化关闭偏好，失败时保留原选择并显示安全提示。</summary>
public sealed class MainWindowCloseSettingsViewModel : ObservableObject
{
    private readonly IMainWindowClosePreferenceStore _store;
    private MainWindowClosePreference _preference = new();
    /// <summary>从本机设置读取，不依据翻译后的标签识别动作。</summary>
    public MainWindowCloseSettingsViewModel(IMainWindowClosePreferenceStore store)
    {
        _store = store;
        try { _preference = store.Load(); } catch (Exception error) { Error = Text.Error(error); }
        Actions = [new(MainWindowCloseAction.HideToTray, Text.Get("HideMainWindowToTray")), new(MainWindowCloseAction.ExitApplication, Text.Get("ExitHermod"))];
    }
    /// <summary>真实询问开关，重启后保留。</summary>
    public bool AskBeforeClosing { get => _preference.AskBeforeClosing; set => Save(_preference with { AskBeforeClosing = value }); }
    /// <summary>默认动作可独立选择。</summary>
    public IReadOnlyList<MainWindowCloseActionOption> Actions { get; }
    /// <summary>下拉框保留稳定动作与本地化标签的映射。</summary>
    public MainWindowCloseActionOption SelectedAction { get => Actions.Single(item => item.Action == _preference.Action); set { if (value is not null && Actions.Contains(value)) Save(_preference with { Action = value.Action }); } }
    /// <summary>持久化错误不显示数据库原始信息。</summary>
    public string Error { get; private set; } = "";
    private void Save(MainWindowClosePreference value)
    {
        if (value == _preference) return;
        try { _store.Save(value); _preference = value; Error = ""; }
        catch (Exception error) { Error = Text.Error(error); }
        OnPropertyChanged(nameof(AskBeforeClosing)); OnPropertyChanged(nameof(SelectedAction)); OnPropertyChanged(nameof(Error));
    }
}
