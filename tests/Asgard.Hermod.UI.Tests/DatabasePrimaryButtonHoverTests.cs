using System.Runtime.InteropServices;

namespace Asgard.Hermod.UI.Tests;

/// <summary>在真实指针下验证共享主按钮的可见性。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class DatabasePrimaryButtonHoverTests(WpfTestFixture fixture)
{
    /// <summary>明暗主题中，鼠标悬停时主按钮及其文字均保留正确的颜色。</summary>
    [Theory]
    [InlineData(UiTheme.Light)]
    [InlineData(UiTheme.Dark)]
    public Task PrimaryButtons_KeepReadableColorsOnHover(UiTheme theme) => fixture.RunAsync(async () =>
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HERMOD_RUN_DESKTOP_POINTER_TESTS") != "1",
            "物理指针集成测试仅在用户明确允许桌面验收并设置 HERMOD_RUN_DESKTOP_POINTER_TESTS=1 时运行。");
        Assert.True(GetCursorPos(out var original));
        var themes = new ThemeService(Application.Current.Resources);
        var window = new Window { Width = 440, Height = 500, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        var panel = new StackPanel { Margin = new Thickness(12) };
        var buttons = new List<Button>();
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Asgard.Hermod.UI;component/Resources/DatabaseDesign.xaml")
        });
        foreach (string key in new[] { "DatabasePrimaryButton", "DatabaseObjectPrimaryButton" })
        {
            var button = new Button { Content = new TextBlock { Text = "Test" }, Margin = new Thickness(0, 0, 0, 8) };
            button.SetResourceReference(FrameworkElement.StyleProperty, key);
            panel.Children.Add(button);
            buttons.Add(button);
        }
        var feedback = new MainConnectionFeedback
        {
            DataContext = new { HasError = true, IsConnecting = false, Title = "Test", Address = "", Detail = "", Diagnostics = "", RetryCommand = (System.Windows.Input.ICommand?)null, DismissCommand = (System.Windows.Input.ICommand?)null, CancelCommand = (System.Windows.Input.ICommand?)null }
        };
        panel.Children.Add(feedback);
        buttons.Add(Assert.IsType<Button>(feedback.FindName("RetryConnection")));
        window.Content = panel;
        try
        {
            themes.SetTheme(theme);
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            foreach (Button button in buttons)
            {
                if (!button.IsMouseOver)
                    Assert.Equal(ColorOf(button.FindResource("AccentBrush")), ColorOf(button.Background));
                Point point = button.PointToScreen(new Point(button.ActualWidth / 2, button.ActualHeight / 2));
                Assert.True(SetCursorPos((int)point.X, (int)point.Y));
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (!button.IsMouseOver)
                {
                    Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), "按钮未收到鼠标悬停状态。");
                    await Task.Delay(10, TestContext.Current.CancellationToken);
                }
                var label = Assert.Single(WindowTestVisuals.Descendants<TextBlock>(button));
                Assert.Equal(ColorOf(window.FindResource("ActionHoverBrush")), ColorOf(button.Background));
                Assert.Equal(ColorOf(window.FindResource("InverseTextBrush")), ColorOf(label.Foreground));
            }
        }
        finally
        {
            bool restored = SetCursorPos(original.X, original.Y);
            window.Close();
            themes.SetTheme(UiTheme.Light);
            Assert.True(restored, "无法恢复桌面指针位置。");
        }
    });

    private static Color ColorOf(object brush) => Assert.IsType<SolidColorBrush>(brush).Color;
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X; public int Y; }
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
}
