#!/usr/bin/env python3
"""
Palette gate: the checks that would have caught the v1.9.0 "everything is black" build.

Luminance alone cannot catch it (both builds have white headings). What broke was:
  1. text with no contrast against the surface it sits on,
  2. surfaces with no separation from each other, so the whole window became one flat slab.

Reads the palette straight out of ThemeManager.cs so it tests the shipped values.
"""
import re, sys
from pathlib import Path

SRC = Path(__file__).resolve().parents[1] / 'KnightWin.Randomizer/Theme/ThemeManager.cs'
src = open(SRC).read()

# split the two theme blocks
blocks = re.findall(r'if \(dark\)\s*\{(.*?)\n            \}\s*\n            else\s*\{(.*?)\n            \}', src, re.S)
dark, light = blocks[0]

def colours(block):
    return {k: v for k, v in re.findall(r'B\("(\w+)",\s*"#?([0-9A-Fa-f]{6,8})"\)', block)}

def parse(h):
    h = h.lstrip("#")
    if len(h) == 6: h = "FF" + h
    a, r, g, b = (int(h[i:i+2], 16) for i in (0, 2, 4, 6))
    return a, r, g, b

def over(fg, bg):
    """flatten a translucent colour onto an opaque backdrop"""
    a, r, g, b = parse(fg)
    af = a / 255
    _, br, bgc, bb = parse(bg)
    return tuple(round(f * af + b_ * (1 - af)) for f, b_ in zip((r, g, b), (br, bgc, bb)))

def lum(c):
    def ch(v):
        v /= 255
        return v / 12.92 if v <= 0.03928 else ((v + 0.055) / 1.055) ** 2.4
    return 0.2126 * ch(c[0]) + 0.7152 * ch(c[1]) + 0.0722 * ch(c[2])

def ratio(a, b):
    la, lb = lum(a), lum(b)
    hi, lo = max(la, lb), min(la, lb)
    return (hi + 0.05) / (lo + 0.05)

# Composite the actual alpha stack against both dark and bright desktop samples.
# Root is the sole page base. Chrome overlays only the title/rail, not the page.
fails = []
for name, block, wallpaper_alpha, chrome_alpha in (("dark",dark,.12,.04),("light",light,.05,.02)):
    c = colours(block)
    for wallpaper in ((20,20,20),(235,210,170)):
        def mix(h, bg):
            a,r,g,b = parse(h)
            t = a / 255
            return tuple(round(f*t + u*(1-t)) for f,u in zip((r,g,b),bg))
        page = mix(c["WindowBg"], wallpaper)
        card = mix(c["CardBg"], page)
        chrome = mix(c["ChromeBg"], page)
        for surface_name, surface in (("page",page),("card",card)):
            for fg, minimum in (("TextPrimary",4.5),("TextSecondary",3.0),("TextTertiary",3.0)):
                result=ratio(mix(c[fg],surface),surface)
                if result < minimum:
                    fails.append(f"{name} {wallpaper} {surface_name}: {fg} contrast {result:.2f} < {minimum}")
        sep = abs(lum(page)-lum(card))
        if sep < .004:
            fails.append(f"{name} {wallpaper}: card/page separation {sep:.4f} < .004")
        print(f"{name:5s} wallpaper={wallpaper} page={page} card={card} chrome={chrome} separation={sep:.4f}")
    a=parse(c['WindowBg'])[0]/255
    b=parse(c['ChromeBg'])[0]/255
    page_visible=1-a
    chrome_visible=(1-a)*(1-b)*(1-3/255) # tint's 1% wash
    if abs(page_visible-wallpaper_alpha) > .008 or abs(chrome_visible-chrome_alpha) > .008:
        fails.append(f"{name}: alpha stack {page_visible:.3f}/{chrome_visible:.3f} != {wallpaper_alpha}/{chrome_alpha}")
if fails:
    for f in fails: print("FAIL ", f)
    print("PALETTE GATE FAILED")
    sys.exit(1)
print("PALETTE GATE OK")
