namespace Asgard.Hermod.UI.Controls;

public partial class AgentPanel
{
    private bool _streamScrollQueued;
    private static readonly DependencyPropertyKey HasStreamingMessagePropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasStreamingMessage), typeof(bool), typeof(AgentPanel), new PropertyMetadata(false));
    /// <summary>已有真实流式回复时隐藏通用等待提示。</summary>
    public static readonly DependencyProperty HasStreamingMessageProperty = HasStreamingMessagePropertyKey.DependencyProperty;
    /// <summary>当前集合是否正在接收正文或思考。</summary>
    public bool HasStreamingMessage => (bool)GetValue(HasStreamingMessageProperty);
    private void UpdateStreamingState() => SetValue(HasStreamingMessagePropertyKey, Messages?.OfType<AgentDisplayMessage>().Any(message => message.IsStreaming) == true);
    private void FollowStream()
    {
        double offset = ConversationScroll.VerticalOffset;
        if (_streamScrollQueued || !IsVisible || ConversationScroll.ScrollableHeight - offset > 24) return;
        _streamScrollQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _streamScrollQueued = false;
            if (IsLoaded && IsVisible && Math.Abs(ConversationScroll.VerticalOffset - offset) < 1) ConversationScroll.ScrollToEnd();
        });
    }
}
