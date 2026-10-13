using System.Globalization;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Providers.AwsS3;
using Asgard.Hermod.Providers.Minio;
using Asgard.Hermod.Providers.S3;
using Asgard.Hermod.Providers.TencentCos;
using Asgard.Hermod.Providers.VolcengineTos;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证品牌资源随程序集发布，以及双语主题下长表单的滚动和键盘导航。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class StorageProviderUiTests(WpfTestFixture fixture)
{
    /// <summary>所有提供商使用同一向导，最后的配置字段可滚动访问，不裁切底部动作。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task Wizard_RendersBundledBrandsAndScrollableFields(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        using var services = new ServiceCollection().AddHermodCore().AddAliyunOss().AddAwsS3()
            .AddTencentCos().AddVolcengineTos().AddMinio().AddS3Connector<S3CompatibleStorageProvider>().BuildServiceProvider();
        using var model = new NewInstanceViewModel(services.GetRequiredService<IInstanceProviderRegistry>(), new NewInstanceWizardTestConfigurationService());
        var dialog = new NewInstanceWindow(model);
        try
        {
            model.SelectedType = model.Types.Single(type => type.Id == "storage");
            WindowPreviewCapture.Show(dialog);
            await WindowPreviewCapture.VerifyAsync(dialog, $"providers-{language}-{theme}");
            Assert.Equal(6, model.Providers.Count);
            var providerList = Assert.IsType<ListBox>(dialog.FindName("ProviderList"));
            var scroll = Assert.Single(WindowTestVisuals.Descendants<ScrollViewer>(dialog), item => item.Content is StackPanel);
            Assert.True(scroll.ScrollableHeight > 0);
            scroll.ScrollToEnd();
            await WindowPreviewCapture.VerifyAsync(dialog, $"providers-end-{language}-{theme}");
            var selected = model.Providers.Single(provider => provider.Id == "minio");
            providerList.SelectedItem = selected;
            Assert.Same(selected, model.SelectedProvider);
            model.NextCommand.Execute(null);
            await WindowTestVisuals.UpdateLayoutAsync(dialog);
            Assert.Equal(2, model.CurrentStep);
            Assert.Equal(7, model.Fields.Count);
            Assert.All(model.Fields, field => Assert.NotEqual(field.Key, field.Label));
            var name = Assert.IsType<TextBox>(dialog.FindName("NameInput"));
            Assert.True(name.Focus());
            Assert.True(name.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            Assert.IsAssignableFrom<FrameworkElement>(Keyboard.FocusedElement);
            scroll.ScrollToEnd();
            await WindowPreviewCapture.VerifyAsync(dialog, $"provider-fields-{language}-{theme}");
            var last = Assert.Single(WindowTestVisuals.Descendants<TextBox>(dialog),
                item => item.DataContext is InstanceConfigurationFieldViewModel { Key: "addressingStyle" });
            var bounds = last.TransformToAncestor(scroll).TransformBounds(new Rect(last.RenderSize));
            Assert.InRange(bounds.Bottom, 0, scroll.ActualHeight + 1);
            Assert.Equal(Text.Get("StorageAddressingStyle"), model.Fields[^1].Label);
            dialog.Width = 1000;
            dialog.Height = 720;
            await WindowPreviewCapture.VerifyAsync(dialog, $"provider-fields-expanded-{language}-{theme}");
        }
        finally
        {
            dialog.Close();
            CultureInfo.CurrentUICulture = previous;
            themes.SetTheme(UiTheme.Light);
        }
    });

    /// <summary>发布程序集内部包含五家官方图像，WPF 可直接离线解码。</summary>
    [Fact]
    public Task OfficialIcons_AreEmbeddedAndDecodable() => fixture.RunAsync(() =>
    {
        foreach (string file in new[] { "aliyun-oss.png", "tencent-cos.ico", "volcengine-tos.png", "aws-s3.png", "minio.png" })
        {
            var uri = new Uri($"pack://application:,,,/Asgard.Hermod.UI;component/Assets/Providers/{file}");
            var resource = Application.GetResourceStream(uri);
            Assert.NotNull(resource);
            using var stream = resource.Stream;
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert.Contains(decoder.Frames, frame => frame.PixelWidth >= 16 && frame.PixelHeight >= 16);
        }
        return Task.CompletedTask;
    });
}
