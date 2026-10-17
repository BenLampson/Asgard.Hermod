using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.Services;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;
using Asgard.Hermod.UI.Views.Dialogs;
using Asgard.Hermod.UI.Windowing;

namespace Asgard.Hermod.UI.Tests;

/// <summary>覆盖保留 SSH 和助手窗口时直接退出主窗口的真实清理链。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MainWindowSshExitTests(WpfTestFixture fixture)
{
    /// <summary>可见及隐藏的助手、进行中的模型请求均不妨碍全应用退出。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ExitWithSshAndChatClosesEveryWindow(bool hideChat) => fixture.RunAsync(async () =>
    {
        using var runtime = new AgentModelTestRuntime();
        using var agents = new AgentWindowService();
        var connection = new SshWorkspaceTestConnection();
        var dialogs = new SshTestDialogs { Accept = true };
        var instance = new InstanceItemViewModel(new("ssh-exit", "SSH", "SSH", "terminal", [], TypeId: SshIds.Type, ProviderId: SshIds.Direct));
        var page = new SshWorkspaceViewModel(instance, new SshTestConfigurationService(),
            new SshTestFactory { Handler = _ => Task.FromResult<ISshConnection>(connection) }, dialogs,
            new WindowsSshWorkspaceDialogs(() => DialogOwner.Current), runtime.Client);
        await using var shell = new ShellViewModel(new TestWorkspaceDataSource(), dialogs, new FakeTerminalSessionFactory(),
            new ThemeService(Application.Current.Resources), pageFactory: new Pages(page));
        using var host = new WorkspaceWindowHost(shell.Workspace);
        bool finished = false;
        var main = new Asgard.Hermod.MainWindow(shell, host, finishExit: () =>
        {
            agents.Dispose(); finished = true; return Task.CompletedTask;
        }, closePreferences: new Preferences());
        main.Show(); shell.Workspace.OpenInstance(instance);
        await SshWorkspaceTests.UntilAsync(() => host.Windows.Count == 1);
        var assistant = page.ActiveConsole!.Assistant;
        runtime.Client.Handler = async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Asgard.Hermod.Agent.AgentReply.FromContent("unused");
        };
        agents.Open(new(page.ActiveConsole, assistant, "SSH", "test", "terminal", () => new SshAssistantView(), () => { }, () => assistant.IsSourceClosed));
        var chat = Assert.Single(Application.Current.Windows.OfType<AgentChatWindow>());
        if (hideChat) chat.Close();
        assistant.Prompt = "test pending request";
        Task sending = assistant.SendCommand.ExecuteAsync();
        Assert.True(assistant.IsBusy);
        var errors = new List<string>();
        int choices = 0, confirmations = 0;
        var dismiss = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Normal, (_, _) =>
        {
            try
            {
                foreach (var choice in Application.Current.Windows.OfType<MainWindowCloseDialog>().ToArray())
                {
                    choices++;
                    WindowTestVisuals.Descendants<Button>(choice).Single(button => Equals(button.Content, Asgard.Hermod.Abstractions.Localization.Text.Get("ExitHermod"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                foreach (var confirmation in Application.Current.Windows.OfType<ConfirmationDialog>().ToArray())
                { confirmations++; confirmation.DialogResult = true; }
                foreach (var error in Application.Current.Windows.OfType<MessageDialog>().ToArray())
                {
                    errors.Add(((TextBlock)error.FindName("MessageText")).Text); error.Close();
                }
            }
            catch (Exception error) { errors.Add(error.ToString()); }
        }, Dispatcher.CurrentDispatcher);
        try
        {
            await WindowTestVisuals.UpdateLayoutAsync(main);
            WindowTestVisuals.Invoke(WindowTestVisuals.Descendants<Button>(main).Single(button => button.Name == "CloseButton"));
            await SshWorkspaceTests.UntilAsync(() => finished || errors.Count > 0);
            Assert.Empty(errors);
            await sending;
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.True(finished); Assert.Empty(host.Windows);
            Assert.Equal(1, choices); Assert.Equal(2, confirmations);
            Assert.True(assistant.IsSourceClosed); Assert.Equal(1, connection.DisposeCount);
            Assert.False(main.IsVisible); Assert.DoesNotContain(chat, Application.Current.Windows.Cast<Window>());
        }
        finally { dismiss.Stop(); }
    });

    private sealed class Pages(WorkspacePageViewModel page) : IInstanceWorkspaceFactory
    {
        public WorkspacePageViewModel CreatePage(InstanceItemViewModel instance) => page;
    }

    private sealed class Preferences : IMainWindowClosePreferenceStore
    {
        public event EventHandler? Changed { add { } remove { } }
        public MainWindowClosePreference Load() => new();
        public void Save(MainWindowClosePreference preference) { }
    }
}
