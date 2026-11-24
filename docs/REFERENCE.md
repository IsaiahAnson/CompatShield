# CompatShield

Keeps Risk of Rain 2 mods working after the October 2026 game update (game 1.5.0), and guards big packs against error loops, hangs and runaway enemy counts.
It doesn't edit any mod files. It's one BepInEx patcher DLL, so removing it undoes everything.

**Who needs it:** packs with mods that broke on game 1.5.0, such as TinkersSatchel, Inferno or Shaman, and big packs that hang or crash in long fights.

## Install / remove

- Install it with a mod manager (r2modman, Thunderstore Mod Manager or Gale). It is a preloader patcher, so the DLL lands under `BepInEx\patchers`, not `BepInEx\plugins`.
- Start the game from the mod manager. The settings file `BepInEx\config\local.compatshield.cfg` is created on that first launch.
- Remove: uninstall it in the mod manager. CompatShield never edits mod files, so that undoes every fix.
- If you have an older manual copy in `BepInEx\patchers\Local-CompatShield`, delete that folder first so the patcher doesn't load twice.
- When a mod author ships a real fix, the patch just stops matching (and logs that), so it's safe to leave in.

## Multiplayer

| Needs it on | Fixes |
|---|---|
| Every PC | Paladin, TinkersSatchel, Mithrix slam, hook churn, Swift, cauldron hum, Shaman, Bubbets, skill slots, Dagger of Frenzy, sound loops, hook reel, Starstorm 2, small guards |
| Every PC, settings must match | Difficulty scaling: same DLL, same Inferno `Difficulty Scaling` and `Level and Difficulty Boost` |
| Host only | REX and Scepter flower, freeze/stun, Gasoline, crash guard, blast cap, hit error guards, pool entry, Inferno cooldown, Sacrifice, enemy cap, Sharp Anchor, ItemRoulette, Focus Crystal, Theta, stakes, locked items, big-hit and pile-up tracers |
| Each PC for itself | Stat limit and retries, Hallowed Ichor, totem tether, Paladin shockwave, fire trail, hook timer |

- Until friends have the DLL, avoid picking Paladin in multiplayer: the mismatch can cause desyncs.
- Shaman is network-compatibility flagged, so every player also needs the Shaman mod.

## What it fixes

### 1. Spawns and Enemies

- **Runaway enemy counts**: summons pile up until the game stops. Host only
- **Dead or runaway enemy objects**: projectiles inflate the enemy count. Host only

### 2. Artifact of Sacrifice

- **Sacrifice drop rate**: the final drop chance can't be scaled. Host only
- **Sacrifice interactables**: no way to keep one kind. Host only
- **ShareSuite + Sacrifice credits**: a stage spends a quarter of its credits. Host only

### 3. Balance

- **ItemRoulette with ShareSuite**: paid spins are free in multiplayer. Host only

### 4. Lag and Hang Protection

- **Kill-chain stack overflow**: nested hits overflow the stack; the game closes. Host only
- **Blast loops**: two blast items feed each other; the game hangs. Host only
- **Gasoline chains**: chained Gasoline blasts overflow the stack. Host only
- **Rapid stat changes**: constant stat recalculation drains the frame rate. Each PC

### 5. Error and Crash Protection

- **Inferno FireWave**: a run-end crash half-breaks the cleanup. Every PC
- **Inferno hook churn**: 85 hooks rebuilt each run crashed the game at Start. Every PC
- **PlayerRespawnSystem**: 35 hooks rebuilt each run; a blank host death timer. Every PC
- **WolfoFixes cauldron hum**: the hum follows you into the lobby. Every PC
- **Blasts that repeat**: a blast whose target errors goes off every tick. Host only
- **Stat updates that fail**: a failing update throws 60 times a second. Each PC
- **Damage zones that repeat**: a zone fires every tick and never expires. Host only
- **DoT ticks that repeat**: one error loses other attackers' damage. Host only
- **Bullets**: a bullet error makes the item fire again every tick. Host only
- **Missing chests and drones**: mods stop at an empty pool entry. Host only
- **Orphaned sound loops**: a loop plays until the game restarts. Every PC
- **Moved assets**: old asset paths lose effects, textures or spawn cards. Every PC
- **AudioOverlapFix**: dropped Stop events leave loops playing. Every PC
- **Starstorm 2 Strange Can**: a crash aborts the death event. Host only
- **Starstorm 2 Relic of Force**: killing blows give no gold, XP or procs. Host only
- **DamageLog 1.2.10**: hitmarkers, damage numbers and the DPS meter break. Host only
- **Artifact of Genetics 4.5.3**: blasts stop at their first target. Host only
- **Freeze/stun/shock**: hits on spawning enemies lose gold and XP. Host only
- **Hook projectile**: the grapple throws every tick. Every PC
- **Starstorm 2 warnings**: dead overrides throw; AfterGummy floods the log. Every PC
- **Chens, Mystics, Caeli, MSU cleanup**: handlers throw after owners vanish. Every PC
- **Sacrifice totem stuck**: it throws every tick and never sacrifices. Each PC
- **Chen's Gradius seed tracker**: a tracker whose body has no master warns about 60 times a second for 3 seconds. Each PC

### 6. Item Mod Fixes

- **TinkersSatchel 6.0.1**: almost nothing loads; names show as raw tokens. Every PC
- **Bubbets Items 1.8.20**: breaks the launch and damage hooks. Every PC
- **Bubbets Items 1.8.21**: its updated hook is left enabled. Every PC
- **Focus Crystal**: counts twice with LinearDamage and Bubbets Items. Host only
- **Dagger of Frenzy (Smite Items)**: skills fail to fire. Every PC
- **Sharp Anchor (StormyItems)**: up to 100 errors a second. Host only
- **Hypercrit2 2.0.x**: a crit roll that throws loses the hit. Host only
- **ZetAspects 2.10.9**: some elite kills skip item procs and drops.
- **ZetAspects 2.11.3+**: already correct; the check stays.
- **PrintToInventory with ShareSuite**: scrap drops on the ground. Host only
- **TinkersSatchel taunt / Icarus / Headset**: stale targets throw. Every PC
- **Hallowed Ichor (Sandswept)**: the run timer stops. Each PC

### 7. Enemy and Survivor Mod Fixes

- **Inferno Mithrix**: no hammer slam, and dash shards multiply each run. Every PC
- **Inferno difficulty**: the level boost misses; enemy cooldowns grow. Every PC
- **ExtraSkillSlots with RiskUI**: the extra slots can't be seen. Every PC
- **Theta Constructs (Sandswept)**: failed casts tank the frame rate. Host only
- **Fire trail (Sandswept)**: other trails skip that tick's damage. Each PC
- **Shaman 1.2.0**: never loads, then loading stalls at 99%. Every PC
- **EnemiesReturns Swift**: errors every tick and a run-end crash. Every PC
- **REX flower**: enemies stay rooted when one was already gone. Host only
- **StandaloneAncientScepter**: the flower stops once its owner is gone. Host only
- **PaladinMod 2.1.0**: the survivor is never added. Every PC
- **Overloading / LightningStake**: repeated stakes pile up projectiles. Host only
- **Paladin shockwave**: about 50 errors a second. Each PC

### 8. Diagnostics

Log-only tools; settings below.

## Settings

The settings file is `BepInEx\config\local.compatshield.cfg`, created on first launch. Each setting has its full description there.

<details>
<summary>1. Spawns and Enemies (9 settings)</summary>

| Setting | Default | What it controls |
|---|---|---|
| `SpawnCreditMultiplier` | `1` | How fast the stage's regular enemy spawners earn credits, so waves come sooner. 1 = game default, 1.5 = about a third sooner. Teleporter, shrine and special-mode spawners are left alone. Host only. |
| `MonsterLimit` | `0` | Most enemies alive at once while Multitudes No Price Scale is installed. The game's own limit is 40; 0 leaves the game and Multitudes alone. |
| `HardEnemyCap` | `80` | The most hostile team members alive at once with a CharacterBody and live master. Unlike MonsterLimit this also stops summons that ignore the games team limit, such as death effects, clones and mods minions, which can otherwise ... |
| `PrinterSpawnMultiplier` | `1` | How likely each stage spawn pick is a 3D printer (white, green, red, overgrown). 1 = game default, 4 = four times as likely. Host only. |
| `ScrapperSpawnMultiplier` | `1` | How likely each stage spawn pick is a scrapper. 1 = game default, 2 = twice as likely. Host only. |
| `DroneSpawnMultiplier` | `1` | How likely each stage spawn pick is a broken drone or turret. 1 = game default, 5 = five times as likely. Host only. |
| `DroneCombinerSpawnMultiplier` | `1` | How likely each stage spawn pick is a Drone Combiner, compared to the unmodded game. This is the total: it already allows for DroneSpawnMultiplier. 1 = game default, 5 = five times as likely. Host only. |
| `DroneNoMinimumStage` | `false` | Lets every broken drone and turret spawn from stage 1, instead of waiting for the stage count the game sets on some of them. Host only. |
| `DroneHealthMultiplier` | `1` | Max health of allied drones and gunner turrets, including drones from items. Enemy drones are left alone. 1 = game default, 10 = ten times the health. Host only. |

</details>

<details>
<summary>2. Artifact of Sacrifice (4 settings)</summary>

| Setting | Default | What it controls |
|---|---|---|
| `DropChanceMultiplier` | `1` | Multiplies each kills item drop chance under Artifact of Sacrifice. 1 = game or mod default, 0.7 = 30% fewer drops, 0 = none; below 0 is ignored. Applies after other installed drop chance changes. Host only. |
| `KeepInteractables` | (empty) | Interactables that still spawn under Artifact of Sacrifice, as a comma-separated list of spawn card names. A name also covers longer ones starting with it, so iscShrineChance covers the Sandy and Snowy versions. |
| `KeptInteractableWeight` | `1` | How much of a spawn category's share goes to the cards kept by KeepInteractables when Sacrifice removes the rest of that category. 1 = the game's own math, 0.5 = half that, 0 = none; below 0 is ignored. Host only. |
| `ShareSuiteSacrificeCreditFix` | `true` | ShareSuite and the game both halve a Sacrifice stage's interactable credits, so the stage spends a quarter. On = only the game halves them, which gives twice the interactables, drones included. Off = both halve. Host only. |

</details>

<details>
<summary>3. Balance (4 settings)</summary>

