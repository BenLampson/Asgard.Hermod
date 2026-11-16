namespace Asgard.Hermod.UI.Tests;

/// <summary>验证覆盖只返回用户确认，提示窗体无写入操作。</summary>
public sealed class ObjectStorageConfirmationDialogViewModelTests
{
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
}
