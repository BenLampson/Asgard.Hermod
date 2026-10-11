namespace Asgard.Hermod.UI.Tests;

/// <summary>在真实 ShowDialog 消息循环中执行用例，并确保失败时也能关闭模态窗口。</summary>
internal static class ModalWindowTestInteraction
{
    /// <summary>在 Loaded 后执行交互，超时保护只用于报告故障及回收测试窗口。</summary>
    internal static async Task<bool?> ShowAsync(Window dialog, Func<Task> interaction)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeout = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(8),
        };
        EventHandler timedOut = (_, _) =>
        {
            finished.TrySetException(new TimeoutException("模态窗口没有完成测试交互。"));
            if (dialog.IsVisible)
                dialog.Close();
        };
        RoutedEventHandler loaded = (_, _) => dialog.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await WpfTestFixture.DrainDispatcherAsync();
                await interaction();
                finished.TrySetResult();
            }
            catch (Exception exception)
            {
                finished.TrySetException(exception);
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();
            }
        }, DispatcherPriority.Background);

        dialog.Loaded += loaded;
        timeout.Tick += timedOut;
        try
        {
            timeout.Start();
            bool? result = dialog.ShowDialog();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            return result;
        }
        finally
        {
            timeout.Stop();
            timeout.Tick -= timedOut;
            dialog.Loaded -= loaded;
            if (dialog.IsVisible)
                dialog.Close();
        }
    }
}
