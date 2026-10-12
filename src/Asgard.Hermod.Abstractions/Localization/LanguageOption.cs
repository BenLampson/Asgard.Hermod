namespace Asgard.Hermod.Abstractions.Localization;

/// <summary>语言标识与本地名称独立于界面翻译，方便添加新语种。</summary>
public sealed record LanguageOption(string Id, string NativeName, string[] SystemLanguagePrefixes);
