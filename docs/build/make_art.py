"""Draws the artwork for the CompatShield GitHub README.

Usage: python -I make_art.py <assets folder> [still-copies folder]
Writes theme pairs (...-dark.svg / ...-light.svg). With a second folder it also writes copies of the animated
files with the motion switched off, for still previews.
"""
import math
import os
import sys
from string import Template

out = sys.argv[1]
settled = sys.argv[2] if len(sys.argv) > 2 else None
os.makedirs(out, exist_ok=True)
if settled:
    os.makedirs(settled, exist_ok=True)

FONT = '-apple-system, BlinkMacSystemFont, "Segoe UI", "Noto Sans", Helvetica, Arial, sans-serif'
MONO = 'ui-monospace, SFMono-Regular, "SF Mono", Menlo, Consolas, "Liberation Mono", monospace'
THEMES = {
    "dark": dict(bg="#0d1117", edge="#30363d", tile="#161b22", name="#f0f6fc", role="#c9d1d9", meta="#8b949e",
                 ink="#9aa4af", rain="#8b949e", err="#ff7b72", blue="#58a6ff", teal="#3fb8af", violet="#a78bfa",
                 orange="#f0883e", hot="#ffb77c", shield_fill="#2a1c0f", check="#0d1117",
                 glow="0.20", rain_a="0.34", rain_b="0.6", dome_in="0.03", dome_mid="0.10", dome_rim="0.28", hex="0.5",
                 top=0.26, bot=0.07, edgeop=0.55, chip_fill=0.14, chip_edge=0.42),
    "light": dict(bg="#f6f8fa", edge="#d0d7de", tile="#ffffff", name="#1f2328", role="#424a53", meta="#59636e",
                  ink="#59636e", rain="#6e7781", err="#cf222e", blue="#0969da", teal="#0f766e", violet="#8250df",
                  orange="#bc4c00", hot="#e16f24", shield_fill="#fff1e5", check="#ffffff",
                  glow="0.13", rain_a="0.3", rain_b="0.5", dome_in="0.02", dome_mid="0.07", dome_rim="0.2", hex="0.42",
                  top=0.15, bot=0.04, edgeop=0.5, chip_fill=0.08, chip_edge=0.38),
}
count = 0


