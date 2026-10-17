using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Agent;
using Asgard.Hermod.PostgreSql;

namespace Asgard.Hermod.PostgreSql.Tests;

/// <summary>真实模块视图在原 SQL 卡片完成确认、运行、结果与重跑；无外部连接。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class PostgreSqlSuggestionCardTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Light, 1120)]
    [InlineData("zh-Hans", UiTheme.Dark, 640)]
    public Task ApprovalAndResultStayInOriginalCard(string language, UiTheme theme, double width) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); new ThemeService(Application.Current.Resources).SetTheme(theme);
        try
        {
            var server = new TestPostgreSql();
            await using var tab = new PostgreSqlTabViewModel("Session 1", "app", PostgreSqlObjectKind.Root, server, TestPostgreSql.Connection, new TestPostgreSqlDialogs());
            using var model = new PostgreSqlAssistantViewModel(tab, new Client(), useInlineApproval: true) { Prompt = "Review query" };
            await model.SendCommand.ExecuteAsync(); var suggestion = Assert.Single(model.Messages[^1].SqlBlocks);
            // 在建议后留足历史，模拟用户滚回旧建议进行操作。
            for (int i = 0; i < 12; i++) model.Messages.Add(new PostgreSqlAssistantMessage("Later response " + i, false));
            int count = model.Messages.Count;
            using var bindings = new WpfBindingTrace(); var view = new PostgreSqlAssistantView { DataContext = model };
            var root = new Border { Child = view, Background = (Brush)Application.Current.FindResource("SurfaceBrush") };
            await Layout(); var panel = (AgentPanel)view.FindName("ChatPanel");
            var scroll = (ScrollViewer)panel.FindName("ConversationScroll");
            var card = Assert.Single(WindowTestVisuals.Descendants<AgentCodeSuggestion>(view));
            scroll.ScrollToVerticalOffset(80); await Layout(); double offset = scroll.VerticalOffset;
            double top = card.TranslatePoint(new Point(), scroll).Y;
            var running = model.ExecuteCommand.ExecuteAsync(suggestion); await Layout();
            Assert.Equal(count, model.Messages.Count); Assert.Same(card, Assert.Single(WindowTestVisuals.Descendants<AgentCodeSuggestion>(view)));
            Assert.Equal(offset, scroll.VerticalOffset, 1); Assert.Equal(top, card.TranslatePoint(new Point(), scroll).Y, 1);
            var approval = (AgentApprovalCard)card.FindName("InlineApproval");
            Assert.Equal(Visibility.Visible, approval.Visibility); Assert.Same(model.PendingApproval, approval.DataContext);
            Assert.Equal(Visibility.Collapsed, ((TextBox)approval.FindName("OperationCode")).Visibility);
            Assert.Empty(server.ExecutedSql); Capture("approval");
            // 通过实际按钮进入执行，完成后仍使用同一卡片和结果快照。
            WindowTestVisuals.Invoke(Assert.Single(WindowTestVisuals.Descendants<Button>(approval), item => ReferenceEquals(item.Command, model.PendingApproval!.ApproveCommand)));
            await running; await Layout();
            Assert.Equal(count, model.Messages.Count); Assert.Single(server.ExecutedSql);
            Assert.Equal(offset, scroll.VerticalOffset, 1); Assert.Equal(top, card.TranslatePoint(new Point(), scroll).Y, 1);
            Assert.Same(card, Assert.Single(WindowTestVisuals.Descendants<AgentCodeSuggestion>(view)));
            Assert.Equal(Visibility.Collapsed, approval.Visibility);
            Assert.Equal(Text.Get("ChatSqlExecuted"), ((TextBlock)card.FindName("ExecutionStatus")).Text);
            var execute = (Button)card.FindName("ExecuteAction"); Assert.Equal(Text.Get("ChatSqlRunAgain"), execute.Content); Assert.True(execute.IsEnabled);
            Assert.Contains("Alice", ((TextBox)card.FindName("ResultPreview")).Text); Capture("result");
            ((System.Windows.Controls.Primitives.ToggleButton)card.FindName("SuggestionToggle")).IsChecked = false; await Layout();
            Assert.False(suggestion.Execution.IsExpanded);
            var thread = model.Archive.ActiveThread; model.Archive.NewCommand.Execute(null); model.Archive.ActiveThread = thread; await Layout();
            card = Assert.Single(WindowTestVisuals.Descendants<AgentCodeSuggestion>(view)); Assert.False(card.IsExpanded);
            Assert.Contains("Alice", ((TextBox)card.FindName("ResultPreview")).Text);
            var rerun = model.ExecuteCommand.ExecuteAsync(suggestion); await Layout();
            approval = (AgentApprovalCard)card.FindName("InlineApproval");
            Assert.True(card.IsExpanded); Assert.Single(server.ExecutedSql); Assert.Equal(Visibility.Visible, approval.Visibility);
            model.PendingApproval!.RejectCommand.Execute(null); await rerun; await Layout();
            Assert.Single(server.ExecutedSql); Assert.Equal(count, model.Messages.Count); Assert.Empty(bindings.Messages);
            Assert.Null(Window.GetWindow(root)); Assert.Null(PresentationSource.FromVisual(root));
            async Task Layout()
            {
                for (int i = 0; i < 3; i++) { root.Measure(new(width, 1000)); root.Arrange(new(0, 0, width, 1000)); root.UpdateLayout(); await WpfTestFixture.DrainDispatcherAsync(); }
            }
            void Capture(string stage)
            {
                string? directory = Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT"); if (directory is null) return;
                Directory.CreateDirectory(directory); var bitmap = new RenderTargetBitmap((int)width, 1000, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(directory, $"PostgreSql-suggestion-{stage}-{language}.png")); encoder.Save(stream);
            }
        }
        finally { Text.SetApplicationCulture(null); new ThemeService(Application.Current.Resources).SetTheme(UiTheme.Light); }
    });

    private sealed class Client : IAgentClient
    {
        public Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default)
            => Task.FromResult(AgentReply.FromContent("Review this query.\n```sql\nSELECT * FROM users;\n```"));
    }
}