| Setting | Default | What it controls |
|---|---|---|
| `AcolyteAttackSpeedMultiplier` | `1.5` | Acolyte's attack speed, in both the normal and the transformed form. 1 = game default, 1.5 = 50% faster; 0 or below is ignored. Each player's own Acolyte uses the value on that player's PC. |
| `LockedItems` | (empty) | Items and equipment locked off in every lobby, as a comma-separated list of internal names (the names in Def.StartBonusMod.cfg, for example ChainLightning). |
| `RouletteSpinCost` | `true` | Takes a paid ItemRoulette spin out of ShareSuite's shared gold pool too. Without this, ShareSuite refunds every spin on the next frame, so spins are free in multiplayer. Does nothing with shared money off or in single player. |
| `DisableGenetics` | `true` | Keeps the Artifact of Genetics (GeneticArtifact mod) off: a vote for it is ignored and it is hidden from the lobby, while the mod stays installed. Set false to get it back. The host decides. |

</details>

<details>
<summary>4. Lag and Hang Protection (8 settings)</summary>

| Setting | Default | What it controls |
|---|---|---|
| `StatUpdateInterval` | `0.1` | Shortest time between one character's automatic stat updates, in seconds. Items and auras that flip a shield or refresh a buff every physics tick made the game redo a character's full stat calculation up to 60 times a second, ... |
| `PhysicsCatchUpCap` | `0.1` | Limits how much physics Unity runs to catch up after a slow frame, in seconds. 0.1 = at most 6 steps per frame, 0.05 = at most 3, so under heavy load game time slows a little instead of the frame rate collapsing. |
| `DamageChainGuard` | `true` | Stops kill chains overflowing the stack and crashing the game. Hits nested more than 3 deep, and Gasoline blasts set off inside another Gasoline blast, wait in line and run after the outer hit. |
| `DamageChainQueueLimit` | `256` | Goes with DamageChainGuard. How many hits from a chain may wait in line at once. Past this, hits nested more than 3 deep are dropped; direct hits and the first 3 links always land. Host only. |
| `DamageChainDrainMs` | `4` | Goes with DamageChainGuard. Milliseconds of waiting hits that may run per physics tick, so a huge chain lands over a few seconds instead of freezing the game. 0 = no limit, the whole line runs in one frame. Host only. |
| `BlastChainCap` | `true` | Stops item combos with exploding on-hit effects (such as Marwans Ash and Fire Shield) from hanging the game. A blast set off inside other blasts stops firing at BlastChainMaxDepth; |
| `BlastChainMaxDepth` | `4` | Goes with BlastChainCap. How many blasts may run inside each other at once. A deeper one deals no damage and sets off nothing. Minimum 1. Host only. |
| `BlastChainBudgetMs` | `100` | Goes with BlastChainCap. Minimum elapsed milliseconds before the nested-blast count limit can stop a chain. A 2 second hard ceiling still applies. Host only. |

</details>

<details>
<summary>5. Error and Crash Protection (10 settings)</summary>

| Setting | Default | What it controls |
|---|---|---|
| `ErrorLoopGuards` | `true` | Stops one error repeating every physics tick. Without these, an error inside a blast, melee hit or damage zone, damage-over-time tick, bullet, stat update or sacrifice totem's tether makes the game run that code again and again. |
| `QuitCrashGuard` | `true` | Skips Wwise callback draining after native sound-engine shutdown to prevent a quit crash. Every PC. |
| `Starstorm2LogQuiet` | `true` | Prunes destroyed Starstorm2 attacker overrides and mutes its AfterGummy termination lookup warning. Every PC. |
| `OutdatedHookGuard` | `true` | Skips hooks a game update broke so the mod keeps loading, and ignores removing a hook that was never added. Every PC. |
| `MovedAssetRedirect` | `true` | When a mod asks for a game asset by a path the October 2026 update moved, loads it from its new path. Every PC. |
| `BrokenHandlerGuard` | `true` | Catches known, harmless crashes in Starstorm 2 (Strange Can, Relic of Force), DamageLog and Artifact of Genetics, so they stop breaking kills and damage. Host only. |
| `StunDurationFix` | `true` | Game bug: a freeze, stun or shock on an enemy that is still spawning crashes and drops the rest of the hit, including the gold and XP on a kill. Host only. |
| `NullDccsPoolEntryFix` | `true` | Removes empty entries from a stage's interactable pool, so Chen's drones, Tinker's Satchel and QoLChests can add their chests and drones. Logs one line per pool it cleans. Host only. |
| `HookChurnFixes` | `true` | Stops Inferno and PlayerRespawnSystem removing and re-adding all their hooks at every run, the kind of work that crashed the game when Start was pressed. They keep their hooks between runs instead, and runs play the same. |
| `StuckSoundFixes` | `true` | Stops looping sounds that would otherwise play until the game restarts: Stop events AudioOverlapFix would drop, loops left behind when their object is destroyed, and WolfoFixes' cauldron hum. One-shot sounds are left alone. |

</details>

<details>
<summary>6. Item Mod Fixes (10 settings)</summary>

| Setting | Default | What it controls |
|---|---|---|
| `BubbetsItemsFix` | `true` | Lets Bubbets Items 1.8.20 run on game 1.5.0, and repairs Jellied Soles, Repulsion Plate Mk2, Void Scrap, Broken Clock, Wildlife Camera and the Slug health bar. Every PC. |
| `HallowedIchorTimerFix` | `true` | Sandswept: stops Hallowed Ichor throwing an error every frame and freezing the run timer once anyone picks it up, on a HUD without its text effect. Every PC. |
| `TinkersSatchelFixes` | `true` | Sets up each TinkersSatchel item on its own, so one broken item doesn't stop the rest, and shows its item names and descriptions as text instead of TINKERSSATCHEL_..._RENDERED. Every PC. |
| `HeadsetCostFix` | `false` | Skips TinkersSatchel Headset work for bodies without an inventory. Every PC. |
| `LinearDamageFocusCrystalFix` | `true` | Stops Focus Crystal counting twice when Bubbets Items and LinearDamage are both installed. Host only. |
| `DaggerOfFrenzyGuard` | `true` | Stops Smite Items' Dagger of Frenzy breaking skills on bodies with no skill locator or primary skill. Every PC. |
| `ZetAspectsDropFix` | `true` | Stops ZetAspects' kill hook crashing on an elite's equipment slot, which skips that kill's item procs and drops. |
| `HypercritNullFix` | `true` | When Hypercrit2's crit roll throws an error, counts it as no crit so the hit still lands. Host only. |
| `PrintToInventoryScrappers` | `true` | Lets PrintToInventory load alongside ShareSuite and keeps only its scrapper part: scrap goes straight into the scrapping player's inventory. Printers, cauldrons, cleansing pools and the Wandering Chef stay as they are. |
| `SharpAnchorZoneFix` | `true` | StormyItems: stops a Sharp Anchor zone that has lost track of its holder throwing an error every tick, up to 100 a second. Host only. |

</details>

<details>
<summary>7. Enemy and Survivor Mod Fixes (13 settings)</summary>

| Setting | Default | What it controls |
|---|---|---|
| `OverloadingStakeCap` | `12` | Maximum live EliteReworks Overloading stakes per owner. 0 disables the cap. Host only. |
| `OverloadingStakeCooldown` | `0.1` | Minimum seconds between Overloading stakes from one owner. Host only. |
| `InfernoMithrixFix` | `true` | Inferno 1.7.8: makes Mithrix's hammer slam work again, and stops his dash shards multiplying each run. Every PC. |
| `InfernoLevelBoostFix` | `true` | Inferno: makes its Level and Difficulty Boost reach monsters when RiskyTweaks raises the level cap. Raises difficulty; the amount is Inferno's own setting. Every PC. |
| `InfernoCooldownScalingFix` | `true` | Inferno: makes Enemy Cooldown Reduction Scaling shorten enemy cooldowns, as its description says, instead of lengthening them. Host only. |
| `ThetaShieldLoopFix` | `true` | Sandswept: stops Theta Constructs repeating a failed shield cast every tick, which drops the frame rate and loops a sound. Host only in effect. |
| `SwiftImpactFix` | `true` | EnemiesReturns: stops Swift breaking the game's Beetle Guard impact effect, which causes errors every tick and a run-end crash. Every PC. |
| `RexFlowerFixes` | `true` | REX's Tangling Growth flower: unroots every enemy when one was already gone, and keeps working when REX died or left the stage with Ancient Scepter installed. Host only. |
| `ShamanModelFix` | `true` | Rebuilds the one method that stops the deprecated Shaman mod loading on game 1.5.0, and fixes the name clash that stalled loading at 99%. Every PC, and every player needs the Shaman mod. |
| `ExtraSkillSlotsHudFix` | `true` | Builds ExtraSkillSlots' four extra icons to fit a replacement HUD such as RiskUI, so the slots can be seen. Each player, for their own screen. |
| `PaladinMaterialsFix` | `false` | Crash-proofs PaladinMod 2.1.0's material setup. Off by default: PaladinMod 2.2.0 fixed this itself. Every PC. |
| `PaladinShockwaveFix` | `true` | PaladinMod: stops a shockwave throwing an error every tick, about 50 a second, once its dust effect is gone. Every PC. |
| `SandsweptFireTrailFix` | `true` | Sandswept: stops the Delta Construct's fire trail throwing an error when its last flame burns out, which skipped other trails' damage that tick. Every PC. |

</details>

<details>
<summary>8. Diagnostics (5 settings)</summary>

| Setting | Default | What it controls |
|---|---|---|
| `LagTracer` | `true` | Finds where lag comes from: during a run, logs fps, the worst frame, counts of monsters and projectiles, and what each hot code path cost, every 10 s. About 6 log lines a minute. Set false to silence it. |
| `SoundTracer` | `true` | Names an audio loop that follows you into the lobby: at each run end, logs loop-type sounds started on objects that no longer exist. A few log lines per run end. Set false once the loop is found. |
| `BigHitTracer` | `true` | Names what dealt a huge hit to a player. During a run it logs the victim, the attacker, the object that carried the damage, the damage type, the proc chain and the code path for any hit that takes 25% or more of a player's full ... |
| `PhysicsPileupTracer` | `true` | Names a physics pile-up: after a frame over 0.5 s during a run, logs which moving objects hold the most colliders and how many share one spot, at most every 10 s. Silent while the game runs smoothly. Only the host needs it. |
| `HookTimer` | `false` | Lag hunting: names the mods whose code costs the most. From the first run start it times each mod's hooks, Harmony patches and event handlers on about 40 busy game methods (stats, damage, buffs, inventory, per-frame updates) and ... |

</details>

## Known limits

