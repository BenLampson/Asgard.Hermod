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
}
