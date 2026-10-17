#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateRange(1, 16384)][int] $MinimumLogicalWidth = 1600,
    [ValidateRange(1, 16384)][int] $MinimumLogicalHeight = 1000,
    [switch] $CheckOnly
)

$ErrorActionPreference = 'Stop'
if (-not $CheckOnly -and $env:CI -ne 'true') {
    throw 'Display changes are restricted to CI. Use -CheckOnly on a personal desktop.'
}

# 托管 Windows 默认的小屏幕会裁剪真实测试窗口；只在临时 CI 桌面调整，不写入显示设置注册表。
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

/// <summary>为桌面验收读取并临时选择足够大的主显示器模式。</summary>
public static class HermodDesktopTestDisplay
{
    /// <summary>Unicode DEVMODE 的显示器分支，字段顺序与 Win32 ABI 一致。</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct Mode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int PositionX, PositionY;
        public uint Orientation, FixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public ushort LogPixels;
        public uint BitsPerPixel, Width, Height, DisplayFlags, Frequency;
        public uint IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string device, int index, ref Mode mode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettings(ref Mode mode, uint flags);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(nint context);

    /// <summary>读取真实系统 DPI，避免初始化工具自身被按 96 DPI 虚拟化。</summary>
    public static uint GetDpi()
    {
        SetProcessDpiAwarenessContext(new nint(-4));
        return GetDpiForSystem();
    }

    /// <summary>读取当前主显示器模式。</summary>
    public static Mode Current()
    {
        var mode = new Mode { Size = (ushort)Marshal.SizeOf<Mode>() };
        if (!EnumDisplaySettings(null, -1, ref mode)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return mode;
    }

    /// <summary>只返回驱动明确支持的模式，不猜测虚拟显示器可接受的分辨率。</summary>
    public static Mode[] Supported()
    {
        var modes = new List<Mode>();
        for (int index = 0; ; index++)
        {
            var mode = new Mode { Size = (ushort)Marshal.SizeOf<Mode>() };
            if (!EnumDisplaySettings(null, index, ref mode)) break;
            modes.Add(mode);
        }
        return modes.ToArray();
    }

    /// <summary>先验证模式，再动态应用；不使用 CDS_UPDATEREGISTRY。</summary>
    public static void Apply(Mode mode)
    {
        mode.Fields = 0x00080000 | 0x00100000;
        int result = ChangeDisplaySettings(ref mode, 2);
        if (result != 0) throw new InvalidOperationException("Display mode test failed: " + result);
        result = ChangeDisplaySettings(ref mode, 0);
        if (result != 0) throw new InvalidOperationException("Display mode change failed: " + result);
    }
}
'@

$testDpi = [HermodDesktopTestDisplay]::GetDpi()
$testDisplay = [HermodDesktopTestDisplay]::Current()
$minimumPixelWidth = [Math]::Ceiling($MinimumLogicalWidth * $testDpi / 96.0)
$minimumPixelHeight = [Math]::Ceiling($MinimumLogicalHeight * $testDpi / 96.0)
Write-Output "Desktop before tests: $($testDisplay.Width)x$($testDisplay.Height), DPI $testDpi."
if ($testDisplay.Width -lt $minimumPixelWidth -or $testDisplay.Height -lt $minimumPixelHeight) {
    if ($CheckOnly) { throw 'The desktop is too small for the requested native layout acceptance.' }
    $testMode = [HermodDesktopTestDisplay]::Supported() |
        Where-Object { $_.Width -ge $minimumPixelWidth -and $_.Height -ge $minimumPixelHeight -and $_.BitsPerPixel -ge 32 } |
        Sort-Object @{ Expression = { [long]$_.Width * $_.Height } }, @{ Expression = { -[int]$_.Frequency } } |
        Select-Object -First 1
    if ($null -eq $testMode) { throw 'No supported display mode can host the native layout acceptance.' }
    [HermodDesktopTestDisplay]::Apply($testMode)
    $testDisplay = [HermodDesktopTestDisplay]::Current()
    if ($testDisplay.Width -lt $minimumPixelWidth -or $testDisplay.Height -lt $minimumPixelHeight) {
        throw 'The display did not reach the required test dimensions.'
    }
}
Write-Output "Desktop acceptance ready: $($testDisplay.Width)x$($testDisplay.Height), DPI $testDpi."
