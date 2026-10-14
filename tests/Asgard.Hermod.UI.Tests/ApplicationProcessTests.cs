using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;
using Asgard.Hermod.Abstractions.Security;

namespace Asgard.Hermod.UI.Tests;

/// <summary>启动真实发行宿主，隔离 SQLite 与文件缓存，通过 UIA 和目标 HWND 完成端到端验收。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ApplicationProcessTests
{
    /// <summary>真实宿主打开终端，隐藏唤回，拒绝第二份数据库初始化，取消退出后继续运行并正常重启。</summary>
    [Fact]
    public async Task RealApplication_HideRecallCancelExitAndRestart()
    {
        Assert.SkipWhen(Process.GetProcessesByName("Asgard.Hermod").Length > 0, "用户已经运行 Hermod，避免干扰真实应用实例。");
        var storage = new LocalStorageTestEnvironment(); var config = storage.CreateConfiguration();
        using (var services = storage.CreateServices(config))
        {
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            services.GetRequiredService<ILanguagePreferenceStore>().Save("en");
            var encrypted = services.GetRequiredService<ISecretProtector>().Protect(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string>
            { ["host"] = "127.0.0.1", ["port"] = "1", ["user"] = "acceptance", ["auth"] = "password", ["password"] = "local-test-only" }));
            services.GetRequiredService<ILocalConfigurationRepository>().CreateInstance(new("acceptance-saved", "Acceptance database", "mysql", "mysql.direct", encrypted, DateTime.UtcNow, []), TestContext.Current.CancellationToken);
            services.GetRequiredService<IRecentInstanceStore>().Save([new("acceptance-saved", DateTimeOffset.UtcNow)]);
            services.GetRequiredService<IFreeSql>().Dispose();
        }
        using var primary = Start(config.DatabasePath, storage.DirectoryPath);
        bool completed = false;
        try
        {
            nint main = 0;
            await UntilAsync(() => { primary.Refresh(); main = primary.MainWindowHandle; return main != 0 && IsWindowVisible(main); });
            var root = AutomationElement.FromHandle(main);
            Assert.Equal("Asgard Hermod", root.Current.Name);
            await OpenSavedInstanceAsync(primary.Id, root, main);
            AutomationElement? saved = null;
            await UntilAsync(() => (saved = Windows(primary.Id).FirstOrDefault(window => window.Current.Name == "Acceptance database")) is not null);
            nint savedHandle = new(saved!.Current.NativeWindowHandle);
            await OpenSavedInstanceAsync(primary.Id, root, main);
            Assert.Single(Windows(primary.Id), window => window.Current.Name == "Acceptance database");
            await InvokeReadyAsync(primary.Id, () => root.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.NameProperty, "Local terminal"))));
            AutomationElement? work = null;
            await UntilAsync(() => (work = Windows(primary.Id).FirstOrDefault(window => window.Current.Name == "PowerShell")) is not null);
            nint workHandle = new(work!.Current.NativeWindowHandle);
            PostMessage(main, 0x0010, 0, 0);
            await UntilAsync(() => !IsWindowVisible(main)); Assert.True(IsWindowVisible(workHandle));
            Assert.True(IsWindowVisible(savedHandle));
            string duplicateDatabase = Path.Combine(storage.DirectoryPath, "must-not-create", "secondary.db");
            using (var duplicate = Start(duplicateDatabase, storage.DirectoryPath))
            {
                await duplicate.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                Assert.Equal(0, duplicate.ExitCode); Assert.False(File.Exists(duplicateDatabase));
            }
            await UntilAsync(() => IsWindowVisible(main)); Assert.True(IsWindowVisible(workHandle));
            await ExitViaTrayAsync(primary.Id, accept: false);
            Assert.False(primary.HasExited); Assert.True(IsWindowVisible(workHandle));
            await ExitViaTrayAsync(primary.Id, accept: true);
            await primary.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            Assert.Equal(0, primary.ExitCode);
            using var restarted = Start(config.DatabasePath, storage.DirectoryPath);
            try
            {
                await UntilAsync(() => Windows(restarted.Id).Any(window => window.Current.Name == "Asgard Hermod"));
                await ExitViaTrayAsync(restarted.Id, accept: true, hasWork: false);
                await restarted.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                Assert.Equal(0, restarted.ExitCode);
            }
            finally { if (!restarted.HasExited) { restarted.Kill(true); await restarted.WaitForExitAsync(CancellationToken.None); } }
            completed = true;
        }
        finally
        {
            if (!primary.HasExited) { primary.Kill(true); await primary.WaitForExitAsync(CancellationToken.None); }
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            GC.Collect(); GC.WaitForPendingFinalizers();
            try { storage.Dispose(); }
            catch (IOException) when (!completed) { /* 失败时保留隔离目录用于诊断，成功路径必须完整清理。 */ }
        }
    }

    private static async Task OpenSavedInstanceAsync(int processId, AutomationElement root, nint main)
    {
        var row = await WaitReadyAsync(processId, () =>
        {
            var element = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Acceptance database"));
            while (element is not null && element.Current.ControlType != ControlType.ListItem) element = TreeWalker.ControlViewWalker.GetParent(element);
            return element;
        }, SelectionItemPattern.Pattern);
        var selection = (SelectionItemPattern)row.GetCurrentPattern(SelectionItemPattern.Pattern);
        selection.Select(); row.SetFocus();
        await UntilAsync(() => row.Current.HasKeyboardFocus && selection.Current.IsSelected && GetForegroundWindow() == main,
            "保存实例行获得实际焦点");
        Assert.True(PostMessage(main, 0x0100, 0x0D, 0x001C0001));
        Assert.True(PostMessage(main, 0x0101, 0x0D, unchecked((nint)0xC01C0001)));
        // 只发送一次打开动作；等待工作窗口真正激活，复用断言也不会抢在排队的 Enter 之前。
        await UntilAsync(() => Windows(processId).Any(window => window.Current.Name == "Acceptance database"
            && window.Current.NativeWindowHandle == GetForegroundWindow()), "保存实例工作窗口激活");
    }

    private static Process Start(string database, string directory)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "Asgard.Hermod.slnx"))) root = root.Parent!;
        string configuration = AppContext.BaseDirectory.Contains("Release", StringComparison.Ordinal) ? "Release" : "Debug";
        string executable = Path.Combine(root.FullName, "src", "Asgard.Hermod", "bin", configuration, "net10.0-windows", "Asgard.Hermod.exe");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false };
        info.ArgumentList.Add("--database:connectionString=Data Source=" + database + ";Version=3;Pooling=False;");
        info.Environment["HERMOD_TEMPORARY_DIRECTORY"] = Path.Combine(directory, "file-cache");
        return Process.Start(info)!;
    }

    private static async Task ExitViaTrayAsync(int processId, bool accept, bool hasWork = true)
    {
        nint tray = 0;
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out uint pid);
            if (pid == processId)
            {
                var title = new System.Text.StringBuilder(100); GetWindowText(handle, title, title.Capacity);
                if (title.ToString() == "Hermod.Tray") tray = handle;
            }
            return true;
        }, 0);
        Assert.NotEqual(0, tray);
        PostMessage(tray, 0x8001, 0, new nint((1 << 16) | 0x007B));
        await InvokeReadyAsync(processId, () => Windows(processId).FirstOrDefault(window => window.Current.Name == "Hermod tray menu")?
            .FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Exit Hermod")));
        if (!hasWork) return;
        await InvokeReadyAsync(processId, () => Windows(processId).FirstOrDefault(window => window.Current.Name == "Exit Hermod")?
            .FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.NameProperty, accept ? "Exit Hermod" : "Cancel"))));
        if (!accept) await UntilAsync(() => !Windows(processId).Any(window => window.Current.Name == "Exit Hermod"));
    }

    private static AutomationElement[] Windows(int processId)
    {
        var roots = AutomationElement.RootElement.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.ProcessIdProperty, processId)).Cast<AutomationElement>().ToArray();
        // UIA 将 owned 对话框放在其主窗体下，不能只看桌面的第一层子窗口。
        return roots.Concat(roots.SelectMany(root => root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window)).Cast<AutomationElement>())).ToArray();
    }
    /// <summary>窗口出现不代表子控件已进入 UIA 树；每个动作均验证归属进程、可见、启用及所需模式。</summary>
    private static async Task<AutomationElement> WaitReadyAsync(int processId, Func<AutomationElement?> find, AutomationPattern pattern)
    {
        AutomationElement? ready = null;
        await UntilAsync(() =>
        {
            var element = find();
            if (element is null) return false;
            var state = element.Current;
            if (state.ProcessId != processId || !state.IsEnabled || state.IsOffscreen || !element.TryGetCurrentPattern(pattern, out _)) return false;
            ready = element; return true;
        }, "可操作的目标 UIA 元素");
        return ready!;
    }
    private static async Task InvokeReadyAsync(int processId, Func<AutomationElement?> find)
    {
        var element = await WaitReadyAsync(processId, find, InvokePattern.Pattern);
        ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
    }
    private static async Task UntilAsync(Func<bool> predicate, string state = "预期窗口状态")
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            try { if (predicate()) return; }
            catch (ElementNotAvailableException) { /* UIA 提供程序重建时重新查询，绝不重放已发送的动作。 */ }
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(12), "真实 Hermod 进程未在期限内达到：" + state);
            await Task.Delay(75, TestContext.Current.CancellationToken);
        }
    }
    private delegate bool WindowVisitor(nint window, nint state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowVisitor visitor, nint state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, System.Text.StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
