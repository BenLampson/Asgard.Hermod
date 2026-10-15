namespace Asgard.Hermod.Abstractions.Models;

/// <summary>实例的托管品牌展示信息，与连接器和协议标识独立。</summary>
public sealed record InstanceHostingProvider(string Id, string Name, string Icon);
