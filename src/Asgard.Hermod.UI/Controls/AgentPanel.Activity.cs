namespace Asgard.Hermod.UI.Controls;

public partial class AgentPanel
{
    private readonly Dictionary<object, AgentConversationItem> _individualItems = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AgentDisplayMessage, AgentConversationItem> _activityItems = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<AgentDisplayMessage> _activityMessages = new(ReferenceEqualityComparer.Instance);
    /// <summary>仅供视图使用的相邻工具投影；业务 Messages 集合和原对象保持不变。</summary>
    public ObservableCollection<AgentConversationItem> PresentationMessages { get; } = [];
    private static bool CanGroup(object message) => message is AgentDisplayMessage { IsTool: true, IsUser: false, IsError: false, Approval: null, IsAwaitingApproval: false, RecoveryPrompt.Length: 0 };
    private void ProjectMessages()
    {
        UpdateStreamingState();
        object[] source = Messages?.Cast<object>().ToArray() ?? [];
        var activities = source.OfType<AgentDisplayMessage>().ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var stale in _activityMessages.Where(message => !activities.Contains(message)).ToArray())
        { PropertyChangedEventManager.RemoveHandler(stale, OnActivityEligibilityChanged, ""); _activityMessages.Remove(stale); }
        foreach (var message in source.OfType<AgentDisplayMessage>())
            if (_activityMessages.Add(message)) PropertyChangedEventManager.AddHandler(message, OnActivityEligibilityChanged, "");
        var desired = new List<AgentConversationItem>();
        for (int index = 0; index < source.Length;)
        {
            int end = index + 1;
            if (CanGroup(source[index])) while (end < source.Length && CanGroup(source[end])) end++;
            if (end - index > 1)
            {
                var first = (AgentDisplayMessage)source[index];
                if (!_activityItems.TryGetValue(first, out var item)) { item = new(new AgentActivityGroup(), true); _activityItems.Add(first, item); }
                ((AgentActivityGroup)item.Message).UpdateMembers(source[index..end].Cast<AgentDisplayMessage>()); desired.Add(item);
            }
            else
            {
                if (!_individualItems.TryGetValue(source[index], out var item)) { item = new(source[index]); _individualItems.Add(source[index], item); }
                desired.Add(item);
            }
            index = end;
        }
        bool hasToolHeader = false, hasAssistantMessage = false;
        for (int index = 0; index < desired.Count; index++)
        {
            var item = desired[index]; var message = OriginalMessage(item);
            if (message is null || message.IsUser)
            { item.ShowToolTurnHeader = false; item.HideMessageIdentity = false; hasToolHeader = false; hasAssistantMessage = false; continue; }
            item.ShowToolTurnHeader = !hasAssistantMessage && message.IsTool;
            if (item.ShowToolTurnHeader) hasToolHeader = true;
            item.HideMessageIdentity = hasToolHeader; hasAssistantMessage = true;
            if (item.ShowToolTurnHeader)
                item.ModelIdentity = desired.Skip(index).Select(OriginalMessage).TakeWhile(candidate => candidate?.IsUser != true).FirstOrDefault(candidate => candidate?.ModelIdentity.Length > 0)?.ModelIdentity ?? "";
        }
        // 原有回复的视图不因新工具到达而重建，保留文字选区和阅读状态。
        for (int index = 0; index < desired.Count; index++)
        {
            if (index < PresentationMessages.Count && ReferenceEquals(PresentationMessages[index], desired[index])) continue;
            int existing = PresentationMessages.IndexOf(desired[index]);
            if (existing >= 0) PresentationMessages.Move(existing, index);
            else PresentationMessages.Insert(index, desired[index]);
        }
        while (PresentationMessages.Count > desired.Count) PresentationMessages.RemoveAt(PresentationMessages.Count - 1);
        var retained = source.ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (object stale in _individualItems.Keys.Where(key => !retained.Contains(key)).ToArray()) _individualItems.Remove(stale);
        foreach (var stale in _activityItems.Keys.Where(key => !desired.Any(item => item.IsGroup && ReferenceEquals(((AgentActivityGroup)item.Message).Messages[0], key))).ToArray()) _activityItems.Remove(stale);
    }
    private void OnActivityEligibilityChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(AgentDisplayMessage.IsAwaitingApproval) or nameof(AgentDisplayMessage.ModelIdentity)) ProjectMessages();
        if (args.PropertyName == nameof(AgentDisplayMessage.IsStreaming)) UpdateStreamingState();
        if (args.PropertyName is nameof(AgentDisplayMessage.Content) or nameof(AgentDisplayMessage.Thinking)) FollowStream();
    }
    private static AgentDisplayMessage? OriginalMessage(AgentConversationItem item) => item.Message is AgentActivityGroup group ? group.Messages.FirstOrDefault() : item.Message as AgentDisplayMessage;
    private void DetachActivityMessages()
    {
        foreach (var message in _activityMessages) PropertyChangedEventManager.RemoveHandler(message, OnActivityEligibilityChanged, "");
        _activityMessages.Clear();
    }
}
