namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>窗口只收集选择，服务再次核对云端和本机版本后提交。</summary>
public partial class ConfigurationMergeWindow : Controls.HermodDialog
{
    /// <summary>关闭绑定仅存在于窗口生命周期。</summary>
    public ConfigurationMergeWindow(ConfigurationMergeViewModel model)
    {
        InitializeComponent(); DataContext = model;
        model.CloseRequested += OnCloseRequested;
        Closed += (_, _) => model.CloseRequested -= OnCloseRequested;
    }
    /// <summary>模态确认结果。</summary>
    private void OnCloseRequested(object? sender, bool confirmed) => DialogResult = confirmed;
}
