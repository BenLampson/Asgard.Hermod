using System.Collections.ObjectModel;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>详情保留列表实例与所选容器，迟到响应不能跨页面或连接覆盖新状态。</summary>
public sealed partial class SshMonitorViewModel
{
    private readonly HashSet<string> _loadedSections = [];
    private SshMonitorContainerItem? _logContainer;
    private DateTimeOffset? _detailsUpdated;

    /// <summary>日志抽屉只能由用户显式打开。</summary>
    public bool IsLogOpen { get; private set { if (SetProperty(ref field, value)) NotifyContainerPane(); } }
    /// <summary>成功读取的终端日志保持原样，不作为界面文案翻译。</summary>
    public string LogText { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>日志读取错误使用安全资源。</summary>
    public string LogStatus { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>日志标题保留用户定义容器名称。</summary>
    public string LogTitle => Text.Format("SshMonitorLogTitle", _logContainer?.Name ?? "");

    private async Task ReadSectionAsync(ISshWorkspaceConnection connection, long generation, CancellationToken token)
    {
        string section = Section; long revision = _sectionRevision;
        if (section == "network") { _loadedSections.Add(section); return; }
        IsLoading = !_loadedSections.Contains(section); Notify();
        try
        {
            string command = section == "overview" ? SshMonitorDetailReader.MountCommand : SshHostMonitor.DetailCommand(section);
            var result = await connection.ExecuteCommandAsync(command, token).WaitAsync(token);
            if (generation != _generation || revision != _sectionRevision) return;
            if (section == "docker")
            {
                ContainerAvailability = result.Truncated ? SshContainerAvailability.Unavailable : result.ExitCode switch
                {
                    0 => SshContainerAvailability.Available,
                    42 => SshContainerAvailability.NotInstalled,
                    43 => SshContainerAvailability.AccessDenied,
                    _ => SshContainerAvailability.Unavailable,
                };
                if (ContainerAvailability != SshContainerAvailability.Available)
                {
                    SelectedContainer = null;
                    _loadedSections.Add(section);
                    SectionError = Text.Get(ContainerErrorKey);
                    return;
                }
                var containers = SshMonitorDetailReader.ParseContainers(result.Output).Select(c => new SshMonitorContainerItem(c)).ToArray();
                string? selected = SelectedContainer?.Id;
                Reconcile(Containers, containers, c => c.Id);
                SelectedContainer = Containers.FirstOrDefault(c => c.Id == selected);
                ValidateContainerSelection();
                if (_logContainer is not null && !Containers.Any(c => c.Id == _logContainer.Id)) CloseLogs();
            }
            else
            {
                if (result.ExitCode != 0 || result.Truncated) throw Text.Exception("SshMonitorDetailUnavailable");
                if (section == "processes")
                {
                    Reconcile(Processes, SshMonitorDetailReader.ParseProcesses(result.Output, Snapshot?.ProcessorCount ?? 1).Select(p => new SshMonitorProcessItem(p)).ToArray(), p => p.Pid);
                }
                else
                {
                    var selected = SelectedMount?.Value;
                    Reconcile(Mounts, SshMonitorDetailReader.ParseMounts(result.Output).Select(m => new SshMonitorMountItem(m)).ToArray(), m => (m.Device, m.Path));
                    SelectedMount = Mounts.FirstOrDefault(m => m.Device == selected?.Device && m.Path == selected.Path) ?? Mounts.OrderByDescending(m => m.UsedPercent).FirstOrDefault();
                }
            }
            _loadedSections.Add(section); SectionError = ""; _detailsUpdated = DateTimeOffset.UtcNow;
        }
        catch (OperationCanceledException) when (generation != _generation || revision != _sectionRevision) { }
        catch (Exception error) { if (generation == _generation && revision == _sectionRevision) SectionError = Text.Error(error, "SshMonitorDetailUnavailable"); }
        finally { if (generation == _generation) { IsLoading = false; Notify(); } }
    }

    private void UpdateNetworks(IReadOnlyList<SshNetworkSnapshot> samples)
    {
        Reconcile(Networks, samples.Select(n => new SshMonitorNetworkItem(n)).ToArray(), n => n.Name);
        var choices = new[] { new SshMonitorNetworkChoice("", Text.Get("SshMonitorAllInterfaces")) }
            .Concat(samples.Select(n => new SshMonitorNetworkChoice(n.Name, n.Name))).ToArray();
        Reconcile(NetworkChoices, choices, n => n.Key);
        if (SelectedNetworkKey.Length > 0 && !samples.Any(n => n.Name == SelectedNetworkKey)) SelectedNetworkKey = "";
    }

    /// <summary>按稳定 ID 增量更新，筛选和排序对象不因刷新而重建。</summary>
    private static void Reconcile<T, TKey>(ObservableCollection<T> target, IReadOnlyList<T> values, Func<T, TKey> key) where TKey : notnull
    {
        var keys = values.Select(key).ToHashSet();
        for (int i = target.Count - 1; i >= 0; i--) if (!keys.Contains(key(target[i]))) target.RemoveAt(i);
        foreach (var item in values)
        {
            int index = -1;
            for (int i = 0; i < target.Count; i++) if (EqualityComparer<TKey>.Default.Equals(key(target[i]), key(item))) { index = i; break; }
            if (index < 0) target.Add(item);
            else if (!EqualityComparer<T>.Default.Equals(target[index], item)) target[index] = item;
        }
    }

    private async Task ReadLogsAsync(SshMonitorContainerItem? item)
    {
        if (item is null || _connection is not { } connection || _sampling is null || !IsCurrentContainer(item)) return;
        CloseContainerPane();
        long generation = _generation;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_sampling.Token);
        _logsCancellation = timeout; timeout.CancelAfter(TimeSpan.FromSeconds(10));
        _logContainer = item; SelectedContainer = item; IsLogOpen = true;
        LogText = ""; LogStatus = Text.Get("SshMonitorLogLoading"); OnPropertyChanged(nameof(LogTitle)); NotifyContainerPane();
        bool acquired = false;
        try
        {
            await _gate.WaitAsync(timeout.Token); acquired = true;
            var result = await connection.ExecuteCommandAsync(SshMonitorDetailReader.ContainerLogCommand(item.Id), timeout.Token).WaitAsync(timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            if (generation != _generation || !IsLogOpen) return;
            if (result.ExitCode != 0) throw Text.Exception("SshMonitorLogFailed");
            LogText = result.Output;
            LogStatus = Text.Get(result.Truncated ? "SshMonitorLogTruncated" : result.Output.Length == 0 ? "SshMonitorLogEmpty" : "SshMonitorLogLoaded");
        }
        catch (OperationCanceledException) when (generation != _generation || !IsLogOpen) { }
        catch (Exception error) { if (generation == _generation && IsLogOpen) LogStatus = Text.Error(error, "SshMonitorLogFailed"); }
        finally { if (acquired) _gate.Release(); if (ReferenceEquals(_logsCancellation, timeout)) _logsCancellation = null; }
    }

    private void CloseLogs()
    {
        _logsCancellation?.Cancel(); IsLogOpen = false; _logContainer = null; LogText = ""; LogStatus = "";
        OnPropertyChanged(nameof(LogTitle));
    }
}
