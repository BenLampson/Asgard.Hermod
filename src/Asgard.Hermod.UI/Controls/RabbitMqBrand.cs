namespace Asgard.Hermod.UI.Controls;

/// <summary>RabbitMQ 官方独立 SVG 标志，沿用 Pencil 原稿路径和品牌色，不在运行时下载。</summary>
internal static class RabbitMqBrand
{
    internal static readonly Geometry Geometry = Create();
    internal static readonly Brush Brush = new SolidColorBrush(Color.FromRgb(255, 102, 0));
    private static Geometry Create()
    {
        var geometry = System.Windows.Media.Geometry.Parse("M39.42 17.37h-12.77a1.59 1.59 0 0 1-1.6-1.6v-12.77a1.59 1.59 0 0 0-1.6-1.59h-4.78a1.59 1.59 0 0 0-1.6 1.59v12.77a1.59 1.59 0 0 1-1.6 1.6h-4.78a1.59 1.59 0 0 1-1.6-1.6v-12.77a1.59 1.59 0 0 0-1.6-1.6h-4.79a1.59 1.59 0 0 0-1.59 1.6v36.72a1.59 1.59 0 0 0 1.6 1.6h36.71a1.59 1.59 0 0 0 1.6-1.6v-20.72a1.59 1.59 0 0 0-1.6-1.63z M33 30.93a2.39 2.39 0 0 1-2.39 2.4h-3.2a2.39 2.39 0 0 1-2.39-2.4v-3.19a2.39 2.39 0 0 1 2.39-2.4h3.2a2.39 2.39 0 0 1 2.39 2.4z");
        geometry.Freeze(); return geometry;
    }
}
