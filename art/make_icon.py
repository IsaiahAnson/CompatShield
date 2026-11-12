"""Draw the CompatShield icon: a steel shield in the rain, its face a hex force field around a gold check badge.
Writes icon.svg (vector master), icon-1024.png (preview) and icon.png (256x256, the size Thunderstore requires).
Usage: python -I make_icon.py      Needs Edge or Chrome for the render and Pillow for the resize.
The earlier drawing (a glowing gold seam down the shield) is kept in seam-version/."""
import math
import random
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image, ImageFilter

HERE = Path(__file__).resolve().parent
W = 1024
SHIELD = ("M 512 172 C 592 214, 692 234, 800 238 C 810 468, 668 742, 512 880 "
          "C 356 742, 214 468, 224 238 C 332 234, 432 214, 512 172 Z")
TOP_EDGE = "M 224 238 C 332 234, 432 214, 512 172 C 592 214, 692 234, 800 238"
RIGHT_EDGE = "M 800 238 C 810 468, 668 742, 512 880"
PLACE = "translate(512 512) scale(1.1) translate(-512 -530)"
CENTER = (512, 520)
EDGES = {
    "top_left": ((224, 238), (332, 234), (432, 214), (512, 172)),
    "top_right": ((512, 172), (592, 214), (692, 234), (800, 238)),
    "right": ((800, 238), (810, 468), (668, 742), (512, 880)),
}
CORE = (512, 470)   # middle of the badge, and of the cell the grid is built around
CELL = 64           # centre to corner of one field cell; the badge is exactly two cells across
RING = [(0, -1), (0, 1), (-1, -1), (-1, 0), (1, -1), (1, 0)]            # the six cells the badge sits on
FLARES = {(-1, -2): 0.62, (2, -1): 0.36, (-2, -1): 0.24, (1, -2): 0.2}  # cells lit where rain is hitting
TICK = [(-60, 4), (-20, 46), (64, -48)]
BROWSERS = [r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            r"C:\Program Files\Google\Chrome\Application\chrome.exe"]


def cubic(p, t):
    a, b, c, d = p
    u = 1 - t
    point = tuple(u ** 3 * a[i] + 3 * u * u * t * b[i] + 3 * u * t * t * c[i] + t ** 3 * d[i] for i in (0, 1))
    slope = tuple(3 * u * u * (b[i] - a[i]) + 6 * u * t * (c[i] - b[i]) + 3 * t * t * (d[i] - c[i]) for i in (0, 1))
    return point, slope


def pts(points):
    return " ".join(f"{x:.1f},{y:.1f}" for x, y in points)


def rain(rng):
    """Slanted streaks. One depth value sets length, width and brightness, so near drops read as near."""
    dx, dy = -0.2588, 0.9659
    out = []
    for _ in range(150):
        z = rng.random() ** 1.6
        x, y = rng.uniform(-80, W + 240), rng.uniform(-160, W)
        length = 46 + 150 * z
        out.append(f'<line x1="{x:.1f}" y1="{y:.1f}" x2="{x + dx * length:.1f}" y2="{y + dy * length:.1f}" '
                   f'stroke-width="{2 + 3.6 * z:.1f}" stroke-opacity="{0.10 + 0.42 * z:.2f}"/>')
    return "\n".join(out)


def spray(rng):
    """Droplets and short dashes thrown off the top edge and the flank facing the wind. Most stay close to the rim."""
    out = []
    for edge, lo, hi, count in (("top_left", 0.04, 0.98, 34), ("top_right", 0.02, 0.96, 34), ("right", 0.03, 0.42, 16)):
        for i in range(count):
            (x, y), (tx, ty) = cubic(EDGES[edge], rng.uniform(lo, hi))
            n = math.hypot(tx, ty)
            ux, uy = tx / n, ty / n
            nx, ny = uy, -ux
            if math.hypot(x + nx - CENTER[0], y + ny - CENTER[1]) < math.hypot(x - CENTER[0], y - CENTER[1]):
                nx, ny = -nx, -ny
            dist = 27 + 62 * rng.random() ** 2.2
            fade = max(0.15, 1 - (dist - 27) / 70)
            px, py = x + nx * dist, y + ny * dist
            if i % 4 == 0:  # a dash flying outward
                a = math.atan2(ny, nx) + rng.uniform(-0.7, 0.7)
                length = rng.uniform(8, 17)
                out.append(f'<line x1="{px:.1f}" y1="{py:.1f}" x2="{px + math.cos(a) * length:.1f}" '
                           f'y2="{py + math.sin(a) * length:.1f}" stroke-opacity="{0.85 * fade:.2f}"/>')
            else:
                out.append(f'<circle cx="{px:.1f}" cy="{py:.1f}" r="{rng.uniform(1.6, 4.4) * (0.55 + 0.45 * fade):.1f}" '
                           f'fill="#dff5ff" fill-opacity="{0.9 * fade:.2f}" stroke="none"/>')
    return "\n".join(out)


