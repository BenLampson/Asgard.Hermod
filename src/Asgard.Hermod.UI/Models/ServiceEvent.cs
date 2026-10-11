namespace Asgard.Hermod.UI.Models;

/// <summary>服务事件的展示快照。</summary>
public sealed record ServiceEvent(string Message, string Time, string Icon, StatusTone Tone);
