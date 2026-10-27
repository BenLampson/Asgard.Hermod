using Xunit;

namespace Asgard.Hermod.Ssh.Tests;

/// <summary>kubectl 的命令参数、集群作用域和结构化资源解析必须独立于显示语言。</summary>
public sealed class SshKubernetesProtocolTests
{
    /// <summary>上下文整体转义，显式命名空间与超时不能通过名称注入覆盖。</summary>
    [Fact]
    public void CommandsPinScopeAndQuoteContextWithoutChangingConfiguration()
    {
        string command = SshKubernetesCommands.List("team's $(touch /tmp/unwanted)", "production", "pods");
        Assert.Contains("--context 'team'\"'\"'s $(touch /tmp/unwanted)'", command);
        Assert.Contains("--namespace 'production'", command); Assert.Contains("--request-timeout=15s", command);
        Assert.DoesNotContain("--namespace", SshKubernetesCommands.List("prod", "", "nodes"));
        Assert.DoesNotContain("use-context", command);
        Assert.Contains("get pods --all-namespaces -o json", SshKubernetesCommands.Snapshot("prod", "pods"));
        Assert.DoesNotContain("--namespace", SshKubernetesCommands.Snapshot("prod", "pods"));
        Assert.DoesNotContain("--all-namespaces", SshKubernetesCommands.Snapshot("prod", "namespaces"));
        Assert.DoesNotContain("--all-namespaces", SshKubernetesCommands.Snapshot("prod", "nodes"));
        Assert.EndsWith("--all-containers=true --tail=500 --timestamps=true", SshKubernetesCommands.Action("prod", "app", "pods", "web-1", "logs"));
        Assert.EndsWith("--watch=false", SshKubernetesCommands.Action("prod", "app", "deployments", "web", "rollout"));
    }

    /// <summary>资源类型和目标不能携带额外选项、Shell 或跨作用域参数。</summary>
    [Theory]
    [InlineData("pods; id", "web", "describe")]
    [InlineData("pods", "web;id", "logs")]
    [InlineData("pods", "--all", "logs")]
    [InlineData("services", "web", "restart")]
    [InlineData("deployments", "web", "delete")]
    public void InvalidActionsAreRejected(string resource, string name, string action) =>
        Assert.ThrowsAny<Exception>(() => SshKubernetesCommands.Action("prod", "app", resource, name, action));

    /// <summary>只保留资源摘要，Kubernetes 原始名称和状态不被翻译。</summary>
    [Fact]
    public void ResourceListParsesPodsAndDeploymentReadiness()
    {
        var items = SshKubernetesResource.Parse("""
            {"items":[{"kind":"Pod","metadata":{"name":"web-1","namespace":"app"},"status":{"phase":"Running"}},
            {"kind":"Deployment","metadata":{"name":"web","namespace":"app"},"status":{"replicas":3,"readyReplicas":2}}]}
            """);
        Assert.Equal(new SshKubernetesResource("web-1", "app", "Running"), items[0]); Assert.Equal("2/3", items[1].Status);
    }
    /// <summary>Pod 阶段不能掩盖容器崩溃，汇总就绪数与重启数时保留实际容器数量。</summary>
    [Fact]
    public void PodReadinessAndWaitingReasonDoNotHideCrashes()
    {
        var item = Assert.Single(SshKubernetesResource.Parse("""
            {"items":[{"kind":"Pod","metadata":{"name":"worker-1","namespace":"app"},"spec":{"containers":[{},{}]},
            "status":{"phase":"Running","containerStatuses":[{"ready":true,"restartCount":1,"state":{"running":{}}},
            {"ready":false,"restartCount":5,"state":{"waiting":{"reason":"CrashLoopBackOff"}}}]}}]}
            """));
        Assert.Equal("CrashLoopBackOff", item.Status); Assert.Equal("1/2", item.Ready); Assert.Equal(6, item.Restarts);
        string events = SshKubernetesCommands.Action("prod", "app", "pods", "worker-1", "events");
        Assert.Contains("--namespace 'app'", events); Assert.EndsWith("--field-selector 'involvedObject.name=worker-1' -o wide", events);
        Assert.EndsWith("config view --minify -o jsonpath='{..namespace}'", SshKubernetesCommands.DefaultNamespace("prod"));
    }

}
