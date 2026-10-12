using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace Asgard.Hermod.UI.Tests;

/// <summary>共享复选框随主题更新，保留 WPF 原生三态与自动化交互。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class CheckBoxThemeTests(WpfTestFixture fixture)
{
    /// <summary>动态主题消除系统白底，自动化切换仍经过原生三态逻辑。</summary>
    [Fact]
    public Task ThemeAndNativeToggle_PreserveThreeStates() => fixture.RunAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(UiTheme.Light);
        var check = new CheckBox { Content = "选择本页全部对象", IsThreeState = true, IsChecked = false };
        var window = new HermodWindow { Content = check, Width = 360, Height = 160, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var box = Assert.IsType<Border>(check.Template.FindName("Box", check));
            Assert.Equal(16, box.ActualWidth);
            Assert.Equal(16, box.ActualHeight);
            var label = Assert.IsType<ContentPresenter>(check.Template.FindName("Label", check));
            Assert.Equal(new Thickness(8, 0, 0, 0), label.Margin);
            Assert.Equal(((SolidColorBrush)Application.Current.FindResource("SurfaceBrush")).Color,
                Assert.IsType<SolidColorBrush>(box.Background).Color);
            themes.SetTheme(UiTheme.Dark);
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal(((SolidColorBrush)Application.Current.FindResource("SurfaceBrush")).Color,
                Assert.IsType<SolidColorBrush>(box.Background).Color);
            var toggle = Assert.IsAssignableFrom<IToggleProvider>(new CheckBoxAutomationPeer(check).GetPattern(PatternInterface.Toggle));
            toggle.Toggle();
            Assert.True(check.IsChecked);
            toggle.Toggle();
            Assert.Null(check.IsChecked);
            toggle.Toggle();
            Assert.False(check.IsChecked);
            check.IsEnabled = false;
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal(((SolidColorBrush)Application.Current.FindResource("SidebarBrush")).Color,
                Assert.IsType<SolidColorBrush>(box.Background).Color);
            Assert.False(check.Focus());
        }
        finally { themes.SetTheme(UiTheme.Light); window.Close(); }
    });
}
