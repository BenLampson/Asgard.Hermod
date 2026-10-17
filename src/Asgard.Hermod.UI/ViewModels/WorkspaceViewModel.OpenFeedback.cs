namespace Asgard.Hermod.UI.ViewModels;

/// <summary>首次连接反馈覆盖主窗体；成功激活、失败回收和重试仍走同一个工作区生命周期。</summary>
public sealed partial class WorkspaceViewModel
{
    private readonly Dictionary<WorkspacePageViewModel, MainConnectionFeedbackViewModel> _openingFeedback = [];
    private MainConnectionFeedbackViewModel? _lastOpenFailure;
    /// <summary>并发连接时只展示一个真实请求，结束后转到下一个请求。</summary>
    public MainConnectionFeedbackViewModel? OpenFeedback { get; private set => SetProperty(ref field, value); }

    private void BeginOpenFeedback(WorkspacePageViewModel page)
    {
        if (page.HasConnectedOnce || _openingFeedback.ContainsKey(page) || !_instances.TryGetValue(page, out var instance)) return;
        MainConnectionFeedbackViewModel feedback = null!;
        feedback = new(instance, () =>
        {
            if (_disposeStarted || IsExitPending || !_pendingActivations.Contains(page)) return;
            if (page.ConnectionState?.CancelCommand is { } cancel && cancel.CanExecute(null)) cancel.Execute(null);
        }, () =>
        {
            if (_disposeStarted || IsExitPending || !ReferenceEquals(_lastOpenFailure, feedback)) return;
            Dismiss(); OpenInstance(instance);
        }, Dismiss);
        _openingFeedback.Add(page, feedback); RefreshOpenFeedback();
        void Dismiss()
        {
            if (!ReferenceEquals(_lastOpenFailure, feedback)) return;
            _lastOpenFailure = null; feedback.Invalidate(); OpenError = ""; OnPropertyChanged(nameof(OpenError)); RefreshOpenFeedback();
        }
    }
    private void FailOpenFeedback(WorkspacePageViewModel page, string safeError)
    {
        if (!_openingFeedback.TryGetValue(page, out var feedback)) return;
        feedback.Fail(safeError); _lastOpenFailure?.Invalidate(); _lastOpenFailure = feedback;
        RefreshOpenFeedback();
    }
    private void EndOpenFeedback(WorkspacePageViewModel page)
    {
        if (_openingFeedback.Remove(page, out var feedback) && !ReferenceEquals(feedback, _lastOpenFailure)) feedback.Invalidate();
        RefreshOpenFeedback();
    }
    private void RefreshOpenFeedback() => OpenFeedback = _disposeStarted || IsExitPending ? null : _openingFeedback.Values.FirstOrDefault(feedback => feedback.IsConnecting) ?? _lastOpenFailure;
    private void ClearOpenFeedback()
    {
        foreach (var feedback in _openingFeedback.Values) feedback.Invalidate();
        _lastOpenFailure?.Invalidate(); _lastOpenFailure = null; _openingFeedback.Clear(); OpenFeedback = null;
    }
}
