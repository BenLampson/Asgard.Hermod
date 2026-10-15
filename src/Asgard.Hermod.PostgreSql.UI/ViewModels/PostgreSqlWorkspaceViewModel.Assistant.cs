using Asgard.Hermod.Agent;

namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

public sealed partial class PostgreSqlWorkspaceViewModel
{
    private readonly Dictionary<PostgreSqlTabViewModel, PostgreSqlAssistantViewModel> _assistants = [];
    private IAgentClient? _assistantClient;
    /// <summary>侧边栏开关不影响页签对话、结果和编辑器。</summary>
    public bool IsAssistantOpen { get; set => SetProperty(ref field, value); }
    /// <summary>侧边栏跟随当前页签，但每个页签保留独立内存对话。</summary>
    public PostgreSqlAssistantViewModel? Assistant => ActiveTab is { } tab ? GetAssistant(tab) : null;
    /// <summary>复用公共布局的侧边详情入口。</summary>
    public RelayCommand ToggleAssistantCommand { get; private set; } = null!;
    partial void InitializeAssistant(IAgentClient? client)
    {
        _assistantClient = client;
        ToggleAssistantCommand = new RelayCommand(_ => IsAssistantOpen = !IsAssistantOpen);
        Tabs.CollectionChanged += (_, _) =>
        {
            foreach (var stale in _assistants.Keys.Where(tab => !Tabs.Contains(tab)).ToArray())
            { _assistants[stale].Dispose(); _assistants.Remove(stale); }
        };
    }
    private PostgreSqlAssistantViewModel GetAssistant(PostgreSqlTabViewModel tab)
    {
        if (!_assistants.TryGetValue(tab, out var assistant)) { assistant = new(tab, _assistantClient); _assistants.Add(tab, assistant); }
        return assistant;
    }
    partial void OnActiveTabChanged() => OnPropertyChanged(nameof(Assistant));
    partial void ReleaseAssistant()
    {
        foreach (var assistant in _assistants.Values) assistant.Dispose();
        _assistants.Clear();
    }
}
