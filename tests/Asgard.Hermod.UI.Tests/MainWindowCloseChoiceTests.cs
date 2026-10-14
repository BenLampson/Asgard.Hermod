using System.Globalization;
using Asgard.Hermod.UI.Views.Dialogs;
using Asgard.Hermod.UI.Windowing;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实关闭请求、模态选择、SQLite 记忆和通用设置联合验收。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MainWindowCloseChoiceTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task CloseChoice_RemembersOnlyExplicitSelectionAndSettingsApplyImmediately(string language, UiTheme theme) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        using var storage = new LocalStorageTestEnvironment(); using var services = storage.CreateServices(); services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var preferences = services.GetRequiredService<IMainWindowClosePreferenceStore>();
        await using var environment = new ShellTestEnvironment(); environment.Themes.SetTheme(theme);
        using var host = new WorkspaceWindowHost(environment.Shell.Workspace); var tray = new CloseTestTray(); bool exitApproved = false;
        var main = new Asgard.Hermod.MainWindow(environment.Shell, host, tray, confirmExit: _ => Task.FromResult(exitApproved), closePreferences: preferences);
        try
        {
            main.Show(); environment.Shell.Workspace.OpenLocalTerminal(); var work = Assert.Single(host.Windows);
            await WindowTestVisuals.UpdateLayoutAsync(main);
            WindowTestVisuals.Invoke(WindowTestVisuals.Descendants<Button>(main).Single(button => button.Name == "CloseButton")); var question = await PromptAsync();
            Assert.InRange(question.ActualHeight, 200, 330);
            await WindowPreviewCapture.VerifyAsync(question, $"close-choice-{language}-{theme}", actionBarHeight: 60);
            Assert.Single(WindowTestVisuals.Descendants<CheckBox>(question)).IsChecked = true;
            await WindowInteractionNativeMethods.PressKeyAsync(question, 0x1B); await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal(new(), preferences.Load()); Assert.True(main.IsVisible); Assert.True(work.IsVisible);
            var cancelled = question;
            WindowInteractionNativeMethods.PostSystemClose(main); question = await PromptAsync(); Assert.NotSame(cancelled, question);
            Assert.False(Assert.Single(WindowTestVisuals.Descendants<CheckBox>(question)).IsChecked == true);
            Click(question, "HideMainWindowToTray"); await UntilAsync(() => !main.IsVisible);
            Assert.True(preferences.Load().AskBeforeClosing, "未勾选记忆的隐藏动作不能关闭询问。");
            Assert.True(work.IsVisible, $"隐藏主窗体不能隐藏工作窗口：Owner={work.Owner}; Closed={work.Page.IsClosed}");
            Assert.Equal(0, environment.Factory.Sessions[0].DisposeCalls);
            main.ActivateMain(); main.Close(); question = await PromptAsync();
            Assert.Single(WindowTestVisuals.Descendants<CheckBox>(question)).IsChecked = true; Click(question, "HideMainWindowToTray"); await UntilAsync(() => !main.IsVisible);
            Assert.Equal(new(false), preferences.Load());
            main.ActivateMain(); main.Close(); await UntilAsync(() => !main.IsVisible);
            Assert.Empty(Application.Current.Windows.OfType<MainWindowCloseDialog>()); Assert.True(work.IsVisible);
            main.ActivateMain();
            using var settings = new SettingsViewModel(new TagManagementTestService(), new TagDialogTestService(), services.GetRequiredService<ILanguagePreferenceStore>(), closePreferences: preferences);
            var settingWindow = new SettingsWindow(settings) { Owner = main };
            try
            {
                WindowPreviewCapture.Show(settingWindow); await WindowTestVisuals.UpdateLayoutAsync(settingWindow);
                var toggle = WindowTestVisuals.Descendants<CheckBox>(settingWindow).Single(control => Equals(control.Content, Text.Get("MainCloseAskSetting")));
                toggle.IsChecked = true; Assert.True(preferences.Load().AskBeforeClosing);
                Assert.Equal(Text.Get("CloseMainWindow"), WindowCloseBehavior.GetCloseActionLabel(main));
                var action = WindowTestVisuals.Descendants<ComboBox>(settingWindow).Single(control => System.Windows.Automation.AutomationProperties.GetName(control) == Text.Get("MainCloseDefaultAction"));
                action.SelectedItem = settings.CloseSettings!.Actions.Single(item => item.Action == MainWindowCloseAction.ExitApplication); toggle.IsChecked = false;
                Assert.Equal(new(false, MainWindowCloseAction.ExitApplication), preferences.Load());
                Assert.Equal(Text.Get("ExitHermod"), WindowCloseBehavior.GetCloseActionLabel(main));
                var close = WindowTestVisuals.Descendants<Button>(main).Single(button => button.Name == "CloseButton");
                await WindowTestVisuals.UpdateLayoutAsync(main); Assert.Equal(Text.Get("ExitHermod"), System.Windows.Automation.AutomationProperties.GetName(close));
                await WindowPreviewCapture.VerifyAsync(settingWindow, $"close-general-settings-{language}-{theme}");
            }
            finally { settingWindow.Close(); }
            main.Close(); await WpfTestFixture.DrainDispatcherAsync();
            Assert.False(main.IsExitCommitted); Assert.True(main.IsVisible); Assert.True(work.IsVisible);
            exitApproved = true; main.Close(); await UntilAsync(() => main.IsExitCommitted && !main.IsVisible);
            Assert.True(work.Page.IsClosed); Assert.False(work.IsVisible); Assert.Equal(1, environment.Factory.Sessions[0].DisposeCalls);
        }
        finally { await main.RequestExitAsync(sessionEnding: true); environment.Themes.SetTheme(UiTheme.Light); Text.SetApplicationCulture(null); }
    });

    [Fact]
    public Task TrayFailure_RepeatedCloseAndSystemEndingDoNotStackOrSave() => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo("en"));
        using var storage = new LocalStorageTestEnvironment(); using var services = storage.CreateServices(); services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var preferences = services.GetRequiredService<IMainWindowClosePreferenceStore>();
        await using var environment = new ShellTestEnvironment(); using var host = new WorkspaceWindowHost(environment.Shell.Workspace); var tray = new CloseTestTray();
        var main = new Asgard.Hermod.MainWindow(environment.Shell, host, tray, closePreferences: preferences);
        try
        {
            main.Show(); environment.Shell.Workspace.OpenLocalTerminal(); var work = Assert.Single(host.Windows);
            main.Close(); var question = await PromptAsync(); main.Close(); await WpfTestFixture.DrainDispatcherAsync();
            Assert.Single(Application.Current.Windows.OfType<MainWindowCloseDialog>());
            tray.SetAvailable(false); Assert.False(Button(question, "HideMainWindowToTray").IsEnabled);
            await WindowPreviewCapture.VerifyAsync(question, "close-choice-tray-unavailable-en-Light", actionBarHeight: 60);
            Assert.Contains(WindowTestVisuals.Descendants<TextBlock>(question), text => text.IsVisible && text.Text == Text.Get("MainCloseTrayUnavailable"));
            Assert.True(main.IsVisible); Assert.True(work.IsVisible);
            Assert.Single(WindowTestVisuals.Descendants<CheckBox>(question)).IsChecked = true;
            WindowTestVisuals.Invoke(WindowTestVisuals.Descendants<Button>(question).Single(button => button.Name == "CloseButton"));
            await WpfTestFixture.DrainDispatcherAsync(); Assert.Equal(new(), preferences.Load());
            preferences.Save(new(false));
            main.Close(); await PromptAsync(); Assert.True(await main.RequestExitAsync(sessionEnding: true));
            Assert.Empty(Application.Current.Windows.OfType<MainWindowCloseDialog>()); Assert.False(work.IsVisible); Assert.Equal(new(false), preferences.Load());
        }
        finally { await main.RequestExitAsync(sessionEnding: true); Text.SetApplicationCulture(null); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ExitChoice_StillHonorsWorkPreflightCancellation(bool remember) => fixture.RunAsync(async () =>
    {
        using var storage = new LocalStorageTestEnvironment(); using var services = storage.CreateServices(); services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var preferences = services.GetRequiredService<IMainWindowClosePreferenceStore>();
        await using var environment = new ShellTestEnvironment(); using var host = new WorkspaceWindowHost(environment.Shell.Workspace); int preflights = 0;
        var main = new Asgard.Hermod.MainWindow(environment.Shell, host, new CloseTestTray(),
            confirmExit: _ => { preflights++; return Task.FromResult(false); }, closePreferences: preferences);
        try
        {
            main.Show(); environment.Shell.Workspace.OpenLocalTerminal(); var work = Assert.Single(host.Windows);
            main.Close(); var question = await PromptAsync();
            Assert.Single(WindowTestVisuals.Descendants<CheckBox>(question)).IsChecked = remember; Click(question, "ExitHermod");
            await UntilAsync(() => preflights == 1); await WpfTestFixture.DrainDispatcherAsync();
            Assert.True(main.IsVisible); Assert.True(work.IsVisible); Assert.False(work.Page.IsClosed); Assert.False(main.IsExitCommitted);
            Assert.Equal(0, environment.Factory.Sessions[0].DisposeCalls);
            Assert.Equal(remember ? new(false, MainWindowCloseAction.ExitApplication) : new(), preferences.Load());
        }
        finally { await main.RequestExitAsync(sessionEnding: true); }
    });

    private static Button Button(MainWindowCloseDialog dialog, string key) => WindowTestVisuals.Descendants<Button>(dialog).Single(button => Equals(button.Content, Text.Get(key)));
    private static void Click(MainWindowCloseDialog dialog, string key) => Button(dialog, key).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    private static async Task<MainWindowCloseDialog> PromptAsync()
    {
        await UntilAsync(() => Application.Current.Windows.OfType<MainWindowCloseDialog>().Any(dialog => dialog.IsVisible));
        var dialog = Application.Current.Windows.OfType<MainWindowCloseDialog>().Single(); await WindowTestVisuals.UpdateLayoutAsync(dialog); return dialog;
    }
    private static async Task UntilAsync(Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition()) { Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4), "主窗体关闭流程未达到预期状态。"); await Task.Delay(10, TestContext.Current.CancellationToken); }
    }
    private sealed class CloseTestTray : ITrayIcon
    {
        public bool IsAvailable { get; private set; } = true;
        public event Action? Activated { add { } remove { } }
        public event Action? MenuRequested { add { } remove { } }
        public event Action? AvailabilityChanged;
        public bool TryGetBounds(out Rect bounds) { bounds = new(10, 10, 20, 20); return true; }
        public void ShowHiddenHint() { }
        public void Dispose() => IsAvailable = false;
        internal void SetAvailable(bool available) { IsAvailable = available; AvailabilityChanged?.Invoke(); }
    }
}
