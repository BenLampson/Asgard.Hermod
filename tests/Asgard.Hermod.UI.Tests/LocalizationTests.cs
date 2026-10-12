using System.Globalization;
using System.Resources;
using System.Text;
using Asgard.Hermod.Abstractions.Localization;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证语言优先级、英语回退、模板完整性与错误展示边界。</summary>
public sealed class LocalizationTests
{
    /// <summary>系统环境翻译只影响显示，用户标签和业务路径不被修改。</summary>
    [Theory]
    [InlineData("en", "Production")]
    [InlineData("zh-Hans", "生产")]
    public void BuiltInTags_LocalizeDisplayAndPreserveUserNames(string language, string expected)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            Assert.Equal(expected, UI.Localization.TagDisplayText.Name("Environment/Production", "Production"));
            Assert.Equal("Production", UI.Localization.TagDisplayText.Name("Owner/Production", "Production"));
            Assert.Equal(expected + " / 用户名称", UI.Localization.TagDisplayText.Summary("Environment/Production/用户名称"));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    /// <summary>系统语言按支持列表匹配，手动选择优先；未支持语种统一使用英语。</summary>
    [Theory]
    [InlineData(null, "zh-CN", "zh-Hans")]
    [InlineData("", "zh-TW", "zh-Hans")]
    [InlineData(null, "en-GB", "en")]
    [InlineData(null, "fr-FR", "en")]
    [InlineData(null, "ja-JP", "en")]
    [InlineData("en", "zh-CN", "en")]
    [InlineData("zh-Hans", "en-US", "zh-Hans")]
    [InlineData("fr", "zh-CN", "en")]
    public void ResolveLanguage_HonorsPreferenceAndEnglishFallback(string? preference, string system, string expected) =>
        Assert.Equal(expected, Text.ResolveCulture(preference, CultureInfo.GetCultureInfo(system)).Name);

    /// <summary>英文与中文真实资源包含一致的键和参数槽位，不依赖运行器机器语言。</summary>
    [Fact]
    public void ResourceCatalogs_AreCompleteAndPreservePlaceholders()
    {
        var manager = new ResourceManager("Asgard.Hermod.Abstractions.Localization.Strings", typeof(Text).Assembly);
        var english = Assert.IsType<ResourceSet>(manager.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, false), exactMatch: false);
        var chinese = Assert.IsType<ResourceSet>(manager.GetResourceSet(CultureInfo.GetCultureInfo("zh-Hans"), true, false), exactMatch: false);
        var englishKeys = english.Cast<System.Collections.DictionaryEntry>().Select(entry => (string)entry.Key).Order().ToArray();
        var chineseKeys = chinese.Cast<System.Collections.DictionaryEntry>().Select(entry => (string)entry.Key).Order().ToArray();
        Assert.True(englishKeys.Length > 450);
        Assert.Equal(englishKeys, chineseKeys);
        foreach (string key in englishKeys)
        {
            var first = Assert.IsType<string>(english.GetObject(key));
            var second = Assert.IsType<string>(chinese.GetObject(key));
            Assert.NotEmpty(first);
            Assert.NotEmpty(second);
            Assert.Equal(CompositeFormat.Parse(first).MinimumArgumentCount, CompositeFormat.Parse(second).MinimumArgumentCount);
        }
        // 中文语言族缺少特定地区卫星资源时，使用共享简体中文资源。
        Assert.Equal("语言", manager.GetString("Language", CultureInfo.GetCultureInfo("zh-Hans-CN")));
        // 尚未提供法语卫星资源，单条资源读取会回退默认英语。
        Assert.Equal("Language", manager.GetString("Language", CultureInfo.GetCultureInfo("fr-FR")));
    }

    /// <summary>参数可按翻译语序重排，操作系统区域格式不被界面语言覆盖。</summary>
    [Theory]
    [InlineData("en", "bucket-a", "read-back", "Temporary object read-back failed in bucket bucket-a.")]
    [InlineData("zh-Hans", "bucket-a", "回读", "容器 bucket-a 的临时对象回读失败")]
    public void Format_AllowsArgumentReordering(string language, string bucket, string stage, string expected)
    {
        var previous = CultureInfo.CurrentUICulture;
        var regional = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            Assert.StartsWith(expected, Text.Format("TemporaryObjectFailedInBucketCheckTheNetworkCredentialsAndObjectPermissions", bucket, stage));
            Assert.Same(regional, CultureInfo.CurrentCulture);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    /// <summary>业务异常在展示时解析文案键，原始系统异常不会把凭据泄露到用户界面。</summary>
    [Fact]
    public void Error_RendersCurrentLanguageAndHidesUnknownMessages()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-Hans");
            var validation = Text.Exception("Enter", "Name");
            var storage = ObjectStorageException.FromResource("TheObjectNoLongerExistsRefreshTheObjectList", "NoSuchKey");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            Assert.Equal("Enter Name.", Text.Error(validation));
            Assert.Equal("The object no longer exists. Refresh the object list.", Text.Error(storage));
            Assert.Equal("NoSuchKey", storage.ErrorCode);
            Assert.Equal("The operation failed. Try again.", Text.Error(new IOException("secret=should-not-be-shown")));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
