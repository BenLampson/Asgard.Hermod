using Asgard.Hermod.UI.Windowing;

namespace Asgard.Hermod;

public partial class MainWindow
{
    private IMainWindowClosePreferenceStore? _closePreferences;
    private MainWindowCloseDialog? _closePrompt;
    private bool _closeChoicePending;
    private void InitializeClosePreferences(IMainWindowClosePreferenceStore? store)
    {
        _closePreferences = store;
        if (store is null) return;
        store.Changed += OnClosePreferencesChanged;
        WindowCloseBehavior.SetIsMainWindow(this, true);
        UpdateClosePresentation();
    }
    private void OnClosePreferencesChanged(object? sender, EventArgs args)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(UpdateClosePresentation); return; }
        UpdateClosePresentation();
    }
    private void UpdateClosePresentation()
    {
        if (_closePreferences is null) return;
        try
        {
            var value = _closePreferences.Load();
            string key = value.AskBeforeClosing || value.Action == MainWindowCloseAction.HideToTray && _tray?.IsAvailable != true
                ? "CloseMainWindow" : value.Action == MainWindowCloseAction.HideToTray ? "HideMainWindowToTray" : "ExitHermod";
            WindowCloseBehavior.SetCloseActionLabel(this, Text.Get(key));
        }
        catch (Exception) { WindowCloseBehavior.SetCloseActionLabel(this, Text.Get("CloseMainWindow")); }
    }
    /// <summary>先结束 Closing 事件再显示模态窗口，保留重复关闭保护，避免在原关闭栈上重入。</summary>
    private void ScheduleMainClose()
    {
        if (_closing || IsExitCommitted || _closeChoicePending || _sessionEnding) return;
        if (WindowActivation.FindModal() is { } modal) { WindowActivation.Show(modal); return; }
        if (_workWindows?.Windows.FirstOrDefault(window => window.IsClosePending) is { } pending) { WindowActivation.Show(pending); return; }
        _closeChoicePending = true;
        Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                if (_sessionEnding || IsExitCommitted) return;
                var preference = _closePreferences!.Load();
                var action = preference.Action;
                if (preference.AskBeforeClosing || action == MainWindowCloseAction.HideToTray && _tray?.IsAvailable != true)
                {
                    _closePrompt = new(_tray?.IsAvailable == true) { Owner = this };
                    bool confirmed;
                    try { confirmed = _closePrompt.ShowDialog() == true && _closePrompt.SelectedAction is not null; }
                    finally { if (_closePrompt.IsVisible) _closePrompt.Close(); }
                    if (!confirmed || _sessionEnding || IsExitCommitted) return;
                    action = _closePrompt.SelectedAction!.Value;
                    if (_closePrompt.DoNotAskAgain) _closePreferences.Save(new(false, action));
                    _closePrompt = null;
                }
                if (action == MainWindowCloseAction.HideToTray) HideToTray();
                else await RequestExitAsync();
            }
            catch (Exception error) { if (!_sessionEnding) OnCommandFailed(this, error); }
            finally { _closePrompt = null; _closeChoicePending = false; }
        });
    }
}
