using System.Text.RegularExpressions;

namespace Asgard.Hermod.Ssh;

/// <summary>kubectl 固定命令目录；显式指定上下文，不改写远端 kubeconfig。</summary>
public static class SshKubernetesCommands
{
    /// <summary>仅探测客户端，不读取配置凭据或访问集群。</summary>
    public const string Probe = "command -v kubectl >/dev/null 2>&1 || exit 127; kubectl version --client -o json";
    /// <summary>只读取上下文名称，不获取用户令牌或证书。</summary>
    public const string Contexts = "kubectl config get-contexts -o name";
    /// <summary>读取默认上下文，不切换配置。</summary>
    public const string CurrentContext = "kubectl config current-context";
    /// <summary>只读取指定上下文的默认命名空间，不输出 kubeconfig 证书或令牌。</summary>
    public static string DefaultNamespace(string context) => Prefix(context, "", "namespaces") + " config view --minify -o jsonpath='{..namespace}'";
    /// <summary>固定资源类型，避免拼接任意 kubectl 子命令。</summary>
    public static IReadOnlyList<string> Resources { get; } = Array.AsReadOnly<string>(["pods", "deployments", "services", "nodes", "namespaces", "events"]);

    /// <summary>列表返回结构化 JSON；集群资源不携带命名空间。</summary>
    public static string List(string context, string ns, string resource)
    {
        ValidateResource(resource);
        return Prefix(context, ns, resource) + " get " + resource + " -o json";
    }

    /// <summary>常用操作只面向已选资源，日志有界，发布状态不持续监听。</summary>
    public static string Action(string context, string ns, string resource, string name, string action)
    {
        ValidateResource(resource); ValidateName(name);
        string prefix = Prefix(context, ns, resource);
        return action switch
        {
            "describe" => prefix + " describe " + resource + " " + Quote(name),
            "logs" when resource == "pods" => prefix + " logs " + Quote(name) + " --all-containers=true --tail=500 --timestamps=true",
            "events" when resource is "pods" or "deployments" or "services" => prefix + " get events --field-selector " + Quote("involvedObject.name=" + name) + " -o wide",
            "rollout" when resource == "deployments" => prefix + " rollout status deployment/" + name + " --watch=false",
            "restart" when resource == "deployments" => prefix + " rollout restart deployment/" + name,
            _ => throw Text.Exception("KubeInvalidParameters"),
        };
    }

    /// <summary>名称按 Kubernetes DNS 子域规则校验，不能注入选项或 Shell 语法。</summary>
    public static void ValidateName(string value)
    {
        if (value.Length is < 1 or > 253 || !Regex.IsMatch(value, @"\A[a-z0-9](?:[a-z0-9.\-]*[a-z0-9])?\z", RegexOptions.CultureInvariant))
            throw Text.Exception("KubeInvalidParameters");
    }

    private static void ValidateResource(string value)
    {
        if (!Resources.Contains(value)) throw Text.Exception("KubeInvalidParameters");
    }
    private static string Prefix(string context, string ns, string resource)
    {
        if (string.IsNullOrWhiteSpace(context) || context.Length > 1024 || context.Any(char.IsControl)) throw Text.Exception("KubeInvalidParameters");
        string prefix = "kubectl --context " + Quote(context) + " --request-timeout=15s";
        if (resource is not ("nodes" or "namespaces")) { ValidateName(ns); prefix += " --namespace " + Quote(ns); }
        return prefix;
    }
    private static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
}
