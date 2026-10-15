using System.Globalization;
using System.Windows.Documents;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.MySql.UI.ViewModels;
using Asgard.Hermod.MySql.UI.Views;
using Asgard.Hermod.PostgreSql.UI.ViewModels;
using Asgard.Hermod.PostgreSql.UI.Views;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>使用三种真实模块视图验证共享外观、双向草稿与原始业务命令，测试不连接服务。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AgentPanelTests(WpfTestFixture fixture)
{
    /// <summary>覆盖中英文、明暗主题与宽窄布局，各模块均由同一面板和消息控件呈现。</summary>
    [Theory]
    [InlineData("ssh", "en", UiTheme.Light)]
    [InlineData("ssh", "en", UiTheme.Dark)]
    [InlineData("ssh", "zh-Hans", UiTheme.Light)]
    [InlineData("ssh", "zh-Hans", UiTheme.Dark)]
    [InlineData("mysql", "en", UiTheme.Light)]
    [InlineData("mysql", "en", UiTheme.Dark)]
    [InlineData("mysql", "zh-Hans", UiTheme.Light)]
    [InlineData("mysql", "zh-Hans", UiTheme.Dark)]
    [InlineData("postgresql", "en", UiTheme.Light)]
    [InlineData("postgresql", "en", UiTheme.Dark)]
    [InlineData("postgresql", "zh-Hans", UiTheme.Light)]
    [InlineData("postgresql", "zh-Hans", UiTheme.Dark)]
    public Task ModulesSharePresentationAndKeepTheirOwnBindings(string kind, string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        var old = CultureInfo.CurrentUICulture;
        var oldDefault = CultureInfo.DefaultThreadCurrentUICulture;
        Text.Initialize(language, CultureInfo.InvariantCulture);
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        using var bindings = new WpfBindingTrace();
        try
        {
            var model = new AgentPanelTestModel();
            const string markdown = "## Overview\n\n| Item | Value |\n| --- | --- |\n| CPU | 0.15 |";
            UserControl view;
            if (kind == "ssh")
            {
                view = new SshAssistantView();
                model.Messages.Add(new SshAssistantMessage("## literal question", true, 0));
                model.Messages.Add(new SshAssistantMessage(markdown + "\n\n```bash\nuname -a\n```", false, 0));
            }
            else if (kind == "mysql")
            {
                view = new MySqlAssistantView();
                model.Messages.Add(new MySqlAssistantMessage("## literal question", true));
                model.Messages.Add(new MySqlAssistantMessage(markdown, false));
            }
            else
            {
                view = new PostgreSqlAssistantView();
                model.Messages.Add(new PostgreSqlAssistantMessage("## literal question", true));
                model.Messages.Add(new PostgreSqlAssistantMessage(markdown, false));
            }
            view.DataContext = model;
            System.Windows.Input.InputMethod.SetIsInputMethodEnabled(view, false);
            var panel = Assert.IsType<AgentPanel>(view.FindName("ChatPanel"));
            foreach (double width in new[] { 480d, 320d })
            {
                await LayoutAsync(view, width);
                var header = Assert.IsType<DockPanel>(panel.FindName("AgentHeader"));
                Assert.Equal(50, Assert.IsType<Border>(header.Parent).ActualHeight);
                var messages = WindowTestVisuals.Descendants<AgentMessageView>(view).ToArray();
                Assert.Equal(2, messages.Length);
                var user = Assert.Single(messages, message => message.IsUser);
                var reply = Assert.Single(messages, message => !message.IsUser);
                var bubble = Assert.IsType<Border>(user.FindName("MessageBubble"));
                Assert.Equal(((SolidColorBrush)Application.Current.FindResource("AccentWeakBrush")).Color, Assert.IsType<SolidColorBrush>(bubble.Background).Color);
                var literal = Assert.Single(WindowTestVisuals.Descendants<MarkdownView>(user));
                Assert.False(literal.IsMarkdownEnabled);
                Assert.Contains("## literal question", new TextRange(literal.Document.ContentStart, literal.Document.ContentEnd).Text);
                Assert.Single(Assert.Single(WindowTestVisuals.Descendants<MarkdownView>(reply)).Document.Blocks.OfType<Table>());
                var composer = Assert.IsType<Border>(panel.FindName("Composer"));
                var send = Assert.IsType<Button>(panel.FindName("SendAction"));
                Assert.True(send.TranslatePoint(new Point(send.ActualWidth, send.ActualHeight), composer).X <= composer.ActualWidth);
                Assert.False(send.IsEnabled);
                Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(header), text => text.Text == Text.Get("AgentPanelTitle"));
            }
            var prompt = Assert.IsType<TextBox>(panel.FindName("PromptEditor"));
            prompt.Text = "new draft";
            prompt.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            await LayoutAsync(view, 320);
            Assert.Equal("new draft", model.Prompt);
            Assert.Equal(kind == "ssh" ? 16000 : 8000, prompt.MaxLength);
            Assert.Null(prompt.Template.FindName("FocusRing", prompt));
            WindowTestVisuals.Invoke(Assert.IsType<Button>(panel.FindName("SendAction")));
            Assert.Equal(1, model.SendCount);
            Assert.Equal("new draft", model.Prompt);
            model.IsBusy = true;
            await LayoutAsync(view, 320);
            Assert.False(Assert.IsType<Button>(panel.FindName("SendAction")).IsEnabled);
            WindowTestVisuals.Invoke(Assert.Single(WindowTestVisuals.Descendants<Button>(panel), button => ReferenceEquals(button.Command, model.CancelCommand)));
            Assert.Equal(1, model.CancelCount);
            model.IsBusy = false;
            await LayoutAsync(view, 320);
            WindowTestVisuals.Invoke(Assert.Single(WindowTestVisuals.Descendants<Button>(panel), button => ReferenceEquals(button.Command, model.ClearCommand)));
            Assert.Equal(1, model.ClearCount);
            if (kind != "ssh")
            {
                WindowTestVisuals.Invoke(Assert.IsType<Button>(panel.FindName("HideAgentButton")));
                Assert.Equal(1, model.HideCount);
                Assert.Contains(WindowTestVisuals.Descendants<CheckBox>(panel), check => !check.IsEnabled);
            }
            else
            {
                var suggestion = Assert.Single(WindowTestVisuals.Descendants<AgentCodeSuggestion>(panel));
                WindowTestVisuals.Invoke(Assert.Single(WindowTestVisuals.Descendants<Button>(suggestion), button => ReferenceEquals(button.Command, model.InsertCommand)));
                Assert.Same(suggestion.CommandParameter, model.Inserted);
                WindowTestVisuals.Invoke(Assert.Single(WindowTestVisuals.Descendants<Button>(suggestion), button => ReferenceEquals(button.Command, model.ExecuteCommand)));
                Assert.Same(suggestion.CommandParameter, model.Executed);
            }
            Assert.Empty(bindings.Messages);
            Assert.Null(PresentationSource.FromVisual(view));
        }
        finally
        {
            themes.SetTheme(UiTheme.Light);
            CultureInfo.CurrentUICulture = old;
            CultureInfo.DefaultThreadCurrentUICulture = oldDefault;
        }
    });

    /// <summary>消息、模板及绑定变更排入同一 Dispatcher 后，重新测量离屏排版。</summary>
    private static async Task LayoutAsync(FrameworkElement view, double width)
    {
        await WpfTestFixture.DrainDispatcherAsync();
        view.Measure(new Size(width, 760));
        view.Arrange(new Rect(0, 0, width, 760));
        view.UpdateLayout();
        await WpfTestFixture.DrainDispatcherAsync();
    }
}
