namespace Asgard.Hermod.MySql;

/// <summary>补全候选保留数据库原始标识符，描述资源由呈现层选择。</summary>
public sealed record MySqlCompletionCandidate(string Name, string InsertText, int Start, int Length, MySqlObjectKind? Kind, string Detail = "");
