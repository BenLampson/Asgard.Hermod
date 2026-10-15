namespace Asgard.Hermod.Ssh;

/// <summary>协议无关的远端目录条目，不把文件名作为命令或本机路径使用。</summary>
public sealed record SshFileEntry(string Name, string Path, bool IsDirectory, long Length, string Permissions, DateTimeOffset Modified);
