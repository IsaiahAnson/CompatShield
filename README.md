# CompatShield

A small local fix for Risk of Rain 2 mods that broke on the October 2026 game update (Hallowed Concepts).
It doesn't edit any mod files. It's one BepInEx patcher DLL, so deleting it undoes everything.

## What it fixes

| Mod | What broke | What CompatShield does |
|---|---|---|
| **TinkersSatchel 6.0.1** | Its hook on `Language.SetCurrentLanguage` was rejected (the game added a parameter), which aborted its startup, so almost none of its content loaded. That hook is also what installs its item text, so names showed as raw tokens like `TINKERSSATCHEL_MUG_NAME_RENDERED`. Past that, **Timelost Rum** references a hook type the update removed (`orig_FireProjectile`), and its crash stopped every item after it. | Skips that outdated hook so the rest of the mod loads, then redoes the hook's work (installing each item's name, pickup and description text) after the game's new two-argument `SetCurrentLanguage`. Also skips hooks whose hook type was removed, and sets up and installs each item on its own, so one broken item (Timelost Rum) is logged and skipped instead of stopping the items after it. Once fully loaded, its auto-config update (which still calls the removed one-argument `Language.SetCurrentLanguage`) would throw every frame, so CompatShield runs that small update itself using the new two-argument version. |
| **Inferno 1.7.8 (Mithrix slam, dash shards)** | (1) Its `Hooks.WeaponSlam` resizes the hammer-slam pillar's ghost through `ProjectileController.ghostPrefab`, which game 1.5.0 leaves empty (the ghost now comes from `ghostPrefabAddress`). It crashes before calling the game's slam, so Mithrix just stands there for 3.5 s (39 times in one session); `CleanupPillar` crashes the same way. (2) Its run-end `UndoHooks` adds its `SlideIntroState` hook (`+=`) instead of removing it, so every run stacks another copy: 11 copies in one evening, about 66 lunar shards per Mithrix dash instead of 6. (3) `UndoHooks` also re-adds three reset hooks at every run end, and after a non-Inferno run a leftover copy shrinks Mithrix's pillar back to 1x in the next Inferno run. | (1) Takes the ghost from its new address (only the ghost resize is skipped if there is none), and if the hook still crashes before the slam, starts the slam anyway. (2) Turns that one add into a remove. (3) Makes each reset-hook add "remove, then add", so at most one copy is attached. |
| **Inferno 1.7.8 (difficulty scaling)** | (1) Its "Level and Difficulty Boost" (+1.25 monster levels) is an IL hook that inserts the boost just before the game reads `Run.ambientLevelCap`. RiskyTweaks' "Raise Level Cap" has already replaced that read with its own `RaiseLevelCap.levelCap`, so Inferno logs "Failed to fully apply Level and Difficulty boost" at every run start and monsters never get the boost. (2) Its "Enemy Cooldown Reduction Scaling" is added to R2API's `cooldownMultAdd`, which makes enemy cooldowns *longer* every stage (+1% per stage), the opposite of what it says. | (1) Widens Inferno's match so it also accepts another mod's level-cap read, so the boost lands where Inferno meant it and RiskyTweaks' 9999 cap stays (don't turn Raise Level Cap off: Starstorm 2 then resets the cap to 99). (2) Sends the same amount to cooldown *reduction* instead: x0.95 enemy cooldowns at 5 stages instead of x1.05. |
| **Inferno 1.7.8** | Its Mithrix `FireWave` hook is rejected the same way; at the end of each run it then crashes removing the hook it never added ("Stack empty"), half-breaking the end-of-run cleanup. | Same hook skip, plus ignores the "remove a hook that was never added" crash. The Inferno-difficulty Mithrix tweak that hook provided stays off. |
| **Any mod (moved assets)** | The update moved hundreds of game assets to new folders. For example, `RoR2/Base/Beetle/BeetleGuardBody.prefab` is now under `RoR2/Base/BeetleGuard/`. Mods that load an old path lose that asset without warning: effects, textures, or spawn cards go missing. In this profile that's 637 paths across 8 mods, including EnemiesReturns' Golem-family cards, Interrogator, Risk of Options, StormyItems and most of Survariants. | Loads the asset from its new path, but only for `RoR2/...` paths that no longer exist and whose file name now exists at exactly one path. 290 paths the update deleted, or that now match several assets, can't be fixed. |
| **AudioOverlapFix** | It drops a sound event posted twice in one frame, Stop events included. When several copies of an enemy (Multitudes), or everything at the end of a run, stop the same looping sound at once, the extra loops keep playing until the game restarts. | Always lets Stop events through. Duplicate Play events are still dropped. |
| **ZetAspects 2.10.9** | Its kill hook (`DropHooks.EarlyDropChanceHook`) handles elites that have no equipment slot by checking their inventory, then reads the missing slot anyway (line 412). That crash comes before it passes the death on, so the game's own on-kill handling (item procs, drops) is skipped for that kill. Your logs had 343 of these. | Uses the inventory equipment the hook just read. ZetAspects 2.10.10 (2026-10-17) fixed it the same way; CompatShield recognizes that and logs "already fixes" instead. Any other change makes it log a warning and leave the code alone. |
| **Gasoline chains (vanilla item, big packs)** | A Gasoline blast that kills an enemy runs that enemy's death handling, and therefore the next Gasoline blast, inside itself. This pack runs about 70 mod hooks on every damage and death, so each link costs about 250 stack frames, and dense Multitudes packs overflowed the stack within a few links. That caused 8 `StackOverflowException`s in one session, plus "Failed at InvokeHandler, probably malformed packet!" when one landed inside a network message. | Keeps every blast. A blast that starts while another one is still running is queued, and when the first one finishes the queue runs one blast at a time, in the same frame. Same timing as vanilla, and every victim is still there, but the stack never holds more than about two links. |
| **Starstorm 2 0.6.46 (Strange Can)** | Its death handler counts Intoxicated stacks per attacker and skips attackers that are already gone, then looks one of them up anyway (`KeyNotFoundException`). The crash aborts the game's death event for every handler after it. | Catches exactly that error type in exactly that handler, so the rest of the death event runs. Any other error still surfaces. |
| **Starstorm 2 0.6.46 (Relic of Force)** | Its damage handler reads the attacker's inventory (`attackerBody.inventory.GetItemCount`, line 79) after that inventory is already gone (`NullReferenceException`). The game calls it from inside `HealthComponent.TakeDamageProcess`, before the kill handling, so a killing blow that hits this crash gives no gold, XP or on-kill procs (the enemy still dies). It also skips every damage handler after it. 587 in 14 seconds on 2026-10-17. | Same guard: Relic of Force skips that one hit's extra hits, and the rest of the damage and kill handling still runs. |
| **DamageLog 1.2.10** | Its damage-notification handler (`DamageLog.Record`) crashes looking up an attacker portrait. That aborts the event for every handler after it (hitmarkers, damage numbers, the LookingGlass DPS meter) and fails the network message ("probably malformed packet"). | Same guard: DamageLog skips that one hit, everything else still sees it. |
| **Artifact of Genetics 4.5.3** | Its damage hook applies the damage, then crashes in its own gene tracking. The crash flies up through every hook wrapping it and the caller, so a blast stops at its first target and outer hooks never finish. TinkersSatchel's Defib "HealingSourceStack was not empty" errors are one result. 655 crashes in one session. | Wraps only the code after the damage call in `try { } catch (NullReferenceException)`, so only Genetics' own bug is caught. Errors coming out of the damage call (other mods, stack overflows) are never touched: they propagate with their own stack trace. (The Strange Can, Relic of Force and DamageLog guards work the same way, around the whole handler.) |
| **EnemiesReturns 0.7.29 (Swift)** | The Swift's death state loads the vanilla Beetle Guard impact effect and releases its Addressables handle in the load callback. When that is the run's first use of the effect, the game's own on-demand load of the same effect re-runs the callback inside itself, so the handle is released twice and the game's copy of the effect breaks until restart. Swift corpses then throw "The Object you want to instantiate is null" every physics tick until they despawn (1,118 in one session, up to 110/s), Beetle Guards lose their death-slam effect, and the run-end cleanup throws "Attempting to use an invalid operation handle", aborting Run.OnDestroy (then Stage.Update NREs every 2 s). | Drops that one `Addressables.Release`, so the handle can't be released twice and the effect keeps working. Also counts a corpse as landed before it spawns the effect, so any other breakage errors once per corpse, not every tick. Every PC needs it (each game spawns the effect itself). |
| **REX Tangling Growth flower (vanilla, with EliteReworks2 / ClassicItemsReturns)** | When the flower ends, the game unroots every enemy it caught in one loop. If one of them already died and was removed, EliteReworks2 (Overloading, Celestine) or ClassicItemsReturns (Hit List) crash on it in their buff hooks, the loop stops, and every enemy after it stays rooted until it dies. 19 in one session. | Does the unroot loop itself: skips enemies that are gone and unroots each of the others on its own, so one crash can't skip the rest. The game's exit sound and effect still play. Only takes over the loop if its IL is exactly the game-1.5.0 shape. |
| **StandaloneAncientScepter 1.1.37** | Its hook on every REX flower's pulse (`TreebotFlower2_2.On_TreebotFlower2RootPulse`, scepter or not) checks the flower's owner with a plain C# null check. Once that body is destroyed (the REX died, respawned or left the stage, or a Vengeance umbra REX was killed), it crashes on every pulse before the game's own pulse runs, so the flower stops rooting and hurting enemies. 70 crashes in one session. | Treats a destroyed owner as no owner, as the game does: the flower keeps rooting and damaging at normal radius (no scepter extras once the owner is gone). |
| **Freeze/stun/shock (vanilla)** | `SetStateOnHurt` fills its duration-modifier list only in `Start()`, but it can be hit before that runs (an enemy spawned earlier in the same laggy frame, e.g. a Multitudes copy). A freeze, stun or shock on it then crashes `GetModifiedDuration` inside the damage function, so the rest of the hit is lost: damage numbers, on-hit effects and, on a killing blow, the gold, XP and on-kill procs. | Fills the list early the same way `Start()` does; if that isn't possible, uses the unmodified duration. |
| **Artifact of Sacrifice drop rate** | RiskyMod's `Sacrifice - No Drop Chance Scaling` makes every eligible kill a flat 8% (with Classic Items Returns installed) and overrides ShareSuite's divide-by-player-count, so with Multitudes copies and faster spawns, drops pile up. | Optional `[Sacrifice] DropChanceMultiplier` scales the final chance right before the game's roll, after RiskyMod and ShareSuite (0.7 = 5.6% per kill). |
| **PrintToInventory 1.3.1 + ShareSuite** | PrintToInventory turns itself off when ShareSuite is installed ("ShareSuite detected! PrintToInventory will be disabled"), because both would hand out printer and cauldron items. So scrappers drop scrap on the ground. | Lets PrintToInventory load, but keeps only its scrapper part: scrap goes straight into the scrapping player's inventory (host-side; multiplayer only by its own default). Printers, cauldrons, cleansing pools and the Wandering Chef stay as they are, whatever its config says. ShareSuite never hooks scrappers, so nothing gets handed out twice. Scrap given this way isn't shared by ShareSuite. |
| **PaladinMod 2.1.0** | `Asset.GatherMaterials` crashes because Mercenary's `EvisProjectile` no longer has a `ProjectileController`, which aborts Paladin's whole setup, so the survivor never gets added. | Makes each material lookup in that method independent (one failure no longer stops the rest), then fills the missing `matMercSwipe` from the Eviscerate ghost prefab directly. |

