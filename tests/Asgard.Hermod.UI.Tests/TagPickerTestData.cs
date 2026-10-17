namespace Asgard.Hermod.UI.Tests;

/// <summary>标签测试的数据与节点查找，不复制产品的级联或匹配算法。</summary>
internal static class TagPickerTestData
{
    /// <summary>两棵分组树覆盖三级路径和跨分组选择。</summary>
    internal static IReadOnlyList<TagFilterDefinition> Definitions() =>
    [
        new("Environment", "Environment",
        [
            new("Production", "Environment/Production",
            [
                new("cn-east", "Environment/Production/cn-east"),
                new("eu-west", "Environment/Production/eu-west"),
            ]),
            new("Staging", "Environment/Staging"),
        ]),
        new("Owner", "Owner",
        [
            new("Platform", "Owner/Platform"),
            new("Payments", "Owner/Payments"),
        ]),
    ];

    /// <summary>遍历公开子节点，以业务路径查找而不依赖行号或展开状态。</summary>
    internal static IEnumerable<TagTreeNodeViewModel> Descendants(IEnumerable<TagTreeNodeViewModel> roots)
    {
        foreach (var node in roots)
        {
            yield return node;
            foreach (var child in Descendants(node.Children))
                yield return child;
        }
    }

    /// <summary>测试数据中的每个路径必须只对应一个可操作节点。</summary>
    internal static TagTreeNodeViewModel Node(TagTreePickerViewModel model, string path) =>
        Assert.Single(Descendants(model.Roots), node => node.Path == path);

    /// <summary>创建不进行连接或导航的侧栏，过滤仍使用产品真实 CollectionView。</summary>
    internal static InstanceSidebarViewModel CreateSidebar() => new(
        new TaggedWorkspaceDataSource(), new FakeInstanceEditor(), _ => { }, () => { });

    /// <summary>只观察用户当前能看到的实例，顺序与筛选语义无关。</summary>
    internal static string[] VisibleIds(InstanceSidebarViewModel sidebar) => sidebar.Instances
        .Cast<InstanceItemViewModel>().Select(item => item.Id).Order(StringComparer.Ordinal).ToArray();
}
