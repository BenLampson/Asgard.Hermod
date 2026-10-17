using System.Globalization;
using Asgard.Hermod.UI.Views.Dialogs;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>在两种主题和语言下启动真实 WPF 窗口，核对规则页、编辑器及只读预览。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class FileOpeningWindowTests(WpfTestFixture fixture)
{
    /// <summary>导航、规则路径与按钮在标准和收窄窗口均能显示。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task Settings_OpensRulesAndEditorInBothLanguages(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        SettingsWindow? window = null;
        FileOpenRuleWindow? editorWindow = null;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var store = new FileOpenRuleTestStore();
            string program = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");
            store.Save(new("txt", program));
            using var model = new SettingsViewModel(new TagManagementTestService(), new TagDialogTestService(), new LanguagePreferenceTestStore(), store);
            await model.InitializeAsync(TestContext.Current.CancellationToken);
            window = new SettingsWindow(model);
            WindowPreviewCapture.Show(window);
            model.SelectCategoryCommand.Execute(model.Categories.Single(category => category.Id == "file-opening"));
            await WindowPreviewCapture.VerifyAsync(window, $"file-opening-{language}-{theme}");
            var view = Assert.Single(WindowTestVisuals.Descendants<FileOpeningSettingsView>(window));
            Assert.True(view.IsVisible);
            Assert.Same(model.FileOpening, view.DataContext);
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(view), text => text.Text == program && text.IsVisible);
            var files = Assert.IsType<FileOpeningSettingsViewModel>(model.FileOpening);
            var editor = files.CreateEditor(files.Rules[0]);
            editorWindow = new FileOpenRuleWindow(editor) { Owner = window };
            WindowPreviewCapture.Show(editorWindow);
            await WindowPreviewCapture.VerifyAsync(editorWindow, $"file-rule-editor-{language}-{theme}");
            Assert.Equal(Text.Get("EditFileRule"), editorWindow.Title);
            Assert.Equal(2, WindowTestVisuals.Descendants<TextBox>(editorWindow).Count());
            editorWindow.Close();
            window.Width = 800;
            window.Height = 540;
            await WindowPreviewCapture.VerifyAsync(window, $"file-opening-small-{language}-{theme}");
            var frame = WindowTestVisuals.Frame(window);
            foreach (var button in WindowTestVisuals.Descendants<Button>(view).Where(button => button.IsVisible))
            {
                Point origin = button.TranslatePoint(new Point(), frame);
                Assert.InRange(origin.X + button.ActualWidth, 1, frame.ActualWidth + 1);
                Assert.InRange(origin.Y + button.ActualHeight, 1, frame.ActualHeight + 1);
            }
        }
        finally
        {
            editorWindow?.Close(); window?.Close();
            CultureInfo.CurrentUICulture = previous;
            themes.SetTheme(UiTheme.Light);
        }
    });

    /// <summary>文本窗口支持原生复制且不能修改正文，收窄窗口仍可查看内容。</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    public Task Preview_IsReadOnlyAndResizable(string language) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        TextPreviewWindow? window = null;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            window = new TextPreviewWindow(new("hello.txt", "Hello 你好\r\n<script>plain text</script>", null));
            WindowPreviewCapture.Show(window);
            await WindowPreviewCapture.VerifyAsync(window, "file-text-preview-" + language);
            Assert.Equal(Text.Format("FilePreviewTitle", "hello.txt"), window.Title);
            Assert.Equal(ResizeMode.CanResize, window.ResizeMode);
            var box = Assert.Single(WindowTestVisuals.Descendants<TextBox>(window), item => item.IsVisible);
            Assert.True(box.IsReadOnly);
            Assert.Contains("<script>", box.Text);
            window.Width = 600; window.Height = 420;
            await WindowPreviewCapture.VerifyAsync(window, "file-text-preview-small-" + language);
        }
        finally { window?.Close(); CultureInfo.CurrentUICulture = previous; }
    });

}
