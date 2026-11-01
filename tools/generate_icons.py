"""Build-time only: download genuine Lucide SVG icons and rasterize into PNG.

Sources: https://github.com/lucide-icons/lucide/tree/main/icons
Licensed per bundled vendor LUCIDE-LICENSE.txt (ISC and Feather MIT).
No external runtime dependencies or network requests in the resulting EXE.
"""
from pathlib import Path
import urllib.request
import fitz

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "Assets" / "lucide"
OUT.mkdir(parents=True, exist_ok=True)
ICONS = {
    "minus": "minus",
    "maximize": "square",
    "restore": "copy",
    "close": "x",
    "refresh": "refresh-cw",
    "arrow": "arrow-right",
    "restore-all": "rotate-ccw",
    "video": "video",
    "cursor": "mouse-pointer-2",
    "shield": "shield-check",
    "settings": "settings-2",
    "check": "check",
    "chevron": "chevron-down",
    "picture": "picture-in-picture-2",
    "circle-check": "circle-check",
    "window": "app-window",
}

for alias, upstream in ICONS.items():
    url = f"https://raw.githubusercontent.com/lucide-icons/lucide/main/icons/{upstream}.svg"
    request = urllib.request.Request(url, headers={"User-Agent": "PiPClickThrough-icon-build/1.1"})
    svg = urllib.request.urlopen(request, timeout=15).read().decode("utf-8")
    svg = svg.replace('stroke="currentColor"', 'stroke="#ffffff"')
    svg_path = OUT / (alias + ".svg")
    svg_path.write_text(svg, encoding="utf-8")
    doc = fitz.open(stream=svg.encode("utf-8"), filetype="svg")
    pix = doc[0].get_pixmap(matrix=fitz.Matrix(4, 4), alpha=True)
    pix.save(str(OUT / (alias + ".png")))
    doc.close()
    print(f"{alias:14s} <- {upstream:24s}  png={pix.width}x{pix.height}")

license_url = "https://raw.githubusercontent.com/lucide-icons/lucide/main/LICENSE"
request = urllib.request.Request(license_url, headers={"User-Agent": "PiPClickThrough-icon-build/1.1"})
(ROOT / "LUCIDE-LICENSE.txt").write_bytes(urllib.request.urlopen(request, timeout=15).read())