- `HardEnemyCap`: a mod that assumes its limit-ignoring summon worked may log a NullReference at the cap.
- `HardEnemyCap`: a summon that asks again from its own failure callback can pass the cap by a few.
- If respawn positions look wrong, switch `HookChurnFixes` off (that also turns off Inferno's half).
- Archaic Wisp 1.1.9 with `Stage List = none`: Archaic Wisps can still arrive through other spawn paths and never attack.

## Reference

<details>
<summary>Every fix in full: what broke and what CompatShield does</summary>

| Mod | What broke | What CompatShield does |
|---|---|---|
| **TinkersSatchel 6.0.1** | Its hook on `Language.SetCurrentLanguage` was rejected (the game added a parameter), which aborted its startup, so almost none of its content loaded. That hook is also what installs its item text, so names showed as raw tokens like `TINKERSSATCHEL_MUG_NAME_RENDERED`. Past that, **Timelost Rum** references a hook type the update removed (`orig_FireProjectile`), and its crash stopped every item after it. | Skips that outdated hook so the rest of the mod loads, then redoes the hook's work (installing each item's name, pickup and description text) after the game's new two-argument `SetCurrentLanguage`. Also skips hooks whose hook type was removed, and sets up and installs each item on its own, so one broken item (Timelost Rum) is logged and skipped instead of stopping the items after it. Once fully loaded, its auto-config update (which still calls the removed one-argument `Language.SetCurrentLanguage`) would throw every frame, so CompatShield runs that small update itself using the new two-argument version. |
| **Inferno 1.7.8 (Mithrix slam, dash shards)** | (1) Its `Hooks.WeaponSlam` resizes the hammer-slam pillar's ghost through `ProjectileController.ghostPrefab`, which game 1.5.0 leaves empty (the ghost now comes from `ghostPrefabAddress`). It crashes before calling the game's slam, so Mithrix just stands there for 3.5 s (39 times in one session); `CleanupPillar` crashes the same way. (2) Its run-end `UndoHooks` adds its `SlideIntroState` hook (`+=`) instead of removing it, so every run stacks another copy: 11 copies in one evening, about 66 lunar shards per Mithrix dash instead of 6. (3) `UndoHooks` also re-adds three reset hooks at every run end, and after a non-Inferno run a leftover copy shrinks Mithrix's pillar back to 1x in the next Inferno run. | (1) Takes the ghost from its new address (only the ghost resize is skipped if there is none), and if the hook still crashes before the slam, starts the slam anyway. (2) Turns that one add into a remove. (3) Makes each reset-hook add "remove, then add", so at most one copy is attached. |
| **Inferno 1.7.8 (difficulty scaling)** | (1) Its "Level and Difficulty Boost" (+1.25 monster levels) is an IL hook that inserts the boost just before the game reads `Run.ambientLevelCap`. RiskyTweaks' "Raise Level Cap" has already replaced that read with its own `RaiseLevelCap.levelCap`, so Inferno logs "Failed to fully apply Level and Difficulty boost" at every run start and monsters never get the boost. (2) Its "Enemy Cooldown Reduction Scaling" is added to R2API's `cooldownMultAdd`, which makes enemy cooldowns *longer* every stage (+1% per stage), the opposite of what it says. | (1) Widens Inferno's match so it also accepts another mod's level-cap read, so the boost lands where Inferno meant it and RiskyTweaks' 9999 cap stays (don't turn Raise Level Cap off: Starstorm 2 then resets the cap to 99). (2) Sends the same amount to cooldown *reduction* instead: x0.95 enemy cooldowns at 5 stages instead of x1.05. |
| **Inferno 1.7.8** | Its Mithrix `FireWave` hook is rejected the same way; at the end of each run it then crashes removing the hook it never added ("Stack empty"), half-breaking the end-of-run cleanup. | Same hook skip, plus ignores the "remove a hook that was never added" crash. The Inferno-difficulty Mithrix tweak that hook provided stays off. |
| **Inferno 1.7.8 (hook churn crash)** | At every run start on its difficulty it adds its 82 enemy-skill hooks one by one (`HookBehavior.ApplyHooks`, which also removes its 3 reset hooks), and at every run end it undoes all of that (`UndoHooks`), so each run builds and tears down 85 MonoMod detours. On 2026-10-18 the sixth cycle of a session crashed the game's C# runtime (access violation in `mono-2.0-bdwgc.dll`) while adding the 58th hook, `FireConstructBeam.OnEnter`, the moment Start was pressed in the lobby; the five cycles before it, and nine that morning, had worked. | Keeps the hooks between Inferno runs: `ApplyHooks` runs for real once per game session, the run-end `UndoHooks` is skipped while they are kept, and when a run on another difficulty starts the kept hooks are taken out with Inferno's own `UndoHooks`, so that run plays without Inferno's skill changes and the next Inferno run adds them again. Inferno's per-run stat and AI hooks are untouched. This removes the biggest burst at a Start press (Inferno's 82 of roughly 130 hook operations on this profile); PlayerRespawnSystem's 35 per-run hooks are kept too (next row); ServerSider's 8, Starstorm2's 2-4 and Inferno's own 6 run-start hooks still cycle each run, so if the same crash ever recurs at Start, those are next (`tools\hook_churn.py` tallies them from a log). |
| **PlayerRespawnSystem 2.2.0 (hook churn)** | Its 35 respawn hooks are instance methods on Unity components that it builds at every run start (its `Run.Awake` hook adds a `PlayerRespawnSystem` component, whose `Awake` adds a `PlayerRespawner`, eight respawn controllers and all their hooks) and destroys at every run end (its `Run.OnDestroy` hook; the `OnDestroy` chain removes all 35). With Inferno's hooks kept, that is the biggest per-run hook churn left on this profile: 35 detours built at every Start press and torn down at every run end, the same kind of work that crashed the game on 2026-10-18. | Keeps the component between runs: the run-end `Object.Destroy(playerRespawnSystem)` becomes a keep (its user timers are cleared), and the run-start `AddComponent<PlayerRespawnSystem>()` hands the kept one back after resetting what a fresh one starts with (respawn queue empty, no stage transition under way, the Timed controller active and the boss/teleporter controllers inactive, user timers cleared; the game re-adds each player's timer at run start as before). Inside a run everything is the mod's own code on the same objects. For a run in a game mode the mod ignores (Simulacrum by default) it adds nothing, so the kept component is destroyed at that run's start (the mod's own run-end path, one run late) and the next normal run builds a fresh one. The mod's "Enabling X respawn controller" lines now appear once per session instead of once per run. Every PC builds the component itself, so every PC needs it. Also (2026-10-19): the mod unspawns its death-timer object at every run end and re-spawns the same object at the next run; one run in sixteen the host's own client lost track of it (104 × "Could not find target object with netId:9 for RPC call ... InvokeRpcTargetUpdateDeathTimer", a blank death timer on the host). Before each timer update the fix re-registers the object with the host's local client when it is missing. |
| **WolfoFixes (Lunar Cauldron / Cleansing Pool hum)** | Its "unused sounds" feature restores the game's idle hum for Lunar Cauldrons and Cleansing Pools by posting `Play_ui_obj_lunarPool_idle_loop` (three times) on every pool when it spawns, and never posts the matching `Stop_ui_obj_lunarPool_idle_loop` (both exist in the game's sound bank; the mod has no toggle). Wwise keeps a loop playing after its object is destroyed, and this is a 2D "ui" sound, so after a stage with a pool, or a run that ended on the Moon, the hum follows you into the lobby and the next stages until the game restarts (2026-10-18, after a team wipe on Commencement). | Right after the mod's hook has run, gives each pool a small component that posts the Stop event the moment the pool is disabled or destroyed. The hum still plays in the stage; it just ends with the pool. Every PC plays the loop itself, so every PC needs it. |
| **Kill-chain stack overflow (any mod)** | With 45 + 66 + 34 + 23 mod hooks on the damage path, one hit is 260 stack frames; a chain of hits nested in hits overflows the 1 MB stack and the game fail-fasts (0xC0000409). `DamageChainGuard` runs hits nested more than 3 deep one at a time after the outer hit, and spreads a huge chain over later physics ticks so it can't freeze the game. (GasolineChainFix is the same idea for Gasoline alone.) |
| **Blast loops that hang the game (any mod)** | A blast set off from inside another blast's hits runs before the outer one finishes, so two such items can feed each other until the game stops responding (Marwan's Ash and Fire Shield, 2026-11-08). The hits go through on-hit effects, which `DamageChainGuard` can't hold back. | `BlastChainCap` stops blasts at the nesting limit. Elapsed time alone cannot stop a short chain; after 32 nested blasts, the configured delay or a 2 second hard ceiling can stop a runaway. A blast that isn't inside another one always fires. Host only. |
| **Blasts that go off again every tick (any mod)** | When one target's damage threw an error, the whole blast stopped there: the later targets were skipped and the blast object wasn't destroyed, so it went off again next physics tick and hit the earlier targets again until the error stopped (549 of the 560 null errors on 2026-11-08). | `BlastTargetGuard` catches the error per target, so the blast finishes and its object goes away. The target that failed loses the rest of that hit, and the log names the victim, the attacker and where it was thrown. It doesn't cure whatever was null. Host only. |
| **Stat updates that fail every tick (any mod)** | The game clears a character's "stats need updating" flag as the last step of the update, so when the update threw an error the flag stayed set and the whole calculation ran again and threw again 60 times a second for as long as the error lasted (1,293 errors on 2026-11-08, 69 fps down to 32 with no monsters alive). | `StatsRetryGuard` keeps the next-tick retry after a first failure, then waits 0.25, 0.5, 1, 2, 4 and at most 8 seconds between tries. Anything that really changes the character's stats still triggers an update straight away. The error itself still happens and still shows in the log, and the failed update behaves as it did before: this only stops the repeat, and it doesn't cure whatever was null. An error thrown by a mod's hook before it calls the game method isn't seen, so that kind still repeats. The log names the character, what was null, its items and its buffs. Each PC protects only itself, so it helps on every PC and the host alone still protects the host. |
| **Damage zones that fire every tick (any mod)** | Melee swings and ground damage zones land their hits through the same game code. When one target's damage threw an error, the later targets were skipped and the hit list stayed full, and a zone also skipped its timer and its expiry, so it fired again next physics tick, hit the earlier targets again and couldn't run out while the error lasted (228 errors in 4 seconds on 2026-11-08, all from zones). | `OverlapHitGuard` catches the error around one target's damage, so the swing or zone tick finishes, the later targets still get hit, and a zone keeps its normal timing and expires. The target that failed loses the rest of that hit, and the log names the victim, the attacker, the inflictor and where it was thrown. It doesn't cure whatever was null. An error outside target damage (the hit search, an effect, a hook on Fire) still repeats, but none of the 228 did. A damage-over-time tick that reaches the same throw site takes another route, which `DotTickGuard` covers. Host only. |
| **Damage-over-time ticks that fire every physics tick (any mod)** | The game stores a DoT's next tick time only after the tick finishes. When one attacker's damage threw an error, the tick stopped there: the other attackers' damage was lost and the timer never moved on, so the same tick ran again next physics tick for as long as the error lasted (933 errors in one run on 2026-11-08). | `DotTickGuard` catches the error around one attacker's damage, so the rest of the tick runs and the timer moves on. The attacker that failed loses that tick, and the log names the victim, the attacker, the DoT and where it was thrown. It doesn't cure whatever was null. Host only. |
| **Bullets that stop an item's timer (any mod)** | A bullet's damage throwing an error went all the way up to whoever fired it. An item that sets its timer after firing never got there, so it fired again next physics tick (Tinker's Satchel's Motion Tracker, 138 errors in one minute on 2026-11-08). | `BulletHitGuard` catches the error around the bullet's damage and on-hit effects, so the shot finishes and the item keeps its timing. The target that failed loses the rest of that hit, and the log names the victim, the attacker, the inflictor and where it was thrown. An error from the game's last on-hit step (`OnHitAll`) isn't covered. It doesn't cure whatever was null. Host only. |
| **ItemRoulette spins never cost anything in multiplayer (ItemRoulette with ShareSuite)** | ItemRoulette charges only the player's own gold, and ShareSuite writes the shared pool's value over every player's gold each frame, so the charge was gone a frame later. | `RouletteSpinCost` takes the same amount out of the shared pool on each paid spin. Casino bets and chaos gold events are still undone by ShareSuite. Host only. |
| **Bubbets Items breaks the launch, and `InvalidProgramException` follows every mod that hooks damage (Bubbets Items 1.8.20 on game 1.5.0)** | The old Jellied Soles damage hook lands where game 1.5.0 starts building a struct, so it hands its delegate a pointer instead of a float. | `BubbetsItemsFix` rewrites the old 1.8.20 hook. It recognizes the 1.8.21 hook as already fitting the game and leaves it enabled; unknown shapes still have that one hook switched off. The preloader restores `Inventory.RemoveItem(ItemDef, int)` only if an installed plugin still calls it. Every PC. |
| **Focus Crystal counts twice (LinearDamage with Bubbets Items)** | LinearDamage removes vanilla Focus Crystal by matching the item load and the count call side by side. Bubbets Items puts its Shifted Quartz and Zealotry Embrace code between them, so the match fails, vanilla stays, and LinearDamage's additive bonus lands on top. | Does the same removal past that code. When the two are still adjacent, or the code between them isn't plain inserted code, the mod's own hook runs unchanged. Host only. |
| **Extra skill slots work but can't be seen (ExtraSkillSlots with RiskUI)** | ExtraSkillSlots copies the HUD's skill icons and looks for children named `SkillBackgroundPanel` and `SkillKeyText`. RiskUI's HUD has different children, so `ExtraHud.Awake` throws and all four icons stay null. | Builds each copy without those names, one row above the icon it came from, and wires the key labels the same way when the icon has one. With the normal HUD the mod's own code runs. Every PC: each player's HUD is local. Since 1.4.41 each copy takes its icon's real size and position every frame, because RiskUI's icons measure 0x0 when the HUD starts. |
| **Dagger of Frenzy breaks skills (Smite Items)** | Its hook on every skill use reads the body's skill locator and primary skill with no null check, before running the skill. On a body with neither, it throws, the skill doesn't fire, and the game tries again (702 errors in one Bazaar visit). | Adds the two null checks. Without a locator or primary skill the buff is skipped and the skill runs normally. Applied at the first run start. Every PC: skills run on each player's game. |
| **Theta Constructs tank the frame rate and loop a sound (Sandswept)** | When a Theta's shield target is another Theta, or the line to it is blocked, the cast fails without using the skill, so the AI fires it again every tick. Each failed cast searches every object in the scene and plays an expire sound (6-11 fps on Abandoned Aqueduct with 12 monsters). | A failed cast now puts the skill on a 1 second retry and skips that cast's exit. Real shields are unchanged. Applied at the first run start. Host only in effect: enemy AI runs on the host. |
| **Fire trail errors (Sandswept)** | When a Delta Construct's fire trail loses its last flame, Sandswept resizes the trail from an empty list and the game throws. That ends the trail update early, so other trails skip that tick's damage and burnt-out trails aren't cleared. | An empty trail keeps its old size; with no flames left nothing uses it. `SandsweptFireTrailFix`, applied at the first run start. Every PC. |
| **Sharp Anchor error flood (StormyItems)** | StormyItems keeps its anchor zones in a list that should line up with its list of bodies. When a body missing from that list is removed, the mod still removes the first zone, so the lists shift apart and a zone can point at a body with no inventory. The mod never cleans that zone up, and every server tick it throws reading the holder's anchor count, up to 100 errors a second (2,018 on one Lakes at Night stage). | When the zone's holder lookup would throw, the zone skips that tick. The throw came before the buff, so the zone gave nothing in that state anyway. `SharpAnchorZoneFix`, applied at the first run start. Host only. |
| **Mods' chests and drones missing from some stages (any mod that adds interactables)** | Some stages have an interactable pool entry with nothing in it (ironalluvium2 on 2026-11-08). Chen's drones, Tinker's Satchel and QoLChests each threw an error at one and stopped, so the rest of their cards and changes missed that stage. | `NullDccsPoolEntryFix` removes the empty entries when the stage loads, before anything walks the pool, and from a variant pool when a stage swaps one in. In the two optional lists the game already ignored them, so those picks don't change. An empty entry in `alwaysIncluded` used to abort the game's own card rebuild, which now finishes. The mods' own cards now reach those stages. MSU's separate error, on a stage with no live `alwaysIncluded` entry, isn't covered. Host only. |
| **Hypercrit2 2.0.x cancelled hits** | Its crit roll throws NullReferenceException on some hits, inside its IL hook in TakeDamageProcess, so the whole hit is lost. `HypercritNullFix` counts such a roll as no crit. |
| **Orphaned sound loops (any enemy or mod)** | A looping sound is tied to the object that started it, and the game normally sends its stop from that object's attack state or `LoopSoundManager`. When the object is destroyed before that happens, e.g. a Solus Amalgamator Flamethrower Cannon that Multitudes spawned 0.2 s *after* the run had already ended (2026-10-19, "flamethrower hiss" in the lobby), Wwise keeps the loop playing on its own until the game restarts or something else happens to end it. Nothing in the game or any installed mod calls `AkSoundEngine.StopAll`. | Records every sound event posted on an object (the same bookkeeping as the `SoundTracer` diagnostic), and when the game unregisters a destroyed object from Wwise (`HG.AkGameObjManager.UnregisterDelayed`) stops, by playing ID with a 250 ms fade, every loop-type sound that object started and never stopped. Sounds that got their stop are already forgotten, one-shots aren't loop-type, and stopping a finished sound is a no-op, so it can only end sounds whose emitter is gone. Logs each distinct loop it had to stop once, with the object's name. Every PC needs it. |
| **Shaman 1.2.0 (StormRiders, deprecated)** | Its `Prefabs.SetupCharacterModel` writes `characterModel.invisibilityCount = 0` as a field store. Game 1.5.0 turned `CharacterModel.invisibilityCount` into a property, so that method can't be compiled at all (`MissingFieldException` the first time it runs), the Shaman body and its three Acolyte bodies are never built, and the mod's startup aborts. It is the only game reference the DLL gets wrong (786 checked); its R2API types resolve through the R2API legacy forwarder and its eight legacy asset paths are still mapped. | Re-emits that one method from the mod's own IL (read from the DLL on disk, since a reflection read dies on the dead field token) with the store turned into a call to the property setter, and points its two callers (`CharacterBase.InitializeCharacterModel` and the one-argument overload) at the re-emitted copy. Second, once the mod loaded for the first time (2026-10-18 23:57), the loading screen stalled at 99%: game 1.5.0 added its own **Acolyte** enemy (`AcolyteBody`), and Shaman's summon body has the same name, so the body catalog threw "An item with the same key has already been added" and never finished. The fix renames a clashing `bodyName` to `Shaman<name>` (so `ShamanAcolyteBody`) as the prefab is created, before any catalog sees it; Shaman never looks its Acolyte up by that string, and its doppelganger master follows the new name. (On a profile with SeekersPatcher, a Starstorm2 dependency that re-adds the removed field at preload, the re-emit is a harmless no-op and the rename is the part that matters; the rename is applied first and on its own.) Nothing else in the mod is touched. Every PC builds the survivor, so every PC needs it; the mod is network-compatibility flagged, so every player must have Shaman itself as well. |
| **Any mod (moved assets)** | The update moved hundreds of game assets to new folders. For example, `RoR2/Base/Beetle/BeetleGuardBody.prefab` is now under `RoR2/Base/BeetleGuard/`. Mods that load an old path lose that asset without warning: effects, textures, or spawn cards go missing. In this profile that's 637 paths across 8 mods, including EnemiesReturns' Golem-family cards, Interrogator, Risk of Options, StormyItems and most of Survariants. | Loads the asset from its new path, but only for `RoR2/...` paths that no longer exist and whose file name now exists at exactly one path. 290 paths the update deleted, or that now match several assets, can't be fixed. |
| **AudioOverlapFix** | It drops a sound event posted twice in one frame, Stop events included. When several copies of an enemy (Multitudes), or everything at the end of a run, stop the same looping sound at once, the extra loops keep playing until the game restarts. | Always lets Stop events through. Duplicate Play events are still dropped. |
| **ZetAspects 2.10.9** | Its kill hook (`DropHooks.EarlyDropChanceHook`) handles elites that have no equipment slot by checking their inventory, then reads the missing slot anyway (line 412). That crash comes before it passes the death on, so the game's own on-kill handling (item procs, drops) is skipped for that kill. | Uses the inventory equipment the hook just read. ZetAspects 2.11.3+ already fixes this; CompatShield keeps the self-check and logs one debug line when no change is needed. |
| **Gasoline chains (vanilla item, big packs)** | A Gasoline blast that kills an enemy runs that enemy's death handling, and therefore the next Gasoline blast, inside itself. This pack runs about 70 mod hooks on every damage and death, so each link costs about 250 stack frames, and dense Multitudes packs overflowed the stack within a few links. That caused 8 `StackOverflowException`s in one session, plus "Failed at InvokeHandler, probably malformed packet!" when one landed inside a network message. | Keeps every blast. A blast that starts while another one is still running is queued, and when the first one finishes the queue runs one blast at a time, in the same frame. Same timing as vanilla, and every victim is still there, but the stack never holds more than about two links. |
| **Starstorm 2 0.6.46 (Strange Can)** | Its death handler counts Intoxicated stacks per attacker and skips attackers that are already gone, then looks one of them up anyway (`KeyNotFoundException`). The crash aborts the game's death event for every handler after it. | Catches exactly that error type in exactly that handler, so the rest of the death event runs. Any other error still surfaces. |
| **Starstorm 2 0.6.46 (Relic of Force)** | Its damage handler reads the attacker's inventory (`attackerBody.inventory.GetItemCount`, line 79) after that inventory is already gone (`NullReferenceException`). The game calls it from inside `HealthComponent.TakeDamageProcess`, before the kill handling, so a killing blow that hits this crash gives no gold, XP or on-kill procs (the enemy still dies). It also skips every damage handler after it. 587 in 14 seconds on 2026-10-17. | Same guard: Relic of Force skips that one hit's extra hits, and the rest of the damage and kill handling still runs. |
| **DamageLog 1.2.10** | Its damage-notification handler (`DamageLog.Record`) crashes looking up an attacker portrait. That aborts the event for every handler after it (hitmarkers, damage numbers, the LookingGlass DPS meter) and fails the network message ("probably malformed packet"). | Same guard: DamageLog skips that one hit, everything else still sees it. |
| **Artifact of Genetics 4.5.3** | Its damage hook applies the damage, then crashes in its own gene tracking. The crash flies up through every hook wrapping it and the caller, so a blast stops at its first target and outer hooks never finish. TinkersSatchel's Defib "HealingSourceStack was not empty" errors are one result. 655 crashes in one session. | Wraps only the code after the damage call in `try { } catch (NullReferenceException)`, so only Genetics' own bug is caught. Errors coming out of the damage call (other mods, stack overflows) are never touched: they propagate with their own stack trace. (The Strange Can, Relic of Force and DamageLog guards work the same way, around the whole handler.) |
| **EnemiesReturns 0.7.29 (Swift)** | The Swift's death state loads the vanilla Beetle Guard impact effect and releases its Addressables handle in the load callback. When that is the run's first use of the effect, the game's own on-demand load of the same effect re-runs the callback inside itself, so the handle is released twice and the game's copy of the effect breaks until restart. Swift corpses then throw "The Object you want to instantiate is null" every physics tick until they despawn (1,118 in one session, up to 110/s), Beetle Guards lose their death-slam effect, and the run-end cleanup throws "Attempting to use an invalid operation handle", aborting Run.OnDestroy (then Stage.Update NREs every 2 s). | Drops that one `Addressables.Release`, so the handle can't be released twice and the effect keeps working. Also counts a corpse as landed before it spawns the effect, so any other breakage errors once per corpse, not every tick. Every PC needs it (each game spawns the effect itself). |
| **REX Tangling Growth flower (vanilla, with EliteReworks2 / ClassicItemsReturns)** | When the flower ends, the game unroots every enemy it caught in one loop. If one of them already died and was removed, EliteReworks2 (Overloading, Celestine) or ClassicItemsReturns (Hit List) crash on it in their buff hooks, the loop stops, and every enemy after it stays rooted until it dies. 19 in one session. | Does the unroot loop itself: skips enemies that are gone and unroots each of the others on its own, so one crash can't skip the rest. The game's exit sound and effect still play. Only takes over the loop if its IL is exactly the game-1.5.0 shape. |
| **StandaloneAncientScepter 1.1.37** | Its hook on every REX flower's pulse (`TreebotFlower2_2.On_TreebotFlower2RootPulse`, scepter or not) checks the flower's owner with a plain C# null check. Once that body is destroyed (the REX died, respawned or left the stage, or a Vengeance umbra REX was killed), it crashes on every pulse before the game's own pulse runs, so the flower stops rooting and hurting enemies. 70 crashes in one session. | Treats a destroyed owner as no owner, as the game does: the flower keeps rooting and damaging at normal radius (no scepter extras once the owner is gone). |
| **Freeze/stun/shock (vanilla)** | `SetStateOnHurt` fills its duration-modifier list only in `Start()`, but it can be hit before that runs (an enemy spawned earlier in the same laggy frame, e.g. a Multitudes copy). A freeze, stun or shock on it then crashes `GetModifiedDuration` inside the damage function, so the rest of the hit is lost: damage numbers, on-hit effects and, on a killing blow, the gold, XP and on-kill procs. | Fills the list early the same way `Start()` does; if that isn't possible, uses the unmodified duration. |
| **Artifact of Sacrifice drop rate** | The game's or an installed mod's drop chance can be scaled for multiplayer and faster spawns. | Optional `[Sacrifice] DropChanceMultiplier` scales the final chance right before the game's roll, after any other drop chance changes (0.7 = 30% fewer drops). |
| **Artifact of Sacrifice interactables** | The artifact removes chests, shops, Shrines of Chance and similar from every stage, with no way to keep one kind. | Optional `[Sacrifice] KeepInteractables` names the spawn cards that stay on (empty by default, so nothing changes), and the log lists every card Sacrifice blocks so the names are there to copy. `KeptInteractableWeight` stops a few kept cards taking over a category whose other cards are gone. Host only. |
| **ShareSuite 2.12.0 + Artifact of Sacrifice (interactable credits)** | ShareSuite replaces the interactable credits of each stage and halves them when Sacrifice is on. That made up for older game code that halved them before ShareSuite ran. Game 1.5.0 halves them later, so both apply and a Sacrifice stage spends a quarter of its credits (220 announced, 55 spent on stage 1 solo). | `[Fixes] ShareSuiteSacrificeCreditFix` (on by default) cancels the ShareSuite halving, so only the game halves them and Sacrifice stages get twice the interactables, drones included. Stages that opt out of Sacrifice scaling get their full credits. Host only. |
| **PrintToInventory 1.3.1 + ShareSuite** | PrintToInventory turns itself off when ShareSuite is installed ("ShareSuite detected! PrintToInventory will be disabled"), because both would hand out printer and cauldron items. So scrappers drop scrap on the ground. | Lets PrintToInventory load, but keeps only its scrapper part: scrap goes straight into the scrapping player's inventory (host-side; multiplayer only by its own default). Printers, cauldrons, cleansing pools and the Wandering Chef stay as they are, whatever its config says. ShareSuite never hooks scrappers, so nothing gets handed out twice. Scrap given this way isn't shared by ShareSuite. |
| **PaladinMod 2.1.0** | `Asset.GatherMaterials` crashes because Mercenary's `EvisProjectile` no longer has a `ProjectileController`, which aborts Paladin's whole setup, so the survivor never gets added. | Makes each material lookup in that method independent (one failure no longer stops the rest), then fills the missing `matMercSwipe` from the Eviscerate ghost prefab directly. PaladinMod 2.2.0+ already supplies the material; the self-check leaves it unchanged and logs at debug level. |
| **ZetAspects 2.11.3+** | Its own elite-drop hook now handles kills without an equipment slot. | `ZetAspectsDropFix` keeps its compatibility check and reports the already-correct hook at debug level. |
| **Bubbets Items 1.8.21** | Jellied Soles updated its damage hook to the game's float-based damage argument. | Leaves the hook enabled when the current IL shape fits. The old 1.8.20 rewrite and unknown-shape skip remain available. |
| **EliteReworks Overloading / ZetAspects LightningStake** | Overloading hits can emit repeated lightning stakes without a per-owner live cap or cooldown, causing projectile pile-ups. | `OverloadingStakeLimit` installs its two game patches once per session, resets live counts, cooldowns and once-per-run notices at each run start, caps live stakes per owner and spaces emissions. It recognizes the EliteReworks projectile and ZetAspects' `LightningStake`; 0 cap and cooldown disables the respective limit. Host only. |
| **Dead or runaway enemy objects** | `TeamComponent` lists also contain projectiles and other non-character objects, which could inflate the hard enemy count. | `HardEnemyCap` counts only team members with a `CharacterBody` and live master. Host only. |
| **TinkersSatchel taunt / Icarus / Headset** | A taunt target can lose its body, Icarus can receive a report without an attacker master, and Headset does item work for bodies that cannot own the item. | Skips the stale taunt tick and invalid Icarus event; Headset calls the original body update and omits its mod work when inventory is absent. Every PC. |
| **Hook projectile with missing owner or victim** | `HookProjectileImpact.Reel` can throw every tick after its owner or victim disappears. | `HookReelGuard` sends the grapple into the game's fail state when either is gone. Every PC. |
| **Starstorm 2 override and termination warnings** | Dead attacker override entries can throw in handlers; `AfterGummy` can flood the log with a termination-result lookup warning. | `Starstorm2LogQuiet` prunes dead entries and suppresses only the stack-scoped lookup warning without changing its result. Every PC. |
| **ChensGradiusMod, MysticsItems, Caeli Imperium, and MSU cleanup** | Several event handlers retry after owners disappear, enumerate a collection while damage callbacks mutate it, or destroy an already-removed display helper. | Small shape-checked guards let ChensGradiusMod's own three-second invalidity timer destroy an unusable seed tracker, snapshot the death-ceremony list, skip invalid Caeli events, and preserve MSU's original cleanup while avoiding duplicate helper destruction. Every PC. |
| **Paladin shockwave error flood (PaladinMod)** | The shockwave carries a dust effect it moves to itself every tick. When the effect ends before the shockwave does, the move throws every tick, about 50 errors a second (373 on one Sulfur Pools stage). | Once the effect is gone the shockwave stops moving it. `PaladinShockwaveFix`, applied at the first run start. Every PC. |
| **Rapid stat changes** (Aetherium Shielding Core, buff wards and anything else that keeps changing a character) | The game redoes a character's full stat calculation on any physics tick that finds its stats marked changed, about 1.25 ms each with this pack. A shield sitting at zero or a stream of ward buffs marked them changed nearly every tick: Goolake ran at 8-13 fps with few enemies, 3.5 s of every 10 s spent here. | `StatUpdateInterval` allows one automatic update per character per 0.1 s. A single change still applies at once; repeats wait at most 0.1 s and none is lost. |
| **Enemy counts running away** (summons that ignore the game's team limit) | `MonsterLimit` only reaches normal spawns. Anything flagged to ignore the team limit, plus the Void Infestors and Malachite urchins that elites leave on death, passes it. On Goolake the count went from 16 to 319 enemies in 10 game seconds, with 9349 projectiles, and the game stopped advancing. | `HardEnemyCap` counts only hostile team members with a `CharacterBody` and live master, then refuses new enemy spawns once the cap is reached. Projectiles and other non-character team objects do not inflate the count. Boss characters, scripted boss parts and the skills that can't take a refusal (Engineer turrets, Goobo clones, Nemesis events) still spawn. It only stops new spawns. Host only. |
| **Run timer freezing after Hallowed Ichor** (Sandswept) | Once anyone picks it up, the item takes over the run timer for the rest of the run and expects a text effect this HUD doesn't have. It threw an error every frame (49,400 in nine minutes) and the timer stopped. | `HallowedIchorTimerFix` runs the game's own timer when the text effect is missing. Every PC. |
| **Sacrifice totem stuck throwing errors** (game) | The totem's sacrifice moves a tether to its first pick every tick, before it checks its timer. When that pick is already gone, the line throws every tick, so the totem never sacrifices or moves on (534 errors in 11 seconds on Helminth Hatchery). | The tether now follows the next living pick, or is put away when none is left, so the attack finishes as the game intended. Part of `ErrorLoopGuards`, applied at the first run start. Every PC. |

The hook guard is generic: any mod whose `On.` hook fails *only* because a game method's signature changed, or
because the hook type it was built against no longer exists, gets that one hook skipped (logged as a warning)
instead of aborting its setup. Every other kind of error passes through untouched.

</details>

<details>
<summary>Setting notes: spawn speed and the enemy cap with Multitudes</summary>

### Spawn speed (`SpawnCreditMultiplier`)

Each stage's regular spawners earn credits over time and spend them on waves, then have to build up again. This
setting multiplies what they earn; 1.5 brings waves about a third sooner.
- Teleporter, shrine, void and special-mode spawners are left alone.
- Starstorm 2's and RiskyMod's own credit changes (RiskyMod's flat x1.4) apply first and get multiplied too. Inferno's
  per-stage credit change is skipped entirely when RiskyMod is installed.
- With Multitudes, more credits also means more copies, so keep `MonsterLimit` on.

### Enemy cap with Multitudes (`MonsterLimit`)

Multitudes No Price Scale copies every enemy spawn (`Monster Scaling`). With its `Lift Monster Limit` on, it also lets
every spawn ignore the game's 40-enemy limit, so one Xi Construct fight reached 182 enemies at once. With
`MonsterLimit` set:
- the game's own limit check uses that number for the monster team;
- Multitudes' copies, which always skip the game's check, stop once the team they'd join is full.

Boss parts and scripted spawns that the game always allows (like Xi Construct's weak points and Mithrix's spires)
still spawn; only their copies stop.

Requirements:
- Set Multitudes' `Lift Monster Limit = false`. Otherwise normal spawns ignore the limit, only the copies are capped,
  and CompatShield logs a warning.
- Inferno's `Enemy Cap` sets the same game limit, so keep it at the same number.

</details>

<details>
<summary>Multiplayer in full</summary>

The fix runs on each PC separately. For **Paladin and TinkersSatchel content**, friends need the same DLL in their
profile too. Otherwise their game still fails to add Paladin and TinkersSatchel's items while yours succeeds, and
the mismatch can cause desyncs. Until they have it, avoid picking Paladin in multiplayer.

For **Inferno's Mithrix slam**, the host alone fixes the fight: friends without the DLL still take the right damage.
They don't see the slam animation, though, and their log shows "Failed at InvokeHandler, probably malformed packet!"
once per slam, so give them the DLL too. The **Swift impact fix** also needs every PC: each game spawns the effect
itself, so a friend without it still gets the error flood and run-end crash on their side. The **Inferno hook churn fix**
needs every PC for the same reason: each game adds and removes Inferno's hooks itself, so a friend without it can still
crash when a new run starts, and so does the **respawn hook churn fix** (each game builds and destroys PlayerRespawnSystem's
components itself). The **cauldron hum fix** is per PC too (each game plays the loop itself), and so is the **Shaman fix**
(each game builds the survivor; Shaman is network-compatibility flagged, so every player must also have the mod). The REX
flower, Ancient Scepter flower, freeze/stun, Gasoline, crash-guard, blast chain cap, null dccs pool entry, Inferno cooldown, Sacrifice and ShareSuite Sacrifice credit fixes only need the host.
The **stat update limit** is per PC: each game updates its own copies of the characters, so only PCs that have it run faster, and players with and without it can mix.
The **hard enemy cap** only needs the host, because enemies are spawned there.
The **Hallowed Ichor timer fix** is per PC, because each game runs its own timer.
The **totem tether guard** is per PC, because each game moves its own tether.
The **Paladin shockwave** and **Sandswept fire trail** fixes are per PC, because each game draws its own shockwaves and runs its own trails.
The **Sharp Anchor** guard is host only, because only the host runs the anchor zones' buffs.
The **physics pile-up tracer** is host only: it only logs, and the host's log is the one read.
The **big-hit tracer** is host only: the host's game deals all damage, so only the host's log has the lines.
The **hook timer** is each PC for itself and off by default: it measures the PC it runs on and changes nothing in the game, so one player can switch it on alone. The host's numbers are the fullest, because damage, deaths and enemy stats run on the host.
**Locked items** are host only: the host's game decides which lobby choices can be picked and sends that to everyone. A friend sees the lock either way; the red `[CONFIG DISABLED]` title shows only on a PC whose `LockedItems` list is the same.

**Difficulty scaling** must match on every PC: each game computes monster level itself, so friends with a different
Inferno `Difficulty Scaling` or without `InfernoLevelBoostFix` see enemies at the wrong level on their health bars
(the host's numbers are the real ones). Give them the same DLL and the same `HIFU.Inferno.cfg` `Difficulty Scaling`
and `Level and Difficulty Boost` (1.25 by default) values.

</details>

<details>
<summary>Known limits in full</summary>

- `HardEnemyCap`: a mod that assumes its limit-ignoring summon always worked may log a NullReference once 80 enemies are alive. The game's own limit refuses spawns the same way, so most code already handles it. A summon that asks again from inside its own failure callback gets through on the fourth ask, because only that ends the recursion, so it can pass the cap by a few.

- **Respawn hook churn fix:** PlayerRespawnSystem's hooks used to be re-added at every run start, so they sat outermost
  around any other mod's per-run hooks on the same methods; kept, they now sit inside hooks added later. No mod on this
  profile hooks the same methods per run (`Stage.GetPlayerSpawnTransform` would be the one that matters), so nothing
  changes today; if one is added and respawn positions look wrong, switch `HookChurnFixes` off (that also turns off Inferno's half).
- **Archaic Wisp 1.1.9:** setting its `Stage List = none` (and `Remove Greater Wisps = none`) takes Archaic Wisps off
  the stage spawn lists and brings Greater Wisps back. They can still come in through Artifact of Dissonance, Wisp
  family events and Artifact of Universe, where they float around and never attack (their charge, fire and death
  states are unregistered in game 1.5.0), and log a FireCannons "The Object you want to instantiate is null" error.

</details>

<details>
<summary>Log lines (source CompatShield), for bug reports</summary>

- `<Fix> not applied: <error type>: <message>`: one fix failed to set up at load; every other fix still applies.
- A warning ending `(logged once)`: a per-hit or per-tick check failed and left the game's own code to run; repeats are not logged.
- `ChensGradiusMod: a seed tracker whose body has no master warns once, then waits quietly for the mod's own 3-second timeout` at load.
- `Big-hit tracer: during a run, logs who and what dealt any hit that takes 25% or more of a player's full health, or the last of it, and every player death (up to 5 lines per source, 60 per run, then a summary)` at load; `Big-hit tracer: can't watch runs, not applied: ...` if the game's run events are missing; `Big-hit tracer: couldn't start for this run: ...` if the damage events can't be found at a run start.
- `Big hit: Mentalize (MercBody) lost 82% of full health in one hit (1230 of 1500; had 1400) in golemplains (stage 3 t=12:01). From: Umbra of Mercenary (MercBody, team Monster, an Umbra). Carried by: NovaThing. Type: AOE; no crit; proc coefficient 1; procs none; raw damage 1800 = 6x the attacker's damage stat; hit point 9 m from the victim. Path: RoR2.BlastAttack.Fire < SomeMod.Nova.Detonate`. `Path:` drops wrapper frames before taking its 12-frame limit, preserving the nearest real source frame. The third hit onward from one source says `path left out`.
- `Big hits this run: 9 from 2 source(s), most first: MercBody via NovaThing (AOE) x8; ...` at run end. `Big-hit tracer: 60 lines this run; further hits are only counted for the summary at run end` at the cap. `Big-hit tracer: couldn't read a hit (logged once): ...`, `Big-hit tracer: stopped after 20 hits it couldn't read` and `Big-hit tracer: the game's damage report changed shape, not applied: ...` if reading fails.
- `Locked items: 28 name(s) will be locked off in the lobby as soon as the game's rule list is ready` at load, then `Locked items: 25 of 28 locked off in the lobby by name (the On choice can't be picked, shown as [CONFIG DISABLED]): ArmedBackpack, ...`. `Locked items: no item or equipment is named X, Y ...` for names with no lobby rule (mod removed, wrong name, or an item the lobby never lists). `Locked items: ... not applied` if the game's rule list changed shape.
- Only with `HookTimer = true`: `Hook timer: from the first run start, times each mod's hooks, patches and event handlers on 39 busy game methods and logs the slowest mods and hooks every 5 minutes, at each stage change and at run end. Log only; expect a pause of about 10 seconds at the first run start and a shorter one at the next stage` at load. `Hook timer: can't watch hooks being added, not applied: ...` or `Hook timer: can't watch runs, not applied: ...` if MonoMod or the game's run events are missing; `Hook timer: couldn't start for this run: ...` if a run start fails.
- At the first run start and after each stage change that added something: `Hook timer: timing 412 methods after the run start (58 game methods, 290 mod hooks, 40 Harmony patches, 24 event handlers); 412 added in 3200 ms`, with notes when some can't be timed, refused the timer, the pass ran out of time or the limit of 1000 was reached (game methods are never left out). Then once: `Hook timer: each timed call costs about 150 ns; that is taken out of the numbers in the reports`.
- Reports (`stage <name>` at each stage change and every 5 minutes as `part N`, `whole run` at run end): `Hook timer: whole run, 1800 s: 54000 ms inside the watched game methods and events (30.0 ms each second, 3.0% of the time) over 9000000 timed calls; the timer itself added about 1300 ms on top, already taken out. Each line is that code's own time, calls it makes into other timed code left out`, then `Hook timer:   mod 1. SomeMod: 9000 ms (5.00 ms each second, 17% of the total), 800000 calls, slowest call 4.10 ms` (the game's own code is the line `(game code and IL hooks)`), then `Hook timer:   hook 1. SomeMod:Hooks.Recalc (hook on CharacterBody.RecalculateStats): 6000 ms, 0.0150 ms a call, 400000 calls, slowest 4.10 ms`, and in the run report `Hook timer:   method 1. CharacterBody.RecalculateStats: 20000 ms in 400000 calls (0.0500 ms a call), game code 8000 ms, mod hooks and patches 12000 ms; IL hooks (counted as game code) by ModA, ModB`. `Hook timer: <title>: nothing was timed` if no timed call ran. The IL hook names include mods with a Harmony transpiler on the method. Since 1.4.53: the header adds `; 570 first calls (2400 ms, mostly compiling the method once with the timer in it) are left out of every number` when a report holds first calls; a hook or patch method that a mod put on several game methods reads `(patch on CharacterBody.OnInventoryChanged and 6 more)` (two are both named), and since its one count can't be split it is left out of the `method N.` lines and summed at the end of the run report: `Hook timer:   not in the method lines: 1117 ms in 2 hook and patch methods that each sit on more than one game method, so the time can't be split between them (CompatShield:DirtyPatches.DirtyPrefix 800 ms, ...)`.
- Rare, and it means restart: `Hook timer: adding the timer to <method> failed, and that left the method without the changes other mods make to it. Restart the game before playing on, and set HookTimer = false if this repeats. The timer is off for this session (...)`. Adding a timer makes the game rebuild that method with every mod's code edits; if one mod's edit fails on the rebuild, the method runs as plain game code until the restart.
- `Physics pile-up tracer: during a run, after a frame over 500 ms (at most every 10 s), logs which moving objects hold the most colliders and how many of them share one 4 m spot` at load; `Physics pile-up tracer: can't watch runs, not applied: ...` if the game's run events are missing; `Physics pile-up tracer: couldn't start for this run: ...` if the ticker can't be made at a run start.
- `Physics pile-up: 600 ms frame in voidraid (stage 12 t=50:01); 41 moving colliders of 3 kinds, 24 in the most crowded 4 m spot, 50 static; most: AmmoPack: 30 colliders on 15 objects, 15 triggers, 24 in one spot; ... (census 2 ms)` after a long frame. Kinds over 500 colliders also list up to three owner body prefab names and counts. Up to six kinds, most colliders first. `Physics pile-up tracer: couldn't take a census (logged once): ...` if counting fails.
- `StormyItems: a Sharp Anchor zone that has lost its holder now skips its tick instead of throwing every tick` at the first run start; `StormyItems: a Sharp Anchor zone had lost track of its holder, so its tick was skipped instead of throwing (first time this launch)` the first time it steps in; `Sharp Anchor fix: ... not applied` if the mod's code is not the expected shape.
- `PaladinMod: a shockwave whose dust effect is gone now stops moving it instead of throwing every tick` at the first run start; `PaladinMod: a shockwave's dust effect was gone, so the shockwave stopped moving it (first time this launch)` the first time it steps in; `Paladin shockwave fix: ... not applied` if the mod's code is not the expected shape.
- `Sandswept: a fire trail whose last flame burned out no longer throws when the game resizes it` at the first run start; `Sandswept: a fire trail's last flame burned out, so its size was left as it was (first time this launch)` the first time it steps in; `Sandswept fire trail fix: ... not applied` if the mod's code is not the expected shape.
- `Game: a sacrifice totem whose first pick is gone now moves its tether on instead of throwing every tick` at the first run start; `Totem tether guard: ... its tether now follows the next one` or `... its tether was put away` the first time each happens; `Totem tether guard: ... not applied` if the game's code is not the expected shape.
- `Sandswept: Hallowed Ichor no longer stops the run timer on a HUD without its text effect` at the first run start; `Hallowed Ichor: this HUD has no text effect to distort, so the run timer keeps counting normally` the first time it matters; `Hallowed Ichor timer fix: ... not applied` if the mod's code is not the expected shape.
- `Config: BepInEx's save didn't update the file, so CompatShield wrote it itself (patches on the save: ...)`: when BepInEx leaves the settings file out of date. `Config: couldn't tidy the settings file...` if that fails too. Values apply either way.
- `Enemy cap: at most 80 enemies alive at once, summons that ignore the team limit included; boss characters and scripted boss parts still spawn` at the first run start; `Enemy cap: N enemies alive in <stage> (cap 80). Most: <body> n, ...` when the count reaches the cap and each time it doubles; `Enemy cap: last stage stopped N spawn(s), most alive at once P (cap 80); stopped: ...; asked for by: ...; let through: ...` when a stage ends, only if the cap mattered (`asked for by` samples one ordinary spawn in 8, so its counts run lower than `stopped`); `...; nothing was stopped, so these came by a route the cap doesn't cover` if the count passed the cap with nothing refused or exempted; `Enemy cap: ... not applied: ...` if the game's code is not the expected shape.
- `Stat limit: one character's automatic stat update now runs at most once every 0.1 s; a single change still applies at once` at the first run start; `Stat limit: last stage ran N automatic stat updates; physics ticks that waited: W, most waiting: <body> N, ...` when a stage or run ends; `Stat limit: ... not applied: ...` if the game's code is not the expected shape.
- `Blast chain cap: stops blasts deeper than N; time is considered only after 32 nested blasts, with a 2000 ms hard ceiling` at the first run start; `Blast chain: stopped N nested blasts (X too deep, Y too late); Z ran, the chain took M ms, and the first one stopped came from <body>` after each chain that lost blasts (at most 200 lines).
- `Blast guard: one target's error no longer ends a blast ...` at the first run start; `Blast guard: a hit failed and the blast carried on (NullReferenceException: ...). Victim <body>: body ok, inventory null, master ok, shield 4/10, barrier 0, health 50/100. Attacker <body>: body ok, inventory ok, input bank ok. Inflictor <object>. Failed hit N this session. Thrown at: ...` once per kind of failure (at most 30 kinds, and a new throw site still gets a line after that); `Blast guard: N failed hits caught so far` at 10, 100, 1000 and so on.
- `Stats guard: a character whose stat update keeps failing now waits 0.25 to 8 seconds between tries ...` at the first run start; `Stats guard: a stat update failed on <body> (NullReferenceException: ...). Failure N in a row for this body, M this session. Parts: inventory ok, master null, ... Stock skills: primary ok, ... Level 12, player controlled True. Items: ... Buffs: ... Thrown at: ...` once per kind of failure; `Stats guard: N failed stat updates so far, B bodies failing now, E of the failures came early because something marked the stats dirty during the wait` at 10, 100, 1000 and so on.
- `Overlap guard: one target's error no longer ends a melee hit or a damage zone tick ...` at the first run start; `Overlap guard: a hit failed and the attack carried on (NullReferenceException: ...). Victim <body>: body ok, inventory null, master ok, shield 4/10, barrier 0, health 50/100. Attacker <body>: body ok, inventory ok, input bank ok. Inflictor <object>. Failed hit N this session. Thrown at: ...` once per kind of failure (at most 30 kinds, and a new throw site still gets a line after that); `Overlap guard: N failed hits caught so far` at 10, 100, 1000 and so on.
- `DoT guard: one attacker's error no longer ends a damage-over-time tick ...` at the first run start; `DoT guard: a hit failed and the tick carried on (NullReferenceException: ...). Victim ... Attacker ... Inflictor ... DoT Bleed. Failed hit N this session. Thrown at: ...` once per kind of failure; `DoT guard: N failed hits caught so far` at 10, 100, 1000 and so on. A DoT added by a mod shows its number.
- `Bullet guard: one target's error no longer ends a bullet ...` at the first run start; `Bullet guard: a hit failed and the bullet carried on (...)` once per kind of failure, in the same format; `Bullet guard: N failed hits caught so far` at 10, 100, 1000 and so on.
- `Roulette: paid spins now come out of ShareSuite's shared gold` at the first run start (only with both mods loaded); `Roulette: $X charged to the shared gold pool, $Y left` on each paid spin.
- `Game: Inventory.RemoveItem(ItemDef, int) added back in memory for <mod>` if a plugin calls it, or `Game: Inventory.RemoveItem(ItemDef, int) shim not needed (no plugin calls it)` if the profile has no caller. `BubbetsItems: Jellied Soles hook already fits this game, left alone` identifies the safe 1.8.21 hook; unknown shapes still log a warning and skip that hook.
- `EliteReworks Overloading stakes: per-owner live cap N and cooldown X s ...` once when its projectile guard is installed; the two game patches stay attached for the session, while live counts, cooldowns and once-per-run notices reset at each run start. The first blocked stake logs the current live count and cap. The guard also covers ZetAspects' vanilla `LightningStake` prefab.
- `Quit crash guard: skips Wwise callback drain after native sound-engine termination; normal drains continue` when installed.
- `Game: a hook with a missing owner or victim now fails its reel cleanly` at run start; if either is gone, the game's reel fail state runs. TinkersSatchel logs the taunt, Icarus and Headset guard installs; ChensGradiusMod logs that its three-second invalidity cleanup is preserved, and MysticsItems, Caeli Imperium and MSU each log their corresponding guard install. Each shape mismatch produces a `... not applied` warning.
- `Starstorm2: dead attacker overrides are pruned ...` when installed and, only when needed, `Starstorm2: pruned dead attacker overrides before hit processing`. The scoped `AfterGummy` termination-result warning is muted.
- `LinearDamage: Focus Crystal's vanilla bonus will be removed` while the mod loads, then `LinearDamage: removed vanilla Focus Crystal past other mods' code` once the damage hook is built.
- `ExtraSkillSlots: the four extra icons will be built to fit a replacement HUD` while the mod loads, then `ExtraSkillSlots: built the extra icons for this HUD (...)` at the first HUD. The brackets name the parent, layout group, icon size and key label, which is what to read if the icons land in the wrong place.
- `Smite Items: Dagger of Frenzy no longer breaks skills on bodies with no skill locator or primary skill` at the first run start.
- `Sandswept: a Theta Construct's failed shield cast now retries after 1 s instead of every tick` at the first run start, then `Theta shield loop fix: stopped a failed shield cast from repeating (first time this launch)` the first time it happens.
- `Acolyte: base attack speed X -> Y, transformed form X -> Y (1.5x)` at the first run start, or `Acolyte: ... not applied` if the game has changed. `Game's enemy limit set to 30 (it was 60)` once per launch, the first time the limit changes.
- `Null dccs fix: interactable pool entries with no dccs are removed before anything walks the pool` at the first run start; `Null dccs fix: removed 1 pool entry from <pool> (DccsPool) on <stage>, found at Awake: category "<name>" [0] includedIfConditionsMet: 1 (empty reference, weight 1, serialized True, expansions DLC2)` once for each pool it cleans. `found at` is `Awake`, or `SwapCSIValuesWithSelf` or `SetInteractableDCCSP` for a variant pool. `serialized False` means a mod built that entry in code. `serialized True` means it came from saved data or a clone of it.
- `Damage chain: hits nested more than 3 deep ... now run one at a time after the outer hit` at startup; `Damage chain: a hit nested 4 deep was queued ... (shown once)` when it acted; `Damage chain: a backlog of up to N waiting hits was spread over M physics ticks (K dropped)` after each storm; `Hypercrit2: a crit roll that throws on a hit now counts as no crit ...` at its load; `Hypercrit2: its crit roll threw on a hit; counted as no crit ... (shown once)`.
- `Lag: <scene> (stage N t=m:ss) | 38 fps avg, worst frame 212 ms, 3 frame(s) over 100 ms | monsters 58, bodies 73, projectiles 140 | RecalculateStats 1240x 310 ms, TakeDamage 2100x 95 ms, OnHitEnemy 2100x 180 ms | GC 4 collection(s), heap 1200 MB` (Info when laggy, Debug otherwise), `Lag: 312 ms frame in <scene> (...)`, and `Lag summary: ...` at run end.
- Since 1.4.54 each `Lag:` window line ends with `| RAM: 2.1 of 39.9 GB free, game 18.8 GB with 13.0 GB of it in RAM | page faults: game 51234 (4000 in the worst frame), 812 from disk; whole PC read 123456 pages from disk`, the long-frame line ends with `; game page faults in this frame 4000; RAM 2.9 of 39.9 GB free`, and the run ends with `Lag summary: memory: free RAM fell to 2.1 of 39.9 GB; game page faults N in the measured windows, M of them read from disk (most in one window: K at <scene> (...)); whole PC read P pages from disk`. How to read it:
  - `RAM ... free` under about 4 GB on a 40 GB PC means Windows is short of memory and is moving other programs' memory to disk.
  - `from disk` is the number that matters: the game stopped that many times in the window to wait for the disk. A hitch with hundreds or thousands of them is the PC, not a mod. It also counts game files read for the first time, so expect a burst right after a stage loads. The count is refreshed every 2 s by a background thread, so it can trail a hitch by one window; `n/a` means no fresh value.
  - The plain `page faults` count mixes cheap faults (the memory was still in RAM) with from-disk ones, so a big number alone proves little. `in the worst frame` and `in this frame` are that plain count for one frame.
  - `whole PC read ... pages from disk` covers every program, not only the game.
  - `Lag tracer: free RAM and page-fault readings are unavailable (...)` or `Lag tracer: the count of the game's page faults read from disk is unavailable (...)` appears once if Windows refuses a reading; the rest of the lag lines carry on without it.

- `Outdated-hook guard installed`
- `Enabled fixes: ErrorLoopGuards=..., StuckSoundFixes=..., HookChurnFixes=..., TinkersSatchelFixes=..., RexFlowerFixes=..., ...`: every setting and its value, at startup.
- `Config: moved 58 settings into the new categories and kept their values (old file saved as local.compatshield.cfg.before-categories)`: once, on the first launch after updating from a version before 1.4.43.
- `Assets: moved game asset paths now load from their new location`, then `Assets: indexed N catalog keys...` on the
  first game-asset load, and up to 25 `Assets: '<old path>' moved to '<new path>'`
- `ZetAspects: elite kills without an equipment slot no longer crash its drop hook`, or the warning
  `ZetAspects drop fix: unexpected IL (maybe a newer ZetAspects), left unchanged`
- `Gasoline: chain reactions now run one blast at a time instead of nesting`, then the first time it matters
  `Gasoline: a chain reaction was run one blast at a time (shown once)`; a blast that throws is logged in full
  (first 3 of each distinct error)
- `After-orig guard: GeneticsArtifact.GeneEngineDriver.HealthComponent_TakeDamage can no longer break the damage chain after it ran`,
  or the warning `After-orig guard: <reason> in ..., guard inactive` if the IL changed. Errors from deeper in the damage
  chain are never caught, so the game's own log shows their real source.
- `Inferno: an error from Mithrix's slam below Inferno's hook passed through Hooks.WeaponSlam; ...` with the real stack
  trace (first 3 per throw site): the Inferno slam fix needs a finalizer, whose rethrow restarts the trace
- `Handler guard: SS2.Items.StrangeCan.OnCharacterDeathGlobal can no longer abort the event it handles`, then
  `Handler guard: Strange Can skipped a kill whose Intoxicated attacker is gone (shown once)`
- `PrintToInventory: scrappers now put scrap straight into the player's inventory; printers, cauldrons, cleansing pools and the Wandering Chef stay as ShareSuite handles them`
- `Inferno: Hooks.WeaponSlam now finds the pillar ghost at its new address, so Mithrix's hammer slam happens again` (and the
  same for `Hooks.CleanupPillar`), `Inferno: run-end cleanup now removes its Mithrix dash (SlideIntroState) hook instead of
  stacking another copy`, `Inferno: run-end cleanup now keeps at most one copy of its 3 reset hooks ...`; on the first slam
  `Inferno: Mithrix's pillar ghost loaded from its new address (shown once)`
- `Inferno: Level and Difficulty Boost will also find a level cap another mod put in (RiskyTweaks' Raise Level Cap), so
  it reaches monster level again`, then at the first Inferno run `Inferno: Level and Difficulty Boost applied in front of
  another mod's level cap (shown once)` (and no more `Failed to fully apply Level and Difficulty boost`);
  `Inferno: Enemy Cooldown Reduction Scaling now shortens enemy cooldowns each stage (it lengthened them)`
- `Inferno: its enemy skill hooks are now kept between Inferno runs instead of removed at every run end and re-added at
  the next run start (that re-add crashed the game); they come out only when a run on another difficulty starts`, then at
  the second Inferno run of a session `Inferno: kept its enemy skill hooks from the earlier Inferno run instead of
  re-adding them (shown once)`, and at a run on another difficulty `Inferno: a run on another difficulty started, so its
  enemy skill hooks from the earlier Inferno run were removed`
- `PlayerRespawnSystem: its respawn hooks (35 in 2.2.0) are now kept between runs instead of removed at every run end and
  re-added at the next run start (the kind of churn that crashed the game); they come out only when a run in a game mode the
  mod ignores starts`, then at the first run end `PlayerRespawnSystem: kept its respawn hooks at run end instead of removing
  them (shown once)`, at the second run `PlayerRespawnSystem: reused its respawn hooks from the earlier run instead of
  re-adding them (shown once)`, and at a Simulacrum run `PlayerRespawnSystem: a run in a game mode it ignores started, so its
  respawn hooks from the earlier run were removed`; or a `Respawn hook churn fix: ... (mod updated?), not applied` warning if
  the mod's code changed shape; plus `PlayerRespawnSystem: if the host's own client loses track of the death-timer object, it is
  registered again before the next timer update` and, when it had to, `PlayerRespawnSystem: the host's own client had lost its
  death-timer object (netId N); registered it again ... (shown once)`
- `Lunar Cauldron / Cleansing Pool: the idle hum WolfoFixes restores now stops when its pool goes away, instead of following
  you into the lobby`, then at the first stage end with a pool `Lunar Cauldron / Cleansing Pool: stopped its idle hum as the
  pool went away (shown once)`
- `Orphan loop guard: a looping sound whose object is destroyed before it was stopped (a flamethrower mid-attack when the run
  ends) now stops with its object instead of playing on into the lobby`, then `Orphan loop guard: stopped <event>, which
  '<object>' left playing as it was destroyed (shown once per sound)` each time it has to act
- `Sound tracer: watching 8 PostEvent overloads; at each run end it names loop sounds left on objects that are gone`, then at
  each run end `Sound tracer: run ended; N loop-type sound(s) were started on objects that are now gone and never got a Stop
  on that object:` followed by one line per sound (`<event> on '<object>' (last started X s before the run ended, Nx)`) and
  `last sounds around the run end: ...`; at each run start `Sound tracer: run start, N started sound(s) from before this run
  still on live objects`; and `Sound tracer: AkSoundEngine.StopAll() was called (shown 3x), from: ...`
- `Shaman: its character-model setup now sets invisibilityCount through game 1.5.0's property, so the Shaman and its Acolytes
  load again` and `Shaman: a summon body named like one of the game's own (AcolyteBody, new in 1.5.0) will be registered as
  Shaman<name> instead ...`, then during its startup `Shaman: registered its 'AcolyteBody' as 'ShamanAcolyteBody' (the game
  has its own AcolyteBody)`; or a `Shaman model fix: ... not applied` warning if the mod or the game changed shape
- `Swift: its impact-effect load keeps its handle, so the Beetle Guard impact effect can't lose its prefab mid-session` and
  `Swift: a corpse counts as landed before its impact effect spawns, so a broken effect can't throw every tick`, or a
  `Swift impact fix: ...` warning if EnemiesReturns changed
- `ShareSuite: its extra Sacrifice halving of stage interactable credits is off, so only the game halves them` at startup, then
  `ShareSuite: cancelled its extra Sacrifice credit halving on this stage (logged once per launch)` on the first Sacrifice stage, or a
  `ShareSuite Sacrifice credit fix: ... not applied` warning if ShareSuite changed
- `REX flower: an expiring flower now unroots every enemy it caught, even when one of them is already gone`, then
  `REX flower: skipped unrooting an enemy that was already gone (shown once)`
- `Ancient Scepter: REX flowers keep working after their owner's body is gone`, then
  `Ancient Scepter: a REX flower outlived its owner's body and keeps working (shown once)`
- `Freeze/stun: hits on a body that has only just spawned no longer crash (and lose the kill)`, then
  `Freeze/stun: set up a just-spawned body's duration modifiers early so the hit could finish (shown once)`
- `Sacrifice: item drop chance per kill now x0.7 (after other installed changes)`, then
  `Sacrifice: a kill rolled 5.6% instead of 8% (shown once)`
- `Sacrifice: these interactables stay on: iscShrineChance, ... (categories that also lose cards at x0.5 weight)`, then on stages:
  `Sacrifice: kept (first seen this stage): Chests/iscEquipmentBarrel, ...`,
  `Sacrifice: still blocked (first seen this stage; add a name to KeepInteractables to keep it): Chests/iscChest1, ...` and
  `Sacrifice: category Chests keeps some cards and loses others, weight W now X, about N% of this stage's picks`
- `AudioOverlapFix: stop events always go through now, so looping sounds can end`, then
  `AudioOverlapFix: first stop event let through (shown once)`
- `Spawns: the stage's regular spawners earn Nx credits`, then on the first stage: `Spawns: Director credits Ax -> Bx`
- `Multitudes: enemies capped at N alive at once (its copies included)`, then `Game's enemy limit set to N (it was X)` once per launch
  and, the first time copies are held back, `Multitudes: copies paused at N enemies (shown once)`
- `Skipped an outdated hook so the mod can keep loading: <mod> -> <game method> (...)`
- `Skipped a hook whose hook type no longer exists after the game update: <mod> -> <game method> (...)`
- `TinkersSatchel: each item now sets up on its own (N setup calls guarded)`, then per broken item:
  `TinkersSatchel: <Item>.<Step> failed and was skipped (...)`
- `TinkersSatchel: auto-config update now uses the new language API`
- `TinkersSatchel: item names/descriptions now refresh on the game's new language API`, then per item whose text
  fails: `TinkersSatchel: <Item> text refresh failed (...)`
- `Ignored removing a hook that was never added: <mod> -> <game method>`
- `PaladinMod: material setup made crash-proof`, then for each lookup that still fails:
  `Asset.GatherMaterials: <field> failed and was skipped (...)`
- `PaladinMod: matMercSwipe taken from the Eviscerate ghost prefab instead`

</details>
