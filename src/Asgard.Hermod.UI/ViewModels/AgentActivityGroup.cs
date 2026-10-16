namespace Asgard.Hermod.UI.ViewModels;

/// <summary>相邻普通工具的展示摘要；批准、错误和用户消息永远不进入组。</summary>
public sealed class AgentActivityGroup : ObservableObject
{
    private AgentDisplayMessage[] _messages = [];
    /// <summary>只读引用原始工具对象，详细证据仍由原模块模板展示。</summary>
    public IReadOnlyList<AgentDisplayMessage> Messages => _messages;
    /// <summary>归档恢复时由第一条原消息保存组展开状态，不持久化或重新授权。</summary>
    public bool IsExpanded { get => _messages.FirstOrDefault()?.IsActivityGroupExpanded == true; set { if (_messages.Length > 0) _messages[0].IsActivityGroupExpanded = value; } }
    /// <summary>只按稳定动作种类选择完整句子模板。</summary>
    public string Title => Text.Format(_messages.All(message => message.ActivityKind == "query") ? Tone == "running" ? "ChatGroupedQueriesRunning" : "ChatGroupedQueries" : Tone == "running" ? "ChatGroupedActionsRunning" : "ChatGroupedActions", _messages.Length);
    /// <summary>进行中优先于已完成；部分失败不会被总结成全部成功。</summary>
    public string Tone => _messages.Any(message => message.ActivityTone == "running") ? "running" : _messages.Any(message => message.ActivityTone == "warning") ? "warning" : _messages.All(message => message.ActivityTone == "success") ? "success" : "muted";
    /// <summary>状态图标与摘要状态同步。</summary>
    public string Icon => Tone switch { "running" => "loader-circle", "warning" => "circle-alert", "success" => "circle-check", _ => "terminal" };
    /// <summary>合计真实测得的各步骤耗时，不解析已翻译的耗时文字。</summary>
    public string Elapsed => _messages.All(message => message.ActivityElapsed is not null) ? Text.Format("ChatActivityElapsed", _messages.Sum(message => message.ActivityElapsed!.Value.TotalMilliseconds)) : "";
    /// <summary>保持展开状态的同时刷新成员；弱订阅允许已移除投影被回收。</summary>
    internal void UpdateMembers(IEnumerable<AgentDisplayMessage> messages)
    {
        var next = messages.ToArray(); if (_messages.SequenceEqual(next)) return;
        foreach (var message in _messages) PropertyChangedEventManager.RemoveHandler(message, OnMessageChanged, "");
        _messages = next;
        foreach (var message in _messages) PropertyChangedEventManager.AddHandler(message, OnMessageChanged, "");
        Notify();
    }
    private void OnMessageChanged(object? sender, PropertyChangedEventArgs args) => Notify();
    private void Notify()
    {
        foreach (string name in new[] { nameof(Messages), nameof(IsExpanded), nameof(Title), nameof(Tone), nameof(Icon), nameof(Elapsed) }) OnPropertyChanged(name);
    }
}
