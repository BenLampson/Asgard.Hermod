using System.ComponentModel;
using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>只在用户确认后保存禁止范围；取消不会影响已有规则。</summary>
public partial class McpExclusionWindow : HermodDialog
{
    private readonly McpSettingsViewModel _model;
    private bool _saved;
    /// <summary>使用设置页的实例列表和未保存草稿。</summary>
    public McpExclusionWindow(McpSettingsViewModel model)
    {
        _model = model;
        InitializeComponent(); DataContext = model;
        _model.ExclusionSaved += OnSaved;
        Closed += OnClosed;
    }
    private void OnSaved(object? sender, EventArgs args) { _saved = true; DialogResult = true; }
    protected override void OnClosing(CancelEventArgs args)
    {
        if (!_saved && _model.IsBusy) args.Cancel = true;
        base.OnClosing(args);
    }
    private void OnClosed(object? sender, EventArgs args) { _model.ExclusionSaved -= OnSaved; Closed -= OnClosed; }
    private void OnBrowseStorage(object sender, RoutedEventArgs args)
    {
        using var browser = _model.CreateStorageBrowser();
        if (browser is null) return;
        var dialog = new McpStorageBrowserWindow(browser) { Owner = this };
        if (dialog.ShowDialog() == true) _model.ApplyStorageScope(browser);
    }
}
