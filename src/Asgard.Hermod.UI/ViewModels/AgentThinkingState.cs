using System.Diagnostics;
using Asgard.Hermod.Agent;

namespace Asgard.Hermod.UI.ViewModels;

/// <summary>显式思考文本的独立阅读状态，完成、取消和归档均保留用户的展开选择。</summary>
public sealed class AgentThinkingState : ObservableObject
{
    private readonly Stopwatch _clock = new();
    private TimeSpan _elapsed;
    /// <summary>模型返回的思考原文，不提取可执行代码。</summary>
    public string Content { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>只有实际收到非空思考文本时才显示入口。</summary>
    public bool HasContent => !string.IsNullOrWhiteSpace(Content);
    /// <summary>思考阶段是否仍在接收。</summary>
    public bool IsActive { get; private set => SetProperty(ref field, value); }
    /// <summary>失败或取消保留文本并明确标注中断。</summary>
    public bool IsInterrupted { get; private set => SetProperty(ref field, value); }
    /// <summary>展开选择不受后续数据与完成事件影响。</summary>
    public bool IsExpanded { get; set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(ToggleLabel)); } }
    /// <summary>真实经过的秒数，界面可见时定时刷新。</summary>
    public int Seconds => (int)Math.Floor((_elapsed + _clock.Elapsed).TotalSeconds);
    /// <summary>本地化阶段标题。</summary>
    public string Header => IsInterrupted ? Text.Get("AgentThinkingInterrupted") : IsActive ? Text.Get("AgentThinking") : Text.Format("AgentThinkingCompleted", Seconds);
    /// <summary>运行时的独立耗时文本，不参与扫光。</summary>
    public string ElapsedLabel => IsActive ? Text.Format("AgentThinkingElapsed", Seconds) : "";
    /// <summary>键盘与屏幕阅读器使用相同的动作说明。</summary>
    public string ToggleLabel => Text.Get(IsExpanded ? "AgentThinkingCollapse" : "AgentThinkingExpand");
    internal void Apply(AgentStreamUpdate update)
    {
        Content = update.Thinking; _elapsed = update.ThinkingElapsed; _clock.Reset();
        IsActive = HasContent && update.IsThinking; IsInterrupted = false;
        if (IsActive) _clock.Start();
        OnPropertyChanged(nameof(HasContent)); RefreshElapsed();
    }
    internal void Interrupt()
    {
        _elapsed += _clock.Elapsed; _clock.Reset(); IsActive = false; IsInterrupted = HasContent;
        RefreshElapsed();
    }
    internal void RefreshElapsed() { OnPropertyChanged(nameof(Header)); OnPropertyChanged(nameof(ElapsedLabel)); }
}
