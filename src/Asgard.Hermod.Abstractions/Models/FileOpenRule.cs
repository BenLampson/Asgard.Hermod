using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.Abstractions.Models;

/// <summary>Hermod 内部的单个文件后缀规则，不修改 Windows 文件关联。</summary>
public sealed record FileOpenRule
{
    /// <summary>规范化后缀和程序路径，持久化只接受单个绝对程序地址。</summary>
    public FileOpenRule(string extension, string programPath)
    {
        Extension = NormalizeExtension(extension);
        if (string.IsNullOrWhiteSpace(programPath) || !Path.IsPathFullyQualified(programPath)
            || !Path.GetExtension(programPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw Text.Exception("FileProgramInvalid");
        ProgramPath = Path.GetFullPath(programPath);
    }

    /// <summary>带点的小写后缀，匹配不依赖界面语言。</summary>
    public string Extension { get; }
    /// <summary>独立的程序路径，不包含命令行参数。</summary>
    public string ProgramPath { get; }
    /// <summary>程序名来自用户选择的文件，不作为翻译文案。</summary>
    public string ProgramName => Path.GetFileNameWithoutExtension(ProgramPath);

    /// <summary>接受 txt 或 .txt，拒绝路径、通配符及多后缀输入。</summary>
    public static string NormalizeExtension(string extension)
    {
        string input = (extension ?? "").Trim();
        string value = input.TrimStart('.').ToLowerInvariant();
        if (value.Length is 0 or > 32 || value.Any(character => !char.IsAsciiLetterOrDigit(character)
            && character is not '-' and not '_') || input.StartsWith("..", StringComparison.Ordinal))
            throw Text.Exception("FileExtensionInvalid");
        return "." + value;
    }
}
