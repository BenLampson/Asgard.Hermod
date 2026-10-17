using System.Globalization;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Asgard.Hermod.UI.Views.Dialogs;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>启动真实双语 WPF 向导，验证标签浮层、绑定、键盘取消和缩放行为。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class InstanceTagSelectorWindowTests(WpfTestFixture fixture)
{
    /// <summary>只有最后一步可见，独立复选与确认回显在两种语言和窄窗口中均生效。</summary>
    [Theory]
    [InlineData("en", 820, "light")]
    [InlineData("zh-Hans", 720, "dark")]
    public Task ConfirmationSelector_IsLocalizedAndAppliesActualCheckboxes(string language, int width, string theme) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        var themeDictionary = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Asgard.Hermod.UI;component/Resources/Theme.{(theme == "dark" ? "Dark" : "Light")}.xaml") };
        NewInstanceWindow? window = null;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            Application.Current.Resources.MergedDictionaries.Add(themeDictionary);
            using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(new NewInstanceWizardTestProvider()),
                new NewInstanceWizardTestConfigurationService(), tags: TagManagementTestData.Create());
            window = new NewInstanceWindow(model);
            WindowPreviewCapture.Show(window);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            // 初始类型入口会折叠连接正文，隐藏控件可能尚未进入可视树；从其 XAML 名称作用域获取。
            var connectionContent = Assert.IsType<InstanceConnectionWizardContent>(window.FindName("ConnectionContent"));
            var selector = Assert.IsType<InstanceTagSelector>(connectionContent.FindName("ConfirmationTagSelector"));
            var trigger = Assert.IsType<Button>(selector.FindName("TagTrigger"));
            Assert.False(selector.IsVisible);
            Assert.False(trigger.IsEnabled);
            await NewInstanceWizardTests.ReachConfirmationAsync(model);
            window.Width = width;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(selector.IsVisible);
            Assert.True(trigger.IsEnabled);
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(selector), text => text.Text == Text.Get("InstanceTagsOptional"));
            Assert.Equal(Text.Get("InstanceConfirmAddition"), model.ContinueLabel);
            trigger.BringIntoView();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var popup = Assert.IsType<Popup>(selector.FindName("TagPopup"));
            Assert.True(popup.IsOpen);
            var panel = Assert.IsType<Border>(selector.FindName("PopupPanel"));
            var draft = Assert.IsType<InstanceTagPickerViewModel>(panel.DataContext);
            var box = Assert.Single(WindowTestVisuals.Descendants<CheckBox>(panel), control =>
                control.DataContext is InstanceTagOptionViewModel { Tag.Id: "platform" });
            box.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(["platform"], draft.SelectedIds);
            Assert.False(model.CanCreate);
            Assert.Empty(model.TagSelection.SelectedIds);
            CapturePanel(panel, "instance-tags-popup-" + language + "-" + theme);
            var apply = Assert.Single(WindowTestVisuals.Descendants<Button>(panel), button => button.Command == draft.ApplyCommand);
            WindowTestVisuals.Invoke(apply);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.False(popup.IsOpen);
            Assert.Equal(["platform"], model.TagSelection.SelectedIds);
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(selector), text => text.Text == "Team/Platform");
            await WindowPreviewCapture.VerifyAsync(window, "instance-tags-confirm-" + language + "-" + theme);

            trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var search = Assert.IsType<TextBox>(selector.FindName("TagSearch"));
            var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(search)!, 0, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            search.RaiseEvent(key);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(key.Handled);
            Assert.False(popup.IsOpen);
            Assert.True(window.IsVisible);
            Assert.Equal(["platform"], model.TagSelection.SelectedIds);
            Assert.True(model.CanCreate);
            var remove = Assert.Single(WindowTestVisuals.Descendants<Button>(selector), button => button.Command == model.TagSelection.RemoveCommand);
            WindowTestVisuals.Invoke(remove);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Empty(model.TagSelection.SelectedIds);
            Assert.True(model.HasSuccessfulConnectionTest);
        }
        finally
        {
            window?.Close();
            Application.Current.Resources.MergedDictionaries.Remove(themeDictionary);
            CultureInfo.CurrentUICulture = previous;
        }
    });

    /// <summary>可选导出真实浮层截图，仅写测试临时目录，不加入设计或源码提交。</summary>
    private static void CapturePanel(FrameworkElement panel, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("HERMOD_UI_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(panel.ActualWidth), (int)Math.Ceiling(panel.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(panel);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }
}
