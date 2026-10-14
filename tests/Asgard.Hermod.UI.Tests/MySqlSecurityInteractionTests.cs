using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.MySql.UI.Services;
using Asgard.Hermod.MySql.UI.ViewModels;
using Asgard.Hermod.MySql.UI.Views;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实渲染安全分区并切换选项，检查绑定更新是否破坏窗口消息循环。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MySqlSecurityInteractionTests(WpfTestFixture fixture)
{
    /// <summary>在测试前后以真实键盘消息切换 TLS，窗口布局和取消操作必须持续可用。</summary>
    [Theory]
    [InlineData(false, "en", UiTheme.Light)]
    [InlineData(true, "en", UiTheme.Light)]
    [InlineData(false, "zh-Hans", UiTheme.Dark)]
    [InlineData(true, "zh-Hans", UiTheme.Dark)]
    public Task SwitchingTls_BeforeOrAfterFailedTest_KeepsWizardUsable(bool previouslyTested, string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        using var wizard = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(new MySqlWizardTestProvider()),
            new NewInstanceWizardTestConfigurationService());
        wizard.SelectedType = Assert.Single(wizard.Types);
        var window = new NewInstanceWindow(wizard, [new MySqlConfigurationContentProvider()])
        { Opacity = 0, ShowInTaskbar = false };
        try
        {
            await ModalWindowTestInteraction.ShowAsync(window, async () =>
            {
                wizard.NextCommand.Execute(null);
                await WindowTestVisuals.UpdateLayoutAsync(window);
                if (previouslyTested)
                {
                    wizard.Name = "MySQL regression";
                    wizard.Fields.Single(item => item.Key == "host").Value = "localhost";
                    wizard.Fields.Single(item => item.Key == "user").Value = "test";
                    wizard.NextCommand.Execute(null);
                    await wizard.TestConnectionCommand.ExecuteAsync();
                    Assert.Equal(InstanceConnectionTestState.Failed, wizard.TestState);
                    wizard.ModifyInformationCommand.Execute(null);
                    await WindowTestVisuals.UpdateLayoutAsync(window);
                }
                var editor = Assert.Single(WindowTestVisuals.Descendants<MySqlConnectionEditor>(window));
                var model = Assert.IsType<MySqlConnectionEditorViewModel>(editor.DataContext);
                model.Pane = "security";
                await WindowTestVisuals.UpdateLayoutAsync(window);
                foreach (string value in new[] { "disabled", "required", "verify-full", "disabled" })
                {
                    var selector = Assert.Single(WindowTestVisuals.Descendants<SegmentedSelector>(editor),
                        item => item.DataContext is InstanceConfigurationFieldViewModel { Key: "tls" });
                    selector.SelectedValue = "verify-full";
                    await WindowTestVisuals.UpdateLayoutAsync(window);
                    selector = Assert.Single(WindowTestVisuals.Descendants<SegmentedSelector>(editor),
                        item => item.DataContext is InstanceConfigurationFieldViewModel { Key: "tls" });
                    Assert.True(Assert.IsType<ListBoxItem>(selector.ItemContainerGenerator.ContainerFromIndex(0)).Focus());
                    WindowInteractionNativeMethods.PostKey(window, value switch { "disabled" => 0x23, "required" => 0x27, _ => 0x24 });
                    await WindowTestVisuals.UpdateLayoutAsync(window);
                    Assert.Equal(value, wizard.Fields.Single(item => item.Key == "tls").Value);
                    Assert.False(wizard.IsTesting);
                    Assert.True(wizard.CancelCommand.CanExecute(null));
                    Assert.True(wizard.IsInformationEditable);
                }
                Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(editor), item => item.IsVisible && item.Text == Text.Get("MySqlTlsWarning"));
            });
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); Text.SetApplicationCulture(null); }
    });
}
