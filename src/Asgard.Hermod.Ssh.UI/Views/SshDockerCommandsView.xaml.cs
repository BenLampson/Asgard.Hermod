using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>先选择容器再执行语义化动作，原始命令作为可选详情。</summary>
public partial class SshDockerCommandsView : UserControl
{
    private GridLength _mainHeight = new(2, GridUnitType.Star);
    private GridLength _outputHeight = new(3, GridUnitType.Star);

    /// <summary>加载真实资源，支持离屏布局与键盘导航测试。</summary>
    public SshDockerCommandsView() => InitializeComponent();
    /// <summary>关闭只收起页面。</summary>
    public event EventHandler? CloseRequested;
    private void OnClose(object sender, RoutedEventArgs args) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (args.OldValue is SshDockerCommandsViewModel oldModel) PropertyChangedEventManager.RemoveHandler(oldModel, OnOutputOpenChanged, nameof(oldModel.IsOutputOpen));
        if (args.NewValue is SshDockerCommandsViewModel newModel) PropertyChangedEventManager.AddHandler(newModel, OnOutputOpenChanged, nameof(newModel.IsOutputOpen));
        UpdateOutputRows(args.NewValue is SshDockerCommandsViewModel { IsOutputOpen: true });
    }

    private void OnOutputOpenChanged(object? sender, PropertyChangedEventArgs args) => UpdateOutputRows(sender is SshDockerCommandsViewModel { IsOutputOpen: true });

    private void UpdateOutputRows(bool open)
    {
        if (CommandColumns is null || CommandColumns.RowDefinitions.Count < 3) return;
        var main = CommandColumns.RowDefinitions[0]; var splitter = CommandColumns.RowDefinitions[1]; var output = CommandColumns.RowDefinitions[2];
        if (open)
        {
            main.Height = _mainHeight; splitter.Height = new(8); output.MinHeight = 360; output.Height = _outputHeight;
        }
        else
        {
            // GridSplitter 会设置本地行高；先保存它，再清空行高才能真正收起结果区。
            if (output.Height.Value > 0) { _mainHeight = main.Height; _outputHeight = output.Height; }
            main.Height = new(1, GridUnitType.Star); splitter.Height = new(0); output.MinHeight = 0; output.Height = new(0);
        }
    }
    /// <summary>宿主提供独立采样模型，图表与当前容器共用真实资源。</summary>
    public SshMonitorViewModel? ResourceMonitor
    {
        get => (SshMonitorViewModel?)GetValue(ResourceMonitorProperty);
        set => SetValue(ResourceMonitorProperty, value);
    }
    /// <summary>允许宿主绑定当前连接的资源监测。</summary>
    public static readonly DependencyProperty ResourceMonitorProperty = DependencyProperty.Register(nameof(ResourceMonitor), typeof(SshMonitorViewModel), typeof(SshDockerCommandsView));
    private void OnOutputSearchChanged(object sender, TextChangedEventArgs args) => FindOutput(false);
    private void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && DataContext is SshDockerCommandsViewModel model)
        {
            var search = model.IsImageTool ? ImageSearch : model.IsContainerTool ? ContainerSearch : model.IsOutputOpen ? OutputSearch : null;
            if (search is not null) { search.Focus(); search.SelectAll(); args.Handled = true; }
        }
        else if (args.Key == Key.F3 || args.Key == Key.Enter && ReferenceEquals(args.OriginalSource, OutputSearch)) { FindOutput(true); args.Handled = true; }
    }
    private void FindOutput(bool advance)
    {
        if (ResultOutput is null || OutputSearch is null || OutputSearch.Text.Length == 0) return;
        int start = advance ? Math.Min(ResultOutput.Text.Length, ResultOutput.SelectionStart + ResultOutput.SelectionLength) : 0;
        int at = ResultOutput.Text.IndexOf(OutputSearch.Text, start, StringComparison.OrdinalIgnoreCase);
        if (at < 0 && start > 0) at = ResultOutput.Text.IndexOf(OutputSearch.Text, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return;
        ResultOutput.Select(at, OutputSearch.Text.Length); ResultOutput.ScrollToLine(ResultOutput.GetLineIndexFromCharacterIndex(at));
    }
    private void OnCopyCommand(object sender, RoutedEventArgs args) => Copy(CommandPreview.Text);
    private void OnCopyOutput(object sender, RoutedEventArgs args) => Copy(ResultOutput.SelectionLength > 0 ? ResultOutput.SelectedText : ResultOutput.Text);
    private void Copy(string value)
    {
        if (value.Length == 0) return;
        try { Clipboard.SetText(value); }
        catch (ExternalException) { ReportLocalFailure(); }
    }
    private void OnExportOutput(object sender, RoutedEventArgs args)
    {
        if (ResultOutput.Text.Length == 0) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "docker-output.txt", OverwritePrompt = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { File.WriteAllText(dialog.FileName, ResultOutput.Text, new System.Text.UTF8Encoding(false)); }
        catch (Exception) { ReportLocalFailure(); }
    }
    private void ReportLocalFailure() => MessageBox.Show(Window.GetWindow(this), Text.Get("DockerLocalActionFailed"), Text.Get("DockerLibrary"), MessageBoxButton.OK, MessageBoxImage.Warning);
}
