namespace Asgard.Hermod.WinTerminal;

/// <summary>负责键盘、输入法提交与剪贴板交互。</summary>
public sealed partial class TerminalControl
{
    private bool _isComposing;

    /// <summary>输入法组合期间，数字键必须继续用于选候选，不能提前发给远端。</summary>
    private void OnCompositionStarted(object sender, TextCompositionEventArgs args) => _isComposing = true;

    /// <summary>将方向键、控制键和 Alt 组合编码为终端输入。</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs args)
    {
        base.OnPreviewKeyDown(args);
        if (args.Handled || _disposed || _session is not { IsDisposed: false } session)
            return;
        Key key = args.Key == Key.System ? args.SystemKey
            : args.Key == Key.ImeProcessed ? args.ImeProcessedKey : args.Key;
        if (_isComposing)
        {
            // Enter、箭头和数字都属于输入法候选操作；取消组合不把 Escape 转发给 Shell。
            if (key == Key.Escape) _isComposing = false;
            return;
        }
        var modifiers = Keyboard.Modifiers;
        bool control = modifiers.HasFlag(ModifierKeys.Control);
        bool shift = modifiers.HasFlag(ModifierKeys.Shift);
        // 数字键在部分输入法下不产生普通 TextInput；按当前布局翻译，并拦截后续文本以免双发。
        if (!_isComposing && !control && !modifiers.HasFlag(ModifierKeys.Alt)
            && (key is >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.NumPad9)
            && WindowsKeyboard.GetTextWithoutAlt(key, modifiers) is { Length: > 0 } numericText)
        {
            SendText(numericText);
            args.Handled = true;
            return;
        }
        if (control && key == Key.C && (shift || _selection is not null))
        {
            CopySelection();
            args.Handled = true;
            return;
        }
        if (control && key == Key.V)
        {
            Paste();
            args.Handled = true;
            return;
        }
        if (control && key == Key.A && shift)
        {
            SelectAll();
            args.Handled = true;
            return;
        }
        string? sequence = null;
        lock (session.Buffer.SyncRoot)
        {
            if (Enum.TryParse(key.ToString(), out TerminalKey terminalKey))
                sequence = KeyboardEncoder.Encode(terminalKey, GetModifiers(), session.Decoder.State.Modes);
        }
        if (sequence is null && control && !Keyboard.IsKeyDown(Key.RightAlt))
        {
            if (key >= Key.A && key <= Key.Z)
                sequence = ((char)(key - Key.A + 1)).ToString();
            else
                sequence = key switch
                {
                    Key.Space or Key.D2 => "\0",
                    Key.OemOpenBrackets => "\x1b",
                    Key.OemPipe => "\x1c",
                    Key.OemCloseBrackets => "\x1d",
                    Key.D6 => "\x1e",
                    Key.OemMinus => "\x1f",
                    _ => null,
                };
        }
        if (sequence is null && modifiers.HasFlag(ModifierKeys.Alt) && !control
            && !Keyboard.IsKeyDown(Key.RightAlt))
        {
            string? text = WindowsKeyboard.GetTextWithoutAlt(key);
            if (!string.IsNullOrEmpty(text))
                sequence = "\x1b" + text;
        }
        if (sequence is not null)
        {
            SendText(sequence);
            args.Handled = true;
        }
    }

    /// <summary>使用 TextInput 获取完整 Unicode 文本，包括输入法提交的中文。</summary>
    protected override void OnPreviewTextInput(TextCompositionEventArgs args)
    {
        base.OnPreviewTextInput(args);
        _isComposing = false;
        if (_session is not { IsDisposed: false } || string.IsNullOrEmpty(args.Text))
            return;
        SendText(args.Text);
        args.Handled = true;
    }

    /// <summary>编码 UTF-8 输入并恢复到实时输出视口。</summary>
    private void SendText(string text)
    {
        if (IsReadOnly || _session is not { IsDisposed: false } session)
            return;
        session.SendInput(Encoding.UTF8.GetBytes(text));
        _scrollOffset = 0;
        _selection = null;
        InvalidateVisual();
    }

    /// <summary>将 WPF 修饰键转换为终端协议修饰键。</summary>
    private static TerminalModifier GetModifiers()
    {
        var modifiers = Keyboard.Modifiers;
        TerminalModifier result = TerminalModifier.None;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= TerminalModifier.Control;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= TerminalModifier.Shift;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= TerminalModifier.Alt;
        return result;
    }

    /// <summary>复制当前选区；剪贴板被其他程序占用时保留选区供重试。</summary>
    public void CopySelection()
    {
        VerifyAccess();
        string text = GetSelectedText();
        if (text.Length == 0)
            return;
        try { Clipboard.SetText(text); }
        catch (ExternalException) { return; }
        _selection = null;
        InvalidateVisual();
    }

    /// <summary>粘贴文本，尊重远端启用的括号粘贴模式。</summary>
    public void Paste()
    {
        VerifyAccess();
        if (_session is not { IsDisposed: false } session)
            return;
        try
        {
            if (!Clipboard.ContainsText())
                return;
            PasteText(Clipboard.GetText());
        }
        catch (ExternalException) { }
    }

    /// <summary>保留原始文本，通过宿主确认后按远端括号粘贴模式发送。</summary>
    public void PasteText(string text)
    {
        VerifyAccess();
        if (IsReadOnly || _session is not { IsDisposed: false } session) return;
        var preview = new TerminalPasteEventArgs(text);
        PreviewPaste?.Invoke(this, preview);
        if (preview.Cancel || IsReadOnly || session.IsDisposed || !ReferenceEquals(_session, session)) return;
        lock (session.Buffer.SyncRoot)
            text = KeyboardEncoder.WrapBracketedPaste(text, session.Decoder.State.Modes.BracketedPaste);
        SendText(text);
    }

    /// <summary>焦点变化后更新光标形状，并按远端模式发送焦点报告。</summary>
    protected override void OnIsKeyboardFocusWithinChanged(DependencyPropertyChangedEventArgs args)
    {
        base.OnIsKeyboardFocusWithinChanged(args);
        if (!IsKeyboardFocusWithin) _isComposing = false;
        if (!IsReadOnly && _session is { IsDisposed: false } session)
        {
            lock (session.Buffer.SyncRoot)
                if (session.Decoder.State.Modes.FocusReporting)
                    session.SendInput(IsKeyboardFocusWithin ? "\x1b[I"u8 : "\x1b[O"u8);
        }
        InvalidateVisual();
    }
}
