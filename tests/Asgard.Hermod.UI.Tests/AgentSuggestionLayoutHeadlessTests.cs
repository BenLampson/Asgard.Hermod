using System.Globalization;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.UI.Common;

namespace Asgard.Hermod.UI.Tests;

/// <summary>长建议完整参与聊天布局；收起保留原结果，横向溢出仍可阅读。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AgentSuggestionLayoutHeadlessTests(WpfTestFixture fixture)
{
    /// <summary>同时验证中英文本、英文回退、完整高度、横向阅读与底部收起后的原数据。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light, 592)]
    [InlineData("zh-Hans", UiTheme.Dark, 960)]
    [InlineData("fr-FR", UiTheme.Dark, 592)]
    public Task LongSuggestionCollapsesWithoutNestedVerticalScrolling(string language, UiTheme theme, double width) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); new ThemeService(Application.Current.Resources).SetTheme(theme);
        try
        {
            string sql = string.Join('\n', Enumerable.Range(1, 40).Select(index => $"SELECT {index}, '{new string('x', 200)}';"));
            string result = string.Join('\n', Enumerable.Range(1, 30).Select(index => $"Statement {index} completed."));
            string sample = JsonSerializer.Serialize(new { columns = new[] { "name", "rows" }, rows = Enumerable.Range(1, 20).Select(index => new[] { new string('y', 200), index.ToString(CultureInfo.InvariantCulture) }) });
            var execution = new AgentSqlSuggestionExecution(); execution.Begin(); execution.Dispatched(); execution.Complete(AgentSqlExecutionState.Succeeded, result, sample);
            int dispatches = 0;
            var card = new AgentCodeSuggestion
            {
                Caption = Text.Get("ChatToolSqlSource"),
                Code = sql,
                Execution = execution,
                InsertText = Text.Get("ChatInsertOnly"),
                ExecuteText = Text.Get("ChatSqlRunAgain"),
                InsertCommand = new RelayCommand(_ => dispatches++),
                ExecuteCommand = new RelayCommand(_ => dispatches++)
            };
            card.SetBinding(AgentCodeSuggestion.IsExpandedProperty, new Binding(nameof(execution.IsExpanded)) { Source = execution, Mode = BindingMode.TwoWay });
            var messages = new StackPanel(); messages.Children.Add(card);
            var scroll = new ScrollViewer { Content = messages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var root = new Border { Child = scroll }; root.Resources.MergedDictionaries.Add(Application.Current.Resources);
            root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Asgard.Hermod.UI;component/Resources/AgentDesign.xaml") });
            using var trace = new WpfBindingTrace(); await Layout();
            var code = (TextBox)card.FindName("SuggestionCode"); var output = (TextBox)card.FindName("ResultPreview");
            var toggle = (ToggleButton)card.FindName("SuggestionToggle"); var body = (StackPanel)card.FindName("SuggestionBody");
            Assert.True(card.IsExpanded); Assert.Equal(Text.Get("ChatSuggestionCollapse"), AutomationProperties.GetName(toggle));
            Assert.True(code.ActualHeight > 180); Assert.True(output.ActualHeight > 200);
            Assert.True(scroll.ScrollableHeight > 0); Assert.True(code.ExtentWidth > code.ViewportWidth); Assert.True(output.ExtentWidth > output.ViewportWidth);
            foreach (var reader in new[] { code, output, (TextBox)card.FindName("ExecutionResult") })
            {
                Assert.True(reader.IsReadOnly); Assert.Equal(ScrollBarVisibility.Disabled, reader.VerticalScrollBarVisibility);
                Assert.InRange(reader.ExtentHeight - reader.ViewportHeight, -1, 1);
                Assert.False(reader.GetRectFromCharacterIndex(reader.Text.Length - 1).IsEmpty);
            }
            double expandedHeight = card.ActualHeight;
            // 鼠标位于代码正文时，纵向滚轮仍应滚动聊天，而不是被只读文本宿主截获。
            var codeScroll = (ScrollViewer)code.Template.FindName("PART_ContentHost", code);
            codeScroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.MouseWheelEvent });
            await Layout(); Assert.True(scroll.VerticalOffset > 0); scroll.ScrollToTop(); await Layout();
            Capture("expanded-top"); scroll.ScrollToEnd(); await Layout(); Capture("expanded-bottom");
            WindowTestVisuals.Invoke((Button)card.FindName("CollapseSuggestion")); await Layout();
            Assert.False(execution.IsExpanded); Assert.Equal(Visibility.Collapsed, body.Visibility); Assert.True(card.ActualHeight < expandedHeight / 4);
            Assert.Equal(Text.Get("ChatSuggestionExpand"), AutomationProperties.GetName(toggle));
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(card), text => text.Text == execution.Status && text.Visibility == Visibility.Visible);
            Capture("collapsed");
            toggle.IsChecked = true; await Layout();
            Assert.True(execution.IsExpanded); Assert.Equal(sql, code.Text); Assert.Equal(result, ((TextBox)card.FindName("ExecutionResult")).Text);
            Assert.Equal(execution.Output, output.Text); Assert.Equal(0, dispatches);
            toggle.IsChecked = false; execution.Begin(); await Layout();
            Assert.True(card.IsExpanded); Assert.False(toggle.IsEnabled); Assert.False(((Button)card.FindName("CollapseSuggestion")).IsEnabled);
            Assert.Equal(Visibility.Visible, body.Visibility); Assert.Equal(0, dispatches); Assert.Empty(trace.Messages);
            Assert.Null(Window.GetWindow(root));

            async Task Layout()
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    root.Measure(new(width, 700)); root.Arrange(new(0, 0, width, 700)); root.UpdateLayout();
                    await WpfTestFixture.DrainDispatcherAsync();
                }
            }

            void Capture(string state)
            {
                if (Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT") is not { } directory) return;
                Directory.CreateDirectory(directory);
                var bitmap = new RenderTargetBitmap((int)width, 700, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(directory, $"agent-suggestion-{state}-{language}.png")); encoder.Save(file);
            }
        }
        finally { Text.SetApplicationCulture(null); new ThemeService(Application.Current.Resources).SetTheme(UiTheme.Light); }
    });
}
