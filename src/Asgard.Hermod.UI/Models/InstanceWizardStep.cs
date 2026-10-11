namespace Asgard.Hermod.UI.Models;

/// <summary>承载向导侧栏中的步骤显示，步骤只能通过受校验的导航命令推进。</summary>
public sealed class InstanceWizardStep(int number, string title) : ObservableObject
{
    /// <summary>步骤的固定顺序。</summary>
    public int Number { get; } = number;
    /// <summary>步骤中文名称。</summary>
    public string Title { get; } = title;
    /// <summary>当前是否处于本步骤。</summary>
    public bool IsCurrent { get; private set; }
    /// <summary>是否已经通过本步骤。</summary>
    public bool IsCompleted { get; private set; }
    /// <summary>已完成步骤以勾号替代序号。</summary>
    public string Badge => IsCompleted ? "✓" : Number.ToString();

    /// <summary>导航变化后同时更新当前状态、完成状态和序号。</summary>
    internal void Update(int currentStep)
    {
        IsCurrent = Number == currentStep;
        IsCompleted = Number < currentStep;
        OnPropertyChanged(nameof(IsCurrent));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(Badge));
    }
}
