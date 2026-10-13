using System.Globalization;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实分隔条拖动、宽度恢复和收起后的全宽列表验证。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ObjectStorageDetailsSplitViewTests(WpfTestFixture fixture)
{
    /// <summary>拖动修改两侧宽度；收起释放详情空间并保留选择，恢复后按最近宽度显示。</summary>
    [Theory]
    [InlineData(UiTheme.Light, "zh-Hans")]
    [InlineData(UiTheme.Dark, "en")]
    public Task Splitter_DragsAndRestoresWidthWhilePreservingSelection(UiTheme theme, string language) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        var themes = new ThemeService(Application.Current.Resources);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        themes.SetTheme(theme);
        using var environment = new TemporaryStorageTestEnvironment();
        var service = ObjectStorageForcePreviewTestData.Service("image.png", "image/png");
        await using var model = ObjectStorageForcePreviewTestData.Model(service, new ObjectStorageTestDialogs(), environment.Storage);
        var view = new ObjectStorageView { DataContext = model };
        var window = new HermodWindow { Content = view, Width = 1120, Height = 820, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            await model.InitializeAsync(TestContext.Current.CancellationToken);
            await model.SelectEntryAsync(model.Entries.Single(row => row.Name == "image.png"), TestContext.Current.CancellationToken);
            Assert.Empty(service.DownloadLimits);
            await model.ForcePreviewCommand.ExecuteAsync();
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var details = Assert.Single(WindowTestVisuals.Descendants<ObjectStorageDetailsView>(view));
            var list = Assert.Single(WindowTestVisuals.Descendants<ObjectStorageListView>(view));
            var splitter = Assert.Single(WindowTestVisuals.Descendants<GridSplitter>(view));
            Assert.True(splitter.IsVisible);
            Assert.Equal(Text.Get("ResizeObjectDetails"), AutomationProperties.GetName(splitter));
            double original = details.ActualWidth;
            splitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            splitter.RaiseEvent(new DragDeltaEventArgs(-96, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            splitter.RaiseEvent(new DragCompletedEventArgs(-96, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.InRange(details.ActualWidth, original + 95, original + 97);
            splitter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(splitter), 0, Key.Left)
            { RoutedEvent = Keyboard.KeyDownEvent });
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.InRange(details.ActualWidth, original + 111, original + 113);
            double resized = details.ActualWidth;
            double listWidth = list.ActualWidth;
            await WindowPreviewCapture.VerifyAsync(window, $"object-details-resized-{theme}-{language}");
            WindowTestVisuals.Invoke(ObjectStorageDialogTestVisuals.CommandButton(window, model.CloseDetailsCommand));
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.False(details.IsVisible);
            Assert.False(splitter.IsVisible);
            Assert.InRange(list.ActualWidth, listWidth + resized + 5, listWidth + resized + 7);
            Assert.Equal("image.png", model.SelectedEntry?.Name);
            await WindowPreviewCapture.VerifyAsync(window, $"object-details-hidden-{theme}-{language}");
            await model.ToggleDetailsCommand.ExecuteAsync();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(details.IsVisible);
            Assert.InRange(details.ActualWidth, resized - 1, resized + 1);
            Assert.False(model.HasImagePreview);
            Assert.Single(service.DownloadLimits);
            window.Width = 620;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(details.ActualWidth <= view.ActualWidth - 265,
                $"Window={window.ActualWidth}, View={view.ActualWidth}, Details={details.ActualWidth}, List={list.ActualWidth}");
            Assert.True(list.ActualWidth >= 259);
        }
        finally
        {
            window.Close(); themes.SetTheme(UiTheme.Light); CultureInfo.CurrentUICulture = previous;
        }
    });
}
