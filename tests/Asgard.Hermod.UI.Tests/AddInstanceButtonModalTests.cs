using System.Globalization;
using Asgard.Hermod.UI.Views.Dialogs;
using Asgard.Hermod.UI.Views.Shell;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证添加实例弹窗只通过原生模态机制阻止后方操作，入口按钮保持主操作外观。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AddInstanceButtonModalTests(WpfTestFixture fixture)
{
    /// <summary>明暗主题下，真实主视图按钮在打开、取消和再次打开向导时保持蓝底白字。</summary>
    [Theory]
    [InlineData(UiTheme.Light)]
    [InlineData(UiTheme.Dark)]
    public Task AddInstance_KeepsAppearanceWhileModalAndCanReopen(UiTheme theme) => fixture.RunAsync(async () =>
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        await using var environment = new ShellTestEnvironment();
        var window = new Asgard.Hermod.MainWindow(environment.Shell);
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            environment.Themes.SetTheme(theme);
            WindowPreviewCapture.Show(window);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var view = Assert.Single(WindowTestVisuals.Descendants<MainWorkspaceView>(window));
            var command = environment.Shell.Sidebar.AddInstanceCommand;
            var button = Assert.Single(WindowTestVisuals.Descendants<Button>(view),
                item => ReferenceEquals(item.Command, command));
            // 排除用户桌面鼠标位置对悬停色的影响，命令仍沿真实按钮绑定执行。
            button.IsHitTestVisible = false;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var icon = Assert.Single(WindowTestVisuals.Descendants<IconView>(button));
            var label = Assert.Single(WindowTestVisuals.Descendants<TextBlock>(button));
            Color background = BrushColor(button.Background);
            Color foreground = BrushColor(button.Foreground);
            Assert.Equal(ResourceColor("ActionFillBrush"), background);
            Assert.Equal(ResourceColor("InverseTextBrush"), foreground);

            int opened = 0;
            environment.Editor.CreateHandler = async () =>
            {
                opened++;
                using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(new NewInstanceWizardTestProvider()),
                    new NewInstanceWizardTestConfigurationService());
                var dialog = new NewInstanceWindow(model) { Owner = window, Opacity = 0, ShowInTaskbar = false };
                await ModalWindowTestInteraction.ShowAsync(dialog, async () =>
                {
                    await WindowTestVisuals.UpdateLayoutAsync(window);
                    Assert.False(WindowInteractionNativeMethods.IsEnabled(window));
                    Assert.True(WindowInteractionNativeMethods.IsEnabled(dialog));
                    Assert.True(command.IsRunning);
                    Assert.True(command.CanExecute(null));
                    Assert.True(button.IsEnabled);
                    Assert.Equal(background, BrushColor(button.Background));
                    Assert.Equal(foreground, BrushColor(label.Foreground));
                    Assert.Equal(foreground, BrushColor(icon.Foreground));

                    int beforeReentry = opened;
                    await command.ExecuteAsync();
                    Assert.Equal(beforeReentry, opened);
                    Assert.Single(window.OwnedWindows.Cast<Window>());
                    model.CancelCommand.Execute(null);
                });
                return null;
            };

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                WindowTestVisuals.Invoke(button);
                await WindowTestVisuals.UpdateLayoutAsync(window);
                Assert.Null(command.LastException);
                Assert.Equal(attempt, opened);
                Assert.False(command.IsRunning);
                Assert.True(command.CanExecute(null));
                Assert.True(WindowInteractionNativeMethods.IsEnabled(window));
                Assert.True(button.IsEnabled);
                Assert.Equal(background, BrushColor(button.Background));
                Assert.Equal(foreground, BrushColor(label.Foreground));
                Assert.Equal(foreground, BrushColor(icon.Foreground));
                Assert.Empty(window.OwnedWindows.Cast<Window>());
            }
        }
        finally
        {
            await window.RequestExitAsync(sessionEnding: true);
            environment.Themes.SetTheme(UiTheme.Light);
            CultureInfo.CurrentUICulture = previousCulture;
        }
    });

    /// <summary>读取控件实际应用的实色画刷。</summary>
    private static Color BrushColor(Brush brush) => Assert.IsType<SolidColorBrush>(brush).Color;

    /// <summary>读取当前主题的真实语义色。</summary>
    private static Color ResourceColor(string key) => BrushColor((Brush)Application.Current.FindResource(key));
}
