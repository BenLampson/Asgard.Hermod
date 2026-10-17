using System.Diagnostics;
using System.Windows.Automation;

namespace Asgard.Hermod.UI.Tests;

/// <summary>在隔离数据目录启动真实应用，验证按钮切换后退出并重启仍保留主题。</summary>
public sealed partial class ApplicationProcessTests
{
    /// <summary>覆盖浅转深和深转浅，真实宿主必须恢复偏好并更新标题栏的主题操作。</summary>
    [Theory]
    [InlineData(UiTheme.Dark)]
    [InlineData(UiTheme.Light)]
    public async Task RealApplication_ThemeSurvivesExitAndRestart(UiTheme expected)
    {
        Assert.SkipWhen(Process.GetProcessesByName("Asgard.Hermod").Length > 0, "用户已经运行 Hermod，避免干扰真实应用实例。");
        using var storage = new LocalStorageTestEnvironment();
        var configuration = storage.CreateConfiguration();
        using (var services = storage.CreateServices(configuration))
        {
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            services.GetRequiredService<ILanguagePreferenceStore>().Save("en");
            services.GetRequiredService<IThemePreferenceStore>().Save(expected == UiTheme.Dark ? UiTheme.Light : UiTheme.Dark);
        }
        string before = expected == UiTheme.Dark ? "Switch to dark mode" : "Switch to light mode";
        string after = expected == UiTheme.Dark ? "Switch to light mode" : "Switch to dark mode";
        using (var first = Start(configuration.DatabasePath, storage.DirectoryPath))
        {
            try
            {
                await InvokeReadyAsync(first.Id, () => FindThemeButton(first.Id, before));
                await UntilAsync(() => FindThemeButton(first.Id, after) is not null, "主题按钮反映新主题");
                // 应用尚未退出时重新打开 SQLite，确认选择已经落盘。
                using (var services = storage.CreateServices(configuration))
                    Assert.Equal(expected, services.GetRequiredService<IThemePreferenceStore>().Load());
                await ExitViaTrayAsync(first.Id, accept: true, hasWork: false);
                await first.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                Assert.Equal(0, first.ExitCode);
            }
            finally { if (!first.HasExited) { first.Kill(true); await first.WaitForExitAsync(CancellationToken.None); } }
        }
        using var restarted = Start(configuration.DatabasePath, storage.DirectoryPath);
        try
        {
            await WaitReadyAsync(restarted.Id, () => FindThemeButton(restarted.Id, after), InvokePattern.Pattern);
            Assert.Null(FindThemeButton(restarted.Id, before));
            await ExitViaTrayAsync(restarted.Id, accept: true, hasWork: false);
            await restarted.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(0, restarted.ExitCode);
        }
        finally { if (!restarted.HasExited) { restarted.Kill(true); await restarted.WaitForExitAsync(CancellationToken.None); } }
    }

    /// <summary>按进程与无障碍操作名称找到真实主题按钮，不依赖窗口坐标或机器显示语言。</summary>
    private static AutomationElement? FindThemeButton(int processId, string name) => Windows(processId)
        .FirstOrDefault(window => window.Current.Name == "Asgard Hermod")?.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.NameProperty, name)));
}
