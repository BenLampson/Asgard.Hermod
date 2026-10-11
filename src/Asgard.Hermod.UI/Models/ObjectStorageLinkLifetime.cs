namespace Asgard.Hermod.UI.Models;

/// <summary>临时链接支持的明确有效期，不接受任意外部数值。</summary>
public sealed record ObjectStorageLinkLifetime(TimeSpan Value, string Label);
