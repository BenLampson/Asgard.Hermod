namespace Asgard.Hermod.UI.Tests;

/// <summary>验证删除和覆盖只返回用户确认，不扩大对象范围。</summary>
public sealed class ObjectStorageConfirmationDialogViewModelTests
{
    /// <summary>复制所有明确键，外部计划变化不会扩大或缩小已显示的确认范围。</summary>
    [Fact]
    public void DeleteConfirmation_SnapshotsEntirePlanAndRequiresExplicitConfirm()
    {
        var keys = new List<string> { "releases/config.json", "releases/nested/manifest.json", "releases/folder/" };
        using var model = ObjectStorageConfirmationDialogViewModel.ForDelete(Bucket(), "releases/", new(keys));
        keys.Clear();
        Assert.Equal(new[] { "releases/config.json", "releases/nested/manifest.json", "releases/folder/" }, model.Keys);
        Assert.Equal("删除这 3 个对象？", model.Heading);
        Assert.Equal("删除 3 个对象", model.ConfirmLabel);
        Assert.False(model.Result);
        Assert.True(model.IsDestructive);
        model.ConfirmCommand.Execute(null);
        Assert.True(model.Result);
        Assert.False(model.ConfirmCommand.CanExecute(null));
    }

    /// <summary>空计划不能确认，取消不会返回删除意图。</summary>
    [Fact]
    public void DeleteConfirmation_EmptyPlanAndCancellationDoNotConfirm()
    {
        using var empty = ObjectStorageConfirmationDialogViewModel.ForDelete(Bucket(), "", new([]));
        Assert.False(empty.ConfirmCommand.CanExecute(null));
        empty.ConfirmCommand.Execute(null);
        Assert.False(empty.Result);
        using var populated = ObjectStorageConfirmationDialogViewModel.ForDelete(Bucket(), "", new(["private.json"]));
        populated.CancelCommand.Execute(null);
        populated.ConfirmCommand.Execute(null);
        Assert.False(populated.Result);
    }

    /// <summary>同名覆盖显示完整键且默认未确认。</summary>
    [Fact]
    public void OverwriteConfirmation_ContainsExactKeyAndCanBeCancelled()
    {
        using var model = ObjectStorageConfirmationDialogViewModel.ForOverwrite("private/nested/config.json");
        Assert.Equal("private/nested/config.json", Assert.Single(model.Keys));
        Assert.Equal("覆盖并上传", model.ConfirmLabel);
        Assert.False(model.Result);
        model.CancelCommand.Execute(null);
        Assert.False(model.Result);
    }

    /// <summary>纯提示没有对象操作语义，只提供关闭出口。</summary>
    [Fact]
    public void Notice_HasNoDestructiveConfirmation()
    {
        using var model = ObjectStorageConfirmationDialogViewModel.ForNotice("复制失败", "无法写入剪贴板，请稍后重试。");
        Assert.False(model.IsDestructive);
        Assert.False(model.RequiresConfirmation);
        Assert.Empty(model.Keys);
        Assert.Equal("关闭", model.ConfirmLabel);
        Assert.True(model.ConfirmCommand.CanExecute(null));
    }

    /// <summary>所有用例使用安全的固定容器元数据。</summary>
    private static ObjectStorageBucket Bucket() => new("assets-dev", "cn-hangzhou");
}
