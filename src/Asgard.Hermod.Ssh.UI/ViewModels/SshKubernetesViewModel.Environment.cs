using System.Text.Json;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>显式获取所有筛选所需的数据；重新加载前始终使用同一份环境快照。</summary>
public sealed partial class SshKubernetesViewModel
{
    private Task DetectAsync() => RunAsync(async (connection, generation, token) =>
    {
        string previousContext = Context, previousNamespace = Namespace;
        _initializing = true; IsInitialized = false; _snapshots.Clear();
        try
        {
            IsInstalled = false; Contexts.Clear(); Namespaces.Clear(); Context = ""; Items.Clear();
            NamespaceWarning = ""; Output = ""; ResultTarget = ""; ExecutedCommand = "";
            EnvironmentSummary = Text.Get("KubeDetectHint");
            Status = Text.Get("KubePreparingClient");
            var probe = await ExecuteAsync(connection, SshKubernetesCommands.Probe, generation, token);
            if (probe.ExitCode == 127) { EnvironmentSummary = Text.Get("KubeMissing"); Status = ""; return; }
            RequireSuccess(probe);
            using var version = JsonDocument.Parse(probe.Output);
            string clientVersion = version.RootElement.GetProperty("clientVersion").GetProperty("gitVersion").GetString() ?? "";
            IsInstalled = true; EnvironmentSummary = Text.Format("KubeDetected", clientVersion);
            Status = Text.Get("KubePreparingContext");
            var contexts = await ExecuteAsync(connection, SshKubernetesCommands.Contexts, generation, token); RequireSuccess(contexts);
            foreach (string name in contexts.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Take(1000)) Contexts.Add(name);
            var current = await ExecuteAsync(connection, SshKubernetesCommands.CurrentContext, generation, token);
            string preferred = current.ExitCode == 0 && !current.Truncated ? current.Output.Trim() : "";
            // 在选项对用户可用前加载每个上下文，避免切换后才触发网络请求。
            foreach (string context in Contexts)
                _snapshots[context] = await ReadSnapshotAsync(connection, context, generation, token);
            Context = Contexts.Contains(previousContext) ? previousContext : Contexts.Contains(preferred) ? preferred : Contexts.FirstOrDefault() ?? "";
            if (Context.Length == 0) { Status = Text.Get("KubeNoContext"); return; }
            ApplyContextSnapshot();
            if (Context == previousContext) Namespace = previousNamespace;
            IsInitialized = true;
        }
        finally
        {
            if (generation == _generation)
            {
                _initializing = false;
                if (IsInitialized) ApplyResourceSnapshot();
            }
        }
    });

    /// <summary>每种资源独立记录安全错误；单个列表缺少权限不能阻止其他列表使用。</summary>
    private async Task<KubernetesEnvironmentSnapshot> ReadSnapshotAsync(ISshWorkspaceConnection connection, string context, long generation, CancellationToken token)
    {
        Status = Text.Get("KubePreparingNamespaces");
        var preferred = await ExecuteAsync(connection, SshKubernetesCommands.DefaultNamespace(context), generation, token);
        string ns = preferred.ExitCode == 0 && !preferred.Truncated ? preferred.Output.Trim() : "";
        try { SshKubernetesCommands.ValidateName(ns); }
        catch (Exception) { ns = "default"; }
        var resources = new Dictionary<string, (IReadOnlyList<SshKubernetesResource> Items, string Error)>(StringComparer.Ordinal);
        foreach (string resource in SshKubernetesCommands.Resources)
        {
            Status = Text.Format("KubeLoadingSnapshot", context, Text.Get("KubeResource_" + resource));
            try
            {
                var result = await ExecuteAsync(connection, SshKubernetesCommands.Snapshot(context, resource), generation, token);
                RequireSuccess(result);
                resources[resource] = (SshKubernetesResource.Parse(result.Output), "");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { resources[resource] = ([], Text.Error(error, "KubeQueryFailed")); }
        }
        string warning = resources["namespaces"].Error.Length > 0 ? Text.Get("KubeNamespaceUnavailable") : "";
        var namespaces = resources["namespaces"].Items.Select(item => item.Name)
            .Concat(resources.Values.SelectMany(value => value.Items).Select(item => item.Namespace).Where(value => value.Length > 0))
            .Append(ns).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new(ns, namespaces, warning, resources);
    }

    private void ApplyContextSnapshot()
    {
        bool initializing = _initializing; _initializing = true;
        try
        {
            Namespaces.Clear(); NamespaceWarning = "";
            if (_snapshots.TryGetValue(Context, out var snapshot))
            {
                foreach (string ns in snapshot.Namespaces) Namespaces.Add(ns);
                NamespaceWarning = snapshot.NamespaceWarning;
                Namespace = snapshot.DefaultNamespace;
            }
            else Namespace = "default";
        }
        finally { _initializing = initializing; }
        InvalidateScope();
    }

    private void ApplyResourceSnapshot()
    {
        Items.Clear(); SelectedItem = null; Error = "";
        if (!_snapshots.TryGetValue(Context, out var snapshot)) { Status = ""; return; }
        var result = snapshot.Resources[Resource];
        Error = result.Error;
        foreach (var item in result.Items.Where(item => Resource is "nodes" or "namespaces" || item.Namespace == Namespace)) Items.Add(item);
        Status = Error.Length > 0 ? "" : Text.Format("KubeLoaded", Items.Count);
        Notify();
    }
}
