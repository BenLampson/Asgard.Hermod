namespace Asgard.Hermod.WinTerminal;

/// <summary>负责 WPF 网格布局、历史行视口与单元格绘制。</summary>
public sealed partial class TerminalControl
{
    /// <summary>配置会话选项与字体度量。</summary>
    private void ConfigureRenderer()
    {
        if (_disposed || _fontConfigured)
            return;
        if (_session is { IsDisposed: false } session)
        {
            lock (session.Buffer.SyncRoot)
            {
                if (session.IsDisposed)
                    return;
                ConfigureOptions(session.Decoder.Options);
            }
        }
        else ConfigureOptions(new TerminalOptions());
        _fontConfigured = true;
    }

    /// <summary>读取或修改会话颜色时持有缓冲锁，防止和后台 OSC 指令并发。</summary>
    private void ConfigureOptions(TerminalOptions options)
    {
        options.CellHeight = double.IsFinite(LineHeight) && LineHeight > 0 ? LineHeight : null;
        if (Foreground is SolidColorBrush fg)
            options.DefaultForeground = TerminalColor.FromArgb(fg.Color.A, fg.Color.R, fg.Color.G, fg.Color.B);
        if (Background is SolidColorBrush bg)
            options.DefaultBackground = TerminalColor.FromArgb(bg.Color.A, bg.Color.R, bg.Color.G, bg.Color.B);
        _renderer.Configure(FontFamily.Source, FontSize, options);
    }

    /// <summary>合并输出重绘，输出到达时取消过期选区。</summary>
    private void OnRenderTick(object? sender, EventArgs args)
    {
        if (_disposed || Interlocked.Exchange(ref _pendingRender, 0) == 0)
            return;
        if (_session is { } session && session.Revision != _renderedRevision)
        {
            _selection = null;
            _renderedRevision = session.Revision;
        }
        InvalidateVisual();
        ScreenUpdated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>合并窗口变化后发送最新尺寸，持续拖动不会推迟当前计时周期。</summary>
    private void OnResizeTick(object? sender, EventArgs args)
    {
        _resizeTimer.Stop();
        ResizeToBounds();
    }

    /// <summary>将像素尺寸转换为列与行；极小控件保留最小 PTY 网格。</summary>
    private void ResizeToBounds()
    {
        if (_disposed || _session is not { IsDisposed: false } session || !_fontConfigured
            || ActualWidth <= 0 || ActualHeight <= 0)
            return;
        int columns = Math.Clamp((int)(ActualWidth / _renderer.CellWidth), 10, 1000);
        int rows = Math.Clamp((int)(ActualHeight / _renderer.CellHeight), 3, 1000);
        lock (session.Buffer.SyncRoot)
        {
            int before = session.Buffer.ScrollbackCount;
            session.Resize(columns, rows, pushScrollback: PreserveOutputOnResize);
            if (PreserveOutputOnResize && _scrollOffset > 0) _scrollOffset = Math.Max(0, _scrollOffset + session.Buffer.ScrollbackCount - before);
            // 网格调整本身不使同一份历史上的选区失效；后续真实输出仍按版本撤销旧选区。
            if (_selection is not null) _renderedRevision = session.Revision;
        }
    }

    /// <summary>窗口连续缩放时合并远端 PTY 尺寸通知。</summary>
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_disposed)
            return;
        if (!_resizeTimer.IsEnabled)
            _resizeTimer.Start();
    }

    /// <summary>绘制可见历史与屏幕行；和解析器共用缓冲区锁。</summary>
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Background, null, new Rect(RenderSize));
        if (_disposed || _session is not { IsDisposed: false } session)
            return;
        ConfigureRenderer();
        lock (session.Buffer.SyncRoot)
        {
            if (session.IsDisposed)
                return;
            var buffer = session.Buffer;
            var colors = session.Decoder.Options;
            drawingContext.DrawRectangle(new SolidColorBrush(ToColor(colors.DefaultBackground)), null, new Rect(RenderSize));
            int historyCount = buffer.IsAlternate ? 0 : buffer.ScrollbackCount;
            _scrollOffset = Math.Clamp(_scrollOffset, 0, historyCount);
            int start = historyCount - _scrollOffset;
            var history = buffer.GetScrollback();
            float pixelsPerDip = (float)VisualTreeHelper.GetDpi(this).PixelsPerDip;
            for (int y = 0; y < buffer.Rows; y++)
            {
                int index = start + y;
                Span<TerminalCellInfo> row = index < historyCount
                    ? history[index].AsSpan() : buffer.GetRow(index - historyCount);
                drawingContext.PushTransform(new TranslateTransform(0, y * _renderer.CellHeight));
                _renderer.RenderRow(drawingContext, row, _selection, index, pixelsPerDip);
                drawingContext.Pop();
            }
            var state = session.Decoder.State;
            if (_scrollOffset == 0 && state.Modes.CursorVisible)
            {
                double x = state.CursorX * _renderer.CellWidth;
                double y = state.CursorY * _renderer.CellHeight;
                var rect = new Rect(x, y, _renderer.CellWidth, _renderer.CellHeight);
                var pen = new Pen(Foreground, 1);
                if (IsKeyboardFocusWithin)
                    drawingContext.DrawRectangle(Foreground, null, new Rect(x, y, 2, rect.Height));
                else
                    drawingContext.DrawRectangle(null, pen, rect);
            }
        }
    }

    /// <summary>转换终端 ARGB 颜色为 WPF 颜色。</summary>
    private static System.Windows.Media.Color ToColor(TerminalColor color) => System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B);
}
