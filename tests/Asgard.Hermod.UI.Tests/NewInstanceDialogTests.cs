using System.Windows.Automation;
using System.Windows.Input;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>通过真实模态消息循环验证新建实例表单，不启动本地 Shell 或远程连接。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class NewInstanceDialogTests
{
    private readonly WpfTestFixture _fixture;

    /// <summary>使用独立 STA 应用，键盘消息仅发给测试 HWND。</summary>
    public NewInstanceDialogTests(WpfTestFixture fixture) => _fixture = fixture;

    /// <summary>回车只提交完整表单，本地与远程字段要求均经实际控件绑定验证。</summary>
    [Theory]
    [InlineData(InstanceConnectionType.Local)]
    [InlineData(InstanceConnectionType.SSH)]
    public Task ShowDialog_EnterSubmitsOnlyCompleteForm(InstanceConnectionType connectionType) => _fixture.RunAsync(async () =>
    {
        var owner = CreateOwner();
        var model = new NewInstanceViewModel();
        var dialog = new NewInstanceWindow(model) { Opacity = 0 };
        try
        {
            owner.Show();
            dialog.Owner = owner;
            bool? result = await ModalWindowTestInteraction.ShowAsync(dialog, async () =>
            {
                AssertModalFocus(dialog, owner);
                var create = Assert.Single(WindowTestVisuals.Descendants<Button>(dialog),
                    button => ReferenceEquals(button.Command, model.SubmitCommand));
                Assert.False(create.IsEnabled);
                WindowInteractionNativeMethods.PostKey(dialog, 0x0D);
                await WpfTestFixture.DrainDispatcherAsync();
                Assert.True(dialog.IsVisible);

                var selector = Assert.Single(WindowTestVisuals.Descendants<ComboBox>(dialog));
                selector.SelectedItem = connectionType;
                var name = Assert.IsType<TextBox>(dialog.FindName("NameInput"));
                name.Text = "  Modal instance  ";
                var tag = FindTextBox(dialog, "Instance tag");
                tag.Text = "  regression  ";
                var endpoint = FindTextBox(dialog, "Instance endpoint");
                await WpfTestFixture.DrainDispatcherAsync();

                if (connectionType != InstanceConnectionType.Local)
                {
                    Assert.True(endpoint.IsEnabled);
                    Assert.False(create.IsEnabled);
                    WindowInteractionNativeMethods.PostKey(dialog, 0x0D);
                    await WpfTestFixture.DrainDispatcherAsync();
                    Assert.True(dialog.IsVisible);
                    endpoint.Text = "  host.test:22  ";
                }
                else
                {
                    Assert.False(endpoint.IsEnabled);
                }
                await WpfTestFixture.DrainDispatcherAsync();
                Assert.True(create.IsEnabled);

                WindowInteractionNativeMethods.PostKey(dialog, 0x0D);
                await WpfTestFixture.DrainDispatcherAsync();
                Assert.False(dialog.IsVisible);
            });

            Assert.Equal<bool?>(true, result);
            Assert.True(WindowInteractionNativeMethods.IsEnabled(owner));
            var instance = model.CreateInstance();
            Assert.Equal("Modal instance", instance.Name);
            Assert.Equal(new[] { "regression" }, instance.Tags);
            Assert.Equal(connectionType == InstanceConnectionType.Local, instance.IsLocal);
            Assert.Equal(instance.IsLocal ? "" : "host.test:22", instance.Endpoint);
            Assert.Null(Record.Exception(() => model.CancelCommand.Execute(null)));
        }
        finally
        {
            if (dialog.IsVisible)
                dialog.Close();
            owner.Close();
        }
    });

    /// <summary>Escape、取消按钮和标题栏关闭均结束真正的模态窗口并解除表单订阅。</summary>
    [Theory]
    [InlineData("Escape")]
    [InlineData("Cancel")]
    [InlineData("CaptionClose")]
    public Task ShowDialog_CancellationRestoresOwnerAndDetachesViewModel(string action) => _fixture.RunAsync(async () =>
    {
        var owner = CreateOwner();
        var model = new NewInstanceViewModel();
        var dialog = new NewInstanceWindow(model) { Opacity = 0 };
        int closedCount = 0;
        dialog.Closed += (_, _) => closedCount++;
        try
        {
            owner.Show();
            dialog.Owner = owner;
            bool? result = await ModalWindowTestInteraction.ShowAsync(dialog, async () =>
            {
                AssertModalFocus(dialog, owner);
                if (action == "Escape")
                {
                    WindowInteractionNativeMethods.PostKey(dialog, 0x1B);
                }
                else if (action == "Cancel")
                {
                    var cancel = Assert.Single(WindowTestVisuals.Descendants<Button>(dialog),
                        button => ReferenceEquals(button.Command, model.CancelCommand));
                    WindowTestVisuals.Invoke(cancel);
                }
                else
                {
                    var controls = Assert.Single(WindowTestVisuals.Descendants<WindowControls>(dialog));
                    var close = Assert.Single(WindowTestVisuals.Descendants<Button>(controls),
                        button => button.Command == SystemCommands.CloseWindowCommand);
                    WindowTestVisuals.Invoke(close);
                }
                await WpfTestFixture.DrainDispatcherAsync();
                Assert.False(dialog.IsVisible);
            });

            Assert.Equal<bool?>(false, result);
            Assert.Equal(1, closedCount);
            Assert.True(WindowInteractionNativeMethods.IsEnabled(owner));
            model.Name = "Lifecycle probe";
            model.ConnectionType = InstanceConnectionType.Local;
            Assert.Null(Record.Exception(() => model.SubmitCommand.Execute(null)));
            Assert.Equal(1, closedCount);
        }
        finally
        {
            if (dialog.IsVisible)
                dialog.Close();
            owner.Close();
        }
    });

    /// <summary>验证拥有者被原生禁用，名称输入框获得实际键盘焦点。</summary>
    private static void AssertModalFocus(NewInstanceWindow dialog, HermodWindow owner)
    {
        Assert.Same(owner, dialog.Owner);
        Assert.False(WindowInteractionNativeMethods.IsEnabled(owner));
        var name = Assert.IsType<TextBox>(dialog.FindName("NameInput"));
        Assert.Same(name, Keyboard.FocusedElement);
        Assert.True(name.IsKeyboardFocused);
    }

    /// <summary>按可访问名称找到真实表单字段，避免依赖控件排列顺序。</summary>
    private static TextBox FindTextBox(Window dialog, string name) =>
        Assert.Single(WindowTestVisuals.Descendants<TextBox>(dialog),
            input => AutomationProperties.GetName(input) == name);

    /// <summary>拥有者保留真实 HWND，测试不会显示窗口或创建连接。</summary>
    private static HermodWindow CreateOwner() => new()
    {
        Title = "Modal regression owner",
        Content = new Grid(),
        Width = 640,
        Height = 480,
        Opacity = 0,
        ShowActivated = false,
        ShowInTaskbar = false,
        WindowStartupLocation = WindowStartupLocation.Manual,
        Left = 100,
        Top = 100,
    };
}