The hook guard is generic: any mod whose `On.` hook fails *only* because a game method's signature changed, or
because the hook type it was built against no longer exists, gets that one hook skipped (logged as a warning)
instead of aborting its setup. Every other kind of error passes through untouched.

## Settings (`BepInEx\config\local.compatshield.cfg`, created on first launch)

| Setting | Default | What it controls |
|---|---|---|
| `OutdatedHookGuard` | true | The generic hook guard above (TinkersSatchel, Inferno, and any other mod that hits it) |
| `TinkersSatchelItemIsolation` | true | Each TinkersSatchel item sets up on its own |
| `TinkersSatchelLanguageFix` | true | TinkersSatchel item names and descriptions show as text instead of `TINKERSSATCHEL_..._RENDERED` |
| `MovedAssetRedirect` | true | Old asset paths load from their new location |
| `AudioOverlapStopFix` | true | AudioOverlapFix never blocks Stop events |
| `ZetAspectsDropFix` | true | ZetAspects' kill hook no longer crashes on elites without an equipment slot |
| `GasolineChainFix` | true | Gasoline chain reactions run one blast at a time in the same frame instead of nesting |
| `BrokenHandlerGuard` | true | The Strange Can, Relic of Force, DamageLog and Artifact of Genetics crashes no longer break the events and damage chains around them |
| `PrintToInventoryScrappers` | true | With ShareSuite installed, scrappers put scrap straight into your inventory (PrintToInventory's scrapper part only) |
| `InfernoMithrixFix` | true | Mithrix's hammer slam works again on Inferno, his dash shards stop multiplying every run, and leftover reset hooks can't shrink his pillar |
| `InfernoLevelBoostFix` | true | Inferno's Level and Difficulty Boost reaches monster level again alongside RiskyTweaks' Raise Level Cap (all PCs need it, or friends see wrong health bars) |
| `InfernoCooldownScalingFix` | true | Inferno's Enemy Cooldown Reduction Scaling shortens enemy cooldowns each stage instead of lengthening them (host only) |
| `SwiftImpactFix` | true | EnemiesReturns' Swift no longer breaks the Beetle Guard impact effect for the session (error floods, run-end crash) |
| `FlowerRootFix` | true | An expiring REX flower unroots every enemy it caught, even when one of them is already gone |
| `ScepterFlowerOwnerFix` | true | REX flowers keep working after their owner's body is gone (StandaloneAncientScepter) |
| `StunDurationFix` | true | A freeze/stun/shock on an enemy that has only just spawned no longer crashes the hit |
| `PaladinMaterialsFix` | false | Only for PaladinMod 2.1.0; 2.2.0 fixed the crash itself |
| `MonsterLimit` (section `[Multitudes]`) | 0 (off) | Most enemies alive at once with Multitudes No Price Scale installed (see below) |
| `SpawnCreditMultiplier` (section `[Spawns]`) | 1 (off) | The stage's regular spawners earn this many times the credits, so waves come sooner (see below) |
| `DropChanceMultiplier` (section `[Sacrifice]`) | 1 (off) | Multiplies each kill's Artifact of Sacrifice item drop chance, after RiskyMod/ShareSuite (0.7 = 30% fewer) |

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

## Install / remove

- Installed at: `%APPDATA%\Thunderstore Mod Manager\DataFolder\RiskOfRain2\profiles\Default\BepInEx\patchers\Local-CompatShield\CompatShield.dll`
- Remove: delete that `Local-CompatShield` folder.
- Rebuild: `dotnet build -c Release`, then copy `bin\Release\netstandard2.1\CompatShield.dll` to the folder above.
- When a mod author ships a real fix, the patch just stops matching (and logs that), so it's safe to leave in.

## Multiplayer

The fix runs on each PC separately. For **Paladin and TinkersSatchel content**, friends need the same DLL in their
profile too. Otherwise their game still fails to add Paladin and TinkersSatchel's items while yours succeeds, and
the mismatch can cause desyncs. Until they have it, avoid picking Paladin in multiplayer.

For **Inferno's Mithrix slam**, the host alone fixes the fight: friends without the DLL still take the right damage.
They don't see the slam animation, though, and their log shows "Failed at InvokeHandler, probably malformed packet!"
once per slam, so give them the DLL too. The **Swift impact fix** also needs every PC: each game spawns the effect
itself, so a friend without it still gets the error flood and run-end crash on their side. The REX flower, Ancient Scepter flower, freeze/stun, Gasoline, crash-guard,
Inferno cooldown and Sacrifice fixes only need the host.

**Difficulty scaling** must match on every PC: each game computes monster level itself, so friends with a different
Inferno `Difficulty Scaling` or without `InfernoLevelBoostFix` see enemies at the wrong level on their health bars
(the host's numbers are the real ones). Give them the same DLL and the same `HIFU.Inferno.cfg` `Difficulty Scaling`
and `Level and Difficulty Boost` (1.25 by default) values.

## Known limits

- **Archaic Wisp 1.1.9:** setting its `Stage List = none` (and `Remove Greater Wisps = none`) takes Archaic Wisps off
  the stage spawn lists and brings Greater Wisps back. They can still come in through Artifact of Dissonance, Wisp
  family events and Artifact of Universe, where they float around and never attack (their charge, fire and death
  states are unregistered in game 1.5.0), and log a FireCannons "The Object you want to instantiate is null" error.

## What it logs (source `CompatShield`)

- `Outdated-hook guard installed`
- `Enabled fixes: OutdatedHookGuard=..., PaladinMaterialsFix=..., TinkersSatchelItemIsolation=..., TinkersSatchelLanguageFix=..., MovedAssetRedirect=..., AudioOverlapStopFix=..., ZetAspectsDropFix=..., GasolineChainFix=..., BrokenHandlerGuard=..., StunDurationFix=..., PrintToInventoryScrappers=..., InfernoMithrixFix=..., InfernoLevelBoostFix=..., InfernoCooldownScalingFix=..., FlowerRootFix=..., ScepterFlowerOwnerFix=..., SwiftImpactFix=..., MonsterLimit=..., SpawnCreditMultiplier=..., SacrificeDropChanceMultiplier=...`
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
- `Swift: its impact-effect load keeps its handle, so the Beetle Guard impact effect can't lose its prefab mid-session` and
  `Swift: a corpse counts as landed before its impact effect spawns, so a broken effect can't throw every tick`, or a
  `Swift impact fix: ...` warning if EnemiesReturns changed
- `REX flower: an expiring flower now unroots every enemy it caught, even when one of them is already gone`, then
  `REX flower: skipped unrooting an enemy that was already gone (shown once)`
- `Ancient Scepter: REX flowers keep working after their owner's body is gone`, then
  `Ancient Scepter: a REX flower outlived its owner's body and keeps working (shown once)`
- `Freeze/stun: hits on a body that has only just spawned no longer crash (and lose the kill)`, then
  `Freeze/stun: set up a just-spawned body's duration modifiers early so the hit could finish (shown once)`
- `Sacrifice: item drop chance per kill now x0.7 (after RiskyMod/ShareSuite)`, then
  `Sacrifice: a kill rolled 5.6% instead of 8% (shown once)`
- `AudioOverlapFix: stop events always go through now, so looping sounds can end`, then
  `AudioOverlapFix: first stop event let through (shown once)`
- `Spawns: the stage's regular spawners earn Nx credits`, then on the first stage: `Spawns: Director credits Ax -> Bx`
- `Multitudes: enemies capped at N alive at once (its copies included)`, then `Game's enemy limit raised from X to N`
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
