namespace Asgard.Hermod.WinTerminal;

/// <summary>每个物理按键独立保留输入法处理结果，排队任务不读取下一按键的状态。</summary>
internal sealed class NumericKeyStroke(KeyEventArgs args, Key key, string text, TerminalSession session, int generation)
{
    /// <summary>关联输入法可能改写的同一个 WPF 按键事件。</summary>
    internal KeyEventArgs Args { get; } = args;
    /// <summary>输入法处理前的数字键标识。</summary>
    internal Key Key { get; } = key;
    /// <summary>按当前键盘布局翻译的兜底文本。</summary>
    internal string Text { get; } = text;
    /// <summary>输入只能发送到原有会话。</summary>
    internal TerminalSession Session { get; } = session;
    /// <summary>焦点和会话上下文的版本。</summary>
    internal int Generation { get; } = generation;
    /// <summary>输入法是否已经提交了非空文本。</summary>
    internal bool HasCommittedText { get; set; }
    /// <summary>数字是否参与过候选操作，组合结束也不能补发。</summary>
    internal bool IsCandidate { get; set; }
    /// <summary>已路由的输入法按键是否需要延后检查。</summary>
    internal bool IsScheduled { get; set; }
    /// <summary>确保 Dispatcher 与下一按键的检查最多执行一次。</summary>
    internal bool IsComplete { get; set; }
    /// <summary>按键释放后不再保留文本去重状态。</summary>
    internal bool IsReleased { get; set; }
}
