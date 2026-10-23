using System.Windows.Threading;
using Asgard.Hermod.Ssh.UI.Views;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证窗口和侧栏改变可用空间时，当前控制台持续同步远端 PTY 网格。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshTerminalResizeTests(WpfTestFixture fixture)
{
    /// <summary>连续放大、缩小和展开侧栏后，本机缓冲区与远端都采用最新行列数。</summary>
    [Fact]
    public Task WindowAndSidebarChangesKeepRemoteGridInSync() => fixture.RunAsync(async () =>
    {
        var factory = new SshTestFactory();
        await using var model = SshWorkspaceTests.Create(factory);
        using var view = new SshWorkspaceView { DataContext = model };
        var window = new HermodWindow { Content = view, Width = 1200, Height = 650 };
        try
        {
            WindowPreviewCapture.Show(window);
            await model.InitializeAsync();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var session = model.Session!;
            foreach (var size in new[] { (Width: 1500, Height: 750), (Width: 1000, Height: 550), (Width: 1400, Height: 700) })
            {
                var before = (session.Buffer.Columns, session.Buffer.Rows);
                window.Width = size.Width; window.Height = size.Height;
                await WindowTestVisuals.UpdateLayoutAsync(window);
                await SshWorkspaceTests.UntilAsync(() => Grid() != before && Grid() == factory.Connections[0].Size);
            }
            var expanded = Grid();
            model.IsDetailsOpen = true;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            await SshWorkspaceTests.UntilAsync(() => Grid().Columns < expanded.Columns && Grid() == factory.Connections[0].Size);
            model.IsDetailsOpen = false;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            await SshWorkspaceTests.UntilAsync(() => Grid() == expanded && Grid() == factory.Connections[0].Size);

            (int Columns, int Rows) Grid() => (session.Buffer.Columns, session.Buffer.Rows);
        }
        finally { window.Close(); }
    });

    /// <summary>持续拖动期间也要更新 PTY，不能等到最后一次尺寸变化后才发送。</summary>
    [Fact]
    public Task ContinuousResizeSendsMultipleUpdatesBeforeStopping() => fixture.RunAsync(async () =>
    {
        var factory = new SshTestFactory();
        await using var model = SshWorkspaceTests.Create(factory);
        using var view = new SshWorkspaceView { DataContext = model };
        var window = new HermodWindow { Content = view, Width = 1500, Height = 750 };
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(20) };
        try
        {
            WindowPreviewCapture.Show(window);
            await model.InitializeAsync();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int frames = 0, updatesDuringDrag = 0, lastAppliedFrame = 0;
            model.Session!.SizeChanged += (_, _) =>
            {
                lastAppliedFrame = frames;
                if (frames is > 0 and < 30) updatesDuringDrag++;
            };
            timer.Tick += (_, _) =>
            {
                window.Width -= 15;
                window.Height -= 3;
                window.UpdateLayout();
                if (++frames == 30) { timer.Stop(); completed.TrySetResult(); }
            };
            timer.Start();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(updatesDuringDrag >= 2, $"Expected multiple PTY updates during dragging, received {updatesDuringDrag}.");
            await SshWorkspaceTests.UntilAsync(() => lastAppliedFrame == 30
                && factory.Connections[0].Size == (model.Session.Buffer.Columns, model.Session.Buffer.Rows));
        }
        finally { timer.Stop(); window.Close(); }
    });
}