def write(name, text, still=None):
    global count
    with open(os.path.join(out, name), "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    count += 1
    if settled and still is not None:
        with open(os.path.join(settled, name), "w", encoding="utf-8", newline="\n") as f:
            f.write(still)


def esc(s):
    return s.replace("&", "&amp;").replace("<", "&lt;")


def text_width(s, size):
    w = 0.0
    for ch in s:
        if ch in "iljI.,:'|! ":
            w += 0.30
        elif ch in "ftr-":
            w += 0.40
        elif ch in "mwMW":
            w += 0.92
        elif ch.isupper() or ch.isdigit() or ch in "#&":
            w += 0.68
        else:
            w += 0.57
    return w * size


# ---------------- line glyphs around (cx, cy), drawn for a 64 px tile: class g = stroke, gf = fill ----------------
def cube(cx, cy):
    return (f'<path class="g" d="M{cx} {cy-19} L{cx+16.5} {cy-9.5} V{cy+9.5} L{cx} {cy+19} L{cx-16.5} {cy+9.5} V{cy-9.5} Z"/>'
            f'<path class="g" d="M{cx-16.5} {cy-9.5} L{cx} {cy} L{cx+16.5} {cy-9.5} M{cx} {cy} V{cy+19}"/>')


def terminal(cx, cy):
    return (f'<rect class="g" x="{cx-21}" y="{cy-15}" width="42" height="30" rx="5"/>'
            f'<path class="g" d="M{cx-12} {cy-6} l7 6 l-7 6"/><path class="g" d="M{cx+1} {cy+7} h11"/>')


def shield(cx, cy):
    return (f'<path class="g" d="M{cx} {cy-19} L{cx+15} {cy-13} V{cy-1} C{cx+15} {cy+9} {cx+8.5} {cy+15.5} {cx} {cy+19} '
            f'C{cx-8.5} {cy+15.5} {cx-15} {cy+9} {cx-15} {cy-1} V{cy-13} Z"/>'
            f'<path class="g" d="M{cx-6.5} {cy-0.5} l4.5 4.5 l8.5 -9.5"/>')


def person(cx, cy):
    return (f'<circle class="g" cx="{cx}" cy="{cy-9}" r="7"/>'
            f'<path class="g" d="M{cx-15} {cy+17} C{cx-15} {cy+7} {cx-8} {cy+3} {cx} {cy+3} C{cx+8} {cy+3} {cx+15} {cy+7} {cx+15} {cy+17}"/>')


def flame(cx, cy):
    return (f'<path class="g" d="M{cx+1} {cy-19} C{cx+3} {cy-10} {cx+14} {cy-5} {cx+14} {cy+6} C{cx+14} {cy+14} {cx+8} {cy+19} {cx} {cy+19} '
            f'C{cx-8} {cy+19} {cx-14} {cy+14} {cx-14} {cy+6} C{cx-14} {cy} {cx-10} {cy-4} {cx-8} {cy-8} '
            f'C{cx-5} {cy-4} {cx-1} {cy-9} {cx+1} {cy-19} Z"/>')


def bars(cx, cy):
    return f'<path class="g" d="M{cx-13} {cy-6} V{cy+6} M{cx-4.5} {cy-16} V{cy+16} M{cx+4.5} {cy-10} V{cy+10} M{cx+13} {cy-4} V{cy+4}"/>'


def bolt(cx, cy):
    return f'<path class="g" d="M{cx+3} {cy-19} L{cx-12} {cy+3} H{cx-1} L{cx-3} {cy+19} L{cx+12} {cy-3} H{cx+1} Z"/>'


def chip_glyph(cx, cy):
    return (f'<rect class="g" x="{cx-12}" y="{cy-12}" width="24" height="24" rx="4"/>'
            f'<path class="g" d="M{cx-5} {cy-20} v8 M{cx+5} {cy-20} v8 M{cx-5} {cy+12} v8 M{cx+5} {cy+12} v8 '
            f'M{cx-20} {cy-5} h8 M{cx-20} {cy+5} h8 M{cx+12} {cy-5} h8 M{cx+12} {cy+5} h8"/>'
            f'<path class="g" d="M{cx-5} {cy+0.5} l3.5 3.5 l6.5 -7.5"/>')


def exit_glyph(cx, cy):
    return (f'<path class="g" d="M{cx-3} {cy-17} H{cx-14} a4 4 0 0 0 -4 4 V{cy+13} a4 4 0 0 0 4 4 H{cx-3}"/>'
            f'<path class="g" d="M{cx-7} {cy} H{cx+18} M{cx+10} {cy-8} l8 8 l-8 8"/>')


def nodes(cx, cy):
    return (f'<circle class="g" cx="{cx-14}" cy="{cy}" r="5.5"/><circle class="g" cx="{cx+13}" cy="{cy-12}" r="5.5"/>'
            f'<circle class="g" cx="{cx+13}" cy="{cy+12}" r="5.5"/>'
            f'<path class="g" d="M{cx-9} {cy-2.5} L{cx+8} {cy-10} M{cx-9} {cy+2.5} L{cx+8} {cy+10}"/>')


def download(cx, cy):
    return (f'<path class="g" d="M{cx} {cy-18} V{cy+5} M{cx-9} {cy-4} l9 9 l9 -9"/>'
            f'<path class="g" d="M{cx-18} {cy+9} v5 a4 4 0 0 0 4 4 h28 a4 4 0 0 0 4 -4 v-5"/>')


def sliders(cx, cy):
    return (f'<path class="g" d="M{cx-18} {cy-12} H{cx+18} M{cx-18} {cy} H{cx+18} M{cx-18} {cy+12} H{cx+18}"/>'
            f'<circle class="gf" cx="{cx-6}" cy="{cy-12}" r="5"/><circle class="gf" cx="{cx+9}" cy="{cy}" r="5"/>'
            f'<circle class="gf" cx="{cx-10}" cy="{cy+12}" r="5"/>')


def people(cx, cy):
    return (f'<circle class="g" cx="{cx-7}" cy="{cy-9}" r="6.5"/>'
            f'<path class="g" d="M{cx-21} {cy+16} C{cx-21} {cy+7} {cx-15} {cy+3} {cx-7} {cy+3} C{cx+1} {cy+3} {cx+7} {cy+7} {cx+7} {cy+16}"/>'
            f'<path class="g" d="M{cx+8} {cy-15} a6 6 0 0 1 0 12"/>'
            f'<path class="g" d="M{cx+13} {cy+3.5} C{cx+18} {cy+5} {cx+21} {cy+9} {cx+21} {cy+16}"/>')


def tag(cx, cy):
    return (f'<path class="g" d="M{cx-17} {cy-17} H{cx-1} L{cx+18} {cy+2} L{cx+2} {cy+18} L{cx-17} {cy-1} Z"/>'
            f'<circle class="gf" cx="{cx-8.5}" cy="{cy-8.5}" r="3.2"/>')


def crosshair(cx, cy):
    return (f'<circle class="g" cx="{cx}" cy="{cy}" r="12.5"/>'
            f'<path class="g" d="M{cx} {cy-20} v8 M{cx} {cy+12} v8 M{cx-20} {cy} h8 M{cx+12} {cy} h8"/>'
            f'<circle class="gf" cx="{cx}" cy="{cy}" r="2.6"/>')


def drop(cx, cy):
    return (f'<path class="g" d="M{cx} {cy-18} C{cx+6} {cy-9} {cx+13} {cy-2} {cx+13} {cy+6} A13 13 0 0 1 {cx-13} {cy+6} '
            f'C{cx-13} {cy-2} {cx-6} {cy-9} {cx} {cy-18} Z"/>')


def scales(cx, cy):
    return (f'<path class="g" d="M{cx} {cy-17} V{cy+17} M{cx-9} {cy+17} H{cx+9} M{cx-15} {cy-11} H{cx+15}"/>'
            f'<path class="g" d="M{cx-15} {cy-11} L{cx-21} {cy+3} A6 6 0 0 0 {cx-9} {cy+3} Z"/>'
            f'<path class="g" d="M{cx+15} {cy-11} L{cx+9} {cy+3} A6 6 0 0 0 {cx+21} {cy+3} Z"/>')


def stopwatch(cx, cy):
    return (f'<circle class="g" cx="{cx}" cy="{cy+3}" r="14"/>'
            f'<path class="g" d="M{cx-5} {cy-18} h10 M{cx} {cy-18} v7 M{cx} {cy+3} V{cy-5} M{cx} {cy+3} l6 4"/>')


def warning(cx, cy):
    return (f'<path class="g" d="M{cx} {cy-17} L{cx+19} {cy+15} H{cx-19} Z"/>'
            f'<path class="g" d="M{cx} {cy-5} v9"/><circle class="gf" cx="{cx}" cy="{cy+9.5}" r="2"/>')


def pulse_line(cx, cy):
    return f'<path class="g" d="M{cx-21} {cy+1} H{cx-11} l5 -13 l8 25 l6 -18 l3 6 H{cx+21}"/>'


def glyph_style(c, sw):
    return (f'<style>.g{{fill:none;stroke:{c};stroke-width:{sw};stroke-linecap:round;stroke-linejoin:round}}'
            f'.gf{{fill:{c}}}</style>')


# ---------------- banner ----------------
W, H = 1280, 340
CX, GY, RX, RY = 962, 298, 246, 232
DOME = f"M{CX - RX} {GY} A{RX} {RY} 0 0 1 {CX + RX} {GY}"
MOVING = ".rA, .rB, .rE, .hit, .flow, .wave, .pulse, .bob"

BANNER = Template("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1280 340" width="1280" height="340" role="img" aria-labelledby="t d">
  <title id="t">CompatShield</title>
  <desc id="d">CompatShield, a BepInEx patcher for Risk of Rain 2. It fixes mods the October 2026 update broke, without editing a single mod file. The picture: errors rain down on a glowing shield dome and stop at its edge. Under the dome, an orange shield with a cross stands between four mod tiles, each with a check mark.</desc>
  <style>
    text { font-family: $font; }
    .mono { font-family: $mono; }
    .bg { fill: $bg; }
    .edge { fill: none; stroke: $edge; }
    .name { fill: $name; } .role { fill: $role; } .meta { fill: $meta; }
    .fO { fill: $orange; } .fT { fill: $teal; } .fB { fill: $blue; } .fH { fill: $hot; }
    .sH { fill: none; stroke: $hot; stroke-width: 2; stroke-linecap: round; }
    .g { fill: none; stroke: $ink; stroke-width: 3; stroke-linecap: round; stroke-linejoin: round; }
    .gf { fill: $ink; }
    .tile { fill: $tile; stroke: $teal; stroke-opacity: 0.7; stroke-width: 1.4; }
    .ck { fill: none; stroke: $check; stroke-width: 2; stroke-linecap: round; stroke-linejoin: round; }
    .pill { fill: $tile; stroke: $edge; }
    .rim { fill: none; stroke: url(#rim); stroke-width: 2.6; stroke-linecap: round; }
    .rim2 { fill: none; stroke: $orange; stroke-opacity: 0.2; stroke-width: 7; stroke-linecap: round; }
    .rim3 { fill: none; stroke: $orange; stroke-opacity: 0.07; stroke-width: 17; stroke-linecap: round; }
    .hexl { fill: none; stroke: $orange; stroke-width: 1; }
    .shield { fill: $shield_fill; stroke: $orange; stroke-width: 6; stroke-linejoin: round; }
    .drop { fill: none; stroke: $rain; stroke-linecap: round; }
    .dropE { fill: none; stroke: $err; stroke-width: 2.4; stroke-linecap: round; }
    .ec { fill: $tile; }
    .ec2 { fill: $err; fill-opacity: 0.1; stroke: $err; stroke-opacity: 0.55; }
    .ect { fill: $err; font-weight: 600; }
    .ex { fill: none; stroke: $err; stroke-width: 1.8; stroke-linecap: round; }
    .hit { opacity: 0.9; transform-box: fill-box; transform-origin: center; }
    .hx { opacity: 0; }
    .flow { fill: none; stroke: $hot; stroke-width: 4.5; stroke-linecap: round; stroke-dasharray: 0.07 0.93; opacity: 0; }
    .wave { fill: none; stroke: $orange; stroke-width: 1.6; opacity: 0; transform-origin: ${cx}px ${gy}px; }
    .pulse { transform-box: fill-box; transform-origin: center; }

    .rA { animation: fallA 2.6s linear infinite; }
    .rB { animation: fallB 1.7s linear infinite; }
    .rE { animation: fallE 2.3s linear infinite; }
    .hit { animation: hit 3.9s ease-out infinite both; }
    .h1 { animation-delay: 0.3s; } .h2 { animation-delay: 1.5s; } .h3 { animation-delay: 2.7s; }
    .h4 { animation-delay: 0.9s; } .h5 { animation-delay: 2.1s; } .h6 { animation-delay: 3.3s; }
    .flow { animation: flow 5.4s linear 1s infinite; }
    .wave { animation: wave 4.8s ease-out infinite both; }
    .w2 { animation-delay: 2.4s; }
    .pulse { animation: pulse 3.6s ease-in-out infinite; }
    .bob { animation: bob 6s ease-in-out infinite; }
    .q2 { animation-delay: -2s; } .q3 { animation-delay: -4s; }
    @keyframes fallA { to { transform: translateY(150px); } }
    @keyframes fallB { to { transform: translateY(220px); } }
    @keyframes fallE { to { transform: translateY(300px); } }
    @keyframes hit { 0% { opacity: 0; transform: scale(0.3); } 5% { opacity: 1; transform: scale(1); } 24% { opacity: 0; transform: scale(1.4); } 100% { opacity: 0; transform: scale(1.4); } }
    @keyframes flow { 0% { stroke-dashoffset: 1; opacity: 0; } 8% { opacity: 1; } 90% { opacity: 1; } 100% { stroke-dashoffset: 0.07; opacity: 0; } }
    @keyframes wave { 0% { transform: scale(0.42); opacity: 0; } 14% { opacity: 0.55; } 100% { transform: scale(1); opacity: 0; } }
    @keyframes pulse { 0%, 100% { opacity: 0.7; transform: scale(1); } 50% { opacity: 1; transform: scale(1.12); } }
    @keyframes bob { 0%, 100% { transform: translateY(0); } 50% { transform: translateY(5px); } }
    @media (prefers-reduced-motion: reduce) { $moving { animation: none; } }
  </style>
  <defs>
    <clipPath id="frame"><rect x="1" y="1" width="1278" height="338" rx="16"/></clipPath>
    <clipPath id="out"><path clip-rule="evenodd" d="M0 0 H1280 V340 H0 Z $dome Z"/></clipPath>
    <pattern id="pA" width="46" height="150" patternUnits="userSpaceOnUse">
      <path class="drop" stroke-width="1.2" stroke-opacity="$rain_a" d="M11 8 v30 M34 88 v22"/>
    </pattern>
    <pattern id="pB" width="74" height="220" patternUnits="userSpaceOnUse">
      <path class="drop" stroke-width="1.9" stroke-opacity="$rain_b" d="M17 18 v50 M53 132 v38"/>
    </pattern>
    <pattern id="pE" width="236" height="300" patternUnits="userSpaceOnUse">
      <path class="dropE" stroke-opacity="0.9" d="M40 24 v44 M158 176 v36"/>
    </pattern>
    <pattern id="hex" width="27.71" height="48" patternUnits="userSpaceOnUse" patternTransform="translate($cx 66)">
      <path class="hexl" d="M13.86 0 L27.71 8 V24 L13.86 32 L0 24 V8 Z M13.86 32 V48"/>
    </pattern>
    <linearGradient id="rfx" gradientUnits="userSpaceOnUse" x1="570" y1="0" x2="800" y2="0">
      <stop offset="0" stop-color="#fff" stop-opacity="0"/><stop offset="1" stop-color="#fff" stop-opacity="1"/>
    </linearGradient>
    <linearGradient id="rfy" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#000" stop-opacity="0"/><stop offset="1" stop-color="#000" stop-opacity="1"/>
    </linearGradient>
    <mask id="rf" maskUnits="userSpaceOnUse" x="0" y="0" width="1280" height="340">
      <rect width="1280" height="340" fill="url(#rfx)"/><rect y="290" width="1280" height="50" fill="url(#rfy)"/>
    </mask>
    <radialGradient id="df" gradientUnits="userSpaceOnUse" cx="$cx" cy="$gy" r="$rx" gradientTransform="translate($cx $gy) scale(1 $squash) translate(-$cx -$gy)">
      <stop offset="0" stop-color="$orange" stop-opacity="0"/><stop offset="0.55" stop-color="$orange" stop-opacity="$dome_in"/>
      <stop offset="0.86" stop-color="$orange" stop-opacity="$dome_mid"/><stop offset="1" stop-color="$orange" stop-opacity="$dome_rim"/>
    </radialGradient>
    <radialGradient id="hm" gradientUnits="userSpaceOnUse" cx="$cx" cy="$gy" r="$rx" gradientTransform="translate($cx $gy) scale(1 $squash) translate(-$cx -$gy)">
      <stop offset="0.3" stop-color="#fff" stop-opacity="0"/><stop offset="0.8" stop-color="#fff" stop-opacity="0.45"/><stop offset="1" stop-color="#fff" stop-opacity="1"/>
    </radialGradient>
    <mask id="hmask" maskUnits="userSpaceOnUse" x="0" y="0" width="1280" height="340"><rect width="1280" height="340" fill="url(#hm)"/></mask>
    <linearGradient id="rim" gradientUnits="userSpaceOnUse" x1="$x0" y1="0" x2="$x1" y2="0">
      <stop offset="0" stop-color="$orange" stop-opacity="0.35"/><stop offset="0.22" stop-color="$orange"/><stop offset="0.5" stop-color="$hot"/>
      <stop offset="0.78" stop-color="$orange"/><stop offset="1" stop-color="$orange" stop-opacity="0.35"/>
    </linearGradient>
    <linearGradient id="ground" gradientUnits="userSpaceOnUse" x1="640" y1="0" x2="1280" y2="0">
      <stop offset="0" stop-color="$ink" stop-opacity="0"/><stop offset="0.16" stop-color="$ink" stop-opacity="0.55"/>
      <stop offset="0.9" stop-color="$ink" stop-opacity="0.55"/><stop offset="1" stop-color="$ink" stop-opacity="0.15"/>
    </linearGradient>
    <radialGradient id="go" cx="0.75" cy="0.92" r="0.42"><stop offset="0" stop-color="$orange" stop-opacity="$glow"/><stop offset="1" stop-color="$orange" stop-opacity="0"/></radialGradient>
    <radialGradient id="gb" cx="0.92" cy="0" r="0.45"><stop offset="0" stop-color="$blue" stop-opacity="$glow"/><stop offset="1" stop-color="$blue" stop-opacity="0"/></radialGradient>
    <radialGradient id="gs"><stop offset="0" stop-color="$orange" stop-opacity="0.42"/><stop offset="0.6" stop-color="$orange" stop-opacity="0.1"/><stop offset="1" stop-color="$orange" stop-opacity="0"/></radialGradient>
    <linearGradient id="ab" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="$orange"/><stop offset="1" stop-color="$hot"/></linearGradient>
  </defs>

  <rect class="bg" x="0.5" y="0.5" width="1279" height="339" rx="16"/>
  <g clip-path="url(#frame)">
    <rect width="1280" height="340" fill="url(#gb)"/>
    <rect width="1280" height="340" fill="url(#go)"/>
    <g clip-path="url(#out)" mask="url(#rf)">
      <g transform="rotate(14 $cx 170)">
        <rect class="rA" x="-400" y="-600" width="2200" height="1500" fill="url(#pA)"/>
        <rect class="rB" x="-400" y="-600" width="2200" height="1500" fill="url(#pB)"/>
        <rect class="rE" x="-400" y="-600" width="2200" height="1500" fill="url(#pE)"/>
      </g>
    </g>
$tags
    <ellipse cx="$cx" cy="$gy" rx="262" ry="9" fill="$orange" fill-opacity="0.13"/>
    <path d="M640 $gy.5 H1280" stroke="url(#ground)" stroke-width="1.5" fill="none"/>
    <path d="$dome Z" fill="url(#df)"/>
    <path d="$dome Z" fill="url(#hex)" opacity="$hex" mask="url(#hmask)"/>
    <path class="wave" d="$dome"/>
    <path class="wave w2" d="$dome"/>
    <path class="rim3" d="$dome"/>
    <path class="rim2" d="$dome"/>
    <path class="rim" d="$dome"/>
    <path class="flow" pathLength="1" d="$dome"/>
$sparks
$tiles
    <circle class="pulse" cx="$cx" cy="172" r="104" fill="url(#gs)"/>
    <path class="shield" d="M$sl 106 H$sr V166 C$sr 203 $sr2 222 $cx 234 C$sl2 222 $sl 203 $sl 166 Z"/>
    <rect class="fO" x="$cl" y="124" width="11" height="88" rx="1.5"/>
    <rect class="fO" x="$bl" y="147" width="58" height="11" rx="1.5"/>
  </g>

  <rect x="64" y="54" width="84" height="4" rx="2" fill="url(#ab)"/>
  <text class="fO" x="64" y="92" font-size="16" font-weight="600" letter-spacing="2.2">RISK OF RAIN 2  ·  BEPINEX PATCHER</text>
  <text class="name" x="61" y="158" font-size="66" font-weight="700" letter-spacing="-1">Compat<tspan class="fO">Shield</tspan></text>
  <text class="role" x="64" y="200" font-size="24">Fixes mods the October 2026 update broke,</text>
  <text class="role" x="64" y="232" font-size="24">without editing a single mod file.</text>
$pills
  <rect class="edge" x="0.5" y="0.5" width="1279" height="339" rx="16"/>
</svg>
""")


def on_dome(deg):
    a = math.radians(deg)
    return CX + RX * math.cos(a), GY - RY * math.sin(a)


def spark(deg, n):
    x, y = on_dome(deg)
    a = math.radians(deg)
    nx, ny = math.cos(a) / RX, -math.sin(a) / RY
    length = math.hypot(nx, ny)
    nx, ny = nx / length, ny / length
    parts = [f'<circle class="fH" cx="{x:.1f}" cy="{y:.1f}" r="3.2"/>']
    for turn in (-40, 0, 40):
        t = math.radians(turn)
        dx, dy = nx * math.cos(t) - ny * math.sin(t), nx * math.sin(t) + ny * math.cos(t)
        parts.append(f'<path class="sH" d="M{x + dx * 7:.1f} {y + dy * 7:.1f} L{x + dx * 14:.1f} {y + dy * 14:.1f}"/>')
    extra = "" if n % 2 else " hx"
    return f'    <g class="hit h{n}{extra}">' + "".join(parts) + "</g>"


def mod_tile(x, y, glyph):
    return (f'    <rect class="tile" x="{x - 22}" y="{y - 22}" width="44" height="44" rx="10"/>'
            f'<g transform="translate({x} {y}) scale(0.6)">{glyph(0, 0)}</g>'
            f'<circle class="fT" cx="{x + 19}" cy="{y - 19}" r="8.5"/>'
            f'<path class="ck" d="M{x + 15} {y - 19} l3 3 l5.5 -6"/>')


def error_chip(x, y, label, n, size=12.5, alpha=1.0):
    h = round(size * 1.9)
    w = round(len(label) * size * 0.602 + size * 2.6)
    cy = y + 0.5 + h / 2
    q = "" if n == 1 else f" q{n}"
    box = f'x="{x}.5" y="{y}.5" width="{w}" height="{h}" rx="{h / 2}"'
    return (f'    <g class="bob{q}" opacity="{alpha}"><rect class="ec" {box}/><rect class="ec2" {box}/>'
            f'<path class="ex" d="M{x + size * 0.85:.1f} {cy - 3.2:.1f} l6.4 6.4 m0 -6.4 l-6.4 6.4"/>'
            f'<text class="mono ect" x="{x + size * 1.85:.1f}" y="{cy + size * 0.34:.1f}" font-size="{size}">{label}</text></g>')


def banner_pills():
    x, parts = 64, []
    for label, cls in [("One DLL", "fO"), ("Patches in memory", "fT"), ("Delete it to undo", "fB")]:
        w = math.ceil(text_width(label, 17)) + 46
        parts.append(f'  <rect class="pill" x="{x}.5" y="256.5" width="{w}" height="34" rx="17"/>'
                     f'<circle class="{cls}" cx="{x + 18}" cy="273.5" r="4.5"/>'
                     f'<text class="role" x="{x + 31}" y="279.5" font-size="17" font-weight="500">{label}</text>')
        x += w + 10
    return "\n".join(parts)


def banner(t):
    sparks = "\n".join(spark(deg, n) for n, deg in enumerate([34, 57, 79, 99, 121, 144], 1))
    tiles = "\n".join(mod_tile(x, 262, g) for x, g in [(790, cube), (856, person), (1068, flame), (1134, bars)])
    tags = "\n".join([error_chip(640, 30, "InvalidProgramException", 1),
                      error_chip(1060, 22, "NullReferenceException", 2),
                      error_chip(604, 96, "StackOverflowException", 3, size=11, alpha=0.62)])
    return BANNER.substitute(t, font=FONT, mono=MONO, moving=MOVING, dome=DOME, cx=CX, gy=GY, rx=RX,
                             squash=f"{RY / RX:.4f}", x0=CX - RX, x1=CX + RX,
                             sl=CX - 50, sr=CX + 50, sl2=CX - 24, sr2=CX + 24, cl=CX - 5.5, bl=CX - 29,
                             sparks=sparks, tiles=tiles, tags=tags, pills=banner_pills())


def still(art, moving):
    gate = "@media (prefers-reduced-motion: reduce) { " + moving + " { animation: none; } }"
    assert gate in art
    return art.replace(gate, moving + " { animation: none; }")


# ---------------- how it works ----------------
HOW = Template("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 880 178" width="880" height="178" role="img" aria-labelledby="t d">
  <title id="t">How CompatShield works</title>
  <desc id="d">Four steps. 1, Loads first: BepInEx starts it before the game and any mod. 2, Patches in memory: it fixes each broken mod as it loads, and no mod file is changed. 3, Guards the run: it stops error loops, hangs and runaway chains while you play. 4, Steps aside: when a mod ships its own fix, the patch stops and says so in the log.</desc>
  <style>
    text { font-family: $font; }
    .mono { font-family: $mono; }
    .card { fill: $tile; stroke: $edge; }
    .name { fill: $name; } .role { fill: $role; } .meta { fill: $meta; }
    .g { fill: none; stroke-width: 3.2; stroke-linecap: round; stroke-linejoin: round; }
    .cB .g { stroke: $blue; } .cO .g { stroke: $orange; } .cT .g { stroke: $teal; } .cV .g { stroke: $violet; }
    .ar { fill: none; stroke: $meta; stroke-width: 2; stroke-linecap: round; stroke-linejoin: round; opacity: 0.8; }
    .pop { animation: pop 0.55s ease-out both; }
    .p2 { animation-delay: 0.12s; } .p3 { animation-delay: 0.24s; } .p4 { animation-delay: 0.36s; }
    .ar { animation: ar 3.2s ease-in-out 1s infinite both; }
    .a2 { animation-delay: 1.5s; } .a3 { animation-delay: 2s; }
    @keyframes pop { from { opacity: 0; transform: translateY(8px); } to { opacity: 1; transform: translateY(0); } }
    @keyframes ar { 0%, 100% { opacity: 0.3; } 25% { opacity: 1; } }
    @media (prefers-reduced-motion: reduce) { .pop, .ar { animation: none; } }
  </style>
  <defs>
$grads
  </defs>
$body
</svg>
""")


def how(t):
    cards = [("Loads first", ["BepInEx starts it before", "the game and any mod."], bolt, "B", "blue"),
             ("Patches in memory", ["Fixes each broken mod as", "it loads. No mod file is", "changed."], chip_glyph, "O", "orange"),
             ("Guards the run", ["Stops error loops, hangs", "and runaway chains while", "you play."], shield, "T", "teal"),
             ("Steps aside", ["When a mod ships its own", "fix, the patch stops and", "says so in the log."], exit_glyph, "V", "violet")]
    grads, body = [], []
    for i, (title, lines, glyph, key, col) in enumerate(cards):
        x, y, c = i * 228 + 0.5, 9.5, t[col]
        grads.append(f'    <linearGradient id="f{key}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{c}" stop-opacity="{t["top"]}"/>'
                     f'<stop offset="1" stop-color="{c}" stop-opacity="{t["bot"]}"/></linearGradient>')
        part = [f'  <g class="pop p{i + 1} c{key}">',
                f'<rect class="card" x="{x}" y="{y}" width="195" height="159" rx="14"/>',
                f'<rect x="{x + 16}" y="{y + 16}" width="44" height="44" rx="11" fill="url(#f{key})" stroke="{c}" stroke-opacity="{t["edgeop"]}" stroke-width="1.6"/>',
                f'<g transform="translate({x + 38} {y + 38}) scale(0.6)">{glyph(0, 0)}</g>',
                f'<text class="mono meta" x="{x + 179}" y="{y + 34}" font-size="13" font-weight="600" text-anchor="end">0{i + 1}</text>',
                f'<text class="name" x="{x + 16}" y="{y + 88}" font-size="16.5" font-weight="600">{title}</text>']
        for n, line in enumerate(lines):
            part.append(f'<text class="role" x="{x + 16}" y="{y + 109 + n * 17}" font-size="12.6">{line}</text>')
        part.append("</g>")
        body.append("".join(part))
        if i < 3:
            ax = x + 195 + 9
            cls = "ar" if i == 0 else f"ar a{i + 1}"
            body.append(f'  <path class="{cls}" d="M{ax} 89 H{ax + 15} M{ax + 9} 83 l6 6 l-6 6"/>')
    return HOW.substitute(t, font=FONT, mono=MONO, grads="\n".join(grads), body="\n".join(body))


# ---------------- small pieces: heading icons, row icons, chips, buttons, divider ----------------
def heading_icon(c, t, glyph):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" width="64" height="64" aria-hidden="true">'
            f'{glyph_style(c, 3)}'
            f'<defs><linearGradient id="f" x1="0" y1="0" x2="0" y2="1">'
            f'<stop offset="0" stop-color="{c}" stop-opacity="{t["top"]}"/><stop offset="1" stop-color="{c}" stop-opacity="{t["bot"]}"/>'
            f'</linearGradient></defs>'
            f'<rect x="1.5" y="1.5" width="61" height="61" rx="15" fill="url(#f)" stroke="{c}" stroke-opacity="{t["edgeop"]}" stroke-width="2.4"/>'
            f'{glyph}</svg>\n')


def row_icon(c, glyph):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="8 8 48 48" width="48" height="48" aria-hidden="true">'
            f'{glyph_style(c, 3.2)}{glyph}</svg>\n')


def chip(c, t, label, h=22):
    w = math.ceil(text_width(label, 11.5)) + 24
    font = FONT.replace('"', "&quot;")
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="{w}" height="{h}" role="img" aria-label="{esc(label)}">'
            f'<title>{esc(label)}</title>'
            f'<rect x="0.5" y="0.5" width="{w - 1}" height="{h - 1}" rx="{(h - 1) / 2}" fill="{c}" fill-opacity="{t["chip_fill"]}" '
            f'stroke="{c}" stroke-opacity="{t["chip_edge"]}"/>'
            f'<text x="{w / 2}" y="15.1" font-size="11.5" font-weight="600" fill="{c}" text-anchor="middle" '
            f'font-family="{font}">{esc(label)}</text></svg>\n'), w


