"""Build CompatShield as a Thunderstore-style zip that Thunderstore Mod Manager / r2modman can "Import local mod".
Layout: manifest.json, icon.png (256x256), README.md, patchers/CompatShield.dll (installs to BepInEx/patchers/...).
Uses zipfile so entry names have forward slashes (PowerShell 5.1's Compress-Archive writes backslashes).
Usage: python -I make_package.py <version>   e.g. 1.4.3   (the zip lands next to the ror2-compat-shield folder)"""
import hashlib
import json
import struct
import sys
import zipfile
import zlib
from pathlib import Path

VERSION = sys.argv[1] if len(sys.argv) > 1 else None
if not VERSION or VERSION.count(".") != 2:
    sys.exit("usage: make_package.py <major.minor.patch>")
SHIELD = Path(__file__).resolve().parent.parent
ROOT = SHIELD.parent
DLL = SHIELD / "bin" / "Release" / "netstandard2.1" / "CompatShield.dll"
OUT = ROOT / f"CompatShield-{VERSION}.zip"


def icon_png(size=256):
    """Orange shield on a dark background, as a raw RGBA PNG."""
    rows = []
    cx, top, bottom = size / 2, size * 0.14, size * 0.88
    for y in range(size):
        row = bytearray([0])  # filter: none
        t = (y - top) / (bottom - top)
        half = size * 0.34 * (1 if t < 0.45 else max(0.0, 1 - ((t - 0.45) / 0.55) ** 1.6))
        inner = half - size * 0.05
        for x in range(size):
            dx = abs(x - cx)
            if 0 <= t <= 1 and dx <= half:
                rgba = (255, 140, 30, 255) if dx > inner or t < 0.04 or t > 0.97 else (60, 36, 20, 255)
                if dx <= inner and abs(x - cx) < size * 0.035 and 0.12 < t < 0.85:
                    rgba = (255, 140, 30, 255)  # center stripe
                if dx <= inner and 0.30 < t < 0.36 and dx < inner * 0.8:
                    rgba = (255, 140, 30, 255)  # cross bar
            else:
                rgba = (24, 26, 32, 255)
            row += bytes(rgba)
        rows.append(bytes(row))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(b"".join(rows), 9))
            + chunk(b"IEND", b""))


manifest = {
    "name": "CompatShield",
    "version_number": VERSION,
    "website_url": "",
    "description": "Fixes for RoR2 mods broken by the Oct 2026 update (TinkersSatchel, Inferno Mithrix/scaling, hook churn, ER Swift, moved assets, Gasoline SO, guards, REX flowers, freeze/stun, scrap-to-inventory, cauldron hum, Shaman) + cap/spawn/Sacrifice options.",
    "dependencies": [],
}
assert len(manifest["description"]) <= 250, len(manifest["description"])  # Thunderstore's limit
if (SHIELD / "CompatShield.cs").stat().st_mtime > DLL.stat().st_mtime:
    sys.exit("CompatShield.cs is newer than the built DLL: run tools\\run_harness.ps1 (builds Release) first")
with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED) as z:
    z.writestr("manifest.json", json.dumps(manifest, indent=2))
    z.writestr("icon.png", icon_png())
    z.write(SHIELD / "README.md", "README.md")
    z.write(DLL, "patchers/CompatShield.dll")

with zipfile.ZipFile(OUT) as z:
    for i in z.infolist():
        print(f"{i.file_size:9d}  {i.filename}")
    dll_hash = hashlib.sha256(z.read("patchers/CompatShield.dll")).hexdigest()[:16]
installed = Path.home() / r"AppData\Roaming\Thunderstore Mod Manager\DataFolder\RiskOfRain2\profiles\Default\BepInEx\patchers\Local-CompatShield\CompatShield.dll"
print("zip:", OUT, f"({OUT.stat().st_size} bytes)")
print("DLL in zip matches your installed copy:", installed.exists() and dll_hash == hashlib.sha256(installed.read_bytes()).hexdigest()[:16])
