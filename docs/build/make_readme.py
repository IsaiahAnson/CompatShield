"""Builds the GitHub landing README for CompatShield.

Usage: python -I make_readme.py <shipped README.md> <VERSIONS.md> <output README.md>
The shipped README (the one inside the zip) is kept in the repo as docs/REFERENCE.md. The counts, section names
and the fix list on the landing page are read from it, so they cannot drift from it.
"""
import re
import sys

reference_path, versions_path, out_path = sys.argv[1:4]
BASE = "https://github.com/IsaiahAnson/compatshield"
STORE = "https://thunderstore.io/c/riskofrain2/p/Mentalize/CompatShield/"
REF = "docs/REFERENCE.md"

with open(reference_path, encoding="utf-8-sig") as f:
    ref = f.read()
with open(versions_path, encoding="utf-8") as f:
    catalog = f.read()

# section number -> (name it must have in the reference, row icon, what the landing page says is in it)
SECTIONS = {
    1: ("Spawns and Enemies", "spawns", "Spawn speed, enemy caps, how often printers, scrappers and drones appear, drone health"),
    2: ("Artifact of Sacrifice", "sacrifice", "Drop chance, which interactables stay under Sacrifice, the ShareSuite credit fix"),
    3: ("Balance", "balance", "Acolyte attack speed, ItemRoulette spin cost, Artifact of Genetics, locked items"),
    4: ("Lag and Hang Protection", "lag", "Limits on physics catch-up, stat updates, damage chains and blast chains"),
    5: ("Error and Crash Protection", "crash", "Guards for error loops, outdated hooks, broken handlers, moved assets and stuck sounds"),
    6: ("Item Mod Fixes", "items", "Bubbets Items, TinkersSatchel, ZetAspects, Hypercrit, PrintToInventory and other item mods"),
    7: ("Enemy and Survivor Mod Fixes", "enemies", "Inferno, Sandswept, EnemiesReturns, REX, Shaman, Paladin and more"),
    8: ("Diagnostics", "diagnostics", "Tracers for lag, sound, physics pile-ups and big hits, and a hook timer"),
}


def anchor(title):
    return re.sub(r"\s", "-", re.sub(r"[^\w\s-]", "", title.lower()))


def themed(name, attrs):
    return ('<picture><source media="(prefers-color-scheme: dark)" srcset="assets/%s-dark.svg">'
            '<img src="assets/%s-light.svg" %s></picture>' % (name, name, attrs))


def heading(icon, title):
    return "## %s %s" % (themed("icon-" + icon, 'width="30" height="30" align="absmiddle" alt=""'), title)


# ---------- facts read from the shipped README and the catalog ----------
headings = re.findall(r"(?m)^(#{2,3}) (.+)$", ref)
by_anchor = {anchor(re.sub(r"`", "", title)) for _, title in headings}
fix_block = ref.split("\n## What it fixes", 1)[1].split("\n## ", 1)[0]
# The first cell is a bold title, sometimes followed by a note in brackets: keep both, drop the bold marks.
fixes = list(dict.fromkeys(cell.replace("**", "") for cell in re.findall(r"(?m)^\| (\*\*.+?) \|", fix_block)))
assert len(fixes) == len(re.findall(r"(?m)^\| \*\*", fix_block)), "a fix row was not read"

rows, counts = [], []
found =re.findall(r"(?m)^### (\d+)\. (.+)$", ref)
assert [int(n) for n, _ in found] == sorted(SECTIONS), found
for number, name in found:
    want, icon, about = SECTIONS[int(number)]
    assert name.strip() == want, (name, want)
    body = ref.split("\n### %s. %s" % (number, name), 1)[1]
    body = re.split(r"(?m)^#{2,3} ", body, maxsplit=1)[0]
    count = len(re.findall(r"(?m)^\| `[A-Za-z0-9]+` \|", body))
    assert count, name
    title = "%s. %s" % (number, name.strip())
    assert anchor(title) in by_anchor, title
    counts.append(count)
    # An HTML table, because the icon column needs a fixed width (GitHub shrinks an image in an auto-sized cell).
    # GitHub counts the cell's 26px of padding and its border inside that width, so 50 leaves room for a 20px icon.
    rows.append('  <tr><td align="center">%s</td><td><a href="%s#%s">%s</a></td><td>%s</td><td align="right">%d</td></tr>' % (
        themed("mini-" + icon, 'width="20" height="20" alt=""'), REF, anchor(title), title.replace(" ", "&nbsp;"),
        about, count))
total_settings = sum(counts)

for needed in ("what-it-fixes", "multiplayer", "known-limits", "what-it-logs-source-compatshield"):
    assert needed in by_anchor, needed

listed = re.findall(r"(?m)^\| \[\**([\d.]+)\**(?: \(latest\))?\]", catalog)
assert listed, "no version rows in the catalog"
latest = listed[0]
assert "**%s** (latest)" % latest in catalog, latest
first = listed[-1]

chips = [("csharp", "C#", 40), ("bepinex", "BepInEx", 70), ("harmony", "Harmony", 74), ("cecil", "Mono.Cecil", 86),
         ("monomod", "MonoMod", 78), ("netstandard", ".NET Standard 2.1", 127)]
chip_row = "\n  ".join(themed("chip-" + slug, 'height="22" alt="%s"' % label) for slug, label, _ in chips)

DIVIDER = '<img src="assets/divider.svg" width="100%" height="8" alt="">'