def button(label, fill, ink, stroke=None, defs="", arrow=False, h=34, size=14.5):
    pad = 18
    extra = 20 if arrow else 0
    w = math.ceil(text_width(label, size)) + pad * 2 + extra
    font = FONT.replace('"', "&quot;")
    edge = f'<rect x="0.5" y="0.5" width="{w - 1}" height="{h - 1}" rx="8" fill="none" stroke="{stroke}"/>' if stroke else ""
    mark = (f'<path d="M{pad + 5} 9.5 V21 M{pad} 16.5 l5 5 l5 -5 M{pad - 1} 25 h12" fill="none" stroke="{ink}" stroke-width="2" '
            f'stroke-linecap="round" stroke-linejoin="round"/>') if arrow else ""
    tx = pad + extra + (w - pad * 2 - extra) / 2
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="{w}" height="{h}" role="img" aria-label="{esc(label)}">'
            f'<title>{esc(label)}</title>{defs}<rect width="{w}" height="{h}" rx="8" fill="{fill}"/>{edge}{mark}'
            f'<text x="{tx:.1f}" y="{h / 2 + size * 0.36:.1f}" font-size="{size}" font-weight="600" fill="{ink}" text-anchor="middle" '
            f'font-family="{font}">{esc(label)}</text></svg>\n'), w


