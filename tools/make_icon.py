"""Generates assets/app.ico and assets/logo.png (requires Pillow)."""
from PIL import Image, ImageDraw

S = 1024

def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))

def make():
    # Background: rounded square with a vertical gradient (Material primary purple)
    grad = Image.new("RGBA", (S, S))
    top, bot = (134, 107, 204, 255), (92, 62, 168, 255)
    gd = ImageDraw.Draw(grad)
    for y in range(S):
        gd.line([(0, y), (S, y)], fill=lerp(top, bot, y / (S - 1)))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([40, 40, S - 40, S - 40], radius=230, fill=255)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)
    W = (255, 255, 255, 255)
    # Monitor
    mx0, my0, mx1, my1 = 210, 250, 814, 690
    d.rounded_rectangle([mx0, my0, mx1, my1], radius=56, fill=W)
    # Screen (cut-out tinted)
    sx0, sy0, sx1, sy1 = mx0 + 52, my0 + 52, mx1 - 52, my1 - 52
    d.rounded_rectangle([sx0, sy0, sx1, sy1], radius=22, fill=(110, 82, 186, 255))
    # Stand
    d.rounded_rectangle([462, my1 - 10, 562, 790], radius=10, fill=W)
    d.rounded_rectangle([352, 770, 672, 830], radius=30, fill=W)

    # Expand arrows inside the screen (diagonal, two arrowheads)
    cx, cy = (sx0 + sx1) / 2, (sy0 + sy1) / 2
    a = 120  # half-length along diagonal (in x and y)
    lw = 34
    p0 = (cx - a, cy + a * 0.62)
    p1 = (cx + a, cy - a * 0.62)
    d.line([p0, p1], fill=W, width=lw)
    import math
    ang = math.atan2(p1[1] - p0[1], p1[0] - p0[0])
    def head(tip, ang):
        L = 92
        for da in (math.radians(150), math.radians(-150)):
            e = (tip[0] + L * math.cos(ang + da), tip[1] + L * math.sin(ang + da))
            d.line([tip, e], fill=W, width=lw)
        d.ellipse([tip[0] - lw / 2, tip[1] - lw / 2, tip[0] + lw / 2, tip[1] + lw / 2], fill=W)
    head(p1, ang)
    head(p0, ang + math.pi)
    return img

import os
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets")
img = make()
img.save(os.path.join(out, "app.ico"), sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
img.resize((128, 128), Image.LANCZOS).save(os.path.join(out, "logo.png"))
