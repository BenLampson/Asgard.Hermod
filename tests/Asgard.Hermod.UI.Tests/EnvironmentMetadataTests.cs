using System.Globalization;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>环境取决于稳定标签 ID，名称、路径和连接状态都不能推断生产环境。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class EnvironmentMetadataTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    public Task Environment_FollowsIdsAndUpdatesWithoutReplacingInstance(string language, UiTheme theme) => fixture.RunAsync(() =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            new ThemeService(Application.Current.Resources).SetTheme(theme);
            var instance = new InstanceItemViewModel(new("instance", "Production", "Production", "database", ["Environment/Production"]));
            Assert.Equal(Text.Get("EnvironmentNotSet"), instance.EnvironmentLabel);
            Assert.Equal(StatusTone.Muted, instance.EnvironmentTone);
            var badge = new StatusBadge { DataContext = instance };
            badge.SetBinding(StatusBadge.TextProperty, new Binding(nameof(InstanceItemViewModel.EnvironmentLabel)));
            badge.SetBinding(StatusBadge.ToneProperty, new Binding(nameof(InstanceItemViewModel.EnvironmentTone)));
            instance.UpdateSnapshot(instance.Summary with { TagIds = ["builtin.environment.production"] });
            Assert.Equal(Text.Get("Production"), badge.Text);
            Assert.Equal(StatusTone.Warning, badge.Tone);
            instance.UpdateSnapshot(instance.Summary with { TagIds = ["builtin.environment.development"] });
            Assert.Equal(Text.Get("Development"), badge.Text);
            Assert.Equal(StatusTone.Accent, badge.Tone);
            instance.UpdateSnapshot(instance.Summary with { TagIds = [] });
            Assert.Equal(Text.Get("EnvironmentNotSet"), badge.Text);
            Assert.Equal("instance", instance.Id);
        }
        finally { Text.SetApplicationCulture(null); }
        return Task.CompletedTask;
    });
}