HEADINGS = [("how", nodes, "teal"), ("fixes", shield, "orange"), ("install", download, "blue"), ("settings", sliders, "violet"),
            ("multiplayer", people, "teal"), ("build", terminal, "violet"), ("versions", tag, "blue")]
ROWS = [("spawns", crosshair, "violet"), ("sacrifice", drop, "violet"), ("balance", scales, "violet"),
        ("lag", stopwatch, "teal"), ("crash", warning, "teal"), ("items", cube, "orange"), ("enemies", person, "orange"),
        ("diagnostics", pulse_line, "blue")]
CHIPS = [("csharp", "C#"), ("bepinex", "BepInEx"), ("harmony", "Harmony"), ("cecil", "Mono.Cecil"), ("monomod", "MonoMod"),
         ("netstandard", ".NET Standard 2.1")]
sizes = {}

for theme, t in THEMES.items():
    art = banner(t)
    write(f"banner-{theme}.svg", art, still(art, MOVING))
    art = how(t)
    write(f"how-{theme}.svg", art, still(art, ".pop, .ar"))
    for name, fn, col in HEADINGS:
        write(f"icon-{name}-{theme}.svg", heading_icon(t[col], t, fn(32, 32)))
    for name, fn, col in ROWS:
        write(f"mini-{name}-{theme}.svg", row_icon(t[col], fn(32, 32)))
    for slug, label in CHIPS:
        text, w = chip(t["orange"], t, label)
        write(f"chip-{slug}-{theme}.svg", text)
        sizes[f"chip-{slug}"] = w

