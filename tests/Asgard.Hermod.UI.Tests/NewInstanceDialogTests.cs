using System.Windows.Input;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>通过真实模态消息循环验证 OSS 五步向导的控件绑定、关闭与键盘行为。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class NewInstanceDialogTests
{
    private readonly WpfTestFixture _fixture;

    /// <summary>键盘消息只发给测试窗口的真实 HWND。</summary>
    public NewInstanceDialogTests(WpfTestFixture fixture) => _fixture = fixture;

    /// <summary>回车按步骤推进，缺少凭据或未测试时不创建，成功保存后才返回确认。</summary>
    [Fact]
    public Task ShowDialog_ValidatesTestsAndSavesThroughActualControls() => _fixture.RunAsync(async () =>
    {
        var owner = CreateOwner();
        var provider = new NewInstanceWizardTestProvider();
        var configurations = new NewInstanceWizardTestConfigurationService();
        using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(provider), configurations);
        var dialog = new NewInstanceWindow(model) { Opacity = 0 };
        try
        {
            owner.Show();
            dialog.Owner = owner;
            bool? result = await ModalWindowTestInteraction.ShowAsync(dialog, async () =>
            {
                AssertModalFocus(dialog, owner);
                Assert.Equal(820d, dialog.ActualWidth);
                Assert.Equal(600d, dialog.ActualHeight);
                Assert.Equal(36d, WindowTestVisuals.Caption(dialog).ActualHeight);
                Assert.Equal(56d, WindowTestVisuals.Footer(dialog).ActualHeight);
                var types = Assert.IsAssignableFrom<ListBox>(WindowTestVisuals.Named(dialog, "TypeList"));
                types.SelectedItem = model.Types.Single(type => type.Id == "storage");
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                await PressEnterAsync(dialog);
                Assert.Equal(2, model.CurrentStep);
                var name = Assert.IsType<TextBox>(WindowTestVisuals.Named(dialog, "NameInput"));
                Assert.Same(name, Keyboard.FocusedElement);
                await PressEnterAsync(dialog);
                Assert.Equal(2, model.CurrentStep);
                Assert.NotEmpty(model.NameError);
                Assert.NotEmpty(GetField(model, "accessKeyId").Error);
                Assert.NotEmpty(GetField(model, "accessKeySecret").Error);

                name.Text = "  Modal OSS  ";
                var key = Assert.Single(WindowTestVisuals.Descendants<TextBox>(dialog),
                    input => input.DataContext is InstanceConfigurationFieldViewModel { Key: "accessKeyId" });
                key.Text = "LTAIMODALREGRESSION0000";
                var secret = Assert.Single(WindowTestVisuals.Descendants<SecretInput>(dialog),
                    input => input.DataContext is InstanceConfigurationFieldViewModel { Key: "accessKeySecret" });
                var editor = Assert.IsType<PasswordBox>(secret.Template.FindName("PART_Editor", secret));
                editor.Password = "modal-secret-regression";
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                Assert.Equal("modal-secret-regression", GetField(model, "accessKeySecret").Value);
                Assert.Equal(128, name.MaxLength);
                Assert.Equal(128, key.MaxLength);
                Assert.Equal(256, editor.MaxLength);
                Assert.DoesNotContain(WindowTestVisuals.Descendants<TextBox>(dialog),
                    input => input.DataContext is InstanceConfigurationFieldViewModel { Key: "accessKeySecret" }
                        && (input.IsVisible || input.Text.Length > 0));

                await PressEnterAsync(dialog);
                Assert.Equal(3, model.CurrentStep);
                var continueButton = Assert.IsType<Button>(WindowTestVisuals.Named(dialog, "ContinueButton"));
                Assert.False(continueButton.IsEnabled);
                Assert.Equal(0, configurations.CallCount);
                var test = Assert.IsType<Button>(WindowTestVisuals.Named(dialog, "StartTestButton"));
                WindowTestVisuals.Invoke(test);
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                Assert.Equal(1, provider.CallCount);
                Assert.True(model.HasSuccessfulConnectionTest);
                Assert.True(continueButton.IsEnabled);

                continueButton.Focus();
                await PressEnterAsync(dialog);
                Assert.Equal(4, model.CurrentStep);
                Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(dialog),
                    text => text.Text == model.SaveLocationDescription);
                Assert.True(model.IsLocalSave);
                await PressEnterAsync(dialog);
                Assert.Equal(5, model.CurrentStep);
                Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(dialog),
                    text => text.Text == GetField(model, "accessKeyId").ConfirmationValue);
                Assert.DoesNotContain(WindowTestVisuals.Descendants<TextBlock>(dialog),
                    text => text.Text.Contains(GetField(model, "accessKeySecret").Value, StringComparison.Ordinal));

                await PressEnterAsync(dialog);
                Assert.False(dialog.IsVisible);
            });

            Assert.Equal<bool?>(true, result);
            Assert.True(WindowInteractionNativeMethods.IsEnabled(owner));
            Assert.Equal(1, configurations.CallCount);
            Assert.NotNull(model.CreatedInstance);
            Assert.Equal("Modal OSS", model.CreatedInstance.Name);
            Assert.False(model.SubmitCommand.CanExecute(null));
            Assert.Null(Record.Exception(() => model.CancelCommand.Execute(null)));
        }
        finally
        {
            if (dialog.IsVisible)
                dialog.Close();
            owner.Close();
        }
    });

    /// <summary>Escape、取消和标题栏关闭均恢复拥有者，并使关闭后的模型命令失效。</summary>
    [Theory]
    [InlineData("Escape")]
    [InlineData("Cancel")]
    [InlineData("CaptionClose")]
    public Task ShowDialog_CancellationRestoresOwnerAndDetachesViewModel(string action) => _fixture.RunAsync(async () =>
    {
        var owner = CreateOwner();
        using var model = CreateModel();
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
                    WindowInteractionNativeMethods.PostKey(dialog, 0x1B);
                else if (action == "Cancel")
                    WindowTestVisuals.Invoke(Assert.Single(WindowTestVisuals.Descendants<Button>(dialog),
                        button => ReferenceEquals(button.Command, model.CancelCommand)));
                else
                    WindowTestVisuals.Invoke(FindCaptionClose(dialog));
                await WpfTestFixture.DrainDispatcherAsync();
                Assert.False(dialog.IsVisible);
            });

            Assert.Equal<bool?>(false, result);
            Assert.Equal(1, closedCount);
            Assert.True(WindowInteractionNativeMethods.IsEnabled(owner));
            Assert.False(model.NextCommand.CanExecute(null));
            Assert.False(model.SubmitCommand.CanExecute(null));
            Assert.Null(Record.Exception(() => model.CancelCommand.Execute(null)));
            Assert.Equal(1, closedCount);
        }
        finally
        {
            if (dialog.IsVisible)
                dialog.Close();
            owner.Close();
        }
    });

    /// <summary>从标题栏关闭测试中的向导会传递取消令牌，不能在关闭后保存。</summary>
    [Fact]
    public Task ShowDialog_CaptionCloseCancelsRunningConnectionTest() => _fixture.RunAsync(async () =>
    {
        var completion = new TaskCompletionSource<InstanceConnectionTestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new NewInstanceWizardTestProvider { TestHandler = (_, token) => completion.Task.WaitAsync(TimeSpan.FromSeconds(5), token) };
        var configurations = new NewInstanceWizardTestConfigurationService();
        using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(provider), configurations);
        var owner = CreateOwner();
        var dialog = new NewInstanceWindow(model) { Opacity = 0 };
        Task? running = null;
        try
        {
            owner.Show();
            dialog.Owner = owner;
            bool? result = await ModalWindowTestInteraction.ShowAsync(dialog, async () =>
            {
                NewInstanceWizardTests.MoveToTest(model);
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                running = model.TestConnectionCommand.ExecuteAsync();
                Assert.True(model.IsTesting);
                WindowTestVisuals.Invoke(FindCaptionClose(dialog));
                await WpfTestFixture.DrainDispatcherAsync();
                Assert.False(dialog.IsVisible);
            });
            Assert.NotNull(running);
            await running;
            Assert.Equal<bool?>(false, result);
            Assert.True(provider.LastCancellationToken.IsCancellationRequested);
            Assert.Equal(0, configurations.CallCount);
            Assert.Null(model.CreatedInstance);
        }
        finally
        {
            if (dialog.IsVisible)
                dialog.Close();
            owner.Close();
        }
    });

    /// <summary>保存中标题栏关闭被暂缓，提交成功仍以确认结果返回并恢复拥有者。</summary>
    [Fact]
    public Task ShowDialog_CaptionCloseWaitsForInFlightSave() => _fixture.RunAsync(async () =>
    {
        var completion = new TaskCompletionSource<InstanceSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        var configurations = new NewInstanceWizardTestConfigurationService { CreateHandler = (_, _) => completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken) };
        using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(new NewInstanceWizardTestProvider()), configurations);
        var owner = CreateOwner();
        var dialog = new NewInstanceWindow(model) { Opacity = 0 };
        try
        {
            owner.Show();
            dialog.Owner = owner;
            bool? result = await ModalWindowTestInteraction.ShowAsync(dialog, async () =>
            {
                await NewInstanceWizardTests.ReachConfirmationAsync(model);
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                Task saving = model.SubmitCommand.ExecuteAsync();
                try
                {
                    WindowTestVisuals.Invoke(FindCaptionClose(dialog));
                    await WpfTestFixture.DrainDispatcherAsync();
                    Assert.True(dialog.IsVisible);
                    Assert.True(model.IsSaving);
                    Assert.False(WindowInteractionNativeMethods.IsEnabled(owner));
                }
                finally
                {
                    // 断言失败时同样结束替身事务，避免保护关闭的窗口遗留在模态循环。
                    completion.TrySetResult(new InstanceSummary("modal-saved", "OSS", "OSS", "cloud", []));
                    await saving;
                    await WpfTestFixture.DrainDispatcherAsync();
                }
                Assert.False(dialog.IsVisible);
            });
            Assert.Equal<bool?>(true, result);
            Assert.Equal(1, configurations.CallCount);
            Assert.NotNull(model.CreatedInstance);
            Assert.True(WindowInteractionNativeMethods.IsEnabled(owner));
        }
        finally
        {
            if (!completion.Task.IsCompleted)
                completion.TrySetResult(new InstanceSummary("cleanup", "OSS", "OSS", "cloud", []));
            if (dialog.IsVisible)
                dialog.Close();
            owner.Close();
        }
    });

    /// <summary>验证模态禁用拥有者，第一步获得实际键盘焦点。</summary>
    private static void AssertModalFocus(NewInstanceWindow dialog, HermodWindow owner)
    {
        Assert.Same(owner, dialog.Owner);
        Assert.False(WindowInteractionNativeMethods.IsEnabled(owner));
        var types = Assert.IsAssignableFrom<ListBox>(WindowTestVisuals.Named(dialog, "TypeList"));
        Assert.True(types.IsKeyboardFocusWithin);
    }

    /// <summary>真实控件绑定同样按服务声明查找对应字段。</summary>
    private static InstanceConfigurationFieldViewModel GetField(NewInstanceViewModel model, string key) =>
        NewInstanceWizardTests.GetField(model, key);

    /// <summary>发送真实回车消息，并等待绑定、焦点和布局操作完成。</summary>
    private static async Task PressEnterAsync(NewInstanceWindow dialog)
    {
        WindowInteractionNativeMethods.PostKey(dialog, 0x0D);
        await WindowTestVisuals.UpdateLayoutAsync(dialog);
    }

    /// <summary>通过共用标题栏发出用户实际的关闭命令。</summary>
    private static Button FindCaptionClose(NewInstanceWindow dialog)
    {
        var controls = Assert.Single(WindowTestVisuals.Descendants<WindowControls>(dialog));
        return Assert.Single(WindowTestVisuals.Descendants<Button>(controls),
            button => button.Command == SystemCommands.CloseWindowCommand);
    }

    /// <summary>模态用例共享相同的无网络服务边界。</summary>
    private static NewInstanceViewModel CreateModel() => new(
        new NewInstanceWizardTestRegistry(new NewInstanceWizardTestProvider()), new NewInstanceWizardTestConfigurationService());

    /// <summary>拥有者保留真实 HWND，窗口完全透明且不运行任何终端会话。</summary>
    private static HermodWindow CreateOwner() => new()
    {
        Title = "OSS modal regression owner",
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
