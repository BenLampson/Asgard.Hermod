namespace Asgard.Hermod.Redis.UI.ViewModels;

/// <summary>集合行保存原始字节对应的文本；成员与字段不是界面标签。</summary>
public sealed class RedisEntryViewModel : ObservableObject
{
    private readonly Action _changed;
    /// <summary>填入基线时不产生用户编辑通知。</summary>
    public RedisEntryViewModel(RedisEntry entry, string format, Action changed)
    {
        _changed = changed; Name = RedisValueCodec.Encode(entry.Name, format); Value = RedisValueCodec.Encode(entry.Value, format);
        Score = entry.Score; RecordId = entry.RecordId;
    }
    /// <summary>字段或成员内容。</summary>
    public string Name { get; set { if (SetProperty(ref field, value)) _changed(); } } = "";
    /// <summary>值文本。</summary>
    public string Value { get; set { if (SetProperty(ref field, value)) _changed(); } } = "";
    /// <summary>协议分数，始终使用 invariant 小数点。</summary>
    public string Score { get; set { if (SetProperty(ref field, value)) _changed(); } } = "";
    /// <summary>已有 Stream 记录的固定身份。</summary>
    public string RecordId { get; }
    /// <summary>提交前进行无损转换。</summary>
    public RedisEntry ToEntry(string format) => new(RedisValueCodec.Decode(Name, format), RedisValueCodec.Decode(Value, format), Score, RecordId);
}
