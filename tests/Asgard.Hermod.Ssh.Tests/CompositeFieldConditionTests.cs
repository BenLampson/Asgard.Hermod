using Asgard.Hermod.Abstractions.Models;
using Xunit;

namespace Asgard.Hermod.Ssh.Tests;

/// <summary>连接路径和认证方式的所有条件必须同时满足。</summary>
public sealed class CompositeFieldConditionTests
{
    [Theory]
    [InlineData("direct", "password", false)]
    [InlineData("ssh", "agent", false)]
    [InlineData("ssh", "password", true)]
    public void Conditions_AreCombinedWithTheExistingDependency(string route, string authentication, bool expected)
    {
        var field = new InstanceConfigurationField("password", "Password", InstanceFieldKind.Secret)
        {
            VisibleWhenKey = "auth",
            VisibleWhenValue = "password",
            Conditions = [new("route", "ssh")],
        };
        Assert.Equal(expected, InstanceFieldValidation.IsApplicable(field,
            new Dictionary<string, string> { ["route"] = route, ["auth"] = authentication }));
    }
}
