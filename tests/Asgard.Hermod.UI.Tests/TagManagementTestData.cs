namespace Asgard.Hermod.UI.Tests;

/// <summary>使用真实层级和值对象覆盖环境、父级重名和深层自定义标签。</summary>
internal static class TagManagementTestData
{
    /// <summary>环境故意使用不同顺序，验证管理页面按设计固定排列。</summary>
    internal static List<ManagedTag> Create() =>
    [
        new("development", "Development", null, "#22C55E", true, "Development"),
        new("staging", "Staging", null, "#F59E0B", true, "Staging"),
        new("production", "Production", null, "#EF4444", true, "Production"),
        new("team", "Team", null, "#8B5CF6", false, "Team", 5),
        new("platform", "Platform", "team", "#3B82F6", false, "Team/Platform", 3),
        new("api", "API", "platform", "#EC4899", false, "Team/Platform/API", 1),
        new("region", "Region", null, "#14B8A6", false, "Region", 3),
        new("eu", "EU West", "region", "#22C55E", false, "Region/EU West", 2),
        new("us", "US East", "region", "#F59E0B", false, "Region/US East", 1),
    ];
}
