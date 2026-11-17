"""Generate the static Windows app icon from the real KnightWin knight mark and wheel.

Run with: python Resources/make_icon.py  (requires Pillow only while rebuilding assets).
The .ico itself is a static emblem; the wheel in MainWindow remains WPF-animated.
"""
from pathlib import Path
from math import cos, sin, radians
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
SIZE = 1024
C = SIZE // 2

# Slightly dimensional charcoal tile, transparent outside the rounded outline.
base = Image.new('RGBA', (SIZE, SIZE), (0, 0, 0, 0))
shade = Image.new('RGBA', (SIZE, SIZE), (0, 0, 0, 0))
pix = shade.load()
for y in range(SIZE):
    for x in range(SIZE):
        t = y / (SIZE - 1)
        glow = max(0, 1 - ((x - C) ** 2 + (y - 300) ** 2) ** .5 / 770) * 6
        pix[x, y] = (int(29 - 10*t + glow), int(32 - 11*t + glow),
                     int(39 - 13*t + glow), 255)
mask = Image.new('L', (SIZE, SIZE))
ImageDraw.Draw(mask).rounded_rectangle((3, 3, SIZE-4, SIZE-4), radius=230, fill=255)
base.paste(shade, (0, 0), mask)

wheel = Image.new('RGBA', (SIZE, SIZE), (0,0,0,0))
d = ImageDraw.Draw(wheel)
outer, inner = 405, 281
# Wheel wedge geometry echoes WheelControl's 12 segments, rather than a generic gear.
colors = ['#D9C6A8', '#37708B', '#338BBB', '#2D5874',
          '#52A5D3', '#355F7C', '#DEC7A7', '#3D87AC',
          '#2A5975', '#56A8D7', '#397696', '#318DBB']
for i, color in enumerate(colors):
    lo = -90 + i*30 + 1.6
    hi = -90 + (i+1)*30 - 1.6
    steps = 14
    angles = [radians(lo+(hi-lo)*j/steps) for j in range(steps+1)]
    points = [(C+outer*cos(a), C+outer*sin(a)) for a in angles]
    points += [(C+inner*cos(a), C+inner*sin(a)) for a in reversed(angles)]
    d.polygon(points, fill=color)
d.ellipse((C-outer,C-outer,C+outer,C+outer), outline=(184,213,224,150), width=6)
d.ellipse((C-inner,C-inner,C+inner,C+inner), fill='#141922', outline=(222,204,169,190), width=7)
# A short fixed pointer at 12 o'clock makes the icon read as the app's spinning wheel.
d.polygon([(C-51, C-outer-27), (C+51,C-outer-27), (C,C-outer+67)],
          fill='#F0DCC0')
d.line([(C-51,C-outer-27),(C,C-outer+67),(C+51,C-outer-27)],
       fill='#26313B', width=7, joint='curve')
base.alpha_composite(wheel)

logo = Image.open(ROOT/'Assets/logo.png').convert('RGBA')
logo = logo.resize((548,548), Image.Resampling.LANCZOS)
# The source mark's visible pixels sit mostly toward the bottom-right of its
# transparent 640px canvas. Center the VISIBLE silhouette in the circular hub,
# not its square image bounds. Ignore faint antialiasing fringe in the bbox.
visible = logo.getchannel('A').point(lambda a: 255 if a >= 64 else 0).getbbox()
assert visible is not None
left, top, right, bottom = visible
logo_x = round(C - (left + right) / 2)
logo_y = round(C - (top + bottom) / 2)
base.alpha_composite(logo, (logo_x, logo_y))

base.save(ROOT/'Assets/app-icon.png', optimize=True)
base.save(ROOT/'Resources/app.ico', format='ICO',
          sizes=[(16,16),(20,20),(24,24),(32,32),(40,40),
                 (48,48),(64,64),(96,96),(128,128),(256,256)])
print('Generated Assets/app-icon.png and Resources/app.ico')
