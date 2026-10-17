namespace Asgard.Hermod.WinTerminal;

/// <summary>在输入法处理之前记录数字按键，协调先提交文本和缺少提交的两种路径。</summary>
public sealed partial class TerminalControl
{
    private InputManager? _keyboardInputManager;
    private NumericKeyStroke? _numericKeyStroke;
    private int _inputGeneration;

    /// <summary>仅在拥有键盘焦点时订阅，避免 Dispatcher 的输入管理器长期持有已关闭的控件。</summary>
    private void AttachKeyboardInput()
    {
        if (_disposed || !IsKeyboardFocusWithin || _keyboardInputManager is not null) return;
        _keyboardInputManager = InputManager.Current;
        _keyboardInputManager.PreProcessInput += OnPreProcessKeyboardInput;
    }

    /// <summary>焦点或可视树生命周期结束时释放全局输入订阅。</summary>
    private void DetachKeyboardInput()
    {
        if (_keyboardInputManager is not { } manager) return;
        manager.PreProcessInput -= OnPreProcessKeyboardInput;
        _keyboardInputManager = null;
    }

    /// <summary>此阶段早于 TSF：输入法可能在 PreviewKeyDown 路由前同步提交字符。</summary>
    private void OnPreProcessKeyboardInput(object sender, PreProcessInputEventArgs args)
    {
        if (!IsKeyboardFocusWithin || args.StagingItem.Input is not KeyEventArgs keyArgs
            || keyArgs.RoutedEvent != Keyboard.PreviewKeyDownEvent) return;
        // 下一按键开始前先落实上一按键的兜底，不能让后台调度把数字排到后续字母之后。
        CompleteNumericFallback(_numericKeyStroke);
        _numericKeyStroke = null;
        _pendingNumericText = null;
        Key key = keyArgs.Key == Key.ImeProcessed ? keyArgs.ImeProcessedKey : keyArgs.Key;
        var modifiers = Keyboard.Modifiers;
        if (_disposed || IsReadOnly || _session is not { IsDisposed: false } session
            || modifiers.HasFlag(ModifierKeys.Control) || modifiers.HasFlag(ModifierKeys.Alt)
            || key is not (>= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.NumPad9)
            || WindowsKeyboard.GetTextWithoutAlt(key, modifiers) is not { Length: > 0 } text) return;
        _numericKeyStroke = new(keyArgs, key, text, session, _inputGeneration) { IsCandidate = _isComposing };
    }

    /// <summary>等待当前输入流水线及输入法回调结束，再处理没有对应文本提交的数字。</summary>
    private void ScheduleNumericFallback(NumericKeyStroke stroke)
    {
        stroke.IsScheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => CompleteNumericFallback(stroke));
    }

    /// <summary>只补发确实没有提交且没有参与候选的数字，断线、失焦和换会话使旧任务失效。</summary>
    private void CompleteNumericFallback(NumericKeyStroke? stroke)
    {
        if (stroke is not { IsScheduled: true, IsComplete: false }) return;
        stroke.IsComplete = true;
        if (stroke.HasCommittedText || stroke.IsCandidate || stroke.Generation != _inputGeneration
            || _disposed || IsReadOnly || !IsKeyboardFocusWithin || !ReferenceEquals(_session, stroke.Session)
            || stroke.Session.IsDisposed) return;
        if (!stroke.IsReleased)
        {
            _pendingNumericKey = stroke.Key;
            _pendingNumericText = stroke.Text;
        }
        SendText(stroke.Text);
    }
}
