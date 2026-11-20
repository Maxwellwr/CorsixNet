"""Draws the client's main menu backgrounds and window icon.

    python tools/menu-art/draw.py

Writes src/OpenWards.Client/Assets/mainmenu{720,1080}.dat.gz and .pal (the picture at each size, in 256 colours with
a 6-bit palette, as CorsixTH's mainmenu .dat/.pal pairs that MenuBackground reads), mainmenu_sign.png (the pixel-art
OpenWards sign, one pixel a cell, which MenuBackground lays over the picture where the menu leaves room) and
Icon.ico / Icon.bmp (from icon.png, an enamel plate with a 6).

The background is ward6.png (made by the project's owner with an image generator, a nod to Chekhov's Ward No. 6).
Needs Pillow and the Segoe UI fonts of Windows.
"""
import gzip
import os

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "..", "src", "OpenWards.Client", "Assets")
SS = 2  # supersampling
W, H = 1920, 1080
FONT_BOLD = r"C:\Windows\Fonts\segoeuib.ttf"
FONT_SEMI = r"C:\Windows\Fonts\seguisb.ttf"
SIZES = ((1280, 720), (1920, 1080))


def s(v):
    return int(round(v * SS))


def wordmark(height, colour, accent):
    """'OpenWards': 'Open' semibold in the accent colour, 'Wards' bold."""
    f1 = ImageFont.truetype(FONT_SEMI, s(height))
    f2 = ImageFont.truetype(FONT_BOLD, s(height))
    w1 = f1.getlength("Open")
    asc, desc = f2.getmetrics()
    im = Image.new("RGBA", (int(w1 + f2.getlength("Wards")) + s(8), asc + desc), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.text((0, 0), "Open", font=f1, fill=accent + (255,))
    d.text((w1 + s(2), 0), "Wards", font=f2, fill=colour + (255,))
    return im


def background():
    """ward6.png, without the sign: the client lays the sign over it where the menu leaves room."""
    return Image.open(os.path.join(HERE, "ward6.png")).convert("RGB").resize((W, H), Image.LANCZOS)


def sign():
    """The pixel-art OpenWards sign, one pixel a cell (the client draws each cell as a square of whole pixels), with
    a dark one-cell outline and a drop shadow, as pixel art has."""
    mark = wordmark(19, colour=(255, 255, 255), accent=(150, 240, 200))
    mark = mark.resize((mark.width // SS, mark.height // SS), Image.LANCZOS)
    # hard alpha, cut to the letters, one cell of room on each side for the outline and one more for the shadow
    alpha = mark.split()[3].point(lambda v: 255 if v > 110 else 0)
    mark.putalpha(alpha)
    mark = mark.crop(alpha.getbbox())
    gw, gh = mark.width + 3, mark.height + 3
    logo = Image.new("RGBA", (gw, gh), (0, 0, 0, 0))
    logo.alpha_composite(mark, (1, 1))
    alpha = logo.split()[3]
    outline = alpha.filter(ImageFilter.MaxFilter(3))
    shadow = Image.new("L", (gw, gh), 0)
    shadow.paste(outline, (1, 1))
    base = Image.new("RGBA", (gw, gh), (0, 0, 0, 0))
    base.paste((20, 24, 34, 140), (0, 0, gw, gh), shadow)
    base.paste((24, 30, 40, 255), (0, 0, gw, gh), outline)
    base.alpha_composite(logo)
    return base


def to_indexed(img, w, h):
    """Scaled to w by h (the picture's own 16:9), then 256 colours that a 6-bit palette can show."""
    r = img.resize((w, h), Image.LANCZOS)
    r = r.point(lambda v: (v >> 2) * 255 // 63)
    return r.quantize(colors=256, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.FLOYDSTEINBERG)


def save_pair(q, base):
    pal = q.getpalette()[:768]
    pal += [0] * (768 - len(pal))
    with gzip.GzipFile(base + ".dat.gz", "wb", compresslevel=9, mtime=0) as f:
        f.write(q.tobytes())
    with open(base + ".pal", "wb") as f:
        f.write(bytes(min(63, round(v * 63 / 255)) for v in pal))


def icons():
    """icon.png (the plate of Ward No. 6, made with the same image generator) as the exe and window icons."""
    icon = Image.open(os.path.join(HERE, "icon.png")).convert("RGBA").resize((256, 256), Image.LANCZOS)
    icon.save(os.path.join(ASSETS, "Icon.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    icon.resize((128, 128), Image.LANCZOS).save(os.path.join(ASSETS, "Icon.bmp"))


if __name__ == "__main__":
    picture = background()
    for w, h in SIZES:
        save_pair(to_indexed(picture, w, h), os.path.join(ASSETS, f"mainmenu{h}"))
    sign().save(os.path.join(ASSETS, "mainmenu_sign.png"), optimize=True)
    icons()
    print("wrote the main menu backgrounds and the icon to", os.path.normpath(ASSETS))
