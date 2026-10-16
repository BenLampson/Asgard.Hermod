using Asgard.Hermod.UI.Controls;

namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>系统设置宿主，使用通用窗体标题和独立标签管理面板。</summary>
public partial class SettingsWindow : HermodWindow
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
        if (model.FileOpening is { } files) files.EditRequested += OnEditFileRule;
        if (model.AgentModels is { } models) models.EditRequested += OnEditModel;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>宿主未预加载时补足读取；预加载失败保留错误，等待用户主动重试。</summary>
    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (!_model.Tags.IsInitialized && _model.Tags.Error.Length == 0 || _model.Storage is { IsInitialized: false, Message: "" })
            await _model.InitializeAsync();
    }

    /// <summary>转换通用关闭命令，不把关闭视为写入确认。</summary>
    private void OnCloseRequested(object? sender, bool accepted) => Close();

    /// <summary>编辑器只在保存成功后刷新列表，取消不修改规则。</summary>
    private void OnEditFileRule(object? sender, FileOpenRule? rule)
    {
        if (_model.FileOpening is not { } files) return;
        var editor = files.CreateEditor(rule);
        var window = new FileOpenRuleWindow(editor) { Owner = this };
        if (window.ShowDialog() == true) files.Reload();
    }
    private void OnEditModel(object? sender, AgentModelEditRequest request)
    {
        if (_model.AgentModels is not { } models) return;
        using var editor = models.CreateEditor(request.Profile, request.Epoch);
        new AgentModelEditorWindow(editor) { Owner = this }.ShowDialog();
    }

    /// <summary>解除窗口与状态订阅，结束后台刷新。</summary>
    private void OnClosed(object? sender, EventArgs args)
    {
        _model.CloseRequested -= OnCloseRequested;
        if (_model.FileOpening is { } files) files.EditRequested -= OnEditFileRule;
        if (_model.AgentModels is { } models) models.EditRequested -= OnEditModel;
        Loaded -= OnLoaded;
        Closed -= OnClosed;
        _model.Dispose();
    }
}
