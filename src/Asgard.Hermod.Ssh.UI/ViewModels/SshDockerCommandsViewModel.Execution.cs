using System.Diagnostics;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>环境读取、确认、执行和取消的串行边界；迟到结果不能写回新连接。</summary>
public sealed partial class SshDockerCommandsViewModel
{
    private async Task DetectAsync(bool force)
    {
        if (IsBusy || _disposed || _connection() is not { } connection) return;
        long generation = _generation;
        using var pending = new CancellationTokenSource(TimeSpan.FromSeconds(20)); _pending = pending;
        IsBusy = true; Error = ""; Status = Text.Get("DockerDetecting");
        try
        {
            var environment = await _environmentReader.ReadAsync(connection, force, pending.Token);
            EnsureCurrent(connection, generation, pending.Token);
            Environment = environment; OnPropertyChanged(nameof(Environment)); RefreshPreview();
            if (!environment.Installed) throw Text.Exception("SshMonitorDockerMissing");
            if (!environment.CanAccess) throw Text.Exception("DockerDaemonUnavailable");
            var result = await connection.ExecuteCommandAsync(SshMonitorDetailReader.ContainerCommand, pending.Token).WaitAsync(pending.Token);
            EnsureCurrent(connection, generation, pending.Token);
            if (result.ExitCode != 0 || result.Truncated) throw Text.Exception("DockerContainerReadFailed");
            var items = SshMonitorDetailReader.ParseContainers(result.Output).Select(c => new SshMonitorContainerItem(c)).ToArray();
            UpdateContainers(items);
            Status = Text.Get("DockerEnvironmentReady");
        }
        catch (OperationCanceledException) { if (generation == _generation) Error = Text.Get("DockerDetectionCanceled"); }
        catch (Exception error) { if (generation == _generation) Error = Text.Error(error, "DockerEnvironmentFailed"); }
        finally { _pending = null; IsBusy = false; RefreshPreview(); }
    }

    private async Task ExecuteAsync()
    {
        if (IsBusy || _disposed || _plan is not { } plan || _connection() is not { } connection) return;
        long generation = _generation; string host = _host(); string target = SelectedContainer?.Name ?? ProjectDirectory;
        using var pending = new CancellationTokenSource(); _pending = pending; IsBusy = true; Error = ""; bool dispatched = false, completed = false;
        try
        {
            if (plan.Operation.ChangesState)
            {
                Status = Text.Get("DockerAwaitingConfirmation");
                string detail = plan.Operation.Id switch
                {
                    "restart" => Text.Format("DockerRestartImpact", target, host),
                    "stop" => Text.Format("DockerStopImpact", target, host),
                    "start" => Text.Format("DockerStartImpact", target, host),
                    _ => Text.Format("DockerChangeImpact", Text.Get(plan.Operation.TitleKey), host, target),
                };
                bool approved = await _dialogs.ConfirmDockerActionAsync(Text.Get(plan.Operation.TitleKey), detail, plan.Command, pending.Token).WaitAsync(pending.Token);
                EnsureCurrent(connection, generation, pending.Token);
                if (!approved) { Status = Text.Get("DockerDeclined"); return; }
            }
            EnsureCurrent(connection, generation, pending.Token);
            Status = Text.Get("DockerExecuting"); ExecutedCommand = plan.Command; Output = ""; ResultSummary = "";
            ResultTarget = Text.Format("DockerActionTarget", Text.Get(plan.Operation.TitleKey), target, host);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(pending.Token); timeout.CancelAfter(TimeSpan.FromMinutes(2));
            var started = Stopwatch.GetTimestamp(); dispatched = true;
            if (plan.RunsInTerminal)
            {
                await _terminal(plan, timeout.Token).WaitAsync(timeout.Token);
                EnsureCurrent(connection, generation, pending.Token); Status = Text.Get("DockerStartedInTerminal"); return;
            }
            var result = await connection.ExecuteCommandAsync(plan.Command, timeout.Token).WaitAsync(timeout.Token);
            EnsureCurrent(connection, generation, pending.Token);
            double seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
            if (result.Truncated && result.ExitCode == -1) { Error = Text.Get("DockerOutcomeUnknown"); return; }
            if (result.ExitCode != 0) { Error = Text.Get("DockerExecutionFailed"); ResultSummary = Text.Format("DockerFailedExit", result.ExitCode); return; }
            completed = true;
            Output = plan.Operation.ChangesState ? Text.Format("DockerActionSucceeded", Text.Get(plan.Operation.TitleKey), target.Length > 0 ? target : host) : result.Output;
            ResultSummary = Text.Format("DockerHumanResultSummary", host, seconds);
            Status = Text.Get(result.Truncated ? "SshOutputTruncated" : "DockerCompleted");
            if (plan.Operation.ChangesState)
            {
                Changed?.Invoke(this, EventArgs.Empty);
                // 重新读取当前列表而不自动重试刚才的变更；被移除的容器不能继续作为目标。
                var refreshed = await connection.ExecuteCommandAsync(SshMonitorDetailReader.ContainerCommand, timeout.Token).WaitAsync(timeout.Token);
                EnsureCurrent(connection, generation, pending.Token);
                if (refreshed.ExitCode == 0 && !refreshed.Truncated)
                {
                    var items = SshMonitorDetailReader.ParseContainers(refreshed.Output).Select(c => new SshMonitorContainerItem(c)).ToArray();
                    UpdateContainers(items);
                }
                else Error = Text.Get("DockerRefreshAfterActionFailed");
            }
        }
        catch (OperationCanceledException) { if (generation == _generation) Error = Text.Get(completed ? "DockerRefreshAfterActionFailed" : dispatched ? "DockerOutcomeUnknown" : "DockerCanceled"); }
        catch (Exception error) { if (generation == _generation) Error = completed ? Text.Get("DockerRefreshAfterActionFailed") : dispatched ? Text.Get("DockerOutcomeUnknown") : Text.Error(error, "DockerExecutionFailed"); }
        finally { _pending = null; IsBusy = false; RefreshPreview(); }
    }

    private void EnsureCurrent(ISshWorkspaceConnection connection, long generation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_disposed || generation != _generation || !ReferenceEquals(connection, _connection())) throw new OperationCanceledException(token);
    }

    private void UpdateContainers(IReadOnlyList<SshMonitorContainerItem> items)
    {
        // 按稳定 ID 增量更新，刷新或启停不能重建整张列表并把滚动位置跳回顶部。
        string? selected = SelectedContainer?.Id;
        var ids = items.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        for (int i = Containers.Count - 1; i >= 0; i--) if (!ids.Contains(Containers[i].Id)) Containers.RemoveAt(i);
        foreach (var item in items)
        {
            int index = -1;
            for (int i = 0; i < Containers.Count; i++) if (Containers[i].Id == item.Id) { index = i; break; }
            if (index < 0) Containers.Add(item);
            else if (Containers[index] != item) Containers[index] = item;
        }
        var target = Containers.FirstOrDefault(c => c.Id == selected);
        SelectedContainer = target is not null && ContainerView.Contains(target) ? target : null;
    }
}
