using System.Globalization;
using System.Windows.Automation;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证保存实例的搜索范围、首页优先级和按实际字体测量的长名称。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class WorkspaceRefinementTests(WpfTestFixture fixture)
{
    [Fact]
    public Task GlobalSearch_FindsAssignedTagsAndUserNames() => fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        var instance = environment.Shell.Sidebar.AllInstances.First(item => !item.IsLocal);
        instance.UpdateSnapshot(instance.Summary with { Tags = ["Team/Payments"] });
        environment.Shell.GlobalSearchQuery = "payments";
        Assert.Same(instance, Assert.Single(environment.Shell.GlobalSearchResults));
        environment.Shell.GlobalSearchQuery = "does-not-exist";
        Assert.Empty(environment.Shell.GlobalSearchResults);
        environment.Shell.GlobalSearchQuery = instance.Name;
        Assert.Contains(instance, environment.Shell.GlobalSearchResults);
    });

    [Theory]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    public Task RecentInstances_MoveAboveIntroduction(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            await using var environment = new ShellTestEnvironment();
            environment.Themes.SetTheme(theme);
            var home = environment.Shell.Workspace.Home;
            var view = new HomeWorkspaceView { DataContext = home };
            var window = new Window { Content = view, Width = 1100, Height = 800, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
            try
            {
                window.Show();
                await WpfTestFixture.DrainDispatcherAsync();
                var recent = Assert.IsType<Border>(view.FindName("RecentCard"));
                Assert.Equal(2, Grid.GetRow(recent));
                home.RecordOpened(environment.Shell.Sidebar.AllInstances.First());
                await WpfTestFixture.DrainDispatcherAsync();
                Assert.Equal(0, Grid.GetRow(recent));
                Assert.DoesNotContain(home.Types, item => item.Id == "local");
                Assert.False(Assert.Single(WindowTestVisuals.Descendants<Expander>(view)).IsExpanded);
            }
            finally { window.Close(); }
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    });

    [Fact]
    public Task LongNames_KeepDistinctSuffixesAndExpandWithWidth() => fixture.RunAsync(async () =>
    {
        const string prefix = "customer:1042:shared:namespace:";
        var profile = new MiddleEllipsisTextBlock { FullText = prefix + "profile", FontSize = 13, MaxWidth = 130 };
        var session = new MiddleEllipsisTextBlock { FullText = prefix + "session", FontSize = 13, MaxWidth = 130 };
        var panel = new StackPanel(); panel.Children.Add(profile); panel.Children.Add(session);
        var window = new Window { Content = panel, Width = 600, Height = 150, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        try
        {
            window.Show(); await WpfTestFixture.DrainDispatcherAsync();
            Assert.Contains("…", profile.Text);
            Assert.EndsWith("profile", profile.Text);
            Assert.EndsWith("session", session.Text);
            Assert.NotEqual(profile.Text, session.Text);
            Assert.Equal(profile.FullText, AutomationProperties.GetName(profile));
            Assert.Equal(profile.FullText, profile.ToolTip);
            profile.MaxWidth = 500;
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal(profile.FullText, profile.Text);
            profile.FullText = string.Concat(Enumerable.Repeat("👩‍💻é", 25)) + ":profile";
            profile.MaxWidth = 130;
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.EndsWith("profile", profile.Text);
            var originals = StringInfo.ParseCombiningCharacters(profile.FullText)
                .Select(index => StringInfo.GetNextTextElement(profile.FullText, index)).ToHashSet();
            foreach (int index in StringInfo.ParseCombiningCharacters(profile.Text))
            {
                string element = StringInfo.GetNextTextElement(profile.Text, index);
                Assert.True(element == "…" || originals.Contains(element));
            }
        }
        finally { window.Close(); }
    });
}
