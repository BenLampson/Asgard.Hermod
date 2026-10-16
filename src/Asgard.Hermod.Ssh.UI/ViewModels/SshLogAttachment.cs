using Asgard.Hermod.Agent;
using LocText = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>明确分享给日志 Agent 的冻结证据；不引用可变的日志文档正文。</summary>
public sealed record SshLogAttachment(string DocumentId, string Host, string ContainerId, string Source, string Text,
    DateTimeOffset CapturedAt, int RequestedLines, int StartCharacter, int EndCharacter, int FromLine, int ToLine, bool IsSelection, bool IsTruncated)
{
    /// <summary>冻结整份快照或真实选区；超过上限仅保留尾部，并保留准确的原文范围。</summary>
    public static SshLogAttachment Freeze(SshLogSnapshot snapshot, string? selection = null, int selectionStart = 0, string? displayedText = null)
    {
        string original = displayedText ?? snapshot.Text;
        if (displayedText is not null && displayedText.Replace("\r\n", "\n", StringComparison.Ordinal) != snapshot.Text.Replace("\r\n", "\n", StringComparison.Ordinal)) throw LocText.Exception("ChatLogSelectionChanged");
        bool selected = selection is not null;
        string text = selection ?? original;
        int start = selected ? selectionStart : 0;
        if (start < 0 || start > original.Length || text.Length > original.Length - start || !original.AsSpan(start, text.Length).SequenceEqual(text)) throw LocText.Exception("ChatLogSelectionChanged");
        bool limited = text.Length > LogAnalysisConversation.ContextLimit;
        if (limited) { start += text.Length - LogAnalysisConversation.ContextLimit; text = text[^LogAnalysisConversation.ContextLimit..]; }
        int end = start + text.Length;
        int fromLine = 1 + original.AsSpan(0, start).Count('\n');
        int toLine = text.Length == 0 ? fromLine : 1 + original.AsSpan(0, end - (text.EndsWith('\n') ? 1 : 0)).Count('\n');
        return new(snapshot.DocumentId, snapshot.Host, snapshot.ContainerId, snapshot.Source, text, snapshot.CapturedAt, snapshot.RequestedLines, start, end, fromLine, toLine, selected, limited || snapshot.IsTruncated);
    }
    /// <summary>向用户展示真实行范围、采集时间和截断状态，完整模板由翻译决定顺序。</summary>
    public string Summary => LocText.Format(IsSelection ? "ChatLogSelectionAttachment" : "ChatLogSnapshotAttachment", Text.Length, FromLine, ToLine, CapturedAt.ToLocalTime(),
        Asgard.Hermod.Abstractions.Localization.Text.Get(IsTruncated ? "ChatContextIncomplete" : "ChatContextComplete"));
}
