using System.Windows.Input;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>鼠标在类型或服务商卡片内时仍由向导正文处理滚轮。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class NewInstanceWizardScrollTests(WpfTestFixture fixture)
{
    /// <summary>卡片和卡片文字的上下滚轮都移动外层内容，选择行为保持正常。</summary>
    [Theory]
    [InlineData("TypeList", false)]
    [InlineData("ProviderList", false)]
    [InlineData("ProviderList", true)]
    public Task WheelOverCard_ScrollsWizardBody(string listName, bool overText) => fixture.RunAsync(async () =>
    {
        var providers = Enumerable.Range(0, 10).Select(index => new NewInstanceWizardTestProvider("provider-" + index, "Provider " + index,
            listName == "TypeList" ? new("type-" + index, "Type " + index, "Description", "database") : null)).ToArray();
        using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(providers), new NewInstanceWizardTestConfigurationService());
        if (listName != "TypeList")
            model.SelectedType = model.Types[0];
        var window = new NewInstanceWindow(model) { Height = 520 };
        try
        {
            WindowPreviewCapture.Show(window);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var scroll = Assert.IsType<ScrollViewer>(WindowTestVisuals.Named(window, "PART_BodyScroll"));
            var list = Assert.IsAssignableFrom<ListBox>(WindowTestVisuals.Named(window, listName));
            Assert.Empty(WindowTestVisuals.Descendants<ScrollViewer>(list));
            Assert.True(scroll.ScrollableHeight > 0);
            var card = WindowTestVisuals.Descendants<SelectionCard>(list).First();
            UIElement target = overText ? WindowTestVisuals.Descendants<TextBlock>(card).First() : card;
            scroll.ScrollToTop();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.MouseWheelEvent });
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(scroll.VerticalOffset > 0);
            double offset = scroll.VerticalOffset;
            target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120) { RoutedEvent = Mouse.MouseWheelEvent });
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(scroll.VerticalOffset < offset);
            if (listName == "ProviderList")
            {
                card.IsChecked = true;
                Assert.Same(card.DataContext, model.SelectedProvider);
            }
            await WindowPreviewCapture.VerifyAsync(window, "wizard-card-scroll-" + listName + "-" + overText);
        }
        finally { window.Close(); }
    });
}
