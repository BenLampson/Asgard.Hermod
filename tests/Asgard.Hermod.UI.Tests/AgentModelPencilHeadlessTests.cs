using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Agent;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>模型设置、编辑器和切换卡使用真实控件离屏核对，禁止构造 Window。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AgentModelPencilHeadlessTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData(640)]
    [InlineData(3840)]
    public Task LongProfileAndModelNamesDoNotCoverToolbarActions(double width) => fixture.RunOffscreenAsync(async () =>
    {
        using var runtime = new AgentModelTestRuntime();
        var profile = AgentModelTestRuntime.First with { Name = new string('模', 80), Model = new string('m', 200) };
        runtime.Store.Save(new([profile], profile.Id));
        var connection = new SshWorkspaceTestConnection(); using var model = new SshAssistantViewModel(runtime.Client, () => connection, () => "host", new SshTroubleshootingWorkspaceTests.Dialogs());
        var view = new SshAssistantView { DataContext = model }; await Layout(view, width, 872);
        var panel = (AgentPanel)view.FindName("ChatPanel"); var toolbar = (DockPanel)panel.FindName("ToolbarColumn"); var selector = (AgentModelSelector)panel.FindName("ModelSelector");
        var actions = Assert.IsType<StackPanel>(toolbar.Children[0]);
        Assert.True(selector.TranslatePoint(new(selector.ActualWidth, 0), toolbar).X <= actions.TranslatePoint(new(), toolbar).X + 1);
        var arrow = Assert.Single(WindowTestVisuals.Descendants<IconView>(selector), icon => icon.Kind == "chevron-down");
        Assert.True(arrow.TranslatePoint(new(arrow.ActualWidth, 0), selector).X <= selector.ActualWidth + 1);
        Capture(view, $"model-long-name-{width}"); Assert.Null(PresentationSource.FromVisual(view));
        model.CloseSource(); await Layout(view, width, 872);
        Assert.Equal(Visibility.Collapsed, Assert.Single(WindowTestVisuals.Descendants<Button>(panel), button => button.Content as string == Text.Get("AgentConfigurationRequired")).Visibility);
    });

    [Theory]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    [InlineData("en", UiTheme.Dark)]
    public Task SettingsAndEditorFollowPencilSizesAndNeverPopulateStoredKeys(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); new ThemeService(Application.Current.Resources).SetTheme(theme);
        try
        {
            using var runtime = new AgentModelTestRuntime(); using var bindings = new WpfBindingTrace();
            runtime.Sync.SetStatus(ConfigurationSyncStatus.Synced);
            if (language == "zh-Hans") runtime.Store.Save(new([AgentModelTestRuntime.First with { Name = "公司模型" }, AgentModelTestRuntime.Second with { Name = "个人模型" }, AgentModelTestRuntime.Third with { Name = "本地模型" }], AgentModelTestRuntime.First.Id));
            using var settings = new SettingsViewModel(runtime.Services.GetRequiredService<ITagManagementService>(), new TagDialogTestService(),
                languageStore: runtime.Services.GetRequiredService<ILanguagePreferenceStore>(),
                modelCatalog: runtime.Catalog, agentClient: runtime.Client);
            settings.SelectCategoryCommand.Execute(settings.Categories.Single(category => category.Id == "agent"));
            var content = new SettingsView { DataContext = settings }; var caption = new SettingsCaption { Title = Text.Get("Settings") };
            var root = Frame(caption, content); await Layout(root, 1020, 800);
            Assert.Equal(60, caption.ActualHeight); Assert.Equal(740, content.ActualHeight);
            var grid = Assert.IsType<Grid>(content.Content); Assert.Equal(190, grid.ColumnDefinitions[0].ActualWidth);
            var list = Assert.Single(WindowTestVisuals.Descendants<AgentSettingsView>(content));
            var syncLabel = Assert.Single(WindowTestVisuals.Descendants<TextBlock>(list), label => label.Text == Text.Get("AgentModelsSynced"));
            Assert.Equal(((SolidColorBrush)list.FindResource("AgentSuccessBrush")).Color, ((SolidColorBrush)syncLabel.Foreground).Color);
            var rows = WindowTestVisuals.Descendants<Border>(list).Where(border => border.Height == 100).ToArray();
            Assert.Equal(3, rows.Length); Assert.All(rows, row => Assert.Equal(100, row.ActualHeight));
            Assert.Equal(120, rows[1].TranslatePoint(new(), root).Y - rows[0].TranslatePoint(new(), root).Y, 1);
            Assert.Equal(new[] { "general", "appearance", "agent", "account" }, settings.PrimaryCategories.Select(category => category.Id));
            Assert.Contains(settings.AdditionalCategories, category => category.Id == "tags");
            Capture(root, $"model-settings-{language}-{theme}");
            using var editor = settings.AgentModels!.CreateEditor(runtime.Catalog.Current.Profiles[0]);
            var form = new AgentModelEditorView { DataContext = editor }; var title = new SettingsCaption { Title = editor.Title, IsEditor = true };
            var editorRoot = Frame(title, form); await Layout(editorRoot, 660, 800);
            Assert.Equal(60, title.ActualHeight); Assert.Equal(740, form.ActualHeight);
            var inputs = WindowTestVisuals.Descendants<TextBox>(form).Where(box => box.Style == form.Resources["FieldInput"]).ToArray();
            Assert.Equal(3, inputs.Length); Assert.All(inputs, input => Assert.Equal(40, input.ActualHeight));
            var secret = Assert.Single(WindowTestVisuals.Descendants<SecretInput>(form)); Assert.Empty(secret.Value); Assert.False(secret.IsPasswordVisible); Assert.Equal(40, secret.ActualHeight);
            var body = Assert.IsType<Grid>(form.Content); Assert.Equal(68, body.RowDefinitions[1].ActualHeight);
            await editor.TestCommand.ExecuteAsync(); await Layout(editorRoot, 660, 800); Assert.True(editor.TestSucceeded);
            Capture(editorRoot, $"model-editor-{language}-{theme}");
            using var session = runtime.Client.CreateSession(); var selector = new AgentModelSelector { Session = session };
            var choices = new AgentModelChoices { DataContext = selector }; await Layout(choices, 440, 310);
            Assert.Equal(3, selector.Choices.Count); Assert.True(selector.Choices[0].IsSelected); Capture(choices, $"model-choices-{language}-{theme}");
            settings.SelectCategoryCommand.Execute(settings.AdditionalCategories.Single(category => category.Id == "tags"));
            Assert.True(settings.IsTagsSelected); Assert.True(settings.IsGeneralNavigationSelected);
            Assert.Empty(bindings.Messages); Assert.Null(Window.GetWindow(root)); Assert.Null(PresentationSource.FromVisual(root));
        }
        finally { Text.SetApplicationCulture(null); }
    });

    [Fact]
    public Task BusySwitchRequiresConfirmationStopsOriginalTaskAndPreservesDraftAndHistory() => fixture.RunOffscreenAsync(async () =>
    {
        using var runtime = new AgentModelTestRuntime(); var late = new TaskCompletionSource<AgentReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Client.Handler = _ => late.Task; var connection = new SshWorkspaceTestConnection();
        using var model = new SshAssistantViewModel(runtime.Client, () => connection, () => "host", new SshTroubleshootingWorkspaceTests.Dialogs()) { Prompt = "Original question" };
        var view = new SshAssistantView { DataContext = model }; await Layout(view, 1120, 872);
        var panel = (AgentPanel)view.FindName("ChatPanel"); var selector = (AgentModelSelector)panel.FindName("ModelSelector");
        var running = model.SendCommand.ExecuteAsync(); model.Prompt = "Next draft"; await Layout(view, 1120, 872);
        selector.SelectCommand.Execute(selector.Choices[1]); Assert.True(selector.NeedsConfirmation); Assert.True(model.IsBusy);
        Assert.Equal(AgentModelTestRuntime.First.Id, model.ModelSession!.SelectedId);
        selector.CancelSwitchCommand.Execute(null); Assert.False(selector.NeedsConfirmation); Assert.True(model.IsBusy);
        selector.SelectCommand.Execute(selector.Choices[1]); await Layout(view, 1120, 872); Capture(view, "model-switch-pending");
        Assert.True(Assert.Single(WindowTestVisuals.Descendants<Button>(panel), button => ReferenceEquals(button.Command, selector.ConfirmSwitchCommand)).IsEnabled);
        await selector.ConfirmSwitchCommand.ExecuteAsync(); await running;
        Assert.Empty(selector.Error); Assert.False(selector.NeedsConfirmation); Assert.False(model.IsBusy);
        Assert.Equal(AgentModelTestRuntime.Second.Id, model.ModelSession.SelectedId); Assert.Equal("Next draft", model.Prompt);
        Assert.Equal("Original question", model.Messages[0].Content); Assert.Single(runtime.Client.Captured);
        late.SetResult(AgentReply.FromContent("Late old-model answer")); await WpfTestFixture.DrainDispatcherAsync();
        Assert.DoesNotContain(model.Messages, message => message.Content == "Late old-model answer");
    });

    private static DockPanel Frame(SettingsCaption caption, FrameworkElement content)
    {
        var root = new DockPanel(); root.SetResourceReference(Control.FontFamilyProperty, "UiFont");
        DockPanel.SetDock(caption, Dock.Top); root.Children.Add(caption); root.Children.Add(content); return root;
    }
    private static async Task Layout(FrameworkElement root, double width, double height)
    {
        await WpfTestFixture.DrainDispatcherAsync(); root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); await WpfTestFixture.DrainDispatcherAsync();
    }
    private static void Capture(FrameworkElement root, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT"); if (directory is null) return;
        Directory.CreateDirectory(directory);
        foreach (int scale in new[] { 1, 2 })
        {
            var bitmap = new RenderTargetBitmap((int)(root.ActualWidth * scale), (int)(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(directory, $"{name}-{scale * 100}pct.png")); encoder.Save(stream);
        }
    }
}
