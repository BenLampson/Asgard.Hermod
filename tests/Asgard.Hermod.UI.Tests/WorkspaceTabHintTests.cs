using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Automation;
using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.UI.Tests;

/// <summary>页签焦点不会留下悬浮提示，完整名称仍提供给悬停和辅助技术。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class WorkspaceTabHintTests(WpfTestFixture fixture)
{
    /// <summary>复现点击页签后保留焦点、切换和关闭的路径，不移动桌面鼠标。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task FocusAndRemoval_DoNotLeaveAnOpenTitleHint(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var tabs = new ObservableCollection<Tab> { new("identity_provider_config", "dev_idp.identity_provider_config"), new("users", "app.users") };
        var strip = new WorkspaceTabStrip { ItemsSource = tabs, DisplayMemberPath = nameof(Tab.Name), ToolTipMemberPath = nameof(Tab.Header) };
        var input = new TextBox(); var panel = new StackPanel(); panel.Children.Add(strip); panel.Children.Add(input);
        var window = new HermodWindow { Width = 540, Height = 240, Content = panel };
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            var first = Assert.IsType<WorkspaceTabStripItem>(strip.ItemContainerGenerator.ContainerFromItem(tabs[0]));
            Assert.Equal(tabs[0].Header, first.ToolTip); Assert.Equal(tabs[0].Header, AutomationProperties.GetName(first));
            Assert.False(ToolTipService.GetShowsToolTipOnKeyboardFocus(first));
            Assert.InRange(ToolTipService.GetShowDuration(first), 1000, 5000);
            first.IsSelected = true; Assert.True(first.Focus()); await WindowTestVisuals.UpdateLayoutAsync(window);
            await Task.Delay(750, TestContext.Current.CancellationToken);
            Assert.True(first.IsKeyboardFocusWithin); AssertNoOpenTitleHint(first);
            var second = Assert.IsType<WorkspaceTabStripItem>(strip.ItemContainerGenerator.ContainerFromItem(tabs[1]));
            second.IsSelected = true; Assert.True(second.Focus()); await WindowTestVisuals.UpdateLayoutAsync(window);
            AssertNoOpenTitleHint(first); AssertNoOpenTitleHint(second);
            tabs.RemoveAt(1); await WindowTestVisuals.UpdateLayoutAsync(window);
            AssertNoOpenTitleHint(second); Assert.True(input.Focus()); await WindowTestVisuals.UpdateLayoutAsync(window);
            AssertNoOpenTitleHint(first);
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); Text.SetApplicationCulture(null); }
    });

    private static void AssertNoOpenTitleHint(ListBoxItem item)
    {
        var hints = PresentationSource.CurrentSources.Cast<PresentationSource>()
            .Where(source => source.RootVisual is not null)
            .SelectMany(source => WindowTestVisuals.Descendants<ToolTip>(source.RootVisual!));
        Assert.DoesNotContain(hints, hint => hint.IsOpen && ReferenceEquals(hint.PlacementTarget, item));
    }

    private sealed record Tab(string Name, string Header);
}