page = f"""<p align="center">
  {themed("banner", 'width="100%" alt="CompatShield, a BepInEx patcher for Risk of Rain 2. It fixes mods the October 2026 update broke, without editing a single mod file."')}
</p>

<a href="{STORE}"><img src="https://raw.githubusercontent.com/IsaiahAnson/CompatShield/stats/downloads.svg" width="100%" alt="Download count and latest version on Thunderstore. Get it on Thunderstore."></a>

<p align="center">
  <a href="{BASE}/releases/latest"><img src="assets/btn-download.svg" height="34" alt="Download latest"></a>&nbsp;
  <a href="{STORE}">{themed("btn-thunderstore", 'height="34" alt="Get it on Thunderstore"')}</a>&nbsp;
  <a href="VERSIONS.md">{themed("btn-versions", 'height="34" alt="All versions"')}</a>&nbsp;
  <a href="{REF}">{themed("btn-reference", 'height="34" alt="Full reference"')}</a>
</p>

**CompatShield** is a small fix for Risk of Rain 2 mods that broke on the October 2026 game update (Hallowed Concepts). It is one BepInEx patcher DLL. It doesn't edit any mod files, so deleting it undoes everything.

<p>
  {chip_row}
</p>

{DIVIDER}

{heading("how", "How it works")}

<p align="center">
  {themed("how", 'width="100%" alt="Four steps. Loads first: BepInEx starts it before the game and any mod. Patches in memory: it fixes each broken mod as it loads, and no mod file is changed. Guards the run: it stops error loops, hangs and runaway chains while you play. Steps aside: when a mod ships its own fix, the patch stops and says so in the log."')}
</p>

Every fix is made in memory, as the mod loads, so the mod's files stay exactly as the mod manager wrote them. When a mod author ships a real fix, the matching patch stops applying and says so in the log. That makes CompatShield safe to leave installed.

{heading("fixes", "What it fixes")}

As of {latest}, the fix list has {len(fixes)} entries. Some fix one mod: TinkersSatchel, Inferno, Bubbets Items, Starstorm 2, PaladinMod and more. Others guard against one kind of failure in any mod, such as kill chains that overflow the stack, blast loops that hang the game, and assets the update moved.

<details>
<summary><b>All {len(fixes)} entries</b></summary>
<br>

{chr(10).join("- " + title for title in fixes)}

</details>

Each entry in the [full reference]({REF}#what-it-fixes) says what broke and what CompatShield does about it.

{heading("install", "Install")}

**From Thunderstore:** install [CompatShield by Mentalize]({STORE}) in Thunderstore Mod Manager, r2modman or Gale, then start the game from the mod manager. If you have an earlier copy installed from the zip, delete its `Local-CompatShield` folder under `BepInEx\\patchers` first, so the patcher doesn't load twice.

**From the zip:**

1. Download the zip from the [latest release]({BASE}/releases/latest).
2. In Thunderstore Mod Manager or r2modman, import the zip as a local mod. It replaces any earlier CompatShield.
3. Start the game from the mod manager. The settings file is created on that first launch.

To remove it, uninstall it in the mod manager. For a zip install, delete the `Local-CompatShield` folder under `BepInEx\\patchers` in your profile. CompatShield never edits mod files, so that undoes every fix.

{heading("settings", "Settings")}

All {total_settings} settings live in one file, `BepInEx\\config\\local.compatshield.cfg`. The mod manager's config editor shows them in eight numbered sections.

<table>
  <tr><th width="50"></th><th align="left">Section</th><th align="left">What's in it</th><th align="right">Settings</th></tr>
{chr(10).join(rows)}
</table>

Most fixes are on by default. The spawn, drone and Sacrifice drop multipliers start at 1, which means off. A few gameplay changes are on from the start, such as the hard enemy cap of 80 and the Acolyte attack speed multiplier of 1.5.

The [full reference]({REF}) lists every setting with its default, the [known limits]({REF}#known-limits) and [what CompatShield writes to the log]({REF}#what-it-logs-source-compatshield).

{heading("multiplayer", "Multiplayer")}

CompatShield runs on each PC separately. Some fixes only need the host, some need every PC, and Inferno's difficulty scaling has to match on every PC. The simplest setup is for everyone in the lobby to install the same zip. The [multiplayer notes]({REF}#multiplayer) say which fix needs what.

{heading("build", "Build from source")}

```
dotnet build -c Release
```

The build that ships is `bin\\Release\\netstandard2.1\\CompatShield.dll`. To use it, copy it over the DLL in `BepInEx\\patchers\\Local-CompatShield` while the game is closed.

`CompatShield.csproj` finds the game and BepInEx through its `GameManaged` and `Profile` properties. They point at a default Steam install and a Thunderstore Mod Manager profile named `Default`, so change them if yours differ. `tools\\run_harness.ps1` builds both targets and runs the test harness in `tests\\shieldtest`. Some scripts in `tools\\` and the test project use absolute paths from the PC they were written on, so adjust those before running them.

{heading("versions", "Versions")}

{len(listed)} packaged builds are kept, from {first} to {latest}, each with its zip: see [VERSIONS.md](VERSIONS.md) or the [releases page]({BASE}/releases). The source code here is the latest version. Each older version has a tag that holds the README shipped with that build.

{DIVIDER}

<p align="center"><sub>An unofficial fan project. Not affiliated with the makers of Risk of Rain 2 or with the authors of the mods named here.</sub></p>
"""

with open(out_path, "w", encoding="utf-8", newline="\n") as f:
    f.write(page)
print("README written:", len(page), "characters")
print("version", latest, "| fixes", len(fixes), "| settings", total_settings, "| builds", len(listed), "from", first)
print("section rows:", ", ".join(str(c) for c in counts))
