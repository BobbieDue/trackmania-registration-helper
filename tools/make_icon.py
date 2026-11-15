#!/usr/bin/env python3
"""Draws the app icon: "C®AżΨ" in the TMUF server-name colours on the site's dark rounded square.
Run: python3 make_icon.py <out.ico> [preview.png]   (needs Pillow and the Inter font)"""
import sys
from PIL import Image, ImageDraw, ImageFont

FONT = '/usr/share/fonts/opentype/inter/Inter-Black.otf'
# from the server name: $bf0C $6f0® $0f6A $0fcż $0cfΨ  (TMUF 3-digit colours, each digit doubled)
LETTERS = [('C', '#BBFF00'), ('®', '#66FF00'), ('A', '#00FF66'), ('ż', '#00FFCC'), ('Ψ', '#00CCFF')]
SMALL = [('C', '#BBFF00'), ('Ψ', '#00CCFF')]   # 16-32 px: just the ends, so it stays readable
BG, BORDER = '#0b1222', '#1e2a40'


def draw(size, letters):
    s = size * 4   # draw big, then shrink for smooth edges
    im = Image.new('RGBA', (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    r = int(s * 0.2)
    d.rounded_rectangle([0, 0, s - 1, s - 1], r, fill=BG)
    d.rounded_rectangle([int(s * .05)] * 2 + [s - 1 - int(s * .05)] * 2, int(r * .8), outline=BORDER, width=max(4, s // 40))
    # largest font where the word fits in 84% of the width
    lo, hi = 10, s
    while hi - lo > 2:
        mid = (lo + hi) // 2
        f = ImageFont.truetype(FONT, mid)
        w = sum(f.getlength(ch) for ch, _ in letters) + (len(letters) - 1) * mid * -0.02
        lo, hi = (mid, hi) if w <= s * 0.84 and mid <= s * 0.62 else (lo, mid)
    f = ImageFont.truetype(FONT, lo)
    widths = [f.getlength(ch) for ch, _ in letters]
    total = sum(widths) + (len(letters) - 1) * lo * -0.02
    x = (s - total) / 2
    top, bottom = f.getbbox('CAΨ')[1], f.getbbox('CAΨ')[3]
    y = (s - (bottom - top)) / 2 - top
    for (ch, col), w in zip(letters, widths):
        d.text((x, y), ch, font=f, fill=col)
        x += w + lo * -0.02
    return im.resize((size, size), Image.LANCZOS)


if __name__ == '__main__':
    out = sys.argv[1]
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    imgs = {n: draw(n, SMALL if n <= 32 else LETTERS) for n in sizes}
    imgs[256].save(out, format='ICO', sizes=[(n, n) for n in sizes], append_images=[imgs[n] for n in sizes[:-1]])
    if len(sys.argv) > 2:
        sheet = Image.new('RGBA', (256 + 128 + 64 + 48 + 32 + 16 + 70, 260), '#f4f4f4')
        x = 5
        for n in (256, 128, 64, 48, 32, 16):
            sheet.paste(imgs[n], (x, 2), imgs[n]); x += n + 10
        sheet.save(sys.argv[2])
