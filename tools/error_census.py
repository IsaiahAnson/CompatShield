"""Tally recurring error/warning lines across BepInEx session logs, normalised (numbers, object ids and names stripped). Read-only.
Usage: python -I error_census.py <LogOutput.log> [...]   (default: the 4 newest Mod Watcher session archives)"""
import glob
import os
import re
import sys
from collections import Counter, defaultdict

SESS = r"C:\Users\isaia\OneDrive\Desktop\RoR2 Mod Watcher\ror2-mod-watcher\sessions"
paths = sys.argv[1:] or sorted(glob.glob(os.path.join(SESS, "*", "LogOutput.log")))[-4:]
LINE = re.compile(r"^\[[^\]]*\] \[(Error|Warning|Fatal)\s*:\s*([^\]]+)\] (.*)$")
NORM = [
    (re.compile(r"0x[0-9a-fA-F]+|-?\d+(\.\d+)?"), "#"),
    (re.compile(r"\(Clone\)"), ""),
    (re.compile(r"'[^']*'"), "'…'"),
    (re.compile(r'"[^"]*"'), '"…"'),
]


def norm(s):
    for rx, rep in NORM:
        s = rx.sub(rep, s)
    return s[:150]


counts = Counter()
sessions = defaultdict(set)
first = {}
for p in paths:
    tag = os.path.basename(os.path.dirname(p))
    with open(p, encoding="utf-8", errors="replace") as fh:
        for raw in fh:
            m = LINE.match(raw.rstrip("\n"))
            if not m:
                continue
            key = (m.group(1), m.group(2).strip(), norm(m.group(3)))
            counts[key] += 1
            sessions[key].add(tag)
            first.setdefault(key, m.group(3)[:160])
print(f"{len(paths)} log(s): " + ", ".join(os.path.basename(os.path.dirname(p)) for p in paths))
print(f"{'count':>6} {'sess':>4} {'level':7} {'source':22} message")
for key, n in counts.most_common(45):
    print(f"{n:6d} {len(sessions[key]):4d} {key[0]:7} {key[1][:22]:22} {first[key]}")
