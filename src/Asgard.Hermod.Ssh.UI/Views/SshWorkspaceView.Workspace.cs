using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>页面入口只切换展示与显式附加选区，模型回复不控制窗口显隐或焦点。</summary>
public partial class SshWorkspaceView
{
    /// <summary>显式插入不附加回车，用户可在终端继续编辑。</summary>
    private void OnDockerInsert(object? sender, string command)
    {
        if (_disposed || _model?.IsReadOnly != false) return;
        _model.IsDockerCommandsOpen = false; TerminalView.PasteText(command); TerminalView.Focus();
    }

    private void OnCloseMonitor(object? sender, EventArgs args)
    {
        if (_model is not null) _model.IsMonitorOpen = false;
    }

    private void OnAgentInsert(object? sender, string command)
    {
        if (_disposed || _model is null || sender is not SshAssistantViewModel assistant || assistant.IsSourceClosed) return;
        var console = _model.Consoles.FirstOrDefault(item => ReferenceEquals(item.Assistant, assistant));
        if (console is null) return;
        _model.ActiveConsole = console;
        if (Window.GetWindow(this) is { } source)
        {
            if (source.WindowState == WindowState.Minimized) source.WindowState = WindowState.Normal;
            source.Show(); source.Activate();
        }
        TerminalView.PasteText(command); TerminalView.Focus();
    }

    private void OnSectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_model?.IsFilesSection == true && _model.Files.Entries.Count == 0) _model.Files.RefreshCommand.Execute(null);
    }

    private readonly HashSet<SshAssistantViewModel> _openedAssistants = [];
    /// <summary>每个控制台固定一个助手窗口，来源关闭后仍可阅读。</summary>
    private void OnOpenAgent(object sender, RoutedEventArgs args)
    {
        if (_model is not { ActiveConsole: { } console } model || TryFindResource(typeof(IAgentWindowService)) is not IAgentWindowService windows) return;
        var assistant = console.Assistant; var origin = Window.GetWindow(this);
        if (_openedAssistants.Add(assistant)) assistant.InsertRequested += OnAgentInsert;
        var view = new SshAssistantView();
        view.AttachSelectionRequested += (_, _) =>
        {
            if (assistant.IsSourceClosed || !model.Consoles.Contains(console)) return;
            model.ActiveConsole = console;
            if (!ReferenceEquals(TerminalView.Session, console.Session)) return;
            assistant.SelectedOutput = TerminalView.GetSelectedText(); assistant.IncludeOutput = assistant.SelectedOutput.Length > 0;
        };
        windows.Open(new(console, assistant, Text.Format("ChatSshSource", model.Name, console.Name), model.RemoteIdentity, "terminal", () => view, () =>
        {
            if (assistant.IsSourceClosed || !model.Consoles.Contains(console)) return;
            model.ActiveConsole = console;
            if (origin is not null) { if (origin.WindowState == WindowState.Minimized) origin.WindowState = WindowState.Normal; origin.Show(); origin.Activate(); }
        }, () => assistant.IsSourceClosed));
    }
}
