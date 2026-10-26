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
    /// <summary>编辑时提供商列表可操作，双语和两种主题下切换品牌会显示对应的新表单。</summary>
    [Theory]
    [MemberData(nameof(CloudInputCases))]
    public Task EditWizard_ChangesProviderAndRendersItsFields(string language, UiTheme theme, string providerId) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        using var services = new ServiceCollection().AddHermodCore().AddAliyunOss().AddVolcengineTos().AddAwsS3().AddTencentCos().AddMinio().BuildServiceProvider();
        var configurations = new NewInstanceWizardTestConfigurationService();
        var existing = new InstanceSummary("provider-edit", "Storage instance", "Storage instance", "cloud", [],
            IsLocal: true, TypeId: "storage", ProviderId: "minio");
        using var model = new NewInstanceViewModel(services.GetRequiredService<IInstanceProviderRegistry>(), configurations, existing, new()
        {
            TypeId = "storage",
            ProviderId = "minio",
            Parameters = new Dictionary<string, string> { ["accessKeyId"] = "OriginalTestKey", ["accessKeySecret"] = "original-secret" },
        });
        var dialog = new NewInstanceWindow(model);
        try
        {
            WindowPreviewCapture.Show(dialog);
            await WindowPreviewCapture.VerifyAsync(dialog, $"provider-edit-{providerId}-{language}-{theme}");
            Assert.Equal(Text.Get("EditInstance"), dialog.Title);
            var providers = Assert.IsType<ListBox>(WindowTestVisuals.Named(dialog, "ProviderList"));
            Assert.True(providers.IsEnabled);
            Assert.False(Assert.IsAssignableFrom<Control>(WindowTestVisuals.Named(dialog, "TypeList")).IsEnabled);
            var selected = model.Providers.Single(provider => provider.Id == providerId);
            var card = Assert.Single(WindowTestVisuals.Descendants<SelectionCard>(providers), item => ReferenceEquals(item.DataContext, selected));
            Assert.True(card.Focus());
            card.IsChecked = true;
            Assert.Same(selected, model.SelectedProvider);
            model.NextCommand.Execute(null);
            await WindowTestVisuals.UpdateLayoutAsync(dialog);
            Assert.Equal(selected.Fields.Select(field => field.Key), model.Fields.Select(field => field.Key));
            Assert.Empty(NewInstanceWizardTests.GetField(model, "accessKeySecret").Value);
            Assert.Contains(WindowTestVisuals.Descendants<TextBox>(dialog),
                item => item.IsVisible && item.DataContext is InstanceConfigurationFieldViewModel { Key: "region" });
            var region = NewInstanceWizardTests.GetField(model, "region");
            var endpoint = NewInstanceWizardTests.GetField(model, "endpoint");
            var mode = NewInstanceWizardTests.GetField(model, "endpointMode");
            Assert.Equal("region", mode.Value);
            Assert.Equal(Text.Get("StorageRegion"), region.Label);
            Assert.True(region.IsVisible);
            Assert.False(endpoint.IsVisible);
            region.Value = "cn-beijing";
            var selector = Assert.Single(WindowTestVisuals.Descendants<SegmentedSelector>(dialog), item => ReferenceEquals(item.DataContext, mode));
            var endpointChoice = Assert.IsType<ListBoxItem>(selector.ItemContainerGenerator.ContainerFromIndex(1));
            Assert.True(endpointChoice.Focus());
            selector.SelectedValue = "endpoint";
            await WindowTestVisuals.UpdateLayoutAsync(dialog);
            Assert.Equal("endpoint", mode.Value);
            Assert.False(region.IsVisible);
            Assert.True(endpoint.IsVisible);
            Assert.False(endpoint.IsValid);
            Assert.Equal(Text.Get("StorageEndpoint"), endpoint.Label);
            Assert.NotEmpty(endpoint.Definition.Hint);
            Assert.Equal(2048, endpoint.Definition.MaxLength);
            var input = Assert.Single(WindowTestVisuals.Descendants<TextBox>(dialog),
                item => item.IsVisible && ReferenceEquals(item.DataContext, endpoint));
            Assert.True(input.Focus());
            input.Text = providerId switch
            {
                "aliyun-oss" => "https://oss-cn-shanghai.aliyuncs.com",
                "aws-s3" => "https://s3.eu-west-1.amazonaws.com",
                "tencent-cos" => "https://cos.ap-shanghai.myqcloud.com",
                _ => "https://tos-cn-shanghai.volces.com",
            };
            input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.Equal(input.Text, endpoint.Value);
            var name = Assert.IsType<TextBox>(WindowTestVisuals.Named(dialog, "NameInput"));
            Assert.Equal(existing.Name, name.Text);
            Assert.True(name.Focus());
            Assert.True(name.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            await WindowPreviewCapture.VerifyAsync(dialog, $"provider-edit-fields-{providerId}-{language}-{theme}");
            dialog.Width = 1000;
            dialog.Height = 720;
            await WindowPreviewCapture.VerifyAsync(dialog, $"provider-edit-expanded-{providerId}-{language}-{theme}");
            selector.SelectedValue = "region";
            await WindowTestVisuals.UpdateLayoutAsync(dialog);
            Assert.True(region.IsVisible);
            Assert.False(endpoint.IsVisible);
            Assert.Equal("cn-beijing", region.Value);
            Assert.Equal(input.Text, endpoint.Value);
            await WindowPreviewCapture.VerifyAsync(dialog, $"provider-edit-region-{providerId}-{language}-{theme}");
            Assert.False(model.CanCreate);
            Assert.Empty(configurations.Updates);
        }
        finally
        {
            dialog.Close();
            CultureInfo.CurrentUICulture = previous;
            themes.SetTheme(UiTheme.Light);
        }
    });

    /// <summary>全部区域云连接器在双语和两种主题下使用相同的地址填写选择。</summary>
    public static IEnumerable<object[]> CloudInputCases() =>
        from language in new[] { "en", "zh-Hans" }
        from theme in new[] { UiTheme.Light, UiTheme.Dark }
        from provider in new[] { "aliyun-oss", "aws-s3", "tencent-cos", "volcengine-tos" }
        select new object[] { language, theme, provider };

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
            var providerList = Assert.IsType<ListBox>(WindowTestVisuals.Named(dialog, "ProviderList"));
            var scroll = Assert.IsType<ScrollViewer>(WindowTestVisuals.Named(dialog, "PART_BodyScroll"));
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
            var name = Assert.IsType<TextBox>(WindowTestVisuals.Named(dialog, "NameInput"));
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
