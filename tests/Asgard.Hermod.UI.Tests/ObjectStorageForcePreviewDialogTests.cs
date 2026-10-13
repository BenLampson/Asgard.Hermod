using System.Globalization;
using Asgard.Hermod.ObjectStorage.UI.Models;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>确认模型以稳定图片类型授权下载，本地化和取消不改变业务结果。</summary>
public sealed class ObjectStorageForcePreviewDialogTests
{
    /// <summary>未知类型不允许确认，无效枚举同样不能绕过显式选择。</summary>
    [Fact]
    public void UnknownType_RequiresValidChoiceBeforeConfirmation()
    {
        using var model = new ObjectStorageForcePreviewDialogViewModel("unknown.dat", 1024, null);
        Assert.False(model.IsTypeInferred);
        Assert.Null(model.SelectedType);
        Assert.False(model.ConfirmCommand.CanExecute(null));
        model.ConfirmCommand.Execute(null);
        Assert.Null(model.Result);
        model.SelectedType = (ObjectStoragePreviewType)99;
        Assert.False(model.ConfirmCommand.CanExecute(null));
        model.SelectedType = ObjectStoragePreviewType.Image;
        Assert.True(model.ConfirmCommand.CanExecute(null));
        model.ConfirmCommand.Execute(null);
        Assert.Equal(ObjectStoragePreviewType.Image, model.Result);
        Assert.False(model.ConfirmCommand.CanExecute(null));
    }

    /// <summary>推断只设置默认值，取消和直接关闭不返回可下载的类型。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InferredType_CancelAndDisposeDoNotAuthorizeDownload(bool cancel)
    {
        using var model = new ObjectStorageForcePreviewDialogViewModel("用户图片.png", 1024, ObjectStoragePreviewType.Image);
        Assert.True(model.ConfirmCommand.CanExecute(null));
        if (cancel) model.CancelCommand.Execute(null);
        else model.Dispose();
        model.ConfirmCommand.Execute(null);
        Assert.Null(model.Result);
        Assert.False(model.ConfirmCommand.CanExecute(null));
    }

    /// <summary>中文、英文及未支持语言的英语回退都包含大小和费用说明，用户名称不翻译。</summary>
    [Theory]
    [InlineData("en", "Image", "839 KB")]
    [InlineData("zh-Hans", "图片", "839 KB")]
    [InlineData("fr-FR", "Image", "839 KB")]
    public void Localization_UsesEnglishFallbackAndKeepsUserName(string language, string label, string size)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            using var model = new ObjectStorageForcePreviewDialogViewModel("用户图片.dat", 839 * 1024, null);
            Assert.Equal(label, Assert.Single(model.Options).Name);
            Assert.Equal("用户图片.dat", model.Name);
            Assert.Contains(size, model.TrafficWarning, StringComparison.Ordinal);
            Assert.Equal(Text.Format("ForcePreviewTrafficWarning", model.Size), model.TrafficWarning);
            Assert.DoesNotContain("ForcePreview", model.TrafficWarning, StringComparison.Ordinal);
            if (language == "fr-FR") Assert.Equal("Force preview", Text.Get("ForcePreview"));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
