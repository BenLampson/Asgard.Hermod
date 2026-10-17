using System.Globalization;
using System.Windows.Input;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.Services;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>通过真实公共控件验证 SSH 分区、认证选项及键盘选择不会丢失字段状态。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshSharedControlsTests(WpfTestFixture fixture)
{
    /// <summary>两种双节点路径均使用同一个分段控件，并沿稳定 ID 切换字段引用。</summary>
    [Theory]
    [InlineData("en", SshIds.Jump, "jump")]
    [InlineData("zh-Hans", SshIds.Jump, "jump")]
    [InlineData("en", SshIds.Proxy, "proxy")]
    [InlineData("zh-Hans", SshIds.Proxy, "proxy")]
    public Task PaneSelectionPreservesConnectionDraft(string language, string route, string pane) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        var previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        Text.Initialize(language, CultureInfo.InvariantCulture);
        using var model = SshWizardTests.Create(new(), new(), route);
        var window = new NewInstanceWindow(model, [new SshConfigurationContentProvider()]);
        try
        {
            WindowPreviewCapture.Show(window);
            model.NextCommand.Execute(null);
            SshWizardTests.Fill(model, route);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var editor = Assert.Single(WindowTestVisuals.Descendants<SshConnectionEditor>(window));
            var adapter = Assert.IsType<SshConnectionEditorViewModel>(editor.DataContext);
            var selector = Assert.IsType<SegmentedSelector>(editor.FindName("ConnectionPaneSelector"));
            Assert.Equal(2, selector.Items.Count);
            Assert.Equal("target", selector.SelectedValue);
            Assert.Empty(WindowTestVisuals.Descendants<RadioButton>(editor));
            var host = adapter.Host;
            var password = adapter.Password;
            string originalPassword = password.Value;
            string originalHost = host.Value;
            var paneOptions = selector.ItemsSource;

            selector.SelectedValue = pane;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(pane, adapter.SelectedPane);
            Assert.Same(paneOptions, selector.ItemsSource);
            Assert.Equal(pane == "jump", adapter.IsJumpPane);
            Assert.Equal(pane == "proxy", adapter.IsProxyPane);
            selector.SelectedValue = "target";
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Same(host, adapter.Host);
            Assert.Same(password, adapter.Password);
            Assert.Equal(originalHost, adapter.Host.Value);
            Assert.Equal(originalPassword, adapter.Password.Value);

            var authentication = Assert.Single(WindowTestVisuals.Descendants<SegmentedSelector>(editor),
                item => item.IsVisible && item.Items.Count == 3);
            authentication.SelectedValue = SshIds.Agent;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(SshIds.Agent, adapter.Authentication.Value);
            Assert.True(adapter.UsesAgent);
            Assert.DoesNotContain(WindowTestVisuals.Descendants<SecretInput>(editor), input => input.IsVisible);
        }
        finally
        {
            window.Close();
            CultureInfo.CurrentUICulture = previous;
            CultureInfo.DefaultThreadCurrentUICulture = previousDefault;
        }
    });

    /// <summary>公共分段选择器沿 ListBox 的真实键盘入口更新稳定值，而不是依赖鼠标或翻译文字。</summary>
    [Fact]
    public Task SegmentedSelectorSupportsKeyboardSelection() => fixture.RunAsync(async () =>
    {
        var selector = new SegmentedSelector
        {
            ItemsSource = new[] { new InstanceFieldChoice("first", "First"), new InstanceFieldChoice("second", "Second") },
            DisplayMemberPath = "Name",
            SelectedValuePath = "Id",
            SelectedValue = "first",
        };
        var window = new HermodDialog { Width = 600, Height = 240, Content = selector };
        try
        {
            WindowPreviewCapture.Show(window);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var first = Assert.IsType<ListBoxItem>(selector.ItemContainerGenerator.ContainerFromIndex(0));
            first.Focus();
            var source = Assert.IsAssignableFrom<PresentationSource>(PresentationSource.FromVisual(first));
            first.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.End)
            { RoutedEvent = Keyboard.KeyDownEvent });
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal("second", selector.SelectedValue);
            Assert.Empty(WindowTestVisuals.Descendants<ScrollViewer>(selector));
        }
        finally { window.Close(); }
    });
}
