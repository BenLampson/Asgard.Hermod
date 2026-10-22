using System.IO;
using System.Runtime.InteropServices;
using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>先选择容器再执行语义化动作，原始命令作为可选详情。</summary>
public partial class SshDockerCommandsView : UserControl
{
    /// <summary>加载真实资源，支持离屏布局与键盘导航测试。</summary>
    public SshDockerCommandsView() => InitializeComponent();
    /// <summary>关闭只收起页面。</summary>
    public event EventHandler? CloseRequested;
    /// <summary>返回 Docker 监测。</summary>
    public event EventHandler? MonitorRequested;
    private void OnClose(object sender, RoutedEventArgs args) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void OnMonitor(object sender, RoutedEventArgs args) => MonitorRequested?.Invoke(this, EventArgs.Empty);
    private void OnViewportChanged(object sender, SizeChangedEventArgs args)
    {
        if (!ReferenceEquals(args.Source, this) || !args.WidthChanged || CommandColumns is null) return;
        double width = ActualWidth < 800 ? 180 : 250;
        if (CatalogColumn.Width.Value != width) CatalogColumn.Width = new(width);
    }
    private void OnOutputSearchChanged(object sender, TextChangedEventArgs args) => FindOutput(false);
    private void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { OutputSearch.Focus(); OutputSearch.SelectAll(); args.Handled = true; }
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
