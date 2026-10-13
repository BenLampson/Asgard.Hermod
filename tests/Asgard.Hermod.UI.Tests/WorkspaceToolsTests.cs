using System.Globalization;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.UI.Common;
using Asgard.Hermod.UI.Models;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证公共工具面板可扩展、可筛选、支持真实命令与 Escape，模块不复制浮层。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class WorkspaceToolsTests(WpfTestFixture fixture)
{
    /// <summary>双语与主题使用同一控件，后续添加工具无需修改模板，命令只执行一次。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task DynamicToolsFilterAndExecuteFromSharedPopup(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture; var previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        Text.Initialize(language, CultureInfo.InvariantCulture);
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        int calls = 0; var target = new TextBox();
        var items = new System.Collections.ObjectModel.ObservableCollection<WorkspaceTool>
        {
            new("first", Text.Get("SshSearchOutput"), "search", new RelayCommand(_ => calls++), Text.Get("WorkspaceTerminalTools")),
        };
        var tools = new WorkspaceTools { Items = items, FocusTarget = target };
        var host = new StackPanel { Children = { tools, target } };
        var window = new HermodWindow { Content = host, Width = 600, Height = 500 };
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            tools.IsOpen = true; await WindowTestVisuals.UpdateLayoutAsync(window);
            var filter = Assert.IsType<TextBox>(tools.FindName("ToolFilter"));
            Assert.Equal(Text.Get("WorkspaceFindTool"), System.Windows.Automation.AutomationProperties.GetName(filter));
            items.Add(new("extension", Text.Get("SshClearDisplay"), "eraser", new RelayCommand(_ => calls++), Text.Get("WorkspaceDisplayTools")));
            Assert.Equal(2, tools.FilteredTools.Count);
            filter.Text = Text.Get("SshClearDisplay"); Assert.Equal("extension", Assert.Single(tools.FilteredTools).Id);
            filter.Text = "no-tool-matches-this"; Assert.Empty(tools.FilteredTools);
            Assert.Equal(Visibility.Visible, Assert.IsType<TextBlock>(tools.FindName("EmptyTools")).Visibility);
            filter.Text = Text.Get("SshClearDisplay"); await WindowTestVisuals.UpdateLayoutAsync(window);
            var list = Assert.IsType<ItemsControl>(tools.FindName("ToolList"));
            var action = Assert.Single(WindowTestVisuals.Descendants<Button>(list));
            ((IInvokeProvider)new ButtonAutomationPeer(action).GetPattern(PatternInterface.Invoke)).Invoke();
            await WpfTestFixture.DrainDispatcherAsync(); Assert.Equal(1, calls); Assert.False(tools.IsOpen);
            tools.IsOpen = true; await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(2, tools.FilteredTools.Count);
            var source = PresentationSource.FromVisual(filter)!;
            filter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            await WpfTestFixture.DrainDispatcherAsync(); Assert.False(tools.IsOpen); Assert.Equal(1, calls);
            host.Children.Remove(tools); await WindowTestVisuals.UpdateLayoutAsync(window);
            items.Add(new("after-unload", "Fixture extension", "settings-2", new RelayCommand(_ => calls++)));
            Assert.Equal(2, tools.FilteredTools.Count);
            host.Children.Add(tools); await WindowTestVisuals.UpdateLayoutAsync(window); Assert.Equal(3, tools.FilteredTools.Count);
        }
        finally { tools.Close(false); window.Close(); themes.SetTheme(UiTheme.Light); CultureInfo.CurrentUICulture = previous; CultureInfo.DefaultThreadCurrentUICulture = previousDefault; }
    });
}
