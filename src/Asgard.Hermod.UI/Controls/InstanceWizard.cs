using System.Collections;

namespace Asgard.Hermod.UI.Controls;

/// <summary>类型无关的流程容器；步骤、标题和正文由引用者提供，不预设步骤数量或业务校验。</summary>
public class InstanceWizard : ContentControl
{
    /// <summary>流程标题。</summary>
    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(
        nameof(Heading), typeof(string), typeof(InstanceWizard), new PropertyMetadata(""));
    /// <summary>所选类型或流程的说明。</summary>
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(InstanceWizard), new PropertyMetadata(""));
    /// <summary>由具体流程提供的进度集合，类型入口不提供步骤。</summary>
    public static readonly DependencyProperty StepsProperty = DependencyProperty.Register(
        nameof(Steps), typeof(IEnumerable), typeof(InstanceWizard));
    /// <summary>只有进入类型流程后才展示侧栏，隐藏时正文使用全部宽度。</summary>
    public static readonly DependencyProperty ShowProgressProperty = DependencyProperty.Register(
        nameof(ShowProgress), typeof(bool), typeof(InstanceWizard), new PropertyMetadata(false));

    /// <summary>自动加载共享流程布局模板。</summary>
    static InstanceWizard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(InstanceWizard), new FrameworkPropertyMetadata(typeof(InstanceWizard)));
        // 展示容器不占焦点，内部输入和按钮继续按原生顺序参与导航。
        FocusableProperty.OverrideMetadata(typeof(InstanceWizard), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(InstanceWizard), new FrameworkPropertyMetadata(false));
    }

    /// <summary>流程标题。</summary>
    public string Heading { get => (string)GetValue(HeadingProperty); set => SetValue(HeadingProperty, value); }
    /// <summary>流程说明。</summary>
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    /// <summary>步骤进度；容器只展示状态，导航和校验由流程负责。</summary>
    public IEnumerable? Steps { get => (IEnumerable?)GetValue(StepsProperty); set => SetValue(StepsProperty, value); }
    /// <summary>是否展示步骤侧栏。</summary>
    public bool ShowProgress { get => (bool)GetValue(ShowProgressProperty); set => SetValue(ShowProgressProperty, value); }
}
