namespace Asgard.Hermod.WinTerminal;

/// <summary>负责键盘、输入法提交与剪贴板交互。</summary>
public sealed partial class TerminalControl
{
    private bool _isComposing;
    private Key _pendingNumericKey;
    private string? _pendingNumericText;

    /// <summary>输入法组合期间，数字键必须继续用于选候选，不能提前发给远端。</summary>
    private void OnCompositionStarted(object sender, TextCompositionEventArgs args)
    {
        // AutoComplete.On 是普通字符的自动组合，不能留下候选锁吞掉后续数字。
        _isComposing = args.TextComposition.AutoComplete == TextCompositionAutoComplete.Off;
        // 普通字符会自动开始并结束组合；真正的候选组合属于新的输入，不沿用上一数字的去重。
        if (args.TextComposition.AutoComplete == TextCompositionAutoComplete.Off)
        {
            _pendingNumericText = null;
            if (_numericKeyStroke is { } stroke) stroke.IsCandidate = true;
        }
    }

    /// <summary>输入上下文结束后清除候选保护和数字去重，避免影响新焦点或新会话。</summary>
    private void ResetInputState()
    {
        _isComposing = false;
        _pendingNumericText = null;
        _numericKeyStroke = null;
        _inputGeneration++;
    }

    /// <summary>将方向键、控制键和 Alt 组合编码为终端输入。</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs args)
    {
        base.OnPreviewKeyDown(args);
        // 去重仅属于上一个 KeyDown；重复按键和长按产生的新 KeyDown 都是独立输入。
        _pendingNumericText = null;
        if (args.Handled || _disposed || _session is not { IsDisposed: false } session)
            return;
        Key key = args.Key == Key.System ? args.SystemKey
            : args.Key == Key.ImeProcessed ? args.ImeProcessedKey : args.Key;
        var numericStroke = _numericKeyStroke is { } current && ReferenceEquals(current.Args, args) ? current : null;
        // TSF 可以先同步提交数字再路由按键；该提交已经发送，不能再走按键兜底。
        if (numericStroke is { HasCommittedText: true } or { IsCandidate: true })
            return;
        if (_isComposing)
        {
            // Enter、箭头和数字都属于输入法候选操作；取消组合不把 Escape 转发给 Shell。
            if (key == Key.Escape) _isComposing = false;
            return;
        }
        // 先让输入法完成处理；没有候选和文本提交的数字在流水线结束后补发。
        if (args.Key == Key.ImeProcessed)
        {
            if (numericStroke is not null) ScheduleNumericFallback(numericStroke);
            return;
        }
        var modifiers = Keyboard.Modifiers;
        bool control = modifiers.HasFlag(ModifierKeys.Control);
        bool shift = modifiers.HasFlag(ModifierKeys.Shift);
        // 保留没有 TextInput 的数字输入路径，并记录同次按键可能随后提交的文本。
        if (!_isComposing && !control && !modifiers.HasFlag(ModifierKeys.Alt)
            && (key is >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.NumPad9)
            && WindowsKeyboard.GetTextWithoutAlt(key, modifiers) is { Length: > 0 } numericText)
        {
            if (!IsReadOnly)
            {
                _pendingNumericKey = key;
                _pendingNumericText = numericText;
                SendText(numericText);
            }
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

    /// <summary>数字键释放后结束去重范围，不把后来的独立文本提交当成旧按键的重复。</summary>
    protected override void OnPreviewKeyUp(KeyEventArgs args)
    {
        base.OnPreviewKeyUp(args);
        Key key = args.Key == Key.System ? args.SystemKey
            : args.Key == Key.ImeProcessed ? args.ImeProcessedKey : args.Key;
        if (_numericKeyStroke is { } stroke && key == stroke.Key)
        {
            CompleteNumericFallback(stroke);
            stroke.IsReleased = true;
        }
        if (key == _pendingNumericKey)
            _pendingNumericText = null;
    }

    /// <summary>使用 TextInput 获取完整 Unicode 文本，包括输入法提交的中文。</summary>
    protected override void OnPreviewTextInput(TextCompositionEventArgs args)
    {
        base.OnPreviewTextInput(args);
    }

    /// <summary>即使输入法已标记隧道事件，也接收最终提交的 Unicode 文本。</summary>
    private void OnPreviewTextInputRouted(object sender, TextCompositionEventArgs args)
    {
        _isComposing = false;
        if (!string.IsNullOrEmpty(args.Text) && _numericKeyStroke is { } stroke) stroke.HasCommittedText = true;
        string? pendingNumericText = _pendingNumericText;
        _pendingNumericText = null;
        // 输入法或父级可能已经标记隧道事件为 handled；终端仍必须消费提交的 Unicode 文本。
        if (_disposed || _session is not { IsDisposed: false } || string.IsNullOrEmpty(args.Text))
            return;
        // 只消费同一次数字按键的完整重复文本；中文、全角字符和多字符提交不会被裁剪。
        if (!string.Equals(pendingNumericText, args.Text, StringComparison.Ordinal))
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
        if (!IsKeyboardFocusWithin)
        {
            DetachKeyboardInput();
            ResetInputState();
            if (ContextMenu?.IsOpen != true) ClearSelection();
        }
        else AttachKeyboardInput();
        if (!IsReadOnly && _session is { IsDisposed: false } session)
        {
            lock (session.Buffer.SyncRoot)
                if (session.Decoder.State.Modes.FocusReporting)
                    session.SendInput(IsKeyboardFocusWithin ? "\x1b[I"u8 : "\x1b[O"u8);
        }
        InvalidateVisual();
    }
}
