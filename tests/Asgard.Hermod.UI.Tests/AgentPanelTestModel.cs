using System.Collections.ObjectModel;
using Asgard.Hermod.UI.Common;

namespace Asgard.Hermod.UI.Tests;

/// <summary>只记录展示层委托的动作，不包含 SQL、SSH 或模型服务实现。</summary>
internal sealed class AgentPanelTestModel : ObservableObject
{
    /// <summary>创建可观察的命令，便于验证共享控件没有代替模块执行业务。</summary>
    internal AgentPanelTestModel()
    {
        Archive = new(Messages, () => Asgard.Hermod.Agent.AgentConversationSnapshot.Empty, _ => { }, () => Prompt, value => Prompt = value, () => IsBusy);
        RestoreDraftCommand = new RelayCommand(_ => { });
        SendCommand = new RelayCommand(_ => SendCount++, _ => !IsBusy && !string.IsNullOrWhiteSpace(Prompt));
        CancelCommand = new RelayCommand(_ => CancelCount++, _ => IsBusy);
        ClearCommand = new RelayCommand(_ => ClearCount++);
        HideCommand = new RelayCommand(_ => HideCount++);
        InsertCommand = new RelayCommand(value => Inserted = value);
        ExecuteCommand = new RelayCommand(value => Executed = value);
    }

    public AgentApprovalRequest? PendingApproval => null;
    public bool IsSourceClosed => false;
    public IReadOnlyList<string> QuickPrompts { get; } = [];
    public AgentConversationArchive Archive { get; }
    public RelayCommand RestoreDraftCommand { get; }
    /// <summary>模块消息集合。</summary>
    public ObservableCollection<object> Messages { get; } = [];
    /// <summary>输入草稿随绑定变化。</summary>
    public string Prompt { get; set { if (SetProperty(ref field, value)) SendCommand.NotifyCanExecuteChanged(); } } = "";
    /// <summary>测试上下文原文。</summary>
    public string ContextLabel => "Current workspace";
    /// <summary>SQL 上下文选项。</summary>
    public bool IncludeSql { get; set => SetProperty(ref field, value); } = true;
    /// <summary>结果样本必须由用户选择。</summary>
    public bool IncludeResults { get; set => SetProperty(ref field, value); }
    /// <summary>SQL 输入区底部的稳定模式值。</summary>
    public string ExecutionMode { get; set => SetProperty(ref field, value); } = "manual";
    /// <summary>按测试显式文化构造模式名称。</summary>
    public IReadOnlyList<InstanceFieldChoice> ExecutionModes { get; } = [new("manual", Asgard.Hermod.Abstractions.Localization.Text.Get("SqlAgentManual")), new("automatic", Asgard.Hermod.Abstractions.Localization.Text.Get("SqlAgentAutomatic"))];
    /// <summary>任务期间不可更换模式。</summary>
    public bool CanChangeExecutionMode => !IsBusy;
    /// <summary>默认未授权结果样本。</summary>
    public bool CanShareResults => false;
    /// <summary>SSH 选区上下文选项。</summary>
    public bool IncludeOutput { get; set => SetProperty(ref field, value); }
    /// <summary>任务状态变化同步命令可用性。</summary>
    public bool IsBusy { get; set { if (SetProperty(ref field, value)) { SendCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(CanChangeExecutionMode)); } } }
    /// <summary>安全错误占位。</summary>
    public string Error => "";
    /// <summary>任务进度原文。</summary>
    public string Status => "Working";
    /// <summary>发送命令。</summary>
    public RelayCommand SendCommand { get; }
    /// <summary>停止命令。</summary>
    public RelayCommand CancelCommand { get; }
    /// <summary>清空命令。</summary>
    public RelayCommand ClearCommand { get; }
    /// <summary>收起命令。</summary>
    public RelayCommand HideCommand { get; }
    /// <summary>插入命令。</summary>
    public RelayCommand InsertCommand { get; }
    /// <summary>执行命令。</summary>
    public RelayCommand ExecuteCommand { get; }
    /// <summary>已收到的发送次数。</summary>
    internal int SendCount { get; private set; }
    /// <summary>已收到的停止次数。</summary>
    internal int CancelCount { get; private set; }
    /// <summary>已收到的新对话次数。</summary>
    internal int ClearCount { get; private set; }
    /// <summary>已收到的收起次数。</summary>
    internal int HideCount { get; private set; }
    /// <summary>未经改写的插入参数。</summary>
    internal object? Inserted { get; private set; }
    /// <summary>未经改写的执行参数。</summary>
    internal object? Executed { get; private set; }
}
