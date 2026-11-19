"""Draws the client's main menu backgrounds and window icon.

    python tools/menu-art/draw.py

Writes src/OpenWards.Client/Assets/mainmenu{480,720,1080}.dat.gz and .pal (the picture cover-scaled to each size,
centred, in 256 colours with a 6-bit palette, as CorsixTH's mainmenu .dat/.pal pairs that MenuBackground reads) and
Icon.ico / Icon.bmp (the OpenWards emblem: a white cross with an open door in the middle).

The background is ward6.png (made by the project's owner with an image generator, a nod to Chekhov's Ward No. 6) with
a pixel-art OpenWards sign laid over it. Needs Pillow and the Segoe UI fonts of Windows.
"""
import gzip
import math
import os

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "..", "src", "OpenWards.Client", "Assets")
SS = 2  # supersampling
W, H = 1920, 1080
FONT_BOLD = r"C:\Windows\Fonts\segoeuib.ttf"
FONT_SEMI = r"C:\Windows\Fonts\seguisb.ttf"
SIZES = ((640, 480), (1280, 720), (1920, 1080))


def s(v):
    return int(round(v * SS))


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(len(a)))


def emblem(size, bg=(22, 140, 110), fg=(255, 255, 255), door=(255, 214, 120)):
    """The OpenWards emblem: a rounded square, a cross whose middle is an open door with warm light behind it."""
    n = s(size)
    im = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    r = n * 0.22
    mask = Image.new("L", (n, n), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, n - 1, n - 1], radius=r, fill=255)
    ImageDraw.Draw(im).rounded_rectangle([0, 0, n - 1, n - 1], radius=r, fill=bg + (255,))
    highlight = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    ImageDraw.Draw(highlight).rounded_rectangle([0, 0, n - 1, n * 0.55], radius=r, fill=(255, 255, 255, 34))
    im.alpha_composite(Image.composite(highlight, Image.new("RGBA", (n, n), (0, 0, 0, 0)), mask))
    d = ImageDraw.Draw(im)
    c = n / 2
    arm, reach, cr = n * 0.18, n * 0.40, n * 0.05
    d.rounded_rectangle([c - arm, c - reach, c + arm, c + reach], radius=cr, fill=fg + (255,))
    d.rounded_rectangle([c - reach, c - arm, c + reach, c + arm], radius=cr, fill=fg + (255,))
    dw, dh = n * 0.21, n * 0.36
    dx0, dy0 = c - dw / 2 + n * 0.02, c - dh / 2 + n * 0.01
    d.rectangle([dx0, dy0, dx0 + dw, dy0 + dh], fill=door + (255,))
    d.polygon([(dx0, dy0 + dh), (dx0 + dw, dy0 + dh), (dx0 + dw + n * 0.07, dy0 + dh + n * 0.05),
               (dx0 + n * 0.03, dy0 + dh + n * 0.05)], fill=lerp(door, fg, 0.55) + (255,))
    d.polygon([(dx0, dy0), (dx0 - dw * 0.40, dy0 - n * 0.03), (dx0 - dw * 0.40, dy0 + dh + n * 0.04), (dx0, dy0 + dh)],
              fill=lerp(bg, (0, 0, 0), 0.25) + (255,))
    hx, hy = dx0 - dw * 0.30, dy0 + dh * 0.52
    d.ellipse([hx - n * 0.012, hy - n * 0.012, hx + n * 0.012, hy + n * 0.012], fill=door + (255,))
    return im


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
    """ward6.png with a pixel-art OpenWards sign laid above the menu, over the yard."""
    img = Image.open(os.path.join(HERE, "ward6.png")).convert("RGB").resize((W, H), Image.LANCZOS).convert("RGBA")
    px = 4  # the sign is drawn on a grid of 4 by 4 screen pixels, like the picture's own pixels
    gw, gh = W // px, H // px
    logo = Image.new("RGBA", (gw, gh), (0, 0, 0, 0))
    em = emblem(24 / SS * 2).resize((24, 24), Image.LANCZOS)
    mark = wordmark(17, colour=(255, 255, 255), accent=(150, 240, 200))
    mark = mark.resize((mark.width // SS, mark.height // SS), Image.LANCZOS)
    x, y = 920 // px, 36 // px
    logo.alpha_composite(em, (x, y))
    logo.alpha_composite(mark, (x + 29, y - 2))
    # hard alpha, then a dark one-cell outline and drop shadow, as pixel art has
    alpha = logo.split()[3].point(lambda v: 255 if v > 110 else 0)
    logo.putalpha(alpha)
    outline = alpha.filter(ImageFilter.MaxFilter(3))
    shadow = Image.new("L", (gw, gh), 0)
    shadow.paste(outline, (1, 1))
    base = Image.new("RGBA", (gw, gh), (0, 0, 0, 0))
    base.paste((20, 24, 34, 140), (0, 0, gw, gh), shadow)
    base.paste((24, 30, 40, 255), (0, 0, gw, gh), outline)
    base.alpha_composite(logo)
    img.alpha_composite(base.resize((gw * px, gh * px), Image.NEAREST))
    return img.convert("RGB")


def to_indexed(img, w, h):
    """Cover-scaled and centred to w by h, then 256 colours that a 6-bit palette can show."""
    factor = max(w / W, h / H)
    rw, rh = math.ceil(W * factor), math.ceil(H * factor)
    r = img.resize((rw, rh), Image.LANCZOS)
    left, top = (rw - w) // 2, (rh - h) // 2
    r = r.crop((left, top, left + w, top + h))
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
    em = emblem(256 / SS).resize((256, 256), Image.LANCZOS)
    em.save(os.path.join(ASSETS, "Icon.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    em.resize((128, 128), Image.LANCZOS).save(os.path.join(ASSETS, "Icon.bmp"))


if __name__ == "__main__":
    picture = background()
    for w, h in SIZES:
        save_pair(to_indexed(picture, w, h), os.path.join(ASSETS, f"mainmenu{h}"))
    icons()
    print("wrote the main menu backgrounds and the icon to", os.path.normpath(ASSETS))
