using System.Collections.ObjectModel;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>详情保留列表实例与所选容器，迟到响应不能跨页面或连接覆盖新状态。</summary>
public sealed partial class SshMonitorViewModel
{
    private readonly HashSet<string> _loadedSections = [];
    private DateTimeOffset? _detailsUpdated;

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
                var snapshots = SshMonitorDetailReader.ParseContainers(result.Output);
                var resources = await ReadContainerResourcesAsync(connection, snapshots, generation, revision, token);
                if (generation != _generation || revision != _sectionRevision) return;
                var containers = snapshots.Select(c => new SshMonitorContainerItem(c, resources.GetValueOrDefault(c.Id))).ToArray();
                string? selected = _requestedContainerId ?? SelectedContainer?.Id;
                Reconcile(Containers, containers, c => c.Id);
                SelectedContainer = Containers.FirstOrDefault(c => c.Id == selected);
                _requestedContainerId = null;
                ValidateContainerSelection();
                DockerSnapshotChanged?.Invoke(this, EventArgs.Empty);
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
            _loadedSections.Add(section); if (section != "docker") SectionError = ""; _detailsUpdated = DateTimeOffset.UtcNow;
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

}