def hexagon(cx, cy, r):
    """Corners of a flat-top hexagon."""
    return [(cx + r * math.cos(math.radians(60 * k)), cy + r * math.sin(math.radians(60 * k))) for k in range(6)]


def cell_centre(col, row):
    """Odd columns sit half a cell lower, so the cells lock together."""
    return CORE[0] + col * 1.5 * CELL, CORE[1] + (row + (0.5 if col % 2 else 0)) * math.sqrt(3) * CELL


def field():
    """The force field: cell outlines, a faint uneven tint, the warm ring under the badge and the rain-lit cells."""
    tint = random.Random(7)
    lines, cool, warm, lit = [], [], [], []
    for col in range(-4, 5):
        for row in range(-5, 6):
            x, y = cell_centre(col, row)
            lines.append(f'<polygon points="{pts(hexagon(x, y, CELL))}"/>')
            inner = pts(hexagon(x, y, CELL - 7))
            if (col, row) in RING:
                warm.append(f'<polygon points="{inner}"/>')
            elif (col, row) in FLARES:
                lit.append(f'<polygon points="{inner}" opacity="{FLARES[(col, row)]}"/>')
            elif (col, row) != (0, 0):
                level = tint.choice([0, 0, 0.05, 0.08, 0.13, 0.18])
                if level:
                    cool.append(f'<polygon points="{inner}" opacity="{level}"/>')
    return "".join(lines), "".join(cool), "".join(warm), "".join(lit)


def sparkle(x, y, size):
    k = size * 0.11
    return (f'<polygon points="{pts([(x, y - size), (x + k, y - k), (x + size, y), (x + k, y + k), (x, y + size), (x - k, y + k), (x - size, y), (x - k, y - k)])}"/>')


def blur(name, amount):
    return (f'<filter id="{name}" filterUnits="userSpaceOnUse" x="-200" y="-200" width="1424" height="1424">'
            f'<feGaussianBlur stdDeviation="{amount}"/></filter>')


def build_svg():
    rng = random.Random(54)
    lines, cool, warm, lit = field()
    badge = pts(hexagon(*CORE, 2 * CELL))
    tick = f'<polyline points="{pts([(CORE[0] + x, CORE[1] + y) for x, y in TICK])}"/>'
    glints = sparkle(*cell_centre(-1, -2), 34) + sparkle(*cell_centre(2, -1), 20)
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{W}" viewBox="0 0 {W} {W}">
<defs>
<radialGradient id="sky" cx="0.5" cy="0.44" r="0.78">
  <stop offset="0" stop-color="#24506a"/><stop offset="0.5" stop-color="#122a40"/><stop offset="1" stop-color="#050a13"/>
</radialGradient>
<radialGradient id="halo" cx="0.5" cy="0.5" r="0.5">
  <stop offset="0" stop-color="#ffb04a" stop-opacity="0.6"/><stop offset="0.55" stop-color="#ff7d1a" stop-opacity="0.16"/>
  <stop offset="1" stop-color="#ff7d1a" stop-opacity="0"/>
</radialGradient>
<radialGradient id="edge" cx="0.5" cy="0.5" r="0.72">
  <stop offset="0.55" stop-color="#000" stop-opacity="0"/><stop offset="1" stop-color="#000" stop-opacity="0.7"/>
</radialGradient>
<linearGradient id="steel" x1="0" y1="0" x2="0" y2="1">
  <stop offset="0" stop-color="#3d5a76"/><stop offset="0.5" stop-color="#1f3044"/><stop offset="1" stop-color="#0b131d"/>
</linearGradient>
<linearGradient id="rim" x1="0.1" y1="0" x2="0.9" y2="1">
  <stop offset="0" stop-color="#ffe3a0"/><stop offset="0.35" stop-color="#ffab33"/>
  <stop offset="0.72" stop-color="#d2640f"/><stop offset="1" stop-color="#7a3209"/>
</linearGradient>
<linearGradient id="ridge" x1="0.1" y1="0" x2="0.9" y2="1">
  <stop offset="0" stop-color="#fffbe6"/><stop offset="0.5" stop-color="#ffd880"/><stop offset="1" stop-color="#f0993a"/>
</linearGradient>
<radialGradient id="gloss" cx="0.5" cy="0" r="1">
  <stop offset="0" stop-color="#fff" stop-opacity="0.26"/><stop offset="0.6" stop-color="#fff" stop-opacity="0"/>
