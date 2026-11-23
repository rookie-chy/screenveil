"""Regenerate screenveil.ico from a square-ish source image.

Usage:  python make_icon.py path\\to\\source.png

Crops around the face, knocks out the near-white background, and composes the
result on a rounded dark tile at 16/24/32/48/64/128/256 px.
"""
import os
import sys
from PIL import Image, ImageDraw

if len(sys.argv) < 2:
    sys.exit("usage: python make_icon.py <source image>")

SRC = sys.argv[1]
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "screenveil.ico")

img = Image.open(SRC).convert("RGBA")
W, H = img.size

# face/mask area, as a fraction of the source image
cx, cy = int(W * 0.491), int(H * 0.400)
side = int(min(W, H) * 0.47)
left = max(0, cx - side // 2)
top = max(0, cy - side // 2)
crop = img.crop((left, top, left + side, top + side))

# flood-fill the background white from the four corners
key = (255, 0, 255, 255)
for xy in [(0, 0), (side - 1, 0), (0, side - 1), (side - 1, side - 1)]:
    ImageDraw.floodfill(crop, xy, key, thresh=42)

px = crop.load()
for y in range(side):
    for x in range(side):
        r, g, b, a = px[x, y]
        if (r, g, b) == key[:3]:
            px[x, y] = (0, 0, 0, 0)
        elif r >= 232 and g >= 232 and b >= 232:
            px[x, y] = (0, 0, 0, 0)
        elif a == 255 and r > 215 and g > 215 and b > 215:
            px[x, y] = (r, g, b, a)

bbox = crop.split()[-1].getbbox()
if bbox:
    crop = crop.crop(bbox)


def compose(size):
    tile = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(tile)
    pad = max(1, int(size * 0.03))
    d.rounded_rectangle([pad, pad, size - pad - 1, size - pad - 1],
                        radius=int(size * 0.22), fill=(26, 28, 40, 255))
    target = int(size * 0.88)
    w, h = crop.size
    scale = min(target / w, target / h)
    im = crop.resize((max(1, int(w * scale)), max(1, int(h * scale))), Image.LANCZOS)
    tile.alpha_composite(im, ((size - im.width) // 2, (size - im.height) // 2))
    return tile


sizes = [16, 24, 32, 48, 64, 128, 256]
frames = {s: compose(s) for s in sizes}
frames[256].save(OUT, format="ICO", sizes=[(s, s) for s in sizes])
print("wrote", OUT)
