using System.Globalization;
using System.Windows.Automation;
using Asgard.Hermod.ObjectStorage.UI.Models;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实 WPF 类型选择、模态确认、图片呈现及中英主题布局验证。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ObjectStorageForcePreviewWindowTests(WpfTestFixture fixture)
{
    /// <summary>未知类型默认为空，确认按钮绑定实时选择；模态窗体复用统一标题与操作栏。</summary>
    [Theory]
    [InlineData(UiTheme.Light, "zh-Hans", false)]
    [InlineData(UiTheme.Dark, "zh-Hans", true)]
    [InlineData(UiTheme.Light, "en", true)]
    [InlineData(UiTheme.Dark, "en", false)]
    public Task ConfirmationWindow_SelectsOnlyImageAndShowsTrafficWarning(UiTheme theme, string language, bool inferred) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        var themes = new ThemeService(Application.Current.Resources);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        themes.SetTheme(theme);
        using var model = new ObjectStorageForcePreviewDialogViewModel("image.dat", 839 * 1024, inferred ? ObjectStoragePreviewType.Image : null);
        var owner = ObjectStorageDialogTestVisuals.CreateOwner();
        var dialog = new ObjectStorageForcePreviewWindow(model) { Opacity = 0 };
        try
        {
            owner.Show();
            dialog.Owner = owner;
            bool? result = await ModalWindowTestInteraction.ShowAsync(dialog, async () =>
            {
                ObjectStorageDialogTestVisuals.AssertCommonFrame(dialog, owner, 548, 536);
                Assert.Equal(Text.Get("ForcePreview"), dialog.Title);
                var selector = Assert.IsType<ComboBox>(dialog.FindName("PreviewTypeSelector"));
                Assert.Single(selector.Items.Cast<object>());
                var confirm = ObjectStorageDialogTestVisuals.CommandButton(dialog, model.ConfirmCommand);
                Assert.Equal(inferred, confirm.IsEnabled);
                Assert.Equal(inferred ? ObjectStoragePreviewType.Image : null, (ObjectStoragePreviewType?)selector.SelectedValue);
                Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(dialog), text => text.Text == model.TrafficWarning && text.IsVisible);
                await WindowPreviewCapture.VerifyAsync(dialog, $"force-preview-confirm-{theme}-{language}-{inferred}");
                selector.SelectedValue = ObjectStoragePreviewType.Image;
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                Assert.True(confirm.IsEnabled);
                Assert.Equal(ObjectStoragePreviewType.Image, model.SelectedType);
                WindowTestVisuals.Invoke(confirm);
            });
            Assert.True(result);
            Assert.Equal(ObjectStoragePreviewType.Image, model.Result);
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            owner.Close();
            themes.SetTheme(UiTheme.Light);
            CultureInfo.CurrentUICulture = previous;
        }
    });

    /// <summary>图片在详情显示，收窄窗口仍保留下载入口；切换对象后清除图片。</summary>
    [Theory]
    [InlineData(UiTheme.Light, "zh-Hans", 1120d)]
    [InlineData(UiTheme.Dark, "en", 760d)]
    public Task DetailsView_PresentsFrozenImageAndKeepsActionsInsideWindow(UiTheme theme, string language, double width) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        var themes = new ThemeService(Application.Current.Resources);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        themes.SetTheme(theme);
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service();
        await using var model = ObjectStorageForcePreviewTestData.Model(service, ObjectStorageForcePreviewTestData.ConfirmingDialogs(), environment.Storage);
        var view = new ObjectStorageView { DataContext = model };
        var window = new HermodWindow { Content = view, Width = width, Height = 620, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            await model.InitializeAsync(TestContext.Current.CancellationToken);
            await model.SelectEntryAsync(model.Entries.Single(row => row.Name == "image.dat"), TestContext.Current.CancellationToken);
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var details = Assert.Single(WindowTestVisuals.Descendants<ObjectStorageDetailsView>(window));
            var force = Assert.Single(WindowTestVisuals.Descendants<Button>(details), button => ReferenceEquals(button.Command, model.ForcePreviewCommand));
            Assert.True(force.IsVisible);
            Assert.True(force.IsEnabled);
            await WindowPreviewCapture.VerifyAsync(window, $"force-preview-entry-{theme}-{language}");
            await model.ForcePreviewCommand.ExecuteAsync();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var image = Assert.Single(WindowTestVisuals.Descendants<Image>(details));
            Assert.True(image.IsVisible);
            Assert.Same(model.ImagePreview, image.Source);
            Assert.False(force.IsVisible);
            var download = Assert.Single(WindowTestVisuals.Descendants<Button>(details), button => ReferenceEquals(button.Command, model.DownloadCommand));
            Assert.True(download.IsEnabled);
            var bounds = WindowTestVisuals.ScreenBounds(view);
            var action = WindowTestVisuals.ScreenBounds(download);
            Assert.True(action.Left >= bounds.Left && action.Right <= bounds.Right + 1 && action.Bottom <= bounds.Bottom + 1);
            await WindowPreviewCapture.VerifyAsync(window, $"force-preview-image-{theme}-{language}");
            var enlarge = Assert.Single(WindowTestVisuals.Descendants<Button>(details), button => AutomationProperties.GetName(button) == Text.Get("ForcePreviewEnlarge"));
            var closePreview = window.Dispatcher.InvokeAsync(() =>
            {
                var preview = Assert.Single(Application.Current.Windows.Cast<Window>().OfType<UI.Views.Dialogs.FilePreviewWindow>());
                Assert.Same(model.ImagePreview, Assert.IsType<FilePreviewContent>(preview.DataContext).Image);
                preview.Close();
            }, DispatcherPriority.Background);
            enlarge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await closePreview;
            Assert.Single(service.DownloadLimits);
            await model.SelectEntryAsync(model.Entries.Single(row => row.Name == "other.dat"), TestContext.Current.CancellationToken);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Null(image.Source);
            Assert.False(image.IsVisible);
            Assert.True(force.IsVisible);
        }
        finally
        {
            window.Close();
            themes.SetTheme(UiTheme.Light);
            CultureInfo.CurrentUICulture = previous;
        }
    });
}
