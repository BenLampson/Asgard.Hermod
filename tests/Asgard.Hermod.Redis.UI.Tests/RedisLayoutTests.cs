using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Core;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.Redis.Tests;

/// <summary>真实 WPF 模板验证中英文、浅深主题、列宽及键页签视图缓存。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class RedisLayoutTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task Workspace_RendersTabsWithoutKeyCheckboxesAndKeepsScrollWhenSwitching(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        using var trace = new WpfBindingTrace();
        var previous = CultureInfo.CurrentUICulture; var previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        Text.Initialize(language, CultureInfo.InvariantCulture); var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var session = new RedisTestSession
        {
            Snapshot = new("string", 4000, 240000, [new([], System.Text.Encoding.UTF8.GetBytes(string.Join('\n', Enumerable.Range(1, 200))))], true),
            Scan = (_, _, _) => Task.FromResult(new RedisScanPage("0", [RedisKeyId.FromText("cache:homepage"), RedisKeyId.FromText("user:1042:profile")]))
        };
        var configs = new RedisTestConfigurations(); await using var model = new RedisWorkspaceViewModel(new(RedisTestConfigurations.Instance), configs, session, configs, new RedisTestDialogs());
        var view = new RedisWorkspaceView { DataContext = model }; var window = new HermodWindow { Width = 1180, Height = 800, Content = view, Title = "Redis test" };
        try
        {
            WindowPreviewCapture.Show(window); await model.InitializeAsync(TestContext.Current.CancellationToken);
            await model.OpenAsync(RedisKeyId.FromText("cache:homepage")); var first = model.ActiveTab!;
            await model.OpenAsync(RedisKeyId.FromText("user:1042:profile")); model.ActivateTabCommand.Execute(first);
            await WindowPreviewCapture.VerifyAsync(window, $"redis-workspace-wide-{language}-{theme}");
            var tabs = Assert.IsType<WorkspaceTabStrip>(view.FindName("KeyTabs")); Assert.Equal(2, tabs.Items.Count);
            var item = Assert.IsType<ListBoxItem>(tabs.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.Contains("cache:homepage", Assert.IsType<string>(item.ToolTip));
            Assert.Contains("cache:homepage", System.Windows.Automation.AutomationProperties.GetName(item));
            Assert.DoesNotContain(WindowTestVisuals.Descendants<CheckBox>(view), checkbox => checkbox.IsVisible);
            var treePanel = Assert.IsType<Border>(view.FindName("TreePanel")); Assert.InRange(treePanel.ActualWidth, 280, 352);
            var host = Assert.IsType<ContentControl>(view.FindName("ContentHost")); var firstView = Assert.IsType<RedisKeyContentView>(host.Content);
            var codeEditor = Assert.IsType<CodeEditor>(firstView.FindName("StringEditor")); var editor = Assert.IsType<TextBox>(codeEditor.FindName("Input")); editor.ScrollToVerticalOffset(100);
            model.CycleTab(1); model.CycleTab(-1); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Same(firstView, host.Content); Assert.True(editor.VerticalOffset > 0);
            await first.EditCommand.ExecuteAsync(); first.EditorText = "unsaved value";
            await WindowPreviewCapture.VerifyAsync(window, $"redis-workspace-edit-{language}-{theme}");
            Assert.False(editor.IsReadOnly); Assert.True(first.IsDirty);
            window.Width = 620; window.Height = 640; await WindowPreviewCapture.VerifyAsync(window, $"redis-workspace-narrow-{language}-{theme}");
            Assert.False(treePanel.IsVisible); Assert.True(host.ActualWidth > 500);
            Assert.True(Assert.IsType<SegmentedSelector>(view.FindName("CompactSelector")).IsVisible);
            model.ShowTree = true; await WindowPreviewCapture.VerifyAsync(window, $"redis-workspace-narrow-tree-{language}-{theme}");
            Assert.True(treePanel.IsVisible); Assert.False(Assert.IsType<Grid>(view.FindName("KeyPanel")).IsVisible);
            Assert.True(trace.Messages.Count == 0, string.Join(Environment.NewLine, trace.Messages.Distinct()));
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); CultureInfo.CurrentUICulture = previous; CultureInfo.DefaultThreadCurrentUICulture = previousDefault; }
    });

    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task Wizard_UsesSharedFiveStepFlowAndSensitiveFields(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        using var trace = new WpfBindingTrace();
        var previous = CultureInfo.CurrentUICulture; var previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        Text.Initialize(language, CultureInfo.InvariantCulture); var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        try
        {
            foreach (string topology in new[] { RedisIds.Standalone, RedisIds.Sentinel, RedisIds.Cluster })
            {
                var session = new RedisTestSession(); var configs = new RedisTestConfigurations();
                var registry = new InstanceProviderRegistry(new[] { RedisIds.Standalone, RedisIds.Sentinel, RedisIds.Cluster }.Select(id => new RedisInstanceProvider(id, session)));
                using var model = new NewInstanceViewModel(registry, configs); model.SelectedType = Assert.Single(model.Types); model.SelectedProvider = model.Providers.Single(provider => provider.Id == topology);
                var window = new NewInstanceWindow(model, [new RedisConfigurationContentProvider()]);
                try
                {
                    WindowPreviewCapture.Show(window); model.NextCommand.Execute(null); model.Name = "Production Cache";
                    foreach (var input in model.Fields)
                    {
                        if (input.Key == "host") input.Value = "localhost";
                        if (input.Key == "nodes") input.Value = "localhost:6379";
                        if (input.Key == "service") input.Value = "mymaster";
                    }
                    await WindowPreviewCapture.VerifyAsync(window, $"redis-wizard-{topology}-{language}-{theme}");
                    var editor = Assert.Single(WindowTestVisuals.Descendants<RedisConnectionEditor>(window));
                    var adapter = Assert.IsType<RedisConnectionEditorViewModel>(editor.DataContext); adapter.Pane = "security";
                    model.Fields.Single(input => input.Key == "auth").Value = "acl";
                    model.Fields.Single(input => input.Key == "user").Value = "tester";
                    model.Fields.Single(input => input.Key == "acl.password").Value = "  test-only  ";
                    await WindowPreviewCapture.VerifyAsync(window, $"redis-wizard-security-{topology}-{language}-{theme}");
                    Assert.Contains(WindowTestVisuals.Descendants<SecretInput>(window), input => input.IsVisible);
                    model.NextCommand.Execute(null); Assert.Equal(3, model.CurrentStep); await model.TestConnectionCommand.ExecuteAsync();
                    Assert.True(model.HasSuccessfulConnectionTest); model.NextCommand.Execute(null); model.NextCommand.Execute(null); await model.SubmitCommand.ExecuteAsync();
                    Assert.Equal("  test-only  ", Assert.Single(configs.Drafts).Connection.Parameters["acl.password"]);
                    Assert.DoesNotContain("password", configs.Drafts[0].Connection.Parameters.Keys);
                }
                finally { window.Close(); }
            }
        }
        finally { themes.SetTheme(UiTheme.Light); CultureInfo.CurrentUICulture = previous; CultureInfo.DefaultThreadCurrentUICulture = previousDefault; }
        Assert.True(trace.Messages.Count == 0, string.Join(Environment.NewLine, trace.Messages.Distinct()));
    });
}
