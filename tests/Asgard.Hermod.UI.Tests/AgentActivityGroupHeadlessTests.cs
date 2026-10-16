using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.UI.Tests;

/// <summary>摘要只改变展示；普通证据保持原对象，批准与错误不能被折叠或合并。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AgentActivityGroupHeadlessTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Dark, 1120, 1000, 1)]
    [InlineData("zh-Hans", UiTheme.Light, 1120, 1000, 1)]
    [InlineData("en", UiTheme.Light, 3840, 2160, 1)]
    [InlineData("en", UiTheme.Light, 1920, 1080, 2)]
    [InlineData("en", UiTheme.Dark, 3840, 2160, 1)]
    [InlineData("en", UiTheme.Dark, 1920, 1080, 2)]
    [InlineData("zh-Hans", UiTheme.Light, 3840, 2160, 1)]
    [InlineData("zh-Hans", UiTheme.Light, 1920, 1080, 2)]
    [InlineData("zh-Hans", UiTheme.Dark, 3840, 2160, 1)]
    [InlineData("zh-Hans", UiTheme.Dark, 1920, 1080, 2)]
    public Task ConsecutiveToolsUseOneSummaryAndPreserveEvidenceAndArchiveState(string language, UiTheme theme, double width, double height, double dpi) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); new ThemeService(Application.Current.Resources).SetTheme(theme);
        try
        {
            var model = new AgentPanelTestModel(); var first = new Activity("SELECT first_evidence;"); var second = new Activity("SELECT second_evidence;");
            first.StatusKey("SqlAgentQueryRunning"); first.StatusKey("SqlAgentQueryCompleted"); second.StatusKey("SqlAgentQueryRunning");
            var user = new AgentDisplayMessage("Question", true); model.Messages.Add(user); model.Messages.Add(first); model.Messages.Add(second);
            var panel = new AgentPanel { Messages = model.Messages, MessageTemplate = Template(), Prompt = "draft remains" };
            var root = new Border { Child = panel }; root.Resources.MergedDictionaries.Add(Application.Current.Resources); using var bindings = new WpfBindingTrace(); await Layout();
            Assert.Equal(3, model.Messages.Count); Assert.Equal(2, panel.PresentationMessages.Count);
            var userItem = panel.PresentationMessages[0]; var groupItem = panel.PresentationMessages[1]; var group = Assert.IsType<AgentActivityGroup>(groupItem.Message);
            Assert.Equal(new[] { first, second }, group.Messages); Assert.False(group.IsExpanded); Assert.Equal("running", group.Tone); Assert.Empty(group.Elapsed);
            Assert.Equal(Text.Format("ChatGroupedQueriesRunning", 2), group.Title);
            var summary = Assert.Single(WindowTestVisuals.Descendants<AgentMessageView>(panel), view => ReferenceEquals(view.DataContext, group));
            var toggle = Assert.Single(WindowTestVisuals.Descendants<ToggleButton>(summary), IsShown); Assert.Equal(32, toggle.ActualHeight); Capture("collapsed");
            toggle.IsChecked = true; await Layout(); Assert.True(group.IsExpanded); Assert.True(first.IsActivityGroupExpanded); Assert.False(first.IsExpanded);
            first.IsExpanded = true; second.StatusKey("SqlAgentQueryCompleted"); Assert.Equal("success", group.Tone); Assert.NotEmpty(group.Elapsed);
            var third = new Activity("SELECT third_evidence;"); third.StatusKey("SqlAgentQueryStopped"); model.Messages.Add(third); await Layout();
            Assert.Same(userItem, panel.PresentationMessages[0]); Assert.Same(groupItem, panel.PresentationMessages[1]); Assert.True(group.IsExpanded); Assert.Equal("warning", group.Tone);
            Assert.Equal(3, group.Messages.Count); Assert.Equal("SELECT first_evidence;", first.Content); Capture("expanded");
            second.StatusKey("ChatAwaitingApproval"); Assert.Equal(4, panel.PresentationMessages.Count); Assert.All(panel.PresentationMessages, item => Assert.False(item.IsGroup));
            second.StatusKey("SqlAgentQueryCompleted"); Assert.Equal(2, panel.PresentationMessages.Count); Assert.True(((AgentActivityGroup)panel.PresentationMessages[1].Message).IsExpanded);
            var old = model.Archive.ActiveThread; model.Archive.NewCommand.Execute(null); Assert.Empty(panel.PresentationMessages);
            model.Archive.ActiveThread = old; await Layout(); var restored = Assert.IsType<AgentActivityGroup>(panel.PresentationMessages[1].Message);
            Assert.True(restored.IsExpanded); Assert.Same(first, restored.Messages[0]); Assert.True(first.IsExpanded); Assert.Equal("draft remains", panel.Prompt);
            panel.IsSourceClosed = true; await Layout(); Assert.True(restored.IsExpanded); Assert.Equal(0, model.SendCount); Assert.True(bindings.Messages.Count == 0, string.Join(Environment.NewLine, bindings.Messages));
            Assert.Null(Window.GetWindow(root)); Assert.Null(PresentationSource.FromVisual(root));
            async Task Layout() { for (int pass = 0; pass < 2; pass++) { root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); VisualTreeHelper.SetRootDpi(root, new(1, 1)); VisualTreeHelper.SetRootDpi(root, new(dpi, dpi)); await WpfTestFixture.DrainDispatcherAsync(); } }
            void Capture(string state)
            {
                string? directory = Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT"); if (directory is null) return; Directory.CreateDirectory(directory);
                var image = new RenderTargetBitmap((int)(width * dpi), (int)(height * dpi), dpi * 96, dpi * 96, PixelFormats.Pbgra32); image.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var output = File.Create(Path.Combine(directory, $"agent-tool-group-{state}-{language}-{theme}-{width}-{dpi * 100}pct.png")); encoder.Save(output);
            }
        }
        finally { Text.SetApplicationCulture(null); new ThemeService(Application.Current.Resources).SetTheme(UiTheme.Light); }
    });

    [Fact]
    public Task ApprovalAndErrorAreAlwaysIndependentEvenAfterReview() => fixture.RunOffscreenAsync(async () =>
    {
        var approval = new AgentApprovalRequest(Text.Get("ChatReviewedOperation"), "fixed source", "UPDATE exact_operation;", CancellationToken.None);
        var before = new Activity("before"); var after = new Activity("after"); var approved = new AgentDisplayMessage("", false, isTool: true, approval: approval);
        var error = new AgentDisplayMessage("safe error", false, isTool: true, isError: true);
        var messages = new ObservableCollection<object> { before, approved, after, error, new Activity("last") };
        var panel = new AgentPanel { Messages = messages, MessageTemplate = Template() }; panel.Resources.MergedDictionaries.Add(Application.Current.Resources);
        panel.Measure(new(960, 1000)); panel.Arrange(new(0, 0, 960, 1000)); panel.UpdateLayout(); await WpfTestFixture.DrainDispatcherAsync();
        Assert.Equal(5, panel.PresentationMessages.Count); Assert.All(panel.PresentationMessages, item => Assert.False(item.IsGroup));
        var card = Assert.Single(WindowTestVisuals.Descendants<AgentApprovalCard>(panel)); Assert.Same(approval, card.DataContext); Assert.True(card.ActualHeight > 100);
        approval.RejectCommand.Execute(null); messages.Add(new Activity("new last")); await WpfTestFixture.DrainDispatcherAsync();
        Assert.False(approval.IsPending); Assert.False(panel.PresentationMessages[1].IsGroup); Assert.Same(approved, panel.PresentationMessages[1].Message);
        Assert.False(panel.PresentationMessages[3].IsGroup); Assert.Same(error, panel.PresentationMessages[3].Message); Assert.True(panel.PresentationMessages[^1].IsGroup);
        Assert.Null(Window.GetWindow(panel));
    });
    private static DataTemplate Template()
    {
        var item = new FrameworkElementFactory(typeof(AgentMessageView));
        foreach (string name in new[] { "IsTool", "IsUser", "IsError", "Approval", "IsAwaitingApproval", "IsExpanded", "ActivityTitle", "ActivityTone", "ActivityIcon", "Status", "Elapsed" })
            item.SetBinding((DependencyProperty)typeof(AgentMessageView).GetField(name + "Property")!.GetValue(null)!, new Binding(name) { Mode = name == "IsExpanded" ? BindingMode.TwoWay : BindingMode.OneWay });
        item.SetBinding(AgentMessageView.BodyProperty, new Binding(nameof(AgentDisplayMessage.Content))); return new() { VisualTree = item };
    }
    private static bool IsShown(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current)) if (current is UIElement { Visibility: not Visibility.Visible }) return false;
        return element.ActualWidth > 0 && element.ActualHeight > 0;
    }
    private sealed class Activity(string body) : AgentDisplayMessage(body, false, isTool: true)
    {
        internal void StatusKey(string key) => UpdateMessage(Content, key);
    }
}