</radialGradient>
<linearGradient id="sheen" x1="0" y1="0" x2="1" y2="0">
  <stop offset="0" stop-color="#fff" stop-opacity="0"/><stop offset="0.5" stop-color="#fff" stop-opacity="0.1"/>
  <stop offset="1" stop-color="#fff" stop-opacity="0"/>
</linearGradient>
<radialGradient id="fade" gradientUnits="userSpaceOnUse" cx="{CORE[0]}" cy="{CORE[1]}" r="440">
  <stop offset="0" stop-color="#fff"/><stop offset="0.5" stop-color="#b8b8b8"/><stop offset="1" stop-color="#3c3c3c"/>
</radialGradient>
<radialGradient id="plate" cx="0.5" cy="0.4" r="0.7">
  <stop offset="0" stop-color="#2b425c"/><stop offset="1" stop-color="#08101a"/>
</radialGradient>
<radialGradient id="ember" cx="0.5" cy="0.5" r="0.5">
  <stop offset="0" stop-color="#ff9a2e" stop-opacity="0.62"/><stop offset="1" stop-color="#ff7d1a" stop-opacity="0"/>
</radialGradient>
<linearGradient id="tick" x1="0" y1="0" x2="0" y2="1">
  <stop offset="0" stop-color="#fffbe6"/><stop offset="0.5" stop-color="#ffcf66"/><stop offset="1" stop-color="#f0871c"/>
</linearGradient>
{blur("soft2", 2)}{blur("soft6", 6)}{blur("soft14", 14)}{blur("soft26", 26)}
<filter id="clouds" filterUnits="userSpaceOnUse" x="0" y="0" width="{W}" height="{W}">
  <feTurbulence type="fractalNoise" baseFrequency="0.0035 0.008" numOctaves="4" seed="11"/>
  <feColorMatrix type="matrix" values="0 0 0 0 0.55  0 0 0 0 0.78  0 0 0 0 0.9  0.9 0 0 0 -0.25"/>
</filter>
<filter id="grain" filterUnits="userSpaceOnUse" x="0" y="0" width="{W}" height="{W}">
  <feTurbulence type="fractalNoise" baseFrequency="0.8 0.05" numOctaves="2" seed="3"/>
  <feColorMatrix type="matrix" values="0 0 0 0 1  0 0 0 0 1  0 0 0 0 1  0.5 0 0 0 -0.12"/>
</filter>
<clipPath id="in"><path d="{SHIELD}"/></clipPath>
<mask id="reach" maskUnits="userSpaceOnUse" x="0" y="0" width="{W}" height="{W}"><rect width="{W}" height="{W}" fill="url(#fade)"/></mask>
</defs>

<rect width="{W}" height="{W}" fill="url(#sky)"/>
<rect width="{W}" height="{W}" filter="url(#clouds)" opacity="0.35"/>
<circle cx="512" cy="500" r="490" fill="url(#halo)"/>
<g stroke="#a9dcf2" stroke-linecap="round">
{rain(rng)}
</g>
<rect width="{W}" height="{W}" fill="url(#edge)"/>

