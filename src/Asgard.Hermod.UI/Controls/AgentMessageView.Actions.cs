using System.Runtime.InteropServices;

namespace Asgard.Hermod.UI.Controls;

/// <summary>回复操作只作用于本地原文、草稿和内存反馈；不自动发送或执行。</summary>
public partial class AgentMessageView
{
    /// <summary>生成本条回复时接受的原始问题，后续草稿不会重写它。</summary>
    public static readonly DependencyProperty QuestionProperty = DependencyProperty.Register(nameof(Question), typeof(string), typeof(AgentMessageView), new PropertyMetadata(""));
    /// <summary>当前内存对话的本地反馈。</summary>
    public static readonly DependencyProperty FeedbackProperty = DependencyProperty.Register(nameof(Feedback), typeof(AgentMessageFeedback), typeof(AgentMessageView), new FrameworkPropertyMetadata(AgentMessageFeedback.None, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    /// <summary>只读的原始问题。</summary>
    public string Question { get => (string)GetValue(QuestionProperty); set => SetValue(QuestionProperty, value); }
    /// <summary>可撤销且互斥的反馈标记。</summary>
    public AgentMessageFeedback Feedback { get => (AgentMessageFeedback)GetValue(FeedbackProperty); set => SetValue(FeedbackProperty, value); }
    private void OnCopyAnswer(object sender, RoutedEventArgs args)
    {
        if (Body.Length == 0) return;
        try { Clipboard.SetText(Body); CopyStatus.Text = Text.Get("ChatAnswerCopied"); }
        catch (ExternalException) { CopyStatus.Text = Text.Get("CopyFailed"); }
    }
    private void OnHelpful(object sender, RoutedEventArgs args) => SetCurrentValue(FeedbackProperty, Feedback == AgentMessageFeedback.Helpful ? AgentMessageFeedback.None : AgentMessageFeedback.Helpful);
    private void OnUnhelpful(object sender, RoutedEventArgs args) => SetCurrentValue(FeedbackProperty, Feedback == AgentMessageFeedback.Unhelpful ? AgentMessageFeedback.None : AgentMessageFeedback.Unhelpful);
}
