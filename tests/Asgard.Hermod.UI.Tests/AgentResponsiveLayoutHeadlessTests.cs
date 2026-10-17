using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>同一聊天视图缩放时保持两侧窄留白，来源、正文和输入框使用剩余宽度。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AgentResponsiveLayoutHeadlessTests(WpfTestFixture fixture)
{
    [Fact]
    public Task ConversationGrowsWithViewportInsteadOfStoppingAtFixedWidth() => fixture.RunOffscreenAsync(async () =>
    {
        using var model = new SshAssistantViewModel(null, () => null, () => "host", new SshTroubleshootingWorkspaceTests.Dialogs());
        model.Messages.Add(new("Explain the evidence", true, 0));
        model.Messages.Add(new("The conversation uses the available width.\n\n| Source | Result |\n| --- | --- |\n| Logs | Review required |", false, 0));
        var view = new SshAssistantView(); var root = new AgentSourceView(new(new object(), model, "SSH / Console", "host", "terminal", () => view, () => { }, () => false));
        root.Resources.MergedDictionaries.Add(Application.Current.Resources);
        using var bindings = new WpfBindingTrace();
        foreach (double width in new[] { 640d, 1120d, 1920d, 3840d, 1440d, 640d })
        {
            root.Measure(new(width, 1000)); root.Arrange(new(0, 0, width, 1000)); root.UpdateLayout();
            await WpfTestFixture.DrainDispatcherAsync(); root.UpdateLayout();
            double inset = width < 800 ? 24 : width * 0.1, expected = width - inset * 2;
            var panel = (AgentPanel)view.FindName("ChatPanel");
            FrameworkElement[] columns = [(Grid)root.FindName("SourceColumn"), (DockPanel)panel.FindName("ToolbarColumn"), (StackPanel)panel.FindName("MessageColumn"), (StackPanel)panel.FindName("ComposerColumn")];
            foreach (var column in columns)
            {
                Assert.Equal(expected, column.ActualWidth, 1); Assert.Equal(inset, column.TranslatePoint(new(), root).X, 1);
                Assert.True(double.IsPositiveInfinity(column.MaxWidth));
            }
            Assert.Equal(2, model.Messages.Count); Assert.Null(PresentationSource.FromVisual(root));
        }
        Assert.Empty(bindings.Messages);
    });
}