GRAD = ('<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#c2520f"/>'
        '<stop offset="1" stop-color="#9a3d08"/></linearGradient></defs>')
text, sizes["btn-download"] = button("Download latest", "url(#g)", "#ffffff", defs=GRAD, arrow=True)
write("btn-download.svg", text)
for slug, label in [("thunderstore", "Get it on Thunderstore"), ("versions", "All versions"), ("reference", "Full reference")]:
    text, sizes[f"btn-{slug}"] = button(label, "#21262d", "#e6edf3", stroke="#3d444d")
    write(f"btn-{slug}-dark.svg", text)
    text, _ = button(label, "#f6f8fa", "#1f2328", stroke="#d0d7de")
    write(f"btn-{slug}-light.svg", text)

write("divider.svg", """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1280 8" width="1280" height="8" aria-hidden="true">
  <defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="0">
    <stop offset="0" stop-color="#db6d28" stop-opacity="0"/><stop offset="0.2" stop-color="#db6d28"/><stop offset="0.5" stop-color="#f0883e"/>
    <stop offset="0.8" stop-color="#17a79b"/><stop offset="1" stop-color="#17a79b" stop-opacity="0"/>
  </linearGradient></defs>
  <rect x="0" y="2.5" width="1280" height="3" rx="1.5" fill="url(#g)"/>
</svg>
""")

print("wrote", count, "files")
print("widths:", ", ".join(f"{k}={v}" for k, v in sorted(sizes.items())))
