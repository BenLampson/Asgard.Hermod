using System.Globalization;
using System.Windows.Controls.Primitives;
using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.UI.Tests;

/// <summary>完整状态不能挤占工具摘要；运行与完成更新同一条内存消息并保留展开状态。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AgentMessageActivityHeadlessTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    public Task CompletedActivityKeepsCompactHeaderAndSafeStatusOnDemand(string language) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            var message = new Activity(); var view = new AgentMessageView { IsTool = true, DataContext = message };
            view.Resources.MergedDictionaries.Add(Application.Current.Resources);
            foreach (var property in new[] { AgentMessageView.BodyProperty, AgentMessageView.StatusProperty, AgentMessageView.ActivityTitleProperty, AgentMessageView.ActivityIconProperty, AgentMessageView.ActivityToneProperty, AgentMessageView.ElapsedProperty, AgentMessageView.IsExpandedProperty })
                view.SetBinding(property, new Binding(property == AgentMessageView.BodyProperty ? nameof(AgentDisplayMessage.Content) : property.Name));
            message.SetStatus("SqlAgentQueryRunning"); await Layout();
            Assert.Equal("running", view.ActivityTone); Assert.Equal("loader-circle", view.ActivityIcon);
            var toggle = Assert.Single(WindowTestVisuals.Descendants<ToggleButton>(view)); Assert.Equal(32, toggle.ActualHeight);
            message.SetStatus("SqlAgentQueryCompleted"); await Layout(); Assert.False(message.IsExpanded);
            Assert.Equal("success", view.ActivityTone); Assert.Equal("circle-check", view.ActivityIcon); Assert.Equal(32, toggle.ActualHeight);
            Assert.Equal(message.Status, toggle.ToolTip); Assert.NotEmpty(message.Elapsed);
            Assert.DoesNotContain(WindowTestVisuals.Descendants<TextBlock>(toggle), text => text.Text == message.Status);
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(toggle), text => text.Text == Text.Get("ChatQueryCompleted"));
            Assert.Equal(Visibility.Collapsed, ((StackPanel)view.FindName("AnswerActions")).Visibility);
            toggle.IsChecked = true; await Layout(); Assert.True(message.IsExpanded); Assert.Contains(WindowTestVisuals.Descendants<MarkdownView>(view), text => text.Source == message.Content);
            Assert.Null(PresentationSource.FromVisual(view));
            async Task Layout()
            {
                view.Measure(new(592, 500)); view.Arrange(new(0, 0, 592, view.DesiredSize.Height)); view.UpdateLayout();
                await WpfTestFixture.DrainDispatcherAsync(); view.UpdateLayout();
            }
        }
        finally { Text.SetApplicationCulture(null); }
    });

    private sealed class Activity() : AgentDisplayMessage("SELECT original_evidence;", false, isTool: true)
    {
        internal void SetStatus(string key) => UpdateMessage(Content, key);
    }
}
