using System.Globalization;
using Asgard.Hermod.UI.Views.Dialogs;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证公共入口、两列卡片与可变步骤容器的实际 WPF 布局和双语资源。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class InstanceWizardPresentationTests(WpfTestFixture fixture)
{
    /// <summary>初始入口没有进度；类型卡片按两列排布，奇数数量不拉伸最后一张。</summary>
    [Theory]
    [InlineData("en", 1)]
    [InlineData("zh-Hans", 3)]
    [InlineData("en", 6)]
    public Task TypePicker_UsesTwoColumnsAndEntersSelectedFlow(string language, int count) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var providers = Enumerable.Range(0, count).Select(index => new NewInstanceWizardTestProvider(
            "provider-" + index, "Provider " + index, new("type-" + index, "Type " + index, "Description", "database"))).ToArray();
        using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(providers), new NewInstanceWizardTestConfigurationService());
        var window = new NewInstanceWindow(model);
        try
        {
            WindowPreviewCapture.Show(window);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var layout = Assert.IsType<InstanceWizard>(WindowTestVisuals.Named(window, "WizardLayout"));
            var picker = Assert.IsType<InstanceTypePicker>(WindowTestVisuals.Named(window, "TypeList"));
            Assert.False(layout.ShowProgress);
            Assert.False(WindowTestVisuals.Named(window, "ProgressSidebar").IsVisible);
            Assert.False(WindowTestVisuals.Named(window, "ContinueButton").IsVisible);
            Assert.Empty(model.Steps);
            Assert.Empty(model.StepCountText);
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(window), item => item.IsVisible && item.Text == Text.Get("ChooseInstanceType"));
            var cards = WindowTestVisuals.Descendants<SelectionCard>(picker).ToArray();
            Assert.Equal(count, cards.Length);
            double width = cards[0].ActualWidth;
            Assert.InRange(width, 300, picker.ActualWidth / 2);
            Assert.All(cards, card => Assert.Equal(width, card.ActualWidth, 2));
            if (count > 1)
            {
                Point first = cards[0].TranslatePoint(new Point(), picker);
                Point second = cards[1].TranslatePoint(new Point(), picker);
                Assert.Equal(first.Y, second.Y);
                Assert.True(second.X > first.X + width);
                Assert.True(cards[2].TranslatePoint(new Point(), picker).Y > first.Y);
            }
            await WindowPreviewCapture.VerifyAsync(window, "instance-type-picker-" + language + "-" + count);
            cards[^1].IsChecked = true;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Same(model.Types[^1], model.SelectedType);
            Assert.True(layout.ShowProgress);
            Assert.False(picker.IsVisible);
            Assert.Equal(Text.Format("WizardStepOfTotal", 1, model.Steps.Count), model.StepCountText);
            Assert.True(WindowTestVisuals.Named(window, "ProviderList").IsVisible);
            Assert.True(WindowTestVisuals.Named(window, "ProviderList").IsKeyboardFocusWithin);
            await WindowPreviewCapture.VerifyAsync(window, "instance-connection-flow-" + language);
            model.PreviousCommand.Execute(null);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.False(layout.ShowProgress);
            Assert.True(picker.IsVisible);
            Assert.True(picker.IsKeyboardFocusWithin);
            Assert.Null(picker.SelectedItem);
        }
        finally { window.Close(); CultureInfo.CurrentUICulture = previous; }
    });

    /// <summary>共享容器可承载非存储流程与任意步骤数量，且隐藏侧栏后正文扩展至全宽。</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    public Task WizardShell_DisplaysCallerSuppliedSteps(int count) => fixture.RunAsync(async () =>
    {
        var body = new Border();
        var steps = Enumerable.Range(1, count).Select(index => new InstanceWizardStep(index, "Custom step " + index)).ToArray();
        var layout = new InstanceWizard { Heading = "Custom type", Description = "Custom workflow", Steps = steps, ShowProgress = true, Content = body };
        var window = new HermodDialog { Width = 820, Height = 760, Content = layout };
        try
        {
            WindowPreviewCapture.Show(window);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var progress = Assert.Single(WindowTestVisuals.Descendants<ItemsControl>(layout));
            Assert.Equal(count, progress.Items.Count);
            Assert.Equal(176d, WindowTestVisuals.Named(layout, "ProgressSidebar").ActualWidth);
            double flowWidth = body.ActualWidth;
            layout.ShowProgress = false;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(flowWidth + 176d, body.ActualWidth, 2);
            Assert.False(WindowTestVisuals.Named(layout, "ProgressSidebar").IsVisible);
            window.Width = 700;
            window.Height = 520;
            await WindowPreviewCapture.VerifyAsync(window, "instance-wizard-custom-" + count);
            Assert.True(body.ActualWidth > 0);
        }
        finally { window.Close(); }
    });
}
