using System.Globalization;
using System.Windows.Input;
using Asgard.Hermod.MySql;
using Asgard.Hermod.MySql.UI.Services;
using Asgard.Hermod.Redis;
using Asgard.Hermod.Redis.UI.Services;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.Services;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>通过真实向导和逐字符输入验证共享字段不会在绑定刷新时重建或丢失焦点。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ConnectionInputFocusTests(WpfTestFixture fixture)
{
    /// <summary>普通文字、完整 IP、端口与密码均连续输入；同时覆盖中英文和浅深主题。</summary>
    [Theory]
    [InlineData("mysql", "en", UiTheme.Light)]
    [InlineData("mysql", "zh-Hans", UiTheme.Dark)]
    [InlineData("redis", "en", UiTheme.Light)]
    [InlineData("redis", "zh-Hans", UiTheme.Dark)]
    [InlineData("ssh", "en", UiTheme.Light)]
    [InlineData("ssh", "zh-Hans", UiTheme.Dark)]
    [InlineData("storage", "en", UiTheme.Light)]
    [InlineData("storage", "zh-Hans", UiTheme.Dark)]
    public Task Typing_PreservesEditorsFocusAndCaret(string type, string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(theme);
        var descriptor = type switch
        {
            "mysql" => MySqlProfile.Descriptor(MySqlIds.Direct),
            "redis" => RedisProfile.Descriptor(RedisIds.Standalone),
            "ssh" => SshProfile.Descriptor(SshIds.Direct),
            _ => new NewInstanceWizardTestProvider().Descriptor,
        };
        using var wizard = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(new ProfileProvider(descriptor)),
            new NewInstanceWizardTestConfigurationService());
        wizard.SelectedType = Assert.Single(wizard.Types);
        if (type == "redis") wizard.Fields.Single(field => field.Key == "auth").Value = "password";
        var window = new NewInstanceWindow(wizard,
            [new MySqlConfigurationContentProvider(), new RedisConfigurationContentProvider(), new SshConfigurationContentProvider()])
        { Opacity = 0, ShowInTaskbar = false };
        try
        {
            // 本组通过合成文本验证字段生命周期，不让开发机输入法把候选提交混入逐字断言。
            InputMethod.SetIsInputMethodEnabled(window, false);
            window.Show();
            window.Activate(); await WindowInteractionNativeMethods.DrainNativeMessagesAsync();
            wizard.NextCommand.Execute(null);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            foreach (string key in new[] { "name", type == "storage" ? "accessKeyId" : "host", "port", "user" })
            {
                if (key != "name" && !wizard.Fields.Any(field => field.Key == key && field.IsVisible)) continue;
                var input = Assert.Single(WindowTestVisuals.Descendants<TextBox>(window), item => item.IsVisible
                    && (key == "name" ? item.Name is "NameInput" or "SshNameInput"
                        : item.DataContext is InstanceConfigurationFieldViewModel field && field.Key == key));
                input.Clear();
                await WindowTestVisuals.UpdateLayoutAsync(window);
                // 清空也会触发绑定；从当前可视树获取编辑器后只聚焦一次。
                input = Assert.Single(WindowTestVisuals.Descendants<TextBox>(window), item => item.IsVisible
                    && (key == "name" ? item.Name is "NameInput" or "SshNameInput"
                        : item.DataContext is InstanceConfigurationFieldViewModel field && field.Key == key));
                Assert.True(input.Focus());
                string value = key switch { "host" => "10.20.30.40", "port" => "12345", _ => "a10b29" };
                for (int index = 0; index < value.Length; index++)
                {
                    TypeCharacter(value[index]);
                    await WindowTestVisuals.UpdateLayoutAsync(window);
                    Assert.True(input.IsKeyboardFocused, WindowInteractionNativeMethods.DescribeFocus(window, input));
                    Assert.Same(input, Keyboard.FocusedElement);
                    Assert.Same(window, Window.GetWindow(input));
                    Assert.Equal(value[..(index + 1)], input.Text);
                    Assert.Equal(index + 1, input.CaretIndex);
                }
                Assert.Equal(value, key == "name" ? wizard.Name : wizard.Fields.Single(field => field.Key == key).Value);
            }

            // Redis 的密码位于安全分区，切换后仍通过同一个公共密钥控件输入。
            if (type == "redis")
            {
                var editor = Assert.Single(WindowTestVisuals.Descendants<Asgard.Hermod.Redis.UI.Views.RedisConnectionEditor>(window));
                Assert.IsType<Asgard.Hermod.Redis.UI.ViewModels.RedisConnectionEditorViewModel>(editor.DataContext).Pane = "security";
                await WindowTestVisuals.UpdateLayoutAsync(window);
            }
            var secret = Assert.Single(WindowTestVisuals.Descendants<SecretInput>(window), item => item.IsVisible);
            var password = Assert.IsType<PasswordBox>(secret.Template.FindName("PART_Editor", secret));
            window.Activate();
            Assert.True(password.Focus());
            // 密码编辑器切换分区后还会收到原生焦点及输入法关闭通知，先完成建立焦点的过程再开始连续输入。
            await WindowInteractionNativeMethods.DrainNativeMessagesAsync();
            Assert.True(password.IsKeyboardFocused, WindowInteractionNativeMethods.DescribeFocus(window, password));
            foreach (char character in "s12345")
            {
                TypeCharacter(character);
                await WindowTestVisuals.UpdateLayoutAsync(window);
                Assert.True(password.IsKeyboardFocused, WindowInteractionNativeMethods.DescribeFocus(window, password));
                Assert.Same(password, Keyboard.FocusedElement);
                Assert.Same(window, Window.GetWindow(password));
            }
            Assert.Equal("s12345", password.Password);
            Assert.Equal(password.Password, Assert.IsType<InstanceConfigurationFieldViewModel>(secret.DataContext).Value);
        }
        finally
        {
            window.Close();
            themes.SetTheme(UiTheme.Light);
            CultureInfo.CurrentUICulture = previous;
        }
    });

    /// <summary>更换服务商与分区后仍复用公共集合，并在调整窗口大小后保留新字段的键盘输入。</summary>
    [Theory]
    [InlineData("mysql", "ssh.host")]
    [InlineData("redis", "nodes")]
    [InlineData("ssh", "jump.host")]
    public Task ProviderAndPaneChanges_UseSharedCollectionAndKeepTyping(string type, string key) => fixture.RunAsync(async () =>
    {
        InstanceProviderDescriptor[] descriptors = type switch
        {
            "mysql" => [MySqlProfile.Descriptor(MySqlIds.Direct), MySqlProfile.Descriptor(MySqlIds.Ssh)],
            "redis" => [RedisProfile.Descriptor(RedisIds.Standalone), RedisProfile.Descriptor(RedisIds.Sentinel)],
            _ => [SshProfile.Descriptor(SshIds.Direct), SshProfile.Descriptor(SshIds.Jump)],
        };
        using var wizard = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(
            descriptors.Select(descriptor => (IInstanceProvider)new ProfileProvider(descriptor)).ToArray()),
            new NewInstanceWizardTestConfigurationService());
        wizard.SelectedType = Assert.Single(wizard.Types);
        wizard.SelectedProvider = wizard.Providers[0];
        var window = new NewInstanceWindow(wizard,
            [new MySqlConfigurationContentProvider(), new RedisConfigurationContentProvider(), new SshConfigurationContentProvider()])
        { Opacity = 0, ShowInTaskbar = false };
        try
        {
            InputMethod.SetIsInputMethodEnabled(window, false);
            window.Show();
            window.Activate(); await WindowInteractionNativeMethods.DrainNativeMessagesAsync();
            wizard.NextCommand.Execute(null);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var content = Assert.IsAssignableFrom<FrameworkElement>(
                Assert.Single(WindowTestVisuals.Descendants<InstanceConnectionWizardContent>(window)).InformationContent);
            var adapter = Assert.IsAssignableFrom<InstanceConfigurationEditorViewModel>(content.DataContext);
            var fields = adapter.Fields;
            var genericFields = wizard.ConfigurationEditor.Fields;
            var oldHost = wizard.Fields.Single(field => field.Key == "host");
            wizard.PreviousCommand.Execute(null);
            wizard.SelectedProvider = wizard.Providers[1];
            wizard.NextCommand.Execute(null);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Same(fields, adapter.Fields);
            Assert.Same(genericFields, wizard.ConfigurationEditor.Fields);
            Assert.DoesNotContain(oldHost, fields);
            Assert.DoesNotContain(oldHost, genericFields);

            if (adapter is Asgard.Hermod.MySql.UI.ViewModels.MySqlConnectionEditorViewModel mysql) mysql.Pane = "ssh";
            if (adapter is Asgard.Hermod.Ssh.UI.ViewModels.SshConnectionEditorViewModel ssh) ssh.SelectedPane = "jump";
            window.Width = 720;
            window.Height = 600;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var input = Assert.Single(WindowTestVisuals.Descendants<TextBox>(content), item => item.IsVisible
                && item.DataContext is InstanceConfigurationFieldViewModel field && field.Key == key);
            Assert.True(input.Focus());
            foreach (char character in "10.20.30.40")
            {
                TypeCharacter(character);
                await WindowTestVisuals.UpdateLayoutAsync(window);
                Assert.Same(input, Keyboard.FocusedElement);
                Assert.Same(fields, adapter.Fields);
            }
            Assert.Equal("10.20.30.40", wizard.Fields.Single(field => field.Key == key).Value);
            Assert.True(input.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            Assert.NotSame(input, Keyboard.FocusedElement);
        }
        finally { window.Close(); }
    });

    /// <summary>切换认证或 TLS 仅改变字段可见性，保留选项控件以及新显示字段的输入焦点。</summary>
    [Theory]
    [InlineData("mysql")]
    [InlineData("redis")]
    public Task ConditionalFields_UpdateWithoutReplacingEditors(string type) => fixture.RunAsync(async () =>
    {
        var descriptor = type == "mysql" ? MySqlProfile.Descriptor(MySqlIds.Direct) : RedisProfile.Descriptor(RedisIds.Standalone);
        using var wizard = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(new ProfileProvider(descriptor)),
            new NewInstanceWizardTestConfigurationService());
        wizard.SelectedType = Assert.Single(wizard.Types);
        var window = new NewInstanceWindow(wizard, [new MySqlConfigurationContentProvider(), new RedisConfigurationContentProvider()])
        { Opacity = 0, ShowInTaskbar = false };
        try
        {
            InputMethod.SetIsInputMethodEnabled(window, false);
            window.Show();
            window.Activate(); await WindowInteractionNativeMethods.DrainNativeMessagesAsync();
            wizard.NextCommand.Execute(null);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            object adapter = Assert.Single(WindowTestVisuals.Descendants<InstanceConnectionWizardContent>(window)).InformationContent is FrameworkElement view
                ? view.DataContext : throw new InvalidOperationException();
            if (adapter is Asgard.Hermod.MySql.UI.ViewModels.MySqlConnectionEditorViewModel mysql) mysql.Pane = "security";
            else Assert.IsType<Asgard.Hermod.Redis.UI.ViewModels.RedisConnectionEditorViewModel>(adapter).Pane = "security";
            await WindowTestVisuals.UpdateLayoutAsync(window);

            var ca = FindField(window, "ca");
            Assert.False(ca.IsVisible);
            if (type == "redis") Select("tls", "yes");
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var trust = FindField(window, "trust");
            Assert.True(trust.IsVisible);
            var selector = Assert.Single(WindowTestVisuals.Descendants<SegmentedSelector>(trust));
            selector.SelectedValue = "custom";
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Same(ca, FindField(window, "ca"));
            Assert.True(ca.IsVisible);
            Assert.Same(selector, Assert.Single(WindowTestVisuals.Descendants<SegmentedSelector>(trust)));
            var input = Assert.Single(WindowTestVisuals.Descendants<TextBox>(ca));
            Assert.True(input.Focus());
            foreach (char character in "ca123.pem")
            {
                TypeCharacter(character);
                await WindowTestVisuals.UpdateLayoutAsync(window);
                Assert.Same(input, Keyboard.FocusedElement);
            }
            Assert.Equal("ca123.pem", wizard.Fields.Single(field => field.Key == "ca").Value);
            Select("tls", type == "mysql" ? "required" : "no");
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Same(ca, FindField(window, "ca"));
            Assert.False(ca.IsVisible);
            if (adapter is Asgard.Hermod.MySql.UI.ViewModels.MySqlConnectionEditorViewModel mysqlAdapter)
                Assert.True(mysqlAdapter.HasTlsWarning);

            void Select(string key, string value) => Assert.Single(WindowTestVisuals.Descendants<SegmentedSelector>(FindField(window, key))).SelectedValue = value;
        }
        finally { window.Close(); }
    });

    /// <summary>定位当前分区中的字段容器，同时包含尚未显示的条件字段。</summary>
    private static InstanceConfigurationFieldEditor FindField(Window window, string key)
    {
        var content = Assert.IsAssignableFrom<FrameworkElement>(
            Assert.Single(WindowTestVisuals.Descendants<InstanceConnectionWizardContent>(window)).InformationContent);
        return Assert.Single(WindowTestVisuals.Descendants<InstanceConfigurationFieldEditor>(content),
            editor => editor.DataContext is InstanceConfigurationFieldViewModel field && field.Key == key);
    }

    /// <summary>文字经过 WPF 的正常输入路由，只发给当前测试 Dispatcher 的焦点元素。</summary>
    private static void TypeCharacter(char character) => TextCompositionManager.StartComposition(
        new TextComposition(InputManager.Current, Keyboard.FocusedElement, character.ToString()));

    /// <summary>使用真实协议字段元数据，测试输入时禁止建立网络连接。</summary>
    private sealed class ProfileProvider(InstanceProviderDescriptor descriptor) : IInstanceProvider
    {
        public InstanceProviderDescriptor Descriptor => descriptor;
        public Task<InstanceConnectionTestResult> TestConnectionAsync(InstanceConnectionOptions options,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
