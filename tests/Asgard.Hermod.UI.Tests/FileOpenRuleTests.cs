using System.Globalization;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>扩展名规范化、持久化原子重命名与立即保存错误恢复。</summary>
public sealed class FileOpenRuleTests
{
    /// <summary>后缀匹配不依赖地区文化或输入大小写。</summary>
    [Theory]
    [InlineData(" TXT ", ".txt")]
    [InlineData(".JSON", ".json")]
    [InlineData("Md", ".md")]
    public void Extension_NormalizesSingleSuffix(string value, string expected) =>
        Assert.Equal(expected, FileOpenRule.NormalizeExtension(value));

    /// <summary>拒绝多扩展名、路径和通配符，而不是意外扩大关联范围。</summary>
    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..exe")]
    [InlineData("*.txt")]
    [InlineData("txt;json")]
    [InlineData("folder/txt")]
    [InlineData("tar.gz")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Extension_RejectsInvalidInput(string value) =>
        Assert.Equal("FileExtensionInvalid", Assert.Throws<InvalidOperationException>(() => FileOpenRule.NormalizeExtension(value)).Data["Hermod.ResourceKey"]);

    /// <summary>规则跨容器重新打开后保留，冲突编辑失败不能移除旧后缀。</summary>
    [Fact]
    public void Sqlite_ReopensRulesAndRenamesAtomically()
    {
        using var environment = new LocalStorageTestEnvironment();
        string program = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");
        using (var services = environment.CreateServices())
        {
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            var store = services.GetRequiredService<IFileOpenRuleStore>();
            Assert.Empty(store.Load());
            store.Save(new("TXT", program));
            store.Save(new("json", program));
            Assert.Throws<InvalidOperationException>(() => store.Save(new(".TXT", program)));
            Assert.Throws<InvalidOperationException>(() => store.Save(new("json", program), ".txt"));
            Assert.Equal(2, store.Load().Count);
            store.Save(new("md", program), ".TXT");
            Assert.Equal([".json", ".md"], store.Load().Select(rule => rule.Extension));
            store.Save(new("md", program), "md");
        }
        using (var services = environment.CreateServices())
        {
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            var store = services.GetRequiredService<IFileOpenRuleStore>();
            Assert.Equal([".json", ".md"], store.Load().Select(rule => rule.Extension));
            store.Remove("MD");
            Assert.Equal(".json", Assert.Single(store.Load()).Extension);
            Assert.Null(services.GetRequiredService<ILanguagePreferenceStore>().Load());
        }
    }

    /// <summary>保存失败保留编辑输入和旧规则，中英文错误不泄漏系统信息。</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("fr-FR")]
    public void Editor_SaveFailurePreservesRuleAndUsesSafeResource(string language)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var store = new FileOpenRuleTestStore();
            string program = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");
            var original = new FileOpenRule("txt", program);
            store.Save(original);
            store.FailSave = true;
            var editor = new FileOpenRuleEditorViewModel(store, original) { Extension = "md" };
            editor.SaveCommand.Execute(null);
            Assert.Null(editor.Result);
            Assert.Equal("md", editor.Extension);
            Assert.Equal(Text.Get("FileRuleSaveFailed"), editor.Error);
            Assert.DoesNotContain("private-database-path", editor.Error);
            Assert.Equal(original, Assert.Single(store.Load()));
            store.FailSave = false;
            editor.SaveCommand.Execute(null);
            Assert.Equal(".md", editor.Result?.Extension);
            var list = new FileOpeningSettingsViewModel(store);
            list.RemoveCommand.Execute(list.Rules[0]);
            Assert.Empty(store.Load());
            Assert.True(list.IsEmpty);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
