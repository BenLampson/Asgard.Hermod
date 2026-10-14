using System.Globalization;
using System.Windows.Interop;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.MySql.UI.Services;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证实际向导放大、返回类型入口后，标题和页脚始终位于所属显示器工作区内。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class NewInstancePlacementTests(WpfTestFixture fixture)
{
    /// <summary>所属窗口靠近任务栏时，切换到更高的 MySQL 向导也不能遮住操作按钮。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task SelectingMySql_RepositionsExpandedWizardAndKeepsFooterVisible(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        var owner = new HermodWindow { Width = 640, Height = 480, Opacity = 0, ShowInTaskbar = false };
        using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(new MySqlWizardTestProvider()),
            new NewInstanceWizardTestConfigurationService());
        var dialog = new NewInstanceWindow(model, [new MySqlConfigurationContentProvider()]) { Opacity = 0 };
        try
        {
            owner.Show();
            dialog.Owner = owner;
            var work = WindowBoundsNativeMethods.GetWorkArea(new WindowInteropHelper(owner).Handle);
            var dpi = VisualTreeHelper.GetDpi(owner);
            owner.Left = work.Left / dpi.DpiScaleX + 10;
            owner.Top = work.Bottom / dpi.DpiScaleY - owner.Height - 10;
            await ModalWindowTestInteraction.ShowAsync(dialog, async () =>
            {
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                AssertFits(dialog, work);
                model.SelectedType = Assert.Single(model.Types);
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                AssertFits(dialog, work);
                await WindowPreviewCapture.VerifyAsync(dialog, $"mysql-wizard-fit-{language}-{theme}");
                Assert.True(dialog.Width <= 912);
                Assert.True(dialog.Height <= 740);
                model.PreviousCommand.Execute(null);
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                Assert.False(model.HasTypeSelection);
                AssertFits(dialog, work);
            });
            Assert.True(WindowInteractionNativeMethods.IsEnabled(owner));
        }
        finally { dialog.Close(); owner.Close(); themes.SetTheme(UiTheme.Light); Text.SetApplicationCulture(null); }
    });

    /// <summary>检查真实模板的设备像素边界，包含不能被任务栏遮住的页脚。</summary>
    private static void AssertFits(NewInstanceWindow dialog, Rect work)
    {
        foreach (var element in new[] { WindowTestVisuals.Frame(dialog), WindowTestVisuals.Caption(dialog), WindowTestVisuals.Footer(dialog) })
        {
            var bounds = WindowTestVisuals.ScreenBounds(element);
            Assert.True(bounds.Left >= work.Left - 1 && bounds.Top >= work.Top - 1
                && bounds.Right <= work.Right + 1 && bounds.Bottom <= work.Bottom + 1, $"Work={work}; Element={bounds}");
        }
    }
}
