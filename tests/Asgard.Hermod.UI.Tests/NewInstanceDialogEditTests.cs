using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证编辑模式的真实窗口标题、预填密钥控件与取消路径。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class NewInstanceDialogEditTests(WpfTestFixture fixture)
{
    /// <summary>已有配置通过通用字段模板预填，类型列表禁用，取消不产生更新。</summary>
    [Fact]
    public Task EditDialog_PrefillsMaskedControlsAndCancelsWithoutSaving() => fixture.RunAsync(async () =>
    {
        var existing = new InstanceSummary("existing-modal", "Existing modal OSS", "Existing modal OSS", "cloud",
            ["生产"], IsLocal: true, TypeId: "storage", ProviderId: "aliyun-oss");
        var connection = new InstanceConnectionOptions
        {
            TypeId = "storage",
            ProviderId = "aliyun-oss",
            Parameters = new Dictionary<string, string>
            {
                ["accessKeyId"] = "LTAIexistingmodal0000",
                ["accessKeySecret"] = "existing-modal-secret",
            },
        };
        var configurations = new NewInstanceWizardTestConfigurationService();
        using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(new NewInstanceWizardTestProvider()),
            configurations, existing, connection);
        var owner = new HermodWindow { Content = new Grid(), Width = 640, Height = 480, Opacity = 0, ShowInTaskbar = false };
        var dialog = new NewInstanceWindow(model) { Opacity = 0 };
        try
        {
            owner.Show();
            dialog.Owner = owner;
            bool? result = await ModalWindowTestInteraction.ShowAsync(dialog, async () =>
            {
                Assert.Equal("编辑实例", dialog.Title);
                Assert.False(Assert.IsType<ListBox>(dialog.FindName("TypeList")).IsEnabled);
                Assert.False(Assert.IsType<ListBox>(dialog.FindName("ProviderList")).IsEnabled);
                model.NextCommand.Execute(null);
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                Assert.Equal("Existing modal OSS", Assert.IsType<TextBox>(dialog.FindName("NameInput")).Text);
                var key = Assert.Single(WindowTestVisuals.Descendants<TextBox>(dialog),
                    control => control.DataContext is InstanceConfigurationFieldViewModel { Key: "accessKeyId" });
                Assert.Equal("LTAIexistingmodal0000", key.Text);
                var secret = Assert.Single(WindowTestVisuals.Descendants<SecretInput>(dialog));
                var editor = Assert.IsType<PasswordBox>(secret.Template.FindName("PART_Editor", secret));
                Assert.Equal("existing-modal-secret", editor.Password);
                Assert.DoesNotContain(WindowTestVisuals.Descendants<TextBox>(dialog),
                    control => control.DataContext is InstanceConfigurationFieldViewModel { Key: "accessKeySecret" }
                        && (control.IsVisible || control.Text.Length > 0));
                model.CancelCommand.Execute(null);
                await WpfTestFixture.DrainDispatcherAsync();
            });
            Assert.Equal<bool?>(false, result);
            Assert.Equal(0, configurations.CallCount);
            Assert.Equal(0, configurations.UpdateCallCount);
            Assert.Null(model.CreatedInstance);
        }
        finally
        {
            if (dialog.IsVisible)
                dialog.Close();
            owner.Close();
        }
    });
}
