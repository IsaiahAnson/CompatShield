# Changelog

Newest first. "Every PC" means each player needs the version; "host only" means only the host does.

## 1.4.56 (2026-11-24)

- One fix that fails to load no longer stops the fixes after it. Each logs `<Fix> not applied: ...` and the rest carry on. Every PC.
- Chen's Gradius seed tracker: a tracker whose body has no master warned on every physics tick (about 1,300 lines in 12 minutes). It now warns once and waits out the mod's own 3-second timeout. Every PC.
- Five warnings that could repeat on every projectile, tick or item event now log once, marked `(logged once)`.
- Overloading stake limit, taunt guard and hook reel guard look their game members up once instead of on every call.
- README shortened: one line per fix, settings in collapsed tables, the full tables kept under Reference.
- Test harness: five new modes, including a check that every type name CompatShield looks up still exists in the installed game and mods.

## 1.4.55 (2026-11-23)

- New, host only: `OverloadingStakeCap` (12 live stakes per owner) and `OverloadingStakeCooldown` (0.1 s).
- New, every PC: `QuitCrashGuard` (crash on quit after the sound engine shuts down), `Starstorm2LogQuiet`, hook reel guard, and taunt, Icarus, seed tracker, Death Ceremony, Caeli Imperium and MSU guards under `ErrorLoopGuards`.
- New, off by default: `HeadsetCostFix`.
- Changed, host only: `HardEnemyCap` counts only bodies with a master; `BlastChainCap` also stops a chain by depth.
- Changed: Bubbets Items 1.8.21 Jellied Soles hook is left alone; the `RemoveItem` shim is added only when a plugin needs it.
- Changed: `Big hit:` and `Physics pile-up:` log lines name their source more often.

## 1.4.54 (2026-11-11)

- Free RAM and the game's page faults added to every `Lag:` line.

## 1.4.53 (2026-11-11)

- Two hook timer reporting fixes.

## 1.4.52 (2026-11-10)

- Hook timer: names slow mod hooks in `Hook timer:` lines.

## 1.4.51 (2026-11-10)

- Big-hit tracer: `Big hit:` lines name the source of very large hits.

## 1.4.50 (2026-11-10)

- `LockedItems`: ban items by internal name. Host only.

## 1.4.49 (2026-11-09)

- Physics pile-up tracer.

## 1.4.48 (2026-11-09)

- Sharp Anchor zone errors (StormyItems). Host only.

## 1.4.47 (2026-11-09)

- Paladin shockwave and Sandswept fire trail errors. Every PC.

## 1.4.46 (2026-11-09)

- Sacrifice totem tether error loop. Every PC.

## 1.4.45 (2026-11-09)

- `HardEnemyCap`, Hallowed Ichor run timer fix, settings clean-up.

## 1.4.44 (2026-11-09)

- Limit on repeated stat updates (`StatUpdateInterval`). Each PC for itself.

## 1.4.43 (2026-11-09)

- Settings sorted into eight numbered sections; old settings move over on first launch.

## 1.4.42 (2026-11-09)

- Theta Construct shield loop. Host only.

## 1.4.41 (2026-11-09)

- Extra skill icons get a real size and position.

## 1.4.40 (2026-11-09)

- Dagger of Frenzy guard.

## 1.4.39 (2026-11-09)

- Focus Crystal counts once; extra skill slots visible with RiskUI.

## 1.4.38 (2026-11-09)

- Bubbets Items runs on game 1.5.0. Every PC.

## 1.4.37 (2026-11-09)

- ItemRoulette spins cost real gold; steeper price.

## 1.4.36 (2026-11-08)

- DoT and bullet error-loop guards (host only); Acolyte attack speed x1.5.

## 1.4.35 (2026-11-08)

- One target's error no longer ends a melee hit or a damage zone tick. Host only.

## 1.4.34 (2026-11-08)

- A failing stat update no longer repeats every tick.

## 1.4.33 (2026-11-08)

- Interactable pools with an empty entry no longer break the stage.

## 1.4.32 (2026-11-08)

- Sacrifice stages get twice the interactables (ShareSuite and the game both halved them).

## 1.4.31 (2026-11-08)

- One target's error no longer ends a blast.

## 1.4.30 (2026-11-08)

- Blast loops can no longer hang the game (`BlastChainCap`).

## 1.4.29 (2026-11-08)

- Proc-chain freezes are spread over time (`DamageChainGuard` time limit).

## 1.4.28 (2026-11-08)

- Sacrifice keeps a chosen list of interactables. Host only.

## 1.4.5 to 1.4.27 (2026-10-18 to 2026-10-20)

Not recorded version by version. The main changes:

- 1.4.24: physics catch-up cap.
- 1.4.21: Artifact of Genetics switched off, mod kept.
- 1.4.18: proc-chain stack overflow guard and Hypercrit null fix.
- 1.4.13: lag tracer (`Lag:` lines).
- 1.4.11: fixed a launch crash caused by 1.4.8 to 1.4.10.
- 1.4.6 to 1.4.9: Shaman loads again; stray looping sounds are stopped.
- 1.4.5: lobby hum and the Shaman model.

## 1.4.3 and 1.4.4 (2026-10-18)

- Inferno and PlayerRespawnSystem no longer re-add all their hooks each run (crash at the Start press).

## 1.4.2 and earlier

- First fixes for mods broken by the October 2026 game update. Not recorded version by version.
