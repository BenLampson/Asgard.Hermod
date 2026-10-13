namespace Asgard.Hermod.UI.Tests;

/// <summary>公共内容控件验证真实模板、只读绑定和文本替换后的滚动边界。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class WorkspaceEditorControlTests(WpfTestFixture fixture)
{
    [Fact]
    public Task CodeEditor_ReplacesScrolledTextWithoutInvalidLineIndices() => fixture.RunAsync(async () =>
    {
        using var trace = new WpfBindingTrace();
        var editor = new CodeEditor { Text = string.Join('\n', Enumerable.Range(0, 200)), IsReadOnly = false };
        var window = new HermodWindow { Width = 700, Height = 460, Content = editor };
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            var input = Assert.IsType<TextBox>(editor.FindName("Input"));
            input.ScrollToVerticalOffset(400); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(input.VerticalOffset > 0);
            editor.Text = "short"; await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal("short", input.Text);
            Assert.StartsWith("1", Assert.IsType<TextBlock>(editor.FindName("LineNumbers")).Text);
            editor.IsReadOnly = true; await WindowTestVisuals.UpdateLayoutAsync(window); Assert.True(input.IsReadOnly);
            Assert.True(trace.Messages.Count == 0, string.Join(Environment.NewLine, trace.Messages.Distinct()));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ContentTabs_KeepStableSelectionAndFullAccessibleNames() => fixture.RunAsync(async () =>
    {
        using var trace = new WpfBindingTrace();
        var choices = new[] { new InstanceFieldChoice("one", "long-key-name:first"), new InstanceFieldChoice("two", "long-key-name:second") };
        var strip = new WorkspaceTabStrip { ItemsSource = choices, DisplayMemberPath = "Name", ToolTipMemberPath = "Name", SelectedItem = choices[0] };
        var section = new SectionSelector { ItemsSource = choices, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = "two" };
        var content = new StackPanel(); content.Children.Add(strip); content.Children.Add(section);
        var window = new HermodWindow { Width = 720, Height = 260, Content = content };
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            var item = Assert.IsType<ListBoxItem>(strip.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.Equal(choices[0].Name, item.ToolTip); Assert.Equal(choices[0].Name, System.Windows.Automation.AutomationProperties.GetName(item));
            Assert.Equal("two", section.SelectedValue); Assert.Equal(33, section.ActualHeight);
            Assert.True(trace.Messages.Count == 0, string.Join(Environment.NewLine, trace.Messages.Distinct()));
        }
        finally { window.Close(); }
    });
}