<g transform="{PLACE}">
  <path d="{SHIELD}" fill="#000" opacity="0.6" transform="translate(0 30)" filter="url(#soft26)"/>
  <path d="{SHIELD}" fill="none" stroke="#ff8a1f" stroke-width="64" opacity="0.5" filter="url(#soft26)"/>
  <!-- mist where the rain breaks on the shield -->
  <path d="{TOP_EDGE}" fill="none" stroke="#cdeeff" stroke-width="30" opacity="0.5" transform="translate(0 -24)" filter="url(#soft14)"/>
  <path d="{RIGHT_EDGE}" pathLength="100" fill="none" stroke="#cdeeff" stroke-width="26" stroke-dasharray="42 100" opacity="0.32"
        transform="translate(24 0)" filter="url(#soft14)"/>
  <path d="{SHIELD}" fill="url(#steel)"/>
  <g clip-path="url(#in)">
    <rect x="150" y="120" width="724" height="800" filter="url(#grain)" opacity="0.5"/>
    <ellipse cx="512" cy="170" rx="360" ry="300" fill="url(#gloss)"/>
    <rect x="250" y="60" width="150" height="1000" fill="url(#sheen)" transform="rotate(-24 512 512)"/>
    <!-- the field: brightest around the badge, fading toward the rim -->
    <g mask="url(#reach)">
      <g fill="#8fe3ff">{cool}</g>
      <g fill="#ff9a2e" opacity="0.3">{warm}</g>
      <g fill="#d9f6ff">{lit}</g>
      <g fill="#bff0ff" filter="url(#soft14)" opacity="0.8">{lit}</g>
      <g fill="none" stroke="#2fbfff" stroke-width="13" opacity="0.6" filter="url(#soft6)">{lines}</g>
      <g fill="none" stroke="#b4eeff" stroke-width="4.5" stroke-linejoin="round">{lines}</g>
    </g>
    <path d="{SHIELD}" fill="none" stroke="#000" stroke-width="84" opacity="0.55" filter="url(#soft14)"/>
    <g fill="#bfefff" filter="url(#soft6)" opacity="0.9">{glints}</g>
    <g fill="#fafeff">{glints}</g>
    <!-- the badge: a dark plate with a gold rim, lit from inside, carrying the check -->
    <polygon points="{badge}" fill="#000" opacity="0.6" transform="translate(0 16)" filter="url(#soft14)"/>
    <polygon points="{badge}" fill="none" stroke="#ff8a1f" stroke-width="46" opacity="0.75" filter="url(#soft26)"/>
    <polygon points="{badge}" fill="url(#plate)"/>
    <circle cx="{CORE[0]}" cy="{CORE[1]}" r="112" fill="url(#ember)"/>
    <polygon points="{badge}" fill="none" stroke="#2a1104" stroke-width="27"/>
    <polygon points="{badge}" fill="none" stroke="url(#rim)" stroke-width="17"/>
    <polygon points="{badge}" fill="none" stroke="url(#ridge)" stroke-width="2.5"/>
    <g fill="none" stroke-linecap="round" stroke-linejoin="round">
      <g stroke="#ffb53d" stroke-width="48" opacity="0.8" filter="url(#soft14)">{tick}</g>
      <g stroke="#2a1104" stroke-width="49">{tick}</g>
      <g stroke="#8a3d0a" stroke-width="36" transform="translate(2 6)">{tick}</g>
      <g stroke="url(#tick)" stroke-width="36">{tick}</g>
      <g stroke="#fffdf2" stroke-width="6" opacity="0.75" transform="translate(-3 -7)">{tick}</g>
    </g>
  </g>
  <!-- rim: dark outline, gold band, a darker inner facet, then the ridge between the two facets -->
  <path d="{SHIELD}" fill="none" stroke="#2a1104" stroke-width="50"/>
  <path d="{SHIELD}" fill="none" stroke="url(#rim)" stroke-width="40"/>
  <g clip-path="url(#in)"><path d="{SHIELD}" fill="none" stroke="#3a1403" stroke-width="40" opacity="0.38"/></g>
  <path d="{SHIELD}" fill="none" stroke="url(#ridge)" stroke-width="3.5"/>
  <path d="{SHIELD}" pathLength="100" fill="none" stroke="#fff" stroke-width="9" stroke-linecap="round"
        stroke-dasharray="0 70 27 100" opacity="0.55" transform="translate(-3 -3)" filter="url(#soft2)"/>
  <g stroke="#dff5ff" stroke-width="3.2" stroke-linecap="round">
{spray(rng)}
  </g>
</g>
</svg>
'''


def render(svg_path, scale=2):
    """Screenshot the SVG in a headless browser at 2x, then crop to the drawing."""
    exe = next((b for b in BROWSERS if Path(b).exists()), None)
    if not exe:
        sys.exit("no Edge or Chrome found for the render")
    work = Path(tempfile.mkdtemp(prefix="compatshield-icon-"))
    try:
        shot = work / "shot.png"
        subprocess.run([exe, "--headless=new", "--disable-gpu", "--hide-scrollbars", f"--force-device-scale-factor={scale}",
                        f"--window-size={W},{W + 200}", f"--user-data-dir={work / 'profile'}", f"--screenshot={shot}",
                        svg_path.as_uri()], check=True, timeout=180, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        with Image.open(shot) as im:
            if im.width < W * scale or im.height < W * scale:
                sys.exit(f"render came out {im.size}, smaller than {W * scale}")
            return im.convert("RGB").crop((0, 0, W * scale, W * scale))
    finally:
        shutil.rmtree(work, ignore_errors=True)


def main():
    svg_path = HERE / "icon.svg"
    svg_path.write_text(build_svg(), encoding="utf-8")
    big = render(svg_path)
    lanczos = Image.Resampling.LANCZOS
    big.resize((W, W), lanczos).save(HERE / "icon-1024.png", optimize=True)
    small = big.resize((256, 256), lanczos).filter(ImageFilter.UnsharpMask(radius=0.6, percent=60, threshold=0))
    small.save(HERE / "icon.png", optimize=True)
    for name in ("icon.svg", "icon-1024.png", "icon.png"):
        print(f"{(HERE / name).stat().st_size:9d}  {HERE / name}")


if __name__ == "__main__":
    main()
