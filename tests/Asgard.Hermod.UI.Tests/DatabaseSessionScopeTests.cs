namespace Asgard.Hermod.UI.Tests;

/// <summary>共享规则以稳定身份判断，两种引擎不得各自解释范围语义。</summary>
public sealed class DatabaseSessionScopeTests
{
    /// <summary>唯一数据库直接使用，多个候选保持未选择。</summary>
    [Fact]
    public void InitialTarget_UsesOnlyUnambiguousDatabase()
    {
        var single = Scope("app"); Assert.Equal("app", single.ExecutionTarget);
        var multiple = Scope("app", "reports"); Assert.Empty(multiple.ExecutionTarget);
        multiple.SelectExecutionTarget("reports"); Assert.Equal("reports", multiple.ExecutionTarget);
    }

    /// <summary>整批拒绝跨实例添加，不发生部分修改。</summary>
    [Fact]
    public void Add_RejectsDifferentInstanceAtomically()
    {
        var scope = Scope("app");
        Assert.ThrowsAny<Exception>(() => scope.Add([new("instance", "reports"), new("other", "secret")]));
        Assert.Single(scope.Databases); Assert.Equal("app", scope.ExecutionTarget); Assert.Equal(0, scope.Revision);
        scope.Add([new("instance", "reports")]); Assert.Equal("app", scope.ExecutionTarget);
    }

    /// <summary>最后一个不能移除；唯一替代目标在确认后直接使用。</summary>
    [Fact]
    public void Removal_RetainsOneDatabaseAndRequiresFreshConfirmation()
    {
        var scope = Scope("app"); Assert.Null(scope.PrepareRemoval("app"));
        scope.Add([new("instance", "reports")]);
        var request = scope.PrepareRemoval("app")!;
        Assert.Equal("reports", scope.ReplacementFor(request, new()));
        Assert.Equal(2, scope.Databases.Count); Assert.Equal("app", scope.ExecutionTarget);
        Assert.True(scope.ApplyConfirmedRemoval(request, new()));
        Assert.Equal("reports", scope.ExecutionTarget); Assert.Single(scope.Databases);
        Assert.False(scope.ApplyConfirmedRemoval(request, new()));
    }

    /// <summary>多个替代目标不得替用户挑选；范围变化使旧确认失效。</summary>
    [Fact]
    public void Removal_MultipleReplacementsNeedExplicitChoice()
    {
        var scope = Scope("app", "reports", "audit"); scope.SelectExecutionTarget("app");
        var request = scope.PrepareRemoval("app")!;
        Assert.Null(scope.ReplacementFor(request, new()));
        Assert.False(scope.ApplyConfirmedRemoval(request, new("outside")));
        scope.Add([new("instance", "archive")]);
        Assert.False(scope.ApplyConfirmedRemoval(request, new("reports")));
        Assert.True(scope.ApplyConfirmedRemoval(scope.PrepareRemoval("app")!, new("audit")));
        Assert.Equal("audit", scope.ExecutionTarget);
    }

    /// <summary>移除非目标库不改变用户先前选择。</summary>
    [Fact]
    public void Removal_OfNonTargetPreservesTarget()
    {
        var scope = Scope("app", "reports"); scope.SelectExecutionTarget("app");
        Assert.True(scope.ApplyConfirmedRemoval(scope.PrepareRemoval("reports")!, new())); Assert.Equal("app", scope.ExecutionTarget);
    }

    private static DatabaseSessionScope Scope(params string[] names) => new("instance", "Session 1", names.Select(name => new DatabaseIdentity("instance", name)));
}
