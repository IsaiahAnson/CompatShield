"""Tally MonoMod hook adds/removes per run from a BepInEx LogOutput.log, by mod assembly. Read-only.

RoR2BepInExPack logs every HookEndpointManager operation as
  [Debug  :RoR2BepInExPack] OnHook <hook> added|removed by assembly: <Mod>.dll for: <target>
  [Debug  :RoR2BepInExPack] ILHook <hook> modifier|removed by assembly: <Mod>.dll for: <target>
A "run segment" starts at each "Run Seed:" line and ends at the next one (so it holds that run's start-up adds, its
run-end removes and the lobby in between). Use it to see what still churns after CompatShield's InfernoHookChurnFix.

Usage: python -I hook_churn.py [LogOutput.log]   (default: the Default profile's live log)
"""
import os
import re
import sys
from collections import Counter, defaultdict

DEFAULT = os.path.join(os.environ.get("APPDATA", ""), r"Thunderstore Mod Manager\DataFolder\RiskOfRain2\profiles\Default\BepInEx\LogOutput.log")
LINE = re.compile(r"\] (OnHook|ILHook|NativeDetour) (\S+) (added|removed|modifier|modified|unmodified) by assembly: (\S+?)(?:\.dll)? for: ")

path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT
segments = []          # (label, Counter[(assembly, verb)])
current = ("before the first run", Counter())
with open(path, encoding="utf-8", errors="replace") as fh:
    for raw in fh:
        if "Run Seed:" in raw:
            segments.append(current)
            current = (raw[:raw.find("]") + 1].strip() + " run " + str(len(segments)), Counter())
            continue
        m = LINE.search(raw)
        if m:
            kind, _hook, verb, asm = m.groups()
            if verb == "modifier":
                verb = "IL added"
            elif kind == "ILHook" and verb == "removed":
                verb = "IL removed"
            current[1][(asm, verb)] += 1
segments.append(current)

print(f"{path}\n{len(segments) - 1} run start(s)\n")
total = Counter()
for label, counts in segments:
    if not counts:
        continue
    print(f"== {label}: {sum(counts.values())} hook operations")
    by_asm = defaultdict(Counter)
    for (asm, verb), n in counts.items():
        by_asm[asm][verb] += n
        total[(asm, verb)] += n
    for asm, verbs in sorted(by_asm.items(), key=lambda kv: -sum(kv[1].values())):
        print(f"   {asm:28s} " + "  ".join(f"{v} {n}" for v, n in sorted(verbs.items())))
print("\n== all segments")
by_asm = defaultdict(Counter)
for (asm, verb), n in total.items():
    by_asm[asm][verb] += n
for asm, verbs in sorted(by_asm.items(), key=lambda kv: -sum(kv[1].values())):
    print(f"   {asm:28s} " + "  ".join(f"{v} {n}" for v, n in sorted(verbs.items())))
