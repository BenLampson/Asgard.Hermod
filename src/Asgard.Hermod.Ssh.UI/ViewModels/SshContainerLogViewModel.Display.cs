namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>快照元数据和当前操作分开展示，刷新失败不会改写旧证据的范围与时间。</summary>
public sealed partial class SshContainerLogViewModel
{
    private bool _readFailed, _localFailed;
    /// <summary>打开时捕获的实例短名称，不随其他工作区选择变化。</summary>
    public string SourceName { get; }
    /// <summary>标题使用短来源，完整地址和 ID 按需查看。</summary>
    public string SourceLabel => Text.Format("SshLogSourceCaption", SourceName);
    /// <summary>本日志文档独立保存显示字号，不改写原文和附件。</summary>
    public int LogFontSize
    {
        get;
        set
        {
            if (value is < 10 or > 24 || !SetProperty(ref field, value)) return;
            OnPropertyChanged(nameof(EncodingAndFont));
        }
    } = 13;
    /// <summary>导出使用 UTF-8，字号以实际逻辑像素显示。</summary>
    public string EncodingAndFont => Text.Format("SshLogEncodingFont", LogFontSize);
    /// <summary>底部只描述已保留快照，不把失败的新范围当作成功读取结果。</summary>
    public string SnapshotStatus => Snapshot is not { } snapshot ? Text.Get("SshLogNoSnapshot")
        : Text.Format(snapshot.IsTruncated ? "SshLogSnapshotTruncated" : snapshot.Text.Length == 0 ? "SshLogSnapshotEmpty" : "SshLogSnapshotComplete",
            snapshot.CapturedAt.ToLocalTime(), snapshot.RequestedLines);
    /// <summary>完整采集时间在来源详情显示，使用用户区域格式。</summary>
    public string SnapshotTime => Snapshot is { } snapshot ? Text.Format("SshLogSnapshotTime", snapshot.CapturedAt.ToLocalTime()) : Text.Get("SshLogNoSnapshot");
    /// <summary>当前没有正文时显示轻量状态，绝不展示示例日志。</summary>
    public bool IsBodyEmpty => LogText.Length == 0;
    /// <summary>加载、无快照与空快照分开说明。</summary>
    public string BodyHeading => Text.Get(IsLoading ? "SshMonitorLogLoading" : Snapshot is null ? "SshLogNoSnapshot" : "SshMonitorLogEmpty");
    /// <summary>首次失败只显示安全错误；已有快照的错误留在标题栏。</summary>
    public string BodyDetail => IsLoading ? Text.Get("SshLogLoadingDetail") : _readFailed && Snapshot is null ? Status
        : !IsConnected ? Text.Get("SshLogDisconnected") : Text.Get(Snapshot is null ? "SshLogNoSnapshotDetail" : "SshLogEmptyDetail");
    /// <summary>异常仅占用标题内紧凑槽位。</summary>
    public bool HasAlert => !IsConnected || _readFailed || _localFailed;
    /// <summary>错误全文通过安全提示保留，短标签不挤占正文。</summary>
    public string AlertText => !IsConnected ? Text.Get("SshLogDisconnected") : _localFailed ? Text.Get("SshLogLocalFailed")
        : Text.Get(Snapshot is null ? "SshLogReadFailedNoSnapshot" : "SshLogRefreshFailedRetained");
    /// <summary>只有远端读取失败且仍在线时允许重试。</summary>
    public bool CanRetry => _readFailed && IsConnected && !IsLoading;
    /// <summary>刷新在进行中原位变为停止，不再显示两个独立按钮。</summary>
    public ICommand ReadActionCommand => IsLoading ? CancelCommand : RefreshCommand;
    /// <summary>当前动作名称从资源读取，不用标签决定行为。</summary>
    public string ReadActionLabel => Text.Get(IsLoading ? "SshAgentStop" : "SshFilesRefresh");
    /// <summary>动作图标使用稳定名称。</summary>
    public string ReadActionIcon => IsLoading ? "square" : "rotate-cw";
    private void RefreshDisplay()
    {
        OnPropertyChanged(nameof(SnapshotStatus)); OnPropertyChanged(nameof(SnapshotTime)); OnPropertyChanged(nameof(IsBodyEmpty));
        OnPropertyChanged(nameof(BodyHeading)); OnPropertyChanged(nameof(BodyDetail)); OnPropertyChanged(nameof(HasAlert));
        OnPropertyChanged(nameof(AlertText)); OnPropertyChanged(nameof(CanRetry)); OnPropertyChanged(nameof(ReadActionCommand));
        OnPropertyChanged(nameof(ReadActionLabel)); OnPropertyChanged(nameof(ReadActionIcon));
    }
}
