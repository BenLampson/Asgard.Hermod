using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实窗口失败后必须关闭、取消异步工作并恢复拥有者，不能遗留禁用的表单。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class InstanceEditorFailureTests(WpfTestFixture fixture)
{
    /// <summary>ShowDialog 初始化抛错也必须销毁已经构造的编辑窗口，再将原异常交给宿主。</summary>
    [Fact]
    public Task EditFailure_ClosesWindowDisposesModelAndRestoresOwner() => fixture.RunAsync(async () =>
    {
        var owner = new HermodWindow { Width = 640, Height = 480, Opacity = 0, ShowInTaskbar = false };
        var content = new WizardFailureTestContentProvider();
        var service = new InstanceEditorService(() => owner, new NewInstanceWizardTestRegistry(new MySqlWizardTestProvider()),
            new WizardFailureTestConfigurationService(), contents: [content]);
        try
        {
            owner.Show();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EditAsync("failure-test"));
            Assert.Equal("Expected wizard initialization failure.", error.Message);
            Assert.True(content.WasClosed);
            Assert.NotNull(content.Window);
            Assert.False(content.Window.IsVisible);
            Assert.DoesNotContain(content.Window, Application.Current.Windows.OfType<Window>());
            Assert.NotNull(content.Model);
            Assert.False(content.Model.CancelCommand.CanExecute(null));
            Assert.False(content.Model.IsInformationEditable);
            Assert.True(WindowInteractionNativeMethods.IsEnabled(owner));
        }
        finally { content.Window?.CloseAfterFailure(); owner.Close(); }
    });

    /// <summary>异常中断正在保存的模态流程时，正常关闭保护不能留下永久锁定的窗口。</summary>
    [Fact]
    public Task ModalExitDuringSave_CancelsWorkAndClosesWindow() => fixture.RunAsync(async () =>
    {
        var owner = new HermodWindow { Width = 640, Height = 480, Opacity = 0, ShowInTaskbar = false };
        var configurations = new NewInstanceWizardTestConfigurationService
        {
            CreateHandler = async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("Unreachable save continuation.");
            },
        };
        using var model = new NewInstanceViewModel(new NewInstanceWizardTestRegistry(new NewInstanceWizardTestProvider()), configurations);
        var dialog = new NewInstanceWindow(model) { Opacity = 0 };
        Task? save = null;
        try
        {
            owner.Show();
            dialog.Owner = owner;
            await ModalWindowTestInteraction.ShowAsync(dialog, async () =>
            {
                model.SelectedType = Assert.Single(model.Types);
                model.NextCommand.Execute(null);
                model.Name = "Save cancellation test";
                model.Fields.Single(item => item.Key == "accessKeyId").Value = "TESTKEY";
                model.Fields.Single(item => item.Key == "accessKeySecret").Value = "test-secret";
                model.NextCommand.Execute(null);
                await model.TestConnectionCommand.ExecuteAsync();
                model.NextCommand.Execute(null);
                model.NextCommand.Execute(null);
                save = model.SubmitCommand.ExecuteAsync();
                Assert.True(model.IsSaving);
                dialog.CloseAfterFailure();
                await WindowTestVisuals.UpdateLayoutAsync(owner);
                Assert.False(dialog.IsVisible);
            });
            Assert.NotNull(save);
            await save;
            Assert.True(configurations.LastCancellationToken.IsCancellationRequested);
            Assert.False(model.IsSaving);
            Assert.True(WindowInteractionNativeMethods.IsEnabled(owner));
            Assert.DoesNotContain(dialog, Application.Current.Windows.OfType<Window>());
        }
        finally { dialog.CloseAfterFailure(); owner.Close(); }
    });
}
