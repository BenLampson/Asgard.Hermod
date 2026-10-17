namespace Asgard.Hermod.UI.Controls;

/// <summary>捕获请求时的文稿、光标和选区，异步返回后由编辑器核对快照。</summary>
public sealed record CodeCompletionRequest(string Text, int Caret, int SelectionStart, int SelectionLength);
