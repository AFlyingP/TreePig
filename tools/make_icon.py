# generates the app icon, run from the repo root:
#   python tools/make_icon.py
import os

from PIL import Image, ImageDraw

SIZE = 256


def pig():
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    pink = (246, 166, 180, 255)
    dark = (150, 62, 82, 255)
    snout_pink = (238, 130, 150, 255)

    # ears
    d.polygon([(52, 118), (86, 34), (126, 92)], fill=pink, outline=dark)
    d.polygon([(204, 118), (170, 34), (130, 92)], fill=pink, outline=dark)

    # head
    d.ellipse((28, 72, 228, 232), fill=pink, outline=dark, width=6)

    # eyes
    d.ellipse((80, 122, 102, 144), fill=(40, 30, 34, 255))
    d.ellipse((154, 122, 176, 144), fill=(40, 30, 34, 255))

    # snout
    d.ellipse((88, 156, 168, 212), fill=snout_pink, outline=dark, width=5)
    d.ellipse((108, 172, 124, 198), fill=(120, 44, 62, 255))
    d.ellipse((132, 172, 148, 198), fill=(120, 44, 62, 255))

    return img


img = pig()
out = os.path.join(os.path.dirname(__file__), "..", "TreePig", "TreePig.ico")
img.save(out, format="ICO", sizes=[(256, 256), (48, 48), (32, 32), (16, 16)])
print("wrote", os.path.normpath(out))
