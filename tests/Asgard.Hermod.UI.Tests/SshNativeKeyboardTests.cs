using System.Windows.Input;
using Asgard.Hermod.Ssh.UI.Views;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>从原生窗口消息验证 SSH 页面到协议写入的完整键盘路径。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshNativeKeyboardTests(WpfTestFixture fixture)
{
    /// <summary>布局先于控制台启动时仍须同步 PTY；方向键只发协议字节，不在本机移动光标。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ArrowsReachShellAndInitialGridMatchesViewport(bool applicationMode) => fixture.RunAsync(async () =>
    {
        var factory = new SshTestFactory();
        await using var model = SshWorkspaceTests.Create(factory);
        using var view = new SshWorkspaceView { DataContext = model };
        var window = new HermodWindow { Content = view, Width = 1500, Height = 650 };
        try
        {
            WindowPreviewCapture.Show(window);
            await model.InitializeAsync();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var terminal = Assert.IsType<TerminalControl>(view.FindName("TerminalView"));
            var session = Assert.IsType<Asgard.Hermod.WinTerminal.TerminalSession>(terminal.Session);
            Assert.True(session.Buffer.Columns > 100);
            Assert.Equal((session.Buffer.Columns, session.Buffer.Rows), factory.Connections[0].Size);
            session.FeedOutput(System.Text.Encoding.UTF8.GetBytes(applicationMode ? "\x1b[?1h" : "\x1b[?1l"));
            var before = session.Decoder.CursorPosition;
            Assert.True(terminal.Focus());
            foreach (uint key in new uint[] { 0x26, 0x28, 0x25, 0x27 })
                await WindowInteractionNativeMethods.PressKeyAsync(window, key);
            string prefix = applicationMode ? "\x1bO" : "\x1b[";
            string expected = prefix + "A" + prefix + "B" + prefix + "D" + prefix + "C";
            await SshWorkspaceTests.UntilAsync(() =>
            {
                lock (factory.Connections[0].Inputs)
                    return string.Concat(factory.Connections[0].Inputs).Length >= expected.Length;
            });
            lock (factory.Connections[0].Inputs)
                Assert.Equal(expected, string.Concat(factory.Connections[0].Inputs));
            Assert.Equal(before, session.Decoder.CursorPosition);
            Assert.Null(terminal.FocusVisualStyle);
        }
        finally { window.Close(); }
    });

    /// <summary>真实按键输入命令和重复数字，两组数字键都必须逐字到达 SSH 连接。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task NumericCommandReachesConnectionThroughNativeMessages(bool keypad, bool enableIme) => fixture.RunAsync(async () =>
    {
        var factory = new SshTestFactory();
        await using var model = SshWorkspaceTests.Create(factory);
        using var view = new SshWorkspaceView { DataContext = model };
        var window = new HermodWindow { Content = view, Width = 1000, Height = 650 };
        var previousImeState = InputMethod.Current.ImeState;
        try
        {
            WindowPreviewCapture.Show(window);
            await model.InitializeAsync();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var terminal = Assert.IsType<TerminalControl>(view.FindName("TerminalView"));
            Assert.True(terminal.Focus());
            InputMethod.Current.ImeState = InputMethodState.Off;
            foreach (uint key in new uint[] { 0x54, 0x4F, 0x50, 0x20, 0xBD, 0x44, 0x20 })
                await WindowInteractionNativeMethods.PressKeyAsync(window, key);
            if (enableIme) InputMethod.Current.ImeState = InputMethodState.On;
            await WindowInteractionNativeMethods.PressKeyAsync(window, keypad ? 0x61u : 0x31u);
            await WindowInteractionNativeMethods.PressKeyAsync(window, 0x0D);
            const string digits = "001111223344567899";
            foreach (char digit in digits)
                await WindowInteractionNativeMethods.PressKeyAsync(window, (keypad ? 0x60u : 0x30u) + digit - '0');
            await WindowInteractionNativeMethods.DrainNativeMessagesAsync();
            const string expected = "top -d 1\r" + digits;
            await SshWorkspaceTests.UntilAsync(() =>
            {
                lock (factory.Connections[0].Inputs)
                    return string.Concat(factory.Connections[0].Inputs).Length >= expected.Length;
            });
            lock (factory.Connections[0].Inputs)
                Assert.Equal(expected, string.Concat(factory.Connections[0].Inputs));
        }
        finally
        {
            InputMethod.Current.ImeState = previousImeState;
            window.Close();
        }
    });
}
