using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>系统设置宿主，使用通用窗体标题和独立标签管理面板。</summary>
public partial class SettingsWindow : HermodDialog
{
    private readonly SettingsViewModel _model;

    /// <summary>只绑定注入的状态，不在窗口内部创建持久化来源。</summary>
    public SettingsWindow(SettingsViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        InitializeComponent();
        DataContext = model;
        model.CloseRequested += OnCloseRequested;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>宿主未预加载时补足读取；预加载失败保留错误，等待用户主动重试。</summary>
    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (!_model.Tags.IsInitialized && _model.Tags.Error.Length == 0)
            await _model.InitializeAsync();
    }

    /// <summary>转换通用关闭命令，不把关闭视为写入确认。</summary>
    private void OnCloseRequested(object? sender, bool accepted) => Close();

    /// <summary>解除窗口与状态订阅，结束后台刷新。</summary>
    private void OnClosed(object? sender, EventArgs args)
    {
        _model.CloseRequested -= OnCloseRequested;
        Loaded -= OnLoaded;
        Closed -= OnClosed;
        _model.Dispose();
    }
}
