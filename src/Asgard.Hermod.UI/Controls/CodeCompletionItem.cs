namespace Asgard.Hermod.UI.Controls;

/// <summary>补全候选携带完整替换范围，调用方负责语言语义和安全引用。</summary>
public sealed record CodeCompletionItem(string Label, string InsertText, int Start, int Length, string Detail = "");
