using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Win32;

namespace Asgard.Hermod.UI.Windowing;

/// <summary>唯一 GUID 通知图标，响应 Explorer 重建并检查失效，主题来自 Windows 通知区域。</summary>
public sealed class WindowsTrayIcon : ITrayIcon
{
    private static readonly Guid IconId = new("5db3d722-11c6-498d-969a-5fcc8ecad204");
    private const int Callback = 0x8001;
    private const uint NotifyMessage = 0x01, NotifyIcon = 0x02, NotifyTip = 0x04, NotifyInfo = 0x10, NotifyGuid = 0x20, ShowTip = 0x80;
    private const uint StandardFlags = NotifyMessage | NotifyIcon | NotifyTip | NotifyGuid | ShowTip;
    private readonly int _taskbarCreated = TrayNativeMethods.RegisterWindowMessage("TaskbarCreated");
    private readonly HwndSource _source;
    private readonly DispatcherTimer _health;
    private TrayNotifyData _data;
    private bool _dark;
    private bool _disposed;

    /// <summary>仅主进程在语言初始化及窗口就绪后创建此服务。</summary>
    public WindowsTrayIcon()
    {
        _source = new HwndSource(new HwndSourceParameters("Hermod.Tray") { Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000) });
        _source.AddHook(OnMessage);
        _data = new TrayNotifyData
        {
            Size = Marshal.SizeOf<TrayNotifyData>(),
            Window = _source.Handle,
            Id = 1,
            Flags = StandardFlags,
            Callback = Callback,
            Guid = IconId,
            Tip = Text.Get("AsgardHermod"),
            Info = "",
            InfoTitle = ""
        };
        UpdateIcon();
        Register();
        _health = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) => CheckHealth(), _source.Dispatcher);
        _health.Start();
    }

    /// <inheritdoc />
    public bool IsAvailable { get; private set; }
    /// <inheritdoc />
    public event Action? Activated;
    /// <inheritdoc />
    public event Action? MenuRequested;
    /// <inheritdoc />
    public event Action? AvailabilityChanged;

    /// <summary>先修改已存在的 GUID，再按需注册，避免恢复时重复创建图标。</summary>
    private void Register()
    {
        if (_disposed) return;
        _data.Flags = StandardFlags;
        bool available = _data.Icon != 0 && (TrayNativeMethods.Notify(1, ref _data) || TrayNativeMethods.Notify(0, ref _data));
        if (available) { _data.Version = 4; available = TrayNativeMethods.Notify(4, ref _data); }
        if (!available) TrayNativeMethods.Notify(2, ref _data);
        if (IsAvailable == available) return;
        IsAvailable = available;
        AvailabilityChanged?.Invoke();
    }

    /// <summary>设置消息后重读系统背景；通知区域未运行时逐步重试并保留主窗口。</summary>
    private void CheckHealth()
    {
        UpdateIcon();
        Register();
    }

    /// <summary>不修改 Windows 个性化配置，读取失败时采用可辨认的品牌默认图标。</summary>
    private void UpdateIcon()
    {
        bool dark;
        try { dark = (int?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 1) == 0; }
        catch (Exception) { dark = false; }
        if (_data.Icon != 0 && dark == _dark) return;
        string asset = dark ? "hermod-dark.ico" : "hermod.ico";
        using var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/Asgard.Hermod.UI;component/Assets/Brand/{asset}"))!.Stream;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        byte[] ico = bytes.ToArray();
        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        int entry = 6;
        for (int i = 0; i < count; i++) if (ico[6 + i * 16] == 32) { entry = 6 + i * 16; break; }
        int size = BinaryPrimitives.ReadInt32LittleEndian(ico.AsSpan(entry + 8));
        int offset = BinaryPrimitives.ReadInt32LittleEndian(ico.AsSpan(entry + 12));
        nint icon = TrayNativeMethods.CreateIconFromResourceEx(ico.AsSpan(offset, size).ToArray(), (uint)size, true, 0x30000, 32, 32, 0);
        if (icon == 0) return;
        nint old = _data.Icon; _data.Icon = icon; _dark = dark;
        if (old != 0) TrayNativeMethods.DestroyIcon(old);
    }

    /// <summary>版本四低位为事件，高位为图标 ID，支持键盘通知与旧式鼠标回调。</summary>
    private nint OnMessage(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == _taskbarCreated) Register();
        else if (message == 0x001A || message == 0x031A) CheckHealth();
        else if (message == Callback)
        {
            int notification = (int)((long)lParam & 0xffff);
            if (notification is 0x0202 or 0x0203 or 0x0400 or 0x0401) Activated?.Invoke();
            else if (notification is 0x007B or 0x0205) MenuRequested?.Invoke();
            handled = true;
        }
        return 0;
    }

    /// <inheritdoc />
    public bool TryGetBounds(out Rect rectangle)
    {
        var id = new TrayIconIdentifier { Size = Marshal.SizeOf<TrayIconIdentifier>(), Window = _source.Handle, Id = 1, Guid = IconId };
        rectangle = default;
        if (!IsAvailable || TrayNativeMethods.GetRect(ref id, out var bounds) != 0) return false;
        rectangle = new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        return true;
    }

    /// <inheritdoc />
    public void ShowHiddenHint()
    {
        if (!IsAvailable) return;
        _data.Flags = NotifyGuid | NotifyInfo; _data.InfoTitle = Text.Get("AsgardHermod");
        _data.Info = Text.Get("TrayHiddenHint"); _data.InfoFlags = 1;
        TrayNativeMethods.Notify(1, ref _data);
        _data.Info = ""; _data.InfoTitle = "";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _health.Stop();
        TrayNativeMethods.Notify(2, ref _data);
        if (_data.Icon != 0) TrayNativeMethods.DestroyIcon(_data.Icon);
        _source.RemoveHook(OnMessage); _source.Dispose(); IsAvailable = false;
    }
}
