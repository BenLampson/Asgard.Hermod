using System.IO;
using System.Runtime.InteropServices;

namespace Asgard.Hermod.UI.Services;

/// <summary>取得用户当前的 Windows 下载文件夹，支持系统中重定向过的位置。</summary>
internal static class WindowsDownloadFolder
{
    private static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    internal static string GetDefault()
    {
        nint pointer = 0;
        try
        {
            if (OperatingSystem.IsWindows() && SHGetKnownFolderPath(Downloads, 0, nint.Zero, out pointer) == 0
                && Marshal.PtrToStringUni(pointer) is { } folder && IsLocalFolder(folder)) return folder;
        }
        finally { if (pointer != 0) Marshal.FreeCoTaskMem(pointer); }
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string downloads = Path.Combine(profile, "Downloads");
        return IsLocalFolder(downloads) ? downloads : IsLocalFolder(profile) ? profile : "";
    }

    private static bool IsLocalFolder(string folder) => folder.Length >= 3 && char.IsAsciiLetter(folder[0])
        && folder[1] == ':' && folder[2] is '\\' or '/' && Directory.Exists(folder);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid folderId, uint flags, nint token, out nint path);
}
