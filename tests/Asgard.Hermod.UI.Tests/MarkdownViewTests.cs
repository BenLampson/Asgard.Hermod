using System.Globalization;
using System.Windows.Documents;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.MySql.UI.ViewModels;
using Asgard.Hermod.PostgreSql.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证真实文档结构、原文边界、主题切换和 SSH 消息模板，不依赖机器显示语言。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MarkdownViewTests(WpfTestFixture fixture)
{
    private const string Sample = "## Resource overview\n\n**Healthy** and *stable*, with `uptime`.\n\n"
        + "| Item | Value | Assessment |\n| :--- | ---: | :---: |\n| CPU | 0.15 | Low load |\n| Memory | 2.5 GB | Available |\n\n"
        + "- First check\n- Second check\n\n> Verify the result.\n\n```text\nraw | output\n# literal heading\n```";

    /// <summary>标题、强调、表格对齐、列表和代码均为文档元素，复制的正文不包含 Markdown 标记。</summary>
    [Fact]
    public Task MarkdownProducesSelectableStructuredDocument() => fixture.RunOffscreenAsync(() =>
    {
        var view = new MarkdownView { Source = Sample };
        var heading = Assert.IsType<Paragraph>(view.Document.Blocks.FirstBlock);
        Assert.Equal("Resource overview", new TextRange(heading.ContentStart, heading.ContentEnd).Text.Trim());
        Assert.True(heading.FontSize > view.FontSize);
        Assert.Equal(FontWeights.SemiBold, heading.FontWeight);
        var paragraph = view.Document.Blocks.OfType<Paragraph>().ElementAt(1);
        Assert.Contains(paragraph.Inlines.OfType<Span>(), span => span.FontWeight == FontWeights.Bold);
        Assert.Contains(paragraph.Inlines.OfType<Span>(), span => span.FontStyle == FontStyles.Italic);
        var table = Assert.Single(view.Document.Blocks.OfType<Table>());
        Assert.Equal(3, table.Columns.Count);
        var rows = Assert.Single(table.RowGroups.Cast<TableRowGroup>()).Rows;
        Assert.Equal(3, rows.Count);
        Assert.Equal(TextAlignment.Right, rows[1].Cells[1].TextAlignment);
        Assert.Equal(TextAlignment.Center, rows[1].Cells[2].TextAlignment);
        Assert.Equal(FontWeights.SemiBold, Assert.IsType<Paragraph>(rows[0].Cells[0].Blocks.FirstBlock).FontWeight);
        Assert.Equal(2, Assert.Single(view.Document.Blocks.OfType<System.Windows.Documents.List>()).ListItems.Count);
        Assert.Single(view.Document.Blocks.OfType<Section>());
        string copied = new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text;
        Assert.Contains("raw | output", copied);
        Assert.Contains("# literal heading", copied);
        Assert.DoesNotContain("## Resource overview", copied);
        Assert.DoesNotContain("| :---", copied);
        Assert.True(view.IsReadOnly);
        Assert.True(view.Focusable);
        Assert.False(view.IsUndoEnabled);
        return Task.CompletedTask;
    });

    /// <summary>模型中的 HTML、图片和链接只显示文本，更新或关闭 Markdown 不会沿用旧文档。</summary>
    [Fact]
    public Task UntrustedMarkupStaysInertAndLiteralModePreservesSource() => fixture.RunOffscreenAsync(() =>
    {
        const string source = "# Literal\n\n<script>alert('test')</script>\n\n[Open](file:///C:/secret) ![alt text](https://invalid.example/image.png)\n\n```xml\n<Button Command=\"{Binding DeleteCommand}\" />\n```";
        var view = new MarkdownView { Source = source };
        string rendered = new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text;
        Assert.Contains("<script>", rendered);
        Assert.Contains("alt text", rendered);
        Assert.Contains("<Button", rendered);
        Assert.DoesNotContain(DocumentElements(view.Document), element => element is Hyperlink or InlineUIContainer or BlockUIContainer);
        var previous = view.Document;
        view.IsMarkdownEnabled = false;
        Assert.NotSame(previous, view.Document);
        Assert.Equal(source + "\r\n", new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text);
        view.Source = "new body";
        Assert.Equal("new body\r\n", new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text);
        Assert.True(new MySqlAssistantMessage(source, false).IsAssistant);
        Assert.False(new MySqlAssistantMessage(source, true).IsAssistant);
        Assert.True(new PostgreSqlAssistantMessage(source, false).IsAssistant);
        Assert.False(new PostgreSqlAssistantMessage(source, true).IsAssistant);
        return Task.CompletedTask;
    });

    /// <summary>实际 SSH 模板只格式化助手消息，原始工具结果和用户内容保持字面语义。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task AgentMarkdownFitsNarrowPaneAndUpdatesWithTheme(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        var old = CultureInfo.CurrentUICulture;
        var oldDefault = CultureInfo.DefaultThreadCurrentUICulture;
        Text.Initialize(language, CultureInfo.InvariantCulture);
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        using var model = new SshAssistantViewModel(null, () => null, () => "host", new SshTroubleshootingWorkspaceTests.Dialogs());
        var user = new SshAssistantMessage("## User text\n| untouched | output |", true, 0);
        var tool = new SshAssistantMessage("# raw tool output\n| a | b |", false, 0, false, "SshAgentDiagnosticStep", true);
        var assistant = new SshAssistantMessage(Sample + "\n\n```bash\nuname -a\n```", false, 0);
        model.Messages.Add(user);
        model.Messages.Add(tool);
        model.Messages.Add(assistant);
        var view = new SshAssistantView { DataContext = model };
        var window = new HermodWindow { Content = view, Width = 440, Height = 900 };
        try
        {
            WindowPreviewCapture.Show(window);
            await WindowPreviewCapture.VerifyAsync(window, $"markdown-wide-{language}-{theme}");
            var messages = WindowTestVisuals.Descendants<MarkdownView>(view).ToArray();
            Assert.Equal(3, messages.Length);
            foreach (var message in messages)
            {
                Assert.Equal(ReferenceEquals(message.DataContext, assistant), message.IsMarkdownEnabled);
                Assert.Null(message.Template.FindName("FocusRing", message));
                Assert.True(message.ActualHeight > 0);
            }
            var rendered = Assert.Single(messages, message => message.IsMarkdownEnabled);
            Assert.Single(rendered.Document.Blocks.OfType<Table>());
            Assert.Equal("uname -a", Assert.Single(assistant.Commands).Command);
            Assert.DoesNotContain("uname -a", new TextRange(rendered.Document.ContentStart, rendered.Document.ContentEnd).Text);
            Assert.Contains(WindowTestVisuals.Descendants<TextBox>(view), editor => editor.IsReadOnly && editor.Text == "uname -a");
            window.Width = 320;
            await WindowPreviewCapture.VerifyAsync(window, $"markdown-narrow-{language}-{theme}");
            Assert.True(rendered.ActualWidth <= 285);
            Assert.True(rendered.ExtentWidth <= rendered.ViewportWidth + 1);
            Assert.True(rendered.ExtentHeight <= rendered.ViewportHeight + 1);
            themes.SetTheme(theme == UiTheme.Light ? UiTheme.Dark : UiTheme.Light);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(((SolidColorBrush)Application.Current.FindResource("TextBodyBrush")).Color, Assert.IsType<SolidColorBrush>(rendered.Document.Foreground).Color);
            var cell = Assert.Single(rendered.Document.Blocks.OfType<Table>()).RowGroups[0].Rows[0].Cells[0];
            Assert.Equal(((SolidColorBrush)Application.Current.FindResource("LineBrush")).Color, Assert.IsType<SolidColorBrush>(cell.BorderBrush).Color);
            window.Activate();
            rendered.Focus();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            rendered.SelectAll();
            Assert.Contains("Resource overview", rendered.Selection.Text);
            Assert.True(rendered.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next)));
        }
        finally
        {
            window.Close();
            themes.SetTheme(UiTheme.Light);
            CultureInfo.CurrentUICulture = old;
            CultureInfo.DefaultThreadCurrentUICulture = oldDefault;
        }
    });

    /// <summary>递归读取文档逻辑树，确认渲染没有生成活动链接或嵌入式 UI。</summary>
    private static IEnumerable<DependencyObject> DocumentElements(DependencyObject root)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject element)
            {
                yield return element;
                foreach (var nested in DocumentElements(element)) yield return nested;
            }
    }
}
