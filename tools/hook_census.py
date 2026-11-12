"""Which game methods carry the most mod hooks, and who hooks the hot paths.

Reads RoR2BepInExPack's hook lines from a BepInEx log:
  [Debug  :RoR2BepInExPack] OnHook <hook> added|removed by assembly: <Mod>.dll for: <target>
  [Debug  :RoR2BepInExPack] ILHook <hook> modifier|removed by assembly: <Mod>.dll for: <target>
and prints, for the hooks still in place at the end of the log (added minus removed), the most-hooked targets and, for the
per-frame / per-hit hot paths, every mod on them. A hook on a hot path runs every time the game runs that method: 30 mods on
CharacterBody.RecalculateStats means every stat recalculation runs 30 extra functions.

Usage: python -I hook_census.py <LogOutput.log> [--top 25]
"""
import re
import sys
from collections import Counter, defaultdict

LINE = re.compile(r"\] (OnHook|ILHook|NativeDetour) (\S+) (added|removed|modifier|modified|unmodified) by assembly: (\S+?)(?:\.dll)? for: (.*)$")

# Called per frame, per hit, per kill or per stat change: a hook here costs on every one of those.
HOT = [
    ("RoR2.CharacterBody.RecalculateStats", "every stat recalculation (every buff/item change; many mods force one per frame)"),
    ("RoR2.CharacterBody.FixedUpdate", "every body, every physics tick"),
    ("RoR2.CharacterBody.Update", "every body, every frame"),
    ("RoR2.CharacterBody.OnInventoryChanged", "every item pickup / stack change"),
    ("RoR2.HealthComponent.TakeDamage", "every hit landed (1.5.0 splits it into TakeDamage + TakeDamageProcess)"),
    ("RoR2.HealthComponent.TakeDamageProcess", "every hit landed"),
    ("RoR2.GlobalEventManager.OnHitEnemy", "every hit on an enemy (on-hit items)"),
    ("RoR2.GlobalEventManager.OnHitAll", "every hit on anything"),
    ("RoR2.GlobalEventManager.OnCharacterDeath", "every kill"),
    ("RoR2.GlobalEventManager.ProcessHitEnemy", "every hit on an enemy"),
    ("RoR2.BulletAttack.Fire", "every hitscan shot"),
    ("RoR2.Projectile.ProjectileManager.FireProjectile", "every projectile fired"),
    ("RoR2.Projectile.ProjectileManager.InitializeProjectile", "every projectile spawned"),
    ("RoR2.HealthComponent.Heal", "every heal tick"),
    ("RoR2.CharacterMaster.OnInventoryChanged", "every item change"),
    ("RoR2.CharacterBody.AddTimedBuff", "every timed buff"),
    ("RoR2.CharacterBody.AddBuff", "every buff"),
    ("RoR2.Inventory.GiveItem", "every item given"),
    ("RoR2.CharacterModel.UpdateOverlays", "every model, every frame"),
    ("RoR2.CharacterModel.UpdateMaterials", "every model, every frame"),
    ("RoR2.CharacterBody.UpdateAllTemporaryVisualEffects", "every body, every frame"),
    ("RoR2.CombatDirector.Simulate", "director tick"),
    ("RoR2.Run.FixedUpdate", "run tick"),
    ("RoR2.PlayerCharacterMasterController.FixedUpdate", "every player, every tick"),
    ("RoR2.CharacterMotor.FixedUpdate", "every mover, every tick"),
    ("RoR2.Items.BaseItemBodyBehavior.UpdateBodyItemBehaviorStacks", "every inventory change"),
]


def norm(target: str) -> str:
    t = target.strip()
    # "System.Void RoR2.CharacterBody::RecalculateStats()" or "RoR2.CharacterBody.RecalculateStats" or "On.RoR2..."
    m = re.search(r"([A-Za-z0-9_.`+/<>]+)::([A-Za-z0-9_<>`]+)", t)
    if m:
        return m.group(1).replace("/", "+") + "." + m.group(2)
    t = re.sub(r"^On\.", "", t)
    t = re.sub(r"^IL\.", "", t)
    return re.sub(r"\(.*$", "", t)


def main(path: str, top: int = 25) -> None:
    live: dict[str, Counter] = defaultdict(Counter)      # target -> Counter(mod) of hooks in place
    kinds: dict[str, Counter] = defaultdict(Counter)     # target -> Counter(kind)
    with open(path, "r", encoding="utf-8", errors="replace") as f:
        for line in f:
            m = LINE.search(line)
            if not m:
                continue
            kind, _hook, verb, mod, target = m.groups()
            if mod == "0Harmony":
                continue
            t = norm(target)
            if verb in ("added", "modifier", "modified"):
                live[t][mod] += 1
                kinds[t][kind] += 1
            elif verb in ("removed", "unmodified"):
                live[t][mod] -= 1
                kinds[t][kind] -= 1
    totals = {t: sum(c.values()) for t, c in live.items() if sum(c.values()) > 0}
    print(f"{len(totals)} game methods carry {sum(totals.values())} mod hooks (in place at the end of the log)\n")
    print(f"== {top} most-hooked methods")
    for t, n in sorted(totals.items(), key=lambda kv: -kv[1])[:top]:
        mods = ", ".join(f"{m}" + (f" x{c}" if c > 1 else "") for m, c in live[t].most_common() if c > 0)
        k = " ".join(f"{kk}={v}" for kk, v in kinds[t].items() if v > 0)
        print(f"  {n:3d}  {t}  [{k}]\n       {mods}")
    print("\n== hot paths (run per frame / hit / kill / stat change)")
    for t, why in HOT:
        n = totals.get(t, 0)
        if n == 0:
            continue
        mods = ", ".join(f"{m}" + (f" x{c}" if c > 1 else "") for m, c in live[t].most_common() if c > 0)
        print(f"  {n:3d}  {t}  ({why})\n       {mods}")
    print("\n== hooks per mod (all targets)")
    per_mod = Counter()
    for t, c in live.items():
        for m, n in c.items():
            if n > 0:
                per_mod[m] += n
    for m, n in per_mod.most_common(20):
        print(f"  {n:4d}  {m}")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    top = int(sys.argv[sys.argv.index("--top") + 1]) if "--top" in sys.argv else 25
    main(sys.argv[1], top)
