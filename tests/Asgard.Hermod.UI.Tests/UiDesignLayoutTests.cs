using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>以真实模板核对设计稿的五步向导、设置与编辑窗体，支持导出浅深主题预览。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class UiDesignLayoutTests(WpfTestFixture fixture)
{
    /// <summary>步骤切换后动作区仍在可见范围，表单编辑器不会被主题切换重建丢失。</summary>
    [Theory]
    [InlineData(UiTheme.Light)]
    [InlineData(UiTheme.Dark)]
    public Task Wizard_AllStepsKeepFooterVisible(UiTheme theme) => fixture.RunAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(new NewInstanceWizardTestProvider()),
            new NewInstanceWizardTestConfigurationService());
        model.SelectedType = model.Types.Single(type => type.Id == "storage");
        var window = new NewInstanceWindow(model);
        try
        {
            WindowPreviewCapture.Show(window);
            for (int step = 1; step <= 5; step++)
            {
                Assert.Equal(step, model.CurrentStep);
                await WindowPreviewCapture.VerifyAsync(window, $"wizard-{theme}-{step}");
                if (step == 2)
                {
                    model.Name = "生产 OSS";
                    model.Fields.Single(field => field.Key == "accessKeyId").Value = "LTAIPREVIEW00000000";
                    model.Fields.Single(field => field.Key == "accessKeySecret").Value = "preview-secret";
                }
                if (step == 3)
                {
                    var description = Assert.IsType<TextBlock>(window.FindName("ConnectionTestDescription"));
                    Assert.True(description.IsVisible);
                    Assert.Equal(model.TestDescription, description.Text);
                    await model.TestConnectionCommand.ExecuteAsync();
                    await WindowPreviewCapture.VerifyAsync(window, $"wizard-{theme}-{step}-verified");
                }
                if (step < 5) model.NextCommand.Execute(null);
            }
            Assert.DoesNotContain(WindowTestVisuals.Descendants<TextBlock>(window),
                label => label.Text.Contains("preview-secret", StringComparison.Ordinal));
        }
        finally
        {
            window.Close();
            themes.SetTheme(UiTheme.Light);
        }
    });

    /// <summary>管理页面、编辑与删除预览在两种主题中共用窗体结构。</summary>
    [Theory]
    [InlineData(UiTheme.Light)]
    [InlineData(UiTheme.Dark)]
    public Task SettingsAndTagDialogs_KeepDesignedWindowSizes(UiTheme theme) => fixture.RunAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        var service = new TagManagementTestService();
        using var settings = new SettingsViewModel(service, new TagDialogTestService());
        await settings.InitializeAsync(TestContext.Current.CancellationToken);
        settings.Tags.SelectedNode = settings.Tags.Rows.Single(row => row.Id == "platform");
        var settingsWindow = new SettingsWindow(settings);
        using var editor = new TagEditorViewModel(service, service.Tags.Single(tag => tag.Id == "platform"));
        await editor.InitializeAsync(TestContext.Current.CancellationToken);
        var editorWindow = new TagEditorWindow(editor);
        using var deletion = new TagDeletionViewModel(service, service.Tags.Single(tag => tag.Id == "team"));
        await deletion.InitializeAsync(TestContext.Current.CancellationToken);
        var deletionWindow = new DeleteTagWindow(deletion);
        try
        {
            WindowPreviewCapture.Show(settingsWindow);
            await WindowPreviewCapture.VerifyAsync(settingsWindow, $"settings-{theme}");
            WindowPreviewCapture.Show(editorWindow);
            await WindowPreviewCapture.VerifyAsync(editorWindow, $"tag-editor-{theme}");
            editor.ColorText = "#12invalid";
            await WindowPreviewCapture.VerifyAsync(editorWindow, $"tag-invalid-{theme}");
            Assert.False(editor.CanSave);
            WindowPreviewCapture.Show(deletionWindow);
            await WindowPreviewCapture.VerifyAsync(deletionWindow, $"tag-delete-{theme}");
        }
        finally
        {
            deletionWindow.Close();
            editorWindow.Close();
            settingsWindow.Close();
            themes.SetTheme(UiTheme.Light);
        }
    });
}
