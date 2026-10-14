namespace Asgard.Hermod.UI.Windowing;

/// <summary>按真实设备像素工作区计算菜单与箭头，允许负坐标和不同 DPI。</summary>
public static class TrayMenuPlacement
{
    /// <summary>菜单优先显示在图标上方，空间不足时翻到下方并限制在工作区内。</summary>
    public static (Point Position, bool ArrowOnTop, double ArrowOffset) Calculate(Rect icon, Rect work, Size menu, double scale)
    {
        double width = menu.Width * scale, height = menu.Height * scale;
        bool top = icon.Top - work.Top < height && work.Bottom - icon.Bottom > icon.Top - work.Top;
        double x = Math.Clamp(icon.Left + icon.Width / 2 - width / 2, work.Left, Math.Max(work.Left, work.Right - width));
        double y = Math.Clamp(top ? icon.Bottom : icon.Top - height, work.Top, Math.Max(work.Top, work.Bottom - height));
        double arrow = Math.Clamp((icon.Left + icon.Width / 2 - x) / scale, 28, menu.Width - 28);
        return (new Point(x, y), top, arrow);
    }
}
