namespace Asgard.Hermod.Sync.Tests;

/// <summary>三方合并验证不同字段、相同结果、删除和敏感冲突。</summary>
public sealed class WorkspaceMergeTests
{
    private static readonly SyncedInstance Instance = new("one", "Original", "storage", "provider",
        new Dictionary<string, string> { ["endpoint"] = "original", ["secret"] = "credential-original" }, DateTime.UnixEpoch, []);
    /// <summary>两台设备修改不同字段自动组合，参数字典顺序无影响。</summary>
    [Fact]
    public void DifferentFields_MergeAutomatically()
    {
        var merge = new WorkspaceMerge();
        var cloud = Instance with { Parameters = new Dictionary<string, string> { ["secret"] = "credential-original", ["endpoint"] = "cloud" } };
        var result = merge.Merge(new(1, [Instance], []), new(1, [Instance with { Name = "Local name" }], []), new(1, [cloud], []));
        Assert.Empty(merge.Conflicts); Assert.Equal("Local name", result.Instances[0].Name); Assert.Equal("cloud", result.Instances[0].Parameters["endpoint"]);
    }
    /// <summary>同字段改成相同值无需选择。</summary>
    [Fact]
    public void EqualChanges_DoNotConflict()
    {
        var merge = new WorkspaceMerge(); var changed = Instance with { Name = "Same" };
        merge.Merge(new(1, [Instance], []), new(1, [changed], []), new(1, [changed], [])); Assert.Empty(merge.Conflicts);
    }
    /// <summary>修改与删除冲突，选择云端删除后不会复活。</summary>
    [Fact]
    public void ModifyAgainstDelete_RequiresExplicitChoice()
    {
        var merge = new WorkspaceMerge(); var local = new WorkspaceSnapshot(1, [Instance with { Name = "Changed" }], []);
        merge.Merge(new(1, [Instance], []), local, new(1, [], [])); var conflict = Assert.Single(merge.Conflicts);
        var result = merge.Merge(new(1, [Instance], []), local, new(1, [], []), new Dictionary<string, bool> { [conflict.Key] = true });
        Assert.Empty(merge.Conflicts); Assert.Empty(result.Instances);
    }
    /// <summary>一端删除另一端未改时自动删除。</summary>
    [Fact]
    public void DeleteAgainstUnchanged_RemovesEntry()
    {
        var merge = new WorkspaceMerge(); var result = merge.Merge(new(1, [Instance], []), new(1, [], []), new(1, [Instance], []));
        Assert.Empty(merge.Conflicts); Assert.Empty(result.Instances);
    }
    /// <summary>无共同基准时互不重叠新增自动组合，相同标识分歧需选择。</summary>
    [Fact]
    public void FirstSync_CombinesIndependentAdditions()
    {
        var merge = new WorkspaceMerge(); var result = merge.Merge(null, new(1, [Instance], []), new(1, [Instance with { Id = "two" }], []));
        Assert.Equal(2, result.Instances.Count); Assert.Empty(merge.Conflicts);
        merge.Merge(null, new(1, [Instance], []), new(1, [Instance with { Name = "Other" }], [])); Assert.Single(merge.Conflicts);
    }
    /// <summary>秘密的双方值都不能进入冲突预览。</summary>
    [Fact]
    public void CredentialConflict_HidesValuesAndAppliesChoice()
    {
        SyncedInstance Changed(string secret) => Instance with { Parameters = new Dictionary<string, string>(Instance.Parameters) { ["secret"] = secret } };
        var merge = new WorkspaceMerge(); var local = new WorkspaceSnapshot(1, [Changed("private-local")], []); var cloud = new WorkspaceSnapshot(1, [Changed("private-cloud")], []);
        merge.Merge(new(1, [Instance], []), local, cloud); var conflict = Assert.Single(merge.Conflicts);
        Assert.DoesNotContain("private", conflict.LocalValue); Assert.DoesNotContain("private", conflict.CloudValue);
        var resolved = merge.Merge(new(1, [Instance], []), local, cloud, new Dictionary<string, bool> { [conflict.Key] = true });
        Assert.Equal("private-cloud", resolved.Instances[0].Parameters["secret"]); Assert.Empty(merge.Conflicts);
    }
}
