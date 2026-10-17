using System.Globalization;
using System.Windows.Input;
using Asgard.Hermod.UI.Services;
using Asgard.Hermod.UI.Views.Dialogs;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>通过标题铅笔使用的真实服务验证 SSH、MySQL、PostgreSQL、Redis 品牌编辑和取消路径。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class HostingProviderEditorTests(WpfTestFixture fixture)
{
    /// <summary>双语主题中的实际弹窗包含品牌选项，保存直接更新元数据，取消不写入。</summary>
    [Theory]
    [InlineData("ssh", "en", UiTheme.Light)]
    [InlineData("ssh", "zh-Hans", UiTheme.Dark)]
    [InlineData("ssh", "zh-Hans", UiTheme.Light)]
    [InlineData("mysql", "en", UiTheme.Dark)]
    [InlineData("mysql", "zh-Hans", UiTheme.Light)]
    [InlineData("postgresql", "en", UiTheme.Dark)]
    [InlineData("postgresql", "zh-Hans", UiTheme.Light)]
    [InlineData("redis", "en", UiTheme.Light)]
    [InlineData("redis", "zh-Hans", UiTheme.Dark)]
    public Task TitleEditor_ChangesHostingBrandWithoutEditingConnection(string type, string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        using var environment = new LocalStorageTestEnvironment();
        using var services = HostingProviderPersistenceTests.Services(environment, type);
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var store = services.GetRequiredService<IInstanceConfigurationService>();
        var saved = await store.CreateAsync(HostingProviderPersistenceTests.Draft(type), TestContext.Current.CancellationToken);
        var editor = new InstanceEditorService(() => null, services.GetRequiredService<IInstanceProviderRegistry>(), store,
            tags: services.GetRequiredService<ITagManagementService>());
        try
        {
            foreach (bool save in new[] { false, true })
            {
                var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
                timer.Tick += async (_, _) =>
                {
                    var window = Application.Current.Windows.OfType<MetadataEditorWindow>().SingleOrDefault();
                    if (window is null) return;
                    timer.Stop();
                    window.Opacity = 0;
                    try
                    {
                        await WindowTestVisuals.UpdateLayoutAsync(window);
                        Assert.Equal(Text.Get("EditInstanceDetails"), window.Title);
                        var model = Assert.IsType<MetadataEditorViewModel>(window.DataContext);
                        Assert.True(model.IsHostingProviderEditing);
                        var selector = Assert.IsType<InstanceHostingProviderSelector>(window.FindName("HostingProviderSelector"));
                        Assert.True(selector.IsVisible);
                        var input = Assert.IsType<ComboBox>(selector.FindName("ProviderInput"));
                        Assert.Equal("aliyun", input.SelectedValue);
                        Assert.Equal(5, input.Items.Count);
                        input.SelectedValue = "volcengine";
                        await WindowTestVisuals.UpdateLayoutAsync(window);
                        Assert.Equal("volcengine", model.HostingProviderId);
                        Assert.True(input.Focus());
                        input.IsDropDownOpen = true;
                        window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(input)!, 0, Key.Escape)
                        { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                        Assert.False(input.IsDropDownOpen);
                        Assert.True(window.IsVisible);
                        window.Width = 460;
                        await WindowPreviewCapture.VerifyAsync(window, $"hosting-provider-{type}-{language}-{theme}");
                        if (save) await model.SaveCommand.ExecuteAsync();
                        else model.CancelCommand.Execute(null);
                        completed.TrySetResult();
                    }
                    catch (Exception error) { completed.TrySetException(error); }
                    finally { if (window.IsVisible) window.Close(); }
                };
                timer.Start();
                try
                {
                    var result = await editor.RenameAsync(saved.Id);
                    await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                    Assert.Equal(save, result is not null);
                    Assert.Equal(save ? "volcengine" : "aliyun", Assert.Single(store.GetInstances()).HostingProviderId);
                    Assert.Equal("transport", Assert.Single(store.GetInstances()).ProviderId);
                }
                finally { timer.Stop(); }
            }
        }
        finally { Text.SetApplicationCulture(null); themes.SetTheme(UiTheme.Light); }
    });
}
