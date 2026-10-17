using System.Diagnostics;
using System.Globalization;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>从真实外壳入口打开生产账号编辑器，确保用户可以先选择注册。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class AccountEditorRegistrationTests(WpfTestFixture fixture)
{
    /// <summary>打开账号页及取消注册均不登录；只有明确点击登录才调用身份服务。</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    public Task ShellAccountEntry_AllowsRegistrationBeforeStartingSignIn(string language) => fixture.RunAsync(async () =>
    {
        var previous = CultureInfo.CurrentUICulture;
        var service = new AccountUiTestService();
        var registration = new RegistrationUiTestService();
        var editor = new AccountEditorService(() => null, service, service, registration: registration);
        await using var shell = new ShellViewModel(new TestWorkspaceDataSource(), new FakeInstanceEditor(),
            new FakeTerminalSessionFactory(), new ThemeService(Application.Current.Resources), accounts: service, accountEditor: editor);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var elapsed = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        AccountWindow? account = null;
        AccountRegistrationWindow? prompt = null;
        int phase = 0;
        timer.Tick += (_, _) =>
        {
            try
            {
                Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(8), "账号入口没有完成登录与注册选择。");
                if (phase == 0)
                {
                    account = Application.Current.Windows.OfType<AccountWindow>().SingleOrDefault();
                    if (account is null || !account.IsLoaded) return;
                    account.Opacity = 0;
                    var model = Assert.IsType<AccountViewModel>(account.DataContext);
                    Assert.Equal(0, service.SignIns);
                    Assert.False(model.IsBusy); Assert.False(model.IsSignedIn);
                    Assert.Empty(model.Message);
                    var button = Assert.IsType<Button>(account.FindName("RegisterButton"));
                    Assert.Equal(Visibility.Visible, button.Visibility);
                    Assert.True(button.IsEnabled);
                    phase = 1;
                    // 嵌套模态窗口在独立消息中打开，使计时器继续驱动注册交互。
                    account.Dispatcher.InvokeAsync(() => WindowTestVisuals.Invoke(button));
                }
                else if (phase == 1)
                {
                    prompt = Application.Current.Windows.OfType<AccountRegistrationWindow>().SingleOrDefault();
                    if (prompt is null || !prompt.IsLoaded) return;
                    prompt.Opacity = 0;
                    Assert.Same(account, prompt.Owner);
                    Assert.Equal(0, service.SignIns); Assert.Equal(0, registration.Calls);
                    phase = 2;
                    prompt.Close();
                }
                else if (phase == 2)
                {
                    if (prompt!.IsVisible) return;
                    Assert.Equal(0, service.SignIns); Assert.Equal(0, registration.Calls);
                    Assert.Null(service.Account);
                    phase = 3;
                    account!.Dispatcher.InvokeAsync(() => WindowTestVisuals.Invoke(Assert.IsType<Button>(account.FindName("SignInButton"))));
                }
                else
                {
                    var model = Assert.IsType<AccountViewModel>(account!.DataContext);
                    if (model.IsBusy || service.SignIns == 0) return;
                    Assert.Equal(1, service.SignIns); Assert.NotNull(service.Account);
                    Assert.Equal(0, registration.Calls);
                    timer.Stop(); account.Close(); finished.TrySetResult();
                }
            }
            catch (Exception error)
            {
                timer.Stop(); prompt?.Close(); account?.Close(); finished.TrySetException(error);
            }
        };
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            timer.Start();
            await shell.OpenAccountCommand.ExecuteAsync();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            timer.Stop();
            if (prompt?.IsVisible == true) prompt.Close();
            if (account?.IsVisible == true) account.Close();
            CultureInfo.CurrentUICulture = previous;
        }
    });
}
