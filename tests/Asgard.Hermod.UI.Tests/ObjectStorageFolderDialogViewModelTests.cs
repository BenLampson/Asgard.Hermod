namespace Asgard.Hermod.UI.Tests;

/// <summary>单级文件夹名称在窗体内就地验证，合法名称只作为创建意图返回。</summary>
public sealed class ObjectStorageFolderDialogViewModelTests
{
    /// <summary>路径、点目录和控制字符不允许作为单级名称。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nested/folder")]
    [InlineData("nested\\folder")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("...")]
    [InlineData("name\u0001value")]
    [InlineData("name\t")]
    public void FolderName_InvalidValuesRemainOpenWithInlineError(string name)
    {
        using var model = CreateModel();
        int closed = 0;
        model.CloseRequested += (_, _) => closed++;
        model.Name = name;
        Assert.Empty(model.Error);
        model.ConfirmCommand.Execute(null);
        Assert.NotEmpty(model.Error);
        Assert.Null(model.Result);
        Assert.Equal(0, closed);
    }

    /// <summary>中文名称可保留，外部空白被清理，校验修正后才返回确认。</summary>
    [Fact]
    public void FolderName_ValidCorrectionReturnsTrimmedSingleLevelName()
    {
        using var model = CreateModel();
        var results = new List<bool>();
        model.CloseRequested += (_, accepted) => results.Add(accepted);
        model.ConfirmCommand.Execute(null);
        Assert.NotEmpty(model.Error);
        model.Name = "  发布文件  ";
        Assert.Empty(model.Error);
        model.ConfirmCommand.Execute(null);
        Assert.Equal("发布文件", model.Result);
        Assert.Equal(new[] { true }, results);
        model.ConfirmCommand.Execute(null);
        Assert.Equal(new[] { true }, results);
    }

    /// <summary>取消并不返回名称，也不能在窗体关闭之后继续确认。</summary>
    [Fact]
    public void FolderName_CancelNeverReturnsCreationInput()
    {
        using var model = CreateModel();
        model.Name = "documents";
        model.CancelCommand.Execute(null);
        model.ConfirmCommand.Execute(null);
        Assert.Null(model.Result);
        Assert.False(model.ConfirmCommand.CanExecute(null));
    }

    /// <summary>名称输入与父级位置相互独立。</summary>
    private static ObjectStorageFolderDialogViewModel CreateModel() =>
        new(new ObjectStorageBucket("assets-dev", "cn-hangzhou"), "releases/v0.9.0/");
}
