"""Build CompatShield as a Thunderstore-style zip that Thunderstore Mod Manager / r2modman can "Import local mod".
Layout: manifest.json, icon.png (256x256), README.md, patchers/CompatShield.dll (installs to BepInEx/patchers/...).
Uses zipfile so entry names have forward slashes (PowerShell 5.1's Compress-Archive writes backslashes).
Usage: python -I make_package.py <version>   e.g. 1.4.3   (the zip lands next to the ror2-compat-shield folder)
Add --thunderstore for the zip that goes on the Thunderstore upload page: the same four files plus the BepInExPack
dependency, written as CompatShield-<version>-thunderstore.zip so the friends' zip is left alone. Its README gets a
mod-manager Install section in place of the one about this PC's manual copy.
The icon is art/icon.png (drawn by art/make_icon.py) when that file is there."""
import hashlib
import json
import struct
import sys
import zipfile
import zlib
from pathlib import Path

VERSION = sys.argv[1] if len(sys.argv) > 1 else None
if not VERSION or VERSION.count(".") != 2:
    sys.exit("usage: make_package.py <major.minor.patch> [--thunderstore]")
THUNDERSTORE = "--thunderstore" in sys.argv[2:]
SHIELD = Path(__file__).resolve().parent.parent
ROOT = SHIELD.parent
DLL = SHIELD / "bin" / "Release" / "netstandard2.1" / "CompatShield.dll"
OUT = ROOT / (f"CompatShield-{VERSION}-thunderstore.zip" if THUNDERSTORE else f"CompatShield-{VERSION}.zip")
ART_ICON = SHIELD / "art" / "icon.png"
BEPINEX = "bbepis-BepInExPack-5.4.2122"  # the version in the Default profile on 2026-11-12


def icon_bytes():
    """The drawn icon when art/icon.png is there, else the plain shield from icon_png()."""
    if not ART_ICON.exists():
        return icon_png()
    data = ART_ICON.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n" or struct.unpack(">II", data[16:24]) != (256, 256):
        sys.exit(f"{ART_ICON} is not a 256x256 PNG (Thunderstore needs exactly that): rerun art/make_icon.py")
    return data


TS_INSTALL = r"""## Install / remove

- Install it with a mod manager (r2modman, Thunderstore Mod Manager or Gale). It is a preloader patcher, so the DLL lands under `BepInEx\patchers`, not `BepInEx\plugins`.
- Start the game from the mod manager. The settings file `BepInEx\config\local.compatshield.cfg` is created on that first launch.
- Remove: uninstall it in the mod manager. CompatShield never edits mod files, so that undoes every fix.
- If you have an older manual copy in `BepInEx\patchers\Local-CompatShield`, delete that folder first so the patcher doesn't load twice.
- When a mod author ships a real fix, the patch just stops matching (and logs that), so it's safe to leave in.
"""


def thunderstore_readme():
    """README.md with the Install section reworded for a mod-manager install (the project file describes this PC's manual copy)."""
    text = (SHIELD / "README.md").read_bytes().decode("utf-8")
    heading = "## Install / remove\n"
    start = text.find(heading)
    end = text.find("\n## ", start + len(heading))
    if text.count(heading) != 1 or end < 0:
        sys.exit("README.md: expected one '## Install / remove' section with another '## ' heading after it; "
                 "fix thunderstore_readme() before building the Thunderstore zip")
    return text[:start] + TS_INSTALL + text[end:]


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
    "dependencies": [BEPINEX] if THUNDERSTORE else [],
}
assert len(manifest["description"]) <= 250, len(manifest["description"])  # Thunderstore's limit
if (SHIELD / "CompatShield.cs").stat().st_mtime > DLL.stat().st_mtime:
    sys.exit("CompatShield.cs is newer than the built DLL: run tools\\run_harness.ps1 (builds Release) first")
with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED) as z:
    z.writestr("manifest.json", json.dumps(manifest, indent=2))
    z.writestr("icon.png", icon_bytes())
    if THUNDERSTORE:
        z.writestr("README.md", thunderstore_readme().encode("utf-8"))
    else:
        z.write(SHIELD / "README.md", "README.md")
    z.write(DLL, "patchers/CompatShield.dll")

with zipfile.ZipFile(OUT) as z:
    for i in z.infolist():
        print(f"{i.file_size:9d}  {i.filename}")
    dll_hash = hashlib.sha256(z.read("patchers/CompatShield.dll")).hexdigest()[:16]
installed = Path.home() / r"AppData\Roaming\Thunderstore Mod Manager\DataFolder\RiskOfRain2\profiles\Default\BepInEx\patchers\Local-CompatShield\CompatShield.dll"
print("zip:", OUT, f"({OUT.stat().st_size} bytes)")
print("DLL in zip matches your installed copy:", installed.exists() and dll_hash == hashlib.sha256(installed.read_bytes()).hexdigest()[:16])
