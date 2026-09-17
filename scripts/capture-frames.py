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

import io
import os
import re
import subprocess
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "docs", "screenshots")
SELFTEST = os.path.join(ROOT, "tests", "FKNRTD.SelfTest", "FKNRTD.SelfTest.csproj")
FKNRTD = os.path.join(ROOT, "src", "FKNRTD.Cli", "bin", "Release", "net10.0", "fknrtd.exe")

# scene name -> (output file, columns, rows)
FRAMES = {
    "dash-wide": ("overview", 150, 38),
    "dash-med": ("overview", 100, 30),
    "dash-narrow": ("overview", 74, 26),
    "new-task": ("wizard-brief", 118, 34),
    "review": ("wizard-review", 118, 26),
    "choose-agent": ("wizard-auditor", 118, 30),
    "help": ("help-search", 112, 34),
    "commands": ("palette", 112, 32),
    "inspect": ("inspect", 112, 34),
    "land": ("land", 112, 30),
    "diff": ("diff", 112, 30),
    "prompts": ("prompts", 112, 32),
    "settings": ("settings", 112, 32),
    "agents": ("agents", 112, 30),
    "agent-log": ("logs-json", 112, 26),
    "welcome": ("welcome", 118, 32),
}

# Frames that are a command's own output rather than a dashboard scene. Each runs against a
# throwaway workspace so the capture shows real output and not a transcription of it.
COMMANDS = {
    "doctor": ["doctor", "-color"],
    "statusline": ["telemetry", "claude-statusline", "-color"],
}

STATUSLINE_PAYLOAD = (
    '{"workspace":{"current_dir":%s},'
    '"context_window":{"used_percentage":38},'
    '"rate_limits":{"five_hour":{"used_percentage":22},"seven_day":{"used_percentage":36}}}'
)

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


def temporary_workspace():
    """A throwaway Git repository with a workspace in it, for the command captures."""
    import tempfile

    # A fixed, short, plausible name: doctor prints absolute paths, and a random temp directory
    # makes the capture both wide and obviously synthetic.
    import shutil
    root = os.path.join(tempfile.gettempdir(), "aurora-api")
    shutil.rmtree(root, ignore_errors=True)
    os.makedirs(root, exist_ok=True)
    run = lambda *args: subprocess.run(args, cwd=root, capture_output=True, text=True)
    run("git", "init", "-q", "-b", "main", ".")
    with io.open(os.path.join(root, "README.md"), "w", encoding="utf-8") as handle:
        handle.write("# aurora-api\n")
    # A project file so setup detects build and test commands. That makes the doctor capture show
    # a fully healthy workspace, which is the representative case and a far narrower frame than the
    # advisory about having nothing that verifies the work.
    with io.open(os.path.join(root, "aurora-api.csproj"), "w", encoding="utf-8") as handle:
        handle.write('<Project Sdk="Microsoft.NET.Sdk"></Project>')
    run("git", "add", "-A")
    run("git", "-c", "user.email=a@b", "-c", "user.name=n", "commit", "-qm", "init")
    subprocess.run([FKNRTD, "init", "-yes", "-root", root], capture_output=True, text=True)
    return root


def capture_command(name, arguments):
    root = temporary_workspace()
    payload = None
    if name == "statusline":
        import json
        payload = STATUSLINE_PAYLOAD % json.dumps(root)

    result = subprocess.run(
        [FKNRTD] + arguments + ["-root", root],
        input=payload, capture_output=True, text=True, encoding="utf-8", cwd=root)
    frame = (result.stdout or "").rstrip("\n")
    if not frame:
        raise SystemExit(f"{name} produced no output:\n{result.stderr}")

    # Pad to a tidy rectangle so the image is not ragged.
    rows = frame.split("\n")
    width = max(len(SEQUENCE.sub("", row)) for row in rows) + 2
    frame = "\n".join(row + " " * (width - len(SEQUENCE.sub("", row))) for row in rows)
    path = os.path.join(OUT, name + ".png")
    size = render(frame, path)
    print(f"  {name}.png  {size[0]}x{size[1]}  from `fknrtd {' '.join(arguments)}`")


def main():
    os.makedirs(OUT, exist_ok=True)
    known = list(FRAMES) + list(COMMANDS)
    wanted = sys.argv[1:] or known
    unknown = [name for name in wanted if name not in known]
    if unknown:
        raise SystemExit(f"unknown frame(s): {', '.join(unknown)}\nknown: {', '.join(known)}")
    print(f"Capturing {len(wanted)} frame(s) into docs/screenshots:")
    for name in wanted:
        if name in COMMANDS:
            capture_command(name, COMMANDS[name])
        else:
            capture(name, *FRAMES[name])


if __name__ == "__main__":
    main()
