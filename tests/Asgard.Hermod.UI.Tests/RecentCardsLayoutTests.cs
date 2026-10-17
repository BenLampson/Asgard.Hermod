using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.UI.Tests;

/// <summary>六张最近卡片按三列两行展示，重启保留排序，窄窗口与长名称仍可打开。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class RecentCardsLayoutTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task SixRecentCards_UseTwoRowsAndKeepTimesAndNavigation(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var registry = new TypesOnlyRegistry(); InstanceItemViewModel? opened = null;
        var home = new HomeWorkspaceViewModel(instance => opened = instance, registry);
        string[] names = language == "zh-Hans" ? ["公司99服务器", "开发缓存", "本地测试", "腾讯南京对象存储", "火山测试机器", "阿里云归档"]
            : ["Production MySQL", "Development cache", "Local database", "Nanjing object storage", "Test SSH server", "Archive storage"];
        string[] types = ["mysql", "redis", "mysql", "storage", "ssh", "storage"];
        var instances = names.Select((name, index) => new InstanceItemViewModel(new("recent-" + index, name, name, registry.Types.Single(type => type.Id == types[index]).Icon, [], TypeId: types[index]), registry)).ToArray();
        home.Refresh(instances); foreach (var instance in instances.Reverse()) home.RecordOpened(instance);
        var view = new HomeWorkspaceView { DataContext = home }; var window = new HermodWindow { Width = 1180, Height = 820, Content = view };
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(6, home.RecentInstances.Count); Assert.Equal(instances, home.RecentInstances.Select(item => item.Instance));
            var buttons = RecentButtons(view, home); VerifyColumns(view, buttons, 3);
            foreach (var button in buttons)
            {
                var recent = Assert.IsType<RecentInstanceViewModel>(button.CommandParameter);
                var time = Assert.Single(WindowTestVisuals.Descendants<TextBlock>(button), block => block.Name == "OpenedAt");
                Assert.Equal(recent.OpenedAtText, time.Text); Assert.Equal(recent.OpenedAtHint, time.ToolTip);
                Assert.True(time.TranslatePoint(new Point(), button).Y > button.ActualHeight / 2);
                Assert.Single(WindowTestVisuals.Descendants<TextBlock>(button), text => text.Text == recent.Instance.DisplayName);
                Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(button), text => text.Text == recent.Instance.TypeName);
            }
            await WindowPreviewCapture.VerifyAsync(window, $"home-six-recent-{language}-{theme}");
            Assert.True(buttons[5].Focus()); WindowTestVisuals.Invoke(buttons[5]); Assert.Same(instances[5], opened);
            window.Width = 800; await WindowTestVisuals.UpdateLayoutAsync(window); VerifyColumns(view, buttons, 2);
            window.Width = 520; await WindowTestVisuals.UpdateLayoutAsync(window); VerifyColumns(view, buttons, 1);
            var scroll = Assert.Single(WindowTestVisuals.Descendants<ScrollViewer>(view), item => item.Parent is HomeWorkspaceView); Assert.Equal(0, scroll.ScrollableWidth);
            instances[0].UpdateSnapshot(instances[0].Summary with { DisplayName = new string('W', 100) }); home.Refresh(instances); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(6, RecentButtons(view, home).Length); await WindowPreviewCapture.VerifyAsync(window, $"home-six-recent-narrow-{language}-{theme}");
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); Text.SetApplicationCulture(null); }
    });
    private static Button[] RecentButtons(HomeWorkspaceView view, HomeWorkspaceViewModel home) =>
        WindowTestVisuals.Descendants<Button>(Assert.IsType<ItemsControl>(view.FindName("RecentItems"))).Where(button => ReferenceEquals(button.Command, home.OpenRecentCommand)).ToArray();
    private static void VerifyColumns(HomeWorkspaceView view, Button[] buttons, int columns)
    {
        var panel = Assert.Single(WindowTestVisuals.Descendants<HomeRecentPanel>(view));
        var origins = buttons.Select(button => button.TranslatePoint(new Point(), panel)).ToArray();
        for (int index = 0; index < buttons.Length; index++)
        {
            Assert.Equal(buttons[0].ActualWidth, buttons[index].ActualWidth);
            Assert.Equal(origins[index % columns].X, origins[index].X); Assert.Equal(origins[index / columns * columns].Y, origins[index].Y);
            Assert.InRange(origins[index].X + buttons[index].ActualWidth, 1, panel.ActualWidth + 0.1);
            if (index >= columns) Assert.True(origins[index].Y > origins[index - columns].Y);
        }
    }
    private sealed class TypesOnlyRegistry : IInstanceProviderRegistry
    {
        public IReadOnlyList<InstanceTypeDescriptor> Types { get; } = [new("mysql", "MySQL", "", "database"), new("redis", "Redis", "", "layers"), new("storage", Text.Get("Storage"), "", "box"), new("ssh", "SSH", "", "terminal")];
        public IReadOnlyList<InstanceProviderDescriptor> GetProviders(string typeId) => [];
        public IInstanceProvider GetProvider(string typeId, string providerId) => throw new NotSupportedException();
    }
}
