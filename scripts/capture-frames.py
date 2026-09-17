#!/usr/bin/env python3
"""Render FKNRTD.CLI frames to PNG for the documentation.

The frames come from the real renderer, not from a mock-up: the self-test harness prints a named
scene as ANSI, and this turns that into an image. Keeping the captures generated is the same
argument as keeping the portal generated — a screenshot committed once is free to go on showing a
keymap the product no longer has.

Requires Pillow and a monospace font. It is a documentation tool, not part of the shipped product;
the zero-dependency rule in AGENTS.md is about what `fknrtd` itself installs.

    python scripts/capture-frames.py            # every documented frame
    python scripts/capture-frames.py wizard     # just one
"""

import os
import re
import subprocess
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "docs", "screenshots")
SELFTEST = os.path.join(ROOT, "tests", "FKNRTD.SelfTest", "FKNRTD.SelfTest.csproj")

# scene name -> (output file, columns, rows)
FRAMES = {
    "dash-wide": ("overview", 150, 38),
    "dash-med": ("overview", 100, 30),
    "dash-narrow": ("overview", 74, 26),
    "new-task": ("wizard-brief", 118, 34),
    "choose-agent": ("wizard-auditor", 118, 30),
    "help": ("help-search", 112, 34),
    "commands": ("palette", 112, 32),
    "inspect": ("inspect", 112, 34),
    "land": ("land", 112, 30),
    "settings": ("settings", 112, 32),
    "welcome": ("welcome", 118, 32),
}

BACKGROUND = (13, 17, 23)
FOREGROUND = (230, 237, 243)
SEQUENCE = re.compile("\x1b\\[([0-9;]*)m")
PADDING = 18
POINT_SIZE = 19
FONT = "C:/Windows/Fonts/CascadiaMono.ttf"


def ansi_cells(frame):
    """Split an ANSI frame into rows of (char, foreground, background, bold)."""
    rows = []
    fg, bg, bold = FOREGROUND, None, False
    for line in frame.split("\n"):
        cells, index = [], 0
        for match in SEQUENCE.finditer(line):
            cells.extend((ch, fg, bg, bold) for ch in line[index:match.start()])
            index = match.end()
            code = match.group(1) or "0"
            if code in ("", "0"):
                fg, bg, bold = FOREGROUND, None, False
            elif code == "1":
                bold = True
            elif code.startswith("38;2;"):
                fg = tuple(int(v) for v in code.split(";")[2:5])
            elif code.startswith("48;2;"):
                bg = tuple(int(v) for v in code.split(";")[2:5])
        cells.extend((ch, fg, bg, bold) for ch in line[index:])
        rows.append(cells)
    return rows


def load_fonts():
    """Cascadia Mono, because the dashboard is drawn almost entirely in box-drawing and geometric
    glyphs that Consolas does not carry — it renders them as empty boxes, which makes a capture
    look broken when the terminal it came from is fine."""
    regular = ImageFont.truetype(FONT, POINT_SIZE)
    try:
        heavy = ImageFont.truetype(FONT, POINT_SIZE)
        heavy.set_variation_by_name("Bold")
    except Exception:
        heavy = regular
    return regular, heavy


def render(frame, path):
    regular, heavy = load_fonts()
    probe = Image.new("RGB", (10, 10))
    box = ImageDraw.Draw(probe).textbbox((0, 0), "M", font=regular)
    cell_w = box[2] - box[0]
    cell_h = int((box[3] - box[1]) * 1.62)

    rows = ansi_cells(frame)
    width = max(len(row) for row in rows)
    image = Image.new("RGB", (width * cell_w + PADDING * 2, len(rows) * cell_h + PADDING * 2), BACKGROUND)
    draw = ImageDraw.Draw(image)

    for y, row in enumerate(rows):
        for x, (char, fg, bg, bold) in enumerate(row):
            left, top = PADDING + x * cell_w, PADDING + y * cell_h
            if bg:
                draw.rectangle([left, top, left + cell_w, top + cell_h], fill=bg)
            if char != " ":
                draw.text((left, top), char, font=heavy if bold else regular, fill=fg)

    image.save(path)
    return image.size


def capture(name, scene, columns, rows):
    result = subprocess.run(
        ["dotnet", "run", "--project", SELFTEST, "-c", "Release", "--no-build",
         "--", "render", scene, str(columns), str(rows), "-color"],
        capture_output=True, text=True, encoding="utf-8", cwd=ROOT)
    if result.returncode != 0:
        raise SystemExit(f"render {scene} failed:\n{result.stderr}")
    path = os.path.join(OUT, name + ".png")
    size = render(result.stdout.rstrip("\n"), path)
    print(f"  {name}.png  {size[0]}x{size[1]}  from scene '{scene}' at {columns}x{rows}")


def main():
    os.makedirs(OUT, exist_ok=True)
    wanted = sys.argv[1:] or list(FRAMES)
    unknown = [name for name in wanted if name not in FRAMES]
    if unknown:
        raise SystemExit(f"unknown frame(s): {', '.join(unknown)}\nknown: {', '.join(FRAMES)}")
    print(f"Capturing {len(wanted)} frame(s) into docs/screenshots:")
    for name in wanted:
        capture(name, *FRAMES[name])


if __name__ == "__main__":
    main()
