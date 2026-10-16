namespace Asgard.Hermod.UI.Controls;

/// <summary>固定来源栏与模块对话正文，可完全离屏测量。</summary>
public partial class AgentSourceView : UserControl
{
    private readonly AgentWindowRequest _request;
    /// <summary>绑定固定模型，不继承工作区随后切换的活动 Session。</summary>
    public AgentSourceView(AgentWindowRequest request)
    {
        _request = request; InitializeComponent(); ConversationContent.Content = request.CreateContent();
        if (ConversationContent.Content is FrameworkElement content) content.DataContext = request.Conversation;
        if (request.Conversation is INotifyPropertyChanged observed) PropertyChangedEventManager.AddHandler(observed, OnConversationChanged, "IsSourceClosed");
        UpdateClosed();
    }
    /// <summary>来源标题保持打开时身份。</summary>
    public string SourceTitle => _request.Title;
    /// <summary>来源地址不包含凭据。</summary>
    public string SourceDetail => _request.Detail;
    /// <summary>协议来源图标。</summary>
    public string Icon => _request.Icon is "brand-mysql" or "brand-postgresql" ? "database" : _request.Icon;
    private void OnViewportChanged(object sender, SizeChangedEventArgs args) => SourceColumn.Margin = new(args.NewSize.Width < 800 ? 24 : args.NewSize.Width * 0.1, 0, args.NewSize.Width < 800 ? 24 : args.NewSize.Width * 0.1, 0);
    private void OnLocate(object sender, RoutedEventArgs args) { if (!_request.IsClosed()) _request.Locate(); }
    private void OnConversationChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == "IsSourceClosed") UpdateClosed(); }
    private void UpdateClosed() { LocateButton.IsEnabled = !_request.IsClosed(); LocateButton.ToolTip = _request.IsClosed() ? Text.Get("ChatSourceClosed") : Text.Get("ChatLocateSource"); }
}
