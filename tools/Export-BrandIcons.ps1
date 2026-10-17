param(
    [string]$Python = 'python',
    [string]$AssetDirectory = (Join-Path (Get-Location) 'src\Asgard.Hermod.UI\Assets\Brand')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
[xml]$source = Get-Content -LiteralPath (Join-Path $assetDirectory 'hermod-logo.svg') -Raw -Encoding UTF8
$variants = @(
    @{ Name = 'hermod'; Primary = '#243247'; Accent = '#A3651C' },
    @{ Name = 'hermod-dark'; Primary = '#E9E2D4'; Accent = '#D6A760' }
)

# 直接渲染矢量轮廓，不绘制底色，保留边缘抗锯齿和完整透明通道。
foreach ($variant in $variants) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $drawing = $visual.RenderOpen()
    $scale = 512.0 / 196.0
    $drawing.PushTransform((New-Object System.Windows.Media.ScaleTransform($scale, $scale)))
    foreach ($path in $source.svg.path) {
        $translation = [regex]::Match($path.transform, 'translate\(([\d.]+) ([\d.]+)\)')
        $x = [double]::Parse($translation.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture)
        $y = [double]::Parse($translation.Groups[2].Value, [Globalization.CultureInfo]::InvariantCulture)
        $drawing.PushTransform((New-Object System.Windows.Media.TranslateTransform($x, $y)))
        $color = if ($path.fill -eq '#D6A760') { $variant.Accent } else { $variant.Primary }
        $brush = [System.Windows.Media.BrushConverter]::new().ConvertFromInvariantString($color)
        $geometry = [System.Windows.Media.Geometry]::Parse($path.d)
        $drawing.DrawGeometry($brush, $null, $geometry)
        $drawing.Pop()
    }
    $drawing.Pop()
    $drawing.Close()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(512, 512, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create((Join-Path $assetDirectory ($variant.Name + '.png')))
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
}

# 标题栏 PNG 保留设计画布；ICO 收紧留白，让任务栏中的主体占据约 92% 的高度。
@'
from pathlib import Path
from PIL import Image
import sys

directory = Path(sys.argv[1])
sizes = [(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)]
for name in ('hermod', 'hermod-dark'):
    image = Image.open(directory / (name + '.png')).convert('RGBA')
    bounds = image.getchannel('A').getbbox()
    left, top, right, bottom = bounds
    side = round(max(right - left, bottom - top) / 0.92)
    x = round((left + right - side) / 2)
    y = round((top + bottom - side) / 2)
    icon = image.crop((x, y, x + side, y + side))
    icon.save(directory / (name + '.ico'), format='ICO', sizes=sizes)
'@ | & $Python - $assetDirectory
if ($LASTEXITCODE -ne 0) { throw 'ICO export failed.' }
