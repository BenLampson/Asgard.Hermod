# Hermod application icons

Selected concept: **01 — Winged Helm**, from `design/Hermod.pen`.
Every shipped icon has a transparent background; only the helmet and crest are drawn.

| Asset | Purpose | Primary / accent |
| --- | --- | --- |
| `hermod-logo.svg` | Editable vector master, 196 × 196 canvas | Ivory / amber |
| `hermod.png`, `hermod.ico` | Light theme; default executable icon | `#243247` / `#A3651C` |
| `hermod-dark.png`, `hermod-dark.ico` | Dark theme | `#E9E2D4` / `#D6A760` |

Both PNG files are 512 × 512. Each ICO contains RGBA frames at 16, 20, 24, 32,
40, 48, 64, 128, and 256 pixels.
The PNGs retain the vector master's presentation spacing. ICO frames use a
centered square crop with the visible mark filling approximately 92% of its
height, so the taskbar and Explorer icons remain legible at small sizes.

The theme dictionaries supply `HermodLogo` and `HermodWindowIcon`. Dynamic WPF
resources update the main caption and native window icons immediately when the
theme changes. Load the ICO resources as `ImageSource`, which preserves the
decoder's frames for WPF's native large/small icon selection. `BitmapImage`
only exposes the first 16-pixel frame and causes undersized native icons.
Dialog captions inherit their window icon. Windows Explorer uses
the light variant embedded in the executable through `ApplicationIcon`;
the executable's static file icon does not change with Hermod's theme.

To regenerate all four raster files from the vector master, run from the repository
root on Windows with Python and Pillow installed:

```powershell
powershell.exe -NoProfile -STA -Command "& ([scriptblock]::Create((Get-Content -LiteralPath 'design/export-brand-icons.ps1' -Raw -Encoding UTF8)))"
```

The script renders SVG path geometry with WPF on a transparent canvas, then uses
Pillow to encode the ICO frames. It keeps the approved shape and composition.
