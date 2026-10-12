# Hermod application icon

Selected concept: **01 — Winged Helm**, from `design/Hermod.pen`.

- `hermod-logo.svg`: editable vector mark on a transparent 196 × 196 canvas.
- `hermod.png`: 512 × 512 application icon with the approved dark rounded background.
- `hermod.ico`: RGBA Windows icon containing 16, 20, 24, 32, 40, 48, 64, 128, and 256 pixel frames.

The palette is ivory `#E9E2D4`, amber `#D6A760`, and dark background `#10131A`.
The icon keeps the original composition and transparent rounded corners.

The executable uses `hermod.ico` through `ApplicationIcon`. The UI assembly embeds
the ICO and PNG as WPF resources. `HermodWindow` supplies the default native window
icon to all derived windows; the shared dialog caption displays that window icon.
The main caption uses the PNG for smooth scaling on high DPI displays.

To regenerate the raster assets, export the `01 · Winged Helm · editable vector`
frame (`LxL8a`) from Pencil as a PNG at 4× scale to `design/exports`, then run from
the repository root with Python and Pillow installed:

```python
from pathlib import Path
from PIL import Image

target = Path("src/Asgard.Hermod.UI/Assets/Brand")
image = Image.open("design/exports/LxL8a.png").convert("RGBA")
image.resize((512, 512), Image.Resampling.LANCZOS).save(target / "hermod.png")
image.save(target / "hermod.ico", format="ICO",
           sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
```
