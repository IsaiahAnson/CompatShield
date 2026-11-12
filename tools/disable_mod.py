# Disables one Thunderstore mod in the Default profile the way the manager does: enabled: false in mods.yml and every file in
# its plugin folder renamed to .old. Backs up mods.yml first, refuses while the game runs, writes a report to tools\backups.
#   python -I disable_mod.py Author-ModName [--dry]
# Run the real thing outside the Claude package (disable-on-close.ps1 does), or the writes land in the package's LocalCache.
import os
import shutil
import subprocess
import sys
import time

PROFILE = "C:/Users/isaia/AppData/Roaming/Thunderstore Mod Manager/DataFolder/RiskOfRain2/profiles/Default/"
BACKUPS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "backups")


def game_running():
    out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq Risk of Rain 2.exe"], capture_output=True, text=True).stdout
    return "Risk of Rain 2.exe" in out


def disabled_text(text, mod):
    nl = "\r\n" if "\r\n" in text else "\n"
    lines = text.split(nl)
    hits = [i for i, l in enumerate(lines) if l == "  name: " + mod]
    if len(hits) != 1:
        raise SystemExit("%s found %d times in mods.yml" % (mod, len(hits)))
    for i in range(hits[0] + 1, len(lines)):
        if lines[i].startswith("- "):
            break
        if lines[i] == "  enabled: true":
            lines[i] = "  enabled: false"
            return nl.join(lines)
        if lines[i] == "  enabled: false":
            raise SystemExit(mod + " is already disabled")
    raise SystemExit("no enabled line for " + mod)


def main():
    mod = sys.argv[1]
    dry = "--dry" in sys.argv
    if not dry and game_running():
        raise SystemExit("the game is running, nothing changed")
    yml = PROFILE + "mods.yml"
    folder = PROFILE + "BepInEx/plugins/" + mod
    text = open(yml, encoding="utf-8", newline="").read()
    new = disabled_text(text, mod)
    files = [os.path.join(r, f) for r, _, fs in os.walk(folder) for f in fs if not f.endswith(".old")]
    if not files:
        raise SystemExit("no files to rename in " + folder)
    report = ["%s: %d files, mods.yml %d to %d chars" % (mod, len(files), len(text), len(new))]
    report += ["  " + os.path.relpath(f, folder) for f in files]
    if dry:
        print("dry run ok\n" + "\n".join(report))
        return
    stamp = time.strftime("%Y%m%d-%H%M%S")
    bak = os.path.join(BACKUPS, "mods-yml-" + stamp)
    os.makedirs(bak, exist_ok=True)
    shutil.copy2(yml, bak)
    open(yml, "w", encoding="utf-8", newline="").write(new)
    for f in files:
        os.rename(f, f + ".old")
    left = [f for f in files if os.path.exists(f) or not os.path.exists(f + ".old")]
    check = "  enabled: false" in open(yml, encoding="utf-8", newline="").read()
    report += ["backup: " + bak, "renamed ok: %s, mods.yml has a disabled entry: %s" % (not left, check)]
    open(os.path.join(BACKUPS, "disable-" + mod + "-" + stamp + ".txt"), "w", encoding="utf-8").write("\n".join(report) + "\n")


main()
