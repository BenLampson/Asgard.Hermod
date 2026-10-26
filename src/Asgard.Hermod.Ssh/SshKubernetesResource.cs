using System.Text.Json;

namespace Asgard.Hermod.Ssh;

/// <summary>Kubernetes 资源摘要；协议名称和状态原样显示，不读取 Secret 资源。</summary>
public sealed record SshKubernetesResource(string Name, string Namespace, string Status, string Ready = "", int? Restarts = null)
{
    /// <summary>读取有限列表中的名称、命名空间和状态，不保留 kubeconfig 或资源完整 JSON。</summary>
    public static IReadOnlyList<SshKubernetesResource> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var items = document.RootElement.GetProperty("items");
        if (items.GetArrayLength() > 2000) throw Text.Exception("KubeTooManyResources");
        return items.EnumerateArray().Select(item =>
        {
            var metadata = item.GetProperty("metadata");
            string name = Value(metadata, "name"); SshKubernetesCommands.ValidateName(name);
            string ns = Value(metadata, "namespace");
            string ready = ""; int? restarts = null;
            if (Value(item, "kind") == "Pod" && item.TryGetProperty("status", out var status) && status.TryGetProperty("containerStatuses", out var containers))
            {
                var states = containers.EnumerateArray().ToArray();
                int total = item.TryGetProperty("spec", out var spec) && spec.TryGetProperty("containers", out var definitions) ? definitions.GetArrayLength() : states.Length;
                ready = FormattableString.Invariant($"{states.Count(c => c.TryGetProperty("ready", out var r) && r.ValueKind == JsonValueKind.True)}/{total}");
                restarts = states.Sum(c => Number(c, "restartCount"));
            }
            return new SshKubernetesResource(name, ns, StatusOf(item), ready, restarts);
        }).ToArray();
    }

    private static string StatusOf(JsonElement item)
    {
        string kind = Value(item, "kind");
        if (kind == "Event") return Value(item, "reason");
        if (!item.TryGetProperty("status", out var status)) return "";
        if (kind == "Pod" && status.TryGetProperty("containerStatuses", out var containers))
        {
            foreach (var container in containers.EnumerateArray())
                if (container.TryGetProperty("state", out var state) && state.TryGetProperty("waiting", out var waiting) && Value(waiting, "reason") is { Length: > 0 } reason) return reason;
        }
        if (kind == "Deployment") return $"{Number(status, "readyReplicas")}/{Number(status, "replicas")}";
        if (kind == "Service" && item.TryGetProperty("spec", out var spec)) return Value(spec, "clusterIP");
        if (kind == "Node" && status.TryGetProperty("conditions", out var conditions))
            return conditions.EnumerateArray().FirstOrDefault(condition => Value(condition, "type") == "Ready") is var ready && ready.ValueKind == JsonValueKind.Object
                ? "Ready=" + Value(ready, "status") : "";
        return Value(status, "phase");
    }
    private static string Value(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static int Number(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.TryGetInt32(out int number) ? number : 0;
}
