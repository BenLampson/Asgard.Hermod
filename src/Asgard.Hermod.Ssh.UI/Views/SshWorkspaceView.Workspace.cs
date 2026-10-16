namespace Asgard.Hermod.Ssh.UI.Views;

/// <summary>页面入口只切换展示与显式附加选区，模型回复不控制窗口显隐或焦点。</summary>
public partial class SshWorkspaceView
{
    private void UpdateAssistantSubscription()
    {
        if (_subscribedAssistant is not null) _subscribedAssistant.InsertRequested -= OnAgentInsert;
        _subscribedAssistant = _model?.Assistant;
        if (_subscribedAssistant is not null) _subscribedAssistant.InsertRequested += OnAgentInsert;
    }
    private void OnCloseAgent(object? sender, EventArgs args)
    {
        if (_model is not null) _model.IsAgentOpen = false;
        if (_model?.IsTerminalSection == true) TerminalView.Focus();
    }

    private void OnCloseMonitor(object? sender, EventArgs args)
    {
        if (_model is not null) _model.IsMonitorOpen = false;
    }

    private void OnAttachAgentSelection(object? sender, EventArgs args)
    {
        if (_model is null) return;
        _model.Assistant.SelectedOutput = TerminalView.GetSelectedText();
        _model.Assistant.IncludeOutput = _model.Assistant.SelectedOutput.Length > 0;
    }

    private void OnAgentInsert(object? sender, string command)
    {
        if (_model is null) return;
        TerminalView.PasteText(command); TerminalView.Focus();
    }

    private void OnSectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_model?.IsFilesSection == true && _model.Files.Entries.Count == 0) _model.Files.RefreshCommand.Execute(null);
    }
}
