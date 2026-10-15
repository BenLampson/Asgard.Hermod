namespace Asgard.Hermod.UI.Controls;

public partial class CodeEditor
{
    private CancellationTokenSource? _completionRequest;
    private bool _applyingCompletion;
    /// <summary>可选的模块补全器；只读编辑器不请求候选。</summary>
    public static readonly DependencyProperty CompletionProviderProperty = DependencyProperty.Register(nameof(CompletionProvider), typeof(ICodeCompletionProvider), typeof(CodeEditor));
    /// <summary>模块提供的补全器，不改变其它代码预览控件。</summary>
    public ICodeCompletionProvider? CompletionProvider { get => (ICodeCompletionProvider?)GetValue(CompletionProviderProperty); set => SetValue(CompletionProviderProperty, value); }
    private async void OnCompletionKeyDown(object sender, KeyEventArgs args)
    {
        if (IsReadOnly || CompletionProvider is null) return;
        if (args.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        { args.Handled = true; await RequestCompletionAsync(false); return; }
        if (!CompletionPopup.IsOpen) return;
        if (args.Key == Key.Escape) { args.Handled = true; CloseCompletion(); }
        else if (args.Key is Key.Up or Key.Down)
        {
            args.Handled = true;
            CompletionList.SelectedIndex = Math.Clamp(CompletionList.SelectedIndex + (args.Key == Key.Up ? -1 : 1), 0, CompletionList.Items.Count - 1);
            CompletionList.ScrollIntoView(CompletionList.SelectedItem);
        }
        else if (args.Key is Key.Tab or Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        { args.Handled = true; AcceptCompletion(); }
        else if (args.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown) CloseCompletion();
    }
    private async void OnCompletionTextChanged()
    {
        if (_applyingCompletion || IsReadOnly || CompletionProvider is null || !Input.IsKeyboardFocusWithin) return;
        int caret = Input.CaretIndex;
        if (caret == 0 || !(char.IsLetterOrDigit(Input.Text[caret - 1]) || Input.Text[caret - 1] is '_' or '.' or '`')) { CloseCompletion(); return; }
        await RequestCompletionAsync(true);
    }
    private async Task RequestCompletionAsync(bool delay)
    {
        _completionRequest?.Cancel();
        CompletionPopup.IsOpen = false;
        using var cancellation = new CancellationTokenSource(); _completionRequest = cancellation;
        var request = new CodeCompletionRequest(Input.Text, Input.CaretIndex, Input.SelectionStart, Input.SelectionLength);
        try
        {
            if (delay) await Task.Delay(180, cancellation.Token);
            if (CompletionProvider is not { } provider || IsReadOnly) return;
            var items = await provider.CompleteAsync(request, cancellation.Token);
            if (cancellation.IsCancellationRequested || Input.Text != request.Text || Input.CaretIndex != request.Caret || !Input.IsKeyboardFocusWithin) return;
            CompletionList.ItemsSource = items; CompletionList.SelectedIndex = items.Count > 0 ? 0 : -1;
            var rect = Input.GetRectFromCharacterIndex(Input.CaretIndex);
            CompletionPopup.PlacementRectangle = rect.IsEmpty ? new Rect(0, 24, 1, 1) : rect;
            CompletionPopup.IsOpen = items.Count > 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception) { CompletionPopup.IsOpen = false; /* 补全失败不打断文稿编辑，也不展示提供方原始异常。 */ }
        finally { if (ReferenceEquals(_completionRequest, cancellation)) _completionRequest = null; }
    }
    private void AcceptCompletion()
    {
        if (CompletionList.SelectedItem is not CodeCompletionItem item) return;
        CloseCompletion();
        if (item.Start < 0 || item.Length < 0 || item.Start + item.Length > Input.Text.Length) return;
        _applyingCompletion = true;
        try { Input.Select(item.Start, item.Length); Input.SelectedText = item.InsertText; Input.CaretIndex = item.Start + item.InsertText.Length; Input.Focus(); }
        finally { _applyingCompletion = false; }
    }
    private void OnCompletionMouseDown(object sender, MouseButtonEventArgs args)
    {
        if (args.OriginalSource is not DependencyObject source || ItemsControl.ContainerFromElement(CompletionList, source) is not ListBoxItem item) return;
        CompletionList.SelectedItem = item.DataContext; args.Handled = true; AcceptCompletion();
    }
    private void CloseCompletion() { _completionRequest?.Cancel(); CompletionPopup.IsOpen = false; }
}
