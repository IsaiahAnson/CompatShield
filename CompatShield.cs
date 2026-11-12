// CompatShield: local compatibility patches for Risk of Rain 2 mods that broke on recent game updates.
// Installed as a BepInEx preloader patcher so it is active before any plugin's Awake() runs.
// Delete the DLL to undo everything; no mod files are modified.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Mono.Cecil;
using MonoMod.RuntimeDetour.HookGen;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace CompatShield
{
    public static class CompatShieldPatcher
    {
        // BepInEx patcher contract: RoR2.dll gets one forwarding method; all other work happens in Finish().
        public static IEnumerable<string> TargetDLLs { get; } = new[] { "RoR2.dll" };

        /// <summary>
        /// Adds Inventory.RemoveItem(ItemDef, int), which game 1.5.0 renamed RemoveItem2, back to the game in memory: Bubbets Items
        /// calls it once a stage for every player, and a method with a dead reference can't be repaired from outside. Runs before
        /// Finish, so it has no config switch; the method only forwards, and nothing calls it unless a mod still uses the old name.
        /// </summary>
        public static void Patch(AssemblyDefinition assembly)
        {
            if (assembly.Name.Name != "RoR2") return;
            try
            {
                var inventory = assembly.MainModule.GetType("RoR2.Inventory");
                bool Takes(MethodDefinition m, string name) => m.Name == name && !m.IsStatic && m.Parameters.Count == 2
                    && m.Parameters[0].ParameterType.FullName == "RoR2.ItemDef" && m.Parameters[1].ParameterType.FullName == "System.Int32";
                var renamed = inventory?.Methods.FirstOrDefault(m => Takes(m, "RemoveItem2"));
                if (renamed == null || inventory.Methods.Any(m => Takes(m, "RemoveItem"))) return;
                var shim = new MethodDefinition("RemoveItem", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig, assembly.MainModule.TypeSystem.Void);
                shim.Parameters.Add(new ParameterDefinition("itemDef", Mono.Cecil.ParameterAttributes.None, renamed.Parameters[0].ParameterType));
                shim.Parameters.Add(new ParameterDefinition("count", Mono.Cecil.ParameterAttributes.None, renamed.Parameters[1].ParameterType));
                var il = shim.Body.GetILProcessor();
                il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
                il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
                il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_2);
                il.Emit(Mono.Cecil.Cil.OpCodes.Call, renamed);
                il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
                inventory.Methods.Add(shim);
                preloadNote = "Game: Inventory.RemoveItem(ItemDef, int) added back in memory for mods built before 1.5.0";
            }
            catch (Exception e) { preloadNote = "Couldn't add Inventory.RemoveItem(ItemDef, int): " + e.Message; }
        }
        static string preloadNote;

        internal static ManualLogSource Log;
        static Harmony harmony;
        static readonly HashSet<string> appliedFixes = new HashSet<string>();

        static bool paladinFixEnabled;

        public static void Finish()
        {
            Log = BepInEx.Logging.Logger.CreateLogSource("CompatShield");
            if (preloadNote != null) Log.LogInfo(preloadNote);
            harmony = new Harmony("local.compatshield");

            // BepInEx\config\local.compatshield.cfg: grouped into the numbered sections in ConfigMigration.Cat, which BepInEx writes in order.
            var cfgPath = Path.Combine(Paths.ConfigPath, "local.compatshield.cfg");
            ConfigMigration.Run(cfgPath);
            var cfg = new ConfigFile(cfgPath, true);

            // 1. Spawns and Enemies
            spawnCredits = cfg.Bind(ConfigMigration.Cat.Spawns, "SpawnCreditMultiplier", 1f,
                "How fast the stage's regular enemy spawners earn credits, so waves come sooner. 1 = game default, 1.5 = about a third sooner. Teleporter, shrine and special-mode spawners are left alone. Host only.").Value;
            monsterLimit = cfg.Bind(ConfigMigration.Cat.Spawns, "MonsterLimit", 0,
                "Most enemies alive at once while Multitudes No Price Scale is installed. The game's own limit is 40; 0 leaves the game and Multitudes alone. Multitudes' copies stop at this number, and normal spawns do too when Multitudes' Lift Monster Limit is false. Boss parts and scripted encounters still spawn. Host only.").Value;
            hardEnemyCap = cfg.Bind(ConfigMigration.Cat.Spawns, "HardEnemyCap", 80,
                "The most enemies alive at once, counting every enemy team. Unlike MonsterLimit this also stops summons that ignore the game's team limit, such as death effects, clones and mods' minions, which can otherwise multiply until the game hangs. Boss characters and scripted boss parts still spawn. It only stops new spawns; nothing already alive is removed. 0 = off. Host only.").Value;
            printerWeight = cfg.Bind(ConfigMigration.Cat.Spawns, "PrinterSpawnMultiplier", 1f,
                "How likely each stage spawn pick is a 3D printer (white, green, red, overgrown). 1 = game default, 4 = four times as likely. Host only.").Value;
            scrapperWeight = cfg.Bind(ConfigMigration.Cat.Spawns, "ScrapperSpawnMultiplier", 1f,
                "How likely each stage spawn pick is a scrapper. 1 = game default, 2 = twice as likely. Host only.").Value;
            droneSpawnWeight = cfg.Bind(ConfigMigration.Cat.Spawns, "DroneSpawnMultiplier", 1f,
                "How likely each stage spawn pick is a broken drone or turret. 1 = game default, 5 = five times as likely. Host only.").Value;
            combinerWeight = cfg.Bind(ConfigMigration.Cat.Spawns, "DroneCombinerSpawnMultiplier", 1f,
                "How likely each stage spawn pick is a Drone Combiner, compared to the unmodded game. This is the total: it already allows for DroneSpawnMultiplier. 1 = game default, 5 = five times as likely. Host only.").Value;
            droneNoMinStage = cfg.Bind(ConfigMigration.Cat.Spawns, "DroneNoMinimumStage", false,
                "Lets every broken drone and turret spawn from stage 1, instead of waiting for the stage count the game sets on some of them. Host only.").Value;
            droneHealth = cfg.Bind(ConfigMigration.Cat.Spawns, "DroneHealthMultiplier", 1f,
                "Max health of allied drones and gunner turrets, including drones from items. Enemy drones are left alone. 1 = game default, 10 = ten times the health. Host only.").Value;

            // 2. Artifact of Sacrifice
            sacrificeDropMult = cfg.Bind(ConfigMigration.Cat.Sacrifice, "DropChanceMultiplier", 1f,
                "Multiplies each kill's item drop chance under Artifact of Sacrifice. 1 = game or mod default, 0.7 = 30% fewer drops, 0 = none; below 0 is ignored. Works with RiskyMod's flat chance and ShareSuite's SacrificeFix on or off. Host only.").Value;
            sacrificeKeepList = cfg.Bind(ConfigMigration.Cat.Sacrifice, "KeepInteractables", "",
                "Interactables that still spawn under Artifact of Sacrifice, as a comma-separated list of spawn card names. A name also covers longer ones starting with it, so iscShrineChance covers the Sandy and Snowy versions. Empty = the game's normal rules. " +
                "iscShrineChance = Shrine of Chance, iscCasinoChest = Adaptive Chest, iscEquipmentBarrel = Equipment Barrel, iscLunarChest = Lunar Pod. With at least one name set, the log lists every card Sacrifice blocks, so the other names are there to copy. Host only.").Value;
            sacrificeKeptWeight = cfg.Bind(ConfigMigration.Cat.Sacrifice, "KeptInteractableWeight", 1f,
                "How much of a spawn category's share goes to the cards kept by KeepInteractables when Sacrifice removes the rest of that category. 1 = the game's own math, 0.5 = half that, 0 = none; below 0 is ignored. Host only.").Value;
            shareSuiteSacrificeFix = cfg.Bind(ConfigMigration.Cat.Sacrifice, "ShareSuiteSacrificeCreditFix", true,
                "ShareSuite and the game both halve a Sacrifice stage's interactable credits, so the stage spends a quarter. On = only the game halves them, which gives twice the interactables, drones included. Off = both halve. Host only.").Value;

            // 3. Balance
            acolyteAttackSpeed = cfg.Bind(ConfigMigration.Cat.Balance, "AcolyteAttackSpeedMultiplier", 1.5f,
                "Acolyte's attack speed, in both the normal and the transformed form. 1 = game default, 1.5 = 50% faster; 0 or below is ignored. Each player's own Acolyte uses the value on that player's PC.").Value;
            lockedItems = cfg.Bind(ConfigMigration.Cat.Balance, "LockedItems", "",
                "Items and equipment locked off in every lobby, as a comma-separated list of internal names (the names in Def.StartBonusMod.cfg, for example ChainLightning). Each one shows [CONFIG DISABLED] in the lobby's item list and its lock can't be clicked off, the way an item disabled in TinkersSatchel's config behaves. " +
                "The list goes by name, so adding or removing mods can't move the locks to other items. Empty = nothing locked. The log lists what was locked and any name it could not find. The host decides; the red title shows on each PC that has the same list.").Value;
            rouletteSpinCost = cfg.Bind(ConfigMigration.Cat.Balance, "RouletteSpinCost", true,
                "Takes a paid ItemRoulette spin out of ShareSuite's shared gold pool too. Without this, ShareSuite refunds every spin on the next frame, so spins are free in multiplayer. Does nothing with shared money off or in single player. Host only.").Value;
            disableGenetics = cfg.Bind(ConfigMigration.Cat.Balance, "DisableGenetics", true,
                "Keeps the Artifact of Genetics (GeneticArtifact mod) off: a vote for it is ignored and it is hidden from the lobby, while the mod stays installed. Set false to get it back. The host decides.").Value;

            // 4. Lag and Hang Protection
            statUpdateInterval = cfg.Bind(ConfigMigration.Cat.Lag, "StatUpdateInterval", 0.1f,
                "Shortest time between one character's automatic stat updates, in seconds. Items and auras that flip a shield or refresh a buff every physics tick made the game redo a character's full stat calculation up to 60 times a second, which drained the frame rate with few enemies around. 0.1 = at most 10 a second per character. A single change still applies at once; only rapid repeats wait, for at most this long. 0 = off. Each PC for itself.").Value;
            physicsCatchUpCap = cfg.Bind(ConfigMigration.Cat.Lag, "PhysicsCatchUpCap", 0.1f,
                "Limits how much physics Unity runs to catch up after a slow frame, in seconds. 0.1 = at most 6 steps per frame, 0.05 = at most 3, so under heavy load game time slows a little instead of the frame rate collapsing. 0 = off (game default 0.333). Set at each run start. Each PC for itself.").Value;
            gasolineFix = damageChainGuard = cfg.Bind(ConfigMigration.Cat.Lag, "DamageChainGuard", true,
                "Stops kill chains overflowing the stack and crashing the game. Hits nested more than 3 deep, and Gasoline blasts set off inside another Gasoline blast, wait in line and run after the outer hit. A huge chain is spread over later physics ticks instead of freezing the game. Host only.").Value;
            damageChainQueueLimit = cfg.Bind(ConfigMigration.Cat.Lag, "DamageChainQueueLimit", DamageChainGuard.DefaultQueueLimit,
                "Goes with DamageChainGuard. How many hits from a chain may wait in line at once. Past this, hits nested more than 3 deep are dropped; direct hits and the first 3 links always land. Host only.").Value;
            damageChainDrainMs = cfg.Bind(ConfigMigration.Cat.Lag, "DamageChainDrainMs", DamageChainGuard.DefaultDrainMs,
                "Goes with DamageChainGuard. Milliseconds of waiting hits that may run per physics tick, so a huge chain lands over a few seconds instead of freezing the game. 0 = no limit, the whole line runs in one frame. Host only.").Value;
            blastChainCap = cfg.Bind(ConfigMigration.Cat.Lag, "BlastChainCap", true,
                "Stops item combos with exploding on-hit effects (such as Marwan's Ash and Fire Shield) from hanging the game. A blast set off inside other blasts stops firing at BlastChainMaxDepth or when BlastChainBudgetMs runs out. A blast that isn't inside another one always fires. Host only.").Value;
            blastChainMaxDepth = cfg.Bind(ConfigMigration.Cat.Lag, "BlastChainMaxDepth", BlastChainCap.DefaultMaxDepth,
                "Goes with BlastChainCap. How many blasts may run inside each other at once. A deeper one deals no damage and sets off nothing. Minimum 1. Host only.").Value;
            blastChainBudgetMs = cfg.Bind(ConfigMigration.Cat.Lag, "BlastChainBudgetMs", BlastChainCap.DefaultBudgetMs,
                "Goes with BlastChainCap. Time allowance in milliseconds for blasts inside blasts, shared by every chain and refilled between chains. A big one-level explosion is not cut. 0 = no time limit. Host only.").Value;

            // 5. Error and Crash Protection
            blastTargetGuard = statsRetryGuard = overlapHitGuard = dotTickGuard = bulletHitGuard = totemTetherGuard = cfg.Bind(ConfigMigration.Cat.Errors, "ErrorLoopGuards", true,
                "Stops one error repeating every physics tick. Without these, an error inside a blast, melee hit or damage zone, damage-over-time tick, bullet, stat update or sacrifice totem's tether makes the game run that code again and again. Each new kind of failure logs one line naming what broke. Host for hits; each PC for its own stat updates and totem tethers.").Value;
            bool hookGuard = cfg.Bind(ConfigMigration.Cat.Errors, "OutdatedHookGuard", true,
                "Skips hooks a game update broke so the mod keeps loading, and ignores removing a hook that was never added. Every PC.").Value;
            assetRedirect = cfg.Bind(ConfigMigration.Cat.Errors, "MovedAssetRedirect", true,
                "When a mod asks for a game asset by a path the October 2026 update moved, loads it from its new path. Every PC.").Value;
            handlerGuard = cfg.Bind(ConfigMigration.Cat.Errors, "BrokenHandlerGuard", true,
                "Catches known, harmless crashes in Starstorm 2 (Strange Can, Relic of Force), DamageLog and Artifact of Genetics, so they stop breaking kills and damage. Host only.").Value;
            stunDurationFix = cfg.Bind(ConfigMigration.Cat.Errors, "StunDurationFix", true,
                "Game bug: a freeze, stun or shock on an enemy that is still spawning crashes and drops the rest of the hit, including the gold and XP on a kill. Host only.").Value;
            nullDccsPoolEntryFix = cfg.Bind(ConfigMigration.Cat.Errors, "NullDccsPoolEntryFix", true,
                "Removes empty entries from a stage's interactable pool, so Chen's drones, Tinker's Satchel and QoLChests can add their chests and drones. Logs one line per pool it cleans. Host only.").Value;
            infernoHookChurnFix = respawnHookChurnFix = cfg.Bind(ConfigMigration.Cat.Errors, "HookChurnFixes", true,
                "Stops Inferno and PlayerRespawnSystem removing and re-adding all their hooks at every run, the kind of work that crashed the game when Start was pressed. They keep their hooks between runs instead, and runs play the same. Every PC.").Value;
            stopSoundFix = orphanLoopGuard = lunarPoolLoopStopFix = cfg.Bind(ConfigMigration.Cat.Errors, "StuckSoundFixes", true,
                "Stops looping sounds that would otherwise play until the game restarts: Stop events AudioOverlapFix would drop, loops left behind when their object is destroyed, and WolfoFixes' cauldron hum. One-shot sounds are left alone. Every PC.").Value;

            // 6. Item Mod Fixes
            bubbetsFix = cfg.Bind(ConfigMigration.Cat.Items, "BubbetsItemsFix", true,
                "Lets Bubbets Items 1.8.20 run on game 1.5.0, and repairs Jellied Soles, Repulsion Plate Mk2, Void Scrap, Broken Clock, Wildlife Camera and the Slug health bar. Every PC.").Value;
            hallowedIchorTimerFix = cfg.Bind(ConfigMigration.Cat.Items, "HallowedIchorTimerFix", true,
                "Sandswept: stops Hallowed Ichor throwing an error every frame and freezing the run timer once anyone picks it up, on a HUD without its text effect. Every PC.").Value;
            tinkersIsolationEnabled = TinkersLanguageFixEnabled = cfg.Bind(ConfigMigration.Cat.Items, "TinkersSatchelFixes", true,
                "Sets up each TinkersSatchel item on its own, so one broken item doesn't stop the rest, and shows its item names and descriptions as text instead of TINKERSSATCHEL_..._RENDERED. Every PC.").Value;
            linearDamageFix = cfg.Bind(ConfigMigration.Cat.Items, "LinearDamageFocusCrystalFix", true,
                "Stops Focus Crystal counting twice when Bubbets Items and LinearDamage are both installed. Host only.").Value;
            daggerOfFrenzyGuard = cfg.Bind(ConfigMigration.Cat.Items, "DaggerOfFrenzyGuard", true,
                "Stops Smite Items' Dagger of Frenzy breaking skills on bodies with no skill locator or primary skill. Every PC.").Value;
            zetDropFix = cfg.Bind(ConfigMigration.Cat.Items, "ZetAspectsDropFix", true,
                "Stops ZetAspects' kill hook crashing on an elite's equipment slot, which skips that kill's item procs and drops.").Value;
            hypercritNullFix = cfg.Bind(ConfigMigration.Cat.Items, "HypercritNullFix", true,
                "When Hypercrit2's crit roll throws an error, counts it as no crit so the hit still lands. Host only.").Value;
            printToInventoryFix = cfg.Bind(ConfigMigration.Cat.Items, "PrintToInventoryScrappers", true,
                "Lets PrintToInventory load alongside ShareSuite and keeps only its scrapper part: scrap goes straight into the scrapping player's inventory. Printers, cauldrons, cleansing pools and the Wandering Chef stay as they are.").Value;

            // 7. Enemy and Survivor Mod Fixes
            infernoMithrixFix = cfg.Bind(ConfigMigration.Cat.Enemies, "InfernoMithrixFix", true,
                "Inferno 1.7.8: makes Mithrix's hammer slam work again, and stops his dash shards multiplying each run. Every PC.").Value;
            infernoBoostFix = cfg.Bind(ConfigMigration.Cat.Enemies, "InfernoLevelBoostFix", true,
                "Inferno: makes its Level and Difficulty Boost reach monsters when RiskyTweaks raises the level cap. Raises difficulty; the amount is Inferno's own setting. Every PC.").Value;
            infernoCooldownFix = cfg.Bind(ConfigMigration.Cat.Enemies, "InfernoCooldownScalingFix", true,
                "Inferno: makes Enemy Cooldown Reduction Scaling shorten enemy cooldowns, as its description says, instead of lengthening them. Host only.").Value;
            thetaShieldLoopFix = cfg.Bind(ConfigMigration.Cat.Enemies, "ThetaShieldLoopFix", true,
                "Sandswept: stops Theta Constructs repeating a failed shield cast every tick, which drops the frame rate and loops a sound. Host only in effect.").Value;
            swiftImpactFix = cfg.Bind(ConfigMigration.Cat.Enemies, "SwiftImpactFix", true,
                "EnemiesReturns: stops Swift breaking the game's Beetle Guard impact effect, which causes errors every tick and a run-end crash. Every PC.").Value;
            flowerRootFix = scepterFlowerFix = cfg.Bind(ConfigMigration.Cat.Enemies, "RexFlowerFixes", true,
                "REX's Tangling Growth flower: unroots every enemy when one was already gone, and keeps working when REX died or left the stage with Ancient Scepter installed. Host only.").Value;
            shamanModelFix = cfg.Bind(ConfigMigration.Cat.Enemies, "ShamanModelFix", true,
                "Rebuilds the one method that stops the deprecated Shaman mod loading on game 1.5.0, and fixes the name clash that stalled loading at 99%. Every PC, and every player needs the Shaman mod.").Value;
            skillSlotsHudFix = cfg.Bind(ConfigMigration.Cat.Enemies, "ExtraSkillSlotsHudFix", true,
                "Builds ExtraSkillSlots' four extra icons to fit a replacement HUD such as RiskUI, so the slots can be seen. Each player, for their own screen.").Value;
            paladinFixEnabled = cfg.Bind(ConfigMigration.Cat.Enemies, "PaladinMaterialsFix", false,
                "Crash-proofs PaladinMod 2.1.0's material setup. Off by default: PaladinMod 2.2.0 fixed this itself. Every PC.").Value;
            paladinShockwaveFix = cfg.Bind(ConfigMigration.Cat.Enemies, "PaladinShockwaveFix", true,
                "PaladinMod: stops a shockwave throwing an error every tick, about 50 a second, once its dust effect is gone. Every PC.").Value;
            fireTrailFix = cfg.Bind(ConfigMigration.Cat.Enemies, "SandsweptFireTrailFix", true,
                "Sandswept: stops the Delta Construct's fire trail throwing an error when its last flame burns out, which skipped other trails' damage that tick. Every PC.").Value;
            sharpAnchorFix = cfg.Bind(ConfigMigration.Cat.Items, "SharpAnchorZoneFix", true,
                "StormyItems: stops a Sharp Anchor zone that has lost track of its holder throwing an error every tick, up to 100 a second. Host only.").Value;

            // 8. Diagnostics
            lagTracer = cfg.Bind(ConfigMigration.Cat.Diagnostics, "LagTracer", true,
                "Finds where lag comes from: during a run, logs fps, the worst frame, counts of monsters and projectiles, and what each hot code path cost, every 10 s. About 6 log lines a minute. Set false to silence it.").Value;
            soundTracer = cfg.Bind(ConfigMigration.Cat.Diagnostics, "SoundTracer", true,
                "Names an audio loop that follows you into the lobby: at each run end, logs loop-type sounds started on objects that no longer exist. A few log lines per run end. Set false once the loop is found.").Value;
            bigHitTracer = cfg.Bind(ConfigMigration.Cat.Diagnostics, "BigHitTracer", true,
                "Names what dealt a huge hit to a player. During a run it logs the victim, the attacker, the object that carried the damage, the damage type, the proc chain and the code path for any hit that takes 25% or more of a player's full health or the last of it, and for every player death. " +
                "At most 5 lines per source and 60 per run, plus a summary at run end. Log only: it changes nothing in the game. Only the host needs it.").Value;
            physicsPileupTracer = cfg.Bind(ConfigMigration.Cat.Diagnostics, "PhysicsPileupTracer", true,
                "Names a physics pile-up: after a frame over 0.5 s during a run, logs which moving objects hold the most colliders and how many share one spot, at most every 10 s. Silent while the game runs smoothly. Only the host needs it.").Value;
            hookTimer = cfg.Bind(ConfigMigration.Cat.Diagnostics, "HookTimer", false,
                "Lag hunting: names the mods whose code costs the most. From the first run start it times each mod's hooks, Harmony patches and event handlers on about 40 busy game methods (stats, damage, buffs, inventory, per-frame updates) " +
                "and logs the slowest mods and single hooks every 5 minutes, at each stage change and at run end. Log only, but it wraps a few hundred mod methods in a timer: expect a pause of about 10 seconds at the first run start, a shorter one at the next stage and slightly lower speed, " +
                "so leave it false unless you are measuring. Each PC measures itself.").Value;

            ConfigMigration.EnsureSaved(cfg);
            Log.LogInfo($"Enabled fixes: ErrorLoopGuards={blastTargetGuard}, StuckSoundFixes={orphanLoopGuard}, HookChurnFixes={respawnHookChurnFix}, TinkersSatchelFixes={tinkersIsolationEnabled}, RexFlowerFixes={flowerRootFix}, " +
                        $"ThetaShieldLoopFix={thetaShieldLoopFix}, HallowedIchorTimerFix={hallowedIchorTimerFix}, SandsweptFireTrailFix={fireTrailFix}, SharpAnchorZoneFix={sharpAnchorFix}, DaggerOfFrenzyGuard={daggerOfFrenzyGuard}, ExtraSkillSlotsHudFix={skillSlotsHudFix}, LinearDamageFocusCrystalFix={linearDamageFix}, BubbetsItemsFix={bubbetsFix}, OutdatedHookGuard={hookGuard}, PaladinMaterialsFix={paladinFixEnabled}, PaladinShockwaveFix={paladinShockwaveFix}, " +
                        $"MovedAssetRedirect={assetRedirect}, ZetAspectsDropFix={zetDropFix}, BrokenHandlerGuard={handlerGuard}, StunDurationFix={stunDurationFix}, " +
                        $"PrintToInventoryScrappers={printToInventoryFix}, InfernoMithrixFix={infernoMithrixFix}, InfernoLevelBoostFix={infernoBoostFix}, " +
                        $"InfernoCooldownScalingFix={infernoCooldownFix}, ShamanModelFix={shamanModelFix}, SwiftImpactFix={swiftImpactFix}, ShareSuiteSacrificeCreditFix={shareSuiteSacrificeFix}, MonsterLimit={monsterLimit}, HardEnemyCap={hardEnemyCap}, " +
                        $"SpawnCreditMultiplier={spawnCredits}, PrinterSpawnMultiplier={printerWeight}, ScrapperSpawnMultiplier={scrapperWeight}, DroneCombinerSpawnMultiplier={combinerWeight}, DroneNoMinimumStage={droneNoMinStage}, DroneSpawnMultiplier={droneSpawnWeight}, DroneHealthMultiplier={droneHealth}, AcolyteAttackSpeedMultiplier={acolyteAttackSpeed}, SacrificeDropChanceMultiplier={sacrificeDropMult}, DropChanceMultiplier={sacrificeDropMult}, KeepInteractables=[{sacrificeKeepList}], KeptInteractableWeight={sacrificeKeptWeight}, DamageChainGuard={damageChainGuard}, BlastChainCap={blastChainCap}, RouletteSpinCost={rouletteSpinCost}, NullDccsPoolEntryFix={nullDccsPoolEntryFix}, HypercritNullFix={hypercritNullFix}, DisableGenetics={disableGenetics}, LockedItems=[{lockedItems}], SoundTracer={soundTracer}, LagTracer={lagTracer}, BigHitTracer={bigHitTracer}, PhysicsPileupTracer={physicsPileupTracer}, HookTimer={hookTimer}, PhysicsCatchUpCap={physicsCatchUpCap}, StatUpdateInterval={statUpdateInterval}, DamageChainQueueLimit={damageChainQueueLimit}, DamageChainDrainMs={damageChainDrainMs}, BlastChainMaxDepth={blastChainMaxDepth}, BlastChainBudgetMs={blastChainBudgetMs}");

            if (hookGuard)
            {
                try
                {
                    HookShield.Apply(harmony);
                    Log.LogInfo("Outdated-hook guard installed");
                }
                catch (Exception e)
                {
                    Log.LogError("Outdated-hook guard failed to install: " + e);
                }
            }
            // Before any mod loads: MonoMod tells this listener about every hook from here on. It touches MonoMod only.
            if (hookTimer) HookTimerCapture.Install();
            if (paladinFixEnabled || tinkersIsolationEnabled || monsterLimit > 0 || SpawnTweakWanted || InteractableTweakWanted || assetRedirect || stopSoundFix || zetDropFix || gasolineFix || handlerGuard || printToInventoryFix
                || flowerRootFix || scepterFlowerFix || stunDurationFix || SacrificeTweakWanted || SacrificeKeepWanted || infernoMithrixFix
                || infernoBoostFix || infernoCooldownFix || infernoHookChurnFix || respawnHookChurnFix || lunarPoolLoopStopFix
                || shamanModelFix || bubbetsFix || linearDamageFix || skillSlotsHudFix || daggerOfFrenzyGuard || thetaShieldLoopFix || hallowedIchorTimerFix || paladinShockwaveFix || fireTrailFix || sharpAnchorFix || swiftImpactFix || shareSuiteSacrificeFix || soundTracer || orphanLoopGuard || lagTracer || physicsPileupTracer || bigHitTracer || hookTimer || damageChainGuard || blastChainCap || blastTargetGuard || statsRetryGuard || overlapHitGuard || dotTickGuard || bulletHitGuard || totemTetherGuard || rouletteSpinCost|| acolyteAttackSpeed != 1f || nullDccsPoolEntryFix || hypercritNullFix || disableGenetics || !string.IsNullOrWhiteSpace(lockedItems) || physicsCatchUpCap > 0f || statUpdateInterval > 0f || hardEnemyCap > 0)
            {
                AppDomain.CurrentDomain.AssemblyLoad += (_, e) => ApplyModFixes(e.LoadedAssembly);
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    ApplyModFixes(asm);
            }
        }

        static bool tinkersIsolationEnabled;
        internal static bool TinkersLanguageFixEnabled;
        static int monsterLimit;
        static float spawnCredits;
        static float printerWeight, scrapperWeight, combinerWeight, droneSpawnWeight, droneHealth;
        static bool droneNoMinStage;
        static bool interactableTweakApplied;
        static bool spawnTweakApplied;
        static bool assetRedirect, assetRedirectApplied;
        static bool stopSoundFix;
        static bool zetDropFix;
        static bool gasolineFix, gasolineApplied;
        static bool handlerGuard;
        static bool printToInventoryFix;
        static bool infernoMithrixFix;
        static bool infernoBoostFix, infernoCooldownFix, infernoHookChurnFix;
        static bool respawnHookChurnFix;
        static bool lunarPoolLoopStopFix, shamanModelFix;
        static bool soundTracer, soundTracerApplied, orphanLoopGuard;
        static bool lagTracer, lagTracerApplied;
        static bool physicsPileupTracer, physicsPileupApplied;
        static bool bigHitTracer, bigHitApplied;
        static bool hookTimer, hookTimerApplied;
        static string lockedItems;
        static bool lockedItemsApplied;
        static float physicsCatchUpCap;
        static float statUpdateInterval;
        static int hardEnemyCap;
        static float damageChainDrainMs;
        static int damageChainQueueLimit;
        static bool physicsCapApplied;
        static bool statLimitApplied;
        static bool hardCapApplied;
        static bool damageChainGuard, damageChainGuardApplied, hypercritNullFix;
        static int blastChainMaxDepth;
        static float blastChainBudgetMs;
        static bool blastChainCap, blastChainCapApplied;
        static bool blastTargetGuard, blastTargetGuardApplied;
        static bool statsRetryGuard, statsRetryGuardApplied;
        static bool overlapHitGuard, overlapHitGuardApplied;
        static bool dotTickGuard, dotTickGuardApplied;
        static bool bulletHitGuard, bulletHitGuardApplied;
        static bool totemTetherGuard, totemTetherGuardApplied;
        static bool rouletteSpinCost, rouletteSpinCostApplied;
        static bool bubbetsFix;
        static bool linearDamageFix;
        static bool skillSlotsHudFix;
        static bool daggerOfFrenzyGuard;
        static bool thetaShieldLoopFix;
        static bool hallowedIchorTimerFix;
        static bool paladinShockwaveFix, fireTrailFix, sharpAnchorFix;
        static float acolyteAttackSpeed;
        static bool acolyteTweakApplied;
        static bool nullDccsPoolEntryFix, nullDccsPoolEntryFixApplied;
        static bool disableGenetics, disableGeneticsApplied;
        static bool swiftImpactFix;
        static bool shareSuiteSacrificeFix;
        static bool flowerRootFix, flowerRootApplied;
        static bool scepterFlowerFix;
        static bool stunDurationFix, stunDurationApplied;
        static float sacrificeDropMult;
        static bool sacrificeApplied;
        static bool InteractableTweakWanted => (printerWeight >= 0f && Math.Abs(printerWeight - 1f) > 0.001f) || (scrapperWeight >= 0f && Math.Abs(scrapperWeight - 1f) > 0.001f) || (combinerWeight >= 0f && Math.Abs(combinerWeight - 1f) > 0.001f) || droneNoMinStage || (droneSpawnWeight >= 0f && Math.Abs(droneSpawnWeight - 1f) > 0.001f) || (droneHealth > 0f && Math.Abs(droneHealth - 1f) > 0.001f);
        static bool SpawnTweakWanted => spawnCredits > 0 && Math.Abs(spawnCredits - 1f) > 0.001f;
        static bool SacrificeTweakWanted => sacrificeDropMult >= 0f && Math.Abs(sacrificeDropMult - 1f) > 0.001f;
        static string sacrificeKeepList;
        static float sacrificeKeptWeight;
        static bool sacrificeKeepApplied;
        static bool SacrificeKeepWanted => !string.IsNullOrWhiteSpace(sacrificeKeepList);

        // Plugins load once the game's own assemblies are up, so patching game code then is as safe as a plugin doing it.
        static bool IsPlugin(Assembly asm)
        {
            try { return asm.Location.StartsWith(Paths.PluginPath, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }   // dynamic assemblies have no location
        }

        static void ApplyModFixes(Assembly asm)
        {
            string name;
            try { name = asm.GetName().Name; }
            catch { return; }
            // appliedFixes is keyed by assembly name: any future PaladinMod fix belongs inside this same if.
            if ((paladinFixEnabled || paladinShockwaveFix) && name == "PaladinMod" && appliedFixes.Add(name))
            {
                if (paladinFixEnabled)
                    PaladinFix.Apply(harmony, asm);
                if (paladinShockwaveFix)
                {
                    var ror2ForPaladin = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                    if (ror2ForPaladin != null) PaladinShockwaveFix.Apply(harmony, asm, ror2ForPaladin);
                    else Log.LogWarning("Paladin shockwave fix: RoR2 isn't loaded yet, not applied");
                }
            }
            if (tinkersIsolationEnabled && name == "TinkersSatchel" && appliedFixes.Add(name))
            {
                const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                var module = asm.GetType("ThinkInvisible.TinkersSatchel.Module");
                ModuleIsolation.Apply(harmony, "TinkersSatchel", module, module?.GetMethod("SetupAll_PluginAwake", any),
                    "SetupConfig", "SetupAttributes", "SetupBehavior");
                ModuleIsolation.Apply(harmony, "TinkersSatchel", module, module?.GetMethod("SetupAll_PluginStart", any),
                    "SetupLate", "Install");
                // Once setup completes, the plugin's per-frame Update runs AutoConfigModule.Update, which still calls
                // the removed Language.SetCurrentLanguage(string) and so throws every frame. Replace it.
                TinkersAutoConfigFix.Apply(harmony, asm);
            }
            if (monsterLimit > 0 && name == "MultitudesNoPriceScale" && appliedFixes.Add(name))
                MonsterLimitTweak.Apply(harmony, AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2"),
                    asm, monsterLimit);
            // On its assembly load, so the patches are in before AudioOverlapFix's Awake hooks PostEvent.
            if (handlerGuard && name == "Starstorm2" && appliedFixes.Add(name))
            {
                HandlerGuard.Apply(harmony, asm, "SS2.Items.StrangeCan", "OnCharacterDeathGlobal", typeof(KeyNotFoundException),
                    "Strange Can skipped a kill whose Intoxicated attacker is gone");
                // Called straight from HealthComponent.TakeDamageProcess, before HandleCharacterDeath: a throw here means a
                // killing blow gives no gold, XP or on-kill procs (the body still dies via ManagedFixedUpdate). It throws
                // reading the attacker's already-destroyed inventory.
                HandlerGuard.Apply(harmony, asm, "SS2.Items.RelicOfForce+Behavior", "OnDamageDealtServer", typeof(NullReferenceException),
                    "Relic of Force skipped a hit whose attacker's inventory is gone");
            }
            if (handlerGuard && name == "DamageLog" && appliedFixes.Add(name))
                HandlerGuard.Apply(harmony, asm, "DamageLog.DamageLog", "Record", typeof(NullReferenceException),
                    "DamageLog skipped a hit it couldn't find an attacker portrait for");
            if (handlerGuard && name == "GeneticsArtifact" && appliedFixes.Add(name))
                AfterOrigGuard.Apply(harmony, asm, "GeneticsArtifact.GeneEngineDriver", "HealthComponent_TakeDamage", typeof(NullReferenceException),
                    "Artifact of Genetics skipped tracking a hit it crashed on (the damage itself already landed)");
            if (zetDropFix && name == "ZetAspects" && appliedFixes.Add(name))
                ZetAspectsDropFix.Apply(harmony, ZetAspectsDropFix.FindHook(asm));
            // On its assembly load, so the patch is in before its Awake checks for ShareSuite.
            if (printToInventoryFix && name == "PrintToInventory" && appliedFixes.Add(name))
                PrintToInventoryScrapperFix.Apply(harmony, asm);
            // appliedFixes is keyed by assembly name: any future AncientScepter fix belongs inside this same if.
            if (scepterFlowerFix && name == "AncientScepter" && appliedFixes.Add(name))
                ScepterFlowerFix.Apply(harmony, asm);
            // On its assembly load: long before its hooks go in at the first run start. appliedFixes is keyed by assembly
            // name: any future Inferno fix belongs inside this same if.
            if ((infernoMithrixFix || infernoBoostFix || infernoCooldownFix || infernoHookChurnFix) && name == "Inferno" && appliedFixes.Add(name))
            {
                if (infernoMithrixFix)
                    InfernoMithrixFix.Apply(harmony, asm);
                if (infernoBoostFix || infernoCooldownFix)
                    InfernoScalingFix.Apply(harmony, asm, infernoBoostFix, infernoCooldownFix);
                if (infernoHookChurnFix)
                    InfernoHookChurnFix.Apply(harmony, asm);
            }
            // On its assembly load, before its plugin Awake subscribes the two Run hooks this patches. appliedFixes is keyed by
            // assembly name: any future PlayerRespawnSystem fix belongs inside this same if.
            if (respawnHookChurnFix && name == "PlayerRespawnSystem" && appliedFixes.Add(name))
                RespawnHookChurnFix.Apply(harmony, asm);
            // On WolfoFixes' assembly load: the patch is on a game method, but only WolfoFixes starts the hum. appliedFixes is
            // keyed by assembly name: any future WolfoFixes fix belongs inside this same if.
            if (lunarPoolLoopStopFix && name == "WolfoFixes" && appliedFixes.Add(name))
                LunarPoolLoopStopFix.Apply(harmony);
            if (skillSlotsHudFix && name == "ExtraSkillSlots" && appliedFixes.Add(name)) SkillSlotsHudFix.Apply(harmony, asm);
            if (daggerOfFrenzyGuard && name == "Smite Items" && appliedFixes.Add(name))
            {
                var ror2ForDagger = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2ForDagger != null) DaggerOfFrenzyFix.Apply(harmony, asm, ror2ForDagger);
                else Log.LogWarning("Dagger of Frenzy guard: RoR2 isn't loaded yet, not applied");
            }
            if ((thetaShieldLoopFix || hallowedIchorTimerFix || fireTrailFix) && name == "Sandswept" && appliedFixes.Add(name))
            {
                var ror2ForTheta = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2ForTheta == null) Log.LogWarning("Sandswept fixes: RoR2 isn't loaded yet, not applied");
                else
                {
                    if (thetaShieldLoopFix) ThetaShieldLoopFix.Apply(harmony, asm, ror2ForTheta);
                    if (hallowedIchorTimerFix) HallowedIchorTimerFix.Apply(harmony, asm, ror2ForTheta);
                    if (fireTrailFix) SandsweptFireTrailFix.Apply(harmony, asm, ror2ForTheta);
                }
            }
            // appliedFixes is keyed by assembly name: any future StormyItems fix belongs inside this same if.
            if (sharpAnchorFix && name == "StormyItems" && appliedFixes.Add(name))
            {
                var ror2ForStormy = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2ForStormy != null) SharpAnchorZoneFix.Apply(harmony, asm, ror2ForStormy);
                else Log.LogWarning("Sharp Anchor fix: RoR2 isn't loaded yet, not applied");
            }
            // On its assembly load, before its plugin Awake runs its Harmony patches. appliedFixes is keyed by assembly name: any
            // future Bubbets fix belongs inside this same if.
            if (linearDamageFix && name == "LinearDamage" && appliedFixes.Add(name)) LinearDamageFix.Apply(harmony, asm);
            if (bubbetsFix && name == "BubbetsItems" && appliedFixes.Add(name))
            {
                var ror2ForBubbets = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2ForBubbets != null) BubbetsFix.Apply(harmony, asm, ror2ForBubbets);
                else Log.LogWarning("Bubbets fix: RoR2 isn't loaded yet, not applied");
            }
            // On its assembly load, before its plugin Awake builds the survivor. appliedFixes is keyed by assembly name: any
            // future Shaman fix belongs inside this same if.
            if (shamanModelFix && name == "ShamanMod" && appliedFixes.Add(name))
                ShamanModelFix.Apply(harmony, asm);
            // On its assembly load, long before the first Swift dies. appliedFixes is keyed by assembly name: any future
            // EnemiesReturns fix belongs inside this same if.
            if (swiftImpactFix && name == "EnemiesReturns" && appliedFixes.Add(name))
                SwiftImpactFix.Apply(harmony, asm);
            // On its assembly load, before its plugin hooks anything. appliedFixes is keyed by assembly name: any future
            // ShareSuite fix belongs inside this same if.
            if (shareSuiteSacrificeFix && name == "ShareSuite" && appliedFixes.Add(name))
                ShareSuiteSacrificeCreditFix.Apply(harmony, asm);
            if (stopSoundFix && name == "AudioOverlapFix" && appliedFixes.Add(name))
                StopSoundFix.Apply(harmony, asm, Type.GetType("AkSoundEngine, AK.Wwise.Unity.API")?.Assembly);
            // At the first plugin, before any sound is posted (Wwise and RoR2.Run are loaded by then).
            if ((soundTracer || orphanLoopGuard) && !soundTracerApplied && IsPlugin(asm))
            {
                soundTracerApplied = true;
                SoundTracer.Apply(harmony, soundTracer, orphanLoopGuard);
            }
            // At the first plugin too (RoR2.Run is loaded). Creates nothing of Unity's here: its ticker comes at the first run start.
            if (lagTracer && !lagTracerApplied && IsPlugin(asm))
            {
                lagTracerApplied = true;
                LagTracer.Apply(harmony);
            }
            // At the first plugin (RoR2.dll is loaded). The lock itself is set from the game's tick once the rule list exists.
            if (!string.IsNullOrWhiteSpace(lockedItems) && !lockedItemsApplied && IsPlugin(asm))
            {
                lockedItemsApplied = true;
                LockedItems.Apply(AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2"), lockedItems);
            }
            // At the first plugin too (RoR2.Run is loaded). It only subscribes to run start and end here; the damage and death events are hooked at the first run start.
            if (bigHitTracer && !bigHitApplied && IsPlugin(asm))
            {
                bigHitApplied = true;
                BigHitTracer.Apply();
            }
            // At the first plugin too (RoR2.Run is loaded). It only subscribes to run start and end here; the timers go on at the first run start.
            if (hookTimer && !hookTimerApplied && IsPlugin(asm))
            {
                hookTimerApplied = true;
                HookTimer.Apply();
            }
            // At the first plugin too (RoR2.Run is loaded). Its ticker comes at the first run start, never here.
            if (physicsPileupTracer && !physicsPileupApplied && IsPlugin(asm))
            {
                physicsPileupApplied = true;
                PhysicsPileupTracer.Apply();
            }
            // At the first plugin (RoR2.Run is loaded); the cap itself is set at each run start, never here.
            if (physicsCatchUpCap > 0f && !physicsCapApplied && IsPlugin(asm))
            {
                physicsCapApplied = true;
                PhysicsCatchUpCap.Apply(physicsCatchUpCap);
            }
            // Before the first plugin's Awake, so every mod's asset loads go through it.
            if (assetRedirect && !assetRedirectApplied && IsPlugin(asm))
            {
                assetRedirectApplied = true;
                AssetRedirect.Apply(harmony, AssetRedirect.FindEvaluateKey());
            }
            // Same moment as the Gasoline chain fix (the first plugin, game code loaded): the general kill-chain guard.
            if (damageChainGuard && !damageChainGuardApplied && IsPlugin(asm))
            {
                var ror2ForGuard = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2ForGuard != null)
                {
                    damageChainGuardApplied = true;
                    DamageChainGuard.Configure(damageChainQueueLimit, damageChainDrainMs);
                    DamageChainGuard.Apply(harmony, ror2ForGuard);
                }
            }
            if (blastChainCap && !blastChainCapApplied && IsPlugin(asm))
            {
                var ror2ForBlast = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2ForBlast != null)
                {
                    blastChainCapApplied = true;
                    BlastChainCap.Configure(blastChainMaxDepth, blastChainBudgetMs);
                    BlastChainCap.Apply(harmony, ror2ForBlast);
                }
            }
            if (blastTargetGuard && !blastTargetGuardApplied && IsPlugin(asm))
            {
                var ror2ForGuard = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2ForGuard != null)
                {
                    blastTargetGuardApplied = true;
                    BlastTargetGuard.Apply(harmony, ror2ForGuard);
                }
            }
            if (statUpdateInterval > 0f && !statLimitApplied && IsPlugin(asm))
            {
                var ror2ForLimit = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2ForLimit != null)
                {
                    statLimitApplied = true;
                    StatUpdateLimit.Apply(harmony, ror2ForLimit, statUpdateInterval);
                }
            }
            if (hardEnemyCap > 0 && !hardCapApplied && IsPlugin(asm))
            {
                var ror2ForCap = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2ForCap != null)
                {
                    hardCapApplied = true;
                    HardEnemyCap.Apply(harmony, ror2ForCap, hardEnemyCap);
                }
            }
            if (statsRetryGuard && !statsRetryGuardApplied && IsPlugin(asm))
            {
                var ror2ForStats = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2ForStats != null)
                {
                    statsRetryGuardApplied = true;
                    StatsRetryGuard.Apply(harmony, ror2ForStats);
                }
            }
            if (overlapHitGuard && !overlapHitGuardApplied && IsPlugin(asm))
            {
                var ror2ForOverlap = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2ForOverlap != null)
                {
                    overlapHitGuardApplied = true;
                    OverlapHitGuard.Apply(harmony, ror2ForOverlap);
                }
            }
            if (dotTickGuard && !dotTickGuardApplied && IsPlugin(asm))
            {
                var ror2ForDot = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2ForDot != null)
                {
                    dotTickGuardApplied = true;
                    DotTickGuard.Apply(harmony, ror2ForDot);
                }
            }
            if (bulletHitGuard && !bulletHitGuardApplied && IsPlugin(asm))
            {
                var ror2ForBullet = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2ForBullet != null)
                {
                    bulletHitGuardApplied = true;
                    BulletHitGuard.Apply(harmony, ror2ForBullet);
                }
            }
            if (totemTetherGuard && !totemTetherGuardApplied && IsPlugin(asm))
            {
                var ror2ForTotem = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2ForTotem != null)
                {
                    totemTetherGuardApplied = true;
                    TotemTetherGuard.Apply(harmony, ror2ForTotem);
                }
            }
            if (rouletteSpinCost && !rouletteSpinCostApplied && IsPlugin(asm))
            {
                var ror2ForRoulette = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2ForRoulette != null)
                {
                    rouletteSpinCostApplied = true;
                    RouletteSpinCost.Apply(harmony, ror2ForRoulette);
                }
            }
            if (acolyteAttackSpeed != 1f && !acolyteTweakApplied && IsPlugin(asm))
            {
                var ror2ForAcolyte = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2ForAcolyte != null)
                {
                    acolyteTweakApplied = true;
                    AcolyteTweak.Apply(ror2ForAcolyte, acolyteAttackSpeed);
                }
            }
            if (nullDccsPoolEntryFix && !nullDccsPoolEntryFixApplied && IsPlugin(asm))
            {
                var ror2ForPools = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2ForPools != null)
                {
                    nullDccsPoolEntryFixApplied = true;
                    NullDccsPoolEntryFix.Apply(harmony, ror2ForPools);
                }
            }

            if (disableGenetics && !disableGeneticsApplied && IsPlugin(asm))
            {
                var ror2ForArtifacts = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2ForArtifacts != null)
                {
                    disableGeneticsApplied = true;
                    ArtifactDisabler.Apply(harmony, ror2ForArtifacts);
                }
            }
            if (hypercritNullFix && name == "NotHypercrit" && appliedFixes.Add(name))
                HypercritNullFix.Apply(harmony, asm);
            if (gasolineFix && !gasolineApplied && IsPlugin(asm))
            {
                var ror2 = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2 != null)
                {
                    gasolineApplied = true;
                    GasolineChainFix.Apply(harmony, ror2);
                }
            }
            if (flowerRootFix && !flowerRootApplied && IsPlugin(asm))
            {
                var ror2 = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2 != null)
                {
                    flowerRootApplied = true;
                    FlowerRootFix.Apply(harmony, ror2);
                }
            }
            if (stunDurationFix && !stunDurationApplied && IsPlugin(asm))
            {
                var ror2 = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2 != null)
                {
                    stunDurationApplied = true;
                    StunDurationFix.Apply(harmony, ror2);
                }
            }
            if (SpawnTweakWanted && !spawnTweakApplied && IsPlugin(asm))
            {
                var ror2 = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2 != null)
                {
                    spawnTweakApplied = true;
                    SpawnCreditTweak.Apply(harmony, ror2, spawnCredits);
                }
            }
            if (InteractableTweakWanted && !interactableTweakApplied && IsPlugin(asm))
            {
                var ror2 = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2 != null)
                {
                    interactableTweakApplied = true;
                    InteractableWeightTweak.Apply(ror2, printerWeight, scrapperWeight, combinerWeight);
                    DroneTweak.Apply(ror2, droneNoMinStage, droneSpawnWeight, droneHealth);
                }
            }
            if (SacrificeTweakWanted && !sacrificeApplied && IsPlugin(asm))
            {
                var ror2 = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2 != null)
                {
                    sacrificeApplied = true;
                    SacrificeDropTweak.Apply(harmony, ror2, sacrificeDropMult);
                }
            }
            if (SacrificeKeepWanted && !sacrificeKeepApplied && IsPlugin(asm))
            {
                var ror2 = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                if (ror2 != null)
                {
                    sacrificeKeepApplied = true;
                    SacrificeKeepTweak.Apply(harmony, ror2, sacrificeKeepList, sacrificeKeptWeight);
                }
            }
        }
    }

    /// <summary>
    /// When a game update changes a method's signature, MonoMod rejects old On.* hooks with
    /// "Parameter ... of hook for X doesn't match". That exception escapes into the mod's startup or
    /// run setup and aborts everything after it. Skip just that hook instead, and ignore the
    /// "Stack empty" the mod later hits when it removes the hook that was never added.
    /// </summary>
    static class HookShield
    {
        static readonly HashSet<string> reported = new HashSet<string>();

        public static void Apply(Harmony h)
        {
            harmonyRef = h;
            Patch(h, "Add", nameof(AddFinalizer));
            Patch(h, "Modify", nameof(AddFinalizer));
            Patch(h, "Remove", nameof(RemoveFinalizer));
            Patch(h, "Unmodify", nameof(RemoveFinalizer));
        }

        static void Patch(Harmony h, string name, string finalizer)
        {
            var target = typeof(HookEndpointManager).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == name && !m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
            var fin = typeof(HookShield).GetMethod(finalizer, BindingFlags.NonPublic | BindingFlags.Static);
            h.Patch(target, finalizer: new HarmonyMethod(fin));
        }

        internal static Harmony harmonyRef;

        static Exception AddFinalizer(Exception __exception, object[] __args)
        {
            if (__exception is InvalidOperationException && __exception.Message.Contains("of hook for")
                && __exception.Message.Contains("doesn't match"))
            {
                Report("Skipped an outdated hook so the mod can keep loading", __args, __exception.Message);
                // TinkersSatchel's skipped hook is how its item names/descriptions get into the game's text:
                // re-attach that work to the game's new SetCurrentLanguage(string, bool).
                var method = __args.Length > 0 ? __args[0] as MethodBase : null;
                if (CompatShieldPatcher.TinkersLanguageFixEnabled && OwnerOf(__args) == "TinkersSatchel"
                    && method?.Name == "SetCurrentLanguage" && method.DeclaringType?.FullName == "RoR2.Language")
                    TinkersLanguageFix.OnTinkersLanguageHookSkipped(harmonyRef);
                return null;
            }
            // The mod was compiled against a hook type (On.*.orig_X / hook_X) that the game update renamed away.
            // Look through invocation wrappers: errors raised in OnAdd handlers arrive as TargetInvocationException.
            var inner = Innermost(__exception);
            if (inner is TypeLoadException && (inner.Message.Contains("expected class 'orig_")
                || inner.Message.Contains("expected class 'hook_")))
            {
                Report("Skipped a hook whose hook type no longer exists after the game update", __args, inner.Message);
                return null;
            }
            return __exception;
        }

        static Exception RemoveFinalizer(Exception __exception, object[] __args)
        {
            if (__exception is InvalidOperationException && __exception.Message.StartsWith("Stack empty"))
            {
                Report("Ignored removing a hook that was never added", __args, null);
                return null;
            }
            return __exception;
        }

        static Exception Innermost(Exception e)
        {
            while (e is TargetInvocationException tie && tie.InnerException != null)
                e = tie.InnerException;
            return e;
        }

        static string OwnerOf(object[] args)
        {
            var hook = args.Length > 1 ? args[1] as Delegate : null;
            try { return hook?.Method?.DeclaringType?.Assembly.GetName().Name ?? "?"; }
            catch { return "?"; }
        }

        static void Report(string what, object[] args, string detail)
        {
            var method = args.Length > 0 ? args[0] as MethodBase : null;
            string owner = OwnerOf(args);
            string target = method == null ? "?" : $"{method.DeclaringType?.FullName}.{method.Name}";
            if (reported.Add($"{what}|{owner}|{target}"))
                CompatShieldPatcher.Log.LogWarning($"{what}: {owner} -> {target}" + (detail != null ? $" ({detail})" : ""));
        }
    }

    /// <summary>
    /// PaladinMod 2.1.0 crashes in Asset.GatherMaterials because Mercenary's EvisProjectile no longer has a
    /// ProjectileController (Hallowed Concepts update), which aborts Paladin's whole setup. Make every
    /// lookup in that method independent, then fill matMercSwipe from the Eviscerate ghost prefab directly.
    /// </summary>
    static class PaladinFix
    {
        static Type asset;

        public static void Apply(Harmony h, Assembly asm)
        {
            try
            {
                asset = asm.GetType("PaladinMod.Modules.Asset");
                var gather = asset?.GetMethod("GatherMaterials", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (gather == null)
                {
                    CompatShieldPatcher.Log.LogWarning("PaladinMod: GatherMaterials not found (mod updated?), fix not applied");
                    return;
                }
                h.Patch(gather,
                    transpiler: new HarmonyMethod(typeof(SafeStatements).GetMethod(nameof(SafeStatements.Transpiler))),
                    postfix: new HarmonyMethod(typeof(PaladinFix).GetMethod(nameof(Postfix), BindingFlags.NonPublic | BindingFlags.Static)));
                CompatShieldPatcher.Log.LogInfo("PaladinMod: material setup made crash-proof");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("PaladinMod fix failed to apply: " + e);
            }
        }

        static void Postfix()
        {
            var field = AccessTools.Field(asset, "matMercSwipe");
            if (field == null || field.GetValue(null) as Material)
                return;
            var mat = MercSwipeFromGhost();
            field.SetValue(null, mat);
            CompatShieldPatcher.Log.LogInfo(mat
                ? "PaladinMod: matMercSwipe taken from the Eviscerate ghost prefab instead"
                : "PaladinMod: matMercSwipe unavailable; that sword effect may look plain");
        }

        static Material MercSwipeFromGhost()
        {
            foreach (var key in new[] { "RoR2/Base/Merc/EvisProjectileGhost.prefab", "RoR2/Base/Merc/EvisOverlapProjectileGhost.prefab" })
            {
                try
                {
                    var ghost = Addressables.LoadAssetAsync<GameObject>(key).WaitForCompletion();
                    if (!ghost) continue;
                    var baseTf = ghost.transform.Find("Base");
                    var renderer = baseTf ? baseTf.GetComponent<ParticleSystemRenderer>() : null;
                    if (renderer && renderer.sharedMaterial)
                        return UnityEngine.Object.Instantiate(renderer.sharedMaterial);
                }
                catch (Exception e)
                {
                    CompatShieldPatcher.Log.LogDebug($"PaladinMod: {key} not usable: {e.Message}");
                }
            }
            return null;
        }
    }

    /// <summary>
    /// A mod that sets up its items in one loop (TinkersSatchel: SetupConfig / SetupAttributes / SetupBehavior for
    /// every module) stops at the first item that throws. Swap each of those calls for a guarded one, so a broken
    /// item is logged and skipped and the loop carries on with the rest.
    /// </summary>
    public static class ModuleIsolation
    {
        static readonly Dictionary<MethodInfo, string> steps = new Dictionary<MethodInfo, string>();
        static readonly Dictionary<string, MethodInfo> byName = new Dictionary<string, MethodInfo>();
        static string modName = "?";
        static int replaced;

        public static void Apply(Harmony h, string mod, Type moduleType, MethodInfo loop, params string[] stepNames)
        {
            try
            {
                if (moduleType == null || loop == null)
                {
                    CompatShieldPatcher.Log.LogWarning($"{mod}: setup loop not found (mod updated?), item isolation not applied");
                    return;
                }
                modName = mod;
                foreach (var n in stepNames)
                {
                    var m = moduleType.GetMethod(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (m == null) continue;
                    steps[m] = n;
                    byName[n] = m;
                }
                int before = replaced;
                h.Patch(loop, transpiler: new HarmonyMethod(typeof(ModuleIsolation).GetMethod(nameof(Transpiler))));
                CompatShieldPatcher.Log.LogInfo($"{mod}: each item now runs {loop.Name} on its own ({replaced - before} calls guarded)");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError($"{mod} item isolation failed to apply: " + e);
            }
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var safe = typeof(ModuleIsolation).GetMethod(nameof(SafeCall));
            foreach (var ins in instructions)
            {
                if ((ins.opcode == OpCodes.Callvirt || ins.opcode == OpCodes.Call) && ins.operand is MethodInfo mi
                    && steps.TryGetValue(mi, out var step))
                {
                    // stack before: [module]  ->  ldstr step; call SafeCall(object, string)
                    // (reuse the instruction itself so any labels/exception blocks on it stay attached)
                    ins.opcode = OpCodes.Ldstr;
                    ins.operand = step;
                    yield return ins;
                    yield return new CodeInstruction(OpCodes.Call, safe);
                    replaced++;
                    continue;
                }
                yield return ins;
            }
        }

        public static void SafeCall(object module, string step)
        {
            try
            {
                byName[step].Invoke(module, null); // reflection Invoke still dispatches to the item's override
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                CompatShieldPatcher.Log.LogWarning(
                    $"{modName}: {module?.GetType().Name}.{step} failed and was skipped ({inner.GetType().Name}: {inner.Message})");
            }
        }
    }

    /// <summary>
    /// TinkersSatchel 6.0.1's AutoConfigModule.Update calls Language.SetCurrentLanguage(string), which the game
    /// replaced with SetCurrentLanguage(string, bool). The whole method then fails to compile, and the plugin's Update
    /// throws MissingMethodException every frame. That method can't be detoured (detours need it compiled), so take
    /// over its caller, TinkersSatchelPlugin.Update, and do the same work through reflection:
    ///   no active run      -> clear stats/drops dirty flags
    ///   stats dirty        -> RecalculateStats on every living body
    ///   drops dirty        -> Run.OnRuleBookUpdated + Run.BuildDropTable
    ///   language dirty     -> Language.SetCurrentLanguage(current, forceUnload: true)  (old 1-arg behaviour)
    /// </summary>
    static class TinkersAutoConfigFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static FieldInfo statsDirty, dropsDirty, langDirty;
        static Type appType, runType, masterType, languageType;
        static bool reported;

        public static void Apply(Harmony h, Assembly asm)
        {
            try
            {
                var plugin = asm.GetType("ThinkInvisible.TinkersSatchel.TinkersSatchelPlugin");
                var update = plugin?.GetMethod("Update", Any);
                var autoCfg = asm.GetType("ThinkInvisible.TinkersSatchel.AutoConfigModule");
                statsDirty = autoCfg?.GetField("globalStatsDirty", Any);
                dropsDirty = autoCfg?.GetField("globalDropsDirty", Any);
                langDirty = autoCfg?.GetField("globalLanguageDirty", Any);
                if (update == null || statsDirty == null || dropsDirty == null || langDirty == null)
                {
                    CompatShieldPatcher.Log.LogWarning("TinkersSatchel: auto-config members not found (mod updated?), fix not applied");
                    return;
                }
                h.Patch(update, prefix: new HarmonyMethod(typeof(TinkersAutoConfigFix).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static)));
                CompatShieldPatcher.Log.LogInfo("TinkersSatchel: auto-config update now uses the new language API");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("TinkersSatchel auto-config fix failed to apply: " + e);
            }
        }

        static bool Prefix()
        {
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                if (!reported)
                {
                    reported = true;
                    CompatShieldPatcher.Log.LogError("TinkersSatchel auto-config replacement failed (logged once): " + e);
                }
            }
            return false; // never run the original: it can't compile
        }

        static void Tick()
        {
            bool stats = (bool)statsDirty.GetValue(null), drops = (bool)dropsDirty.GetValue(null), lang = (bool)langDirty.GetValue(null);
            if (!stats && !drops && !lang)
                return; // nothing to do (same as the original in this case)
            ResolveGameTypes();
            if (!(bool)appType.GetProperty("loadFinished", Any).GetValue(null, null))
                return; // TinkersSatchelPlugin.Update only calls AutoConfigModule.Update after loading finishes

            var run = runType.GetProperty("instance", Any).GetValue(null, null) as UnityEngine.Object;
            bool runActive = run && ((Behaviour)run).isActiveAndEnabled;
            if (!runActive)
            {
                statsDirty.SetValue(null, false);
                dropsDirty.SetValue(null, false);
            }
            else
            {
                if (stats)
                {
                    statsDirty.SetValue(null, false);
                    var list = (System.Collections.IEnumerable)masterType.GetProperty("readOnlyInstancesList", Any).GetValue(null, null);
                    var masters = list.Cast<object>().ToList(); // copy first, like the original's ToList()
                    foreach (var m in masters)
                    {
                        if (!(bool)masterType.GetProperty("hasBody", Any).GetValue(m, null))
                            continue;
                        var body = masterType.GetMethod("GetBody", Any, null, Type.EmptyTypes, null).Invoke(m, null);
                        var health = body.GetType().GetProperty("healthComponent", Any).GetValue(body, null);
                        if (health != null && (bool)health.GetType().GetProperty("alive", Any).GetValue(health, null))
                            body.GetType().GetMethod("RecalculateStats", Any, null, Type.EmptyTypes, null).Invoke(body, null);
                    }
                }
                if (drops)
                {
                    dropsDirty.SetValue(null, false);
                    var ruleBook = runType.GetField("networkRuleBookComponent", Any).GetValue(run);
                    runType.GetMethod("OnRuleBookUpdated", Any).Invoke(run, new[] { ruleBook });
                    runType.GetMethod("BuildDropTable", Any, null, Type.EmptyTypes, null).Invoke(run, null);
                }
            }
            if (lang)
            {
                langDirty.SetValue(null, false);
                var current = (string)languageType.GetProperty("currentLanguageName", Any).GetValue(null, null);
                languageType.GetMethod("SetCurrentLanguage", Any, null, new[] { typeof(string), typeof(bool) }, null)
                    .Invoke(null, new object[] { current, true });
            }
        }

        static void ResolveGameTypes()
        {
            if (appType != null) return;
            var ror2 = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "RoR2");
            appType = ror2.GetType("RoR2.RoR2Application", true);
            runType = ror2.GetType("RoR2.Run", true);
            masterType = ror2.GetType("RoR2.CharacterMaster", true);
            languageType = ror2.GetType("RoR2.Language", true);
        }
    }

    /// <summary>
    /// TinkersSatchel 6.0.1 renders its item text (TINKERSSATCHEL_..._RENDERED tokens) into R2API language overlays
    /// from a hook on Language.SetCurrentLanguage(string). The game now has SetCurrentLanguage(string, bool), the hook
    /// is skipped, and the names show as raw tokens. Redo that hook's body as a postfix on the new method:
    ///   for each module: RefreshPermanentLanguage(); if enabled: (UninstallLanguage() if installed); InstallLanguage()
    ///   then AutoConfigModule.globalLanguageDirty = false
    /// </summary>
    public static class TinkersLanguageFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static bool applied;
        static FieldInfo allModules, langDirty;
        static MethodInfo refreshPermanent, getEnabled, getLanguageInstalled, uninstallLanguage, installLanguage;
        static readonly HashSet<string> failedModules = new HashSet<string>();

        public static void OnTinkersLanguageHookSkipped(Harmony h)
        {
            if (applied || h == null) return;
            applied = true;
            try
            {
                var tinkers = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "TinkersSatchel");
                var ror2 = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RoR2");
                var target = ror2?.GetType("RoR2.Language")?.GetMethod("SetCurrentLanguage", Any, null,
                    new[] { typeof(string), typeof(bool) }, null);
                if (tinkers == null || target == null || !Init(tinkers))
                {
                    CompatShieldPatcher.Log.LogWarning("TinkersSatchel language fix: expected members not found, not applied");
                    return;
                }
                PatchTarget(h, target);
                CompatShieldPatcher.Log.LogInfo("TinkersSatchel: item names/descriptions now refresh on the game's new language API");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("TinkersSatchel language fix failed to apply: " + e);
            }
        }

        public static bool Init(Assembly tinkers)
        {
            var module = tinkers.GetType("ThinkInvisible.TinkersSatchel.Module");
            var autoCfg = tinkers.GetType("ThinkInvisible.TinkersSatchel.AutoConfigModule");
            if (module == null || autoCfg == null) return false;
            allModules = module.GetField("allModules", Any);
            langDirty = autoCfg.GetField("globalLanguageDirty", Any);
            refreshPermanent = module.GetMethod("RefreshPermanentLanguage", Any, null, Type.EmptyTypes, null);
            getEnabled = module.GetProperty("enabled", Any)?.GetGetMethod(true);
            getLanguageInstalled = module.GetProperty("languageInstalled", Any)?.GetGetMethod(true);
            uninstallLanguage = module.GetMethod("UninstallLanguage", Any, null, Type.EmptyTypes, null);
            installLanguage = module.GetMethod("InstallLanguage", Any, null, Type.EmptyTypes, null);
            return allModules != null && langDirty != null && refreshPermanent != null && getEnabled != null
                && getLanguageInstalled != null && uninstallLanguage != null && installLanguage != null;
        }

        public static void PatchTarget(Harmony h, MethodBase target)
        {
            h.Patch(target, postfix: new HarmonyMethod(typeof(TinkersLanguageFix).GetMethod(nameof(Postfix), BindingFlags.NonPublic | BindingFlags.Static)));
        }

        // ReadOnlyFilingDictionary<Module> implements IEnumerable
        static List<object> Modules() =>
            ((System.Collections.IEnumerable)allModules.GetValue(null))?.Cast<object>().ToList() ?? new List<object>();

        static bool running;  // a module's refresh could set the language again; don't loop

        static void Postfix()
        {
            if (running) return;
            running = true;
            try
            {
                foreach (var m in Modules())
                {
                    try
                    {
                        refreshPermanent.Invoke(m, null);
                        if ((bool)getEnabled.Invoke(m, null))
                        {
                            if ((bool)getLanguageInstalled.Invoke(m, null))
                                uninstallLanguage.Invoke(m, null);
                            installLanguage.Invoke(m, null);
                        }
                    }
                    catch (Exception e)
                    {
                        var name = m.GetType().Name;
                        if (failedModules.Add(name))
                        {
                            var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                            CompatShieldPatcher.Log.LogWarning($"TinkersSatchel: {name} text refresh failed ({inner.GetType().Name}: {inner.Message})");
                        }
                    }
                }
                langDirty.SetValue(null, false);
            }
            catch (Exception e)
            {
                if (failedModules.Add("<all>"))
                    CompatShieldPatcher.Log.LogError("TinkersSatchel language refresh failed: " + e);
            }
            finally
            {
                running = false;
            }
        }
    }

    /// <summary>
    /// Multitudes No Price Scale copies every enemy spawn (Monster Scaling, x4 here) and, with Lift Monster Limit on,
    /// lets every summon ignore the game's 40-enemy limit, so fights snowball (182 enemies at once in one Xi Construct
    /// fight). This caps them at MonsterLimit instead:
    ///   - the game's own check in MasterSummon.Perform (team size vs TeamDef.softCharacterLimit) uses MonsterLimit for
    ///     the monster team; it only runs for normal spawns when Multitudes' Lift Monster Limit is off;
    ///   - Multitudes always spawns its copies with the limit ignored, so SpawnDuplicate is skipped once the team the
    ///     copy would join already has MonsterLimit members.
    /// Spawns the game itself exempts (Xi Construct's weak points, Mithrix's spires, scripted encounters) still happen.
    /// </summary>
    public static class MonsterLimitTweak
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static int limit;
        static object monsterTeam, monsterDef;
        static MethodInfo getTeamDef, getTeamMembers, getLiftSetting;
        static FieldInfo softLimit, teamOverride;
        static bool liftChecked;
        static int skippedCopies;
        static readonly HashSet<string> errors = new HashSet<string>();

        public static void Apply(Harmony h, Assembly ror2, Assembly multitudes, int max)
        {
            try
            {
                var teamIndex = ror2?.GetType("RoR2.TeamIndex");
                getTeamDef = ror2?.GetType("RoR2.TeamCatalog")?.GetMethod("GetTeamDef", Any, null, new[] { teamIndex }, null);
                softLimit = ror2?.GetType("RoR2.TeamDef")?.GetField("softCharacterLimit", Any);
                getTeamMembers = ror2?.GetType("RoR2.TeamComponent")?.GetMethod("GetTeamMembers", Any, null, new[] { teamIndex }, null);
                teamOverride = ror2?.GetType("RoR2.DirectorSpawnRequest")?.GetField("teamIndexOverride", Any);
                var perform = ror2?.GetType("RoR2.MasterSummon")?.GetMethod("Perform", Any, null, Type.EmptyTypes, null);
                var plugin = multitudes.GetType("SnoresvilleMultitudes.MultitudesNoPriceScale");
                var duplicate = plugin?.GetMethod("SpawnDuplicate", Any);
                getLiftSetting = plugin?.GetProperty("ConfigLiftMonsterLimit", Any)?.GetGetMethod(true);
                if (teamIndex == null || getTeamDef == null || softLimit == null || getTeamMembers == null
                    || teamOverride == null || perform == null || duplicate == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Monster limit: expected game/Multitudes members not found, not applied");
                    return;
                }
                limit = max;
                monsterTeam = Enum.Parse(teamIndex, "Monster");
                h.Patch(perform, prefix: new HarmonyMethod(typeof(MonsterLimitTweak).GetMethod(nameof(PerformPrefix), BindingFlags.NonPublic | BindingFlags.Static)));
                h.Patch(duplicate, prefix: new HarmonyMethod(typeof(MonsterLimitTweak).GetMethod(nameof(DuplicatePrefix), BindingFlags.NonPublic | BindingFlags.Static)));
                CompatShieldPatcher.Log.LogInfo($"Multitudes: enemies capped at {max} alive at once (its copies included)");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Monster limit failed to apply: " + e);
            }
        }

        // Another mod can put the limit back before every spawn, so only the first change is logged.
        static bool limitChangeLogged;

        // Before the game's limit check: make the monster team's limit ours. TeamDefs are built once, in TeamCatalog's
        // static constructor, which has run by the time anything is summoned.
        static void PerformPrefix()
        {
            try
            {
                monsterDef = monsterDef ?? getTeamDef.Invoke(null, new[] { monsterTeam });
                if (monsterDef == null) return;
                var old = (int)softLimit.GetValue(monsterDef);
                if (old == limit) return;
                softLimit.SetValue(monsterDef, limit);
                if (limitChangeLogged) return;
                limitChangeLogged = true;
                CompatShieldPatcher.Log.LogInfo($"Game's enemy limit set to {limit} (it was {old})");
            }
            catch (Exception e)
            {
                Once("perform", "Monster limit: couldn't set the game's limit: " + e);
            }
        }

        // Multitudes spawns each copy with the limit ignored: skip the copy once its team is full.
        static bool DuplicatePrefix(object request, ref GameObject __result)
        {
            try
            {
                CheckLiftOnce();
                var team = teamOverride.GetValue(request) ?? monsterTeam;   // boxed TeamIndex?, null when not set
                var members = (System.Collections.ICollection)getTeamMembers.Invoke(null, new[] { team });
                if (members.Count < limit) return true;
                __result = null;
                if (skippedCopies++ == 0)
                    CompatShieldPatcher.Log.LogInfo($"Multitudes: copies paused at {limit} enemies (shown once)");
                return false;
            }
            catch (Exception e)
            {
                Once("duplicate", "Monster limit: couldn't check a Multitudes copy, letting it spawn: " + e);
                return true;
            }
        }

        static void CheckLiftOnce()
        {
            if (liftChecked) return;
            liftChecked = true;
            var entry = getLiftSetting?.Invoke(null, null);
            if (entry?.GetType().GetProperty("Value")?.GetValue(entry) is bool lift && lift)
                CompatShieldPatcher.Log.LogWarning("Multitudes' Lift Monster Limit is on, so normal spawns ignore the limit " +
                    $"and only its copies stop at {limit}. Set Lift Monster Limit = false for the full cap.");
        }

        static void Once(string key, string message)
        {
            if (errors.Add(key)) CompatShieldPatcher.Log.LogError(message + " (logged once)");
        }
    }

    /// <summary>
    /// Spawn speed. A CombatDirector earns credits through money waves that its Awake builds from creditMultiplier, so
    /// scaling creditMultiplier just before Awake makes that spawner earn more and send waves sooner. Only the stage's
    /// regular spawners are scaled: the CombatDirectors on the scene's Director object (next to its SceneDirector or
    /// DirectorCore). Teleporter, shrine, void and special-mode spawners live on other objects and keep their credits.
    /// Mods that adjust creditMultiplier in their own Awake hooks (Inferno, Starstorm 2) run first and get scaled too.
    /// </summary>
    public static class SpawnCreditTweak
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static float multiplier;
        static FieldInfo creditMultiplier;
        static Type sceneDirector, directorCore;
        static int logged;
        static readonly HashSet<string> errors = new HashSet<string>();
        internal static Func<object, bool> IsStageSpawner = OnDirectorObject;   // swapped out by the test harness

        public static void Apply(Harmony h, Assembly ror2, float mult)
        {
            try
            {
                var combat = ror2?.GetType("RoR2.CombatDirector");
                creditMultiplier = combat?.GetField("creditMultiplier", Any);
                sceneDirector = ror2?.GetType("RoR2.SceneDirector");
                directorCore = ror2?.GetType("RoR2.DirectorCore");
                var awake = combat?.GetMethod("Awake", Any, null, Type.EmptyTypes, null);
                if (creditMultiplier == null || awake == null || (sceneDirector == null && directorCore == null))
                {
                    CompatShieldPatcher.Log.LogWarning("Spawn credits: expected game members not found, not applied");
                    return;
                }
                multiplier = mult;
                h.Patch(awake, prefix: new HarmonyMethod(typeof(SpawnCreditTweak).GetMethod(nameof(AwakePrefix), BindingFlags.NonPublic | BindingFlags.Static)));
                CompatShieldPatcher.Log.LogInfo($"Spawns: the stage's regular spawners earn {mult}x credits");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Spawn credit tweak failed to apply: " + e);
            }
        }

        static bool OnDirectorObject(object director)
        {
            var c = director as Component;
            return c != null && ((sceneDirector != null && c.GetComponent(sceneDirector) != null)
                                 || (directorCore != null && c.GetComponent(directorCore) != null));
        }

        static void AwakePrefix(object __instance)
        {
            try
            {
                if (!IsStageSpawner(__instance)) return;
                var before = (float)creditMultiplier.GetValue(__instance);
                creditMultiplier.SetValue(__instance, before * multiplier);
                if (logged++ < 2)   // a stage has two regular spawners: show the first stage's, then stay quiet
                    CompatShieldPatcher.Log.LogInfo($"Spawns: {Describe(__instance)} credits {before:0.##}x -> {before * multiplier:0.##}x");
            }
            catch (Exception e)
            {
                if (errors.Add("awake")) CompatShieldPatcher.Log.LogError("Spawn credits: couldn't scale a spawner (logged once): " + e);
            }
        }

        static string Describe(object director) => director is Component c ? GameObjectName(c) : director.GetType().Name;

        // Kept separate (and never inlined) so callers compile without Unity engine calls; the test harness runs outside Unity.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static string GameObjectName(Component c) => c.gameObject.name;
    }

    /// <summary>
    /// Printer / scrapper frequency. DirectorCardCategorySelection.calcCardWeight is the game's own hook on each card's final
    /// weight (after the per-category normalisation), so scaling it there changes how often a stage pick lands on a printer
    /// or scrapper versus everything else. Cards are matched by spawn card name (iscDuplicator*, iscScrapper); monster cards
    /// pass through untouched. Weights are rebuilt every stage from the stage's asset, so nothing stacks.
    /// </summary>
    public static class InteractableWeightTweak
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static float printer = 1f, scrapper = 1f, combiner = 1f;
        internal static float combinerCategoryFactor = 1f;   // what DroneTweak already multiplied the combiner's category by
        static MethodInfo getSpawnCard;
        static readonly Dictionary<object, float> factor = new Dictionary<object, float>();
        static readonly HashSet<string> seen = new HashSet<string>();
        static bool errorLogged;

        public static void Apply(Assembly ror2, float printerMult, float scrapperMult, float combinerMult)
        {
            try
            {
                var dccs = ror2?.GetType("RoR2.DirectorCardCategorySelection");
                var ev = dccs?.GetEvent("calcCardWeight", Any);
                getSpawnCard = ror2?.GetType("RoR2.DirectorCard")?.GetMethod("GetSpawnCard", Any, null, Type.EmptyTypes, null);
                if (ev == null || getSpawnCard == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Printer/scrapper spawns: expected game members not found, not applied");
                    return;
                }
                printer = printerMult >= 0f ? printerMult : 1f;
                scrapper = scrapperMult >= 0f ? scrapperMult : 1f;
                combiner = combinerMult >= 0f ? combinerMult : 1f;
                var handler = Delegate.CreateDelegate(ev.EventHandlerType, typeof(InteractableWeightTweak).GetMethod(nameof(OnCalc), BindingFlags.NonPublic | BindingFlags.Static));
                ev.AddEventHandler(null, handler);
                CompatShieldPatcher.Log.LogInfo($"Spawns: printers {printer}x, scrappers {scrapper}x, Drone Combiners {combiner}x as likely per stage pick");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Printer/scrapper spawn tweak failed to apply: " + e);
            }
        }

        internal static float FactorFor(string cardName)
        {
            if (string.IsNullOrEmpty(cardName)) return 1f;
            if (DroneTweak.IsCombiner(cardName)) return combinerCategoryFactor > 0f ? combiner / combinerCategoryFactor : combiner;
            if (cardName.IndexOf("Duplicator", StringComparison.OrdinalIgnoreCase) >= 0) return printer;
            if (cardName.IndexOf("Scrapper", StringComparison.OrdinalIgnoreCase) >= 0) return scrapper;
            return 1f;
        }

        static void OnCalc(object card, ref float weight)
        {
            try
            {
                var sc = getSpawnCard.Invoke(card, null);
                if (sc == null) return;
                if (!factor.TryGetValue(sc, out var f))
                {
                    var name = CardName(sc);
                    f = FactorFor(name);
                    factor[sc] = f;
                    if (f != 1f && seen.Add(name)) CompatShieldPatcher.Log.LogInfo($"Spawns: {name} weight x{f}");
                }
                weight *= f;
            }
            catch (Exception e)
            {
                if (!errorLogged) { errorLogged = true; CompatShieldPatcher.Log.LogError("Printer/scrapper spawns: couldn't scale a card (logged once): " + e); }
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static string CardName(object sc) => (sc as UnityEngine.Object)?.name;
    }

    /// <summary>
    /// Drones. SceneDirector.onGenerateInteractableCardSelection hands over a per-stage copy of the stage's categories just before
    /// the weights are built, and before anything on the stage spawns. Scaling the "Drones" category's selectionWeight there
    /// is safe (the struct array is a fresh copy, so nothing stacks). DirectorCard objects are shared with the stage asset, so
    /// minimumStageCompletions = 0 stays for the session, which is what this option wants. Health goes on the body prefabs,
    /// each scaled once: DroneCatalog (bought drones and their Remote Op bodies), the bodies behind any card in the Drones
    /// category (modded drones not in the catalog), and the item-granted drones. Enemy drones are in none of those.
    /// The event fires on the host only, so this is host-side.
    /// </summary>
    public static class DroneTweak
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static readonly string[] ItemDroneBodies = { "BackupDroneBody", "DroneCommanderBody" };
        static bool noMinStage;
        static float spawn = 1f, health = 1f;
        static FieldInfo categories, catName, catWeight, catCards, cardMinStage, spawnPrefab, droneBody, droneRemote, baseHealth, levelHealth, summonMaster, masterBody;
        static MethodInfo getSpawnCard, findBody;
        static PropertyInfo allDrones;
        static Type bodyType, summonType, masterType;
        static bool catalogDone, errorLogged;
        static readonly HashSet<GameObject> scaled = new HashSet<GameObject>();
        static readonly HashSet<string> unlockedLogged = new HashSet<string>();

        internal static bool IsCombiner(string cardName) => cardName != null && cardName.IndexOf("DroneCombiner", StringComparison.OrdinalIgnoreCase) >= 0;

        internal static bool IsDroneCategory(string name) => name != null && name.IndexOf("Drone", StringComparison.OrdinalIgnoreCase) >= 0;

        public static void Apply(Assembly ror2, bool noMinimumStage, float spawnMult, float healthMult)
        {
            try
            {
                noMinStage = noMinimumStage;
                spawn = spawnMult >= 0f ? spawnMult : 1f;
                health = healthMult > 0f ? healthMult : 1f;
                if (!noMinStage && Math.Abs(spawn - 1f) < 0.001f && Math.Abs(health - 1f) < 0.001f) return;
                var dccs = ror2?.GetType("RoR2.DirectorCardCategorySelection");
                var category = ror2?.GetType("RoR2.DirectorCardCategorySelection+Category");
                var card = ror2?.GetType("RoR2.DirectorCard");
                var drone = ror2?.GetType("RoR2.DroneDef");
                bodyType = ror2?.GetType("RoR2.CharacterBody");
                summonType = ror2?.GetType("RoR2.SummonMasterBehavior");
                masterType = ror2?.GetType("RoR2.CharacterMaster");
                categories = dccs?.GetField("categories", Any);
                catName = category?.GetField("name", Any);
                catWeight = category?.GetField("selectionWeight", Any);
                catCards = category?.GetField("cards", Any);
                cardMinStage = card?.GetField("minimumStageCompletions", Any);
                getSpawnCard = card?.GetMethod("GetSpawnCard", Any, null, Type.EmptyTypes, null);
                spawnPrefab = ror2?.GetType("RoR2.SpawnCard")?.GetField("prefab", Any);
                droneBody = drone?.GetField("bodyPrefab", Any);
                droneRemote = drone?.GetField("remoteOpBody", Any);
                baseHealth = bodyType?.GetField("baseMaxHealth", Any);
                levelHealth = bodyType?.GetField("levelMaxHealth", Any);
                summonMaster = summonType?.GetField("masterPrefab", Any);
                masterBody = masterType?.GetField("bodyPrefab", Any);
                allDrones = ror2?.GetType("RoR2.DroneCatalog")?.GetProperty("allDroneDefs", Any);
                findBody = ror2?.GetType("RoR2.BodyCatalog")?.GetMethod("FindBodyPrefab", Any, null, new[] { typeof(string) }, null);
                var ev = ror2?.GetType("RoR2.SceneDirector")?.GetEvent("onGenerateInteractableCardSelection", Any);
                if (ev == null || categories == null || catName == null || catWeight == null || catCards == null || cardMinStage == null || getSpawnCard == null
                    || spawnPrefab == null || droneBody == null || droneRemote == null || baseHealth == null || levelHealth == null
                    || summonMaster == null || masterBody == null || allDrones == null || findBody == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Drones: expected game members not found, not applied");
                    return;
                }
                ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(DroneTweak).GetMethod(nameof(OnGenerate), BindingFlags.NonPublic | BindingFlags.Static)));
                CompatShieldPatcher.Log.LogInfo($"Drones: no minimum stage={noMinStage}, {spawn}x as likely per stage pick, {health}x health");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Drone tweak failed to apply: " + e);
            }
        }

        static void OnGenerate(object director, object selection)
        {
            try
            {
                if (!catalogDone)
                {
                    catalogDone = true;
                    ScaleCatalog();
                }
                var cats = categories.GetValue(selection) as Array;
                if (cats == null) return;
                for (int i = 0; i < cats.Length; i++)
                {
                    var boxed = cats.GetValue(i);
                    var label = catName.GetValue(boxed) as string;
                    bool drones = IsDroneCategory(label);
                    if (drones)
                    {
                        catWeight.SetValue(boxed, (float)catWeight.GetValue(boxed) * spawn);
                        cats.SetValue(boxed, i);
                    }
                    if (!(catCards.GetValue(boxed) is Array cards)) continue;
                    foreach (var card in cards)
                    {
                        if (card == null) continue;
                        NoteCombiner(card, label, drones);
                        if (drones) HandleCard(card);
                    }
                }
            }
            catch (Exception e)
            {
                if (!errorLogged) { errorLogged = true; CompatShieldPatcher.Log.LogError("Drones: couldn't adjust a stage's drones (logged once): " + e); }
            }
        }

        // Runs before HandleCard, so the log shows the stage gate the game set. Inside a drone category the combiner already got
        // DroneSpawnMultiplier; recording that keeps DroneCombinerSpawnMultiplier the total versus the unmodded game.
        static void NoteCombiner(object card, string category, bool inDroneCategory)
        {
            var name = (getSpawnCard.Invoke(card, null) as UnityEngine.Object)?.name;
            if (!IsCombiner(name)) return;
            if (inDroneCategory) InteractableWeightTweak.combinerCategoryFactor = spawn;
            if (unlockedLogged.Add("combiner:" + category))
                CompatShieldPatcher.Log.LogInfo($"Drones: {name} is in category '{category}', needs {cardMinStage.GetValue(card)} stages cleared");
        }

        static void HandleCard(object card)
        {
            var sc = getSpawnCard.Invoke(card, null);
            if (noMinStage && (int)cardMinStage.GetValue(card) > 0)
            {
                var name = (sc as UnityEngine.Object)?.name ?? "a drone card";
                if (unlockedLogged.Add(name)) CompatShieldPatcher.Log.LogInfo($"Drones: {name} can now spawn from stage 1 (needed {cardMinStage.GetValue(card)} stages cleared)");
                cardMinStage.SetValue(card, 0);
            }
            if (Math.Abs(health - 1f) < 0.001f || sc == null) return;
            var broken = spawnPrefab.GetValue(sc) as GameObject;
            var summon = broken != null ? broken.GetComponent(summonType) : null;
            var master = summon != null ? summonMaster.GetValue(summon) as GameObject : null;
            var character = master != null ? master.GetComponent(masterType) : null;
            if (character != null) ScaleBody(masterBody.GetValue(character) as GameObject);
        }

        static void ScaleCatalog()
        {
            if (Math.Abs(health - 1f) < 0.001f) return;
            if (allDrones.GetValue(null, null) is System.Collections.IEnumerable defs)
                foreach (var def in defs)
                {
                    if (def == null) continue;
                    ScaleBody(droneBody.GetValue(def) as GameObject);
                    ScaleBody(droneRemote.GetValue(def) as GameObject);
                }
            foreach (var name in ItemDroneBodies)
                ScaleBody(findBody.Invoke(null, new object[] { name }) as GameObject);
        }

        static void ScaleBody(GameObject prefab)
        {
            if (prefab == null || !scaled.Add(prefab)) return;
            var body = prefab.GetComponent(bodyType);
            if (body == null) return;
            var before = (float)baseHealth.GetValue(body);
            baseHealth.SetValue(body, before * health);
            levelHealth.SetValue(body, (float)levelHealth.GetValue(body) * health);
            CompatShieldPatcher.Log.LogInfo($"Drones: {prefab.name} health {before:0.#} -> {before * health:0.#}");
        }
    }

    /// <summary>
    /// The October 2026 update moved hundreds of game assets to new Addressables paths (RoR2/Base/Beetle/BeetleGuardBody.prefab
    /// is now RoR2/Base/BeetleGuard/BeetleGuardBody.prefab, cloud textures moved to Common/TiledTextures, ...), so mods
    /// loading an old path get an InvalidKeyException and lose that asset. Every key-based Addressables load passes
    /// through AddressablesImpl.EvaluateKey: when it returns a RoR2/... path the catalogs don't have, swap in the asset's
    /// new path, but only when exactly one RoR2/... key with that file name exists. Everything else passes through.
    /// </summary>
    public static class AssetRedirect
    {
        static HashSet<string> known;
        static Dictionary<string, string> byFile;   // lowercased file name -> its only RoR2/ key, null when several
        static readonly Dictionary<string, string> decided = new Dictionary<string, string>();
        static readonly object gate = new object();
        static int logged;
        static bool failed;

        public static void Apply(Harmony h, MethodInfo evaluateKey)
        {
            try
            {
                if (evaluateKey == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Asset redirect: AddressablesImpl.EvaluateKey not found, not applied");
                    return;
                }
                h.Patch(evaluateKey, postfix: new HarmonyMethod(typeof(AssetRedirect).GetMethod(nameof(Postfix), BindingFlags.NonPublic | BindingFlags.Static)));
                CompatShieldPatcher.Log.LogInfo("Assets: moved game asset paths now load from their new location");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Asset redirect failed to apply: " + e);
            }
        }

        public static MethodInfo FindEvaluateKey() =>
            typeof(Addressables).Assembly.GetType("UnityEngine.AddressableAssets.AddressablesImpl")?.GetMethod("EvaluateKey",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(object) }, null);

        /// <summary>Index the catalog keys; the game builds it from its loaded catalogs, the test harness passes real ones.</summary>
        public static void BuildIndex(IEnumerable<object> keys)
        {
            var set = new HashSet<string>();
            var files = new Dictionary<string, string>();
            foreach (var k in keys)
            {
                if (!(k is string s)) continue;
                set.Add(s);
                if (!s.StartsWith("RoR2/", StringComparison.Ordinal)) continue;
                var file = s.Substring(s.LastIndexOf('/') + 1).ToLowerInvariant();
                files[file] = files.ContainsKey(file) ? null : s;
            }
            byFile = files;
            known = set;
            decided.Clear();
        }

        /// <summary>The new path for a moved RoR2/... key, or null to leave the key alone.</summary>
        public static string Redirect(string key)
        {
            if (!key.StartsWith("RoR2/", StringComparison.Ordinal) || known.Contains(key)) return null;
            if (decided.TryGetValue(key, out var cached)) return cached;
            byFile.TryGetValue(key.Substring(key.LastIndexOf('/') + 1).ToLowerInvariant(), out var target);
            decided[key] = target;
            if (target != null && logged++ < 25)
                CompatShieldPatcher.Log.LogInfo($"Assets: '{key}' moved to '{target}'" + (logged == 25 ? " (further moves not logged)" : ""));
            return target;
        }

        static void Postfix(ref object __result)
        {
            if (failed || !(__result is string key) || !key.StartsWith("RoR2/", StringComparison.Ordinal)) return;
            try
            {
                lock (gate)
                {
                    if (known == null)
                    {
                        var keys = CatalogKeys();
                        if (keys.Count == 0) return;   // catalogs not loaded yet; a later load builds the index
                        BuildIndex(keys);
                        CompatShieldPatcher.Log.LogInfo($"Assets: indexed {known.Count} catalog keys for the moved-asset redirect");
                    }
                    var target = Redirect(key);
                    if (target != null) __result = target;
                }
            }
            catch (Exception e)
            {
                failed = true;
                CompatShieldPatcher.Log.LogError("Asset redirect stopped after an error: " + e);
            }
        }

        // Separate and never inlined: touching Addressables runs Unity code, which the test harness (outside Unity) can't.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static List<object> CatalogKeys()
        {
            var keys = new List<object>();
            foreach (var locator in Addressables.ResourceLocators)
                keys.AddRange(locator.Keys);
            return keys;
        }
    }

    /// <summary>
    /// AudioOverlapFix drops a sound event that's posted again in the same frame, keyed by event ID only, and that
    /// includes Stop events. When several objects stop the same looping sound in one frame (Multitudes' copies of an
    /// enemy, everything at run end), only the first stop goes through. Wwise keeps a loop playing even after its object
    /// is gone, so the rest loop until the game restarts. Stops can't make anything louder: always let them through.
    /// Name-based posts are checked by name; ID-based posts by the IDs Wwise hands out for "Stop_..." names.
    /// </summary>
    public static class StopSoundFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static readonly HashSet<uint> stopIds = new HashSet<uint>();
        static int passed;

        public static void Apply(Harmony h, Assembly audioOverlapFix, Assembly wwise)
        {
            try
            {
                var byName = audioOverlapFix.GetType("AudioOverlapFix.SoundEnginePatcher")?.GetMethod("shouldPostEvent", Any, null, new[] { typeof(string) }, null);
                var byId = audioOverlapFix.GetType("AudioOverlapFix.Main")?.GetMethod("tryPlaySound", Any, null, new[] { typeof(uint) }, null);
                var getId = wwise?.GetType("AkSoundEngine")?.GetMethod("GetIDFromString", Any, null, new[] { typeof(string) }, null);
                if (byName == null || byId == null || getId == null)
                {
                    CompatShieldPatcher.Log.LogWarning("AudioOverlapFix stop fix: expected members not found, not applied");
                    return;
                }
                h.Patch(getId, postfix: Method(nameof(RecordStopId)));
                h.Patch(byName, prefix: Method(nameof(ByNamePrefix)));
                h.Patch(byId, prefix: Method(nameof(ByIdPrefix)));
                CompatShieldPatcher.Log.LogInfo("AudioOverlapFix: stop events always go through now, so looping sounds can end");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("AudioOverlapFix stop fix failed to apply: " + e);
            }
        }

        static HarmonyMethod Method(string name) => new HarmonyMethod(typeof(StopSoundFix).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));

        static bool IsStop(string eventName) => eventName != null && eventName.StartsWith("Stop_", StringComparison.OrdinalIgnoreCase);

        static void RecordStopId(string in_pszString, uint __result)
        {
            if (IsStop(in_pszString))
                lock (stopIds) stopIds.Add(__result);
        }

        static bool ByNamePrefix(string eventName, ref bool __result)
        {
            if (!IsStop(eventName)) return true;
            __result = true;
            Noted();
            return false;
        }

        static bool ByIdPrefix(uint eventID, ref bool __result)
        {
            bool stop;
            lock (stopIds) stop = stopIds.Contains(eventID);
            if (!stop) return true;
            __result = true;
            Noted();
            return false;
        }

        static void Noted()
        {
            if (passed++ == 0) CompatShieldPatcher.Log.LogInfo("AudioOverlapFix: first stop event let through (shown once)");
        }
    }

    /// <summary>
    /// ZetAspects 2.10.9, DropHooks.EarlyDropChanceHook (an On.GlobalEventManager.OnCharacterDeath hook): for an elite
    /// victim with no EquipmentSlot it falls back to inventory.currentEquipmentIndex, and if that's an aspect it then
    /// looks up victimBody.equipmentSlot.equipmentIndex anyway (line 412), on the slot it just found missing. That throws
    /// before the hook calls orig, so the game's own on-kill handling (and every OnCharacterDeath hook deeper in the
    /// chain) is skipped for that kill. Load the currentEquipmentIndex local it just stored instead.
    /// </summary>
    public static class ZetAspectsDropFix
    {
        static bool matched, alreadyFixed;

        public static MethodInfo FindHook(Assembly zetAspects)
        {
            try
            {
                var closure = zetAspects.GetType("TPDespair.ZetAspects.DropHooks+<>c");
                return closure?.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .FirstOrDefault(m => m.Name.StartsWith("<EarlyDropChanceHook>") && m.GetParameters().Length == 3);
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("ZetAspects drop fix: couldn't look up the hook: " + e);
                return null;
            }
        }

        public static void Apply(Harmony h, MethodInfo hook)
        {
            if (hook == null)
            {
                CompatShieldPatcher.Log.LogWarning("ZetAspects drop fix: EarlyDropChanceHook not found, not applied");
                return;
            }
            try
            {
                matched = alreadyFixed = false;
                h.Patch(hook, transpiler: new HarmonyMethod(typeof(ZetAspectsDropFix).GetMethod(nameof(Transpiler), BindingFlags.NonPublic | BindingFlags.Static)));
                if (matched)
                    CompatShieldPatcher.Log.LogInfo("ZetAspects: elite kills without an equipment slot no longer crash its drop hook");
                else if (alreadyFixed)
                    CompatShieldPatcher.Log.LogInfo("ZetAspects: this version already fixes the slotless-elite kill crash, nothing to change");
                else
                    CompatShieldPatcher.Log.LogWarning("ZetAspects drop fix: unexpected IL (maybe a newer ZetAspects), left unchanged");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("ZetAspects drop fix failed to apply: " + e);
            }
        }

        static bool Calls(CodeInstruction ci, string type, string method) =>
            (ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && ci.operand is MethodInfo m
            && m.Name == method && m.DeclaringType?.Name == type;

        static bool IsSimpleLoad(CodeInstruction ci) =>
            ci.opcode == OpCodes.Ldloc_0 || ci.opcode == OpCodes.Ldloc_1 || ci.opcode == OpCodes.Ldloc_2 || ci.opcode == OpCodes.Ldloc_3
            || ci.opcode == OpCodes.Ldloc_S || ci.opcode == OpCodes.Ldloc || ci.opcode == OpCodes.Ldarg_S || ci.opcode == OpCodes.Ldarg
            || ci.opcode == OpCodes.Ldarg_0 || ci.opcode == OpCodes.Ldarg_1 || ci.opcode == OpCodes.Ldarg_2 || ci.opcode == OpCodes.Ldarg_3;

        static bool SameLocal(object a, object b) =>
            Equals(a, b) || (a is LocalBuilder la && b is LocalBuilder lb && la.LocalIndex == lb.LocalIndex);

        // The ldloc that reads back what a stloc wrote, or null for anything else.
        static (OpCode op, object operand)? LoadFor(CodeInstruction st)
        {
            if (st.opcode == OpCodes.Stloc_0) return (OpCodes.Ldloc_0, null);
            if (st.opcode == OpCodes.Stloc_1) return (OpCodes.Ldloc_1, null);
            if (st.opcode == OpCodes.Stloc_2) return (OpCodes.Ldloc_2, null);
            if (st.opcode == OpCodes.Stloc_3) return (OpCodes.Ldloc_3, null);
            if (st.opcode == OpCodes.Stloc_S) return (OpCodes.Ldloc_S, st.operand);
            if (st.opcode == OpCodes.Stloc) return (OpCodes.Ldloc, st.operand);
            return null;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            int fixedAt = -1;
            for (int i = 0; i + 1 < code.Count && fixedAt < 0; i++)
            {
                if (!Calls(code[i], "Inventory", "get_currentEquipmentIndex")) continue;
                var load = LoadFor(code[i + 1]);
                if (load == null) break;
                // The buggy re-read: "<body>.equipmentSlot.equipmentIndex" fed straight into GetEquipmentDef, right after
                // the store (11 instructions in 2.10.9). Anything farther or shaped differently is left alone.
                for (int j = i + 2; j + 3 < code.Count && j <= i + 22; j++)
                {
                    // Fixed upstream (ZetAspects 2.10.10): the stored index goes straight into GetEquipmentDef.
                    if (code[j].opcode == load.Value.op && SameLocal(code[j].operand, load.Value.operand)
                        && Calls(code[j + 1], "EquipmentCatalog", "GetEquipmentDef"))
                    {
                        alreadyFixed = true;
                        break;
                    }
                    if (!IsSimpleLoad(code[j]) || !Calls(code[j + 1], "CharacterBody", "get_equipmentSlot")
                        || !Calls(code[j + 2], "EquipmentSlot", "get_equipmentIndex")
                        || !Calls(code[j + 3], "EquipmentCatalog", "GetEquipmentDef")) continue;
                    // Same stack effect (push one EquipmentIndex); mutate in place to keep labels.
                    code[j].opcode = load.Value.op;
                    code[j].operand = load.Value.operand;
                    code[j + 1].opcode = OpCodes.Nop; code[j + 1].operand = null;
                    code[j + 2].opcode = OpCodes.Nop; code[j + 2].operand = null;
                    fixedAt = j;
                    break;
                }
            }
            matched = fixedAt >= 0;   // Apply logs the outcome once Harmony has actually applied the patch
            return code;
        }
    }

    /// <summary>
    /// PrintToInventory 1.3.1's Awake returns early when ShareSuite is installed ("ShareSuite detected! PrintToInventory
    /// will be disabled"), because both would hand out printer and cauldron items (ShareSuite's PrinterCauldronFix). Its
    /// scrapper hook (an IL hook on ScrappingToIdle.OnEnter that gives the queued scrap straight to the interactor) has no
    /// ShareSuite counterpart: ShareSuite only hooks GenericPickupController and shop purchases. So feed the check false,
    /// and once Awake has read its config, switch everything except scrappers back off.
    /// </summary>
    public static class PrintToInventoryScrapperFix
    {
        const string ShareSuiteGuid = "com.funkfrog_sipondo.sharesuite";
        const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        static readonly string[] keptOff = { "affectPrinters", "affectCauldrons", "affectCleanse", "affectWanderingChef" };
        static bool matched, shareSuitePresent, announced;
        static Type plugin;

        public static void Apply(Harmony h, Assembly asm)
        {
            try
            {
                plugin = asm.GetType("PrintToInventory.PrintToInventory");
                var awake = plugin?.GetMethod("Awake", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (awake == null || plugin.GetField("affectScrappers", Static) == null || keptOff.Any(f => plugin.GetField(f, Static) == null))
                {
                    CompatShieldPatcher.Log.LogWarning("PrintToInventory scrapper fix: expected members not found, not applied");
                    return;
                }
                matched = false;
                h.Patch(awake, transpiler: Method(nameof(Transpiler)), finalizer: Method(nameof(Finalizer)));
                if (!matched)
                    CompatShieldPatcher.Log.LogWarning("PrintToInventory scrapper fix: unexpected IL (maybe a newer PrintToInventory), left unchanged");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("PrintToInventory scrapper fix failed to apply: " + e);
            }
        }

        static HarmonyMethod Method(string name) => new HarmonyMethod(typeof(PrintToInventoryScrapperFix).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));

        // "Chainloader.PluginInfos.ContainsKey(ShareSuiteGuid)": pass its result through ShareSuiteGate.
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            for (int i = 0; i + 1 < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Ldstr || !(code[i].operand is string s) || s != ShareSuiteGuid) continue;
                if ((code[i + 1].opcode == OpCodes.Call || code[i + 1].opcode == OpCodes.Callvirt)
                    && code[i + 1].operand is MethodInfo m && m.Name == "ContainsKey")
                {
                    code.Insert(i + 2, new CodeInstruction(OpCodes.Call, typeof(PrintToInventoryScrapperFix).GetMethod(nameof(ShareSuiteGate), BindingFlags.NonPublic | BindingFlags.Static)));
                    matched = true;
                }
                break;
            }
            return code;
        }

        static bool ShareSuiteGate(bool installed)
        {
            shareSuitePresent = installed;
            return false;
        }

        // A finalizer, so the other interactables go back off even if the rest of Awake throws. Never throws itself, and
        // returns nothing, so an exception from Awake passes on with its own stack trace.
        static void Finalizer()
        {
            try
            {
                if (!shareSuitePresent) return;
                foreach (var f in keptOff) plugin.GetField(f, Static).SetValue(null, false);
                if (!announced)
                {
                    announced = true;
                    bool scrappers = (bool)plugin.GetField("affectScrappers", Static).GetValue(null);
                    CompatShieldPatcher.Log.LogInfo(scrappers
                        ? "PrintToInventory: scrappers now put scrap straight into the player's inventory; printers, cauldrons, cleansing pools and the Wandering Chef stay as ShareSuite handles them"
                        : "PrintToInventory: loaded alongside ShareSuite, but its own config has Scrapper = false, so nothing changes");
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Transpiler helper for the guards below: wrap a method's code from <c>start</c> to its end in
    /// try { ... } catch (T) { handler(exception, key) }. Only that one exception type is ever caught; anything else
    /// (and anything thrown before <c>start</c>) is never touched, so it keeps its own stack trace and no CompatShield code
    /// runs while it unwinds. Harmony finalizers can't do that: one that may swallow an exception has to return it
    /// otherwise, and Harmony then rethrows it with "throw ex", which restarts its stack trace at the guarded method
    /// (that hid where a stack overflow really came from).
    /// </summary>
    static class CatchWrap
    {
        // Labels are handled as boxed objects: the netstandard2.1 build can't name Harmony's Label type (CS7069).
        static readonly FieldInfo labelsField = typeof(CodeInstruction).GetField("labels");
        static System.Collections.IList LabelsOf(CodeInstruction ci) => (System.Collections.IList)labelsField.GetValue(ci);

        static IEnumerable<object> Targets(CodeInstruction ci)
        {
            var t = ci.operand?.GetType();
            if (t == null) yield break;
            if (t.FullName == "System.Reflection.Emit.Label") yield return ci.operand;
            else if (t.IsArray && t.GetElementType()?.FullName == "System.Reflection.Emit.Label")
                foreach (var o in (Array)ci.operand) yield return o;
        }

        /// <summary>Wraps code[start..] in place. Returns null on success, else why it left the code alone.</summary>
        public static string Wrap(List<CodeInstruction> code, int start, ILGenerator generator, MethodBase method,
            Type catchType, string key, MethodInfo handler)
        {
            if (method is MethodInfo mi && mi.ReturnType != typeof(void)) return "a non-void method";
            if (start < 0 || start >= code.Count || code.Skip(start).All(ci => ci.opcode == OpCodes.Nop || ci.opcode == OpCodes.Ret))
                return "no code to guard";
            // Nothing may jump into the region from before it or back out of it. Exception handling already inside it (a
            // foreach's try/finally) is fine: it nests inside the new try, and its leaves stay inside the region.
            var before = new HashSet<object>(code.Take(start).SelectMany(ci => LabelsOf(ci).Cast<object>()));
            var inside = new HashSet<object>(code.Skip(start).SelectMany(ci => LabelsOf(ci).Cast<object>()));
            for (int i = 0; i < code.Count; i++)
            {
                foreach (var t in Targets(code[i]))
                {
                    if (i >= start && before.Contains(t)) return "a jump back out of the guarded code";
                    if (i < start && inside.Contains(t)) return "a jump into the guarded code";
                }
            }
            object end = generator.DefineLabel();
            for (int i = start; i < code.Count; i++)
                if (code[i].opcode == OpCodes.Ret) { code[i].opcode = OpCodes.Leave; code[i].operand = end; }   // labels stay
            var last = code[code.Count - 1].opcode;
            if (last != OpCodes.Leave && last != OpCodes.Throw && last != OpCodes.Br && last != OpCodes.Br_S)
                code.Add(new CodeInstruction(OpCodes.Leave, end));
            // The try opens on a fresh nop, so a jump to the first guarded instruction lands inside the try.
            var open = new CodeInstruction(OpCodes.Nop);
            open.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
            code.Insert(start, open);
            var ldKey = new CodeInstruction(OpCodes.Ldstr, key);            // catch: (exception) -> (exception, key)
            ldKey.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginCatchBlock, catchType));
            var call = new CodeInstruction(OpCodes.Call, handler);
            call.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
            var ret = new CodeInstruction(OpCodes.Ret);
            LabelsOf(ret).Add(end);
            code.Add(ldKey);
            code.Add(call);
            code.Add(ret);
            return null;
        }

        /// <summary>Wraps code[start..end] (inclusive) in place as try { ... } catch (T) { load...; handler(exception, loaded...) } and carries on with the next
        /// instruction, so it can guard a stretch in the middle of a loop. The stretch must be straight-line code that starts and ends with an empty stack.
        /// Returns null on success, else why it left the code alone.</summary>
        public static string WrapRange(List<CodeInstruction> code, int start, int end, ILGenerator generator, Type catchType, IEnumerable<CodeInstruction> load, MethodInfo handler)
        {
            if (start < 0 || end < start || end + 1 >= code.Count) return "no code to guard";
            for (int i = start; i <= end; i++)
            {
                if (LabelsOf(code[i]).Count > 0) return "a jump into the guarded code";
                if (code[i].blocks.Count > 0) return "exception handling inside the guarded code";
                if (Targets(code[i]).Any() || code[i].opcode == OpCodes.Ret) return "a jump out of the guarded code";
            }
            object after = generator.DefineLabel();
            LabelsOf(code[end + 1]).Add(after);
            var catchCode = load.ToList();
            catchCode.Add(new CodeInstruction(OpCodes.Call, handler));
            catchCode[0].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginCatchBlock, catchType));
            catchCode[catchCode.Count - 1].blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
            catchCode.Insert(0, new CodeInstruction(OpCodes.Leave, after));
            code.InsertRange(end + 1, catchCode);
            var open = new CodeInstruction(OpCodes.Nop);
            open.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
            code.Insert(start, open);
            return null;
        }
    }

    /// <summary>
    /// A mod's event handler that throws aborts the whole event: when one onCharacterDeathGlobal subscriber crashes,
    /// every subscriber after it (and the rest of the game's death handling) misses that kill. For handlers with a
    /// known, harmless crash, catch exactly that exception type in exactly that method (a try/catch wrapped around its
    /// body) and log it once; anything else propagates untouched.
    /// Starstorm 2 0.6.46 StrangeCan.OnCharacterDeathGlobal: counts Intoxicated stacks per attacker, skips adding an
    /// attacker whose object is destroyed, then indexes the dictionary with it anyway (KeyNotFoundException, key 'null').
    /// </summary>
    public static class HandlerGuard
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static readonly Dictionary<string, (Type exception, string note)> guarded = new Dictionary<string, (Type, string)>();
        static readonly HashSet<string> noted = new HashSet<string>();
        static string why;

        static string Key(MethodBase m) => m.DeclaringType?.FullName + "::" + m.Name;

        public static void Apply(Harmony h, Assembly asm, string typeName, string methodName, Type exception, string note)
        {
            try
            {
                var method = asm.GetType(typeName)?.GetMethods(Any).FirstOrDefault(m => m.Name == methodName);
                if (method == null)
                {
                    CompatShieldPatcher.Log.LogWarning($"Handler guard: {typeName}.{methodName} not found, not applied");
                    return;
                }
                guarded[Key(method)] = (exception, note);
                why = "unexpected IL";
                h.Patch(method, transpiler: new HarmonyMethod(typeof(HandlerGuard).GetMethod(nameof(Transpiler), BindingFlags.NonPublic | BindingFlags.Static)));
                if (why == null)
                    CompatShieldPatcher.Log.LogInfo($"Handler guard: {typeName}.{methodName} can no longer abort the event it handles");
                else
                    CompatShieldPatcher.Log.LogWarning($"Handler guard: {why} in {typeName}.{methodName}, guard inactive");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError($"Handler guard for {typeName}.{methodName} failed to apply: " + e);
            }
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod)
        {
            var code = instructions.ToList();
            if (__originalMethod == null || !guarded.TryGetValue(Key(__originalMethod), out var g)) { why = "not a guarded method"; return code; }
            why = CatchWrap.Wrap(code, 0, generator, __originalMethod, g.exception, Key(__originalMethod),
                typeof(HandlerGuard).GetMethod(nameof(Caught), BindingFlags.Public | BindingFlags.Static));
            return code;   // Wrap checks everything before it changes anything, so a refusal leaves it untouched
        }

        /// <summary>The catch handler. Logs once per method; never throws.</summary>
        public static void Caught(Exception e, string key)
        {
            try
            {
                if (noted.Add(key) && guarded.TryGetValue(key, out var g))
                    CompatShieldPatcher.Log.LogInfo($"Handler guard: {g.note} (shown once)");
            }
            catch { }
        }
    }

    /// <summary>
    /// For an On. hook that calls orig first and then crashes in its own code afterwards: catch that one exception type,
    /// but only in the hook's own code after orig, never what comes out of orig (another mod deeper in the chain, which
    /// must still propagate untouched). Everything after the first orig_*.Invoke is wrapped in try/catch (CatchWrap).
    /// GeneticsArtifact 4.5.3 GeneEngineDriver.HealthComponent_TakeDamage: applies the damage via orig, then throws a
    /// NullReferenceException in its gene tracking. The exception flies up through every hook wrapping it and the
    /// caller, so a blast stops at its first target and outer hooks never finish (TinkersSatchel's Defib stack is left
    /// dirty: "HealingSourceStack was not empty on new frame").
    /// </summary>
    public static class AfterOrigGuard
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static readonly Dictionary<string, (Type exception, string note)> guarded = new Dictionary<string, (Type, string)>();
        static readonly HashSet<string> noted = new HashSet<string>();
        static string why;

        static string Key(MethodBase m) => m.DeclaringType?.FullName + "::" + m.Name;

        public static void Apply(Harmony h, Assembly asm, string typeName, string methodName, Type exception, string note)
        {
            try
            {
                var method = asm.GetType(typeName)?.GetMethods(Any).FirstOrDefault(m => m.Name == methodName);
                if (method == null)
                {
                    CompatShieldPatcher.Log.LogWarning($"After-orig guard: {typeName}.{methodName} not found, not applied");
                    return;
                }
                guarded[Key(method)] = (exception, note);
                why = "unexpected IL";
                h.Patch(method, transpiler: new HarmonyMethod(typeof(AfterOrigGuard).GetMethod(nameof(Transpiler), BindingFlags.NonPublic | BindingFlags.Static)));
                if (why == null)
                    CompatShieldPatcher.Log.LogInfo($"After-orig guard: {typeName}.{methodName} can no longer break the damage chain after it ran");
                else
                    CompatShieldPatcher.Log.LogWarning($"After-orig guard: {why} in {typeName}.{methodName}, guard inactive");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError($"After-orig guard for {typeName}.{methodName} failed to apply: " + e);
            }
        }

        static bool IsOrigInvoke(CodeInstruction ci) =>
            (ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) && ci.operand is MethodInfo m
            && m.Name == "Invoke" && m.DeclaringType != null && m.DeclaringType.Name.StartsWith("orig_");

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod)
        {
            var code = instructions.ToList();
            if (__originalMethod == null || !guarded.TryGetValue(Key(__originalMethod), out var g)) { why = "not a guarded method"; return code; }
            int call = code.FindIndex(IsOrigInvoke);
            if (call < 0) { why = "no orig call found"; return code; }
            if (((MethodInfo)code[call].operand).ReturnType != typeof(void)) { why = "an orig that returns a value"; return code; }
            why = CatchWrap.Wrap(code, call + 1, generator, __originalMethod, g.exception, Key(__originalMethod),
                typeof(AfterOrigGuard).GetMethod(nameof(Caught), BindingFlags.Public | BindingFlags.Static));
            return code;   // Wrap checks everything before it changes anything, so a refusal leaves it untouched
        }

        /// <summary>The catch handler: the hook's own crash after orig. Logs once per hook; never throws.</summary>
        public static void Caught(Exception e, string key)
        {
            try
            {
                if (noted.Add(key) && guarded.TryGetValue(key, out var g))
                    CompatShieldPatcher.Log.LogInfo($"After-orig guard: {g.note} (shown once)");
            }
            catch { }
        }
    }

    /// <summary>
    /// A hit nested inside a hit (an on-hit or on-death effect dealing damage from inside the damage it reacts to) runs the whole
    /// TakeDamage chain again, nested. This pack runs 45 hooks on HealthComponent.TakeDamage, 66 on TakeDamageProcess, 34 on
    /// ProcessHitEnemy and 23 on OnHitEnemy: 260 stack frames per hit (measured 2026-10-19). A few links of a kill chain (a blast
    /// kills something whose death blasts...) overflow Mono's 1 MB main-thread stack: StackOverflowException, fail-fast
    /// 0xC0000409, game gone (2026-10-19 20:41 local, right after two player deaths in a row). GasolineChainFix flattens
    /// Gasoline's own chain; this flattens every path: a TakeDamage call nested more than MaxDepth deep is queued, and when the
    /// outermost hit returns the queue runs one hit at a time through TakeDamageProcess (the outer TakeDamage hooks already ran
    /// for it), in the same frame, so every victim is still there and the stack holds about MaxDepth links at most (a replay
    /// starts as link 1 because the outermost hit's outer hooks are still under it). What changes for a hit beyond depth 3: the
    /// outer TakeDamage hooks' after-the-hit logic ran before the damage (a gold-share or shield-break effect tied to that one hit
    /// is skipped) and its on-hit procs fire before the damage lands; nothing is doubled, and only a target destroyed in the
    /// meantime is dropped. Damage is server-side, so the host's copy is the one that matters.
    /// 2026-11-08: a proc storm queued 4096 hits behind one hit and the same-frame drain froze the game for 12-17 s (about 25,000
    /// TakeDamage calls at ~0.5 ms each). The line is now limited by hits still waiting (DamageChainQueueLimit) and by time per
    /// physics tick (DamageChainDrainMs). What doesn't fit runs on later ticks, up to a few seconds late at worst.
    /// </summary>
    public static class DamageChainGuard
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        public const int MaxDepth = 3;       // TakeDamage calls allowed on the stack at once (~260 frames each)
        internal const int DefaultQueueLimit = 256;
        internal const float DefaultDrainMs = 4f;
        internal const int MaxBacklogHits = 1024;   // hits one backlog may take in; ends a chain whose replays keep refilling the line
        const int MaxBacklogLines = 200;     // one log line per backlog; a long run can't flood the log
        internal static int QueueLimit = DefaultQueueLimit;
        internal static double DrainBudgetMs = DefaultDrainMs;   // waiting-hit time allowed per physics tick; 0 = no limit
        static int depth, deferredTotal, dropped, replayFailures;
        static int head;                     // queued[head..] are still waiting; earlier entries already ran
        static int carriedTicks, peakWaiting, droppedThisBacklog, acceptedThisBacklog, backlogLines;
        static double spentMs;               // drain time used this physics tick, plus any overrun carried from the last one
        static bool draining;
        static readonly List<object[]> queued = new List<object[]>();
        internal static Action<object, object> Replay;                                           // (HealthComponent, DamageInfo) -> TakeDamageProcess; the harness swaps in a recorder
        internal static Func<object, bool> IsGone = o => o is UnityEngine.Object u && u == null;  // the harness swaps in a stub
        internal static int Depth => depth;
        internal static int Deferred => deferredTotal;
        internal static int QueuedCount => queued.Count - head;
        internal static Func<double> NowMs = () => System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 / System.Diagnostics.Stopwatch.Frequency;   // the harness swaps in a hand-driven clock

        /// <summary>Takes the limits from [4. Lag and Hang Protection]. Runs on the assembly-load path, so it must not touch Unity.</summary>
        internal static void Configure(int queueLimit, float drainMs)
        {
            QueueLimit = Math.Max(1, queueLimit);
            DrainBudgetMs = Math.Max(0f, drainMs);
        }

        public static void Apply(Harmony h, Assembly ror2)
        {
            try
            {
                var hc = ror2.GetType("RoR2.HealthComponent");
                var damageInfo = ror2.GetType("RoR2.DamageInfo");
                var take = damageInfo == null ? null : hc?.GetMethod("TakeDamage", Any, null, new[] { damageInfo }, null);
                var process = damageInfo == null ? null : hc?.GetMethod("TakeDamageProcess", Any, null, new[] { damageInfo }, null);
                if (take == null || process == null || take.IsStatic || process.IsStatic)
                {
                    CompatShieldPatcher.Log.LogWarning("Damage chain guard: HealthComponent.TakeDamage / TakeDamageProcess(DamageInfo) not found (game updated?), not applied");
                    return;
                }
                Replay = (self, info) => process.Invoke(self, new[] { info });
                h.Patch(take, prefix: M(nameof(Prefix)), finalizer: M(nameof(Finalizer)));
                // Callers run TakeDamage then OnHitEnemy side by side, so an on-hit proc that fires a blast nests through OnHitEnemy,
                // not through TakeDamage; count those frames too (never deferred, only counted).
                var onHit = damageInfo == null ? null : ror2.GetType("RoR2.GlobalEventManager")?.GetMethod("OnHitEnemy", Any, null, new[] { damageInfo, typeof(GameObject) }, null);
                if (onHit != null) h.Patch(onHit, prefix: M(nameof(CountPrefix)), finalizer: M(nameof(Finalizer)));
                // Each physics tick gets a fresh share of time and runs what is still waiting; TakeDamage is never on the stack there.
                var tick = ror2.GetType("RoR2.RoR2Application")?.GetEvent("onFixedUpdate", BindingFlags.Public | BindingFlags.Static);
                if (tick != null) tick.AddEventHandler(null, new Action(TickDrain));
                else
                {
                    DrainBudgetMs = 0;   // no tick would ever renew the time share, so waiting hits must all run at once
                    CompatShieldPatcher.Log.LogWarning("Damage chain: RoR2Application.onFixedUpdate not found (game updated?), so the per-tick time limit is off");
                }
                CompatShieldPatcher.Log.LogInfo($"Damage chain: hits nested more than {MaxDepth} deep (on-hit and on-death effects damaging from inside the hit they react to) " +
                    $"now run one at a time after the outer hit, so a kill chain can't overflow the stack; at most {QueueLimit} wait at once, " +
                    (DrainBudgetMs > 0 ? $"{DrainBudgetMs} ms of them per physics tick" : "with no time limit"));
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Damage chain guard failed to apply: " + e);
            }
        }

        static HarmonyMethod M(string name) => new HarmonyMethod(typeof(DamageChainGuard).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));

        // Harmony binds damageInfo by the original's parameter name. Never throws.
        static bool Prefix(object __instance, object damageInfo, out bool __state)
        {
            if (depth >= MaxDepth)
            {
                __state = false;
                // Only hits still waiting count against the line, because it shrinks as it runs. The backlog total ends a chain whose
                // replays keep refilling the line: once it is reached nothing joins until the line has emptied.
                if (queued.Count - head < QueueLimit && acceptedThisBacklog < MaxBacklogHits)
                {
                    acceptedThisBacklog++;
                    queued.Add(new[] { __instance, damageInfo });
                    if (queued.Count - head > peakWaiting) peakWaiting = queued.Count - head;
                    if (deferredTotal++ == 0)
                        CompatShieldPatcher.Log.LogInfo($"Damage chain: a hit nested {MaxDepth + 1} deep was queued to run after the outer hit instead of deeper on the stack (shown once)");
                }
                else
                {
                    droppedThisBacklog++;
                    if (dropped++ == 0)
                        CompatShieldPatcher.Log.LogWarning($"Damage chain: too many hits waiting behind one hit (limits: {QueueLimit} at once, {MaxBacklogHits} per backlog); the rest of this chain is dropped (shown once)");
                }
                return false;
            }
            depth++;
            __state = true;
            return true;
        }

        static void CountPrefix(out bool __state) { depth++; __state = true; }

        static void TickDrain()
        {
            // A fresh share each tick, minus any overrun: one slow hit can pass the limit, but later ticks pay it back.
            spentMs = Math.Max(0, spentMs - DrainBudgetMs);
            if (draining || queued.Count == 0) return;
            depth = 0;
            Drain();
        }

        // A void finalizer: an exception from the hit keeps its own stack trace (Harmony only rethrows what a finalizer returns).
        static void Finalizer(bool __state)
        {
            if (!__state) return;
            depth--;
            if (depth <= 0)
            {
                depth = 0;
                if (!draining && queued.Count > 0) Drain();
            }
        }

        /// <summary>Runs the waiting hits one at a time until none are left or this tick's time is used up; hits queued while draining join the same loop.</summary>
        internal static void Drain()
        {
            bool limited = DrainBudgetMs > 0;
            if (limited && spentMs >= DrainBudgetMs) return;   // this tick's share is used up; the next physics tick carries on
            double started = NowMs();
            draining = true;
            try
            {
                while (head < queued.Count)
                {
                    if (limited && spentMs + NowMs() - started >= DrainBudgetMs) break;
                    var q = queued[head++];
                    // The outermost hit's outer hooks are still on the stack under us, so a replay counts as link 1, not 0: its own
                    // nested hits get two more links before they queue again.
                    depth = 1;
                    try
                    {
                        if (IsGone(q[0])) continue;
                        Replay?.Invoke(q[0], q[1]);
                    }
                    catch (Exception e)
                    {
                        if (replayFailures++ < 3)
                            CompatShieldPatcher.Log.LogWarning("Damage chain: a queued hit threw (shown 3x): " +
                                (e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e));
                    }
                    finally { depth = 0; }
                }
            }
            finally
            {
                spentMs += NowMs() - started;
                queued.RemoveRange(0, head);
                head = 0;
                draining = false;
            }
            NoteBacklog();
        }

        // One line per backlog that outlived its tick or lost hits, so the next log shows how big the storms got.
        static void NoteBacklog()
        {
            if (queued.Count > 0) { carriedTicks++; return; }
            if ((carriedTicks > 0 || droppedThisBacklog > 0) && backlogLines++ < MaxBacklogLines)
                CompatShieldPatcher.Log.LogInfo($"Damage chain: a backlog of up to {peakWaiting} waiting hits was spread over {carriedTicks + 1} physics ticks ({droppedThisBacklog} dropped)");
            carriedTicks = peakWaiting = droppedThisBacklog = acceptedThisBacklog = 0;
        }

        internal static void ResetForTest()
        {
            depth = deferredTotal = dropped = replayFailures = head = carriedTicks = peakWaiting = droppedThisBacklog = acceptedThisBacklog = backlogLines = 0;
            spentMs = 0;
            draining = false;
            queued.Clear();
            QueueLimit = DefaultQueueLimit;
            DrainBudgetMs = DefaultDrainMs;
        }
    }

    /// <summary>
    /// A blast set off from inside another blast's hits (Fire Shield reacting to the damage, Marwan's Ash reacting to the hit) runs before the outer blast
    /// finishes, and DamageChainGuard only counts the OnHitEnemy frames those go through, so a loop between two such items has no limit: at 3:54 AM on
    /// 2026-11-08 one hung the game for good, 6-7 blasts inside each other and about 1,970 frames deep. A blast that would sit more than MaxDepth deep, or
    /// that starts once the time allowance is gone, deals no damage and sets off nothing (Fire() still returns its hits). The allowance is shared by every
    /// chain and refills between them, because hits replayed from DamageChainGuard's line (and each hit a client reports) start a new chain with nothing on
    /// the stack. A blast that isn't inside another one always runs, and one directly inside it gets far more room, so ordinary one-level fan-out such as
    /// Brilliant Behemoth isn't cut. Host only: a client just reports its blasts to the host.
    /// Patched at the first run start, because Harmony.Patch on the load path can run a game type's static constructor (two launch crashes so far).
    /// </summary>
    public static class BlastChainCap
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal const int DefaultMaxDepth = 4;
        internal const float DefaultBudgetMs = 100f;
        const int MaxChainLines = 200;       // one log line per chain that lost blasts; a long run can't flood the log
        internal static int MaxDepth = DefaultMaxDepth;
        internal static double BudgetMs = DefaultBudgetMs;   // the time allowance when full, shared by every chain; 0 = no time limit
        const double RefillShare = 0.25;     // the allowance refills at this share of the time that passes between chains
        const double LevelOneOverdraft = 3;  // a blast directly inside the outermost one keeps going until the allowance is this many BudgetMs overdrawn
        const double MaxOverdraft = 4;       // the most one chain can overdraw it, so one huge chain or a GC pause inside one can't shut blasts off for long
        static int depth, nestedRan, skippedDeep, skippedLate, chainLines;
        static double chainStartMs, allowanceMs = DefaultBudgetMs, lastRefillMs;
        static string firstSkipped;
        static bool patched;
        static Harmony patcher;
        static Assembly game;
        static FieldInfo attackerField;
        internal static int Depth => depth;
        internal static Func<double> NowMs = () => System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 / System.Diagnostics.Stopwatch.Frequency;   // the harness swaps in a hand-driven clock
        internal static Func<object, string> AttackerName = blast => attackerField?.GetValue(blast) is UnityEngine.Object o && o != null ? o.name : "no attacker";   // the harness swaps in a stub

        /// <summary>Takes the limits from [4. Lag and Hang Protection]. Runs on the assembly-load path, so it must not touch Unity.</summary>
        internal static void Configure(int maxDepth, float budgetMs)
        {
            MaxDepth = Math.Max(1, maxDepth);
            BudgetMs = Math.Max(0f, budgetMs);
            allowanceMs = BudgetMs;
        }

        public static void Apply(Harmony h, Assembly ror2)
        {
            try
            {
                patcher = h;
                game = ror2;
                var ev = ror2.GetType("RoR2.Run")?.GetEvent("onRunStartGlobal", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) throw new MissingMemberException("RoR2.Run.onRunStartGlobal not found");
                ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(BlastChainCap).GetMethod(nameof(OnRunStart), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Blast chain cap: can't watch run starts, not applied: " + e.Message);
            }
        }

        static void OnRunStart(object run)
        {
            if (patched) return;
            patched = true;
            try { PatchBlast(patcher, game.GetType("RoR2.BlastAttack")); }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Blast chain cap: can't patch BlastAttack, not applied: " + e.Message); }
        }

        internal static bool PatchBlast(Harmony h, Type blast)
        {
            var hitPoint = blast?.GetNestedType("HitPoint", Any);
            var handleHits = hitPoint == null ? null : blast.GetMethod("HandleHits", Any, null, new[] { hitPoint.MakeArrayType() }, null);
            if (handleHits == null || handleHits.IsStatic || handleHits.ReturnType != typeof(void))
            {
                CompatShieldPatcher.Log.LogWarning("Blast chain cap: BlastAttack.HandleHits(HitPoint[]) not found (game updated?), not applied");
                return false;
            }
            attackerField = blast.GetField("attacker", Any);
            h.Patch(handleHits, prefix: M(nameof(Prefix)), finalizer: M(nameof(Finalizer)));
            CompatShieldPatcher.Log.LogInfo($"Blast chain cap: a blast set off from inside other blasts stops firing once {MaxDepth} are running inside each other" +
                (BudgetMs > 0 ? $" or they have used up a shared {BudgetMs} ms allowance" : "") + ", so item combos can't hang the game");
            return true;
        }

        static HarmonyMethod M(string name) => new HarmonyMethod(typeof(BlastChainCap).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));

        // Never throws. __state says this blast went on the count, so its finalizer takes it off again.
        static bool Prefix(object __instance, out bool __state)
        {
            __state = false;
            if (depth == 0)
            {
                chainStartMs = NowMs();
                allowanceMs = Math.Min(BudgetMs, allowanceMs + (chainStartMs - lastRefillMs) * RefillShare);
                lastRefillMs = chainStartMs;
            }
            else
            {
                bool tooDeep = depth >= MaxDepth;
                bool tooLate = !tooDeep && BudgetMs > 0 && allowanceMs - (NowMs() - chainStartMs) <= (depth >= 2 ? 0 : -LevelOneOverdraft * BudgetMs);
                if (tooDeep || tooLate)
                {
                    if (tooDeep) skippedDeep++; else skippedLate++;
                    if (firstSkipped == null) firstSkipped = AttackerName(__instance);
                    return false;
                }
                nestedRan++;
            }
            depth++;
            __state = true;
            return true;
        }

        // A void finalizer: an exception from the blast keeps its own stack trace (Harmony only rethrows what a finalizer returns).
        static void Finalizer(bool __state)
        {
            if (!__state || --depth > 0) return;
            depth = 0;
            int stopped = skippedDeep + skippedLate;
            if (nestedRan + stopped > 0)   // a blast with nothing inside it isn't what the allowance is for
            {
                double now = NowMs(), took = now - chainStartMs;
                allowanceMs = Math.Max(-MaxOverdraft * BudgetMs, allowanceMs - took);
                lastRefillMs = now;
                if (stopped > 0 && chainLines++ < MaxChainLines)
                    CompatShieldPatcher.Log.LogInfo($"Blast chain: stopped {stopped} nested {(stopped == 1 ? "blast" : "blasts")} ({skippedDeep} too deep, {skippedLate} too late); " +
                        $"{nestedRan} ran, the chain took {took:0} ms, and the first one stopped came from {firstSkipped}");
            }
            nestedRan = skippedDeep = skippedLate = 0;
            firstSkipped = null;
        }

        internal static void ResetForTest()
        {
            depth = nestedRan = skippedDeep = skippedLate = chainLines = 0;
            chainStartMs = lastRefillMs = 0;
            allowanceMs = DefaultBudgetMs;
            firstSkipped = null;
            patched = false;
            MaxDepth = DefaultMaxDepth;
            BudgetMs = DefaultBudgetMs;
        }
    }

    /// <summary>
    /// One target's exception used to end a whole blast. BlastAttack.HandleHits calls PerformDamageServer for each target with no try/catch, and
    /// DelayBlast.Detonate and ProjectileExplosion.Detonate destroy their object only after the blast returns. So a throw skipped the later targets and the
    /// Destroy, and the object detonated again next physics tick, hitting the earlier targets again, until the throw stopped: 549 of the 560 null errors on
    /// 2026-11-08 came from blasts, 159 of them inside 3 seconds. This catches the exception per target, so the blast finishes and its object goes away.
    /// It does not cure whatever was null. The target that failed loses the rest of that hit, and each new kind of failure gets one line naming the victim, the
    /// attacker and the throw site so the real cause can be found. An error outside target damage (the hit search, an effect, a hook on Fire) would still skip
    /// the Destroy, but none of the 549 did. Host only: blast damage runs on the server.
    /// Patched at the first run start, because Harmony.Patch on the load path can run a game type's static constructor (two launch crashes so far).
    /// </summary>
    public static class BlastTargetGuard
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal const int MaxKinds = 30;   // a kind is one victim, attacker, exception type and throw site
        internal const int MaxSites = 30;   // a site is one exception type and throw site, so a second bug still gets a line after one bug has filled the kinds
        const int StackFrames = 6;
        static int total, nextMilestone = 10;
        static readonly HashSet<string> kinds = new HashSet<string>(), sites = new HashSet<string>();
        static bool patched, describeFailed;
        static Harmony patcher;
        static Assembly game;
        static FieldInfo victimField, attackerField, inflictorField;
        static Type bodyType;
        internal static int Total => total;

        // Swappable, because the harness has no Unity objects to read.
        internal static Func<object, bool> Alive = o => o is UnityEngine.Object u ? u != null : o != null;
        internal static Func<object, string> NameOf = o => (o as UnityEngine.Object)?.name ?? o?.ToString();
        internal static Func<object, object> BodyOf = attacker => attacker is GameObject g && g != null && bodyType != null ? g.GetComponent(bodyType) : null;

        public static void Apply(Harmony h, Assembly ror2)
        {
            try
            {
                patcher = h;
                game = ror2;
                var ev = ror2.GetType("RoR2.Run")?.GetEvent("onRunStartGlobal", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) throw new MissingMemberException("RoR2.Run.onRunStartGlobal not found");
                ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(BlastTargetGuard).GetMethod(nameof(OnRunStart), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Blast guard: can't watch run starts, not applied: " + e.Message);
            }
        }

        static void OnRunStart(object run)
        {
            if (patched) return;
            patched = true;
            try { PatchGuard(patcher, game.GetType("RoR2.BlastAttack")); }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Blast guard: can't patch BlastAttack, not applied: " + e.Message); }
        }

        internal static bool PatchGuard(Harmony h, Type blast)
        {
            var info = blast?.GetNestedType("BlastAttackDamageInfo", Any);
            var perform = info == null ? null : blast.GetMethod("PerformDamageServer", Any, null, new[] { info.MakeByRefType() }, null);
            if (perform == null || !perform.IsStatic || perform.ReturnType != typeof(void))
            {
                CompatShieldPatcher.Log.LogWarning("Blast guard: BlastAttack.PerformDamageServer(in BlastAttackDamageInfo) not found (game updated?), not applied");
                return false;
            }
            victimField = info.GetField("hitHealthComponent", Any);
            attackerField = info.GetField("attacker", Any);
            inflictorField = info.GetField("inflictor", Any);
            bodyType = blast.Assembly.GetType("RoR2.CharacterBody");
            h.Patch(perform, finalizer: new HarmonyMethod(typeof(BlastTargetGuard).GetMethod(nameof(Finalizer), BindingFlags.NonPublic | BindingFlags.Static)));
            CompatShieldPatcher.Log.LogInfo("Blast guard: one target's error no longer ends a blast, so a failed target can't make it detonate again every tick");
            return true;
        }

        // Returns Exception, not void, because only a returned null stops an exception, and stopping it is the whole fix. Never throws.
        // __args copies the hit info on every call: one small allocation next to the DamageInfo and hook work of the hit itself.
        static Exception Finalizer(Exception __exception, object[] __args)
        {
            if (__exception == null) return null;
            total++;
            try
            {
                Note(__exception, __args != null && __args.Length > 0 ? __args[0] : null);
            }
            catch (Exception e)
            {
                if (!describeFailed)
                {
                    describeFailed = true;
                    try { CompatShieldPatcher.Log.LogWarning("Blast guard: couldn't describe a failed hit (logged once): " + e); } catch { }
                }
            }
            return null;
        }

        static void Note(Exception ex, object info)
        {
            if (total >= nextMilestone)
            {
                nextMilestone *= 10;
                CompatShieldPatcher.Log.LogWarning($"Blast guard: {total} failed hits caught so far");
            }
            if (kinds.Count >= MaxKinds && sites.Count >= MaxSites) return;   // nothing left to log, so stop paying for the stack trace
            object victim = info == null ? null : victimField?.GetValue(info);
            object attacker = info == null ? null : attackerField?.GetValue(info);
            object inflictor = info == null ? null : inflictorField?.GetValue(info);
            var frames = (ex.StackTrace ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(StackFrames).ToArray();
            string victimName = Name(victim), attackerName = Name(attacker), kindName = ex.GetType().Name;
            string site = kindName + "|" + (frames.Length > 0 ? frames[0] : "no stack trace");
            bool newSite = sites.Count < MaxSites && sites.Add(site);
            bool newKind = kinds.Count < MaxKinds && kinds.Add(victimName + "|" + attackerName + "|" + site);
            if (!newSite && !newKind) return;
            object body = Alive(victim) ? Read(victim, "body") : null;
            object attackerBody = Alive(attacker) ? BodyOf(attacker) : null;
            CompatShieldPatcher.Log.LogWarning($"Blast guard: a hit failed and the blast carried on ({kindName}: {ex.Message}). " +
                $"Victim {victimName}: body {Has(victim, "body")}, inventory {Has(body, "inventory")}, master {Has(body, "master")}, " +
                $"shield {Num(victim, "shield")}/{Num(victim, "fullShield")}, barrier {Num(victim, "barrier")}, health {Num(victim, "health")}/{Num(victim, "fullHealth")}. " +
                $"Attacker {attackerName}: body {(Alive(attackerBody) ? "ok" : Alive(attacker) ? "null" : "unknown")}, inventory {Has(attackerBody, "inventory")}, input bank {Has(attackerBody, "inputBank")}. " +
                $"Inflictor {Name(inflictor)}. Failed hit {total} this session. " +
                (newKind && kinds.Count == MaxKinds ? $"That is {MaxKinds} kinds, so from here only a new throw site gets a line; the count carries on. " : "") +
                (newSite && sites.Count == MaxSites ? $"That is {MaxSites} throw sites, the last new one logged. " : "") +
                "Thrown at: " + (frames.Length > 0 ? string.Join(" | ", frames) : "no stack trace"));
        }

        static string Name(object o) => o == null ? "none" : Alive(o) ? NameOf(o) : "destroyed";

        // A field or a property by name; null when the owner or the member is missing.
        static object Read(object owner, string member)
        {
            if (!Alive(owner)) return null;
            var t = owner.GetType();
            var f = t.GetField(member, Any);
            return f != null ? f.GetValue(owner) : t.GetProperty(member, Any)?.GetValue(owner, null);
        }

        static string Has(object owner, string member)
        {
            if (!Alive(owner)) return "unknown";
            try { return Alive(Read(owner, member)) ? "ok" : "null"; }
            catch { return "unreadable"; }
        }

        static string Num(object owner, string member)
        {
            try { return Read(owner, member) is float f ? f.ToString("0.#") : "unknown"; }
            catch { return "unknown"; }
        }

        internal static void ResetForTest()
        {
            total = 0;
            nextMilestone = 10;
            kinds.Clear();
            sites.Clear();
            patched = describeFailed = false;
        }
    }

    /// <summary>
    /// Some stages have an interactable pool entry with no dccs in it (ironalluvium2 on 2026-11-08). Mods walk the pool without a null check and throw at it:
    /// R2API.Director's InteractableActions subscribers (Chen's Gradius mod, Tinker's Satchel) and QoLChests' ClassicStageInfo.Start hook. R2API catches the throw per
    /// subscriber, so the visible effect is that those mods' cards and changes never reach that stage. This removes the empty entries from all three lists of every
    /// category, so nothing has to handle them. MSU's InteractableModule throws on some stages too, but this can't fix that one: it has its own null check, reads only
    /// alwaysIncluded, and fails when no live entry is left there to add its cards to.
    /// Removed, not filled with an empty card set, because an empty set would count as available and take one of the limited content mix slots.
    /// Not a balance change. DCCSBlender already skips an empty entry in the two optional lists, so those picks and weights stay the same. It has no null check on
    /// alwaysIncluded, so an empty entry there used to abort the game's own RebuildCards, which now finishes. The mods' own cards do now reach those stages.
    /// Runs in ClassicStageInfo.Awake: no installed mod hooks it through MonoMod (the harness scans every plugin), and it comes before R2API's Start hook, which copies
    /// the pool for its per-stage backup (a copy taken first would put the empty entries back on a revisit). A vanilla component, ClassicStageInfoVariation, can swap a
    /// variant pool in after Start, so both it and SetInteractableDCCSP are patched: the setter is small enough for Mono to inline into that component, where its prefix
    /// would never run.
    /// The line logged for each cleaned pool carries the evidence for who made the hole: weight, requiredExpansions and hasBeenSerialized (false means the entry
    /// was built in code, so by a mod; true means it came from saved data or a clone of it). Only the host needs this: only the host picks what spawns.
    /// Patched at the first run start, because Harmony.Patch on the load path can run a game type's static constructor (two launch crashes so far).
    /// </summary>
    public static class NullDccsPoolEntryFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static readonly string[] EntryLists = { "alwaysIncluded", "includedIfConditionsMet", "includedIfNoConditionsMet" };
        static int total;
        static bool patched, failed;
        static Harmony patcher;
        static Assembly game;
        static FieldInfo poolField, variantPoolField;
        internal static int Total => total;

        // Swappable, because the harness has no Unity objects to read.
        internal static Func<object, bool> Alive = o => o is UnityEngine.Object u ? u != null : o != null;
        internal static Func<object, string> NameOf = o => o is UnityEngine.Object u ? (u != null ? u.name : "destroyed") : o?.ToString();
        internal static Func<object, string> SceneOf = o => o is Component c ? c.gameObject.scene.name : "unknown scene";

        public static void Apply(Harmony h, Assembly ror2)
        {
            try
            {
                patcher = h;
                game = ror2;
                var ev = ror2.GetType("RoR2.Run")?.GetEvent("onRunStartGlobal", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) throw new MissingMemberException("RoR2.Run.onRunStartGlobal not found");
                ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(NullDccsPoolEntryFix).GetMethod(nameof(OnRunStart), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Null dccs fix: can't watch run starts, not applied: " + e.Message);
            }
        }

        static void OnRunStart(object run)
        {
            if (patched) return;
            patched = true;
            try { PatchPools(patcher, game.GetType("RoR2.ClassicStageInfo")); }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Null dccs fix: can't patch ClassicStageInfo, not applied: " + e.Message); }
        }

        internal static bool PatchPools(Harmony h, Type stage)
        {
            poolField = stage?.GetField("interactableDccsPool", Any);
            var awake = stage?.GetMethod("Awake", Any, null, Type.EmptyTypes, null);
            // The whole shape is checked here, so a game update that renames a field is left alone instead of reading as "every entry is empty".
            var category = poolField?.FieldType.GetField("poolCategories", Any)?.FieldType.GetElementType();
            if (awake == null || awake.IsStatic || category == null || EntryLists.Any(n => category.GetField(n, Any)?.FieldType.GetElementType()?.GetField("dccs", Any) == null))
            {
                CompatShieldPatcher.Log.LogWarning("Null dccs fix: ClassicStageInfo or DccsPool is not the expected shape (game updated?), not applied");
                return false;
            }
            h.Patch(awake, postfix: new HarmonyMethod(typeof(NullDccsPoolEntryFix).GetMethod(nameof(AwakePostfix), BindingFlags.NonPublic | BindingFlags.Static)));
            PatchSwaps(h, stage);
            CompatShieldPatcher.Log.LogInfo("Null dccs fix: interactable pool entries with no dccs are removed before anything walks the pool");
            return true;
        }

        // Each in its own try: a failure here must not read as "not applied" while the Awake patch is live, and one swap point failing shouldn't cost the other.
        static void PatchSwaps(Harmony h, Type stage)
        {
            try
            {
                var swap = stage.GetMethod("SetInteractableDCCSP", Any, null, new[] { poolField.FieldType }, null);
                if (swap == null) CompatShieldPatcher.Log.LogWarning("Null dccs fix: ClassicStageInfo.SetInteractableDCCSP not found, a pool swapped in later isn't covered");
                else h.Patch(swap, prefix: new HarmonyMethod(typeof(NullDccsPoolEntryFix).GetMethod(nameof(SwapPrefix), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Null dccs fix: can't patch SetInteractableDCCSP, a pool swapped in later isn't covered: " + e.Message); }
            try
            {
                var variant = stage.Assembly.GetType(stage.FullName + "Variation");
                variantPoolField = variant?.GetField("interactableDccsPool", Any);
                var apply = variant?.GetMethod("SwapCSIValuesWithSelf", Any, null, Type.EmptyTypes, null);
                if (apply == null || apply.IsStatic || variantPoolField?.FieldType != poolField.FieldType) CompatShieldPatcher.Log.LogWarning("Null dccs fix: ClassicStageInfoVariation is not the expected shape (game updated?), a stage variant's pool may not be covered");
                else h.Patch(apply, prefix: new HarmonyMethod(typeof(NullDccsPoolEntryFix).GetMethod(nameof(VariantPrefix), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Null dccs fix: can't patch SwapCSIValuesWithSelf, a stage variant's pool may not be covered: " + e.Message); }
        }

        static void AwakePostfix(object __instance)
        {
            try { Clean(poolField.GetValue(__instance), __instance, "Awake"); }
            catch (Exception e) { WarnOnce(e); }
        }

        // __args rather than a named parameter, so a renamed parameter can't stop the patch applying.
        static void SwapPrefix(object __instance, object[] __args)
        {
            try { Clean(__args[0], __instance, "SetInteractableDCCSP"); }
            catch (Exception e) { WarnOnce(e); }
        }

        // Cleans the variant's own pool before it reaches the stage, whether or not the setter's body survived inlining.
        static void VariantPrefix(object __instance)
        {
            try { Clean(variantPoolField.GetValue(__instance), __instance, "SwapCSIValuesWithSelf"); }
            catch (Exception e) { WarnOnce(e); }
        }

        static void WarnOnce(Exception e)
        {
            if (failed) return;
            failed = true;
            try { CompatShieldPatcher.Log.LogWarning("Null dccs fix: couldn't clean a pool: " + e.GetType().Name + ": " + e.Message); } catch { }
        }

        // The pool is an asset that outlives the stage, so a cleaned pool has nothing left to remove and logs nothing on later visits.
        internal static int Clean(object pool, object stageInfo, string foundAt)
        {
            if (!Alive(pool)) return 0;
            if (!(pool.GetType().GetField("poolCategories", Any)?.GetValue(pool) is Array categories)) return 0;
            var notes = new List<string>();
            int removed = 0;
            for (int i = 0; i < categories.Length; i++)
            {
                var category = categories.GetValue(i);
                if (category == null) continue;
                var categoryName = category.GetType().GetField("name", Any)?.GetValue(category);
                foreach (var listName in EntryLists)
                {
                    var list = category.GetType().GetField(listName, Any);
                    if (!(list?.GetValue(category) is Array entries)) continue;
                    var dccs = entries.GetType().GetElementType().GetField("dccs", Any);
                    if (dccs == null) continue;
                    var kept = new List<object>(entries.Length);
                    var gone = new List<string>();
                    foreach (var entry in entries)
                    {
                        if (entry != null && Alive(dccs.GetValue(entry))) kept.Add(entry);
                        else gone.Add(Describe(entry));
                    }
                    if (gone.Count == 0) continue;
                    var rebuilt = Array.CreateInstance(entries.GetType().GetElementType(), kept.Count);
                    kept.ToArray().CopyTo(rebuilt, 0);
                    list.SetValue(category, rebuilt);
                    removed += gone.Count;
                    notes.Add($"category \"{categoryName}\" [{i}] {listName}: {gone.Count} ({string.Join(", ", gone)})");
                }
            }
            if (removed == 0) return 0;
            total += removed;
            CompatShieldPatcher.Log.LogInfo($"Null dccs fix: removed {removed} pool {(removed == 1 ? "entry" : "entries")} from {NameOf(pool)} ({pool.GetType().Name}) on {SceneOf(stageInfo)}, found at {foundAt}: {string.Join("; ", notes)}");
            return removed;
        }

        static string Describe(object entry)
        {
            if (entry == null) return "empty slot";
            var type = entry.GetType();
            object Read(string name) => type.GetField(name, Any)?.GetValue(entry);
            var text = $"{(Read("dccs") == null ? "empty reference" : "destroyed object")}, weight {Read("weight")}, serialized {Read("hasBeenSerialized")}";
            if (Read("requiredExpansions") is Array expansions && expansions.Length > 0)
            {
                var names = new List<string>();
                foreach (var expansion in expansions) names.Add(NameOf(expansion) ?? "none");
                text += ", expansions " + string.Join("+", names);
            }
            return text;
        }

        internal static void ResetForTest()
        {
            total = 0;
            patched = failed = false;
        }
    }

    /// <summary>
    /// A throw inside CharacterBody.RecalculateStats used to repeat every physics tick. The game clears statsDirty as the very last step of that method and
    /// CharacterBody.FixedUpdate calls it again whenever the flag is set, so one persistent null reran the whole method and threw again 60 times a second
    /// (1,293 errors on 2026-11-08, 69 fps down to 32 with no monsters alive). From the second failure in a row this clears the flag and sets it again after a
    /// growing wait, so a body that keeps failing costs a few calls a minute. The first failure keeps the next-tick retry, so a one-tick glitch heals as it always did.
    /// The finalizer returns void on purpose: the exception carries on exactly as before and keeps its stack trace. Swallowing it would let On. hooks that run
    /// after orig act on half-computed stats, and a bonus added to a stat the method never reached would stack on every retry.
    /// It does not cure whatever was null. Each new kind of failure logs one line naming the body, the throw site, which parts of the body are null and its items
    /// and buffs, so the real cause can be found. Every PC: each game runs this for its own copy of every body, so a PC with the guard protects only itself.
    /// An error thrown by an On. hook before it calls orig never reaches this finalizer, so that kind still repeats. All 1,293 were inside the method.
    /// Patched at the first run start, because Harmony.Patch on the load path can run a game type static constructor (two launch crashes so far).
    /// </summary>
    public static class StatsRetryGuard
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal const int MaxKinds = 30;   // a kind is one body name, exception type and throw site
        internal const int MaxSites = 30;   // a site is one exception type and throw site, so a second bug still gets a line after one bug has filled the kinds
        internal const int MaxListed = 60;  // items or buffs named on one line
        internal const double FirstWaitSeconds = 0.25, LongestWaitSeconds = 8;
        const int StackFrames = 6;
        static readonly string[] Parts = { "inventory", "master", "healthComponent", "skillLocator", "equipmentSlot", "characterMotor", "inputBank", "modelLocator", "teamComponent" };
        static readonly string[] StockSkills = { "primary", "secondary", "utility", "special" };

        sealed class Entry { public int failures; public bool waiting; public double dueAt; }
        static readonly Dictionary<object, Entry> failing = new Dictionary<object, Entry>();
        static readonly List<object> due = new List<object>();
        static readonly HashSet<string> kinds = new HashSet<string>(), sites = new HashSet<string>();
        static int total, early, nextMilestone = 10;
        static bool patched, complained;
        static Harmony patcher;
        static Assembly game;
        static FieldInfo dirtyField;
        static MethodInfo itemDefLookup, buffDefLookup;
        internal static int Total => total;
        internal static int Early => early;
        internal static int Failing => failing.Count;

        // Swappable, because the harness has no Unity objects or game catalogs to read.
        internal static Func<object, bool> Alive = o => o is UnityEngine.Object u ? u != null : o != null;
        internal static Func<object, string> NameOf = o => (o as UnityEngine.Object)?.name ?? o?.ToString();
        internal static Func<object, string> ItemName = i => CatalogName(itemDefLookup, i);
        internal static Func<object, string> BuffName = b => CatalogName(buffDefLookup, b);
        internal static Func<double> Clock = () => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

        public static void Apply(Harmony h, Assembly ror2)
        {
            try
            {
                patcher = h;
                game = ror2;
                var ev = ror2.GetType("RoR2.Run")?.GetEvent("onRunStartGlobal", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) throw new MissingMemberException("RoR2.Run.onRunStartGlobal not found");
                ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(StatsRetryGuard).GetMethod(nameof(OnRunStart), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Stats guard: cannot watch run starts, not applied: " + e.Message);
            }
        }

        static void OnRunStart(object run)
        {
            if (patched) return;
            patched = true;
            try
            {
                const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;
                itemDefLookup = game.GetType("RoR2.ItemCatalog")?.GetMethods(publicStatic).FirstOrDefault(m => m.Name == "GetItemDef" && m.GetParameters().Length == 1);
                buffDefLookup = game.GetType("RoR2.BuffCatalog")?.GetMethods(publicStatic).FirstOrDefault(m => m.Name == "GetBuffDef" && m.GetParameters().Length == 1);
                PatchGuard(patcher, game.GetType("RoR2.CharacterBody"), game.GetType("RoR2.RoR2Application")?.GetEvent("onFixedUpdate", publicStatic));
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Stats guard: cannot patch CharacterBody, not applied: " + e.Message); }
        }

        internal static bool PatchGuard(Harmony h, Type body, EventInfo tick)
        {
            var recalc = body?.GetMethod("RecalculateStats", Any, null, Type.EmptyTypes, null);
            var dirty = body?.GetField("statsDirty", Any);
            if (recalc == null || recalc.IsStatic || recalc.ReturnType != typeof(void) || dirty == null || dirty.IsStatic || dirty.FieldType != typeof(bool))
            {
                CompatShieldPatcher.Log.LogWarning("Stats guard: CharacterBody.RecalculateStats or statsDirty is not the expected shape (game updated?), not applied");
                return false;
            }
            if (tick == null || tick.EventHandlerType != typeof(Action))
            {
                CompatShieldPatcher.Log.LogWarning("Stats guard: RoR2Application.onFixedUpdate not found (game updated?), so a waiting body would never retry, not applied");
                return false;
            }
            dirtyField = dirty;
            // The tick first: a patch without its tick would clear flags that nothing ever sets again, a tick without its patch has nothing to do.
            tick.AddEventHandler(null, new Action(Tick));
            h.Patch(recalc, finalizer: new HarmonyMethod(typeof(StatsRetryGuard).GetMethod(nameof(Finalizer), BindingFlags.NonPublic | BindingFlags.Static)));
            CompatShieldPatcher.Log.LogInfo($"Stats guard: a character whose stat update keeps failing now waits {FirstWaitSeconds} to {LongestWaitSeconds} seconds between tries instead of repeating it every physics tick");
            return true;
        }

        // Returns void on purpose: only a void finalizer leaves the exception and its stack trace alone. Never throws.
        static void Finalizer(object __instance, Exception __exception)
        {
            if (__exception == null)
            {
                if (failing.Count != 0) failing.Remove(__instance);
                return;
            }
            total++;
            try { Throttle(__instance); }
            catch (Exception e) { Complain("could not hold back a failing body", e); }
            try { Note(__exception, __instance); }
            catch (Exception e) { Complain("could not describe a failed stat update", e); }
        }

        static void Throttle(object body)
        {
            if (!failing.TryGetValue(body, out var entry)) failing[body] = entry = new Entry();
            entry.failures++;
            if (entry.failures < 2) return;   // a one-tick glitch still retries on the very next tick, as the game always did
            if (!entry.waiting && !(bool)dirtyField.GetValue(body)) return;   // the flag was already clear, so the game would not have retried this one and neither should the guard
            double now = Clock();
            if (entry.waiting && now < entry.dueAt) early++;   // something else marked the stats dirty during the wait, so this one was not a retry of ours
            dirtyField.SetValue(body, false);
            entry.waiting = true;
            entry.dueAt = now + WaitFor(entry.failures);
        }

        // Failure 2 waits 0.25 seconds, failure 3 waits 0.5, and so on up to the longest wait.
        internal static double WaitFor(int failures) => Math.Min(FirstWaitSeconds * (1L << Math.Min(failures - 2, 16)), LongestWaitSeconds);

        // Runs every physics tick, and almost always returns at the first line. Sets the flag directly because MarkAllStatsDirty is a method mods hook.
        static void Tick()
        {
            if (failing.Count == 0) return;
            try
            {
                double now = Clock();
                due.Clear();
                foreach (var pair in failing)
                    if (!Alive(pair.Key) || (pair.Value.waiting && now >= pair.Value.dueAt)) due.Add(pair.Key);
                foreach (var body in due)
                {
                    if (!Alive(body)) { failing.Remove(body); continue; }
                    failing[body].waiting = false;
                    dirtyField.SetValue(body, true);
                }
                due.Clear();
            }
            catch (Exception e) { Complain("could not run the retry tick", e); }
        }

        static void Note(Exception ex, object body)
        {
            if (total >= nextMilestone)
            {
                nextMilestone *= 10;
                CompatShieldPatcher.Log.LogWarning($"Stats guard: {total} failed stat updates so far, {failing.Count} bodies failing now, {early} of the failures came early because something marked the stats dirty during the wait");
            }
            if (kinds.Count >= MaxKinds && sites.Count >= MaxSites) return;   // nothing left to log, so stop paying for the stack trace
            var frames = (ex.StackTrace ?? "").Split((char)10).Select(l => l.Trim()).Where(l => l.Length > 0).Take(StackFrames).ToArray();
            string bodyName = Name(body), kindName = ex.GetType().Name;
            string site = kindName + "|" + (frames.Length > 0 ? frames[0] : "no stack trace");
            bool newSite = sites.Count < MaxSites && sites.Add(site);
            bool newKind = kinds.Count < MaxKinds && kinds.Add(bodyName + "|" + site);
            if (!newSite && !newKind) return;
            object skills = Safe(() => Read(body, "skillLocator"));
            int inRow = failing.TryGetValue(body, out var entry) ? entry.failures : 1;
            CompatShieldPatcher.Log.LogWarning($"Stats guard: a stat update failed on {bodyName} ({kindName}: {ex.Message}). " +
                $"Failure {inRow} in a row for this body, {total} this session. " +
                "Parts: " + string.Join(", ", Parts.Select(p => p + " " + Has(body, p))) + ". " +
                "Stock skills: " + string.Join(", ", StockSkills.Select(p => p + " " + Has(skills, p + "BonusStockSkill"))) + ". " +
                $"Level {Num(body, "level")}, player controlled {Text(body, "isPlayerControlled")}. " +
                $"Items: {Listing(() => Items(body))}. Buffs: {Listing(() => Buffs(body))}. " +
                (newKind && kinds.Count == MaxKinds ? $"That is {MaxKinds} kinds, so from here only a new throw site gets a line; the count carries on. " : "") +
                (newSite && sites.Count == MaxSites ? $"That is {MaxSites} throw sites, the last new one logged. " : "") +
                "Thrown at: " + (frames.Length > 0 ? string.Join(" | ", frames) : "no stack trace"));
        }

        static string Items(object body)
        {
            var order = Read(Read(body, "inventory"), "itemAcquisitionOrder") as System.Collections.IEnumerable;
            return order == null ? "unknown" : Named(order.Cast<object>(), ItemName);
        }

        static string Buffs(object body)
        {
            var list = Read(body, "activeBuffsList") as Array;
            if (list == null || !(Read(body, "activeBuffsListCount") is int count)) return "unknown";
            return Named(list.Cast<object>().Take(Math.Min(count, list.Length)), BuffName);
        }

        static string Named(IEnumerable<object> entries, Func<object, string> name)
        {
            var all = entries.ToList();
            if (all.Count == 0) return "none";
            return string.Join(", ", all.Take(MaxListed).Select(name)) + (all.Count > MaxListed ? $" and {all.Count - MaxListed} more" : "");
        }

        static string CatalogName(MethodInfo lookup, object index)
        {
            if (lookup == null) return index.ToString();
            return (lookup.Invoke(null, new[] { index }) as UnityEngine.Object)?.name ?? index.ToString();
        }

        static string Listing(Func<string> read) { try { return read(); } catch { return "unreadable"; } }
        static object Safe(Func<object> read) { try { return read(); } catch { return null; } }
        static string Name(object o) => o == null ? "none" : Alive(o) ? NameOf(o) : "destroyed";

        // A field or a property by name; null when the owner or the member is missing.
        static object Read(object owner, string member)
        {
            if (!Alive(owner)) return null;
            var t = owner.GetType();
            var f = t.GetField(member, Any);
            return f != null ? f.GetValue(owner) : t.GetProperty(member, Any)?.GetValue(owner, null);
        }

        static string Has(object owner, string member)
        {
            if (!Alive(owner)) return "unknown";
            try { return Alive(Read(owner, member)) ? "ok" : "null"; }
            catch { return "unreadable"; }
        }

        static string Num(object owner, string member)
        {
            try { return Read(owner, member) is float f ? f.ToString("0.#") : "unknown"; }
            catch { return "unknown"; }
        }

        static string Text(object owner, string member)
        {
            try { return Read(owner, member)?.ToString() ?? "unknown"; }
            catch { return "unknown"; }
        }

        static void Complain(string what, Exception e)
        {
            if (complained) return;
            complained = true;
            try { CompatShieldPatcher.Log.LogWarning("Stats guard: " + what + " (logged once): " + e); } catch { }
        }

        internal static void ResetForTest()
        {
            total = early = 0;
            nextMilestone = 10;
            failing.Clear();
            due.Clear();
            kinds.Clear();
            sites.Clear();
            patched = complained = false;
        }
    }

    /// <summary>
    /// Melee swings and ground damage zones both land their hits through OverlapAttack. OverlapAttack.Fire clears its hit list only after the damage is dealt, and
    /// ProjectileDotZone.FixedUpdate takes the interval off its stopwatch and checks its lifetime only after Fire returns. So a throw from one target's damage skipped the later
    /// targets, left the list full, and made the zone fire again on the next physics tick, hitting the earlier targets again, and it could not expire while the throw lasted:
    /// 228 null errors in 4 seconds on 2026-11-08, all from zones. One more error at that throw site came through a damage-over-time tick, which does not pass through here. This catches the exception around one target's damage, so the attack finishes.
    /// It does not cure whatever was null. The target that failed loses the rest of that hit, and each new kind of failure gets one line naming the victim, the attacker, the
    /// inflictor and the throw site so the real cause can be found. An error outside target damage (the hit search, an effect, a hook on Fire) would still repeat, but none of
    /// the 228 did. Host only: hits are dealt on the server.
    /// Patched at the first run start, because Harmony.Patch on the load path can run a game type's static constructor (two launch crashes so far).
    /// </summary>
    public static class OverlapHitGuard
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal const int MaxKinds = 30;   // a kind is one victim, attacker, inflictor, exception type and throw site
        internal const int MaxSites = 30;   // a site is one exception type and throw site, so a second bug still gets a line after one bug has filled the kinds
        const int StackFrames = 6;
        static int total, nextMilestone = 10;
        static readonly HashSet<string> kinds = new HashSet<string>(), sites = new HashSet<string>();
        static bool patched, describeFailed;
        static Harmony patcher;
        static Assembly game;
        static FieldInfo attackerField, inflictorField;
        static Type bodyType;
        static string why;
        internal static int Total => total;

        // Swappable, because the harness has no Unity objects to read.
        internal static Func<object, bool> Alive = o => o is UnityEngine.Object u ? u != null : o != null;
        internal static Func<object, string> NameOf = o => (o as UnityEngine.Object)?.name ?? o?.ToString();
        internal static Func<object, object> BodyOf = attacker => attacker is GameObject g && g != null && bodyType != null ? g.GetComponent(bodyType) : null;

        public static void Apply(Harmony h, Assembly ror2)
        {
            try
            {
                patcher = h;
                game = ror2;
                var ev = ror2.GetType("RoR2.Run")?.GetEvent("onRunStartGlobal", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) throw new MissingMemberException("RoR2.Run.onRunStartGlobal not found");
                ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(OverlapHitGuard).GetMethod(nameof(OnRunStart), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Overlap guard: can't watch run starts, not applied: " + e.Message);
            }
        }

        static void OnRunStart(object run)
        {
            if (patched) return;
            patched = true;
            try { PatchGuard(patcher, game.GetType("RoR2.OverlapAttack")); }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Overlap guard: can't patch OverlapAttack, not applied: " + e.Message); }
        }

        internal static bool PatchGuard(Harmony h, Type overlap)
        {
            var message = overlap?.GetNestedType("OverlapAttackMessage", Any);
            var info = overlap?.GetNestedType("OverlapInfo", Any);
            var perform = message == null || info == null ? null : overlap.GetMethod("PerformDamageServer", Any, null, new[] { message, typeof(List<>).MakeGenericType(info) }, null);
            if (perform == null || !perform.IsStatic || perform.ReturnType != typeof(void))
            {
                CompatShieldPatcher.Log.LogWarning("Overlap guard: OverlapAttack.PerformDamageServer(OverlapAttackMessage, List<OverlapInfo>) not found (game updated?), not applied");
                return false;
            }
            attackerField = message.GetField("attacker", Any);
            inflictorField = message.GetField("inflictor", Any);
            bodyType = overlap.Assembly.GetType("RoR2.CharacterBody");
            why = "the transpiler did not run";
            h.Patch(perform, transpiler: new HarmonyMethod(typeof(OverlapHitGuard).GetMethod(nameof(Transpiler), BindingFlags.NonPublic | BindingFlags.Static)));
            if (why != null)
            {
                CompatShieldPatcher.Log.LogWarning($"Overlap guard: {why} in OverlapAttack.PerformDamageServer (game updated?), not applied");
                return false;
            }
            CompatShieldPatcher.Log.LogInfo("Overlap guard: one target's error no longer ends a melee hit or a damage zone tick, so a failed target can't make a zone fire again every tick");
            return true;
        }

        // The catch goes around only the three calls that deal one target's damage, inside the loop, so the later targets still get theirs. A Harmony finalizer can't do that:
        // it runs after the loop has already been abandoned.
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = instructions.ToList();
            why = FindRange(code.Select(StepOf).ToList(), out int start, out int end);
            if (why == null)
                why = CatchWrap.WrapRange(code, start, end, generator, typeof(Exception),
                    new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(code[start].opcode, code[start].operand) },   // the attack, then the target
                    typeof(OverlapHitGuard).GetMethod(nameof(Caught), BindingFlags.Public | BindingFlags.Static));
            return code;   // both checks run before anything changes, so a refusal leaves the method as it was
        }

        static string StepOf(CodeInstruction ci)
        {
            var m = ci.operand as MethodInfo;
            return StepOf(ci.opcode.Name, ci.opcode.FlowControl.ToString(), m?.Name, m != null && m.ReturnType == typeof(void));
        }

        // One word per instruction, so the harness can run the same shape check over the real game DLL, which it can't JIT.
        internal static string StepOf(string opcode, string flow, string called, bool returnsVoid)
        {
            if (flow == "Branch" || flow == "Cond_Branch" || flow == "Return" || flow == "Throw") return "jump";
            if (opcode == "nop") return "nop";
            if (opcode == "ldloc" || opcode.StartsWith("ldloc.")) return "local";
            if ((opcode == "call" || opcode == "callvirt") && called != null) return (returnsVoid ? "void:" : "value:") + called;
            return "other";
        }

        // Finds health.TakeDamage(info) through OnHitAll(...). Each call must appear exactly once and in that order, and the stretch must start right after a finished statement,
        // because a try block has to open on an empty stack. Returns why not, or null.
        internal static string FindRange(IList<string> steps, out int start, out int end)
        {
            start = end = -1;
            int Only(string name) { int first = steps.IndexOf(name); return first >= 0 && steps.Count(s => s == name) == 1 ? first : -1; }
            int take = Only("void:TakeDamage"), enemy = Only("void:OnHitEnemy"), all = Only("void:OnHitAll");
            if (take < 0 || enemy < take || all < enemy) return "the damage calls aren't the expected shape";
            if (take < 3 || steps[take - 1] != "local" || steps[take - 2] != "local") return "the TakeDamage arguments aren't the expected shape";
            int before = take - 3;
            while (before >= 0 && steps[before] == "nop") before--;
            if (before < 0 || !steps[before].StartsWith("void:")) return "the code before TakeDamage isn't the expected shape";
            for (int i = take - 2; i <= all; i++) if (steps[i] == "jump") return "a jump in the middle of the damage calls";
            if (all + 1 >= steps.Count) return "nothing after the damage calls";
            start = take - 2;
            end = all;
            return null;
        }

        /// <summary>The catch block around one target's damage. Never throws.</summary>
        public static void Caught(Exception e, object attackData, object victim)
        {
            total++;
            try
            {
                Note(e, attackData, victim);
            }
            catch (Exception f)
            {
                if (!describeFailed)
                {
                    describeFailed = true;
                    try { CompatShieldPatcher.Log.LogWarning("Overlap guard: couldn't describe a failed hit (logged once): " + f); } catch { }
                }
            }
        }

        static void Note(Exception ex, object attackData, object victim)
        {
            if (total >= nextMilestone)
            {
                nextMilestone *= 10;
                CompatShieldPatcher.Log.LogWarning($"Overlap guard: {total} failed hits caught so far");
            }
            if (kinds.Count >= MaxKinds && sites.Count >= MaxSites) return;   // both limits full, so nothing more can be logged. Until then every failed hit still formats its stack trace, because its top frame is how a new site is told apart
            object attacker = attackData == null ? null : attackerField?.GetValue(attackData);
            object inflictor = attackData == null ? null : inflictorField?.GetValue(attackData);
            var frames = (ex.StackTrace ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(StackFrames).ToArray();
            string victimName = Name(victim), attackerName = Name(attacker), inflictorName = Name(inflictor), kindName = ex.GetType().Name;
            string site = kindName + "|" + (frames.Length > 0 ? frames[0] : "no stack trace");
            bool newSite = sites.Count < MaxSites && sites.Add(site);
            bool newKind = kinds.Count < MaxKinds && kinds.Add(victimName + "|" + attackerName + "|" + inflictorName + "|" + site);
            if (!newSite && !newKind) return;
            object body = Alive(victim) ? Read(victim, "body") : null;
            object attackerBody = Alive(attacker) ? BodyOf(attacker) : null;
            CompatShieldPatcher.Log.LogWarning($"Overlap guard: a hit failed and the attack carried on ({kindName}: {ex.Message}). " +
                $"Victim {victimName}: body {Has(victim, "body")}, inventory {Has(body, "inventory")}, master {Has(body, "master")}, " +
                $"shield {Num(victim, "shield")}/{Num(victim, "fullShield")}, barrier {Num(victim, "barrier")}, health {Num(victim, "health")}/{Num(victim, "fullHealth")}. " +
                $"Attacker {attackerName}: body {(Alive(attackerBody) ? "ok" : Alive(attacker) ? "null" : "unknown")}, inventory {Has(attackerBody, "inventory")}, input bank {Has(attackerBody, "inputBank")}. " +
                $"Inflictor {inflictorName}. Failed hit {total} this session. " +
                (newKind && kinds.Count == MaxKinds ? $"That is {MaxKinds} kinds, so from here only a new throw site gets a line; the count carries on. " : "") +
                (newSite && sites.Count == MaxSites ? $"That is {MaxSites} throw sites, the last new one logged. " : "") +
                "Thrown at: " + (frames.Length > 0 ? string.Join(" | ", frames) : "no stack trace"));
        }

        static string Name(object o) => o == null ? "none" : Alive(o) ? NameOf(o) : "destroyed";

        // A field or a property by name; null when the owner or the member is missing.
        static object Read(object owner, string member)
        {
            if (!Alive(owner)) return null;
            var t = owner.GetType();
            var f = t.GetField(member, Any);
            return f != null ? f.GetValue(owner) : t.GetProperty(member, Any)?.GetValue(owner, null);
        }

        static string Has(object owner, string member)
        {
            if (!Alive(owner)) return "unknown";
            try { return Alive(Read(owner, member)) ? "ok" : "null"; }
            catch { return "unreadable"; }
        }

        static string Num(object owner, string member)
        {
            try { return Read(owner, member) is float f ? f.ToString("0.#") : "unknown"; }
            catch { return "unknown"; }
        }

        internal static void ResetForTest()
        {
            total = 0;
            nextMilestone = 10;
            kinds.Clear();
            sites.Clear();
            patched = describeFailed = false;
        }
    }

    /// <summary>
    /// What DotTickGuard and BulletHitGuard look for in the game's code. One word per instruction, so the harness can run the same check over the real game DLL, which it can't JIT.
    /// </summary>
    internal static class HitShape
    {
        internal static string StepOf(CodeInstruction ci)
        {
            var m = ci.operand as MethodInfo;
            return StepOf(ci.opcode.Name, ci.opcode.FlowControl.ToString(), m?.Name, m != null && m.ReturnType == typeof(void));
        }

        internal static string StepOf(string opcode, string flow, string called, bool returnsVoid)
        {
            if (flow == "Branch" || flow == "Cond_Branch" || flow == "Return" || flow == "Throw") return "jump";
            if (opcode == "nop") return "nop";
            if (opcode == "ldarg.0") return "arg0";
            if (opcode == "stfld") return "store";
            if (opcode == "ldloc" || opcode.StartsWith("ldloc.")) return "local";
            if ((opcode == "call" || opcode == "callvirt") && called != null) return (returnsVoid ? "void:" : "value:") + called;
            return "other";
        }

        /// <summary>The index of the only step with this name, or -1 if it is missing or repeated.</summary>
        internal static int Only(IList<string> steps, string name)
        {
            int first = steps.IndexOf(name);
            return first >= 0 && steps.Count(s => s == name) == 1 ? first : -1;
        }

        /// <summary>The nearest step at or before this index that isn't a nop, or -1.</summary>
        internal static int Previous(IList<string> steps, int index)
        {
            while (index >= 0 && steps[index] == "nop") index--;
            return index;
        }
    }

    /// <summary>
    /// The count and the log lines DotTickGuard and BulletHitGuard share: one line per new kind of failure, naming the victim, the attacker and where it was thrown.
    /// </summary>
    internal sealed class FailedHitLog
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal const int MaxKinds = 30;   // a kind is one victim, attacker, inflictor, exception type and throw site
        internal const int MaxSites = 30;   // a site is one exception type and throw site, so a second bug still gets a line after one bug has filled the kinds
        const int StackFrames = 6;
        readonly string label, carriedOn;
        readonly HashSet<string> kinds = new HashSet<string>(), sites = new HashSet<string>();
        int total, nextMilestone = 10;
        bool describeFailed;
        internal Type BodyType;
        internal int Total => total;

        // Swappable, because the harness has no Unity objects to read.
        internal static Func<object, bool> Alive = o => o is UnityEngine.Object u ? u != null : o != null;
        internal static Func<object, string> NameOf = o => (o as UnityEngine.Object)?.name ?? o?.ToString();
        internal static Func<object, Type, object> BodyOf = (attacker, bodyType) => attacker is GameObject g && g != null && bodyType != null ? g.GetComponent(bodyType) : null;

        internal FailedHitLog(string label, string carriedOn)
        {
            this.label = label;
            this.carriedOn = carriedOn;
        }

        internal void Count() => total++;

        internal void CouldNotDescribe(Exception f)
        {
            if (describeFailed) return;
            describeFailed = true;
            try { CompatShieldPatcher.Log.LogWarning($"{label}: couldn't describe a failed hit (logged once): " + f); } catch { }
        }

        internal void Note(Exception ex, object victim, object attacker, object inflictor, string detail)
        {
            if (total >= nextMilestone)
            {
                nextMilestone *= 10;
                CompatShieldPatcher.Log.LogWarning($"{label}: {total} failed hits caught so far");
            }
            if (kinds.Count >= MaxKinds && sites.Count >= MaxSites) return;   // both limits full, so nothing more can be logged. Until then every failed hit still formats its stack trace, because its top frame is how a new site is told apart
            var frames = (ex.StackTrace ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(StackFrames).ToArray();
            string victimName = Name(victim), attackerName = Name(attacker), inflictorName = Name(inflictor), kindName = ex.GetType().Name;
            string site = kindName + "|" + (frames.Length > 0 ? frames[0] : "no stack trace");
            bool newSite = sites.Count < MaxSites && sites.Add(site);
            bool newKind = kinds.Count < MaxKinds && kinds.Add(victimName + "|" + attackerName + "|" + inflictorName + "|" + site);
            if (!newSite && !newKind) return;
            object body = Alive(victim) ? Read(victim, "body") : null;
            object attackerBody = Alive(attacker) ? BodyOf(attacker, BodyType) : null;
            CompatShieldPatcher.Log.LogWarning($"{label}: a hit failed and the {carriedOn} carried on ({kindName}: {ex.Message}). " +
                $"Victim {victimName}: body {Has(victim, "body")}, inventory {Has(body, "inventory")}, master {Has(body, "master")}, " +
                $"shield {Num(victim, "shield")}/{Num(victim, "fullShield")}, barrier {Num(victim, "barrier")}, health {Num(victim, "health")}/{Num(victim, "fullHealth")}. " +
                $"Attacker {attackerName}: body {(Alive(attackerBody) ? "ok" : Alive(attacker) ? "null" : "unknown")}, inventory {Has(attackerBody, "inventory")}, input bank {Has(attackerBody, "inputBank")}. " +
                $"Inflictor {inflictorName}. {detail}Failed hit {total} this session. " +
                (newKind && kinds.Count == MaxKinds ? $"That is {MaxKinds} kinds, so from here only a new throw site gets a line; the count carries on. " : "") +
                (newSite && sites.Count == MaxSites ? $"That is {MaxSites} throw sites, the last new one logged. " : "") +
                "Thrown at: " + (frames.Length > 0 ? string.Join(" | ", frames) : "no stack trace"));
        }

        static string Name(object o) => o == null ? "none" : Alive(o) ? NameOf(o) : "destroyed";

        // A field or a property by name; null when the owner or the member is missing.
        internal static object Read(object owner, string member)
        {
            if (!Alive(owner)) return null;
            var t = owner.GetType();
            var f = t.GetField(member, Any);
            return f != null ? f.GetValue(owner) : t.GetProperty(member, Any)?.GetValue(owner, null);
        }

        static string Has(object owner, string member)
        {
            if (!Alive(owner)) return "unknown";
            try { return Alive(Read(owner, member)) ? "ok" : "null"; }
            catch { return "unreadable"; }
        }

        static string Num(object owner, string member)
        {
            try { return Read(owner, member) is float f ? f.ToString("0.#") : "unknown"; }
            catch { return "unknown"; }
        }

        internal void ResetForTest()
        {
            total = 0;
            nextMilestone = 10;
            kinds.Clear();
            sites.Clear();
            describeFailed = false;
        }
    }

    /// <summary>
    /// DotController.EvaluateDotStacksForType deals one damage-over-time tick per attacker in a loop, and FixedUpdate stores the DoT's timer only after that returns. One attacker's
    /// damage throwing therefore ended the loop (later attackers lost their damage and the rented lists never went back to their pools) and skipped the timer store, so the same tick
    /// ran again every physics tick for as long as the error lasted. 933 of the 1,107 damage errors on 2026-11-08 were DoT ticks, 402 of them in one minute. Host only: DoTs tick on the server.
    /// The null itself isn't cured: this stops it repeating and names it in the log.
    /// </summary>
    public static class DotTickGuard
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static bool patched;
        static Harmony patcher;
        static Assembly game;
        static Type dotIndexType;
        static string why;
        internal static readonly FailedHitLog Failures = new FailedHitLog("DoT guard", "tick");
        internal static int Total => Failures.Total;

        public static void Apply(Harmony h, Assembly ror2)
        {
            try
            {
                patcher = h;
                game = ror2;
                var ev = ror2.GetType("RoR2.Run")?.GetEvent("onRunStartGlobal", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) throw new MissingMemberException("RoR2.Run.onRunStartGlobal not found");
                ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(DotTickGuard).GetMethod(nameof(OnRunStart), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("DoT guard: can't watch run starts, not applied: " + e.Message);
            }
        }

        static void OnRunStart(object run)
        {
            if (patched) return;
            patched = true;
            try { PatchGuard(patcher, game.GetType("RoR2.DotController")); }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("DoT guard: can't patch DotController, not applied: " + e.Message); }
        }

        internal static bool PatchGuard(Harmony h, Type controller)
        {
            var index = controller?.GetNestedType("DotIndex", Any);
            var evaluate = index == null ? null : controller.GetMethod("EvaluateDotStacksForType", Any, null, new[] { index, typeof(float), typeof(int).MakeByRefType() }, null);
            if (evaluate == null || evaluate.IsStatic || evaluate.ReturnType != typeof(void))
            {
                CompatShieldPatcher.Log.LogWarning("DoT guard: DotController.EvaluateDotStacksForType(DotIndex, float, out int) not found (game updated?), not applied");
                return false;
            }
            dotIndexType = index;
            Failures.BodyType = controller.Assembly.GetType("RoR2.CharacterBody");
            why = "the transpiler did not run";
            h.Patch(evaluate, transpiler: new HarmonyMethod(typeof(DotTickGuard).GetMethod(nameof(Transpiler), BindingFlags.NonPublic | BindingFlags.Static)));
            if (why != null)
            {
                CompatShieldPatcher.Log.LogWarning($"DoT guard: {why} in DotController.EvaluateDotStacksForType (game updated?), not applied");
                return false;
            }
            CompatShieldPatcher.Log.LogInfo("DoT guard: one attacker's error no longer ends a damage-over-time tick, so a failing tick can't repeat every physics tick");
            return true;
        }

        // The catch goes around the one call that deals one attacker's damage, inside the loop. A Harmony finalizer can't do that: it runs after the loop has been abandoned.
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = instructions.ToList();
            why = FindRange(code.Select(c => HitShape.StepOf(c)).ToList(), out int start, out int end);
            if (why == null)
                why = CatchWrap.WrapRange(code, start, end, generator, typeof(Exception),
                    new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(code[start + 2].opcode, code[start + 2].operand) },   // the controller, the DoT type, then the damage info
                    typeof(DotTickGuard).GetMethod(nameof(Caught), BindingFlags.Public | BindingFlags.Static));
            return code;   // both checks run before anything changes, so a refusal leaves the method as it was
        }

        // Finds this.victimHealthComponent.TakeDamage(damageInfo). A try block has to open on an empty stack, so the stretch must start right after a finished statement: in the game's own
        // code that is the last field set on the damage info, and once R2API.Dot has hooked the method it is the jump that sends DoTs with their own damage code past this call.
        internal static string FindRange(IList<string> steps, out int start, out int end)
        {
            start = end = -1;
            int take = HitShape.Only(steps, "void:TakeDamage");
            if (take < 0) return "the damage call isn't the expected shape";
            if (take < 3 || steps[take - 1] != "local" || steps[take - 2] != "value:get_victimHealthComponent" || steps[take - 3] != "arg0") return "the TakeDamage arguments aren't the expected shape";
            int before = HitShape.Previous(steps, take - 4);
            if (before < 0 || (steps[before] != "store" && steps[before] != "jump")) return "the code before TakeDamage isn't the expected shape";
            if (take + 1 >= steps.Count) return "nothing after the damage call";
            start = take - 3;
            end = take;
            return null;
        }

        /// <summary>The catch block around one attacker's tick. Never throws.</summary>
        public static void Caught(Exception e, object controller, int dotIndex, object damageInfo)
        {
            Failures.Count();
            try
            {
                Failures.Note(e, FailedHitLog.Read(controller, "victimHealthComponent"), FailedHitLog.Read(damageInfo, "attacker"), FailedHitLog.Read(damageInfo, "inflictor"), $"DoT {DotName(dotIndex)}. ");
            }
            catch (Exception f)
            {
                Failures.CouldNotDescribe(f);
            }
        }

        static string DotName(int index)
        {
            if (dotIndexType == null) return index.ToString();
            object named = Enum.ToObject(dotIndexType, index);
            return Enum.IsDefined(dotIndexType, named) ? named.ToString() : index.ToString();
        }

        internal static void ResetForTest()
        {
            Failures.ResetForTest();
            patched = false;
        }
    }

    /// <summary>
    /// BulletAttack.DefaultHitCallbackImplementation lands a bullet's damage inside the code that fires the bullet, so one target's error ended the whole attack and went on up to whoever
    /// fired it. Tinker's Satchel's Motion Tracker fires on a timer it only stores after firing, so the error made it fire again every physics tick (138 errors in one minute on 2026-11-08).
    /// Host only, and only the host's own bullets: a client's bullet reaches the host through HandleBulletDamage, which this doesn't wrap (it has no timer to get stuck, and the 2026-11-08 log has none).
    /// The null itself isn't cured: this stops it repeating and names it in the log.
    /// </summary>
    public static class BulletHitGuard
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static bool patched;
        static Harmony patcher;
        static Assembly game;
        static string why;
        internal static readonly FailedHitLog Failures = new FailedHitLog("Bullet guard", "bullet");
        internal static int Total => Failures.Total;

        public static void Apply(Harmony h, Assembly ror2)
        {
            try
            {
                patcher = h;
                game = ror2;
                var ev = ror2.GetType("RoR2.Run")?.GetEvent("onRunStartGlobal", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) throw new MissingMemberException("RoR2.Run.onRunStartGlobal not found");
                ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(BulletHitGuard).GetMethod(nameof(OnRunStart), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Bullet guard: can't watch run starts, not applied: " + e.Message);
            }
        }

        static void OnRunStart(object run)
        {
            if (patched) return;
            patched = true;
            try { PatchGuard(patcher, game.GetType("RoR2.BulletAttack")); }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Bullet guard: can't patch BulletAttack, not applied: " + e.Message); }
        }

        internal static bool PatchGuard(Harmony h, Type bullet)
        {
            var hit = bullet?.GetNestedType("BulletHit", Any);
            var callback = hit == null ? null : bullet.GetMethod("DefaultHitCallbackImplementation", Any, null, new[] { bullet, hit.MakeByRefType() }, null);
            if (callback == null || !callback.IsStatic || callback.ReturnType != typeof(bool))
            {
                CompatShieldPatcher.Log.LogWarning("Bullet guard: BulletAttack.DefaultHitCallbackImplementation(BulletAttack, ref BulletHit) not found (game updated?), not applied");
                return false;
            }
            Failures.BodyType = bullet.Assembly.GetType("RoR2.CharacterBody");
            why = "the transpiler did not run";
            h.Patch(callback, transpiler: new HarmonyMethod(typeof(BulletHitGuard).GetMethod(nameof(Transpiler), BindingFlags.NonPublic | BindingFlags.Static)));
            if (why != null)
            {
                CompatShieldPatcher.Log.LogWarning($"Bullet guard: {why} in BulletAttack.DefaultHitCallbackImplementation (game updated?), not applied");
                return false;
            }
            CompatShieldPatcher.Log.LogInfo("Bullet guard: one target's error no longer ends a bullet, so an item that fires on a timer can't fire again every physics tick");
            return true;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = instructions.ToList();
            why = FindRange(code.Select(c => HitShape.StepOf(c)).ToList(), out int start, out int end);
            if (why == null)
                why = CatchWrap.WrapRange(code, start, end, generator, typeof(Exception),
                    new[] { new CodeInstruction(code[start].opcode, code[start].operand), new CodeInstruction(code[start + 1].opcode, code[start + 1].operand) },   // the target, then the damage info
                    typeof(BulletHitGuard).GetMethod(nameof(Caught), BindingFlags.Public | BindingFlags.Static));
            return code;   // both checks run before anything changes, so a refusal leaves the method as it was
        }

        // Finds health.TakeDamage(info) through OnHitEnemy(...). OnHitAll comes after a jump target, so it can't share the try block. The stretch must start right after the jump
        // that skips it for a bullet that hit nothing it can damage, because a try block has to open on an empty stack.
        internal static string FindRange(IList<string> steps, out int start, out int end)
        {
            start = end = -1;
            int take = HitShape.Only(steps, "void:TakeDamage"), enemy = HitShape.Only(steps, "void:OnHitEnemy");
            if (take < 0 || enemy < take) return "the damage calls aren't the expected shape";
            if (take < 3 || steps[take - 1] != "local" || steps[take - 2] != "local") return "the TakeDamage arguments aren't the expected shape";
            int before = HitShape.Previous(steps, take - 3);
            if (before < 0 || steps[before] != "jump") return "the code before TakeDamage isn't the expected shape";
            for (int i = take - 2; i <= enemy; i++) if (steps[i] == "jump") return "a jump in the middle of the damage calls";
            if (enemy + 1 >= steps.Count) return "nothing after the damage calls";
            start = take - 2;
            end = enemy;
            return null;
        }

        /// <summary>The catch block around one target's bullet damage. Never throws.</summary>
        public static void Caught(Exception e, object victim, object damageInfo)
        {
            Failures.Count();
            try
            {
                Failures.Note(e, victim, FailedHitLog.Read(damageInfo, "attacker"), FailedHitLog.Read(damageInfo, "inflictor"), "");
            }
            catch (Exception f)
            {
                Failures.CouldNotDescribe(f);
            }
        }

        internal static void ResetForTest()
        {
            Failures.ResetForTest();
            patched = false;
        }
    }

    /// <summary>
    /// Makes a paid ItemRoulette spin cost real gold in a ShareSuite game. ItemRoulette takes the price from CharacterMaster.money only; ShareSuite then writes the shared pool's value back
    /// over every player's money on the next frame, so the charge disappears. This takes the same amount out of the pool. Patches at the first run start, by name, so neither mod is a
    /// hard dependency. Host only: both the charge and ShareSuite's sync run only on the host.
    /// </summary>
    public static class RouletteSpinCost
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal const string SpinTypeName = "ItemRoulette.Core.ServerSpinService", SpinMethodName = "ExecuteSpinOnServer";
        internal const string PoolTypeName = "ShareSuite.MoneySharingHooks", GeneralTypeName = "ShareSuite.GeneralHooks";
        static Harmony harmony;
        static Assembly game;
        static bool done, warned;
        static FieldInfo poolField;
        static MethodInfo sharedOn, multiplayer;
        static PropertyInfo moneyProperty;

        public struct Snapshot { public bool Active; public uint Money; public int Pool; }

        public static void Apply(Harmony h, Assembly ror2)
        {
            try
            {
                harmony = h;
                game = ror2;
                var ev = ror2.GetType("RoR2.Run")?.GetEvent("onRunStartGlobal", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) throw new MissingMemberException("RoR2.Run.onRunStartGlobal not found");
                ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(RouletteSpinCost).GetMethod(nameof(OnRunStart), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Roulette: can't watch run starts, not applied: " + e.Message);
            }
        }

        static void OnRunStart(object run)
        {
            if (done) return;
            done = true;
            try
            {
                var loaded = AppDomain.CurrentDomain.GetAssemblies();
                var roulette = loaded.FirstOrDefault(a => a.GetName().Name == "ItemRoulette");
                var share = loaded.FirstOrDefault(a => a.GetName().Name == "ShareSuite");
                if (roulette == null || share == null) return;
                Patch(harmony, roulette.GetType(SpinTypeName), game.GetType("RoR2.CharacterMaster"), share.GetType(PoolTypeName), share.GetType(GeneralTypeName));
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Roulette: couldn't patch the spin charge, not applied: " + e.Message); }
        }

        internal static bool Patch(Harmony h, Type spinService, Type masterType, Type poolType, Type generalType)
        {
            var target = spinService?.GetMethod(SpinMethodName, Any);
            var parameters = target?.GetParameters();
            var pool = poolType?.GetField("SharedMoneyValue", Any);
            var shared = poolType?.GetMethod("SharedMoneyEnabled", Any, null, Type.EmptyTypes, null);
            var mp = generalType?.GetMethod("IsMultiplayer", Any, null, Type.EmptyTypes, null);
            var money = masterType?.GetProperty("money", Any);
            if (target == null || parameters.Length == 0 || parameters[0].ParameterType != masterType
                || pool == null || !pool.IsStatic || pool.FieldType != typeof(int)
                || shared == null || !shared.IsStatic || shared.ReturnType != typeof(bool)
                || mp == null || !mp.IsStatic || mp.ReturnType != typeof(bool)
                || money == null || money.PropertyType != typeof(uint) || money.GetGetMethod(true) == null)
            {
                CompatShieldPatcher.Log.LogWarning("Roulette: expected ItemRoulette or ShareSuite members not found (mod updated?), not applied");
                return false;
            }
            poolField = pool;
            sharedOn = shared;
            multiplayer = mp;
            moneyProperty = money;
            h.Patch(target, new HarmonyMethod(typeof(RouletteSpinCost).GetMethod(nameof(Prefix), Any)), new HarmonyMethod(typeof(RouletteSpinCost).GetMethod(nameof(Postfix), Any)));
            CompatShieldPatcher.Log.LogInfo("Roulette: paid spins now come out of ShareSuite's shared gold");
            return true;
        }

        /// <summary>The pool's value after a spin. Leaves it alone for a free or rejected spin, and when something else already charged it, so a later mod version can't charge twice.</summary>
        internal static int NewPool(uint before, uint after, int poolBefore, int poolNow)
        {
            if (after >= before || poolNow != poolBefore) return poolNow;
            return (int)Math.Max(0L, (long)poolNow - (before - after));
        }

        static void Prefix(object __0, out Snapshot __state)
        {
            __state = default;
            try
            {
                if (__0 == null || !(bool)sharedOn.Invoke(null, null) || !(bool)multiplayer.Invoke(null, null)) return;
                __state = new Snapshot { Active = true, Money = (uint)moneyProperty.GetValue(__0), Pool = (int)poolField.GetValue(null) };
            }
            catch (Exception e) { WarnOnce(e); }
        }

        static void Postfix(object __0, Snapshot __state)
        {
            if (!__state.Active) return;
            try
            {
                uint after = (uint)moneyProperty.GetValue(__0);
                int poolNow = (int)poolField.GetValue(null);
                int pool = NewPool(__state.Money, after, __state.Pool, poolNow);
                if (pool == poolNow) return;
                poolField.SetValue(null, pool);
                CompatShieldPatcher.Log.LogInfo($"Roulette: ${__state.Money - after} charged to the shared gold pool, ${pool} left");
            }
            catch (Exception e) { WarnOnce(e); }
        }

        static void WarnOnce(Exception e)
        {
            if (warned) return;
            warned = true;
            CompatShieldPatcher.Log.LogWarning("Roulette: couldn't charge the shared gold pool: " + e.Message);
        }

        internal static void ResetForTest()
        {
            done = warned = false;
            poolField = null;
            sharedOn = multiplayer = null;
            moneyProperty = null;
        }
    }

    /// <summary>
    /// Multiplies Acolyte's attack speed. The game keeps two copies of the number: the body prefab's, and a private one on AcolyteBodySwapController that is written onto the body
    /// while Acolyte is transformed. Scaling only the first would lose the bonus for the whole transformation. Runs once, at the first run start, when the catalog is ready and before
    /// any body spawns; prefabs keep the change for the rest of the session. Every PC: skills read the stat on the PC that controls the character.
    /// </summary>
    public static class AcolyteTweak
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal const string BodyName = "AcolyteBody";
        static float multiplier = 1f;
        static Assembly game;
        static bool done;

        // Swappable, because the harness has no Unity objects to read.
        internal static Func<object, Type, object> ComponentOf = (prefab, type) => (prefab as GameObject)?.GetComponentInChildren(type, true);

        internal static bool Wanted(float mult) => mult > 0f && Math.Abs(mult - 1f) > 0.001f;

        public static void Apply(Assembly ror2, float mult)
        {
            try
            {
                if (!Wanted(mult)) return;
                multiplier = mult;
                game = ror2;
                var ev = ror2.GetType("RoR2.Run")?.GetEvent("onRunStartGlobal", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) throw new MissingMemberException("RoR2.Run.onRunStartGlobal not found");
                ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(AcolyteTweak).GetMethod(nameof(OnRunStart), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Acolyte: can't watch run starts, not applied: " + e.Message);
            }
        }

        static void OnRunStart(object run)
        {
            if (done) return;
            done = true;
            try { Scale(game.GetType("RoR2.BodyCatalog"), game.GetType("RoR2.CharacterBody"), game.GetType("RoR2.AcolyteBodySwapController"), multiplier); }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Acolyte: couldn't change attack speed, not applied: " + e.Message); }
        }

        internal static bool Scale(Type catalog, Type bodyType, Type swapType, float mult)
        {
            var find = catalog?.GetMethod("FindBodyPrefab", Any, null, new[] { typeof(string) }, null);
            var baseField = bodyType?.GetField("baseAttackSpeed", Any);
            var levelField = bodyType?.GetField("levelAttackSpeed", Any);
            if (find == null || baseField?.FieldType != typeof(float) || levelField?.FieldType != typeof(float))
            {
                CompatShieldPatcher.Log.LogWarning("Acolyte: expected game members not found (game updated?), not applied");
                return false;
            }
            var prefab = find.Invoke(null, new object[] { BodyName });
            var body = prefab == null ? null : ComponentOf(prefab, bodyType);
            if (body == null)
            {
                CompatShieldPatcher.Log.LogWarning($"Acolyte: {BodyName} not found, not applied");
                return false;
            }
            var swapField = swapType?.GetField("baseAttackSpeed", Any);
            var swap = swapField?.FieldType == typeof(float) ? ComponentOf(prefab, swapType) : null;
            float oldBase = (float)baseField.GetValue(body), oldLevel = (float)levelField.GetValue(body);
            baseField.SetValue(body, oldBase * mult);
            if (oldLevel != 0f) levelField.SetValue(body, oldLevel * mult);
            string transformed = "";
            if (swap != null)
            {
                float oldSwap = (float)swapField.GetValue(swap);
                swapField.SetValue(swap, oldSwap * mult);
                transformed = $", transformed form {oldSwap} -> {oldSwap * mult}";
            }
            else CompatShieldPatcher.Log.LogWarning("Acolyte: the transformed form's attack speed wasn't found, so only the normal form changed");
            CompatShieldPatcher.Log.LogInfo($"Acolyte: base attack speed {oldBase} -> {oldBase * mult}{transformed} ({mult}x)");
            return true;
        }

        internal static void ResetForTest() => done = false;
    }

    /// <summary>
    /// Hypercrit2 2.0.x (NotHypercrit.dll): Crit.GetEffectiveCount throws NullReferenceException on some hits (75x on 2026-10-19,
    /// first seen that session), from inside its IL hook in HealthComponent.TakeDamageProcess, which aborts the whole hit: no
    /// damage, no procs. Count such a roll as no crit instead, so the hit lands. Host only (damage is server-side).
    /// </summary>
    public static class HypercritNullFix
    {
        static int logged;

        public static void Apply(Harmony h, Assembly asm)
        {
            try
            {
                var crit = asm.GetType("NotHypercrit.Crit");
                // Two overloads share the name; the one that throws is (float chance, CharacterBody body, bool forceSingleCrit).
                var m = crit?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(x => x.Name == "GetEffectiveCount" && x.ReturnType == typeof(float) && x.GetParameters().Length == 3
                        && x.GetParameters()[0].ParameterType == typeof(float) && x.GetParameters()[2].ParameterType == typeof(bool));
                if (m == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Hypercrit2 null fix: NotHypercrit.Crit.GetEffectiveCount(float, CharacterBody, bool) not found (mod updated?), not applied");
                    return;
                }
                h.Patch(m, finalizer: new HarmonyMethod(typeof(HypercritNullFix).GetMethod(nameof(Finalizer), BindingFlags.NonPublic | BindingFlags.Static)));
                CompatShieldPatcher.Log.LogInfo("Hypercrit2: a crit roll that throws on a hit now counts as no crit instead of cancelling the whole hit");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Hypercrit2 null fix failed to apply: " + e);
            }
        }

        // Returning null swallows the exception; anything but the NullReferenceException is handed back untouched.
        internal static Exception Finalizer(Exception __exception, ref float __result)
        {
            if (!(__exception is NullReferenceException)) return __exception;
            __result = 0f;
            if (logged++ == 0)
                CompatShieldPatcher.Log.LogInfo("Hypercrit2: its crit roll threw on a hit; counted as no crit so the hit still lands (shown once)");
            return null;
        }
    }

    /// <summary>
    /// [3. Balance] DisableGenetics: the Artifact of Genetics (Rico's GeneticArtifact mod) stays installed but can't run: a vote
    /// that would enable it is turned into "off" in RunArtifactManager.SetArtifactEnabledServer (the host decides artifacts), and
    /// its lobby choice is hidden (RuleChoiceDef.excludeByDefault) once RuleCatalog has built the choices. With the artifact
    /// off, the mod's own hooks (one on every hit) return at their first line. Host for the force-off; every PC for the hiding.
    /// </summary>
    public static class ArtifactDisabler
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal const string GeneticsToken = "GENETIC_ARTIFACT_NAME_TOKEN";
        static int logged;
        internal static int Hidden;
        internal static Func<object, string> TokenOf = def => def == null ? null : def.GetType().GetField("nameToken")?.GetValue(def) as string;   // the harness swaps in a stub
        static Assembly ror2Asm;

        public static void Apply(Harmony h, Assembly ror2)
        {
            try
            {
                ror2Asm = ror2;
                var artifactDef = ror2.GetType("RoR2.ArtifactDef");
                var set = artifactDef == null ? null : ror2.GetType("RoR2.RunArtifactManager")?.GetMethod("SetArtifactEnabledServer", Any, null, new[] { artifactDef, typeof(bool) }, null);
                var init = ror2.GetType("RoR2.RuleCatalog")?.GetMethod("Init", BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (set == null || init == null || set.IsStatic)
                {
                    CompatShieldPatcher.Log.LogWarning("Artifact of Genetics switch: RunArtifactManager.SetArtifactEnabledServer(ArtifactDef, bool) or RuleCatalog.Init() not found (game updated?), not applied");
                    return;
                }
                h.Patch(set, prefix: M(nameof(SetPrefix)));
                h.Patch(init, postfix: M(nameof(HideChoices)));
                CompatShieldPatcher.Log.LogInfo("Artifact of Genetics: kept off for every run and hidden from the lobby's artifact list (the GeneticArtifact mod stays installed; " +
                    "[3. Balance] DisableGenetics=false brings it back)");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Artifact of Genetics switch failed to apply: " + e);
            }
        }

        static HarmonyMethod M(string name) => new HarmonyMethod(typeof(ArtifactDisabler).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));

        // Prefix on RunArtifactManager.SetArtifactEnabledServer(ArtifactDef artifactDef, bool newEnabled). Never throws.
        static void SetPrefix(object artifactDef, ref bool newEnabled)
        {
            try
            {
                if (!newEnabled || TokenOf(artifactDef) != GeneticsToken) return;
                newEnabled = false;
                if (logged++ == 0)
                    CompatShieldPatcher.Log.LogInfo("Artifact of Genetics: a vote tried to enable it; kept off (shown once)");
            }
            catch { }
        }

        // Postfix on RuleCatalog.Init: mark the Genetics choice excludeByDefault so the lobby doesn't list it.
        static void HideChoices()
        {
            try
            {
                var ror2 = ror2Asm;
                var choices = ror2.GetType("RoR2.RuleCatalog").GetField("allChoicesDefs", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as System.Collections.IEnumerable;
                var choiceType = ror2.GetType("RoR2.RuleChoiceDef");
                var indexField = choiceType?.GetField("artifactIndex", Any);
                var excludeField = choiceType?.GetField("excludeByDefault", Any);
                var getDef = ror2.GetType("RoR2.ArtifactCatalog")?.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == "GetArtifactDef" && m.GetParameters().Length == 1);
                if (choices == null || indexField == null || excludeField == null || getDef == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Artifact of Genetics switch: the rule catalog looks different (game updated?); the artifact is still forced off but stays visible in the lobby");
                    return;
                }
                Hidden = HideIn(choices, c =>
                {
                    var idx = indexField.GetValue(c);
                    return Convert.ToInt32(idx) < 0 ? null : getDef.Invoke(null, new[] { idx });
                }, c => excludeField.SetValue(c, true));
                if (Hidden > 0) CompatShieldPatcher.Log.LogInfo($"Artifact of Genetics: hidden from the lobby ({Hidden} choice(s))");
                else CompatShieldPatcher.Log.LogWarning("Artifact of Genetics switch: no lobby choice for it was found (mod not loaded, or registered after the catalog); it is still forced off");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Artifact of Genetics switch: couldn't hide the lobby choice: " + e.Message);
            }
        }

        /// <summary>Marks every choice whose artifact is Genetics; returns how many. Pure, so the harness can drive it.</summary>
        internal static int HideIn(System.Collections.IEnumerable choices, Func<object, object> artifactOf, Action<object> exclude)
        {
            int n = 0;
            foreach (var c in choices)
            {
                object def;
                try { def = artifactOf(c); } catch { continue; }
                if (def != null && TokenOf(def) == GeneticsToken) { exclude(c); n++; }
            }
            return n;
        }

        internal static void ResetForTest() { logged = 0; Hidden = 0; }
    }

    /// <summary>
    /// Gasoline (GlobalEventManager.ProcIgniteOnKill) blasts everything near a kill; a kill by that blast runs
    /// OnCharacterDeath and Gasoline again, nested inside the first. Vanilla spends a handful of frames per link, but
    /// this pack runs ~70 hooks on every TakeDamage and OnCharacterDeath, so each link costs ~250 frames and a dense
    /// Multitudes pack overflows the stack within a few links (StackOverflowException, and "Failed at InvokeHandler,
    /// probably malformed packet!" when it hits inside a network message). Keep the chain, stop the nesting: a Gasoline
    /// proc that starts while another is still running is queued, and when the outermost proc returns the queue is run
    /// one proc at a time (procs those add join the same loop). Same frame as vanilla, so every victim is still there
    /// (Unity destroys objects at the end of the frame), but the stack never holds more than about two links.
    /// </summary>
    public static class GasolineChainFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        public const int MaxDepth = 1;      // Gasoline procs allowed on the stack at once
        const int MaxQueued = 2048;
        static int depth, deferredTotal, skippedGone;
        static bool draining;
        static MethodInfo target;
        static readonly List<object[]> queued = new List<object[]>();
        static readonly Dictionary<string, int> failures = new Dictionary<string, int>();

        public static void Apply(Harmony h, Assembly ror2)
        {
            try
            {
                var proc = ror2?.GetType("RoR2.GlobalEventManager")?.GetMethods(Any)
                    .FirstOrDefault(m => m.Name == "ProcIgniteOnKill" && m.IsStatic && m.GetParameters().Length == 4);
                var tick = ror2?.GetType("RoR2.RoR2Application")?.GetEvent("onFixedUpdate", Any);
                if (proc == null || tick == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Gasoline chain fix: expected game members not found, not applied");
                    return;
                }
                target = proc;
                h.Patch(proc, prefix: Method(nameof(Prefix)), finalizer: Method(nameof(Finalizer)));
                tick.AddEventHandler(null, new Action(RunDeferred));
                CompatShieldPatcher.Log.LogInfo("Gasoline: chain reactions now run one blast at a time instead of nesting");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Gasoline chain fix failed to apply: " + e);
            }
        }

        static HarmonyMethod Method(string name) => new HarmonyMethod(typeof(GasolineChainFix).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));

        static bool Prefix(object[] __args, out bool __state)
        {
            if (depth >= MaxDepth)
            {
                __state = false;
                if (queued.Count < MaxQueued) queued.Add((object[])__args.Clone());
                if (deferredTotal++ == 0)
                    CompatShieldPatcher.Log.LogInfo("Gasoline: a chain reaction was run one blast at a time (shown once)");
                return false;
            }
            depth++;
            __state = true;
            return true;
        }

        // Harmony runs this on both the normal and the exception path; it must never throw (a throw would run it again).
        // It returns nothing, so Harmony passes any exception on with "rethrow" and its stack trace survives. While a stack
        // overflow unwinds, queued blasts wait for the next physics tick (RunDeferred) instead of running on a full stack.
        static void Finalizer(Exception __exception, bool __state)
        {
            try
            {
                if (__state) depth--;
                if (__state && depth == 0 && !draining && queued.Count > 0 && !(__exception is StackOverflowException)) Drain();
            }
            catch
            {
            }
        }

        static void Drain()
        {
            draining = true;
            try
            {
                for (int i = 0; i < queued.Count && i < MaxQueued; i++)   // procs queued while looping join this loop
                    RunOne(queued[i]);
            }
            finally
            {
                queued.Clear();
                draining = false;
            }
        }

        static void RunOne(object[] args)
        {
            if (args.Length > 2 && IsGone(args[2]))
            {
                skippedGone++;
                return;
            }
            try
            {
                target.Invoke(null, args);   // through the patch: depth 0 -> 1, deeper links queue into the running loop
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                var key = inner.GetType().FullName + " @ " + (inner.TargetSite?.DeclaringType?.FullName ?? "?") + "." + inner.TargetSite?.Name;
                failures.TryGetValue(key, out var n);
                failures[key] = n + 1;
                if (n < 3)
                    CompatShieldPatcher.Log.LogWarning($"Gasoline: a chained blast threw (#{n + 1} of this error; first 3 shown): {inner}");
            }
        }

        // A destroyed Unity object compares equal to null; kept out of line so the test harness (no Unity) can run RunOne.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static bool IsGone(object body) => body is UnityEngine.Object o && !o;

        /// <summary>Fallback on RoR2Application.onFixedUpdate: runs anything left queued (normally the queue is already empty).</summary>
        public static void RunDeferred()
        {
            if (depth == 0 && !draining && queued.Count > 0) Drain();
        }
    }

    /// <summary>
    /// StandaloneAncientScepter 1.1.37, TreebotFlower2_2.On_TreebotFlower2RootPulse: an On. hook on every REX Tangling
    /// Growth flower's RootPulse (scepter or not). It reads self.owner?.GetComponent<CharacterBody>().skillLocator...
    /// with a plain C# null check (IL_0019 dup / brtrue.s). Once the owner body is destroyed (REX died, respawned, left the
    /// stage; also a killed Vengeance umbra REX) the flower still holds that destroyed GameObject, GetComponent throws a
    /// NullReferenceException before the hook calls orig, and the flower never roots or damages again: every pulse throws.
    /// The game treats a destroyed owner as no owner (HealPulse checks "owner ?", a hit with no attacker is fine), and so
    /// does the hook's own null branch (normal radius, no scepter debuffs, orig runs). So clear the flower's owner field
    /// once that object is destroyed. The field is read only by RootPulse (the hit's attacker) and HealPulse, and a
    /// destroyed object already counts as null to both. RootPulse runs on the server only, so the host having this is enough.
    /// </summary>
    public static class ScepterFlowerFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static FieldInfo ownerField;
        static int cleared;
        static bool failed;

        public static void Apply(Harmony h, Assembly asm)
        {
            try
            {
                var hook = asm.GetType("AncientScepter.TreebotFlower2_2")?.GetMethods(Any)
                    .FirstOrDefault(m => m.Name == "On_TreebotFlower2RootPulse" && m.GetParameters().Length == 2);
                var self = hook?.GetParameters()[1];
                var field = self != null && self.Name == "self" ? AccessTools.Field(self.ParameterType, "owner") : null;
                if (hook == null || field == null || field.IsStatic
                    || !typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))
                {
                    CompatShieldPatcher.Log.LogWarning("Ancient Scepter flower fix: On_TreebotFlower2RootPulse or the flower's owner field not found (mod updated?), not applied");
                    return;
                }
                if (ownerField != null)
                {
                    CompatShieldPatcher.Log.LogWarning("Ancient Scepter flower fix: already applied, skipped a second apply");
                    return;
                }
                ownerField = field;   // set only once everything checked out, so a later failed lookup can't undo a working fix
                h.Patch(hook, prefix: new HarmonyMethod(typeof(ScepterFlowerFix).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static)));
                CompatShieldPatcher.Log.LogInfo("Ancient Scepter: REX flowers keep working after their owner's body is gone");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Ancient Scepter flower fix failed to apply: " + e);
            }
        }

        // Before the hook's own "owner == null" check. Never throws: a failure just leaves the flower as it was.
        static void Prefix(object self)
        {
            if (failed || self == null) return;
            try
            {
                var owner = ownerField.GetValue(self);
                if (owner == null || !IsGone(owner)) return;
                ownerField.SetValue(self, null);
                if (cleared++ == 0)
                    CompatShieldPatcher.Log.LogInfo("Ancient Scepter: a REX flower outlived its owner's body and keeps working (shown once)");
            }
            catch (Exception e)
            {
                failed = true;
                CompatShieldPatcher.Log.LogError("Ancient Scepter flower fix: couldn't check a flower's owner, fix off for this session (logged once): " + e);
            }
        }

        // A destroyed Unity object compares equal to null; kept out of line like GasolineChainFix.IsGone.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static bool IsGone(object o) => o is UnityEngine.Object u && !u;
    }

    /// <summary>
    /// REX's Tangling Growth flower (EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile, also used by Ancient
    /// Scepter's Chaotic Growth) roots each enemy it pulls in with Entangle and keeps it in rootedBodies. Its OnExit (run
    /// from EntityStateMachine.OnDestroy when the flower expires) removes Entangle from all of them in one foreach. An
    /// enemy that died and was destroyed while rooted stays in that list; RemoveBuff on it still runs SetBuffCount and, on
    /// a host, OnClientBuffsChanged, where EliteReworks2 2.1.1 (Overloading.cs:183, Celestine.cs:62) and
    /// ClassicItemsReturns 3.7.0 (HitList.cs:91) call GetComponent on the destroyed body after orig and throw. OnExit stops
    /// there: every enemy after it keeps Entangle (rooted until it dies) and the exit sound and effect are skipped.
    /// Run OnExit's loop in a prefix instead: skip bodies that are gone (nothing left to unroot), remove Entangle from each
    /// living one on its own so one failure can't skip the rest, and clear the list first so the game's own loop has
    /// nothing left to do. The game's OnExit then plays the exit sound and effect and finishes as usual.
    /// </summary>
    public static class FlowerRootFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static FieldInfo rootedBodies, entangle;
        static MethodInfo removeBuff;
        static int skippedGone;
        static readonly Dictionary<string, int> failures = new Dictionary<string, int>();
        static readonly HashSet<string> errors = new HashSet<string>();
        internal static Func<object, bool> IsGone = UnityGone;   // swapped out by the test harness

        public static void Apply(Harmony h, Assembly ror2)
        {
            try
            {
                var flower = ror2?.GetType("EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile");
                var buffDef = ror2?.GetType("RoR2.BuffDef");
                var onExit = flower?.GetMethod("OnExit", Any, null, Type.EmptyTypes, null);
                rootedBodies = flower?.GetField("rootedBodies", Any);
                entangle = ror2?.GetType("RoR2.RoR2Content+Buffs")?.GetField("Entangle", Any);
                removeBuff = buffDef == null ? null
                    : ror2.GetType("RoR2.CharacterBody")?.GetMethod("RemoveBuff", Any, null, new[] { buffDef }, null);
                if (onExit == null || rootedBodies == null || entangle == null || removeBuff == null
                    || !typeof(System.Collections.IList).IsAssignableFrom(rootedBodies.FieldType))
                {
                    CompatShieldPatcher.Log.LogWarning("REX flower fix: expected game members not found, not applied");
                    return;
                }
                var original = PatchProcessor.ReadMethodBody(onExit);
                if (original == null || !LoopMatches(original))
                {
                    CompatShieldPatcher.Log.LogWarning("REX flower fix: unexpected IL in TreebotFlower2Projectile.OnExit (game updated?), left unchanged");
                    return;
                }
                h.Patch(onExit, prefix: new HarmonyMethod(typeof(FlowerRootFix).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static)));
                CompatShieldPatcher.Log.LogInfo("REX flower: an expiring flower now unroots every enemy it caught, even when one of them is already gone");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("REX flower fix failed to apply: " + e);
            }
        }

        // The prefix redoes OnExit's loop, so it only takes over the loop it was written for (game 1.5.0): rootedBodies
        // read twice (null check, then the foreach) and set to null once, and between the foreach read and that store
        // nothing but the List enumerator calls and exactly one RemoveBuff(Entangle). Code after the loop (exit sound,
        // effect, base.OnExit) may change freely: the game still runs it. A changed loop is left to the game.
        internal static bool LoopMatches(IEnumerable<KeyValuePair<OpCode, object>> body)
        {
            var code = body.Where(ci => ci.Key != OpCodes.Nop).ToList();
            int reads = 0, clears = 0, removes = 0, loopStart = -1, loopEnd = -1;
            for (int i = 0; i < code.Count; i++)
            {
                if (!(code[i].Value is FieldInfo f) || f.Name != "rootedBodies") continue;
                if (code[i].Key == OpCodes.Ldfld) { reads++; loopStart = i; }
                else if (code[i].Key == OpCodes.Stfld && i > 0 && code[i - 1].Key == OpCodes.Ldnull) { clears++; loopEnd = i; }
                else return false;
            }
            if (reads != 2 || clears != 1 || loopStart < 0 || loopEnd < loopStart) return false;
            for (int i = loopStart + 1; i < loopEnd; i++)
            {
                var op = code[i].Key;
                if (op != OpCodes.Call && op != OpCodes.Callvirt && op != OpCodes.Calli && op != OpCodes.Newobj) continue;
                if (!(code[i].Value is MethodInfo m)) return false;
                if (m.Name == "RemoveBuff" && m.DeclaringType?.FullName == "RoR2.CharacterBody"
                    && code[i - 1].Key == OpCodes.Ldsfld && code[i - 1].Value is FieldInfo buff && buff.Name == "Entangle")
                {
                    removes++;
                    continue;
                }
                bool enumerator = (m.Name == "GetEnumerator" || m.Name == "get_Current" || m.Name == "MoveNext" || m.Name == "Dispose")
                    && (m.DeclaringType == typeof(IDisposable) || m.DeclaringType?.Namespace == "System.Collections.Generic");
                if (!enumerator) return false;
            }
            return removes == 1;
        }

        // Does OnExit's unroot loop, then lets the game's OnExit run (it finds the list cleared and skips its own loop).
        // Never throws: anything unexpected leaves the loop to the game, as before.
        static void Prefix(object __instance)
        {
            object[] bodies;
            object buff;
            try
            {
                var list = rootedBodies.GetValue(__instance) as System.Collections.IList;
                if (list == null || list.Count == 0) return;   // a client's flower (the list is server-only), or nothing caught
                buff = entangle.GetValue(null);
                bodies = new object[list.Count];
                list.CopyTo(bodies, 0);
                rootedBodies.SetValue(__instance, null);       // before unrooting, so no body ever loses Entangle twice
            }
            catch (Exception e)
            {
                Once("read", "REX flower fix: couldn't take over the unroot loop, leaving it to the game: " + e);
                return;
            }
            foreach (var body in bodies)
                Unroot(body, buff);
        }

        static void Unroot(object body, object buff)
        {
            try
            {
                if (IsGone(body))
                {
                    if (skippedGone++ == 0)
                        CompatShieldPatcher.Log.LogInfo("REX flower: skipped unrooting an enemy that was already gone (shown once)");
                    return;
                }
                removeBuff.Invoke(body, new[] { buff });   // RemoveBuff(BuffDef), as OnExit calls it; mods' RemoveBuff hooks still run
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                var key = inner.GetType().FullName + " @ " + (inner.TargetSite?.DeclaringType?.FullName ?? "?") + "." + inner.TargetSite?.Name;
                failures.TryGetValue(key, out var n);
                failures[key] = n + 1;
                if (n < 3)
                    CompatShieldPatcher.Log.LogWarning($"REX flower: unrooting one enemy threw, the others were still unrooted (#{n + 1} of this error; first 3 shown): {inner}");
            }
        }

        static void Once(string key, string message)
        {
            if (errors.Add(key)) CompatShieldPatcher.Log.LogError(message + " (logged once)");
        }

        // A destroyed Unity object compares equal to null; kept out of line so the test harness (no Unity) can run Unroot.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static bool UnityGone(object body) => body == null || (body is UnityEngine.Object o && !o);
    }


    /// <summary>
    /// Vanilla bug (game 1.5.0): SetStateOnHurt fills buffDurationControllers (the body's AlterBuffDuration list) only in
    /// Start(), but HealthComponent.Awake already lists it as a damage receiver, so a body can be hit before its Start has
    /// run: one spawned earlier in the same frame (with lag Unity runs several physics steps per frame, and Start only
    /// runs at the next frame), or one whose SetStateOnHurt is disabled (Start never runs then). A freeze, stun or shock
    /// on it throws a NullReferenceException at GetModifiedDuration's loop head, inside TakeDamageProcess's
    /// OnTakeDamageServer loop, so the rest of that hit is skipped: damage numbers, on-hit handlers and, on a killing
    /// blow, OnKilledServer and HandleCharacterDeath (no gold, XP or on-kill procs). When the list is still empty, fill
    /// it exactly as Start does (GetComponents&lt;AlterBuffDuration&gt;()); if that can't be done, use the unmodified
    /// duration. SetStun/OverrideStun/SetFrozen/SetMidasTouchFrozen/SetShockInternal all start with GetModifiedDuration,
    /// so this one prefix covers every caller. The stun state itself is only queued (EntityStateMachine.nextState); on a
    /// body that hasn't started, its EntityStateMachine.Start decides whether it enters that state or its spawn state.
    /// </summary>
    public static class StunDurationFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static FieldInfo controllers;
        static MethodInfo getComponents;   // Component.GetComponents<AlterBuffDuration>(), the call Start() makes
        static int filled, unmodified, lookupErrors;
        internal static Func<object, Array> FindControllers = ComponentsOf;   // swapped out by the test harness

        public static void Apply(Harmony h, Assembly ror2)
        {
            try
            {
                var soh = ror2?.GetType("RoR2.SetStateOnHurt");
                var alter = ror2?.GetType("RoR2.AlterBuffDuration");
                controllers = soh?.GetField("buffDurationControllers", Any);
                var target = soh?.GetMethod("GetModifiedDuration", Any, null, new[] { typeof(float), typeof(Type) }, null);
                getComponents = alter == null ? null : typeof(Component).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "GetComponents" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0)
                    ?.MakeGenericMethod(alter);
                if (controllers == null || alter == null || controllers.FieldType != alter.MakeArrayType() || target == null
                    || target.ReturnType != typeof(float) || getComponents == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Stun duration fix: expected game members not found, not applied");
                    return;
                }
                h.Patch(target, prefix: new HarmonyMethod(typeof(StunDurationFix).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static)));
                CompatShieldPatcher.Log.LogInfo("Freeze/stun: hits on a body that has only just spawned no longer crash (and lose the kill)");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Stun duration fix failed to apply: " + e);
            }
        }

        // Runs before every freeze/stun/shock duration lookup; once Start has run it only compares one field with null.
        // Never throws: anything unexpected falls back to the unmodified duration, and a failed log line changes nothing.
        static bool Prefix(object __instance, object ___buffDurationControllers, float duration, ref float __result)
        {
            if (___buffDurationControllers != null) return true;
            bool stored = false;
            try
            {
                var found = FindControllers(__instance);
                if (found != null && controllers.FieldType.IsInstanceOfType(found))
                {
                    controllers.SetValue(__instance, found);   // what Start() stores; Start later stores the same list again
                    stored = true;
                }
            }
            catch (Exception e)
            {
                try
                {
                    if (lookupErrors++ == 0)
                        CompatShieldPatcher.Log.LogError("Freeze/stun: couldn't look up a body's duration modifiers, used the unmodified duration (logged once): " + e);
                }
                catch { }
            }
            if (stored)
            {
                try
                {
                    if (filled++ == 0)
                        CompatShieldPatcher.Log.LogInfo("Freeze/stun: set up a just-spawned body's duration modifiers early so the hit could finish (shown once)");
                }
                catch { }
                return true;   // vanilla runs with the body's own modifiers
            }
            __result = duration;   // what vanilla returns for a body with no AlterBuffDuration
            try
            {
                if (unmodified++ == 0)
                    CompatShieldPatcher.Log.LogInfo("Freeze/stun: used the unmodified duration for a body that wasn't set up yet (shown once)");
            }
            catch { }
            return false;
        }

        // Unity calls kept separate and never inlined: the test harness runs outside Unity. A destroyed body is Unity-null.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static Array ComponentsOf(object setStateOnHurt) =>
            setStateOnHurt is Component c && c ? (Array)getComponents.Invoke(c, null) : null;
    }

    /// <summary>
    /// Keeps chosen interactables on the stage under Artifact of Sacrifice. The game drops a card whose InteractableSpawnCard has
    /// skipSpawnWhenSacrificeArtifactEnabled set in two places: SacrificeArtifactManager's stage filter and InteractableSpawnCard.Spawn.
    /// Clearing the flag on the listed cards, just before that filter runs, covers both. The spawn cards are shared, so the cleared
    /// flag lasts the session, and only Sacrifice reads it. The filter runs on the host only, so this is host-side.
    /// The game hands a category's whole weight to whatever cards it has left, so a few kept cards out of the big Chests category
    /// would otherwise get that category's whole share of the stage's picks: KeptInteractableWeight scales a category that keeps some cards and loses
    /// others. The selection is a per-stage copy, so that doesn't stack.
    /// </summary>
    public static class SacrificeKeepTweak
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static string[] keep = new string[0];
        static float keptWeight = 1f;
        static FieldInfo categories, catName, catWeight, catCards, skipFlag, weightScalar;
        static MethodInfo getSpawnCard;
        static Type spawnCardType;
        static bool errorLogged;
        static readonly HashSet<string> cleared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> logged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Swappable so the harness can use stand-in spawn cards (Unity objects can't be created outside the game).
        internal static Func<object, string> NameOf = sc => (sc as UnityEngine.Object)?.name;

        internal static string[] ParseList(string raw) =>
            (raw ?? "").Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        // A prefix match, so iscShrineChance also covers iscShrineChanceSandy and iscShrineChanceSnowy.
        static bool Listed(string cardName) => keep.Any(k => cardName.StartsWith(k, StringComparison.OrdinalIgnoreCase));

        static void Configure(string list, float weight)
        {
            keep = ParseList(list);
            keptWeight = weight >= 0f ? weight : 1f;
        }

        internal static void ResetForTest(string list, float weight)
        {
            Configure(list, weight);
            errorLogged = false;
            cleared.Clear();
            logged.Clear();
        }

        internal static bool Bind(Type selection, Type category, Type card, Type spawnCard)
        {
            categories = selection?.GetField("categories", Any);
            catName = category?.GetField("name", Any);
            catWeight = category?.GetField("selectionWeight", Any);
            catCards = category?.GetField("cards", Any);
            getSpawnCard = card?.GetMethod("GetSpawnCard", Any, null, Type.EmptyTypes, null);
            skipFlag = spawnCard?.GetField("skipSpawnWhenSacrificeArtifactEnabled", Any);
            weightScalar = spawnCard?.GetField("weightScalarWhenSacrificeArtifactEnabled", Any);
            spawnCardType = spawnCard;
            return categories != null && catName != null && catWeight != null && catCards != null && getSpawnCard != null && skipFlag != null && weightScalar != null;
        }

        public static void Apply(Harmony h, Assembly ror2, string list, float weight)
        {
            try
            {
                Configure(list, weight);
                if (keep.Length == 0) return;
                var selection = ror2?.GetType("RoR2.DirectorCardCategorySelection");
                var director = ror2?.GetType("RoR2.SceneDirector");
                var filter = selection == null || director == null ? null : ror2.GetType("RoR2.Artifacts.SacrificeArtifactManager")
                    ?.GetMethod("OnGenerateInteractableCardSelection", Any, null, new[] { director, selection }, null);
                if (filter == null || !Bind(selection, ror2.GetType("RoR2.DirectorCardCategorySelection+Category"), ror2.GetType("RoR2.DirectorCard"), ror2.GetType("RoR2.InteractableSpawnCard")))
                {
                    CompatShieldPatcher.Log.LogWarning("Sacrifice interactables: expected game members not found, not applied");
                    return;
                }
                h.Patch(filter, prefix: new HarmonyMethod(typeof(SacrificeKeepTweak).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static)));
                CompatShieldPatcher.Log.LogInfo($"Sacrifice: these interactables stay on: {string.Join(", ", keep)} (categories that also lose cards at x{keptWeight} weight)");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Sacrifice interactable exceptions failed to apply: " + e);
            }
        }

        // Harmony binds this by name to the game method's second parameter. Never throws: the game's own filter always runs after it.
        static void Prefix(object dccs)
        {
            try
            {
                Adjust(dccs);
            }
            catch (Exception e)
            {
                if (!errorLogged) { errorLogged = true; CompatShieldPatcher.Log.LogError("Sacrifice interactables: couldn't adjust a stage (logged once): " + e); }
            }
        }

        internal static void Adjust(object selection)
        {
            if (!(categories.GetValue(selection) is Array cats)) return;
            var keptNow = new List<string>();
            var blockedNow = new List<string>();
            var mixed = new List<(string label, float before, float after)>();
            float live = 0f;
            for (int i = 0; i < cats.Length; i++)
            {
                var boxed = cats.GetValue(i);
                var label = catName.GetValue(boxed) as string ?? "?";
                if (!(catCards.GetValue(boxed) is Array cards)) continue;
                bool kept = false, lost = false, survives = false;
                foreach (var card in cards)
                {
                    if (card == null) continue;
                    var sc = getSpawnCard.Invoke(card, null);
                    if (sc == null || !spawnCardType.IsInstanceOfType(sc)) { survives = true; continue; }
                    var name = NameOf(sc) ?? "?";
                    if ((bool)skipFlag.GetValue(sc))
                    {
                        if (Listed(name))
                        {
                            skipFlag.SetValue(sc, false);
                            if ((float)weightScalar.GetValue(sc) <= 0f) weightScalar.SetValue(sc, 1f);
                            cleared.Add(name);
                            kept = survives = true;
                            if (logged.Add(name)) keptNow.Add(label + "/" + name);
                        }
                        else
                        {
                            lost = true;
                            if (logged.Add(name)) blockedNow.Add(label + "/" + name);
                        }
                    }
                    else
                    {
                        survives = true;
                        if (cleared.Contains(name)) kept = true;
                    }
                }
                float before = (float)catWeight.GetValue(boxed), after = before;
                if (kept && lost)
                {
                    if (Math.Abs(keptWeight - 1f) > 0.001f)
                    {
                        after = before * keptWeight;
                        catWeight.SetValue(boxed, after);
                        cats.SetValue(boxed, i);
                    }
                    if (logged.Add("category:" + label)) mixed.Add((label, before, after));
                }
                if (survives) live += after;
            }
            foreach (var m in mixed)
                CompatShieldPatcher.Log.LogInfo($"Sacrifice: category {m.label} keeps some cards and loses others, weight {m.before} now {m.after}, about {(live > 0f ? m.after / live * 100f : 0f):0}% of this stage's picks");
            if (keptNow.Count > 0) CompatShieldPatcher.Log.LogInfo("Sacrifice: kept (first seen this stage): " + string.Join(", ", keptNow));
            if (blockedNow.Count > 0) CompatShieldPatcher.Log.LogInfo("Sacrifice: still blocked (first seen this stage; add a name to KeepInteractables to keep it): " + string.Join(", ", blockedNow));
        }
    }

    /// <summary>
    /// Artifact of Sacrifice drop rate. Each kill rolls once, in SacrificeArtifactManager.OnServerCharacterDeath:
    ///   Util.CheckRoll(Util.GetExpAdjustedDropChancePercent(5, victim), 0, null)     (vanilla: 5% x log2(spawnValue + 1))
    /// RiskyMod's "Sacrifice - No Drop Chance Scaling" IL-hooks that method: it swaps the 5 for its own chance, then forces
    /// whatever GetExpAdjustedDropChancePercent returned (when above 0) to exactly that chance: a flat 8% per kill with
    /// Classic Items Returns installed. ShareSuite's SacrificeFix divides the chance by the player count inside
    /// GetExpAdjustedDropChancePercent, so RiskyMod's clamp overwrites it, and it would overwrite a change to the 5 or to
    /// that method's result the same way. So scale the chance where it is used: right before that one CheckRoll call,
    /// park its luck and master arguments in locals, multiply the chance, and put them back. Works whichever IL hook runs
    /// first, keeps other mods' CheckRoll hooks, and only the host rolls (clients need nothing).
    /// </summary>
    public static class SacrificeDropTweak
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static float multiplier = 1f;
        static bool matched, noted;

        public static void Apply(Harmony h, Assembly ror2, float mult)
        {
            try
            {
                var report = ror2?.GetType("RoR2.DamageReport");
                var death = report == null ? null : ror2.GetType("RoR2.Artifacts.SacrificeArtifactManager")
                    ?.GetMethod("OnServerCharacterDeath", Any, null, new[] { report }, null);
                if (death == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Sacrifice drop chance: SacrificeArtifactManager.OnServerCharacterDeath not found, not applied");
                    return;
                }
                multiplier = mult;
                matched = false;
                h.Patch(death, transpiler: new HarmonyMethod(typeof(SacrificeDropTweak).GetMethod(nameof(Transpiler), BindingFlags.NonPublic | BindingFlags.Static)));
                if (matched)
                    CompatShieldPatcher.Log.LogInfo($"Sacrifice: item drop chance per kill now x{mult} (after RiskyMod/ShareSuite)");
                else
                    CompatShieldPatcher.Log.LogWarning("Sacrifice drop chance: unexpected IL (maybe a game update), left unchanged");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Sacrifice drop chance tweak failed to apply: " + e);
            }
        }

        // Util.CheckRoll(float percentChance, float luck, CharacterMaster effectOriginMaster)
        static bool IsRoll(CodeInstruction ci)
        {
            if ((ci.opcode != OpCodes.Call && ci.opcode != OpCodes.Callvirt) || !(ci.operand is MethodInfo m)
                || m.Name != "CheckRoll" || m.DeclaringType?.FullName != "RoR2.Util" || m.ReturnType != typeof(bool))
                return false;
            var p = m.GetParameters();
            return p.Length == 3 && p[0].ParameterType == typeof(float) && p[1].ParameterType == typeof(float)
                && p[2].ParameterType.FullName == "RoR2.CharacterMaster";
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = instructions.ToList();
            var rolls = Enumerable.Range(0, code.Count).Where(i => IsRoll(code[i])).ToList();
            bool ok = rolls.Count == 1;   // exactly the one roll, or leave the method alone; Apply logs the first outcome
            // MonoMod re-runs this on every rebuild of the method's hook chain (another mod adding or removing a hook on it).
            // If a rebuild loses the match after it once worked, say so: drops quietly go back to the unscaled chance.
            if (!ok && matched)
            {
                try { CompatShieldPatcher.Log.LogWarning("Sacrifice drop chance: another mod changed SacrificeArtifactManager.OnServerCharacterDeath, so the multiplier is no longer applied"); }
                catch { }
            }
            matched = ok;
            if (!ok) return code;
            int at = rolls[0];
            var roll = (MethodInfo)code[at].operand;
            var luck = generator.DeclareLocal(typeof(float));
            var master = generator.DeclareLocal(roll.GetParameters()[2].ParameterType);
            // stack at the roll: [chance, luck, master]  ->  [chance * multiplier, luck, master]
            // The roll instruction itself becomes the first new one, so its labels stay in front (a jump to it is scaled too).
            var rollCall = new CodeInstruction(code[at].opcode, roll);
            code[at].opcode = OpCodes.Stloc;
            code[at].operand = master;
            code.InsertRange(at + 1, new[]
            {
                new CodeInstruction(OpCodes.Stloc, luck),
                new CodeInstruction(OpCodes.Call, typeof(SacrificeDropTweak).GetMethod(nameof(Scale))),
                new CodeInstruction(OpCodes.Ldloc, luck),
                new CodeInstruction(OpCodes.Ldloc, master),
                rollCall,
            });
            return code;
        }

        /// <summary>The chance this kill rolls against. Runs inside the game's death handling, so it never throws.</summary>
        public static float Scale(float chance)
        {
            if (!(chance > 0f)) return chance;   // 0 (victim has no spawn value) and NaN pass through
            float scaled = chance * multiplier;
            if (!noted)
            {
                noted = true;
                try { CompatShieldPatcher.Log.LogInfo($"Sacrifice: a kill rolled {scaled:0.##}% instead of {chance:0.##}% (shown once)"); }
                catch { }
            }
            return scaled;
        }
    }

    /// <summary>
    /// Inferno 1.7.8 (prodzpod), three Mithrix hook bugs:
    /// 1. Hooks.WeaponSlam (its On.EntityStates.BrotherMonster.WeaponSlam.OnEnter hook) resizes the hammer-slam pillar's
    ///    ghost through ProjectileController.ghostPrefab before calling orig (Hooks.cs line 258). Game 1.5.0 leaves that
    ///    field empty (the ghost now comes from ghostPrefabAddress), so the hook throws a NullReferenceException, orig never
    ///    runs, and Mithrix stands still for the whole slam: no hammer hit, blast, pillar, waves or crystals, and the state
    ///    is never sent to clients. Hooks.CleanupPillar (attached after every run ends, it puts the pillar's size back on the
    ///    next slam) crashes the same way, so even a later non-Inferno run loses the slam. Read the ghost from its address
    ///    when the field is empty, and skip only the ghost resize if no ghost can be found. If anything else in these hooks
    ///    throws before orig, run orig anyway.
    /// 2. HookBehavior.UndoHooks (every run end) adds its SlideIntroState hook again (+=) where every other hook is removed
    ///    (-=), so one more copy stacks each run (3 after two runs: 18 lunar shards per dash instead of 6), on any
    ///    difficulty. Make that one call a remove.
    /// 3. UndoHooks also adds three reset hooks on purpose (CleanupPillar, CleanupDeathState, CleanupDeathState2) at every
    ///    run end, but ApplyHooks removes only one copy at an Inferno run start. After a non-Inferno run in the same game
    ///    session a copy is left inside Inferno's own hook (MonoMod runs the newest hook first), so in the next Inferno run
    ///    it puts the pillar back to 1x (and the void death-bomb timers back to vanilla) right after Inferno set them.
    ///    Make each of those adds "remove, then add", so at most one copy is ever attached.
    /// </summary>
    public static class InfernoMithrixFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        const string SlideIntroHooks = "On.EntityStates.BrotherMonster.SlideIntroState";
        enum GhostRead { Unexpected, Guarded, Absent }
        static GhostRead ghostRead;
        static bool origMarked;
        static int adds, removes, swapped, single;
        static readonly HashSet<string> marked = new HashSet<string>();   // hooks whose orig call is marked
        static readonly HashSet<string> noted = new HashSet<string>();
        [ThreadStatic] static Stack<bool> frames;
        static Type fieldsOf;
        static FieldInfo ghostField, addressField;
        static MethodInfo getTransform, setLocalScale;   // the calls Inferno made, run through reflection
        static object cachedFor, cachedGhost, triedFor;   // triedFor: its address gave no ghost, don't reload every slam
        internal static Func<object, object> LoadGhost = LoadFromAddress;   // swapped out by the test harness

        public static void Apply(Harmony h, Assembly inferno)
        {
            try
            {
                var hooks = inferno.GetType("Inferno.Skill_Misc.Hooks");
                var behavior = inferno.GetType("Inferno.Skill_Misc.HookBehavior");
                if (hooks == null || behavior == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Inferno Mithrix fix: Inferno.Skill_Misc types not found (mod updated?), not applied");
                    return;
                }
                PatchSlamHook(h, hooks, "WeaponSlam");      // each patch on its own: one failing doesn't stop the others
                PatchSlamHook(h, hooks, "CleanupPillar");
                PatchUndoHooks(h, behavior);
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Inferno Mithrix fix failed to apply: " + e);
            }
        }

        static HarmonyMethod M(string name) => new HarmonyMethod(typeof(InfernoMithrixFix).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));
        static string Key(MethodBase m) => m.DeclaringType?.FullName + "::" + m.Name;

        static void PatchSlamHook(Harmony h, Type hooks, string name)
        {
            try
            {
                var hook = hooks.GetMethod(name, Any);
                if (hook == null || hook.GetParameters().Length != 2)
                {
                    CompatShieldPatcher.Log.LogWarning($"Inferno Mithrix fix: Hooks.{name} not found, not applied");
                    return;
                }
                ghostRead = GhostRead.Unexpected;
                origMarked = false;
                h.Patch(hook, prefix: M(nameof(Prefix)), transpiler: M(nameof(SlamTranspiler)), finalizer: M(nameof(Finalizer)));
                if (origMarked) marked.Add(Key(hook));
                if (ghostRead == GhostRead.Guarded)
                    CompatShieldPatcher.Log.LogInfo($"Inferno: Hooks.{name} now finds the pillar ghost at its new address, so Mithrix's hammer slam happens again");
                else if (ghostRead == GhostRead.Absent)
                    CompatShieldPatcher.Log.LogInfo($"Inferno: Hooks.{name} no longer reads ProjectileController.ghostPrefab, ghost fix not needed");
                else
                    CompatShieldPatcher.Log.LogWarning($"Inferno Mithrix fix: unexpected IL in Hooks.{name} (maybe a newer Inferno), ghost read left unchanged");
                if (!origMarked)
                    CompatShieldPatcher.Log.LogWarning($"Inferno Mithrix fix: no single orig call in Hooks.{name}, crash fallback inactive");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError($"Inferno Mithrix fix for Hooks.{name} failed to apply: " + e);
            }
        }

        static void PatchUndoHooks(Harmony h, Type behavior)
        {
            try
            {
                var undo = behavior.GetMethod("UndoHooks", Any);
                if (undo == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Inferno Mithrix fix: HookBehavior.UndoHooks not found, dash hook fix not applied");
                    return;
                }
                adds = removes = swapped = single = 0;
                h.Patch(undo, transpiler: M(nameof(UndoTranspiler)));
                if (swapped == 1)
                    CompatShieldPatcher.Log.LogInfo("Inferno: run-end cleanup now removes its Mithrix dash (SlideIntroState) hook instead of stacking another copy");
                else if (adds == 0 && removes > 0)
                    CompatShieldPatcher.Log.LogInfo("Inferno: this version already removes its SlideIntroState hook at run end, nothing to change");
                else
                    CompatShieldPatcher.Log.LogWarning("Inferno Mithrix fix: unexpected IL in HookBehavior.UndoHooks (maybe a newer Inferno), dash hook left unchanged");
                if (single > 0)
                    CompatShieldPatcher.Log.LogInfo($"Inferno: run-end cleanup now keeps at most one copy of its {single} reset hooks (CleanupPillar...), " +
                        "so a non-Inferno run can't leave one behind that shrinks Mithrix's pillar in the next Inferno run");
                else
                    CompatShieldPatcher.Log.LogInfo("Inferno: no run-end reset hooks found in HookBehavior.UndoHooks, nothing to keep single");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Inferno Mithrix fix for HookBehavior.UndoHooks failed to apply: " + e);
            }
        }

        static bool Calls(CodeInstruction ci, string method) =>
            (ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && ci.operand is MethodInfo m && m.Name == method;

        static bool IsOrigInvoke(CodeInstruction ci) =>
            Calls(ci, "Invoke") && ((MethodInfo)ci.operand).DeclaringType?.Name.StartsWith("orig_") == true;

        static bool SameLocal(object a, object b) =>
            Equals(a, b) || (a is LocalBuilder la && b is LocalBuilder lb && la.LocalIndex == lb.LocalIndex);

        // The ldloc that reads back what a stloc wrote, or null for anything else.
        static CodeInstruction LoadFor(CodeInstruction st)
        {
            if (st.opcode == OpCodes.Stloc_0) return new CodeInstruction(OpCodes.Ldloc_0);
            if (st.opcode == OpCodes.Stloc_1) return new CodeInstruction(OpCodes.Ldloc_1);
            if (st.opcode == OpCodes.Stloc_2) return new CodeInstruction(OpCodes.Ldloc_2);
            if (st.opcode == OpCodes.Stloc_3) return new CodeInstruction(OpCodes.Ldloc_3);
            if (st.opcode == OpCodes.Stloc_S) return new CodeInstruction(OpCodes.Ldloc_S, st.operand);
            if (st.opcode == OpCodes.Stloc) return new CodeInstruction(OpCodes.Ldloc, st.operand);
            return null;
        }

        // Inferno 1.7.8, both hooks:  ghost = pillar.GetComponent<ProjectileController>().ghostPrefab;
        //                             ghost.transform.localScale = new Vector3(s, s, s);   ...   orig(self);
        // becomes                     ghost = (GameObject)GhostOf(pillar.GetComponent<ProjectileController>());
        //                             SetLocalScale(TransformOf(ghost), new Vector3(s, s, s));   ...   OrigReached(); orig(self);
        // where both helpers do nothing when there's no ghost. Each replacement is in place (labels stay put) and has the
        // stack effect of the instruction it replaces.
        static IEnumerable<CodeInstruction> SlamTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            int read = code.FindIndex(ci => ci.opcode == OpCodes.Ldfld && ci.operand is FieldInfo f && f.Name == "ghostPrefab"
                && f.DeclaringType?.Name == "ProjectileController");
            if (read < 0)
                ghostRead = GhostRead.Absent;
            else if (read + 4 < code.Count)
            {
                // The shipped DLL is a Debug build: "stloc ghost; ldloc ghost; get_transform". An optimized build reads
                // .transform straight off the field.
                var load = LoadFor(code[read + 1]);
                int get = -1, set = -1;
                if (load != null && code[read + 2].opcode == load.opcode && SameLocal(code[read + 2].operand, load.operand)
                    && Calls(code[read + 3], "get_transform"))
                    get = read + 3;
                else if (Calls(code[read + 1], "get_transform"))
                    get = read + 1;
                for (int k = get + 1; get > 0 && k < code.Count && k <= get + 7; k++)
                    if (Calls(code[k], "set_localScale")) { set = k; break; }
                if (set > 0)
                {
                    var fieldType = ((FieldInfo)code[read].operand).FieldType;
                    getTransform = (MethodInfo)code[get].operand;
                    setLocalScale = (MethodInfo)code[set].operand;
                    code[read].opcode = OpCodes.Call;             // controller in, ghost out (as the ldfld)
                    code[read].operand = typeof(InfernoMithrixFix).GetMethod(nameof(GhostOf));
                    code[get].opcode = OpCodes.Call;              // ghost in, transform out (as get_transform)
                    code[get].operand = typeof(InfernoMithrixFix).GetMethod(nameof(TransformOf));
                    code[set].opcode = OpCodes.Call;              // transform and scale in (as set_localScale)
                    code[set].operand = typeof(InfernoMithrixFix).GetMethod(nameof(SetLocalScale));
                    code.Insert(read + 1, new CodeInstruction(OpCodes.Castclass, fieldType));   // typed like the field again
                    ghostRead = GhostRead.Guarded;
                }
            }
            // Mark "orig reached" just before the only orig call, so the finalizer knows whether a crash came from Inferno's
            // own code before orig (run orig) or from orig and the hooks below it (rethrow). The call instruction becomes
            // the mark, so a jump to it still passes the mark, and the orig call follows it.
            if (code.Count(IsOrigInvoke) == 1)
            {
                int call = code.FindIndex(IsOrigInvoke);
                var invoke = new CodeInstruction(code[call].opcode, code[call].operand);
                code[call].opcode = OpCodes.Call;
                code[call].operand = typeof(InfernoMithrixFix).GetMethod(nameof(OrigReached));
                code.Insert(call + 1, invoke);
                origMarked = true;
            }
            return code;
        }

        // The matching static remove_X accessor of an On.* add_X (same parameter types), or null.
        static MethodInfo RemoveFor(MethodInfo add) =>
            add.DeclaringType?.GetMethod("remove_" + add.Name.Substring(4), BindingFlags.Public | BindingFlags.Static, null,
                add.GetParameters().Select(p => p.ParameterType).ToArray(), null);

        // 1. UndoHooks has exactly one "SlideIntroState.OnEnter += Hooks.SlideIntroState": swap add_OnEnter for remove_OnEnter
        //    (same signature, so the stack is unchanged).
        // 2. Its intentional adds of Hooks.Cleanup* become "remove, then add" (dup the delegate, remove one copy, add it), so
        //    at most one copy is ever attached. The add call is a branch target (the compiler's cached-delegate pattern jumps
        //    to it), so the original instruction becomes the dup and keeps its labels; the add follows the remove.
        static IEnumerable<CodeInstruction> UndoTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            bool IsHook(CodeInstruction ci, string accessor) =>
                Calls(ci, accessor) && ((MethodInfo)ci.operand).DeclaringType?.FullName == SlideIntroHooks;
            var found = code.Where(ci => IsHook(ci, "add_OnEnter")).ToList();
            adds = found.Count;
            removes = code.Count(ci => IsHook(ci, "remove_OnEnter"));
            if (adds == 1 && RemoveFor((MethodInfo)found[0].operand) is MethodInfo remove)
            {
                found[0].operand = remove;
                swapped = 1;
            }
            for (int i = code.Count - 1; i > 0; i--)
            {
                if (code[i].opcode != OpCodes.Call || !(code[i].operand is MethodInfo add) || !add.IsStatic || !add.Name.StartsWith("add_")
                    || add.DeclaringType?.FullName?.StartsWith("On.") != true)
                    continue;
                bool cleanup = false;   // the delegate added here wraps one of Inferno's Hooks.Cleanup* methods
                for (int j = i - 1; j >= 0 && j >= i - 8; j--)
                {
                    if ((code[j].opcode == OpCodes.Call || code[j].opcode == OpCodes.Callvirt) && code[j].operand is MethodInfo prev
                        && (prev.Name.StartsWith("add_") || prev.Name.StartsWith("remove_")))
                        break;   // the previous statement: stop
                    if (code[j].opcode == OpCodes.Ldftn && code[j].operand is MethodInfo target && target.Name.StartsWith("Cleanup")
                        && target.DeclaringType?.FullName == "Inferno.Skill_Misc.Hooks")
                    {
                        cleanup = true;
                        break;
                    }
                }
                var removeCleanup = cleanup ? RemoveFor(add) : null;
                if (removeCleanup == null) continue;
                code[i].opcode = OpCodes.Dup;         // delegate, delegate (labels stay here)
                code[i].operand = null;
                code.Insert(i + 1, new CodeInstruction(OpCodes.Call, removeCleanup));   // delegate
                code.Insert(i + 2, new CodeInstruction(OpCodes.Call, add));             // (empty)
                single++;
            }
            return code;
        }

        /// <summary>The pillar's ghost prefab: ProjectileController.ghostPrefab when set, else the asset at
        /// ghostPrefabAddress (where game 1.5.0 keeps it); null when neither gives one. Never throws.</summary>
        public static object GhostOf(object controller)
        {
            try
            {
                if (controller == null) return null;
                var type = controller.GetType();
                if (type != fieldsOf)
                {
                    ghostField = type.GetField("ghostPrefab", Any);
                    addressField = type.GetField("ghostPrefabAddress", Any);
                    fieldsOf = type;
                }
                var direct = ghostField?.GetValue(controller);
                if (!IsMissing(direct)) return direct;
                if (ReferenceEquals(controller, cachedFor) && !IsMissing(cachedGhost)) return cachedGhost;
                if (ReferenceEquals(controller, triedFor)) return null;
                var address = addressField?.GetValue(controller);
                var loaded = address == null ? null : LoadGhost(address);
                if (IsMissing(loaded))
                {
                    triedFor = controller;
                    Note("noghost", "Inferno: Mithrix's pillar ghost not found, only the pillar itself is resized (shown once)");
                    return null;
                }
                cachedFor = controller;
                cachedGhost = loaded;
                Note("ghost", "Inferno: Mithrix's pillar ghost loaded from its new address (shown once)");
                return loaded;
            }
            catch (Exception e)
            {
                Note("ghosterror", "Inferno: couldn't load Mithrix's pillar ghost, skipping its resize: " + e.Message);
                return null;
            }
        }

        /// <summary>The ghost's transform, or null when there's no ghost. Never throws.</summary>
        public static object TransformOf(object ghost)
        {
            try
            {
                return IsMissing(ghost) ? null : getTransform.Invoke(ghost, null);
            }
            catch (Exception e)
            {
                Note("transform", "Inferno: couldn't reach Mithrix's pillar ghost, skipping its resize: " + (e.InnerException ?? e).Message);
                return null;
            }
        }

        /// <summary>Resizes the ghost; does nothing when there's no ghost. Never throws.</summary>
        public static void SetLocalScale(object transform, Vector3 scale)
        {
            try
            {
                if (!IsMissing(transform)) setLocalScale.Invoke(transform, new object[] { scale });
            }
            catch (Exception e)
            {
                Note("scale", "Inferno: couldn't resize Mithrix's pillar ghost: " + (e.InnerException ?? e).Message);
            }
        }

        /// <summary>True for null and for a destroyed Unity object.</summary>
        public static bool IsMissing(object o) => o == null || IsDestroyed(o);

        public static void OrigReached()
        {
            if (frames == null || frames.Count == 0) return;
            frames.Pop();
            frames.Push(true);
        }

        static void Prefix() => (frames ?? (frames = new Stack<bool>())).Push(false);

        // Harmony runs this on both paths; never throw from here. A crash before orig: run orig(self), as the hook would
        // have, so the slam still starts. A crash from orig (another mod deeper in the chain) passes through unchanged,
        // but Harmony rethrows a returned exception with "throw ex", which restarts its trace here, so log the real one.
        // A stack overflow passes straight through with no other work (there may be almost no stack left).
        static Exception Finalizer(Exception __exception, object[] __args, MethodBase __originalMethod)
        {
            bool invoked = false;
            try
            {
                bool reached = frames != null && frames.Count > 0 && frames.Pop();
                if (__exception == null || __exception is StackOverflowException) return __exception;
                if (reached || __originalMethod == null || !marked.Contains(Key(__originalMethod))
                    || __args == null || __args.Length < 2 || !(__args[0] is Delegate orig))
                {
                    NotePassThrough(__originalMethod, __exception);
                    return __exception;
                }
                Note(Key(__originalMethod), $"Inferno: Hooks.{__originalMethod.Name} crashed before Mithrix's slam started, started it anyway (shown once): {__exception}");
                invoked = true;
                orig.DynamicInvoke(__args[1]);
                return null;
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                return tie.InnerException;   // orig itself threw: same as if the hook had called it
            }
            catch (Exception e)
            {
                return invoked ? e : __exception;
            }
        }

        static readonly Dictionary<string, int> passed = new Dictionary<string, int>();
        static void NotePassThrough(MethodBase hook, Exception e)
        {
            try
            {
                var top = new System.Diagnostics.StackTrace(e, false);   // one decode, no file info
                var at = top.FrameCount > 0 ? top.GetFrame(0).GetMethod() : null;
                var site = e.GetType().FullName + " @ " + (at?.DeclaringType?.FullName ?? "?") + "." + at?.Name;
                passed.TryGetValue(site, out var n);
                passed[site] = n + 1;
                if (n < 3)
                    CompatShieldPatcher.Log.LogWarning($"Inferno: an error from Mithrix's slam below Inferno's hook passed through Hooks.{hook?.Name}; " +
                        $"the game's log will blame Inferno, the real source is below (#{n + 1} from this site; first 3 shown): {e}");
            }
            catch { }
        }

        static void Note(string key, string message)
        {
            if (noted.Add(key)) CompatShieldPatcher.Log.LogInfo(message);
        }

        // Separate and never inlined: these touch Unity (the test harness runs outside it). The Addressables handle is
        // never released, so the ghost stays loaded and the resize applies to every pillar the game spawns from it.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static bool IsDestroyed(object o) => o is UnityEngine.Object u && !u;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static object LoadFromAddress(object address)
        {
            var reference = address as AssetReference;
            if (reference == null || !reference.RuntimeKeyIsValid()) return null;
            return Addressables.LoadAssetAsync<GameObject>(reference.RuntimeKey).WaitForCompletion();
        }
    }

    /// <summary>
    /// Two fixes to Inferno 1.7.8's difficulty scaling (both only matter in Inferno runs, where Inferno attaches these hooks).
    /// 1. "Level and Difficulty Boost" (+1.25 monster levels by default, plus 0.05 x boost on the director's credit coefficient)
    ///    is an IL hook on Run.RecalculateDifficultyCoefficentInternal, added at every Inferno run start, that inserts its boost
    ///    just before the game reads Run.ambientLevelCap. RiskyTweaks' "(Server-Side) Raise Level Cap" (an IL hook added at
    ///    startup, so it runs first) has already replaced that read with its own RaiseLevelCap.levelCap (9999), so Inferno's
    ///    match fails, it logs "Failed to fully apply Level and Difficulty boost" and monster level never gets the boost (its
    ///    On-hook half writes ambientLevel after the game has floored it, so it only moves the difficulty bar). Widen Inferno's
    ///    match (a postfix on its compiled predicate) to also accept another mod's level-cap read; Inferno then inserts its own
    ///    boost exactly where it meant to, before the cap, and RiskyTweaks' cap stays. The amount stays Inferno's setting.
    /// 2. "Enemy Cooldown Reduction Scaling" adds its amount x cleared stages to R2API's cooldownMultAdd, and R2API computes
    ///    COOLDOWN ~ (1 + cooldownMultAdd) / (1 + cooldownReductionMultAdd), so enemy cooldowns get LONGER every stage (+1% per
    ///    stage by default), the opposite of the setting's description. Send the same amount to allSkills.cooldownReductionMultAdd.
    /// </summary>
    public static class InfernoScalingFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static bool matchedOther;   // log the first widened match only
        internal static int Swapped;   // read by the test harness
        // Labels are handled as boxed objects: the netstandard2.1 build can't name Harmony's Label type (CS7069).
        static readonly FieldInfo labelsField = typeof(CodeInstruction).GetField("labels");
        static bool HasLabels(CodeInstruction ci) => ((System.Collections.IList)labelsField.GetValue(ci)).Count > 0;

        public static void Apply(Harmony h, Assembly inferno, bool boostFix, bool cooldownFix)
        {
            try
            {
                var hooks = inferno.GetType("Inferno.Skill_Misc.Hooks");
                if (hooks == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Inferno scaling fix: Inferno.Skill_Misc.Hooks not found (mod updated?), not applied");
                    return;
                }
                if (boostFix) PatchBoostMatch(h, hooks);       // each on its own: one failing doesn't stop the other
                if (cooldownFix) PatchCooldown(h, hooks);
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Inferno scaling fix failed to apply: " + e);
            }
        }

        static HarmonyMethod M(string name) => new HarmonyMethod(typeof(InfernoScalingFix).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));

        // Inferno's Hooks.Ambient: cursor.TryGotoNext(MoveType.Before, x => x.MatchLdsfld<Run>("ambientLevelCap")), then
        // EmitDelegate(level => level + boost). The lambda compiles to Hooks+<>c.<Ambient>b__N_M(Instruction x) : bool.
        static void PatchBoostMatch(Harmony h, Type hooks)
        {
            try
            {
                var predicates = hooks.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                    .SelectMany(MethodsOf).Where(IsAmbientPredicate).ToList();
                if (predicates.Count != 1)
                {
                    CompatShieldPatcher.Log.LogWarning($"Inferno scaling fix: expected one Ambient match predicate, found {predicates.Count} " +
                        "(maybe a newer Inferno), Level and Difficulty Boost left unchanged");
                    return;
                }
                h.Patch(predicates[0], postfix: M(nameof(MatchPostfix)));
                CompatShieldPatcher.Log.LogInfo("Inferno: Level and Difficulty Boost will also find a level cap another mod put in " +
                    "(RiskyTweaks' Raise Level Cap), so it reaches monster level again");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Inferno scaling fix for the Level and Difficulty Boost failed to apply: " + e);
            }
        }

        // One method with a signature that no longer resolves (a removed hook type) must not hide the others.
        static IEnumerable<MethodInfo> MethodsOf(Type t)
        {
            try { return t.GetMethods(Any | BindingFlags.DeclaredOnly); }
            catch { return new MethodInfo[0]; }
        }

        static bool IsAmbientPredicate(MethodInfo m)
        {
            try
            {
                if (!m.Name.StartsWith("<Ambient>b__") || m.ReturnType != typeof(bool)) return false;
                var p = m.GetParameters();
                return p.Length == 1 && p[0].ParameterType == typeof(Mono.Cecil.Cil.Instruction);
            }
            catch { return false; }
        }

        // Postfix on Inferno's predicate; never throws (a throw here would abort Inferno's IL hook).
        static void MatchPostfix(object[] __args, ref bool __result)
        {
            try
            {
                if (__result || __args == null || __args.Length < 1) return;
                if (!IsOtherLevelCapRead(__args[0] as Mono.Cecil.Cil.Instruction)) return;
                __result = true;
                if (!matchedOther)
                {
                    matchedOther = true;
                    CompatShieldPatcher.Log.LogInfo("Inferno: Level and Difficulty Boost applied in front of another mod's level cap (shown once)");
                }
            }
            catch { }
        }

        /// <summary>A static read of another mod's level cap (RiskyTweaks: float RaiseLevelCap.levelCap), not the game's own
        /// Run.ambientLevelCap (Inferno already matches that).</summary>
        public static bool IsOtherLevelCapRead(Mono.Cecil.Cil.Instruction x)
        {
            if (x == null || x.OpCode != Mono.Cecil.Cil.OpCodes.Ldsfld || !(x.Operand is FieldReference f)) return false;
            if (!f.Name.EndsWith("levelCap", StringComparison.OrdinalIgnoreCase) || f.DeclaringType?.FullName == "RoR2.Run") return false;
            var t = f.FieldType.MetadataType;
            return t == MetadataType.Single || t == MetadataType.Int32;
        }

        static void PatchCooldown(Harmony h, Type hooks)
        {
            try
            {
                var stats = hooks.GetMethod("RecalculateStatsAPI_GetStatCoefficients", Any);
                if (stats == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Inferno scaling fix: Hooks.RecalculateStatsAPI_GetStatCoefficients not found, cooldown scaling left unchanged");
                    return;
                }
                Swapped = 0;
                h.Patch(stats, transpiler: M(nameof(CooldownTranspiler)));
                if (Swapped == 1)
                    CompatShieldPatcher.Log.LogInfo("Inferno: Enemy Cooldown Reduction Scaling now shortens enemy cooldowns each stage (it lengthened them)");
                else
                    CompatShieldPatcher.Log.LogWarning("Inferno scaling fix: unexpected IL in Hooks.RecalculateStatsAPI_GetStatCoefficients (maybe a newer Inferno), cooldown scaling left unchanged");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Inferno scaling fix for cooldown scaling failed to apply: " + e);
            }
        }

        // Inferno 1.7.8:  args.cooldownMultAdd += Main.StageCooldownReduction.Value * Run.instance.stageClearCount;
        //   IL: ldarg.1; dup; ldfld cooldownMultAdd; <amount>; add; stfld cooldownMultAdd
        // becomes         args.allSkills.cooldownReductionMultAdd += <same amount>;
        //   IL: ldarg.1; ldflda allSkills; dup; ldfld cooldownReductionMultAdd; <amount>; add; stfld cooldownReductionMultAdd
        // allSkills is a struct field, so it is updated in place through its address. Untouched unless the shape matches exactly.
        static IEnumerable<CodeInstruction> CooldownTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            int read = code.FindIndex(ci => ci.opcode == OpCodes.Ldfld && ci.operand is FieldInfo f && f.Name == "cooldownMultAdd"
                && f.DeclaringType?.Name == "StatHookEventArgs");
            if (read < 2 || code[read - 1].opcode != OpCodes.Dup || HasLabels(code[read - 1]) || HasLabels(code[read]))
                return code;
            var multAdd = (FieldInfo)code[read].operand;
            int write = code.FindIndex(read + 1, ci => ci.opcode == OpCodes.Stfld && ci.operand is FieldInfo g
                && g.Name == multAdd.Name && g.DeclaringType == multAdd.DeclaringType);
            if (write < 0 || write > read + 12) return code;
            for (int k = read + 1; k < write; k++)   // the amount: plain calls and arithmetic, no jumps in or out
                if (HasLabels(code[k]) || code[k].opcode.FlowControl == FlowControl.Branch || code[k].opcode.FlowControl == FlowControl.Cond_Branch
                    || code[k].opcode == OpCodes.Stfld || code[k].opcode == OpCodes.Dup)
                    return code;
            var all = multAdd.DeclaringType.GetField("allSkills", Any);
            var reduction = all?.FieldType.GetField("cooldownReductionMultAdd", Any);
            if (all == null || !all.FieldType.IsValueType || reduction == null || reduction.FieldType != typeof(float))
                return code;
            code[read].operand = reduction;
            code[write].operand = reduction;
            code.Insert(read - 1, new CodeInstruction(OpCodes.Ldflda, all));   // args -> &args.allSkills, then dup as before
            Swapped++;
            return code;
        }
    }

    /// <summary>
    /// Inferno 1.7.8, HookBehavior: at every run start on its difficulty (with "Enable Skill Changes?" on) ApplyHooks adds its
    /// 82 enemy-skill hooks one by one (and removes its 3 reset hooks), and at every run end UndoHooks mirrors that, so each
    /// run builds and tears down 85 MonoMod detours. On 2026-10-18 the sixth cycle of a session crashed the game's C# runtime (access violation
    /// in mono-2.0-bdwgc.dll) while ApplyHooks added its 58th hook, FireConstructBeam.OnEnter, the moment Start was pressed
    /// in the lobby; the five cycles before it that session, and nine the morning before, had worked.
    /// Fix: keep the hooks between Inferno runs. ApplyHooks runs for real once per game session; UndoHooks is skipped while
    /// the hooks are kept; and when a run on another difficulty starts (Run.onRunStartGlobal, subscribed at that first real
    /// apply) the kept hooks are removed with Inferno's own UndoHooks, so that run plays without Inferno's skill changes
    /// and the next Inferno run adds them again. This is a mitigation of an unconfirmed cause (hook churn), not a proof:
    /// it removes Inferno's 82-hook burst, about 60% of the roughly 130 hook adds/removes a Start press costs on this
    /// profile (measured 2026-10-18). PlayerRespawnSystem (35 On hooks), ServerSider (6 On + 2 IL), Starstorm2 (2-4) and
    /// Inferno's own run-start lambda (2 On + 4 IL hooks: difficulty icon, AmbientLevelBoost, LookBusy x2, Wander, Ambient)
    /// still cycle every run; they are the next targets if the crash recurs at a Start press (tools\hook_churn.py tallies
    /// them from a log). A recurrence during a session's FIRST apply would disprove the churn theory instead. Inferno's
    /// lambda hooks are difficulty-dependent and left alone. Every PC runs ApplyHooks itself, so every PC needs this.
    /// </summary>
    public static class InfernoHookChurnFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal static bool Applied;                        // the hooks are in and being kept
        internal static int SkippedApplies, SkippedUndos;    // read by the test harness
        internal static bool runStartHooked;                 // Run.onRunStartGlobal subscribed (the harness presets it)
        internal static Action InvokeUndo;                   // Inferno's real UndoHooks (the harness swaps in a counter)
        internal static Func<object, bool?> IsInfernoRun;    // null = can't tell (the harness swaps in a stub)
        static bool forceUndo, loggedKept, loggedUnknown;
        static MethodInfo isInferno;

        public static void Apply(Harmony h, Assembly inferno)
        {
            try
            {
                var behavior = inferno.GetType("Inferno.Skill_Misc.HookBehavior");
                var apply = behavior?.GetMethod("ApplyHooks", BindingFlags.Public | BindingFlags.Static);
                var undo = behavior?.GetMethod("UndoHooks", BindingFlags.Public | BindingFlags.Static);
                if (apply == null || undo == null || apply.GetParameters().Length != 0 || undo.GetParameters().Length != 0)
                {
                    CompatShieldPatcher.Log.LogWarning("Inferno hook churn fix: HookBehavior.ApplyHooks/UndoHooks not found (mod updated?), not applied");
                    return;
                }
                isInferno = inferno.GetType("Inferno.Main")?.GetMethod("IsInferno", BindingFlags.Public | BindingFlags.Static);
                if (isInferno != null && (isInferno.ReturnType != typeof(bool) || isInferno.GetParameters().Length != 1))
                    isInferno = null;
                InvokeUndo = () => undo.Invoke(null, null);
                IsInfernoRun = RunIsInferno;
                h.Patch(apply, prefix: M(nameof(ApplyPrefix)));
                h.Patch(undo, prefix: M(nameof(UndoPrefix)));
                CompatShieldPatcher.Log.LogInfo("Inferno: its enemy skill hooks are now kept between Inferno runs instead of removed at every run end " +
                    "and re-added at the next run start (that re-add crashed the game); they come out only when a run on another difficulty starts");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Inferno hook churn fix failed to apply: " + e);
            }
        }

        static HarmonyMethod M(string name) => new HarmonyMethod(typeof(InfernoHookChurnFix).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));

        // Prefix on HookBehavior.ApplyHooks: the first call runs for real, later ones are skipped while the hooks are kept.
        static bool ApplyPrefix()
        {
            try
            {
                if (!Applied)
                {
                    Applied = true;
                    EnsureRunStartHook();
                    return true;
                }
                SkippedApplies++;
                if (!loggedKept)
                {
                    loggedKept = true;
                    CompatShieldPatcher.Log.LogInfo("Inferno: kept its enemy skill hooks from the earlier Inferno run instead of re-adding them (shown once)");
                }
                return false;
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Inferno hook churn fix: ApplyHooks prefix failed, letting Inferno's own run: " + e);
                return true;
            }
        }

        // Prefix on HookBehavior.UndoHooks: skipped while the hooks are kept, unless this fix is taking them out itself.
        static bool UndoPrefix()
        {
            if (!Applied || forceUndo) return true;
            SkippedUndos++;
            return false;
        }

        /// <summary>Takes the kept hooks out with Inferno's own UndoHooks (its run-end path, CompatShield's other Inferno
        /// fixes included), so the next ApplyHooks runs for real again.</summary>
        internal static void UndoKeptHooks()
        {
            forceUndo = true;
            try { InvokeUndo?.Invoke(); }
            finally { forceUndo = false; Applied = false; }
        }

        // Run.onRunStartGlobal, subscribed at the first real ApplyHooks (RoR2.Run is certainly initialised by then).
        static void EnsureRunStartHook()
        {
            if (runStartHooked) return;
            runStartHooked = true;   // one attempt: on failure the hooks are simply kept for the whole session
            try
            {
                var run = Type.GetType("RoR2.Run, RoR2");
                var ev = run?.GetEvent("onRunStartGlobal", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) throw new MissingMemberException("RoR2.Run.onRunStartGlobal not found");
                var handler = Delegate.CreateDelegate(ev.EventHandlerType,
                    typeof(InfernoHookChurnFix).GetMethod(nameof(OnRunStart), BindingFlags.NonPublic | BindingFlags.Static));
                ev.AddEventHandler(null, handler);
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Inferno hook churn fix: can't watch run starts, so Inferno's enemy skill hooks stay in for the whole " +
                    "session, other difficulties included: " + e.Message);
            }
        }

        // Every run start, on every PC. Inferno runs: nothing (the hooks are kept, Inferno's ApplyHooks is skipped).
        // Other difficulties: take the kept hooks out, so that run plays as Inferno intends, without them.
        static void OnRunStart(object run)
        {
            try
            {
                if (!Applied) return;
                var inferno = IsInfernoRun?.Invoke(run);
                if (inferno == true) return;
                if (inferno == null)
                {
                    if (!loggedUnknown)
                    {
                        loggedUnknown = true;
                        CompatShieldPatcher.Log.LogWarning("Inferno hook churn fix: can't tell this run's difficulty, so Inferno's enemy skill hooks stay in (shown once)");
                    }
                    return;
                }
                UndoKeptHooks();
                CompatShieldPatcher.Log.LogInfo("Inferno: a run on another difficulty started, so its enemy skill hooks from the earlier Inferno run were removed");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Inferno hook churn fix: taking the kept hooks out at a non-Inferno run start failed: " + e);
            }
        }

        // Inferno's own test, Main.IsInferno(run.selectedDifficulty). Null when either half can't be found. selectedDifficulty
        // is a property in game 1.5.0 (a field in older builds); a run start is rare, so no caching.
        static bool? RunIsInferno(object run)
        {
            if (run == null || isInferno == null) return null;
            var type = run.GetType();
            var prop = type.GetProperty("selectedDifficulty", Any);
            var difficulty = prop != null ? prop.GetValue(run, null) : type.GetField("selectedDifficulty", Any)?.GetValue(run);
            if (difficulty == null || !isInferno.GetParameters()[0].ParameterType.IsInstanceOfType(difficulty)) return null;
            return (bool)isInferno.Invoke(null, new[] { difficulty });
        }
    }

    /// <summary>
    /// PlayerRespawnSystem 2.2.0: its 35 respawn hooks are instance methods on Unity components that it builds at every run
    /// start and destroys at every run end. Its Run.Awake hook adds a PlayerRespawnSystem component to the plugin's own
    /// GameObject; that component's Awake adds a PlayerRespawner (2 hooks: Stage.RespawnCharacter/GetPlayerSpawnTransform),
    /// one controller per respawn type (Timed 8 hooks; Teleporter, Mithrix, Artifact, Voidling, False Son, Solus Wing and
    /// Solus Heart 3 each) and 4 hooks of its own. Its Run.OnDestroy hook destroys the component, whose OnDestroy chain
    /// removes all 35. So a Start press builds 35 MonoMod detours and a run end tears 35 down: the biggest per-run hook
    /// churn left on this profile after InfernoHookChurnFix (tools\hook_churn.py, 2026-10-18).
    /// The hooks can't be kept on their own the way Inferno's static ones are: each is bound to its component and reads
    /// that component's per-run state (respawn queue, controller active flags, user timers). So keep the component. The
    /// Run.OnDestroy hook's Object.Destroy(playerRespawnSystem) becomes KeepOrDestroy, which leaves it alive with its
    /// timers cleared, and the Run.Awake hook's AddComponent<PlayerRespawnSystem>() becomes ReuseOrAdd, which hands the
    /// kept one back after redoing what a fresh one's Awake chain leaves behind: respawn queue empty, not advancing a stage,
    /// the Timed controller active and the other controllers inactive, user timers cleared (Run.Start re-adds them through
    /// Run.OnUserAdded, as before). Between runs the kept hooks are inert: nothing they hook (stages, bodies, masters, a
    /// run, boss fight states) exists in the lobby; the one exception is the Artifact trial's CombatState.OnExit, which the
    /// scene unload after a run that ended mid-trial fires (two controller flag writes that the next reset overwrites). The
    /// mod picks its controllers at build time from each type's static IsEnabled (only Timed's reads config, UseTimedRespawn),
    /// so a kept system whose controller set no longer matches is rebuilt the mod's way. For a run in a game mode the mod
    /// ignores (IgnoredGameModes, Simulacrum
    /// by default) the mod adds nothing, so a postfix on its Run.Awake hook destroys the kept component then: the mod's
    /// own run-end path, one run late, and the next normal run builds a fresh one. Everything inside a run is the mod's
    /// own code on the same objects. Every PC builds and destroys the component itself, so every PC needs this.
    /// </summary>
    public static class RespawnHookChurnFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal static object Kept;                          // the PlayerRespawnSystem component kept from the last run
        internal static bool claimed;                         // ReuseOrAdd handed it to the run that is starting
        internal static int Reused, Built, DestroyedKept;     // read by the test harness
        // Unity touch points; the harness swaps in stand-ins.
        internal static Func<object, bool> IsAlive = o => o is UnityEngine.Object u && u;
        internal static Action<object> DestroyObject = o => UnityEngine.Object.Destroy((UnityEngine.Object)o);
        internal static Action<object> DestroyObjectNow = o => UnityEngine.Object.DestroyImmediate((UnityEngine.Object)o);
        internal static Func<GameObject, Type, object> AddSystem = (host, type) => host.AddComponent(type);
        internal static Type systemType;                      // PlayerRespawnSystem.PlayerRespawnSystem
        internal static FieldInfo systemField;                // PlayerRespawnSystemPlugin.playerRespawnSystem
        internal static bool swappedAdd, swappedDestroy;      // set by the transpilers when they find their call
        static bool applied, loggedKept, loggedReused, loggedKeepError, loggedReuseError, loggedRerunMiss;

        public static void Apply(Harmony h, Assembly mod)
        {
            try
            {
                string why = Resolve(mod, out var awake, out var destroy);
                if (why != null)
                {
                    CompatShieldPatcher.Log.LogWarning("Respawn hook churn fix: " + why + " (mod updated?), not applied");
                    return;
                }
                swappedAdd = swappedDestroy = false;
                bool attemptedAwake = false;
                try
                {
                    h.Patch(destroy, transpiler: M(nameof(KeepTranspiler)));
                    if (!swappedDestroy) throw new InvalidOperationException("Run_OnDestroy's Object.Destroy(playerRespawnSystem) was not swapped");
                    attemptedAwake = true;
                    h.Patch(awake, prefix: M(nameof(AwakePrefix)), postfix: M(nameof(AwakePostfix)), transpiler: M(nameof(ReuseTranspiler)));
                    if (!swappedAdd) throw new InvalidOperationException("Run_Awake's AddComponent<PlayerRespawnSystem>() was not swapped");
                    applied = true;
                }
                catch
                {
                    // Never leave half of it in: a kept component beside a freshly added one would run every hook twice. Harmony
                    // registers a patch before it compiles it, so both come out whether or not their Patch call returned.
                    try { h.Unpatch(destroy, HarmonyPatchType.All, h.Id); }
                    catch (Exception e) { CompatShieldPatcher.Log.LogError("Respawn hook churn fix: unpatching Run_OnDestroy failed: " + e.Message); }
                    if (attemptedAwake)
                    {
                        try { h.Unpatch(awake, HarmonyPatchType.All, h.Id); }
                        catch (Exception e) { CompatShieldPatcher.Log.LogError("Respawn hook churn fix: unpatching Run_Awake failed: " + e.Message); }
                    }
                    throw;
                }
                CompatShieldPatcher.Log.LogInfo("PlayerRespawnSystem: its respawn hooks (35 in 2.2.0) are now kept between runs instead of removed at " +
                    "every run end and re-added at the next run start (the kind of churn that crashed the game); they come out only when a run " +
                    "in a game mode the mod ignores starts");
                ApplyDeathTimerHeal(h, mod);
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Respawn hook churn fix failed to apply: " + e);
            }
        }

        static HarmonyMethod M(string name) => new HarmonyMethod(typeof(RespawnHookChurnFix).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));

        // --- Death-timer self-heal (2026-10-19 00:39: 104x "Could not find target object with netId:9 for RPC call
        // ClientRpc:InvokeRpcTargetUpdateDeathTimer" in one run of 16). The mod unspawns its UIDeathTimerController at every
        // run end and re-spawns the same object at the next Run.Awake; its server-side Update sends every player a TargetRpc
        // twice a second, and that warning means the host's own local client no longer had the object under that netId, so
        // the host's death timer was blank. Before each update, if the local client can't find the object, register it again.
        // UNet is reached by reflection (the patcher doesn't reference the HLAPI); the harness swaps in stand-ins.
        internal static Func<bool> LocalClientActive;             // NetworkServer.localClientActive
        internal static Func<object, object> NetIdOf;             // NetworkBehaviour.netId (a NetworkInstanceId)
        internal static Func<object, bool> NetIdEmpty;            // NetworkInstanceId.IsEmpty()
        internal static Func<object, GameObject> FindLocal;       // ClientScene.FindLocalObject(netId)
        internal static Action<object, GameObject> SetLocal;      // ClientScene.SetLocalObject(netId, go)
        internal static int Healed;                               // read by the test harness
        static bool loggedHeal;

        static void ApplyDeathTimerHeal(Harmony h, Assembly mod)
        {
            try
            {
                var cmd = mod.GetType("PlayerRespawnSystem.UIDeathTimerController")?.GetMethod("CmdUpdateAllDeathTimers", Any, null, Type.EmptyTypes, null);
                const string hlapi = ", com.unity.multiplayer-hlapi.Runtime";
                var clientScene = Type.GetType("UnityEngine.Networking.ClientScene" + hlapi);
                var netServer = Type.GetType("UnityEngine.Networking.NetworkServer" + hlapi);
                var netBehaviour = Type.GetType("UnityEngine.Networking.NetworkBehaviour" + hlapi);
                var netIdType = Type.GetType("UnityEngine.Networking.NetworkInstanceId" + hlapi);
                var netIdProp = netBehaviour?.GetProperty("netId", Any);
                var isEmpty = netIdType?.GetMethod("IsEmpty", Any, null, Type.EmptyTypes, null);
                var find = netIdType == null ? null : clientScene?.GetMethod("FindLocalObject", BindingFlags.Public | BindingFlags.Static, null, new[] { netIdType }, null);
                var set = netIdType == null ? null : clientScene?.GetMethod("SetLocalObject", BindingFlags.Public | BindingFlags.Static, null, new[] { netIdType, typeof(GameObject) }, null);
                var localActive = netServer?.GetProperty("localClientActive", BindingFlags.Public | BindingFlags.Static);
                if (cmd == null || netIdProp == null || isEmpty == null || find == null || set == null || localActive == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Respawn hook churn fix: death-timer self-heal not applied (UIDeathTimerController.CmdUpdateAllDeathTimers or the UNet ClientScene API not found)");
                    return;
                }
                LocalClientActive = () => (bool)localActive.GetValue(null);
                NetIdOf = b => netIdProp.GetValue(b);
                NetIdEmpty = id => (bool)isEmpty.Invoke(id, null);
                FindLocal = id => find.Invoke(null, new[] { id }) as GameObject;
                SetLocal = (id, go) => set.Invoke(null, new[] { id, go });
                h.Patch(cmd, prefix: M(nameof(DeathTimerPrefix)));
                CompatShieldPatcher.Log.LogInfo("PlayerRespawnSystem: if the host's own client loses track of the death-timer object, it is registered again before the next timer update");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Respawn hook churn fix: death-timer self-heal failed to apply: " + e);
            }
        }

        // Prefix on UIDeathTimerController.CmdUpdateAllDeathTimers (server side, twice a second). Never throws.
        static void DeathTimerPrefix(object __instance)
        {
            try { EnsureDeathTimerLocal(__instance, (__instance as Component)?.gameObject); }
            catch { }
        }

        /// <summary>Registers the death-timer object with the host's local client if it lost it. Returns true when it did.</summary>
        internal static bool EnsureDeathTimerLocal(object behaviour, GameObject go)
        {
            if (behaviour == null || LocalClientActive == null || !LocalClientActive()) return false;
            var id = NetIdOf(behaviour);
            if (id == null || NetIdEmpty(id) || !(FindLocal(id) is null)) return false;   // reference check: ClientScene returns a real null when the object is missing
            SetLocal(id, go);
            Healed++;
            if (!loggedHeal)
            {
                loggedHeal = true;
                CompatShieldPatcher.Log.LogInfo($"PlayerRespawnSystem: the host's own client had lost its death-timer object (netId {id}); registered it again before the next timer update (shown once)");
            }
            return true;
        }

        // Finds everything the fix touches and checks its shape: null when all of it is as in 2.2.0, else the reason.
        internal static string Resolve(Assembly mod, out MethodInfo awake, out MethodInfo destroy)
        {
            awake = destroy = null;
            var plugin = mod.GetType("PlayerRespawnSystem.PlayerRespawnSystemPlugin");
            systemType = mod.GetType("PlayerRespawnSystem.PlayerRespawnSystem");
            if (plugin == null || systemType == null) return "PlayerRespawnSystemPlugin or PlayerRespawnSystem type not found";
            awake = plugin.GetMethod("Run_Awake", Any);
            destroy = plugin.GetMethod("Run_OnDestroy", Any);
            systemField = plugin.GetField("playerRespawnSystem", Any);
            if (awake == null || destroy == null || awake.IsStatic || destroy.IsStatic || awake.GetParameters().Length != 2 || destroy.GetParameters().Length != 2)
                return "PlayerRespawnSystemPlugin.Run_Awake/Run_OnDestroy not found or not two-argument hook methods";
            if (systemField == null || systemField.FieldType != systemType) return "PlayerRespawnSystemPlugin.playerRespawnSystem not found";
            // What ResetRunState and ClearTimers touch.
            var respawner = systemType.GetField("playerRespawner", Any)?.FieldType;
            if (respawner == null || respawner.GetField("respawnQueue", Any) == null || respawner.GetProperty("IsAdvancingStage", Any)?.GetSetMethod(true) == null)
                return "PlayerRespawner's respawnQueue or IsAdvancingStage not found";
            var controllers = systemType.GetField("respawnControllers", Any);
            if (controllers == null || !typeof(System.Collections.IDictionary).IsAssignableFrom(controllers.FieldType))
                return "PlayerRespawnSystem.respawnControllers not found";
            if (mod.GetType("PlayerRespawnSystem.RespawnController")?.GetProperty("IsActive", Any)?.GetSetMethod(true) == null)
                return "RespawnController.IsActive setter not found";
            var controllerTypes = ControllerTypes(mod);
            if (controllerTypes.Count == 0 || controllerTypes.Any(t => t.GetProperty("IsEnabled", BindingFlags.Public | BindingFlags.Static)?.PropertyType != typeof(bool)))
                return "the respawn controller types (AssociatedRespawnType) or their static IsEnabled not found";
            var timers = mod.GetType("PlayerRespawnSystem.TimedRespawnController")?.GetField("userRespawnTimers", Any);
            if (timers == null || timers.FieldType.GetMethod("ClearTimers", Any, null, Type.EmptyTypes, null) == null)
                return "TimedRespawnController.userRespawnTimers.ClearTimers() not found";
            // The two calls the transpilers swap, each exactly once.
            var a = PatchProcessor.ReadMethodBody(awake).ToList();
            int adds = 0;
            for (int i = 0; i + 1 < a.Count; i++)
                if (IsAddSystem(a[i].Key, a[i].Value) && IsSystemStore(a[i + 1].Key, a[i + 1].Value)) adds++;
            if (adds != 1) return $"Run_Awake stores gameObject.AddComponent<PlayerRespawnSystem>() into playerRespawnSystem {adds} times, not once";
            var d = PatchProcessor.ReadMethodBody(destroy).ToList();
            int destroys = 0;
            for (int i = 1; i < d.Count; i++)
                if (IsDestroy(d[i].Key, d[i].Value) && IsSystemLoad(d[i - 1].Key, d[i - 1].Value)) destroys++;
            if (destroys != 1) return $"Run_OnDestroy calls Object.Destroy(playerRespawnSystem) {destroys} times, not once";
            return null;
        }

        static bool SameField(FieldInfo f) => systemField != null && f.MetadataToken == systemField.MetadataToken && f.Module == systemField.Module;
        static bool IsAddSystem(OpCode op, object operand) =>
            (op == OpCodes.Callvirt || op == OpCodes.Call) && operand is MethodInfo m && m.Name == "AddComponent" && m.DeclaringType == typeof(GameObject)
            && m.IsGenericMethod && m.GetParameters().Length == 0 && m.GetGenericArguments().Length == 1 && m.GetGenericArguments()[0] == systemType;
        static bool IsSystemStore(OpCode op, object operand) => op == OpCodes.Stfld && operand is FieldInfo f && SameField(f);
        static bool IsSystemLoad(OpCode op, object operand) => op == OpCodes.Ldfld && operand is FieldInfo f && SameField(f);
        static bool IsDestroy(OpCode op, object operand) =>
            op == OpCodes.Call && operand is MethodInfo m && m.Name == "Destroy" && m.DeclaringType == typeof(UnityEngine.Object) && m.GetParameters().Length == 1;

        // Run_Awake: "playerRespawnSystem = gameObject.AddComponent<PlayerRespawnSystem>()" calls ReuseOrAdd instead. Same stack
        // effect (a GameObject in, a PlayerRespawnSystem out); the instruction is changed in place to keep its labels.
        static IEnumerable<CodeInstruction> ReuseTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            for (int i = 0; i + 1 < code.Count; i++)
            {
                if (!IsAddSystem(code[i].opcode, code[i].operand) || !IsSystemStore(code[i + 1].opcode, code[i + 1].operand)) continue;
                code[i].opcode = OpCodes.Call;
                code[i].operand = typeof(RespawnHookChurnFix).GetMethod(nameof(ReuseOrAdd), BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(systemType);
                swappedAdd = true;
                return code;
            }
            RerunMiss("Run_Awake's AddComponent<PlayerRespawnSystem>()");
            return code;
        }

        // Run_OnDestroy: "Object.Destroy(playerRespawnSystem)" calls KeepOrDestroy instead (one object in, nothing out).
        static IEnumerable<CodeInstruction> KeepTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            for (int i = 1; i < code.Count; i++)
            {
                if (!IsDestroy(code[i].opcode, code[i].operand) || !IsSystemLoad(code[i - 1].opcode, code[i - 1].operand)) continue;
                code[i].operand = typeof(RespawnHookChurnFix).GetMethod(nameof(KeepOrDestroy), BindingFlags.NonPublic | BindingFlags.Static);
                swappedDestroy = true;
                return code;
            }
            RerunMiss("Run_OnDestroy's Object.Destroy(playerRespawnSystem)");
            return code;
        }

        // Harmony re-runs every transpiler whenever anyone re-patches the method; if another mod's transpiler rewrote the call this
        // one swaps, the mod's own build/destroy quietly runs again (safe: a destroyed kept component is not reused). Say so once.
        static void RerunMiss(string what)
        {
            if (!applied || loggedRerunMiss) return;
            loggedRerunMiss = true;
            CompatShieldPatcher.Log.LogWarning("Respawn hook churn fix: " + what + " is no longer where it was (another mod re-patched the method), so " +
                "PlayerRespawnSystem's respawn hooks cycle every run again from here on (shown once)");
        }

        // In place of Object.Destroy(playerRespawnSystem) at the mod's run end: keep the component, and with it its hooks.
        static void KeepOrDestroy(object system)
        {
            if (system == null) return;
            try
            {
                ClearTimers(system);
                Kept = system;
                claimed = false;
                if (!loggedKept)
                {
                    loggedKept = true;
                    CompatShieldPatcher.Log.LogInfo("PlayerRespawnSystem: kept its respawn hooks at run end instead of removing them (shown once)");
                }
            }
            catch (Exception e)
            {
                Kept = null;
                if (!loggedKeepError)
                {
                    loggedKeepError = true;
                    CompatShieldPatcher.Log.LogError("Respawn hook churn fix: keeping the respawn system at run end failed, so it is destroyed as the mod does (shown once): " + e);
                }
                DestroyObject(system);
            }
        }

        // In place of gameObject.AddComponent<PlayerRespawnSystem>() at the mod's run start: the kept component, reset, or a new one.
        static T ReuseOrAdd<T>(GameObject host) where T : class
        {
            var kept = Kept;
            if (kept != null)
            {
                try
                {
                    if (IsAlive(kept) && !SameControllerSet(kept))
                    {
                        // The mod would build a different set now (UseTimedRespawn toggled by a runtime config editor): let it.
                        Kept = null;
                        DestroyObjectNow(kept);
                        CompatShieldPatcher.Log.LogInfo("PlayerRespawnSystem: its enabled respawn controllers changed since the last run, so the kept system was rebuilt");
                    }
                    else if (IsAlive(kept))
                    {
                        ResetRunState(kept);
                        claimed = true;
                        Reused++;
                        if (!loggedReused)
                        {
                            loggedReused = true;
                            CompatShieldPatcher.Log.LogInfo("PlayerRespawnSystem: reused its respawn hooks from the earlier run instead of re-adding them (shown once)");
                        }
                        return (T)kept;
                    }
                }
                catch (Exception e)
                {
                    // A half-reset component must not stay beside a fresh one (every hook would run twice), and it must be gone
                    // before the fresh one's OnEnable: a deferred Destroy would leave it as PlayerRespawnSystem.instance until end
                    // of frame, so SingletonHelper would reject the new one and then null the singleton for the whole run.
                    Kept = null;
                    if (!loggedReuseError)
                    {
                        loggedReuseError = true;
                        CompatShieldPatcher.Log.LogError("Respawn hook churn fix: reusing the kept respawn system failed, so it is destroyed and the mod builds a new one (shown once): " + e);
                    }
                    try { DestroyObjectNow(kept); }
                    catch (Exception e2) { CompatShieldPatcher.Log.LogError("Respawn hook churn fix: destroying it failed too: " + e2); }
                }
                Kept = null;
            }
            Built++;
            var built = AddSystem(host, typeof(T));
            // Tracked from the start: should a run end never reach KeepOrDestroy (an exception earlier in Run.OnDestroy's hook chain),
            // the next run still gets this one back instead of building a second one beside it (the mod's own latent double-hook case).
            Kept = built;
            claimed = true;
            return (T)built;
        }

        static void AwakePrefix() => claimed = false;

        // After the mod's Run.Awake hook: a kept component it didn't take back belongs to a run in a game mode the mod ignores, so
        // it goes the way the mod's own run end would have sent it (its hooks come out). The next normal run builds a fresh one.
        static void AwakePostfix()
        {
            try
            {
                if (Kept == null || claimed) return;
                var kept = Kept;
                Kept = null;
                if (!IsAlive(kept)) return;
                DestroyedKept++;
                DestroyObject(kept);
                CompatShieldPatcher.Log.LogInfo("PlayerRespawnSystem: a run in a game mode it ignores started, so its respawn hooks from the earlier run were removed");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Respawn hook churn fix: removing the kept respawn system at an ignored game mode's run start failed: " + e);
            }
        }

        // Redoes what a fresh component's Awake chain leaves behind. Any failure throws, and the caller rebuilds the mod's way.
        internal static void ResetRunState(object system)
        {
            var respawner = Field(system, "playerRespawner") ?? throw new MissingMemberException("playerRespawner is null");
            var queue = Field(respawner, "respawnQueue") ?? throw new MissingMemberException("respawnQueue is null");
            queue.GetType().GetMethod("Clear", Any, null, Type.EmptyTypes, null).Invoke(queue, null);
            Set(respawner, "IsAdvancingStage", false);
            foreach (var controller in Controllers(system))
                Set(controller, "IsActive", IsTimed(controller));   // the mod's own setter: logs "Activating/Deactivating ..." on a change
            ClearTimers(system);
        }

        // The Timed controller's user timers (Run.OnDestroy's hook clears them too; this covers a run end that never got there).
        static void ClearTimers(object system)
        {
            foreach (var controller in Controllers(system))
            {
                if (!IsTimed(controller)) continue;
                var timers = Field(controller, "userRespawnTimers") ?? throw new MissingMemberException("userRespawnTimers is null");
                timers.GetType().GetMethod("ClearTimers", Any, null, Type.EmptyTypes, null).Invoke(timers, null);
            }
        }

        static bool IsTimed(object controller) => controller.GetType().Name == "TimedRespawnController";

        // The mod's own choice (RespawnController.GetRespawnControllerTypes + IsEnabled): every type carrying AssociatedRespawnType
        // whose static IsEnabled is true right now.
        static List<Type> ControllerTypes(Assembly mod) =>
            mod.GetTypes().Where(t => !t.IsAbstract && t.GetCustomAttributes(false).Any(a => a.GetType().Name == "AssociatedRespawnType")).ToList();

        static bool SameControllerSet(object system)
        {
            var want = new HashSet<Type>(ControllerTypes(systemType.Assembly)
                .Where(t => (bool)t.GetProperty("IsEnabled", BindingFlags.Public | BindingFlags.Static).GetValue(null, null)));
            var have = new HashSet<Type>(Controllers(system).Select(c => c.GetType()));
            return have.SetEquals(want);
        }

        static IEnumerable<object> Controllers(object system)
        {
            var dict = Field(system, "respawnControllers") as System.Collections.IDictionary ?? throw new MissingMemberException("respawnControllers is null");
            foreach (var controller in dict.Values)
                if (controller != null) yield return controller;
        }

        static object Field(object obj, string name) =>
            (obj.GetType().GetField(name, Any) ?? throw new MissingMemberException(obj.GetType().Name + "." + name)).GetValue(obj);

        static void Set(object obj, string name, object value)
        {
            var prop = obj.GetType().GetProperty(name, Any) ?? throw new MissingMemberException(obj.GetType().Name + "." + name);
            (prop.GetSetMethod(true) ?? throw new MissingMemberException(obj.GetType().Name + "." + name + " setter")).Invoke(obj, new[] { value });
        }
    }

    /// <summary>
    /// WolfoFixes, Audio.UnusedSounds: its ShopTerminalBehavior.PreStartClient hook posts Play_ui_obj_lunarPool_idle_loop
    /// (three times) on every object named "LunarCauldron, ..." or "ShrineCleanse...", restoring the game's unused Lunar
    /// Cauldron / Cleansing Pool idle hum, and never posts Stop_ui_obj_lunarPool_idle_loop (both events exist in the game's
    /// Global.bnk; the mod has no toggle for it). Wwise keeps a loop playing after its object is destroyed, and a "ui" event
    /// is 2D, so when the stage or run ends the hum follows the player into the lobby and the next stages until the game
    /// restarts (2026-10-18 23:05, after a team wipe on Commencement, which is full of cauldrons). Fix: right after that hook
    /// has run, give each pool a small component that posts the Stop event the moment the pool is disabled or destroyed.
    /// Every PC plays the loop itself, so every PC needs this.
    /// </summary>
    public static class LunarPoolLoopStopFix
    {
        internal const string PlayEvent = "Play_ui_obj_lunarPool_idle_loop";
        internal const string StopEvent = "Stop_ui_obj_lunarPool_idle_loop";
        internal static Func<string, GameObject, uint> PlaySound;   // RoR2.Util.PlaySound(string, GameObject); the harness swaps in a counter
        internal static int Stopped;                                // read by the test harness
        static bool loggedStopped, loggedAddError;

        public static void Apply(Harmony h)
        {
            try
            {
                var terminal = Type.GetType("RoR2.ShopTerminalBehavior, RoR2");
                var preStart = terminal?.GetMethod("PreStartClient", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                var play = Type.GetType("RoR2.Util, RoR2")?.GetMethod("PlaySound", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(string), typeof(GameObject) }, null);
                if (preStart == null || play == null || play.ReturnType != typeof(uint))
                {
                    CompatShieldPatcher.Log.LogWarning("Lunar pool loop fix: RoR2.ShopTerminalBehavior.PreStartClient or Util.PlaySound(string, GameObject) " +
                        "not found (game updated?), not applied");
                    return;
                }
                PlaySound = (Func<string, GameObject, uint>)Delegate.CreateDelegate(typeof(Func<string, GameObject, uint>), play);
                h.Patch(preStart, postfix: new HarmonyMethod(typeof(LunarPoolLoopStopFix).GetMethod(nameof(PreStartPostfix), BindingFlags.NonPublic | BindingFlags.Static)));
                CompatShieldPatcher.Log.LogInfo("Lunar Cauldron / Cleansing Pool: the idle hum WolfoFixes restores now stops when its pool goes away, " +
                    "instead of following you into the lobby");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Lunar pool loop fix failed to apply: " + e);
            }
        }

        /// <summary>WolfoFixes' own test: objects named "LunarCauldron, WhiteToGreen" etc. and "ShrineCleanse...".</summary>
        internal static bool IsLunarPool(string name) =>
            name != null && (name.StartsWith("LunarCauldron", StringComparison.Ordinal) || name.StartsWith("ShrineCleanse", StringComparison.Ordinal));

        // Postfix on ShopTerminalBehavior.PreStartClient (where WolfoFixes starts the loop). Never throws into the game.
        static void PreStartPostfix(object __instance)
        {
            try
            {
                if (!(__instance is Component c) || !IsLunarPool(c.name)) return;
                var go = c.gameObject;
                if (go.GetComponent<LunarPoolLoopStopper>() == null) go.AddComponent<LunarPoolLoopStopper>();
            }
            catch (Exception e)
            {
                if (loggedAddError) return;
                loggedAddError = true;
                CompatShieldPatcher.Log.LogError("Lunar pool loop fix: couldn't attach the stopper to a pool (shown once): " + e);
            }
        }

        /// <summary>Posts the Stop event on the pool. Null-safe; counted for the harness.</summary>
        internal static void StopOn(GameObject pool)
        {
            if (pool == null || PlaySound == null) return;
            PlaySound(StopEvent, pool);
            Stopped++;
            if (loggedStopped) return;
            loggedStopped = true;
            CompatShieldPatcher.Log.LogInfo("Lunar Cauldron / Cleansing Pool: stopped its idle hum as the pool went away (shown once)");
        }
    }

    /// <summary>Posts the lunar pool Stop event when its object is disabled or destroyed, whichever comes first.</summary>
    public class LunarPoolLoopStopper : MonoBehaviour
    {
        bool done;
        void OnDisable() { Stop(); }
        void OnDestroy() { Stop(); }
        void Stop()
        {
            if (done) return;
            done = true;
            try { LunarPoolLoopStopFix.StopOn(gameObject); }
            catch { }
        }
    }

    /// <summary>
    /// Shaman 1.2.0 (StormRiders, deprecated 2024-10-28): its Prefabs.SetupCharacterModel does "characterModel.invisibilityCount = 0"
    /// as a field store. Game 1.5.0 made CharacterModel.invisibilityCount a property, so that method can't even be compiled
    /// (MissingFieldException the first time it is called), the Shaman body and its three Acolyte bodies are never set up and
    /// the mod's Awake aborts. It is the only game reference the DLL gets wrong (786 checked, 2026-10-18). Fix: re-emit the
    /// method from its IL on disk (Cecil) with that one store turned into a call to the property setter (a reflection-based
    /// copy can't even read the body: the unresolvable field token stops it), then point its two callers, the one-argument
    /// overload and CharacterBase.InitializeCharacterModel, at the re-emitted copy. Every PC builds the prefabs, so every PC
    /// needs this; the mod is NetworkCompatibility-flagged, so every player must have the mod itself anyway.
    /// Second problem, seen the moment the first one was fixed (2026-10-18 23:57): game 1.5.0 added its own Acolyte enemy
    /// (RoR2/DLC4/Acolyte/AcolyteBody.prefab), and Shaman's summon body is also named "AcolyteBody", so BodyCatalog.SetBodyPrefabs
    /// threw "An item with the same key has already been added" and the loading screen stalled at 99%. Shaman names every body
    /// through Prefabs.CreateBodyPrefab(bodyName, modelName, bodyInfo) (component.name = bodyInfo.bodyName) and never looks its
    /// Acolyte up by that string, so a prefix on that helper renames a clashing bodyInfo.bodyName to "Shaman" + name before the
    /// prefab is named; its doppelganger master follows (bodyInfo.bodyName + "MonsterMaster"). Vanilla's master is
    /// AcolyteMonsterMaster; Shaman's AcolyteMaster and AcolyteBodyMonsterMaster don't clash.
    /// </summary>
    /// <summary>
    /// Bubbets Items 1.8.20 against game 1.5.0. Jellied Soles' damage hook lands in the wrong place and emits code the runtime
    /// rejects; that bad hook stays on HealthComponent.TakeDamageProcess, so every later mod hooking that method fails too.
    /// The rest is smaller: a hook aimed at the wrong Mathf.Max, a Harmony target that was renamed, and methods that read
    /// members the game no longer has. Methods with dead references can't be patched themselves (compiling them fails), so
    /// their callers are pointed at replacements here. Inventory.RemoveItem(ItemDef, int) is added back by the preloader.
    /// </summary>
    /// <summary>
    /// Smite Items' Dagger of Frenzy hook on GenericSkill.OnExecute reads characterBody.skillLocator.primary.skillDef with no null
    /// check, and does so before calling the original. On a body with no skill locator or no primary skill it throws, so the skill
    /// never fires and the game tries again (702 errors in one Bazaar visit). This adds the two checks. When either is missing the
    /// buff is skipped and the skill runs; everything else in the hook is unchanged. Patched at the first run start, so nothing
    /// here runs while the game loads.
    /// </summary>
    public static class DaggerOfFrenzyFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        const string TypeName = "Smite_Items.Equipment.DaggerOfFrenzy";
        const string MethodName = "ApplyDaggerAttackSpeedBuff";
        static readonly FieldInfo labelsField = typeof(CodeInstruction).GetField("labels");
        static System.Collections.IList LabelsOf(CodeInstruction ci) => (System.Collections.IList)labelsField.GetValue(ci);
        static Harmony harmony;
        static Assembly mod;
        static bool tried;
        static bool matched;

        public static void Apply(Harmony h, Assembly smite, Assembly ror2)
        {
            harmony = h;
            mod = smite;
            try
            {
                var ev = ror2.GetType("RoR2.Run")?.GetEvent("onRunStartGlobal", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) throw new MissingMemberException("RoR2.Run.onRunStartGlobal not found");
                ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(DaggerOfFrenzyFix).GetMethod(nameof(OnRunStart), Any)));
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Dagger of Frenzy guard: can't watch run starts, not applied: " + e.Message);
            }
        }

        static void OnRunStart(object run)
        {
            if (tried) return;
            tried = true;
            Patch(harmony, mod);
        }

        internal static void Patch(Harmony h, Assembly asm)
        {
            try
            {
                var target = asm.GetType(TypeName)?.GetMethod(MethodName, Any);
                if (target == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Dagger of Frenzy guard: " + MethodName + " not found (mod updated?), not applied");
                    return;
                }
                matched = false;
                h.Patch(target, transpiler: new HarmonyMethod(typeof(DaggerOfFrenzyFix).GetMethod(nameof(Transpiler), Any)));
                if (matched) CompatShieldPatcher.Log.LogInfo("Smite Items: Dagger of Frenzy no longer breaks skills on bodies with no skill locator or primary skill");
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogError("Dagger of Frenzy guard: failed to apply: " + e); }
        }

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = instructions.ToList();
            int at = -1, hits = 0;
            for (int i = 0; i + 6 < code.Count; i++)
                if (IsShape(code, i)) { at = i; hits++; }
            if (hits != 1)
            {
                CompatShieldPatcher.Log.LogWarning($"Dagger of Frenzy guard: expected one primary skill check, found {hits} (mod updated?), not applied");
                return code;
            }
            var bail = code[at + 6].operand;
            object haveLocator = generator.DefineLabel();
            object havePrimary = generator.DefineLabel();
            LabelsOf(code[at + 1]).Add(haveLocator);
            LabelsOf(code[at + 2]).Add(havePrimary);
            code.InsertRange(at + 2, Guard(havePrimary, bail));
            code.InsertRange(at + 1, Guard(haveLocator, bail));
            matched = true;
            return code;
        }

        // Stack on entry: the reference to test. A null one is dropped and the buff check is skipped, the same exit as a mismatch.
        static CodeInstruction[] Guard(object next, object bail) => new[]
        {
            new CodeInstruction(OpCodes.Dup),
            new CodeInstruction(OpCodes.Brtrue, next),
            new CodeInstruction(OpCodes.Pop),
            new CodeInstruction(OpCodes.Br, bail),
        };

        static bool IsShape(List<CodeInstruction> c, int i) =>
            Named(c[i], "get_skillLocator") && (Named(c[i + 1], "primary") || Named(c[i + 1], "get_primary")) && Named(c[i + 2], "get_skillDef")
            && c[i + 3].opcode.Name.StartsWith("ldarg") && Named(c[i + 4], "get_skillDef") && Named(c[i + 5], "op_Equality")
            && (c[i + 6].opcode == OpCodes.Brfalse || c[i + 6].opcode == OpCodes.Brfalse_S);

        static bool Named(CodeInstruction ci, string name) => ci.operand is MemberInfo m && m.Name == name;
    }

    /// <summary>
    /// Sandswept's Theta Construct casts a shield on a nearby friend. When the friend is another Theta, or something blocks the
    /// line to it, CastShield.OnEnter returns before DeductStock, so the skill stays ready and the AI fires it again on the next
    /// tick. Each failed cast's OnExit runs FindObjectsOfType (over 20 ms of each physics step on Aqueduct) and plays the expire
    /// sound on both bodies, the constant loop players hear. A failed cast now puts the skill on a one second retry and skips
    /// that exit. Host only in effect, because enemy AI runs on the host.
    /// </summary>
    public static class ThetaShieldLoopFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        const string StateName = "Sandswept.Enemies.ThetaConstruct.CastShield";
        internal const float RetrySeconds = 1f;
        static readonly object Marker = new object();
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> failed = new System.Runtime.CompilerServices.ConditionalWeakTable<object, object>();
        static Harmony harmony;
        static Assembly mod;
        static Assembly game;
        static bool tried;
        static bool loggedFirst;
        static bool loggedError;
        static FieldInfo shieldField;
        static FieldInfo primaryField;
        static PropertyInfo authorityProp;
        static PropertyInfo locatorProp;
        static PropertyInfo cooldownProp;
        static PropertyInfo stopwatchProp;
        static MethodInfo deductStock;

        // Swappable so the harness can run the flow without Unity.
        internal static Func<object, bool> IsAuthority = RealIsAuthority;
        internal static Func<object, bool> HasShield = RealHasShield;
        internal static Action<object> BackOff = RealBackOff;

        public static void Apply(Harmony h, Assembly sandswept, Assembly ror2)
        {
            harmony = h;
            mod = sandswept;
            game = ror2;
            try
            {
                var ev = ror2.GetType("RoR2.Run")?.GetEvent("onRunStartGlobal", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) throw new MissingMemberException("RoR2.Run.onRunStartGlobal not found");
                ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(ThetaShieldLoopFix).GetMethod(nameof(OnRunStart), Any)));
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Theta shield loop fix: can't watch run starts, not applied: " + e.Message);
            }
        }

        static void OnRunStart(object run)
        {
            if (tried) return;
            tried = true;
            Patch(harmony, mod, game);
        }

        internal static void Patch(Harmony h, Assembly asm, Assembly ror2)
        {
            try
            {
                var state = asm.GetType(StateName);
                var enter = state?.GetMethod("OnEnter", Any | BindingFlags.DeclaredOnly);
                var exit = state?.GetMethod("OnExit", Any | BindingFlags.DeclaredOnly);
                shieldField = state?.GetField("shieldInstance", Any);
                authorityProp = state?.GetProperty("isAuthority", Any);
                locatorProp = state?.GetProperty("skillLocator", Any);
                primaryField = ror2?.GetType("RoR2.SkillLocator")?.GetField("primary", Any);
                var skill = ror2?.GetType("RoR2.GenericSkill");
                deductStock = skill?.GetMethod("DeductStock", Any, null, new[] { typeof(int) }, null);
                cooldownProp = skill?.GetProperty("cooldownRemaining", Any);
                stopwatchProp = skill?.GetProperty("rechargeStopwatch", Any);
                if (enter == null || exit == null || shieldField == null || authorityProp == null || locatorProp == null
                    || primaryField == null || deductStock == null || cooldownProp == null || stopwatchProp == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Theta shield loop fix: CastShield or a skill member not found (mod updated?), not applied");
                    return;
                }
                h.Patch(enter, postfix: new HarmonyMethod(typeof(ThetaShieldLoopFix).GetMethod(nameof(EnterPostfix), Any)));
                h.Patch(exit, prefix: new HarmonyMethod(typeof(ThetaShieldLoopFix).GetMethod(nameof(ExitPrefix), Any)));
                CompatShieldPatcher.Log.LogInfo("Sandswept: a Theta Construct's failed shield cast now retries after " + RetrySeconds + " s instead of every tick");
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogError("Theta shield loop fix: failed to apply: " + e); }
        }

        static void EnterPostfix(object __instance)
        {
            try
            {
                if (!IsAuthority(__instance) || HasShield(__instance)) return;
                failed.GetValue(__instance, k => Marker);
                BackOff(__instance);
                if (!loggedFirst)
                {
                    loggedFirst = true;
                    CompatShieldPatcher.Log.LogInfo("Theta shield loop fix: stopped a failed shield cast from repeating (first time this launch)");
                }
            }
            catch (Exception e)
            {
                if (!loggedError)
                {
                    loggedError = true;
                    CompatShieldPatcher.Log.LogWarning("Theta shield loop fix: couldn't delay a failed cast: " + e.Message);
                }
            }
        }

        static bool ExitPrefix(object __instance)
        {
            object seen;
            return !failed.TryGetValue(__instance, out seen);
        }

        static bool RealIsAuthority(object state) => (bool)authorityProp.GetValue(state, null);

        static bool RealHasShield(object state) => (shieldField.GetValue(state) as UnityEngine.Object) != null;

        static void RealBackOff(object state)
        {
            var locator = locatorProp.GetValue(state, null) as UnityEngine.Object;
            if (locator == null) return;
            var slot = primaryField.GetValue(locator) as UnityEngine.Object;
            if (slot == null) return;
            deductStock.Invoke(slot, new object[] { 1 });
            float left = (float)cooldownProp.GetValue(slot, null);
            if (left > RetrySeconds) stopwatchProp.SetValue(slot, (float)stopwatchProp.GetValue(slot, null) + left - RetrySeconds, null);
        }
    }

    /// <summary>
    /// ExtraSkillSlots builds its four extra icons by copying the HUD's skill icons and looking for children named
    /// SkillBackgroundPanel and SkillKeyText. A replacement HUD such as RiskUI's has different children, so the lookup throws in
    /// ExtraHud.Awake, all four icons stay null, and the slots work but can't be seen. This builds the copy without those names.
    /// With the normal HUD the panel is there and the mod's own code runs unchanged.
    /// </summary>
    public static class SkillSlotsHudFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        const string PanelName = "SkillBackgroundPanel";
        const string ModifierAction = "ExtraModifier";
        const float RowGap = 1.15f;
        const float FallbackHeight = 64f;
        static readonly HashSet<string> logged = new HashSet<string>();
        static Type switchType;
        static FieldInfo modifierField;
        static readonly List<PlacedIcon> placed = new List<PlacedIcon>();

        sealed class PlacedIcon
        {
            public RectTransform Copy;
            public RectTransform Source;
            public Component Icon;
            public int ActiveFrames;
        }

        // Swapped by the harness, which has no Unity to build icons with. Returns null to let the mod's own code run.
        internal static Func<string, object, object> Build = BuildForOtherHud;

        public static void Apply(Harmony h, Assembly asm)
        {
            try
            {
                var target = asm.GetType("ExtraSkillSlots.ExtraHud")?.GetMethod("CopyUISkillSlot", Any);
                switchType = asm.GetType("ExtraSkillSlots.SkillInputDisplaySwitch");
                modifierField = switchType?.GetField("modifierGO", Any);
                if (target == null || modifierField == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Extra skill slots fix: ExtraHud.CopyUISkillSlot or its key switch not found (mod updated?), not applied");
                    return;
                }
                h.Patch(target, prefix: new HarmonyMethod(typeof(SkillSlotsHudFix).GetMethod(nameof(Prefix), Any)));
                var update = target.DeclaringType.GetMethod("Update", Any);
                if (update != null) h.Patch(update, postfix: new HarmonyMethod(typeof(SkillSlotsHudFix).GetMethod(nameof(KeepPlaced), Any)));
                else CompatShieldPatcher.Log.LogWarning("Extra skill slots fix: ExtraHud.Update not found (mod updated?), the icons may stack in one spot");
                CompatShieldPatcher.Log.LogInfo("ExtraSkillSlots: the four extra icons will be built to fit a replacement HUD such as RiskUI");
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogError("Extra skill slots fix: failed to apply: " + e); }
        }

        internal static bool Prefix(string __0, object __1, ref object __result)
        {
            try
            {
                var made = Build(__0, __1);
                if (made == null) return true;
                __result = made;
                return false;
            }
            catch (Exception e)
            {
                Once("error", "Extra skill slots fix: couldn't build an icon, the slots stay hidden: " + (e.InnerException ?? e).Message);
                return true;
            }
        }

        static object BuildForOtherHud(string actionName, object skillsContainer)
        {
            var icon = skillsContainer as Component;
            if (icon == null) return null;
            var source = icon.transform;
            if (source.Find(PanelName) != null) return null;
            var copy = UnityEngine.Object.Instantiate(source, source.parent);
            try
            {
                var grouped = Place((RectTransform)copy, (RectTransform)source, source.parent);
                var labelled = WireKey(copy, actionName, icon.GetType().Assembly);
                var rect = ((RectTransform)source).rect;
                Once("done", $"ExtraSkillSlots: built the extra icons for this HUD (parent {source.parent.name}, layout group {(grouped ? "yes" : "no")}, icon {rect.width:0}x{rect.height:0}, key label {(labelled ? "yes" : "no")})");
                var built = copy.GetComponent(icon.GetType());
                placed.Add(new PlacedIcon { Copy = (RectTransform)copy, Source = (RectTransform)source, Icon = built });
                return built;
            }
            catch
            {
                UnityEngine.Object.Destroy(copy.gameObject);
                throw;
            }
        }

        // Puts the copy one row above the icon it came from. A layout group would drag it back into the row, so it opts out of one.
        static bool Place(RectTransform copy, RectTransform source, Transform parent)
        {
            var groupType = Type.GetType("UnityEngine.UI.LayoutGroup, UnityEngine.UI");
            bool grouped = groupType != null && parent.GetComponent(groupType) != null;
            if (grouped)
            {
                var elementType = Type.GetType("UnityEngine.UI.LayoutElement, UnityEngine.UI", true);
                var element = copy.GetComponent(elementType);
                if (element == null) element = copy.gameObject.AddComponent(elementType);
                elementType.GetProperty("ignoreLayout").SetValue(element, true, null);
                if (parent is RectTransform parentRect)
                    Type.GetType("UnityEngine.UI.LayoutRebuilder, UnityEngine.UI", true).GetMethod("ForceRebuildLayoutImmediate").Invoke(null, new object[] { parentRect });
            }
            copy.anchorMin = source.anchorMin;
            copy.anchorMax = source.anchorMax;
            copy.pivot = source.pivot;
            copy.sizeDelta = source.sizeDelta;
            copy.anchoredPosition = source.anchoredPosition + new Vector2(0f, AboveOffset(source.rect.height, source.sizeDelta.y, source.localScale.y));
            return grouped;
        }

        // The HUD has no sizes yet when the copies are built, so they start with no size and all in one spot.
        // Each frame, once the real icon has been laid out, its copy takes the same size and sits one row above it.
        internal static void KeepPlaced()
        {
            for (int i = placed.Count - 1; i >= 0; i--)
            {
                var icon = placed[i];
                if (icon.Copy == null || icon.Source == null) { placed.RemoveAt(i); continue; }
                try { Align(icon); }
                catch (Exception e)
                {
                    placed.RemoveAt(i);
                    Once("align", "Extra skill slots fix: couldn't position an icon: " + e.Message);
                }
            }
        }

        static void Align(PlacedIcon icon)
        {
            var size = icon.Source.rect.size;
            if (size.y < 1f) return;
            if (icon.Copy.pivot != icon.Source.pivot) icon.Copy.pivot = icon.Source.pivot;
            if (Math.Abs(icon.Copy.rect.width - size.x) > 0.01f) icon.Copy.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
            if (Math.Abs(icon.Copy.rect.height - size.y) > 0.01f) icon.Copy.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);
            // TransformPoint already applies the icon's scale, so the offset is worked out at scale 1.
            var spot = icon.Source.TransformPoint(new Vector3(0f, AboveOffset(size.y, 0f, 1f), 0f));
            if (icon.Copy.position != spot) icon.Copy.position = spot;
            if (icon.Copy.gameObject.activeInHierarchy && !logged.Contains("placed") && ++icon.ActiveFrames > 30) LogPlaced(icon, size);
        }

        // One line per launch, so the next log says whether the skill picture really got a size.
        static void LogPlaced(PlacedIcon icon, Vector2 size)
        {
            var type = icon.Icon.GetType();
            var picture = type.GetField("iconImage", Any)?.GetValue(icon.Icon) as Component;
            var pictureRect = picture != null ? ((RectTransform)picture.transform).rect : default(Rect);
            var skill = type.GetField("targetSkill", Any)?.GetValue(icon.Icon) as UnityEngine.Object;
            if (logged.Add("placed")) CompatShieldPatcher.Log.LogInfo($"ExtraSkillSlots: placed an extra icon above its skill (icon {size.x:0}x{size.y:0}, picture {pictureRect.width:0}x{pictureRect.height:0}, skill {(skill != null ? "set" : "none")})");
        }

        internal static float AboveOffset(float rectHeight, float sizeHeight, float scaleY)
        {
            float height = rectHeight >= 1f ? rectHeight : sizeHeight >= 1f ? sizeHeight : FallbackHeight;
            float scale = Math.Abs(scaleY) > 0.01f ? Math.Abs(scaleY) : 1f;
            return height * scale * RowGap;
        }

        // The same wiring the mod does once it has found its key text: one label for the slot, the original for the modifier.
        static bool WireKey(Transform copy, string actionName, Assembly game)
        {
            var controllerType = game.GetType("RoR2.InputBindingDisplayController");
            var locatorType = game.GetType("RoR2.UI.MPEventSystemLocator");
            var actionField = controllerType?.GetField("actionName", Any);
            if (actionField == null || locatorType == null) return false;
            var original = copy.GetComponentInChildren(controllerType, true);
            if (original == null) return false;
            var panel = original.transform.parent;
            var second = UnityEngine.Object.Instantiate(original.transform, panel);
            actionField.SetValue(second.GetComponent(controllerType), actionName);
            actionField.SetValue(original, ModifierAction);
            if (panel.GetComponent(locatorType) == null) panel.gameObject.AddComponent(locatorType);
            modifierField.SetValue(panel.gameObject.AddComponent(switchType), original.gameObject);
            return true;
        }

        static void Once(string key, string message)
        {
            if (!logged.Add(key)) return;
            if (key == "done") CompatShieldPatcher.Log.LogInfo(message);
            else CompatShieldPatcher.Log.LogWarning(message);
        }
    }

    /// <summary>
    /// LinearDamage removes vanilla Focus Crystal by matching the item load and the count call side by side. Bubbets Items puts
    /// its Shifted Quartz and Zealotry Embrace code between those two instructions, so the match fails, vanilla stays, and the
    /// additive bonus LinearDamage adds lands on top: Focus Crystal counts twice. This does the same removal past that code.
    /// When the two are still adjacent, or the code between them isn't plain inserted code, the mod's own hook runs unchanged.
    /// </summary>
    public static class LinearDamageFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        const string FocusCrystalType = "LinearDamage.Tweaks.Items.FocusCrystal";
        const int MaxInserted = 40;
        static readonly HashSet<string> logged = new HashSet<string>();

        public static void Apply(Harmony h, Assembly asm)
        {
            try
            {
                var target = asm.GetType(FocusCrystalType)?.GetMethod("RemoveVanilla", Any, null, new[] { typeof(MonoMod.Cil.ILContext) }, null);
                if (target == null)
                {
                    CompatShieldPatcher.Log.LogWarning("LinearDamage fix: FocusCrystal.RemoveVanilla not found (mod updated?), not applied");
                    return;
                }
                h.Patch(target, prefix: new HarmonyMethod(typeof(LinearDamageFix).GetMethod(nameof(Prefix), Any)));
                CompatShieldPatcher.Log.LogInfo("LinearDamage: Focus Crystal's vanilla bonus will be removed even with other mods' code in that line");
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogError("LinearDamage fix: failed to apply: " + e); }
        }

        static bool IsFocusCrystalLoad(Mono.Cecil.Cil.Instruction i) =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Ldsfld && i.Operand is FieldReference f && f.Name == "NearbyDamageBonus" && f.DeclaringType.Name == "Items";

        static bool IsAnyItemLoad(Mono.Cecil.Cil.Instruction i) =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Ldsfld && i.Operand is FieldReference f && f.DeclaringType.Name == "Items";

        static bool IsCountCall(Mono.Cecil.Cil.Instruction i) =>
            (i.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt || i.OpCode == Mono.Cecil.Cil.OpCodes.Call)
            && i.Operand is MethodReference m && m.Name == "GetItemCountEffective" && m.DeclaringType.FullName == "RoR2.Inventory";

        static bool IsPlainCode(Mono.Cecil.Cil.Instruction i) =>
            i.OpCode.FlowControl == Mono.Cecil.Cil.FlowControl.Next || i.OpCode.FlowControl == Mono.Cecil.Cil.FlowControl.Call;

        // Returns false when it did the removal, true to let the mod's own hook run (and log its own failure if it fails).
        internal static bool Prefix(MonoMod.Cil.ILContext il)
        {
            try
            {
                var c = new MonoMod.Cil.ILCursor(il);
                if (!c.TryGotoNext(MonoMod.Cil.MoveType.After, IsFocusCrystalLoad)) return true;
                if (c.Next != null && IsCountCall(c.Next)) return true;
                for (int seen = 0; seen < MaxInserted && c.Next != null; seen++)
                {
                    var next = c.Next;
                    if (IsCountCall(next))
                    {
                        c.Index++;
                        c.Emit(Mono.Cecil.Cil.OpCodes.Pop);
                        c.Emit(Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
                        Once("done", "LinearDamage: removed vanilla Focus Crystal past other mods' code, so it no longer counts twice");
                        return false;
                    }
                    if (IsAnyItemLoad(next) || !IsPlainCode(next)) break;
                    c.Index++;
                }
                Once("shape", "LinearDamage fix: the code around Focus Crystal isn't the expected shape; the mod's own hook runs unchanged");
            }
            catch (Exception e) { Once("error", "LinearDamage fix: the Focus Crystal hook failed: " + e.Message); }
            return true;
        }

        static void Once(string key, string message)
        {
            if (!logged.Add(key)) return;
            if (key == "done") CompatShieldPatcher.Log.LogInfo(message);
            else CompatShieldPatcher.Log.LogWarning(message);
        }
    }

    public static class BubbetsFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal const string SolesName = "BubbetsItems.Items.JelliedSoles", PlateName = "BubbetsItems.Items.RepulsionPlateMk2",
            ScrapName = "BubbetsItems.Items.VoidJunkToScrapTier1", EquipmentName = "BubbetsItems.EquipmentBase",
            BarDataName = "BubbetsItems.Behaviours.ExtraHealthBarSegments+BarData", SlugDataName = "BubbetsItems.Items.BarrierItems.EternalSlug+SlugData";

        static Harmony harmony;
        static Assembly mod, game;
        static bool done;
        static readonly HashSet<string> logged = new HashSet<string>();
        internal static bool SolesRewritten;
        internal static int PlateHooked, SlotCallsRedirected, StyleCallsRedirected;
        static MethodInfo doMk2;

        public static void Apply(Harmony h, Assembly bubbets, Assembly ror2)
        {
            harmony = h;
            mod = bubbets;
            game = ror2;
            ApplySoles(h, bubbets.GetType(SolesName));
            ApplyPlate(h, bubbets.GetType(PlateName));
            try
            {
                var ev = ror2.GetType("RoR2.Run")?.GetEvent("onRunStartGlobal", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) throw new MissingMemberException("RoR2.Run.onRunStartGlobal not found");
                ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(BubbetsFix).GetMethod(nameof(OnRunStart), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Bubbets fix: can't watch run starts, its Void Scrap, Broken Clock, Wildlife Camera and Slug fixes are not applied: " + e.Message);
            }
        }

        static HarmonyMethod M(string name) => new HarmonyMethod(typeof(BubbetsFix).GetMethod(name, Any));

        // ---- Jellied Soles: must be in place before the plugin's Awake runs its Harmony patches ----
        static void ApplySoles(Harmony h, Type soles)
        {
            var target = soles?.GetMethod("IlTakeDamage", Any);
            if (target == null)
            {
                CompatShieldPatcher.Log.LogWarning("Bubbets fix: JelliedSoles.IlTakeDamage not found (mod updated?), not applied");
                return;
            }
            SolesRewritten = false;
            try { h.Patch(target, transpiler: M(nameof(SolesTranspiler))); }
            catch (Exception e) { CompatShieldPatcher.Log.LogError("Bubbets fix: the Jellied Soles rewrite failed: " + e); }
            if (SolesRewritten)
            {
                CompatShieldPatcher.Log.LogInfo("Bubbets Items: Jellied Soles' damage hook now fits game 1.5.0, so it no longer breaks every later damage hook");
                return;
            }
            // Left as written it takes every later TakeDamageProcess hook down with it, so losing this one hook is the safe failure.
            try
            {
                h.Patch(target, prefix: M(nameof(SkipPrefix)));
                CompatShieldPatcher.Log.LogWarning("Bubbets fix: Jellied Soles' damage hook isn't the expected shape (mod updated?), not applied; the hook is switched off so it can't break other damage hooks");
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogError("Bubbets fix: couldn't switch off Jellied Soles' damage hook: " + e); }
        }

        static bool SkipPrefix() => false;

        static IEnumerable<CodeInstruction> SolesTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            if (RewriteSoles(code)) SolesRewritten = true;
            return code;
        }

        static int FindCecilOpCode(List<CodeInstruction> code, string name)
        {
            int found = -1;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Ldsfld || !(code[i].operand is FieldInfo f) || f.DeclaringType != typeof(Mono.Cecil.Cil.OpCodes) || f.Name != name) continue;
                if (found >= 0) return -1;
                found = i;
            }
            return found;
        }

        static bool IsCallNamed(CodeInstruction ci, string name) =>
            (ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && ci.operand is MethodBase m && m.Name == name;

        static bool LoadsInt(CodeInstruction ci, int value)
        {
            if (ci.opcode == OpCodes.Ldc_I4_0) return value == 0;
            if (ci.opcode == OpCodes.Ldc_I4_1) return value == 1;
            if (ci.opcode == OpCodes.Ldc_I4_2) return value == 2;
            return (ci.opcode == OpCodes.Ldc_I4 || ci.opcode == OpCodes.Ldc_I4_S) && ci.operand != null && Convert.ToInt32(ci.operand) == value;
        }

        /// <summary>
        /// The mod moves past the instruction its branch lands on and assumes that left the damage float on the stack. On 1.5.0
        /// that instruction starts building a DamageMacroData, so its delegate is handed a pointer. The rewrite makes it emit
        /// "ldloc damage; ldarg.0; ldloc master; ldarg.1; delegate; nop; stloc damage" at the branch target: stack neutral, and
        /// the same effect on damage as the old "after ldloc damage ... dup; stloc damage".
        /// Returns false, with the list untouched, when the shape isn't the expected one.
        /// </summary>
        internal static bool RewriteSoles(List<CodeInstruction> code)
        {
            int ldarg0 = FindCecilOpCode(code, "Ldarg_0"), ldloc = FindCecilOpCode(code, "Ldloc"), dup = FindCecilOpCode(code, "Dup"), stloc = FindCecilOpCode(code, "Stloc");
            if (ldarg0 < 13 || ldloc < ldarg0 || dup < ldloc || stloc < dup || stloc + 2 >= code.Count || ldloc + 3 >= code.Count) return false;
            int goNext = ldarg0 - 3;
            if (!IsCallNamed(code[goNext], "GotoNext") || code[goNext + 1].opcode != OpCodes.Pop) return false;
            if (!LoadsInt(code[goNext - 9], 2) || !LoadsInt(code[goNext - 8], 1) || code[goNext - 7].opcode != OpCodes.Newarr) return false;
            if (!IsCallNamed(code[ldloc + 2], "Emit") || code[ldloc + 3].opcode != OpCodes.Pop || !IsCallNamed(code[stloc + 2], "Emit")) return false;
            var nop = typeof(Mono.Cecil.Cil.OpCodes).GetField("Nop", BindingFlags.Public | BindingFlags.Static);
            if (nop == null) return false;

            var block = new List<CodeInstruction>();
            for (int i = ldloc - 1; i <= ldloc + 3; i++) block.Add(new CodeInstruction(code[i].opcode, code[i].operand));
            block[2] = new CodeInstruction(code[stloc + 1].opcode, code[stloc + 1].operand);   // the damage local's index in place of the master's

            code[goNext - 9].opcode = OpCodes.Ldc_I4_1;     // MoveType.AfterLabel: stop before the target, and take over branches to it
            code[goNext - 9].operand = null;
            code[dup].operand = nop;
            code.InsertRange(ldarg0 - 1, block);
            return true;
        }

        // ---- Repulsion Plate Mk2: 1.5.0 added a Mathf.Max ahead of the one the mod wants ----
        static void ApplyPlate(Harmony h, Type plate)
        {
            try
            {
                var target = plate?.GetMethod("TakeDamageHook", Any);
                doMk2 = plate?.GetMethod("DoMk2ArmorPlates", Any);
                if (target == null || doMk2 == null || !doMk2.IsStatic || doMk2.ReturnType != typeof(float))
                {
                    CompatShieldPatcher.Log.LogWarning("Bubbets fix: RepulsionPlateMk2 hook not found (mod updated?), not applied");
                    return;
                }
                h.Patch(target, prefix: M(nameof(PlatePrefix)));
                CompatShieldPatcher.Log.LogInfo("Bubbets Items: Repulsion Plate Mk2 now reduces the real incoming damage on game 1.5.0");
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogError("Bubbets fix: the Repulsion Plate Mk2 re-aim failed: " + e); }
        }

        static bool IsCall(Mono.Cecil.Cil.Instruction i, string type, string name)
        {
            if ((i.OpCode != Mono.Cecil.Cil.OpCodes.Call && i.OpCode != Mono.Cecil.Cil.OpCodes.Callvirt) || !(i.Operand is MethodReference m)) return false;
            return m.Name == name && m.DeclaringType.FullName == type;
        }

        static bool IsFloatMax(Mono.Cecil.Cil.Instruction i) =>
            IsCall(i, "UnityEngine.Mathf", "Max") && ((MethodReference)i.Operand).ReturnType.FullName == "System.Single";

        // The mod's own TakeDamageHook hooks the first Max after get_armor; replaced by the same hook on the second.
        static bool PlatePrefix(MonoMod.Cil.ILContext il)
        {
            try
            {
                var c = new MonoMod.Cil.ILCursor(il);
                if (c.TryGotoNext(MonoMod.Cil.MoveType.After, i => IsCall(i, "RoR2.CharacterBody", "get_armor"))
                    && c.TryGotoNext(MonoMod.Cil.MoveType.After, IsFloatMax)
                    && c.TryGotoNext(MonoMod.Cil.MoveType.After, IsFloatMax))
                {
                    c.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
                    c.Emit(Mono.Cecil.Cil.OpCodes.Call, doMk2);
                    PlateHooked++;
                    return false;
                }
                Once("plate", "Bubbets fix: Repulsion Plate Mk2's damage line isn't the expected shape (game updated?); its own hook runs unchanged");
            }
            catch (Exception e) { Once("plate", "Bubbets fix: the Repulsion Plate Mk2 hook failed: " + e.Message); }
            return true;
        }

        static void Once(string key, string message)
        {
            if (logged.Add(key)) CompatShieldPatcher.Log.LogWarning(message);
        }

        // ---- Everything below only matters in a run; patching here keeps game types out of the load path ----
        static void OnRunStart(object run)
        {
            if (done) return;
            done = true;
            Try("Void Scrap", () => ApplyVoidScrap(harmony, mod.GetType(ScrapName), game.GetType("RoR2.CostTypeCatalog")));
            Try("equipment swap", () => ApplyEquipSwap(harmony, mod.GetType(EquipmentName), game.GetType("RoR2.Inventory")));
            Try("Broken Clock", () => ApplySlotSet(harmony, mod.GetType(EquipmentName), game.GetType("RoR2.Inventory"), game.GetType("RoR2.EquipmentSlot"), game.GetType("RoR2.EquipmentLocation")));
            Try("Slug health bar", () => ApplyBars(harmony, mod.GetType(BarDataName), mod.GetType(SlugDataName)));
        }

        static void Try(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { CompatShieldPatcher.Log.LogError("Bubbets fix: the " + what + " repair failed: " + e); }
        }

        internal static bool ApplyVoidScrap(Harmony h, Type scrap, Type catalog)
        {
            var prefix = scrap?.GetMethod("PrefixItemPayCost", Any);
            var target = catalog?.GetMethods(Any).FirstOrDefault(m => m.IsStatic && m.Name.StartsWith("<Init>g__PayCostItems|") && m.GetParameters().Length == 2);
            if (prefix == null || target == null)
            {
                CompatShieldPatcher.Log.LogWarning("Bubbets fix: Void Scrap's prefix or the game's PayCostItems not found (mod or game updated?), not applied");
                return false;
            }
            if (Harmony.GetPatchInfo(target)?.Prefixes.Any(p => p.PatchMethod == prefix) == true) return true;
            h.Patch(target, new HarmonyMethod(prefix));
            CompatShieldPatcher.Log.LogInfo("Bubbets Items: Void Scrap is spent at printers again (its target was renamed in 1.5.0)");
            return true;
        }

        // ---- OnEquip / OnUnEquip: Broken Clock and Wildlife Camera add their components there ----
        static MethodInfo getEquipment, onEquip, onUnEquip;
        static FieldInfo stateIndex, stateDef;
        static PropertyInfo equipments;
        static MemberInfo equipmentDefMember;
        static Dictionary<object, object> byDef;

        static object Read(MemberInfo member, object from) => member is FieldInfo f ? f.GetValue(from) : ((PropertyInfo)member).GetValue(from, null);

        static MemberInfo Member(Type t, string name) => (MemberInfo)t?.GetField(name, Any) ?? t?.GetProperty(name, Any);

        internal static bool ApplyEquipSwap(Harmony h, Type equipmentBase, Type inventory)
        {
            var methods = inventory?.GetMethods(Any) ?? new MethodInfo[0];
            var target = methods.FirstOrDefault(m => m.Name == "SetEquipmentInternal" && m.GetParameters().Length == 2 && m.GetParameters().All(p => p.ParameterType.IsByRef));
            var state = target?.GetParameters()[0].ParameterType.GetElementType();
            var location = target?.GetParameters()[1].ParameterType;
            getEquipment = methods.FirstOrDefault(m => m.Name == "GetEquipment" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == location);
            stateIndex = state?.GetField("equipmentIndex", Any);
            stateDef = state?.GetField("equipmentDef", Any);
            equipments = equipmentBase?.GetProperty("Equipments", Any);
            equipmentDefMember = Member(equipmentBase, "EquipmentDef");
            onEquip = equipmentBase?.GetMethod("OnEquip", Any);
            onUnEquip = equipmentBase?.GetMethod("OnUnEquip", Any);
            if (target == null || getEquipment == null || getEquipment.ReturnType != state || stateIndex == null || stateDef == null
                || equipments == null || equipmentDefMember == null || onEquip == null || onUnEquip == null)
            {
                CompatShieldPatcher.Log.LogWarning("Bubbets fix: equipment members not found (mod or game updated?), Broken Clock and Wildlife Camera won't set up; not applied");
                return false;
            }
            byDef = null;
            h.Patch(target, new HarmonyMethod(typeof(BubbetsFix).GetMethod(nameof(EquipSwapPrefix), Any)));

            // The mod's own prefix sits on the old overload and can't compile; take it off in case another mod still calls that overload.
            var old = methods.FirstOrDefault(m => m.Name == "SetEquipmentInternal" && m.GetParameters().Length == 3);
            var dead = equipmentBase.GetMethod("OnEquipmentSwap", Any);
            if (old != null && dead != null && Harmony.GetPatchInfo(old)?.Prefixes.Any(p => p.PatchMethod == dead) == true) h.Unpatch(old, dead);
            CompatShieldPatcher.Log.LogInfo("Bubbets Items: equipment pickups reach Broken Clock and Wildlife Camera again");
            return true;
        }

        static object Lookup(object def)
        {
            if (def == null) return null;
            if (byDef == null)
            {
                var map = new Dictionary<object, object>();
                foreach (var e in (System.Collections.IEnumerable)equipments.GetValue(null, null))
                {
                    var d = Read(equipmentDefMember, e);
                    if (d != null) map[d] = e;
                }
                byDef = map;
            }
            return byDef.TryGetValue(def, out var found) ? found : null;
        }

        // __args: the game passes both by "in", and CompatShield can't name their types.
        static void EquipSwapPrefix(object __instance, object[] __args)
        {
            try
            {
                var next = __args[0];
                var current = getEquipment.Invoke(__instance, new[] { __args[1] });
                if (Convert.ToInt32(stateIndex.GetValue(current)) == Convert.ToInt32(stateIndex.GetValue(next))) return;
                var leaving = Lookup(stateDef.GetValue(current));
                var arriving = Lookup(stateDef.GetValue(next));
                if (leaving != null) Call(onUnEquip, leaving, __instance, next);
                if (arriving != null) Call(onEquip, arriving, __instance, current);
            }
            catch (Exception e) { Once("equip", "Bubbets fix: an equipment swap failed: " + e); }
        }

        static void Call(MethodInfo method, object target, object inventory, object state)
        {
            try { method.Invoke(target, new[] { inventory, state }); }
            catch (Exception e) { Once("equip-" + method.Name, "Bubbets fix: " + method.Name + " threw: " + (e.InnerException ?? e)); }
        }

        // ---- Broken Clock's rewind ends by selecting the slot it started from ----
        static MethodInfo trySetLocation;
        static FieldInfo slotInventory, locationSlot, locationSet;
        static Type locationType, equipSlotType;

        internal static bool ApplySlotSet(Harmony h, Type equipmentBase, Type inventory, Type equipmentSlot, Type location)
        {
            trySetLocation = inventory?.GetMethods(Any).FirstOrDefault(m => m.Name == "TrySetActiveEquipmentLocation" && m.GetParameters().Length == 1);
            slotInventory = equipmentSlot?.GetField("inventory", Any);
            locationSlot = location?.GetField("slot", Any);
            locationSet = location?.GetField("set", Any);
            locationType = location;
            equipSlotType = equipmentSlot;
            var callers = equipmentBase?.GetMethods(Any).Where(m => m.Name == "CmdSetEquipmentSlotAndSet" || m.Name == "CmdExecuteEquipmentSlotAndSet"
                || (m.Name == "SetEquipmentSlotAndSet" && m.GetParameters().Length == 1)).ToList();
            if (trySetLocation == null || slotInventory == null || locationSlot == null || locationSet == null || callers == null || callers.Count != 3)
            {
                CompatShieldPatcher.Log.LogWarning("Bubbets fix: equipment-slot members not found (mod or game updated?), Broken Clock's rewind will error when it ends; not applied");
                return false;
            }
            SlotCallsRedirected = 0;
            foreach (var caller in callers) h.Patch(caller, transpiler: M(nameof(SlotSetTranspiler)));
            if (SlotCallsRedirected == 3) CompatShieldPatcher.Log.LogInfo("Bubbets Items: Broken Clock's rewind finishes through game 1.5.0's equipment slots");
            else CompatShieldPatcher.Log.LogWarning($"Bubbets fix: redirected {SlotCallsRedirected} of 3 equipment-slot calls (maybe a newer mod); the others still error");
            return SlotCallsRedirected == 3;
        }

        static IEnumerable<CodeInstruction> SlotSetTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            for (int i = 0; i < code.Count; i++)
            {
                if (!IsCallNamed(code[i], "SetEquipmentSlotAndSet") || ((MethodBase)code[i].operand).GetParameters().Length != 3) continue;
                code[i].opcode = OpCodes.Call;
                code[i].operand = typeof(BubbetsFix).GetMethod(nameof(SetSlotAndSet), Any);
                code.Insert(i + 1, new CodeInstruction(OpCodes.Castclass, equipSlotType));
                SlotCallsRedirected++;
                i++;
            }
            return code;
        }

        /// <summary>Stands in for EquipmentBase.SetEquipmentSlotAndSet(EquipmentSlot, short, short), which reads fields game 1.5.0 removed.</summary>
        public static object SetSlotAndSet(object equipSlot, short slot, short set)
        {
            try
            {
                if (slotInventory.GetValue(equipSlot) is UnityEngine.Object inventory && inventory != null)
                {
                    var location = Activator.CreateInstance(locationType);
                    locationSlot.SetValue(location, (uint)Math.Max((int)slot, 0));
                    locationSet.SetValue(location, (uint)Math.Max((int)set, 0));
                    trySetLocation.Invoke(inventory, new[] { location });
                }
            }
            catch (Exception e) { Once("slot", "Bubbets fix: selecting an equipment slot failed: " + (e.InnerException ?? e)); }
            return equipSlot;
        }

        // ---- Slug's health bar: HealthBar.style is a property on 1.5.0 ----
        static Type slugType, barStyleType;
        static MethodInfo getStyle;
        static FieldInfo barField, barrierStyle, lowHealthStyle, sizeDelta;
        static PropertyInfo styleProperty;

        internal static bool ApplyBars(Harmony h, Type barData, Type slug)
        {
            var update = barData?.GetMethod("UpdateInfo", Any);
            getStyle = barData?.GetMethod("GetStyle", Any);
            barField = barData?.GetField("Bar", Any);
            styleProperty = barField?.FieldType.GetProperty("style", Any);
            var style = styleProperty?.PropertyType;
            barrierStyle = style?.GetField("barrierBarStyle", Any);
            lowHealthStyle = style?.GetField("lowHealthOverStyle", Any);
            barStyleType = getStyle?.ReturnType;
            sizeDelta = barStyleType?.GetField("sizeDelta", Any);
            slugType = slug;
            if (update == null || getStyle == null || slug == null || barrierStyle == null || lowHealthStyle == null || sizeDelta == null)
            {
                CompatShieldPatcher.Log.LogWarning("Bubbets fix: health-bar members not found (mod or game updated?), Slug's bar will error; not applied");
                return false;
            }
            StyleCallsRedirected = 0;
            h.Patch(update, transpiler: M(nameof(StyleTranspiler)));
            if (StyleCallsRedirected == 1) CompatShieldPatcher.Log.LogInfo("Bubbets Items: the Slug health bar reads game 1.5.0's HealthBar.style");
            else CompatShieldPatcher.Log.LogWarning($"Bubbets fix: redirected {StyleCallsRedirected} of 1 health-bar style calls (maybe a newer mod)");
            return StyleCallsRedirected == 1;
        }

        static IEnumerable<CodeInstruction> StyleTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            for (int i = 0; i < code.Count; i++)
            {
                if (!IsCallNamed(code[i], "GetStyle") || ((MethodBase)code[i].operand).DeclaringType != getStyle.DeclaringType) continue;
                code[i].opcode = OpCodes.Call;
                code[i].operand = typeof(BubbetsFix).GetMethod(nameof(StyleRouter), Any);
                code.Insert(i + 1, new CodeInstruction(OpCodes.Unbox_Any, barStyleType));
                StyleCallsRedirected++;
                i++;
            }
            return code;
        }

        public static object StyleRouter(object barData)
        {
            try { return barData.GetType() == slugType ? SlugStyle(barData) : getStyle.Invoke(barData, null); }
            catch (Exception e)
            {
                Once("style", "Bubbets fix: reading a health-bar style failed: " + (e.InnerException ?? e));
                return Activator.CreateInstance(barStyleType);
            }
        }

        // SlugData.GetStyle, written against the property: the barrier bar's style at the low-health overlay's thickness.
        static object SlugStyle(object data)
        {
            var bar = barField.GetValue(data) as UnityEngine.Object;
            if (bar == null) return Activator.CreateInstance(barStyleType);
            var style = styleProperty.GetValue(bar, null);
            var result = barrierStyle.GetValue(style);
            sizeDelta.SetValue(result, sizeDelta.GetValue(lowHealthStyle.GetValue(style)));
            return result;
        }
    }

    public static class ShamanModelFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal static MethodInfo Fixed;       // the re-emitted SetupCharacterModel; held here for the life of the game
        internal static MethodInfo Original;    // the mod's own two-argument SetupCharacterModel (the call the transpiler swaps)
        internal static int Redirected;         // call sites swapped, read by the test harness
        internal static FieldInfo bodyNameField; // BodyInfo.bodyName (the harness points it at a stand-in)
        internal static int Renamed;            // bodies renamed, read by the test harness
        /// <summary>Shaman body names the game itself now uses (game 1.5.0 DLC4 Acolyte). Prefixed "Shaman" at creation.</summary>
        internal static readonly HashSet<string> ClashingBodies = new HashSet<string> { "AcolyteBody" };

        public static void Apply(Harmony h, Assembly shaman)
        {
            var prefabs = shaman.GetType("ShamanMod.Modules.Prefabs");
            var methods = prefabs == null ? new MethodInfo[0] : prefabs.GetMethods(Any);
            ApplyRename(h, methods);      // first and on its own: without it a fully loading Shaman stalls the loading screen
            ApplyReEmit(h, shaman, prefabs, methods);
        }

        // The body-name clash with the game's own Acolyte: rename at creation, before the catalogs see the prefab.
        static void ApplyRename(Harmony h, MethodInfo[] methods)
        {
            try
            {
                // The BodyInfo type is taken from the helper's own third parameter (it lives in ShamanMod.Modules.Characters).
                var create = methods.FirstOrDefault(m => m.Name == "CreateBodyPrefab" && m.GetParameters().Length == 3
                    && m.GetParameters()[0].ParameterType == typeof(string) && m.GetParameters()[1].ParameterType == typeof(string)
                    && m.GetParameters()[2].ParameterType.Name == "BodyInfo");
                bodyNameField = create?.GetParameters()[2].ParameterType.GetField("bodyName", Any);
                if (create == null || bodyNameField == null || bodyNameField.FieldType != typeof(string))
                {
                    CompatShieldPatcher.Log.LogWarning("Shaman model fix: Prefabs.CreateBodyPrefab(string, string, BodyInfo) or BodyInfo.bodyName not found (mod updated?); " +
                        "its 'AcolyteBody' will clash with the game's own Acolyte and stall the loading screen");
                    return;
                }
                h.Patch(create, prefix: M(nameof(RenamePrefix)));
                CompatShieldPatcher.Log.LogInfo("Shaman: a summon body named like one of the game's own (AcolyteBody, new in 1.5.0) will be registered as Shaman<name> " +
                    "instead, so the body catalog no longer stalls the loading screen at 99%");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Shaman model fix: the body rename failed to apply: " + e);
            }
        }

        // The dead field store. On a profile with SeekersPatcher (a Starstorm2 dependency) the field is re-added at preload and
        // this part is a harmless no-op; it matters on a PC without it.
        static void ApplyReEmit(Harmony h, Assembly shaman, Type prefabs, MethodInfo[] methods)
        {
            try
            {
                Original = methods.FirstOrDefault(m => m.Name == "SetupCharacterModel" && m.GetParameters().Length == 2);
                var oneArg = methods.FirstOrDefault(m => m.Name == "SetupCharacterModel" && m.GetParameters().Length == 1);
                var init = shaman.GetType("ShamanMod.Modules.Characters.CharacterBase")?.GetMethod("InitializeCharacterModel", Any);
                if (Original == null || oneArg == null || init == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Shaman model fix: Prefabs.SetupCharacterModel or CharacterBase.InitializeCharacterModel not found (mod updated?), not applied");
                    return;
                }
                var setter = Type.GetType("RoR2.CharacterModel, RoR2")?.GetProperty("invisibilityCount", Any)?.GetSetMethod(true);
                if (setter == null || setter.GetParameters().Length != 1)
                {
                    CompatShieldPatcher.Log.LogWarning("Shaman model fix: game has no CharacterModel.invisibilityCount property setter (game updated?), not applied");
                    return;
                }
                string path = shaman.Location;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    path = Directory.GetFiles(Paths.PluginPath, "ShamanMod.dll", SearchOption.AllDirectories).FirstOrDefault();
                Fixed = FieldToPropertyReEmit.Build(Original, path, "RoR2.CharacterModel", "invisibilityCount", setter, out string why);
                if (Fixed == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Shaman model fix: " + why + ", not applied");
                    return;
                }
                Redirected = 0;
                h.Patch(init, transpiler: M(nameof(RedirectTranspiler)));
                h.Patch(oneArg, transpiler: M(nameof(RedirectTranspiler)));
                if (Redirected == 2)
                    CompatShieldPatcher.Log.LogInfo("Shaman: its character-model setup now sets invisibilityCount through game 1.5.0's property, so the Shaman and its Acolytes load again");
                else
                    CompatShieldPatcher.Log.LogWarning($"Shaman model fix: swapped {Redirected} of 2 SetupCharacterModel call sites (maybe a newer Shaman); the others still break");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Shaman model fix: the character-model re-emit failed to apply: " + e);
            }
        }

        static HarmonyMethod M(string name) => new HarmonyMethod(typeof(ShamanModelFix).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));

        // Prefix on Prefabs.CreateBodyPrefab(bodyName, modelName, bodyInfo): the prefab's final name is bodyInfo.bodyName, and the
        // doppelganger master is named from it later, so renaming the shared BodyInfo here covers both. Never throws.
        static void RenamePrefix(object bodyInfo)
        {
            try
            {
                if (bodyInfo == null || bodyNameField == null) return;
                var name = bodyNameField.GetValue(bodyInfo) as string;
                if (name == null || !ClashingBodies.Contains(name)) return;
                bodyNameField.SetValue(bodyInfo, "Shaman" + name);
                Renamed++;
                CompatShieldPatcher.Log.LogInfo($"Shaman: registered its '{name}' as 'Shaman{name}' (the game has its own {name})");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Shaman model fix: renaming a clashing body failed: " + e);
            }
        }

        // In the two callers: "call Prefabs.SetupCharacterModel(GameObject, CustomRendererInfo[])" calls the re-emitted copy
        // instead. Same signature, same stack effect; the instruction is changed in place to keep its labels.
        static IEnumerable<CodeInstruction> RedirectTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            if (Fixed == null || Original == null) return code;
            foreach (var ci in code)
            {
                if ((ci.opcode != OpCodes.Call && ci.opcode != OpCodes.Callvirt) || !(ci.operand is MethodInfo m)) continue;
                if (m.MetadataToken != Original.MetadataToken || m.Module != Original.Module) continue;
                ci.opcode = OpCodes.Call;
                ci.operand = Fixed;
                Redirected++;
            }
            return code;
        }
    }

    /// <summary>
    /// Re-emits a method whose IL stores a field that a newer game turned into a property: the body is read from the
    /// assembly file with Cecil (a reflection read fails on the dead field token), every reference is imported into a
    /// MonoMod DynamicMethodDefinition, the one "stfld Type::field" becomes "callvirt set_field", and Generate() gives a
    /// method with the original's signature that callers can be pointed at. Returns null with a reason when the shape
    /// isn't the expected one (no such store, or more than one).
    /// </summary>
    public static class FieldToPropertyReEmit
    {
        public static MethodInfo Build(MethodInfo original, string assemblyPath, string typeFullName, string fieldName, MethodInfo setter, out string why)
        {
            why = null;
            try
            {
                if (string.IsNullOrEmpty(assemblyPath) || !File.Exists(assemblyPath)) { why = "the mod's DLL file was not found"; return null; }
                using (var module = ModuleDefinition.ReadModule(assemblyPath))
                {
                    var type = module.GetType(original.DeclaringType.FullName.Replace('+', '/'));
                    var src = type?.Methods.FirstOrDefault(m => m.Name == original.Name && m.Parameters.Count == original.GetParameters().Length
                        && m.MetadataToken.ToInt32() == original.MetadataToken);
                    if (src == null || !src.HasBody) { why = $"{original.Name} not found in the DLL on disk"; return null; }
                    int stores = src.Body.Instructions.Count(i => IsStore(i, typeFullName, fieldName));
                    if (stores != 1) { why = $"{original.Name} stores {typeFullName}.{fieldName} {stores} times, not once (maybe a newer version)"; return null; }
                    var dmd = new MonoMod.Utils.DynamicMethodDefinition(original.DeclaringType.Name + "_" + original.Name + "_reemit", original.ReturnType,
                        original.GetParameters().Select(p => p.ParameterType).ToArray());
                    CopyBody(src, dmd, typeFullName, fieldName, setter);
                    return dmd.Generate();
                }
            }
            catch (Exception e)
            {
                why = "re-emitting " + original.Name + " failed: " + e.GetType().Name + ": " + e.Message;
                return null;
            }
        }

        static bool IsStore(Mono.Cecil.Cil.Instruction i, string typeFullName, string fieldName) =>
            i.OpCode == Mono.Cecil.Cil.OpCodes.Stfld && i.Operand is FieldReference f && f.Name == fieldName && f.DeclaringType.FullName == typeFullName;

        static void CopyBody(MethodDefinition src, MonoMod.Utils.DynamicMethodDefinition dmd, string typeFullName, string fieldName, MethodInfo setter)
        {
            var body = dmd.Definition.Body;
            var mod = dmd.Module;
            body.InitLocals = src.Body.InitLocals;
            body.MaxStackSize = src.Body.MaxStackSize;
            foreach (var v in src.Body.Variables) body.Variables.Add(new Mono.Cecil.Cil.VariableDefinition(mod.ImportReference(v.VariableType)));
            var map = new Dictionary<Mono.Cecil.Cil.Instruction, Mono.Cecil.Cil.Instruction>();
            var pending = new List<Mono.Cecil.Cil.Instruction>();
            foreach (var i in src.Body.Instructions)
            {
                var n = Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Nop);
                n.OpCode = i.OpCode;
                n.Offset = i.Offset;
                switch (i.Operand)
                {
                    case null: break;
                    case FieldReference f when IsStore(i, typeFullName, fieldName):
                        n.OpCode = Mono.Cecil.Cil.OpCodes.Callvirt; n.Operand = mod.ImportReference(setter); break;
                    case FieldReference f: n.Operand = mod.ImportReference(f); break;
                    case MethodReference m: n.Operand = mod.ImportReference(m); break;
                    case TypeReference t: n.Operand = mod.ImportReference(t); break;
                    case Mono.Cecil.Cil.VariableDefinition v: n.Operand = body.Variables[v.Index]; break;
                    case ParameterDefinition p: n.Operand = dmd.Definition.Parameters[p.Index]; break;
                    case Mono.Cecil.Cil.Instruction _: case Mono.Cecil.Cil.Instruction[] _: n.Operand = i.Operand; pending.Add(n); break;
                    case CallSite _: throw new NotSupportedException("indirect call sites are not supported");
                    default: n.Operand = i.Operand; break;   // strings and numbers
                }
                map[i] = n;
                body.Instructions.Add(n);
            }
            foreach (var n in pending)
            {
                if (n.Operand is Mono.Cecil.Cil.Instruction t) n.Operand = map[t];
                else n.Operand = ((Mono.Cecil.Cil.Instruction[])n.Operand).Select(t2 => map[t2]).ToArray();
            }
            foreach (var hnd in src.Body.ExceptionHandlers)
            {
                body.ExceptionHandlers.Add(new Mono.Cecil.Cil.ExceptionHandler(hnd.HandlerType)
                {
                    TryStart = Map(map, hnd.TryStart), TryEnd = Map(map, hnd.TryEnd), HandlerStart = Map(map, hnd.HandlerStart),
                    HandlerEnd = Map(map, hnd.HandlerEnd), FilterStart = Map(map, hnd.FilterStart),
                    CatchType = hnd.CatchType == null ? null : mod.ImportReference(hnd.CatchType),
                });
            }
        }

        static Mono.Cecil.Cil.Instruction Map(Dictionary<Mono.Cecil.Cil.Instruction, Mono.Cecil.Cil.Instruction> map, Mono.Cecil.Cil.Instruction i) => i == null ? null : map[i];
    }

    /// <summary>
    /// Diagnostic, not a fix: an audio loop that follows the player into the lobby (2026-10-18 23:05 after a Moon wipe, again
    /// 2026-10-19 00:49 after a death, gone at the next run start) can't be named from the game's log, because Wwise sound
    /// events are never logged. This records every AkSoundEngine.PostEvent that names a GameObject (by event name; numeric IDs
    /// are resolved through the game's SoundbanksInfo.xml), pairs each "Stop_x" with the "Play_x" loops on the same object,
    /// and when a run ends logs the loop-type sounds that were started on objects that no longer exist and never got their
    /// stop, plus the last sounds posted before the run ended. Also logs who calls AkSoundEngine.StopAll (something at run
    /// start kills the stray loop). A few log lines per run end; the entries are pruned there. [8. Diagnostics] SoundTracer=false
    /// turns the reports off.
    /// The fix built on the same bookkeeping, OrphanLoopGuard (2026-10-19 01:10, after "flamethrower hiss" was confirmed as
    /// the Solus Amalgamator Flamethrower Cannon's Play_GG_Boss_SolusAmalgamator_Flame_Loop, left behind when Multitudes
    /// spawned the cannon into a stage that had already ended): when an object is destroyed (HG.AkGameObjManager.UnregisterDelayed,
    /// the game's path from AkGameObj.OnDestroy), every loop-type sound it started that was never stopped is stopped by its
    /// playing ID with a short fade. Loops that got their Stop are already forgotten, one-shots are not loop-type, and a
    /// StopPlayingID on a finished sound is a no-op, so the guard can only end sounds whose emitter is gone.
    /// </summary>
    public static class SoundTracer
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal class Entry { public string name; public string objectName; public int instanceId; public object target; public float lastTime; public int count; public uint playingId; }
        static readonly object sync = new object();
        static readonly Dictionary<string, Entry> plays = new Dictionary<string, Entry>();           // key: instanceId ":" event name
        static readonly List<Entry> recent = new List<Entry>();                                      // ring of the last 40 posts
        static Dictionary<uint, string> idToName = new Dictionary<uint, string>();                  // Wwise ShortID -> event name
        internal static Func<object, bool> IsGone = o => !(o is UnityEngine.Object u) || u == null;  // Unity's destroyed check; the harness swaps in a stub
        internal static Func<float> Now = () => Time.unscaledTime;                                   // the harness swaps in a clock
        internal static Action<uint> StopPlaying;                                                    // AkSoundEngine.StopPlayingID(id, 250 ms); the harness swaps in a recorder
        internal static bool Report = true, Guard;
        internal static int Tracked => plays.Count;
        internal static int GuardStopped;                                                            // read by the test harness
        static readonly HashSet<string> guardLogged = new HashSet<string>();
        static float runEndTime = -1f, dumpAt = -1f;
        static int stopAllLogged;
        static bool runHooked;

        public static void Apply(Harmony h, bool report, bool guard)
        {
            Report = report;
            Guard = guard;
            try
            {
                var ak = Type.GetType("AkSoundEngine, AK.Wwise.Unity.API");
                if (ak == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Sound tracer: AkSoundEngine not found, not applied");
                    return;
                }
                int patched = 0;
                foreach (var m in ak.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    var p = m.GetParameters();
                    if (m.Name != "PostEvent" || p.Length < 2 || p[1].ParameterType != typeof(GameObject)) continue;
                    if (p[0].ParameterType != typeof(string) && p[0].ParameterType != typeof(uint)) continue;
                    h.Patch(m, postfix: M(nameof(PostEventPostfix)));
                    patched++;
                }
                foreach (var m in ak.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    if (m.Name == "StopAll" && m.GetParameters().Length == 0) h.Patch(m, postfix: M(nameof(StopAllPostfix)));
                var xml = SoundbanksXmlPath();   // from BepInEx's paths, not Application.streamingAssetsPath: no engine calls on the load path
                if (xml != null) System.Threading.ThreadPool.QueueUserWorkItem(_ => { var t = BuildNameTable(xml); if (t != null) idToName = t; });
                HookRun();
                // No Unity objects here. This runs from the AssemblyLoad event while MonoManager is still loading the game's
                // assemblies ("Begin MonoManager ReloadAssembly"), and a GameObject + AddComponent at that point crashes Mono
                // (mono_class_get_field_from_name, access violation): 1.4.8-1.4.10 died at the "modded" click, 2026-10-19.
                // The delayed run-end report is driven from PostEventPostfix instead (see Tick).
                if (report)
                    CompatShieldPatcher.Log.LogInfo($"Sound tracer: watching {patched} PostEvent overloads; at each run end it names loop sounds left on objects that are gone");
                if (guard) ApplyGuard(h, ak);
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Sound tracer failed to apply: " + e);
            }
        }

        static void ApplyGuard(Harmony h, Type ak)
        {
            try
            {
                var stop = ak.GetMethod("StopPlayingID", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(uint), typeof(int) }, null);
                var unregister = Type.GetType("HG.AkGameObjManager, HGUnityUtils")?.GetMethod("UnregisterDelayed", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(GameObject) }, null);
                if (stop == null || unregister == null)
                {
                    Guard = false;
                    CompatShieldPatcher.Log.LogWarning("Orphan loop guard: AkSoundEngine.StopPlayingID(uint, int) or HG.AkGameObjManager.UnregisterDelayed(GameObject) not found (game updated?), not applied");
                    return;
                }
                StopPlaying = id => stop.Invoke(null, new object[] { id, 250 });
                h.Patch(unregister, prefix: M(nameof(UnregisterPrefix)));
                CompatShieldPatcher.Log.LogInfo("Orphan loop guard: a looping sound whose object is destroyed before it was stopped (a flamethrower mid-attack when the " +
                    "run ends) now stops with its object instead of playing on into the lobby");
            }
            catch (Exception e)
            {
                Guard = false;
                CompatShieldPatcher.Log.LogError("Orphan loop guard failed to apply: " + e);
            }
        }

        // Prefix on HG.AkGameObjManager.UnregisterDelayed (AkGameObj.OnDestroy): the object is going away. Never throws.
        static void UnregisterPrefix(GameObject gameObject)
        {
            try
            {
                if (!Guard || gameObject == null) return;
                OnObjectGone(gameObject.GetInstanceID(), gameObject.name);
            }
            catch { }
        }

        /// <summary>Stops the loop-type sounds the object started and never stopped. Returns how many it stopped.</summary>
        internal static int OnObjectGone(int instanceId, string objectName)
        {
            int stopped = 0;
            var toLog = new List<string>();
            lock (sync)
            {
                var dead = new List<string>();
                foreach (var kv in plays)
                {
                    var p = kv.Value;
                    if (p.instanceId != instanceId || !IsLoopName(p.name)) continue;
                    dead.Add(kv.Key);
                    if (p.playingId == 0 || StopPlaying == null) continue;
                    try { StopPlaying(p.playingId); stopped++; } catch { }
                    if (guardLogged.Count < 8 && guardLogged.Add(p.name)) toLog.Add(p.name);
                }
                foreach (var k in dead) plays.Remove(k);
                GuardStopped += stopped;
            }
            foreach (var n in toLog)
                CompatShieldPatcher.Log.LogInfo($"Orphan loop guard: stopped {n}, which '{objectName}' left playing as it was destroyed (shown once per sound)");
            return stopped;
        }

        static HarmonyMethod M(string name) => new HarmonyMethod(typeof(SoundTracer).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));

        /// <summary>&lt;game&gt;\Risk of Rain 2_Data\StreamingAssets\Audio\GeneratedSoundBanks\Windows\SoundbanksInfo.xml, from BepInEx's Managed path. Null if unknown.</summary>
        internal static string SoundbanksXmlPath()
        {
            try
            {
                var data = Path.GetDirectoryName(Paths.ManagedPath);
                return data == null ? null : Path.Combine(data, "StreamingAssets", "Audio", "GeneratedSoundBanks", "Windows", "SoundbanksInfo.xml");
            }
            catch { return null; }
        }

        /// <summary>Event names by Wwise ShortID from SoundbanksInfo.xml (3,318 events in game 1.5.0). Null if unreadable.</summary>
        internal static Dictionary<uint, string> BuildNameTable(string xmlPath)
        {
            try
            {
                if (!File.Exists(xmlPath)) return null;
                var table = new Dictionary<uint, string>();
                var rx = new System.Text.RegularExpressions.Regex("<Event Id=\"(\\d+)\" Name=\"([^\"]+)\"");
                foreach (System.Text.RegularExpressions.Match m in rx.Matches(File.ReadAllText(xmlPath)))
                    table[uint.Parse(m.Groups[1].Value)] = m.Groups[2].Value;
                return table;
            }
            catch { return null; }
        }

        static void HookRun()
        {
            if (runHooked) return;
            runHooked = true;
            try
            {
                var run = Type.GetType("RoR2.Run, RoR2");
                foreach (var pair in new[] { ("onRunDestroyGlobal", nameof(OnRunDestroy)), ("onRunStartGlobal", nameof(OnRunStart)) })
                {
                    var ev = run?.GetEvent(pair.Item1, BindingFlags.Public | BindingFlags.Static);
                    if (ev == null) throw new MissingMemberException("RoR2.Run." + pair.Item1 + " not found");
                    ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(SoundTracer).GetMethod(pair.Item2, BindingFlags.NonPublic | BindingFlags.Static)));
                }
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Sound tracer: can't watch run ends, so it logs nothing: " + e.Message);
            }
        }

        // Postfix on every AkSoundEngine.PostEvent(string|uint, GameObject, ...). Never throws. Also drives the delayed run-end
        // report: sounds keep posting in the lobby (UI, music), so the report lands moments after its 2.5 s settle without a
        // MonoBehaviour of our own (a patcher can't create Unity objects at load time; see Apply).
        static void PostEventPostfix(object[] __args, uint __result)
        {
            try
            {
                Tick(Now());
                if (__args == null || __args.Length < 2) return;
                string name = __args[0] as string;
                if (name == null && __args[0] is uint id) name = idToName.TryGetValue(id, out var n) ? n : "0x" + id.ToString("x8");
                var go = __args[1] as GameObject;
                Record(name, go, go != null ? go.name : "(no object)", go != null ? go.GetInstanceID() : 0, __result);
            }
            catch { }
        }

        /// <summary>The bookkeeping, separated from Unity so the harness can drive it.</summary>
        internal static void Record(string name, object target, string objectName, int instanceId, uint playingId)
        {
            if (string.IsNullOrEmpty(name)) return;
            float now = Now();
            lock (sync)
            {
                var e = new Entry { name = name, objectName = objectName, instanceId = instanceId, target = target, lastTime = now, count = 1, playingId = playingId };
                recent.Add(e);
                if (recent.Count > 40) recent.RemoveAt(0);
                if (name.StartsWith("Play_", StringComparison.OrdinalIgnoreCase))
                {
                    string key = instanceId + ":" + name;
                    if (plays.TryGetValue(key, out var old)) { old.lastTime = now; old.count++; old.target = target; if (playingId != 0) old.playingId = playingId; }
                    else plays[key] = e;
                    if (plays.Count > 4000) Prune(true);
                }
                else if (name.StartsWith("Stop_", StringComparison.OrdinalIgnoreCase))
                {
                    // "Stop_x" ends "Play_x" on this object; a stop whose name differs in its last word (Flame_Stop vs Flame_Loop)
                    // ends the loops sharing the words before it.
                    string suffix = name.Substring(5);
                    int cut = suffix.LastIndexOf('_');
                    string family = cut > 0 ? suffix.Substring(0, cut) : suffix;
                    var dead = new List<string>();
                    foreach (var kv in plays)
                    {
                        var p = kv.Value;
                        if (p.instanceId != instanceId) continue;
                        string ps = p.name.Substring(5);
                        if (string.Equals(ps, suffix, StringComparison.OrdinalIgnoreCase)
                            || (IsLoopName(p.name) && ps.StartsWith(family + "_", StringComparison.OrdinalIgnoreCase)))
                            dead.Add(kv.Key);
                    }
                    foreach (var k in dead) plays.Remove(k);
                }
            }
        }

        internal static bool IsLoopName(string name) =>
            name.IndexOf("loop", StringComparison.OrdinalIgnoreCase) >= 0 || name.EndsWith("_LP", StringComparison.OrdinalIgnoreCase)
            || name.IndexOf("_LP_", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Idle", StringComparison.Ordinal) >= 0;

        static void OnRunDestroy(object run) { runEndTime = Now(); dumpAt = runEndTime + 2.5f; }

        static void OnRunStart(object run)
        {
            try
            {
                int alive;
                lock (sync) { Prune(false); alive = plays.Count; }
                if (Report) CompatShieldPatcher.Log.LogInfo($"Sound tracer: run start, {alive} started sound(s) from before this run still on live objects");
            }
            catch { }
        }

        internal static void Tick(float now)
        {
            if (dumpAt < 0f || now < dumpAt) return;
            dumpAt = -1f;
            try
            {
                if (Report) Dump();
                else lock (sync) Prune(true);
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogError("Sound tracer: dump failed: " + e); }
        }

        /// <summary>The run-end report. Returns the lines it logged (the harness reads them).</summary>
        internal static List<string> Dump()
        {
            var lines = new List<string>();
            lock (sync)
            {
                var gone = new List<Entry>();
                foreach (var p in plays.Values)
                    if (IsLoopName(p.name) && IsGone(p.target)) gone.Add(p);
                gone.Sort((a, b) => b.lastTime.CompareTo(a.lastTime));
                lines.Add($"Sound tracer: run ended; {gone.Count} loop-type sound(s) were started on objects that are now gone and never got a Stop on that object" +
                    (gone.Count > 0 ? ":" : "."));
                foreach (var p in gone.Take(25))
                    lines.Add($"  {p.name} on '{p.objectName}' (last started {runEndTime - p.lastTime:0.0} s before the run ended, {p.count}x)");
                var last = recent.Where(r => r.lastTime >= runEndTime - 3f).ToList();
                if (last.Count > 0)
                    lines.Add("  last sounds around the run end: " + string.Join(", ", last.Select(r => $"{r.name}@'{r.objectName}'({r.lastTime - runEndTime:+0.0;-0.0}s)")));
                Prune(true);
            }
            foreach (var l in lines) CompatShieldPatcher.Log.LogInfo(l);
            return lines;
        }

        // Drops entries whose object is gone (and, when asked, everything but the newest 500).
        static void Prune(bool hard)
        {
            var dead = plays.Where(kv => IsGone(kv.Value.target)).Select(kv => kv.Key).ToList();
            foreach (var k in dead) plays.Remove(k);
            if (hard && plays.Count > 500)
                foreach (var k in plays.OrderBy(kv => kv.Value.lastTime).Take(plays.Count - 500).Select(kv => kv.Key).ToList()) plays.Remove(k);
        }

        static void StopAllPostfix()
        {
            try
            {
                if (stopAllLogged++ >= 3) return;
                CompatShieldPatcher.Log.LogInfo("Sound tracer: AkSoundEngine.StopAll() was called (shown 3x), from: " +
                    string.Join(" <- ", new System.Diagnostics.StackTrace(2, false).GetFrames().Take(4).Select(f => f.GetMethod()?.DeclaringType?.Name + "." + f.GetMethod()?.Name)));
            }
            catch { }
        }

        internal static void ResetForTest() { lock (sync) { plays.Clear(); recent.Clear(); guardLogged.Clear(); GuardStopped = 0; runEndTime = -1f; dumpAt = -1f; } }
        internal static void SetNames(Dictionary<uint, string> table) { idToName = table ?? new Dictionary<uint, string>(); }
    }

    /// <summary>
    /// [8. Diagnostics] LagTracer: where the frame time goes. While a run is on, every 10 s window logs the average fps, the worst
    /// frame, how many frames went over 100 ms, how many monsters / bodies / projectiles were alive, how much time went into the
    /// paths item mods pile their hooks onto (CharacterBody.RecalculateStats, HealthComponent.TakeDamageProcess,
    /// GlobalEventManager.OnHitEnemy / OnCharacterDeath, CharacterBody.OnInventoryChanged, BulletAttack.Fire,
    /// ProjectileManager.FireProjectile, HealthComponent.Heal; inclusive, so every mod's hook on them counts), and how many garbage
    /// collections ran; plus Unity's CPU vs GPU frame time when the player exposes it, the physics steps per frame, and the three
    /// mods whose per-frame subscribers (RoR2Application.onUpdate / onFixedUpdate / onLateUpdate) cost the most that window, and the
    /// frame split into Update->end-of-frame (scripts, animation, render submission) and end-of-frame->Update (present/GPU wait + physics),
    /// and (sampled) who keeps dirtying stats, i.e. what drives the RecalculateStats churn; and, when 20+ projectiles are live, which
    /// projectile prefabs make up the crowd.
    /// Info when the window was laggy (under 45 fps on average, or any frame over 100 ms), Debug otherwise, so the Mod Watcher can
    /// draw the whole run. A frame over 250 ms is logged as it happens. The first 3 s after a scene change are not counted (that
    /// is the stage load; 6 s). A run-end summary names the worst moment and the busiest hot path.
    /// Since 1.4.54 each window also ends with free RAM, the game's size and its page faults (how many fell in the worst frame, how
    /// many were read from disk), a big-hitch line carries that frame's page faults, and the summary has a memory line: see MemReader.
    /// Nothing here touches Unity on the load path: the per-frame ticker is created at the first run start (see 1.4.8-1.4.10).
    /// </summary>
    public static class LagTracer
    {
        internal const float WindowSeconds = 10f, LagFps = 45f, HitchMs = 100f, BigHitchMs = 250f, StageLoadGraceSeconds = 6f, BigHitchEverySeconds = 5f;
        internal sealed class Sink { public readonly string Name; public long Calls, Ticks, WinCalls, WinTicks; public Sink(string n) { Name = n; } }
        public struct Live { public int Monsters, Bodies, Projectiles; }
        public struct FrameSample { public bool Ok; public double CpuMs, GpuMs; }
        static readonly Dictionary<MethodBase, Sink> sinkByMethod = new Dictionary<MethodBase, Sink>();
        internal static readonly List<Sink> Sinks = new List<Sink>();
        internal static readonly List<Sink> SubSinks = new List<Sink>();   // mods' per-frame subscribers, timed one by one
        static readonly HashSet<MethodBase> subPatched = new HashSet<MethodBase>();
        internal const int MaxSubscribers = 80;
        // Swappable for the harness (which has no Unity): the clock, the scene, the live-object counts, the GC and the run clock.
        internal static Func<float> Now = () => Time.unscaledTime;
        internal static Func<string> Scene = () => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        internal static Func<Live> Counts = DefaultCounts;
        internal static Func<IEnumerable<string>> ProjectileNames = DefaultProjectileNames;   // live projectile prefab names, once per window
        internal static Func<int> Gen0 = () => GC.CollectionCount(0);
        internal static Func<long> Heap = () => GC.GetTotalMemory(false);
        internal static Func<string> RunClock = DefaultRunClock;   // "stage 4 t=12:34"
        internal static Func<FrameSample> FrameTimes = SampleFrameTiming;   // Unity's CPU/GPU frame times, when the player exposes them
        internal static Func<float> Realtime = () => Time.realtimeSinceStartup;   // for the frame phases below
        // Memory (1.4.54): is the PC short of RAM, and did the game itself stop to wait for memory? Free RAM, the game's size and
        // its page-fault count cost about 15 microseconds a read; the count that had to be read from disk comes from MemReader's
        // background thread, because that query takes 10 to 35 ms.
        public struct Mem
        {
            public bool Ok, DiskOk, HardOk;                       // each group of readings can be missing on its own
            public long FreeMb, TotalMb, GameMb, GamePrivateMb;   // the PC's RAM; the game's part that is in RAM; all the game's own memory
            public uint Faults, DiskPages, Hard;                  // running counts: the game's page faults, pages the whole PC read from disk, the game's faults read from disk
        }
        internal static Func<Mem> Memory = MemReader.Read;
        internal static Func<long> FaultCount = MemReader.Faults;   // the game's page faults so far, read every frame (-1: unavailable)
        static Mem memAtStart;
        static long lastFaults = -1, winMaxFaults = -1;
        static long lowFreeMb = -1, memTotalMb, runFaults, runDiskPages, runHard, worstHard = -1;
        static int memWindows, diskWindows, hardWindows;
        static string worstHardWhere;
        internal static bool Running;
        static bool runHooked;
        static float winStart = -1f, winSum, winMax;
        static int winFrames, winOver, gen0AtStart, winFixed, winTimed;
        static double winCpu, winGpu, runCpu, runGpu;
        // Frame phases, from the ticker's end-of-frame coroutine: Update -> end of frame is this frame's scripts, animation and
        // render submission; end of frame -> next Update is the present / GPU wait plus the physics steps. With Unity's own
        // CPU/GPU timing unavailable in this build, the second phase minus the timed FixedUpdate work is the GPU-side wait.
        static float lastUpdateAt = -1f, lastEofAt = -1f;
        static double winUpdToEof, winEofToUpd;
        static int winPhased;
        static int runTimed;
        static string lastScene;
        static float ignoreUntil = -1f, lastBigHitch = -1e9f, loadPendingUntil = -1f;
        // run totals
        static int windows, lagWindows, peakMonsters;
        static double fpsSum;
        static float worstMs;
        static string worstWhere;
        internal static int Windows => windows;
        internal static int LagWindows => lagWindows;

        static Harmony harmony;
        static readonly List<KeyValuePair<MethodInfo, string>> pending = new List<KeyValuePair<MethodInfo, string>>();
        static bool sinksPatched;

        public static void Apply(Harmony h)
        {
            harmony = h;
            try
            {
                var ror2 = Type.GetType("RoR2.Run, RoR2")?.Assembly;
                if (ror2 == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Lag tracer: RoR2 assembly not found, not applied");
                    return;
                }
                const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var damageInfo = ror2.GetType("RoR2.DamageInfo");
                Queue(ror2.GetType("RoR2.CharacterBody")?.GetMethod("RecalculateStats", any, null, Type.EmptyTypes, null), "RecalculateStats");
                Queue(damageInfo == null ? null : ror2.GetType("RoR2.HealthComponent")?.GetMethod("TakeDamageProcess", any, null, new[] { damageInfo }, null), "TakeDamage");
                Queue(damageInfo == null ? null : ror2.GetType("RoR2.GlobalEventManager")?.GetMethod("OnHitEnemy", any, null, new[] { damageInfo, typeof(GameObject) }, null), "OnHitEnemy");
                var damageReport = ror2.GetType("RoR2.DamageReport");
                Queue(damageReport == null ? null : ror2.GetType("RoR2.GlobalEventManager")?.GetMethod("OnCharacterDeath", any, null, new[] { damageReport }, null), "OnCharacterDeath");
                Queue(ror2.GetType("RoR2.CharacterBody")?.GetMethod("OnInventoryChanged", any, null, Type.EmptyTypes, null), "InventoryChanged");
                Queue(ror2.GetType("RoR2.BulletAttack")?.GetMethod("Fire", any, null, Type.EmptyTypes, null), "BulletFire");
                var fireInfo = ror2.GetType("RoR2.Projectile.FireProjectileInfo");
                Queue(fireInfo == null ? null : ror2.GetType("RoR2.Projectile.ProjectileManager")?.GetMethod("FireProjectile", any, null, new[] { fireInfo }, null), "FireProjectile");
                var procMask = ror2.GetType("RoR2.ProcChainMask");
                Queue(procMask == null ? null : ror2.GetType("RoR2.HealthComponent")?.GetMethod("Heal", any, null, new[] { typeof(float), procMask, typeof(bool) }, null), "Heal");
                // Per-frame paths: what every body, AI, state machine, the HUD and the mods' own per-frame subscribers cost.
                Queue(ror2.GetType("RoR2.CharacterBody")?.GetMethod("FixedUpdate", any, null, Type.EmptyTypes, null), "BodyFixed");
                Queue(ror2.GetType("RoR2.CharacterAI.BaseAI")?.GetMethod("FixedUpdate", any, null, Type.EmptyTypes, null), "AI");
                Queue(ror2.GetType("RoR2.EntityStateMachine")?.GetMethod("FixedUpdate", any, null, Type.EmptyTypes, null), "States");
                Queue(ror2.GetType("RoR2.CharacterModel")?.GetMethod("UpdateOverlays", any, null, Type.EmptyTypes, null), "Overlays");
                Queue(ror2.GetType("RoR2.UI.HUD")?.GetMethod("Update", any, null, Type.EmptyTypes, null), "HUD");
                Queue(ror2.GetType("RoR2.RoR2Application")?.GetMethod("Update", any, null, Type.EmptyTypes, null), "AppUpdate");
                Queue(ror2.GetType("RoR2.RoR2Application")?.GetMethod("FixedUpdate", any, null, Type.EmptyTypes, null), "AppFixed");
                HookRun();
                CompatShieldPatcher.Log.LogInfo($"Lag tracer: during a run, every {WindowSeconds:0} s logs fps, the worst frame, live monsters/bodies/projectiles, time in " +
                    $"{pending.Count} hot paths (timed from the first run start), GC work, free RAM and the game's page faults (Info when under {LagFps:0} fps or a frame passed {HitchMs:0} ms, Debug otherwise); a run-end summary names the worst moment");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Lag tracer failed to apply: " + e);
            }
        }

        static void Queue(MethodInfo m, string name)
        {
            if (m == null)
            {
                CompatShieldPatcher.Log.LogWarning($"Lag tracer: {name} not found (game updated?), not timed");
                return;
            }
            pending.Add(new KeyValuePair<MethodInfo, string>(m, name));
        }

        // Harmony-patching a game method JIT-compiles it, which runs its type's static constructor: BulletAttack's runs
        // LayerIndex's, which calls Physics.GetIgnoreLayerCollision, and inside MonoManager's assembly load that kills the game
        // (1.4.14/1.4.15 died at the "modded" click, 2026-10-19). So the timers go in at the first run start, engine up.
        static void PatchSinks()
        {
            if (sinksPatched) return;
            sinksPatched = true;
            int ok = 0;
            foreach (var p in pending)
            {
                try
                {
                    var sink = new Sink(p.Value);
                    harmony.Patch(p.Key, prefix: M(nameof(SinkPrefix)), postfix: M(nameof(SinkPostfix)));
                    sinkByMethod[p.Key] = sink;
                    Sinks.Add(sink);
                    ok++;
                }
                catch (Exception e)
                {
                    CompatShieldPatcher.Log.LogWarning($"Lag tracer: couldn't time {p.Value}: {e.Message}");
                }
            }
            CompatShieldPatcher.Log.LogInfo($"Lag tracer: timing {ok} hot paths from here on");
            var ror2 = Type.GetType("RoR2.Run, RoR2")?.Assembly;
            if (ror2 != null) PatchDirtySources(ror2);
        }

        // RoR2Application.onUpdate / onFixedUpdate / onLateUpdate carry every mod's per-frame work (AppUpdate / AppFixed above):
        // time each subscriber's method on its own so a laggy window names the mod. Re-scanned at each run start (mods
        // subscribe late); methods already timed are skipped. Runtime only, like PatchSinks.
        static void PatchSubscribers()
        {
            try
            {
                var app = Type.GetType("RoR2.RoR2Application, RoR2");
                if (app == null || harmony == null) return;
                int added = 0;
                foreach (var evName in new[] { "onUpdate", "onFixedUpdate", "onLateUpdate" })
                {
                    var field = app.GetField(evName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                    if (!(field?.GetValue(null) is Delegate del)) continue;
                    foreach (var d in del.GetInvocationList())
                    {
                        var m = d.Method;
                        if (m == null || m.DeclaringType == null || subPatched.Count >= MaxSubscribers || subPatched.Contains(m)) continue;
                        if (m.DeclaringType.Assembly == typeof(LagTracer).Assembly) continue;
                        subPatched.Add(m);
                        string name = SubName(m, evName);
                        try
                        {
                            var sink = new Sink(name);
                            harmony.Patch(m, prefix: M(nameof(SinkPrefix)), postfix: M(nameof(SinkPostfix)));
                            sinkByMethod[m] = sink;
                            SubSinks.Add(sink);
                            added++;
                        }
                        catch (Exception e)
                        {
                            CompatShieldPatcher.Log.LogDebug($"Lag tracer: can't time {name}: {e.Message}");
                        }
                    }
                }
                if (added > 0)
                    CompatShieldPatcher.Log.LogInfo($"Lag tracer: timing {SubSinks.Count} per-frame subscriber(s) of RoR2Application.onUpdate / onFixedUpdate / onLateUpdate, by mod");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Lag tracer: per-frame subscriber scan failed: " + e.Message);
            }
        }

        /// <summary>"Mod:Type.Method(Update)"; a lambda's compiler-generated holder is replaced by its outer type and method.</summary>
        internal static string SubName(MethodBase m, string evName)
        {
            var type = m.DeclaringType;
            string asm = type.Assembly.GetName().Name;
            string typeName = type.Name;
            if (typeName.StartsWith("<") && type.DeclaringType != null) typeName = type.DeclaringType.Name;
            string method = m.Name;
            int close = method.IndexOf('>');
            if (method.StartsWith("<") && close > 1) method = method.Substring(1, close - 1);
            string ev = evName.StartsWith("on") ? evName.Substring(2) : evName;
            return $"{asm}:{typeName}.{method}({ev})";
        }

        internal static Sink AddSubSinkForTest(string name) { var s = new Sink(name); SubSinks.Add(s); return s; }

        // --- Who dirties stats / who calls RecalculateStats. A dirtying is counted only when CharacterBody.statsDirty flips
        // false -> true across the call (the game's delayed-damage tick calls RemoveBuff on every body every tick, and
        // SetBuffCount bails before dirtying when the clamped count is unchanged; UpdateOutOfCombatAndDanger runs every frame
        // and dirties only on a transition). One flip in 8 takes a stack walk: the label is what was dirtied (a buff by name, a
        // timed buff, the inventory, the combat state, MarkAllStatsDirty, the team level) and the first frame outside the game
        // and outside any hook on a CharacterBody method (the mod), else the game method. RecalculateStats calls are tallied
        // the same way, by caller, to split direct calls from the dirty flag. ---
        internal const int DirtySampleEvery = 8;
        static int dirtyCounter, recalcCounter;
        static readonly Dictionary<string, int> winDirty = new Dictionary<string, int>(), runDirty = new Dictionary<string, int>();
        static FieldInfo statsDirtyField;
        static Func<int, string> buffNameOf;
        static readonly string[] gamePrefixes = { "RoR2", "UnityEngine", "Unity.", "com.unity", "mscorlib", "System", "netstandard", "MonoMod", "0Harmony",
            "BepInEx", "HGUnityUtils", "KinematicCharacterController", "Rewired", "Wwise", "Facepunch", "AK.", "Zio", "Mono." };

        internal static bool IsGameAssembly(string asm) => gamePrefixes.Any(p => asm.StartsWith(p, StringComparison.Ordinal));

        /// <summary>
        /// The first frame outside the game and its runtime ("Mod:Type.Method") that is not a hook on a CharacterBody method, else
        /// "game:Type.Method" for the first RoR2 frame that is not the dirtied method itself, else "game". A mod's hook on the dirtied
        /// method, or on any CharacterBody method, sits between the method and its real caller: in the chain, not the cause.
        /// </summary>
        internal static string DirtySource(IEnumerable<MethodBase> frames, string target = null)
        {
            MethodBase firstGame = null;
            string passedThrough = null;
            var walked = new List<string>();
            foreach (var m in frames)
            {
                if (m?.DeclaringType == null) continue;
                if (IsWrapperFrame(m)) { walked.Add(WrappedMethod(m.Name)); continue; }
                string asm = m.DeclaringType.Assembly.GetName().Name;
                if (asm == "CompatShield") continue;
                if (IsGameAssembly(asm))
                {
                    if (asm == "RoR2") walked.Add(m.DeclaringType.FullName + "::" + m.Name);
                    if (firstGame == null && asm == "RoR2" && !(target != null && m.Name == target && m.DeclaringType.Name == "CharacterBody")) firstGame = m;
                    continue;
                }
                if (IsChainHook(m, target)) continue;
                string through = PassThroughOf(m, walked);
                if (through != null)
                {
                    if (passedThrough == null) passedThrough = "game:" + through;
                    continue;
                }
                return ModName(m);
            }
            if (passedThrough != null) return passedThrough;
            return firstGame != null ? "game:" + firstGame.DeclaringType.Name + "." + firstGame.Name : "game";
        }

        // The "Type::Method" in a wrapper's name: "DMD<RoR2.BuffWard::BuffTeam>", "Trampoline<RoR2.BuffWard::BuffTeam>?123".
        internal static string WrappedMethod(string name)
        {
            int open = name.IndexOf('<'), close = name.LastIndexOf('>');
            return open >= 0 && close > open ? name.Substring(open + 1, close - open - 1) : name;
        }

        /// <summary>
        /// An On-hook that only called orig on the way to the change: its orig_ parameter names a method the walk already went through, so
        /// the change came from the game's own body of that method and the hook merely wraps it. Returns "Type.Method", or null when the hook's
        /// own code made the change. vanillaVoid's Crystal Lotus hook on BuffWard.BuffTeam got blamed for every ward in the game before this.
        /// </summary>
        internal static string PassThroughOf(MethodBase m, List<string> walked)
        {
            if (walked.Count == 0) return null;
            var ps = m.GetParameters();
            if (ps.Length == 0) return null;
            string orig = ps[0].ParameterType.FullName ?? "";
            int cut = orig.IndexOf("+orig_", StringComparison.Ordinal);
            if (cut < 3 || !orig.StartsWith("On.", StringComparison.Ordinal)) return null;
            string type = orig.Substring(3, cut - 3), method = orig.Substring(cut + 6), prefix = type + "::";
            foreach (var w in walked)
            {
                if (!w.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string name = w.Substring(prefix.Length);
                // MMHOOK adds the parameter types to an overload's delegate: orig_AddTimedBuff_BuffDef_float.
                if (method == name || method.StartsWith(name + "_", StringComparison.Ordinal))
                    return type.Substring(type.LastIndexOf('.') + 1) + "." + name;
            }
            return null;
        }

        /// <summary>MonoMod's generated wrappers: "DMD<T::M>", "Trampoline<T::M>?123", "Hook<T::M>?123" (they show a real declaring type in Mono's traces).</summary>
        internal static bool IsWrapperFrame(MethodBase m) =>
            m.Name.StartsWith("DMD<", StringComparison.Ordinal) || m.Name.StartsWith("Trampoline<", StringComparison.Ordinal) || m.Name.StartsWith("Hook<", StringComparison.Ordinal)
            || m.Name.IndexOf("::", StringComparison.Ordinal) >= 0;

        // A MonoMod On-hook has an orig_ delegate as its first parameter: "On.RoR2.CharacterBody+orig_SetBuffCount". Any hook on a
        // CharacterBody method is chain noise here; so is a method whose own name carries the target's ("CharacterBody_SetBuffCount").
        internal static bool IsChainHook(MethodBase m, string target)
        {
            if (target != null && m.Name.IndexOf(target, StringComparison.Ordinal) >= 0) return true;
            var ps = m.GetParameters();
            if (ps.Length == 0) return false;
            string orig = ps[0].ParameterType.FullName ?? "";
            return orig.StartsWith("On.RoR2.CharacterBody+orig_", StringComparison.Ordinal) || orig.StartsWith("On.RoR2.CharacterBody+hook_", StringComparison.Ordinal);
        }

        static string ModName(MethodBase m)
        {
            var type = m.DeclaringType;
            string typeName = type.Name;
            if (typeName.StartsWith("<") && type.DeclaringType != null) typeName = type.DeclaringType.Name;
            string method = m.Name;
            int close = method.IndexOf('>');
            if (method.StartsWith("<") && close > 1) method = method.Substring(1, close - 1);
            return $"{type.Assembly.GetName().Name}:{typeName}.{method}";
        }

        static void PatchDirtySources(Assembly ror2)
        {
            try
            {
                var body = ror2.GetType("RoR2.CharacterBody");
                var buffIndex = ror2.GetType("RoR2.BuffIndex");
                var buffDef = ror2.GetType("RoR2.BuffDef");
                const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                statsDirtyField = body?.GetField("statsDirty", any);
                if (statsDirtyField == null || statsDirtyField.FieldType != typeof(bool))
                {
                    CompatShieldPatcher.Log.LogWarning("Lag tracer: CharacterBody.statsDirty not found (game updated?), stats-dirty attribution off");
                    return;
                }
                var getDef = buffIndex == null ? null : ror2.GetType("RoR2.BuffCatalog")?.GetMethod("GetBuffDef", BindingFlags.Public | BindingFlags.Static, null, new[] { buffIndex }, null);
                if (getDef != null) buffNameOf = i => (getDef.Invoke(null, new[] { Enum.ToObject(buffIndex, i) }) as UnityEngine.Object)?.name;
                var targets = new List<MethodInfo>
                {
                    body.GetMethod("MarkAllStatsDirty", any, null, Type.EmptyTypes, null),
                    buffIndex == null ? null : body.GetMethod("SetBuffCount", any, null, new[] { buffIndex, typeof(int) }, null),
                    buffDef == null ? null : body.GetMethod("AddTimedBuffInternal", any, null, new[] { buffDef, typeof(float), typeof(float) }, null),
                    body.GetMethod("OnInventoryChanged", any, null, Type.EmptyTypes, null),
                    body.GetMethod("UpdateOutOfCombatAndDanger", any, null, Type.EmptyTypes, null),
                    body.GetMethod("OnTeamLevelChanged", any, null, Type.EmptyTypes, null),
                };
                int ok = 0;
                foreach (var m in targets)
                {
                    if (m == null) continue;
                    try { harmony.Patch(m, prefix: DirtyPatches.Method(nameof(DirtyPatches.DirtyPrefix)), postfix: DirtyPatches.Method(nameof(DirtyPatches.DirtyPostfix))); ok++; }
                    catch (Exception e) { CompatShieldPatcher.Log.LogDebug($"Lag tracer: can't watch {m.Name}: {e.Message}"); }
                }
                var recalc = body.GetMethod("RecalculateStats", any, null, Type.EmptyTypes, null);
                if (recalc != null)
                {
                    try { harmony.Patch(recalc, prefix: M(nameof(RecalcPrefix))); ok++; }
                    catch (Exception e) { CompatShieldPatcher.Log.LogDebug("Lag tracer: can't watch RecalculateStats callers: " + e.Message); }
                }
                CompatShieldPatcher.Log.LogInfo($"Lag tracer: watching {ok} ways stats get dirtied (only real statsDirty flips count, 1 in {DirtySampleEvery} sampled) to name who drives RecalculateStats");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Lag tracer: stats-dirty attribution failed to apply: " + e.Message);
            }
        }

        static bool StatsDirty(object body)
        {
            try { return (bool)statsDirtyField.GetValue(body); } catch { return true; }
        }

        // Their own class: Harmony keys a patch's __state by the patch's declaring type, and OnInventoryChanged already carries the
        // timing sink's long __state from LagTracer (bool vs long from one class = "IL Compile Error", 2026-10-19 22:54).
        internal static class DirtyPatches
        {
            internal static HarmonyMethod Method(string name) => new HarmonyMethod(typeof(DirtyPatches).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static));

            internal static void DirtyPrefix(object __instance, out bool __state) { __state = Running && StatsDirty(__instance); }

            internal static void DirtyPostfix(object __instance, MethodBase __originalMethod, object[] __args, bool __state)
            {
            if (!Running || __state) return;                 // not running, or already dirty before the call: not this call's doing
            try
            {
                if (!StatsDirty(__instance)) return;         // no flip
                if ((++dirtyCounter % DirtySampleEvery) != 0) return;
                string what = __originalMethod.Name;
                if (what == "SetBuffCount" && __args != null && __args.Length == 2) what = "buff " + (buffNameOf?.Invoke(Convert.ToInt32(__args[0])) ?? Convert.ToInt32(__args[0]).ToString());
                else if (what == "AddTimedBuffInternal") what = "timed buff " + ((__args != null && __args.Length > 0 ? __args[0] as UnityEngine.Object : null)?.name ?? "?");
                else if (what == "OnInventoryChanged") what = "inventory";
                else if (what == "UpdateOutOfCombatAndDanger") what = "combat state";
                else if (what == "OnTeamLevelChanged") what = "team level";
                var frames = new System.Diagnostics.StackTrace(1, false).GetFrames();
                string src = frames == null ? "game" : DirtySource(frames.Select(f => f.GetMethod()), __originalMethod.Name);
                Tally(what + " by " + src, DirtySampleEvery);   // one sample stands for DirtySampleEvery flips
            }
            catch { }
            }
        }

        // RecalculateStats itself: who calls it (a direct call from a mod, the sprint toggle, Start, or the dirty flag in FixedUpdate).
        static void RecalcPrefix(object __instance)
        {
            if (!Running || (++recalcCounter % DirtySampleEvery) != 0) return;
            try
            {
                var frames = new System.Diagnostics.StackTrace(1, false).GetFrames();
                string src = frames == null ? "game" : DirtySource(frames.Select(f => f.GetMethod()), "RecalculateStats");
                if (src.StartsWith("game:CharacterBody.FixedUpdate")) src = "the dirty flag (FixedUpdate)";
                Tally("RecalculateStats by " + src, DirtySampleEvery);
            }
            catch { }
        }

        /// <summary>The flip rule, on its own for the harness: a dirtying is this call's when the flag was clear before and set after.</summary>
        internal static bool IsFlip(bool before, bool after) => !before && after;

        internal static void Tally(string label, int n = 1)
        {
            lock (winDirty)
            {
                winDirty[label] = (winDirty.TryGetValue(label, out var a) ? a : 0) + n;
                runDirty[label] = (runDirty.TryGetValue(label, out var b) ? b : 0) + n;
            }
        }

        static string TopDirty(Dictionary<string, int> d, int take)
        {
            lock (winDirty)
                return string.Join(", ", d.OrderByDescending(kv => kv.Value).Take(take).Select(kv => $"{kv.Key} ~{kv.Value}x"));
        }

        internal static Sink AddSinkForTest(string name) { var s = new Sink(name); Sinks.Add(s); return s; }

        static HarmonyMethod M(string name) => new HarmonyMethod(typeof(LagTracer).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static));

        // Inclusive time: a nested call of the same method (RecalculateStats inside RecalculateStats) is counted in both.
        static void SinkPrefix(out long __state) { __state = System.Diagnostics.Stopwatch.GetTimestamp(); }

        static void SinkPostfix(long __state, MethodBase __originalMethod)
        {
            if (__originalMethod != null && sinkByMethod.TryGetValue(__originalMethod, out var s))
            {
                s.WinTicks += System.Diagnostics.Stopwatch.GetTimestamp() - __state;
                s.WinCalls++;
            }
        }

        static void HookRun()
        {
            if (runHooked) return;
            runHooked = true;
            try
            {
                var run = Type.GetType("RoR2.Run, RoR2");
                foreach (var pair in new[] { ("onRunStartGlobal", nameof(OnRunStart)), ("onRunDestroyGlobal", nameof(OnRunDestroy)) })
                {
                    var ev = run?.GetEvent(pair.Item1, BindingFlags.Public | BindingFlags.Static);
                    if (ev == null) throw new MissingMemberException("RoR2.Run." + pair.Item1 + " not found");
                    ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(LagTracer).GetMethod(pair.Item2, BindingFlags.NonPublic | BindingFlags.Static)));
                }
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Lag tracer: can't watch runs, so it logs nothing: " + e.Message);
            }
        }

        static GameObject ticker;

        // Run start, main thread, game fully up: the one place this class may create a Unity object.
        static void OnRunStart(object run)
        {
            try
            {
                MemReader.RunStarted();
                PatchSinks();
                PatchSubscribers();
                ResetRun();
                Running = true;
                EnsureTicker();
            }
            catch (Exception e)
            {
                Running = false;
                CompatShieldPatcher.Log.LogWarning("Lag tracer: couldn't start for this run: " + e.Message);
            }
        }

        static void EnsureTicker()
        {
            if (ticker != null) return;
            ticker = new GameObject("CompatShield.LagTracer") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(ticker);
            ticker.AddComponent<LagTracerTicker>();
        }

        static void OnRunDestroy(object run)
        {
            Running = false;
            MemReader.RunEnded();
            try
            {
                foreach (var line in Summary()) CompatShieldPatcher.Log.LogInfo(line);
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogError("Lag tracer: summary failed: " + e); }
        }

        internal static void ResetRun()
        {
            windows = lagWindows = peakMonsters = 0;
            fpsSum = 0; worstMs = 0f; worstWhere = null;
            runCpu = runGpu = 0; runTimed = 0;
            lastScene = null; ignoreUntil = -1f; lastBigHitch = -1e9f; loadPendingUntil = -1f;
            lastUpdateAt = lastEofAt = -1f;
            lastFaults = winMaxFaults = -1; memAtStart = default(Mem);
            lowFreeMb = -1; memTotalMb = runFaults = runDiskPages = runHard = 0; worstHard = -1; worstHardWhere = null;
            memWindows = diskWindows = hardWindows = 0;
            lock (winDirty) { winDirty.Clear(); runDirty.Clear(); }
            winStart = -1f;
            foreach (var s in Sinks) { s.Calls = s.Ticks = s.WinCalls = s.WinTicks = 0; }
            foreach (var s in SubSinks) { s.Calls = s.Ticks = s.WinCalls = s.WinTicks = 0; }
        }

        /// <summary>One frame of dtSeconds. Called from the ticker's Update; the harness drives it with a fake clock.</summary>
        internal static void Frame(float dtSeconds)
        {
            if (!Running) return;
            float now = Now();
            // The game's page faults during this one frame: read every frame (about 5 microseconds), so a long frame can be told
            // apart from its neighbours.
            long faultsNow = -1;
            try { faultsNow = FaultCount(); } catch { }
            long frameFaults = faultsNow >= 0 && lastFaults >= 0 ? (long)unchecked((uint)faultsNow - (uint)lastFaults) : -1;
            lastFaults = faultsNow;
            string scene = null;
            try { scene = Scene(); } catch { }
            if (scene != lastScene)
            {
                lastScene = scene;
                ignoreUntil = now + StageLoadGraceSeconds;   // the load itself is not the game's fault
                loadPendingUntil = now + 20f;                 // ...and the loading screen is its own scene, so the real load frame can land later
            }
            if (winStart < 0f)
            {
                OpenWindow(now);
                return;
            }
            float ms = dtSeconds * 1000f;
            if (ms > 1000f && now < loadPendingUntil)
            {
                loadPendingUntil = -1f;   // the one multi-second frame after a scene change is the stage load: not counted
                return;
            }
            float rt = Realtime();
            if (now >= ignoreUntil && lastEofAt >= 0f && lastUpdateAt >= 0f && rt >= lastEofAt) { winEofToUpd += (rt - lastEofAt) * 1000.0; }
            lastUpdateAt = rt;
            if (now >= ignoreUntil)
            {
                winFrames++;
                winSum += ms;
                if (ms > winMax) { winMax = ms; winMaxFaults = frameFaults; }
                if (ms > HitchMs) winOver++;
                var ft = FrameTimes();
                if (ft.Ok) { winCpu += ft.CpuMs; winGpu += ft.GpuMs; winTimed++; }
                if (ms > BigHitchMs && now - lastBigHitch >= BigHitchEverySeconds)
                {
                    lastBigHitch = now;
                    var mem = SafeMemory();
                    CompatShieldPatcher.Log.LogInfo($"Lag: {ms:0} ms frame in {scene} ({RunClock()}); {Fmt(Counts())}" +
                        (frameFaults >= 0 ? $"; game page faults in this frame {frameFaults}" : "") +
                        (mem.Ok ? $"; RAM {Gb(mem.FreeMb)} of {Gb(mem.TotalMb)} GB free" : ""));
                }
            }
            if (now - winStart >= WindowSeconds)
            {
                var line = CloseWindow(scene, out bool laggy);
                if (line != null)
                {
                    if (laggy) CompatShieldPatcher.Log.LogInfo(line);
                    else CompatShieldPatcher.Log.LogDebug(line);
                }
                OpenWindow(now);
            }
        }

        /// <summary>End of the frame whose Update was last recorded (the ticker's WaitForEndOfFrame coroutine).</summary>
        internal static void EndOfFrame()
        {
            if (!Running) return;
            float rt = Realtime();
            if (lastUpdateAt >= 0f && rt >= lastUpdateAt && winStart >= 0f && Now() >= ignoreUntil) { winUpdToEof += (rt - lastUpdateAt) * 1000.0; winPhased++; }
            lastEofAt = rt;
        }

        /// <summary>One physics step. More than one per frame means the game is running FixedUpdate several times to catch up.</summary>
        internal static void FixedStep()
        {
            if (Running && winStart >= 0f && Now() >= ignoreUntil) winFixed++;
        }

        static void OpenWindow(float now)
        {
            winStart = now;
            winSum = winMax = 0f;
            winFrames = winOver = winFixed = winTimed = 0;
            winCpu = winGpu = 0;
            winUpdToEof = winEofToUpd = 0; winPhased = 0;
            lock (winDirty) winDirty.Clear();
            winMaxFaults = -1;
            memAtStart = SafeMemory();
            gen0AtStart = Gen0();
            foreach (var s in Sinks) { s.WinCalls = 0; s.WinTicks = 0; }
            foreach (var s in SubSinks) { s.WinCalls = 0; s.WinTicks = 0; }
        }

        /// <summary>The window's log line (null when no frame was counted) and whether it was laggy. Folds the window into the run totals.</summary>
        internal static string CloseWindow(string scene, out bool laggy)
        {
            laggy = false;
            if (winFrames == 0 || winSum <= 0f) return null;
            float avgFps = winFrames * 1000f / winSum;
            bool paused = winFixed == 0 && winFrames >= 30;   // frames drew but physics never stepped: the pause menu (Time.timeScale 0)
            laggy = !paused && (avgFps < LagFps || winOver > 0);
            windows++;
            if (laggy) lagWindows++;
            fpsSum += avgFps;
            var live = Counts();
            if (live.Monsters > peakMonsters) peakMonsters = live.Monsters;
            string clock = RunClock();
            if (winMax > worstMs)
            {
                worstMs = winMax;
                worstWhere = $"{scene} ({clock}), {Fmt(live)}";
            }
            int gcs = Gen0() - gen0AtStart;
            var sinks = string.Join(", ", Sinks.Select(s => $"{s.Name} {s.WinCalls}x {TicksToMs(s.WinTicks):0} ms"));
            foreach (var s in Sinks) { s.Calls += s.WinCalls; s.Ticks += s.WinTicks; }
            var topSubs = SubSinks.Where(s => s.WinTicks > 0).OrderByDescending(s => s.WinTicks).Take(3).Select(s => $"{s.Name} {TicksToMs(s.WinTicks):0} ms").ToList();
            foreach (var s in SubSinks) { s.Calls += s.WinCalls; s.Ticks += s.WinTicks; }
            string subs = topSubs.Count > 0 ? " | per-frame top: " + string.Join(", ", topSubs) : "";
            string phases = winPhased > 0 ? $" | phases: update->eof {winUpdToEof / winPhased:0} ms, eof->update {winEofToUpd / winPhased:0} ms" : "";
            string dirty = winDirty.Count > 0 ? " | stats dirtied by: " + TopDirty(winDirty, 5) : "";
            string projTop = "";
            if (live.Projectiles >= 20)
            {
                try { var t = TopNames(ProjectileNames(), 4); if (t.Length > 0) projTop = " | projectiles top: " + t; } catch { }
            }
            string timing = winTimed > 0 ? $" | frame cpu {winCpu / winTimed:0} ms, gpu {winGpu / winTimed:0} ms" : " | frame cpu/gpu n/a";
            if (winTimed > 0) { runCpu += winCpu / winTimed; runGpu += winGpu / winTimed; runTimed++; }
            return $"Lag: {scene} ({clock}){(paused ? " (paused)" : "")} | {avgFps:0} fps avg, worst frame {winMax:0} ms, {winOver} frame(s) over {HitchMs:0} ms | {Fmt(live)}" +
                (sinks.Length > 0 ? $" | {sinks}" : "") + $" | GC {gcs} collection(s), heap {Heap() >> 20} MB" + timing +
                $" | fixed steps {winFixed} ({(float)winFixed / winFrames:0.0}/frame)" + phases + dirty + projTop + subs + MemorySegment(scene, clock);
        }

        static Mem SafeMemory()
        {
            try { return Memory(); } catch { return default(Mem); }
        }

        static string Gb(long mb) => (mb / 1024.0).ToString("0.0");
        static string OrNa(long v) => v < 0 ? "n/a" : v.ToString();

        /// <summary>
        /// The window's memory readings, last in the line (the Mod Watcher's parser reads the earlier segments in order), and
        /// their run totals. Empty when Windows gave no readings. The page faults cover the whole 10 s, stage load included.
        /// </summary>
        static string MemorySegment(string scene, string clock)
        {
            var end = SafeMemory();
            var problem = MemReader.TakeProblem();
            if (problem != null) CompatShieldPatcher.Log.LogInfo("Lag tracer: " + problem);
            if (!end.Ok) return "";
            long faults = memAtStart.Ok ? (long)unchecked(end.Faults - memAtStart.Faults) : -1;
            long hard = memAtStart.HardOk && end.HardOk ? (long)unchecked(end.Hard - memAtStart.Hard) : -1;
            long disk = memAtStart.DiskOk && end.DiskOk ? (long)unchecked(end.DiskPages - memAtStart.DiskPages) : -1;
            memWindows++;
            if (lowFreeMb < 0 || end.FreeMb < lowFreeMb) lowFreeMb = end.FreeMb;
            memTotalMb = end.TotalMb;
            if (faults >= 0) runFaults += faults;
            if (disk >= 0) { runDiskPages += disk; diskWindows++; }
            if (hard >= 0)
            {
                runHard += hard; hardWindows++;
                if (hard > worstHard) { worstHard = hard; worstHardWhere = $"{scene} ({clock})"; }
            }
            return $" | RAM: {Gb(end.FreeMb)} of {Gb(end.TotalMb)} GB free, game {Gb(end.GamePrivateMb)} GB with {Gb(end.GameMb)} GB of it in RAM" +
                $" | page faults: game {OrNa(faults)}" + (winMaxFaults >= 0 ? $" ({winMaxFaults} in the worst frame)" : "") +
                $", {OrNa(hard)} from disk; whole PC read {OrNa(disk)} pages from disk";
        }

        /// <summary>The run-end lines.</summary>
        internal static List<string> Summary()
        {
            var lines = new List<string>();
            if (windows == 0)
            {
                lines.Add("Lag summary: no full window measured this run");
                return lines;
            }
            var busiest = Sinks.OrderByDescending(s => s.Ticks).FirstOrDefault();
            lines.Add($"Lag summary: {windows} window(s) of {WindowSeconds:0} s, {lagWindows} laggy (under {LagFps:0} fps or a frame over {HitchMs:0} ms); " +
                $"{fpsSum / windows:0} fps average; worst frame {worstMs:0} ms at {worstWhere}; peak monsters {peakMonsters}" +
                (busiest != null && busiest.Calls > 0 ? $"; busiest hot path {busiest.Name}: {TicksToMs(busiest.Ticks) / 1000:0.0} s in {busiest.Calls} calls" : "") +
                (runTimed > 0 ? $"; frame cpu {runCpu / runTimed:0} ms / gpu {runGpu / runTimed:0} ms average" : ""));
            var byTime = Sinks.Where(s => s.Calls > 0).OrderByDescending(s => s.Ticks).Take(6).Select(s => $"{s.Name} {TicksToMs(s.Ticks) / 1000:0.0} s");
            if (byTime.Any()) lines.Add("Lag summary: time by path over the run: " + string.Join(", ", byTime) + $" (of {windows * WindowSeconds:0} s measured)");
            var bySub = SubSinks.Where(s => s.Ticks > 0).OrderByDescending(s => s.Ticks).Take(8).Select(s => $"{s.Name} {TicksToMs(s.Ticks) / 1000:0.0} s");
            if (bySub.Any()) lines.Add("Lag summary: mods' per-frame work over the run: " + string.Join(", ", bySub));
            if (runDirty.Count > 0) lines.Add("Lag summary: stats dirtied by (sampled, over the run): " + TopDirty(runDirty, 8));
            if (memWindows > 0)
                lines.Add($"Lag summary: memory: free RAM fell to {Gb(lowFreeMb)} of {Gb(memTotalMb)} GB; game page faults {runFaults} in the measured windows, " +
                    (hardWindows > 0 ? $"{runHard} of them read from disk (most in one window: {worstHard} at {worstHardWhere})" : "read from disk n/a") +
                    (diskWindows > 0 ? $"; whole PC read {runDiskPages} pages from disk" : ""));
            return lines;
        }

        internal static double TicksToMs(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        static string Fmt(Live l) => $"monsters {N(l.Monsters)}, bodies {N(l.Bodies)}, projectiles {N(l.Projectiles)}";
        static string N(int v) => v < 0 ? "?" : v.ToString();

        // RoR2 is reached by reflection (CompatShield doesn't reference RoR2.dll), resolved once, at the first window of a run.
        static bool countsReady;
        static Func<int> monsters, bodies, projectiles;

        static void BuildCounts()
        {
            countsReady = true;
            try
            {
                var ror2 = Type.GetType("RoR2.Run, RoR2").Assembly;
                var teamIndex = ror2.GetType("RoR2.TeamIndex");
                var getMembers = ror2.GetType("RoR2.TeamComponent").GetMethod("GetTeamMembers", BindingFlags.Public | BindingFlags.Static, null, new[] { teamIndex }, null);
                // Every enemy team: Monster, plus Lunar (Mithrix's and Twisted Scavengers' side) and Void (void seeds, infestors), which the
                // Monster count alone misses (a window read "monsters 0" while 800 hits landed, 2026-10-19).
                var enemyTeams = new[] { "Monster", "Lunar", "Void" }.Select(n => new[] { Enum.Parse(teamIndex, n) }).ToArray();
                monsters = () => enemyTeams.Sum(a => (getMembers.Invoke(null, a) as System.Collections.ICollection)?.Count ?? 0);
                var list = ror2.GetType("RoR2.CharacterBody").GetField("readOnlyInstancesList", BindingFlags.Public | BindingFlags.Static);
                bodies = () => (list.GetValue(null) as System.Collections.ICollection)?.Count ?? -1;
                var projectile = ror2.GetType("RoR2.Projectile.ProjectileController");
                var getList = ror2.GetType("RoR2.InstanceTracker").GetMethod("GetInstancesList", BindingFlags.Public | BindingFlags.Static).MakeGenericMethod(projectile);
                projectiles = () => (getList.Invoke(null, null) as System.Collections.ICollection)?.Count ?? -1;
                projectileList = () => getList.Invoke(null, null) as System.Collections.IEnumerable;
            }
            catch (Exception e)
            {
                monsters = bodies = projectiles = null;
                CompatShieldPatcher.Log.LogWarning("Lag tracer: live-object counts unavailable (game updated?): " + e.Message);
            }
        }

        static Func<System.Collections.IEnumerable> projectileList;

        // The live projectiles' prefab names ("(Clone)" stripped), read once at window close: the laggy windows carry ~100 live
        // projectiles against ~12 when smooth, and the name says which weapon or mod floods the scene.
        static IEnumerable<string> DefaultProjectileNames()
        {
            if (!countsReady) BuildCounts();
            var list = projectileList?.Invoke();
            if (list == null) yield break;
            foreach (var o in list)
            {
                string n = (o as UnityEngine.Object)?.name;
                if (n == null) continue;
                if (n.EndsWith("(Clone)")) n = n.Substring(0, n.Length - 7);
                yield return n;
            }
        }

        /// <summary>"name N, name N" for the top few names by count (pure; the harness feeds it).</summary>
        internal static string TopNames(IEnumerable<string> names, int take)
        {
            var counts = new Dictionary<string, int>();
            foreach (var n in names) counts[n] = (counts.TryGetValue(n, out var c) ? c : 0) + 1;
            return string.Join(", ", counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(take).Select(kv => $"{kv.Key} {kv.Value}"));
        }

        static Live DefaultCounts()
        {
            if (!countsReady) BuildCounts();
            var l = new Live { Monsters = -1, Bodies = -1, Projectiles = -1 };
            if (monsters == null) return l;
            try { l.Monsters = monsters(); l.Bodies = bodies(); l.Projectiles = projectiles(); } catch { }
            return l;
        }

        static readonly UnityEngine.FrameTiming[] frameTimingBuffer = new UnityEngine.FrameTiming[1];

        // Unity's own frame timing (CPU main-thread time vs GPU time) from FrameTimingManager; returns Ok=false when the
        // player was built without frame timing stats, in which case the line says n/a.
        static FrameSample SampleFrameTiming()
        {
            try
            {
                UnityEngine.FrameTimingManager.CaptureFrameTimings();
                if (UnityEngine.FrameTimingManager.GetLatestTimings(1, frameTimingBuffer) == 0) return default;
                return new FrameSample { Ok = true, CpuMs = frameTimingBuffer[0].cpuFrameTime, GpuMs = frameTimingBuffer[0].gpuFrameTime };
            }
            catch { FrameTimes = () => default; return default; }
        }

        static bool runReady;
        static PropertyInfo runInstance;
        static MethodInfo runStopwatch;
        static FieldInfo stageClearCount;

        static string DefaultRunClock()
        {
            try
            {
                if (!runReady)
                {
                    runReady = true;
                    var run = Type.GetType("RoR2.Run, RoR2");
                    runInstance = run?.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
                    runStopwatch = run?.GetMethod("GetRunStopwatch", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    stageClearCount = run?.GetField("stageClearCount", BindingFlags.Public | BindingFlags.Instance);
                }
                var inst = runInstance?.GetValue(null);
                if (inst == null || runStopwatch == null) return "no run";
                float t = (float)runStopwatch.Invoke(inst, null);
                int stage = stageClearCount != null ? (int)stageClearCount.GetValue(inst) + 1 : 0;
                return $"stage {stage} t={(int)(t / 60)}:{(int)(t % 60):00}";
            }
            catch { return "no run"; }
        }

        internal static void ResetForTest()
        {
            Sinks.Clear();
            SubSinks.Clear();
            sinkByMethod.Clear();
            ResetRun();
            Running = false;
        }
    }

    /// <summary>LagTracer's per-frame clock. Added at the first run start, never at load.</summary>
    public class LagTracerTicker : MonoBehaviour
    {
        void Update() { LagTracer.Frame(Time.unscaledDeltaTime); }
        void FixedUpdate() { LagTracer.FixedStep(); }
        void Start() { StartCoroutine(EndOfFrames()); }
        System.Collections.IEnumerator EndOfFrames()
        {
            var wait = new WaitForEndOfFrame();
            while (true) { yield return wait; LagTracer.EndOfFrame(); }
        }
    }

    /// <summary>
    /// LagTracer's memory readings (1.4.54). On 2026-11-10 the PC had 3 GB of 40 free while frames of 600 to 2366 ms hit with no
    /// monsters alive, and nothing in the log could say whether the game itself had been waiting for memory. Three sources:
    /// - free and total RAM, the game's size and its page-fault count (GlobalMemoryStatusEx, K32GetProcessMemoryInfo): about 15
    ///   microseconds, read on the main thread. That count mixes cheap faults (the page was still in RAM) with ones read from disk.
    /// - pages the whole PC read from disk (NtQuerySystemInformation class 2): about 5 microseconds, main thread.
    /// - the game's own faults that were read from disk (the hard-fault count in class 5, the list of every process): 10 to 35 ms
    ///   a call with 700 processes, so it runs on a background thread every 2 s during a run and the main thread only reads the
    ///   last value. A value older than 6 s is not used. "From disk" also counts game files read for the first time (a stage load).
    /// Windows only; any failure turns that source off for the session and is logged once from the main thread. Nothing here
    /// touches Unity, and the worker never logs.
    /// </summary>
    internal static class MemReader
    {
        [StructLayout(LayoutKind.Sequential)]
        struct ProcessCounters
        {
            public int Size; public uint PageFaultCount;
            public UIntPtr PeakWorkingSet, WorkingSet, PeakPagedPool, PagedPool, PeakNonPagedPool, NonPagedPool, PagefileUsage, PeakPagefileUsage, PrivateUsage;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct MemoryStatus
        {
            public int Length, Load;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool K32GetProcessMemoryInfo(IntPtr process, out ProcessCounters counters, int size);
        [DllImport("kernel32.dll")] static extern uint GetCurrentProcessId();
        [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int written);

        internal const int SampleMs = 2000, StaleSeconds = 6;
        const int PerformanceClass = 2, ProcessListClass = 5, PerformanceSize = 1024;
        // Offsets in Windows' own structures on 64-bit: SYSTEM_PERFORMANCE_INFORMATION.PageReadCount, and in each
        // SYSTEM_PROCESS_INFORMATION entry NextEntryOffset (0), HardFaultCount (16) and UniqueProcessId (80).
        const int PageReadCountAt = 80, HardFaultCountAt = 16, ProcessIdAt = 80, EntryHeadSize = 88;
        static readonly IntPtr Self = new IntPtr(-1);   // the "this process" handle
        static bool cheapBroken, diskBroken;
        static string cheapProblem;
        static IntPtr performanceBuffer;
        static System.Threading.Thread worker;
        static readonly System.Threading.AutoResetEvent wake = new System.Threading.AutoResetEvent(false);
        static volatile bool wanted;
        static volatile string hardProblem;
        static int hardValue;
        static long hardStamp;

        /// <summary>Free RAM, the game's size and counts. Cheap: safe on the main thread. Ok is false when Windows gave nothing.</summary>
        internal static LagTracer.Mem Read()
        {
            var m = default(LagTracer.Mem);
            if (cheapBroken) return m;
            try
            {
                var status = new MemoryStatus { Length = Marshal.SizeOf(typeof(MemoryStatus)) };
                if (!GlobalMemoryStatusEx(ref status)) throw new InvalidOperationException("GlobalMemoryStatusEx failed, Windows error " + Marshal.GetLastWin32Error());
                ProcessCounters counters;
                if (!K32GetProcessMemoryInfo(Self, out counters, Marshal.SizeOf(typeof(ProcessCounters)))) throw new InvalidOperationException("GetProcessMemoryInfo failed, Windows error " + Marshal.GetLastWin32Error());
                m.FreeMb = (long)(status.AvailPhys >> 20); m.TotalMb = (long)(status.TotalPhys >> 20);
                m.GameMb = (long)(counters.WorkingSet.ToUInt64() >> 20); m.GamePrivateMb = (long)(counters.PrivateUsage.ToUInt64() >> 20);
                m.Faults = counters.PageFaultCount;
                m.Ok = true;
            }
            catch (Exception e)
            {
                Broke(e);
                return default(LagTracer.Mem);
            }
            if (!diskBroken)
            {
                try
                {
                    if (performanceBuffer == IntPtr.Zero) performanceBuffer = Marshal.AllocHGlobal(PerformanceSize);
                    if (NtQuerySystemInformation(PerformanceClass, performanceBuffer, PerformanceSize, out int written) == 0 && written >= PageReadCountAt + 4)
                    {
                        m.DiskPages = (uint)Marshal.ReadInt32(performanceBuffer, PageReadCountAt);
                        m.DiskOk = true;
                    }
                    else diskBroken = true;
                }
                catch { diskBroken = true; }
            }
            long stamp = System.Threading.Volatile.Read(ref hardStamp);
            if (stamp != 0 && System.Diagnostics.Stopwatch.GetTimestamp() - stamp < System.Diagnostics.Stopwatch.Frequency * StaleSeconds)
            {
                m.Hard = (uint)System.Threading.Volatile.Read(ref hardValue);
                m.HardOk = true;
            }
            return m;
        }

        /// <summary>The game's page faults so far (cheap and from-disk together), or -1. Read every frame: about 5 microseconds.</summary>
        internal static long Faults()
        {
            if (cheapBroken) return -1;
            try
            {
                ProcessCounters counters;
                return K32GetProcessMemoryInfo(Self, out counters, Marshal.SizeOf(typeof(ProcessCounters))) ? counters.PageFaultCount : -1;
            }
            catch (Exception e)
            {
                Broke(e);
                return -1;
            }
        }

        static void Broke(Exception e)
        {
            cheapBroken = true;
            cheapProblem = "free RAM and page-fault readings are unavailable (" + e.GetType().Name + ": " + e.Message + "), so the lag lines leave them out";
        }

        /// <summary>Main thread: a problem to log, once each.</summary>
        internal static string TakeProblem()
        {
            string text = cheapProblem;
            cheapProblem = null;
            string hard = hardProblem;
            if (hard != null)
            {
                hardProblem = null;
                text = (text == null ? "" : text + "; ") + "the count of the game's page faults read from disk is unavailable (" + hard + "), so the lag lines show n/a for it";
            }
            return text;
        }

        /// <summary>Run start, main thread: start the background reader, or wake it for a fresh value.</summary>
        internal static void RunStarted()
        {
            wanted = true;
            try
            {
                if (worker == null)
                {
                    worker = new System.Threading.Thread(Loop) { IsBackground = true, Name = "CompatShield.MemReader", Priority = System.Threading.ThreadPriority.BelowNormal };
                    worker.Start();
                }
                else wake.Set();
            }
            catch (Exception e) { hardProblem = e.GetType().Name + ": " + e.Message; }
        }

        internal static void RunEnded() { wanted = false; }

        static void Loop()
        {
            IntPtr buffer = IntPtr.Zero;
            int size = 0, misses = 0;
            try
            {
                if (IntPtr.Size != 8) { hardProblem = "the game is not running as a 64-bit process"; return; }
                long pid = GetCurrentProcessId();
                while (true)
                {
                    if (wanted)
                    {
                        if (SampleHard(pid, ref buffer, ref size, out uint hard))
                        {
                            System.Threading.Volatile.Write(ref hardValue, (int)hard);
                            System.Threading.Volatile.Write(ref hardStamp, System.Diagnostics.Stopwatch.GetTimestamp());
                            misses = 0;
                        }
                        else if (++misses >= 5) { hardProblem = "Windows did not return the process list five times in a row"; return; }
                    }
                    wake.WaitOne(SampleMs);
                }
            }
            catch (Exception e) { hardProblem = e.GetType().Name + ": " + e.Message; }
            finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
        }

        /// <summary>One read of the process list for this process's from-disk fault count. Slow (10 to 35 ms): never on the main thread.</summary>
        static bool SampleHard(long pid, ref IntPtr buffer, ref int size, out uint hard)
        {
            hard = 0;
            if (IntPtr.Size != 8) return false;   // the offsets below are the 64-bit layout (the game is 64-bit; the test harness is not)
            int written = 0, status = -1;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (buffer == IntPtr.Zero)
                {
                    if (size < (1 << 20)) size = 1 << 20;
                    if (size > (64 << 20)) return false;
                    buffer = Marshal.AllocHGlobal(size);
                }
                status = NtQuerySystemInformation(ProcessListClass, buffer, size, out written);
                if (status == 0) break;
                Marshal.FreeHGlobal(buffer); buffer = IntPtr.Zero;
                size = Math.Max(written, size) + (256 << 10);   // the list grew: room for it and for a few more processes
            }
            if (status != 0) return false;
            int limit = written > 0 && written < size ? written : size;
            int at = 0;
            for (int guard = 0; guard < 100000; guard++)
            {
                if (at < 0 || at + EntryHeadSize > limit) return false;
                if (Marshal.ReadInt64(buffer, at + ProcessIdAt) == pid)
                {
                    hard = (uint)Marshal.ReadInt32(buffer, at + HardFaultCountAt);
                    return true;
                }
                int next = Marshal.ReadInt32(buffer, at);
                if (next <= 0) return false;
                at += next;
            }
            return false;
        }

        /// <summary>Harness: one from-disk count for this process, read on the calling thread (-1 when it can't be read).</summary>
        internal static long HardNowForTest()
        {
            IntPtr buffer = IntPtr.Zero;
            int size = 0;
            try { return SampleHard(GetCurrentProcessId(), ref buffer, ref size, out uint hard) ? hard : -1; }
            finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
        }
    }

    /// <summary>
    /// [8. Diagnostics] PhysicsPileupTracer: names a physics pile-up. The 8:57 PM hang on 2026-11-09 (Void Locus, stage 12) was PhysX
    /// registering newly overlapping collider pairs without end: Unity's symbols on the crash dump put the main thread in
    /// PhysicsManager::Simulate waiting on a worker in Sc::Scene::registerSceneInteractions, and memory rose 0.9 GB. Pairs grow with
    /// the square of the objects packed into one spot, and nothing in the log said which objects they were. After any frame over
    /// 0.5 s during a run (at most every 10 s; stage loads skipped), this logs the moving objects (colliders on a rigidbody) that hold
    /// the most colliders: how many objects, how many triggers, and the most of them inside one 4 m cube. Silent while frames stay
    /// under 0.5 s. Nothing here touches Unity on the load path: the ticker is created at the first run start. Only the host needs it.
    /// </summary>
    public static class PhysicsPileupTracer
    {
        internal const float TriggerMs = 500f, EverySeconds = 10f, SceneGraceSeconds = 6f, LoadFrameMs = 1000f, LoadPendingSeconds = 20f, CellMeters = 4f;
        internal const int TopGroups = 6;
        public struct Item { public string Name; public int Owner; public bool Trigger; public bool Moving; public long Cell; }
        // Swappable for the harness (no Unity there).
        internal static Func<float> Now = () => Time.unscaledTime;
        internal static Func<int> SceneId = () => UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        internal static Func<string> SceneName = () => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        internal static Func<string> RunClock = () => LagTracer.RunClock();
        internal static Func<List<Item>> Census = DefaultCensus;
        internal static Func<double> ClockMs = () => System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        internal static bool Running;
        internal static int Fired;   // censuses taken (read by the harness)
        static bool hooked, failedOnce;
        static int lastScene = int.MinValue;
        static float lastAt = -1e9f, graceUntil = -1f, loadPendingUntil = -1f;
        static GameObject ticker;

        public static void Apply()
        {
            if (hooked) return;
            hooked = true;
            try
            {
                var run = Type.GetType("RoR2.Run, RoR2");
                foreach (var pair in new[] { ("onRunStartGlobal", nameof(OnRunStart)), ("onRunDestroyGlobal", nameof(OnRunDestroy)) })
                {
                    var ev = run?.GetEvent(pair.Item1, BindingFlags.Public | BindingFlags.Static);
                    if (ev == null) throw new MissingMemberException("RoR2.Run." + pair.Item1 + " not found");
                    ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(PhysicsPileupTracer).GetMethod(pair.Item2, BindingFlags.NonPublic | BindingFlags.Static)));
                }
                CompatShieldPatcher.Log.LogInfo($"Physics pile-up tracer: during a run, after a frame over {TriggerMs:0} ms (at most every {EverySeconds:0} s), " +
                    $"logs which moving objects hold the most colliders and how many of them share one {CellMeters:0} m spot");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Physics pile-up tracer: can't watch runs, not applied: " + e.Message);
            }
        }

        // Run start, main thread, game fully up: the one place this class may create a Unity object.
        static void OnRunStart(object run)
        {
            try
            {
                ResetRun();
                Running = true;
                if (ticker == null)
                {
                    ticker = new GameObject("CompatShield.PhysicsPileupTracer") { hideFlags = HideFlags.HideAndDontSave };
                    UnityEngine.Object.DontDestroyOnLoad(ticker);
                    ticker.AddComponent<PhysicsPileupTicker>();
                }
            }
            catch (Exception e)
            {
                Running = false;
                CompatShieldPatcher.Log.LogWarning("Physics pile-up tracer: couldn't start for this run: " + e.Message);
            }
        }

        static void OnRunDestroy(object run) { Running = false; }

        /// <summary>One frame of dtSeconds. Called from the ticker's Update; the harness drives it with a fake clock.</summary>
        internal static void Frame(float dtSeconds)
        {
            if (!Running) return;
            try
            {
                float now = Now();
                int scene = 0;
                try { scene = SceneId(); } catch { }
                if (scene != lastScene)
                {
                    lastScene = scene;
                    graceUntil = now + SceneGraceSeconds;          // the load itself is not a pile-up
                    loadPendingUntil = now + LoadPendingSeconds;   // ...and the loading screen is its own scene, so the load frame can land later
                }
                float ms = dtSeconds * 1000f;
                if (ms > LoadFrameMs && now < loadPendingUntil)
                {
                    loadPendingUntil = -1f;   // the one multi-second frame after a scene change is the stage load
                    return;
                }
                if (now < graceUntil || ms < TriggerMs || now - lastAt < EverySeconds) return;
                lastAt = now;
                Fired++;
                double t0 = ClockMs();
                var items = Census();
                double took = ClockMs() - t0;
                string where;
                try { where = $"in {SceneName()} ({RunClock()})"; } catch { where = "(scene unknown)"; }
                CompatShieldPatcher.Log.LogInfo(Describe(ms, where, items, took));
            }
            catch (Exception e)
            {
                if (failedOnce) return;
                failedOnce = true;
                CompatShieldPatcher.Log.LogWarning("Physics pile-up tracer: couldn't take a census (logged once): " + e.Message);
            }
        }

        sealed class Group
        {
            public string Name;
            public int Colliders, Triggers;
            public readonly HashSet<int> Owners = new HashSet<int>();
            public readonly Dictionary<long, int> Cells = new Dictionary<long, int>();
        }

        /// <summary>The log line for one census (pure; the harness feeds it).</summary>
        internal static string Describe(float ms, string where, List<Item> items, double censusMs)
        {
            int moving = 0, still = 0;
            var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
            var allCells = new Dictionary<long, int>();
            foreach (var it in items ?? new List<Item>())
            {
                if (!it.Moving) { still++; continue; }
                moving++;
                string name = it.Name ?? "?";
                if (!groups.TryGetValue(name, out var g)) groups[name] = g = new Group { Name = name };
                g.Colliders++;
                if (it.Trigger) g.Triggers++;
                g.Owners.Add(it.Owner);
                g.Cells[it.Cell] = (g.Cells.TryGetValue(it.Cell, out var c) ? c : 0) + 1;
                allCells[it.Cell] = (allCells.TryGetValue(it.Cell, out var a) ? a : 0) + 1;
            }
            var top = groups.Values.OrderByDescending(g => g.Colliders).ThenBy(g => g.Name, StringComparer.Ordinal).Take(TopGroups)
                .Select(g => $"{g.Name}: {g.Colliders} colliders on {g.Owners.Count} objects" + (g.Triggers > 0 ? $", {g.Triggers} triggers" : "") + $", {g.Cells.Values.Max()} in one spot");
            int densest = allCells.Count == 0 ? 0 : allCells.Values.Max();
            return $"Physics pile-up: {ms:0} ms frame {where}; {moving} moving colliders of {groups.Count} kinds, {densest} in the most crowded {CellMeters:0} m spot, {still} static; most: " +
                   (moving == 0 ? "none" : string.Join("; ", top)) + $" (census {censusMs:0} ms)";
        }

        /// <summary>Which CellMeters cube a point is in, packed into one number (21 bits a side).</summary>
        internal static long CellOf(float x, float y, float z)
        {
            long cx = (long)Math.Floor(x / CellMeters) & 0x1FFFFF, cy = (long)Math.Floor(y / CellMeters) & 0x1FFFFF, cz = (long)Math.Floor(z / CellMeters) & 0x1FFFFF;
            return (cx << 42) | (cy << 21) | cz;
        }

        // Every enabled collider in the loaded scenes. One with a rigidbody is a moving object, named by its rigidbody's object
        // ("(Clone)" stripped): the ammo pack, the projectile, the body. Level geometry has none and only counts as static.
        static List<Item> DefaultCensus()
        {
            var colliders = UnityEngine.Object.FindObjectsOfType<Collider>();
            var list = new List<Item>(colliders.Length);
            var names = new Dictionary<int, string>();
            foreach (var c in colliders)
            {
                if (c == null || !c.enabled) continue;
                var rb = c.attachedRigidbody;
                if (rb == null) { list.Add(new Item { Moving = false }); continue; }
                int id = rb.GetInstanceID();
                if (!names.TryGetValue(id, out var n))
                {
                    n = rb.gameObject.name ?? "?";
                    if (n.EndsWith("(Clone)")) n = n.Substring(0, n.Length - 7);
                    names[id] = n;
                }
                var p = rb.position;
                list.Add(new Item { Name = n, Owner = id, Trigger = c.isTrigger, Moving = true, Cell = CellOf(p.x, p.y, p.z) });
            }
            return list;
        }

        internal static void ResetRun() { lastScene = int.MinValue; lastAt = -1e9f; graceUntil = -1f; loadPendingUntil = -1f; }
        internal static void ResetForTest() { ResetRun(); Running = false; Fired = 0; failedOnce = false; }
    }

    /// <summary>PhysicsPileupTracer's per-frame clock. Added at the first run start, never at load.</summary>
    public class PhysicsPileupTicker : MonoBehaviour
    {
        void Update() { PhysicsPileupTracer.Frame(Time.unscaledDeltaTime); }
    }

    /// <summary>
    /// [3. Balance] LockedItems: items and equipment named in the setting are locked off in every lobby, the way an item disabled
    /// in TinkersSatchel's config is. The game already has the switch: PreGameController.ResolveChoiceMask leaves out any rule
    /// choice with excludeByDefault set, and EnforceValidRuleChoices then moves the rule to its other choice (Off). So the lock
    /// sets that flag on the "On" choice of the rule "Items.NAME" or "Equipment.NAME" and puts [CONFIG DISABLED] in front of
    /// both choices' titles. It goes by name, never by rule position, so adding or removing mods cannot move it to other items
    /// (a saved lobby vote list goes by position, which is how bans drifted). The mask is the host's and is sent to clients.
    /// Nothing is touched at load: the rule list does not exist yet, so a tick handler waits for RuleCatalog.availability.
    /// </summary>
    public static class LockedItems
    {
        public const string Prefix = "<color=#FF0000>[CONFIG DISABLED] </color>";
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        internal static HashSet<string> Wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal static Func<bool> Ready;
        internal static Func<System.Collections.IEnumerable> Rules;
        internal static Action Unsubscribe;
        internal static bool Done;
        internal static int Ticks;
        static readonly Dictionary<object, Delegate> Originals = new Dictionary<object, Delegate>();

        /// <summary>Comma-separated names, trimmed; an "Items." or "Equipment." prefix is accepted and dropped.</summary>
        internal static HashSet<string> Parse(string list)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in (list ?? "").Split(','))
            {
                string name = raw.Trim();
                if (name.StartsWith("Items.", StringComparison.OrdinalIgnoreCase)) name = name.Substring(6);
                else if (name.StartsWith("Equipment.", StringComparison.OrdinalIgnoreCase)) name = name.Substring(10);
                if (name.Length > 0) set.Add(name);
            }
            return set;
        }

        public static void Apply(Assembly ror2, string list)
        {
            try
            {
                Wanted = Parse(list);
                if (Wanted.Count == 0) return;
                var catalog = ror2?.GetType("RoR2.RuleCatalog");
                var avail = catalog?.GetField("availability", Any);
                var available = avail?.FieldType.GetProperty("available", Any);
                var all = catalog?.GetField("allRuleDefs", Any);
                var tick = ror2?.GetType("RoR2.RoR2Application")?.GetEvent("onFixedUpdate", BindingFlags.Public | BindingFlags.Static);
                if (avail == null || available == null || all == null || tick == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Locked items: the game's rule list was not found (game updated?), not applied");
                    return;
                }
                Ready = () => (bool)available.GetValue(avail.GetValue(null), null);
                Rules = () => all.GetValue(null) as System.Collections.IEnumerable;
                var handler = new Action(Tick);
                Unsubscribe = () => tick.RemoveEventHandler(null, handler);
                Done = false;
                Ticks = 0;
                tick.AddEventHandler(null, handler);
                CompatShieldPatcher.Log.LogInfo($"Locked items: {Wanted.Count} name(s) will be locked off in the lobby as soon as the game's rule list is ready");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Locked items: not applied: " + e.Message);
            }
        }

        /// <summary>Runs on the game's physics tick until the rule list is ready, then locks once and stops listening.</summary>
        internal static void Tick()
        {
            if (Done || (Ticks++ % 25) != 0) return;
            try
            {
                if (Ready == null || !Ready()) return;
                Done = true;
                try { Unsubscribe?.Invoke(); } catch { }
                var missing = new List<string>();
                var names = new List<string>();
                int untitled;
                int locked = LockRules(Rules(), Wanted, names, missing, out untitled);
                names.Sort(StringComparer.OrdinalIgnoreCase);
                CompatShieldPatcher.Log.LogInfo($"Locked items: {locked} of {Wanted.Count} locked off in the lobby by name (the On choice can't be picked, shown as [CONFIG DISABLED]): {string.Join(", ", names)}");
                if (missing.Count > 0)
                {
                    missing.Sort(StringComparer.OrdinalIgnoreCase);
                    CompatShieldPatcher.Log.LogWarning($"Locked items: no item or equipment is named {string.Join(", ", missing)} (mod removed, or check the internal name), so nothing was locked for {(missing.Count == 1 ? "it" : "them")}");
                }
                if (untitled > 0)
                    CompatShieldPatcher.Log.LogWarning($"Locked items: {untitled} title(s) could not be marked [CONFIG DISABLED] (game updated?); the lock itself is on");
            }
            catch (Exception e)
            {
                if (!Done)
                {
                    Done = true;
                    try { Unsubscribe?.Invoke(); } catch { }
                }
                CompatShieldPatcher.Log.LogWarning("Locked items: not applied: " + e.Message);
            }
        }

        /// <summary>Locks every wanted rule in the list. Works on any objects with the game's field names, so the test can use stand-ins.</summary>
        internal static int LockRules(System.Collections.IEnumerable rules, HashSet<string> wanted, List<string> lockedNames, List<string> missing, out int untitled)
        {
            untitled = 0;
            if (rules == null) throw new InvalidOperationException("the rule list is empty");
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in rules)
            {
                if (rule == null) continue;
                string global = Field(rule, "globalName")?.GetValue(rule) as string;
                if (global == null) continue;
                string name = global.StartsWith("Items.", StringComparison.Ordinal) ? global.Substring(6)
                    : global.StartsWith("Equipment.", StringComparison.Ordinal) ? global.Substring(10) : null;
                if (name == null || !wanted.Contains(name)) continue;
                var choices = Field(rule, "choices")?.GetValue(rule) as System.Collections.IEnumerable;
                if (choices == null) throw new InvalidOperationException("a rule has no choices list");
                bool on = false;
                var all = new List<object>();
                foreach (var choice in choices)
                {
                    if (choice == null) continue;
                    all.Add(choice);
                    if (!string.Equals(Field(choice, "localName")?.GetValue(choice) as string, "On", StringComparison.Ordinal)) continue;
                    var exclude = Field(choice, "excludeByDefault");
                    if (exclude == null || exclude.FieldType != typeof(bool)) throw new InvalidOperationException("a rule choice has no excludeByDefault flag");
                    exclude.SetValue(choice, true);
                    on = true;
                }
                if (!on) continue;
                if (found.Add(name)) lockedNames.Add(name);
                foreach (var choice in all)
                {
                    try { if (!Retitle(choice)) untitled++; }
                    catch { untitled++; }
                }
            }
            foreach (var w in wanted) if (!found.Contains(w)) missing.Add(w);
            return found.Count;
        }

        static FieldInfo Field(object o, string name) => o.GetType().GetField(name, Any);

        /// <summary>Wraps the choice's title function once. False when the field is not the expected shape.</summary>
        static bool Retitle(object choice)
        {
            if (Originals.ContainsKey(choice)) return true;
            var f = Field(choice, "getTooltipName");
            if (f == null || !f.FieldType.IsGenericType) return false;
            var args = f.FieldType.GetGenericArguments();
            if (args.Length != 2 || args[1] != typeof(string) || !args[0].IsInstanceOfType(choice) || !typeof(Delegate).IsAssignableFrom(f.FieldType)) return false;
            var wrapper = Delegate.CreateDelegate(f.FieldType, typeof(LockedItems).GetMethod(nameof(NameOf), Any).MakeGenericMethod(args[0]));
            Originals[choice] = f.GetValue(choice) as Delegate;
            f.SetValue(choice, wrapper);
            return true;
        }

        /// <summary>The title a locked choice shows: the red marker plus whatever the game or another mod would have shown.</summary>
        public static string NameOf<T>(T choice)
        {
            string name = null;
            try
            {
                Delegate original;
                if (choice != null && Originals.TryGetValue(choice, out original) && original is Func<T, string> f) name = f(choice);
                else if (choice != null) name = Field(choice, "tooltipNameToken")?.GetValue(choice) as string;
            }
            catch { }
            if (name != null && name.StartsWith(Prefix, StringComparison.Ordinal)) return name;   // TinkersSatchel already marked it
            return Prefix + (name ?? "");
        }
    }

    /// <summary>
    /// [8. Diagnostics] BigHitTracer: names what dealt a huge hit to a player. On 2026-11-10 a blast that looks like the Wandering
    /// Vagrant's nova kept taking most of the players' health, sometimes as enemies died, and nothing in the log could say what
    /// fired it: the game's death messages name a killer, never the item, skill or mod code behind the hit. During a run this
    /// listens to the game's own server damage and death events. For a hit on a player-controlled body that takes 25% or more of
    /// full health, or the last of it, and for every player death, it logs the victim, the attacker (body, team, Umbra or not,
    /// minion owner), the object that carried the damage, the damage type and skill slot, crit, proc chain, the size against the
    /// attacker's damage stat, how far the hit point was from the victim, and the code path that dealt it with pass-through hooks
    /// left out. At most 5 lines per source (the path on the first 2) and 60 per run, then a summary at run end. Log only: nothing
    /// in the game changes. Nothing here touches the game at load: the two events are subscribed at the first run start. Only
    /// the host's game deals damage, so only the host needs it.
    /// </summary>
    public static class BigHitTracer
    {
        internal const float ShareOfFullHealth = 0.25f;
        internal const int PerSourceLines = 5, PerSourcePaths = 2, PerRunLines = 60, PathFrames = 12, SummarySources = 8, MaxFailures = 20;
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        /// <summary>One hit, already read out of the game's damage report (public so the harness can build one).</summary>
        public struct Hit
        {
            public bool Death, Self, Umbra, Crit;
            public string Victim, VictimBody, Attacker, AttackerBody, AttackerTeam, Owner, Inflictor, DamageType, Source, Procs, Dot;
            public float Dealt, Before, Full, Raw, AttackerDamage, ProcCoefficient, Distance;
            public object Info;   // the game's DamageInfo, kept only to tell a death from the big hit already logged for it
        }

        /// <summary>
        /// Every game member this reads, as type|member|kind (a method row also names its one parameter's type). The harness
        /// checks each row against the real RoR2.dll, and that the code asks for nothing outside this list.
        /// </summary>
        internal static readonly string[] Needs =
        {
            "RoR2.GlobalEventManager|onServerDamageDealt|event", "RoR2.GlobalEventManager|onCharacterDeathServer|event",
            "RoR2.DamageReport|victim|field", "RoR2.DamageReport|victimBody|field", "RoR2.DamageReport|damageInfo|field",
            "RoR2.DamageReport|attacker|field", "RoR2.DamageReport|attackerBody|field", "RoR2.DamageReport|attackerTeamIndex|field",
            "RoR2.DamageReport|attackerOwnerMaster|field", "RoR2.DamageReport|dotType|field", "RoR2.DamageReport|damageDealt|field",
            "RoR2.DamageReport|combinedHealthBeforeDamage|field",
            "RoR2.DamageInfo|damage|field", "RoR2.DamageInfo|crit|field", "RoR2.DamageInfo|inflictor|field", "RoR2.DamageInfo|position|field",
            "RoR2.DamageInfo|procChainMask|field", "RoR2.DamageInfo|procCoefficient|field", "RoR2.DamageInfo|damageType|field",
            "RoR2.DamageTypeCombo|damageType|field", "RoR2.DamageTypeCombo|damageTypeExtended|field", "RoR2.DamageTypeCombo|damageSource|field",
            "RoR2.CharacterBody|isPlayerControlled|property", "RoR2.CharacterBody|damage|property", "RoR2.CharacterBody|corePosition|property",
            "RoR2.CharacterBody|inventory|property", "RoR2.HealthComponent|fullCombinedHealth|property",
            "RoR2.Util|GetBestBodyName|method|GameObject", "RoR2.Util|GetBestMasterName|method|CharacterMaster",
            "RoR2.Inventory|GetItemCountEffective|method|ItemDef", "RoR2.RoR2Content+Items|InvadingDoppelganger|field",
        };

        // Swappable for the harness (no Unity and no game there).
        internal static Func<string> Where = () => $"in {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} ({LagTracer.RunClock()})";
        internal static Func<string> Path = DefaultPath;
        internal static Func<object, Hit> Light = DefaultLight;        // the three numbers and the DamageInfo, for the size test
        internal static Func<object, Hit, Hit> Full = DefaultFull;     // everything else, only for a hit that will be counted
        internal static bool Running;
        static bool hooked, subscribed, broken, capSaid;
        static int lines, failures;
        static readonly Dictionary<string, int> seen = new Dictionary<string, int>(StringComparer.Ordinal);
        static Type filterType;
        static Func<object, bool> filter;
        static Members members;
        static object lastInfo;

        public static void Apply()
        {
            if (hooked) return;
            hooked = true;
            try
            {
                var run = Type.GetType("RoR2.Run, RoR2");
                foreach (var pair in new[] { ("onRunStartGlobal", nameof(OnRunStart)), ("onRunDestroyGlobal", nameof(OnRunDestroy)) })
                {
                    var ev = run?.GetEvent(pair.Item1, BindingFlags.Public | BindingFlags.Static);
                    if (ev == null) throw new MissingMemberException("RoR2.Run." + pair.Item1 + " not found");
                    ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(BigHitTracer).GetMethod(pair.Item2, BindingFlags.NonPublic | BindingFlags.Static)));
                }
                CompatShieldPatcher.Log.LogInfo($"Big-hit tracer: during a run, logs who and what dealt any hit that takes {Num(ShareOfFullHealth * 100f, "0")}% or more of a player's " +
                    $"full health, or the last of it, and every player death (up to {PerSourceLines} lines per source, {PerRunLines} per run, then a summary)");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Big-hit tracer: can't watch runs, not applied: " + e.Message);
            }
        }

        // Run start, main thread, game fully up: the first one is where the damage and death events are subscribed.
        static void OnRunStart(object run)
        {
            try
            {
                ResetRun();
                if (!subscribed)
                {
                    Subscribe(Type.GetType("RoR2.GlobalEventManager, RoR2"));
                    subscribed = true;
                }
                Running = true;
            }
            catch (Exception e)
            {
                Running = false;
                CompatShieldPatcher.Log.LogWarning("Big-hit tracer: couldn't start for this run: " + e.Message);
            }
        }

        static void OnRunDestroy(object run)
        {
            bool was = Running;
            Running = false;
            if (!was) return;
            try
            {
                string s = Summary();
                if (s != null) CompatShieldPatcher.Log.LogInfo(s);
            }
            catch { }
        }

        /// <summary>Adds both handlers, or neither: a missing event throws before anything is subscribed.</summary>
        internal static void Subscribe(Type events)
        {
            var pairs = new[] { ("onServerDamageDealt", nameof(OnDamage)), ("onCharacterDeathServer", nameof(OnDeath)) };
            var found = new List<EventInfo>();
            foreach (var pair in pairs)
            {
                var ev = events?.GetEvent(pair.Item1, BindingFlags.Public | BindingFlags.Static);
                if (ev == null) throw new MissingMemberException("RoR2.GlobalEventManager." + pair.Item1 + " not found");
                found.Add(ev);
            }
            var handlers = new List<Delegate>();
            for (int i = 0; i < pairs.Length; i++)
                handlers.Add(Delegate.CreateDelegate(found[i].EventHandlerType, typeof(BigHitTracer).GetMethod(pairs[i].Item2, BindingFlags.NonPublic | BindingFlags.Static)));
            for (int i = 0; i < pairs.Length; i++) found[i].AddEventHandler(null, handlers[i]);
        }

        static void OnDamage(object report) { Handle(report, false); }
        static void OnDeath(object report) { Handle(report, true); }

        // Called for every hit the host's game deals, so everything before the size test is kept cheap: one compiled check
        // for "is the victim a player's body" turns away nearly all of them.
        static void Handle(object report, bool death)
        {
            if (!Running || broken || report == null) return;
            try
            {
                var type = report.GetType();
                if (type != filterType)
                {
                    filter = BuildPlayerFilter(type);
                    filterType = type;
                }
                if (!filter(report)) return;
                Hit h = Light(report);
                h.Death = death;
                if (death)
                {
                    if (h.Info != null && ReferenceEquals(h.Info, lastInfo)) return;   // the big-hit line already says it took the last health
                }
                else if (!Qualifies(h.Dealt, h.Before, h.Full)) return;
                h = Full(report, h);
                if (!death && h.DamageType != null && h.DamageType.IndexOf("FallDamage", StringComparison.Ordinal) >= 0) return;
                lastInfo = h.Info;
                Consider(h);
            }
            catch (MissingMemberException e)
            {
                broken = true;
                CompatShieldPatcher.Log.LogWarning("Big-hit tracer: the game's damage report changed shape, not applied: " + e.Message);
            }
            catch (Exception e)
            {
                failures++;
                if (failures == 1) CompatShieldPatcher.Log.LogWarning("Big-hit tracer: couldn't read a hit (logged once): " + e.GetType().Name + ": " + e.Message);
                if (failures >= MaxFailures)
                {
                    broken = true;
                    CompatShieldPatcher.Log.LogWarning($"Big-hit tracer: stopped after {MaxFailures} hits it couldn't read");
                }
            }
        }

        /// <summary>A hit counts when it took the last of the health, or 25% or more of full health (health plus shield).</summary>
        internal static bool Qualifies(float dealt, float before, float full)
        {
            if (!(dealt > 0f) || !(full > 0f)) return false;
            return dealt >= full * ShareOfFullHealth || (before > 0f && dealt >= before - 0.01f);
        }

        /// <summary>
        /// (report) => report.victimBody != null and report.victimBody.isPlayerControlled, compiled once per report type so the
        /// common case (a monster was hit) costs no reflection. Falls back to reflection if it can't be compiled.
        /// </summary>
        internal static Func<object, bool> BuildPlayerFilter(Type reportType)
        {
            var field = reportType.GetField("victimBody", Any);
            var prop = field?.FieldType.GetProperty("isPlayerControlled", Any);
            if (field == null || prop == null || prop.PropertyType != typeof(bool) || prop.GetGetMethod(true) == null)
                throw new MissingMemberException(reportType.FullName + ".victimBody.isPlayerControlled");
            try
            {
                var p = System.Linq.Expressions.Expression.Parameter(typeof(object), "report");
                var body = System.Linq.Expressions.Expression.Field(System.Linq.Expressions.Expression.Convert(p, reportType), field);
                var test = System.Linq.Expressions.Expression.AndAlso(
                    System.Linq.Expressions.Expression.ReferenceNotEqual(body, System.Linq.Expressions.Expression.Constant(null, typeof(object))),
                    System.Linq.Expressions.Expression.Property(body, prop));
                return System.Linq.Expressions.Expression.Lambda<Func<object, bool>>(test, p).Compile();
            }
            catch
            {
                return r => { var b = field.GetValue(r); return b != null && (bool)prop.GetValue(b, null); };
            }
        }

        /// <summary>Counts the hit under its source and logs it while the caps allow. True when a line was written.</summary>
        internal static bool Consider(Hit h)
        {
            string key = (h.AttackerBody ?? "no attacker") + "|" + (h.Inflictor ?? "nothing named") + "|" + (h.DamageType ?? "?");
            int nth = (seen.TryGetValue(key, out var c) ? c : 0) + 1;
            seen[key] = nth;
            if (nth > PerSourceLines) return false;
            if (lines >= PerRunLines)
            {
                if (!capSaid)
                {
                    capSaid = true;
                    CompatShieldPatcher.Log.LogInfo($"Big-hit tracer: {PerRunLines} lines this run; further hits are only counted for the summary at run end");
                }
                return false;
            }
            lines++;
            string path = null, where;
            if (nth <= PerSourcePaths) { try { path = Path() ?? ""; } catch { path = ""; } }
            try { where = Where(); } catch { where = "(place unknown)"; }
            CompatShieldPatcher.Log.LogInfo(Describe(h, where, path, nth));
            return true;
        }

        static string Num(float v, string format) { return v.ToString(format, System.Globalization.CultureInfo.InvariantCulture); }

        /// <summary>The log line for one hit (pure; the harness feeds it). path null = left out for this hit.</summary>
        internal static string Describe(Hit h, string where, string path, int nth)
        {
            var sb = new System.Text.StringBuilder(h.Death ? "Player death: " : "Big hit: ");
            sb.Append(h.Victim ?? "?").Append(" (").Append(h.VictimBody ?? "?").Append(')');
            string share = h.Full > 0f ? Num(h.Dealt / h.Full * 100f, "0") : "?";
            if (h.Death)
                sb.Append($" died to a hit of {Num(h.Dealt, "0")} ({share}% of {Num(h.Full, "0")} full health; had {Num(h.Before, "0")})");
            else
            {
                bool last = h.Before > 0f && h.Dealt >= h.Before - 0.01f;
                sb.Append($" lost {share}% of full health in one hit ({Num(h.Dealt, "0")} of {Num(h.Full, "0")}; had {Num(h.Before, "0")}");
                sb.Append(last ? ", which took the last of it)" : ")");
            }
            sb.Append(' ').Append(where ?? "(place unknown)").Append(". From: ");
            if (h.AttackerBody == null) sb.Append("no attacker");
            else
            {
                sb.Append(h.Attacker ?? h.AttackerBody).Append(" (").Append(h.AttackerBody).Append(", team ").Append(h.AttackerTeam ?? "none");
                if (h.Umbra) sb.Append(", an Umbra");
                if (h.Self) sb.Append(", the victim's own body");
                if (h.Owner != null) sb.Append(", minion of ").Append(h.Owner);
                sb.Append(')');
            }
            sb.Append(". Carried by: ").Append(h.Inflictor ?? "nothing named");
            sb.Append(". Type: ").Append(h.DamageType ?? "?");
            if (h.Source != null && h.Source != "NoneSpecified" && h.Source != "0") sb.Append(", skill slot ").Append(h.Source);
            if (h.Dot != null) sb.Append(", damage over time ").Append(h.Dot);
            sb.Append(h.Crit ? "; crit" : "; no crit");
            sb.Append("; proc coefficient ").Append(Num(h.ProcCoefficient, "0.##"));
            sb.Append("; procs ").Append(string.IsNullOrEmpty(h.Procs) ? "none" : h.Procs);
            sb.Append("; raw damage ").Append(Num(h.Raw, "0"));
            if (h.AttackerDamage > 0f) sb.Append(" = ").Append(Num(h.Raw / h.AttackerDamage, "0.#")).Append("x the attacker's damage stat");
            sb.Append("; hit point ").Append(Num(h.Distance, "0")).Append(" m from the victim");
            if (path == null) sb.Append($". Hit {nth} from this source, path left out");
            else sb.Append(". Path: ").Append(path.Length == 0 ? "(not readable)" : path);
            return sb.ToString();
        }

        /// <summary>The run-end line: how many counted hits each source dealt, most first. Null when there were none.</summary>
        internal static string Summary()
        {
            if (seen.Count == 0) return null;
            int total = seen.Values.Sum();
            var top = seen.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Take(SummarySources).Select(p =>
            {
                var k = p.Key.Split('|');
                return $"{k[0]} via {(k.Length > 1 ? k[1] : "?")} ({(k.Length > 2 ? k[2] : "?")}) x{p.Value}";
            });
            return $"Big hits this run: {total} from {seen.Count} source(s), most first: " + string.Join("; ", top) + (seen.Count > SummarySources ? "; ..." : "");
        }

        /// <summary>
        /// One stack frame as it should read in the path, or null to leave it out (pure). Left out: this tracer, the runtime's
        /// own frames, MonoMod and Harmony glue, a hook handler (its first parameter is the "orig" delegate; it only passes the
        /// call on), and the damage plumbing every hit shares. MonoMod's copy of a patched method, DMD&lt;Type::Method&gt;, reads
        /// as Type.Method.
        /// </summary>
        internal static string FrameName(string type, string method, bool takesDelegateFirst)
        {
            if (string.IsNullOrEmpty(method)) return null;
            int dmd = method.IndexOf("DMD<", StringComparison.Ordinal);
            if (dmd >= 0)
            {
                int end = method.LastIndexOf('>');
                if (end <= dmd + 4) return null;
                string inner = method.Substring(dmd + 4, end - dmd - 4);
                int paren = inner.IndexOf('(');
                if (paren >= 0) inner = inner.Substring(0, paren);
                int space = inner.LastIndexOf(' ');
                if (space >= 0) inner = inner.Substring(space + 1);
                inner = inner.Replace("::", ".");
                return inner.Length == 0 || Plumbing(inner) ? null : inner;
            }
            if (method.StartsWith("Hook<", StringComparison.Ordinal) || method.StartsWith("Trampoline<", StringComparison.Ordinal)
                || method.StartsWith("Chain<", StringComparison.Ordinal) || method.StartsWith("Glue:", StringComparison.Ordinal)) return null;
            if (type == null || takesDelegateFirst) return null;
            if (type.StartsWith("CompatShield.BigHitTracer", StringComparison.Ordinal) || type.StartsWith("System.", StringComparison.Ordinal)
                || type.StartsWith("MonoMod.", StringComparison.Ordinal) || type.StartsWith("HarmonyLib.", StringComparison.Ordinal)
                || type.StartsWith("On.", StringComparison.Ordinal) || type.StartsWith("IL.", StringComparison.Ordinal)) return null;
            string full = type.Replace('+', '.') + "." + method;
            return Plumbing(full) ? null : full;
        }

        static bool Plumbing(string name)
        {
            return name == "RoR2.GlobalEventManager.ServerDamageDealt" || name == "RoR2.GlobalEventManager.OnCharacterDeath"
                || name.StartsWith("RoR2.HealthComponent.TakeDamage", StringComparison.Ordinal);
        }

        /// <summary>Innermost frame first. Keeps the first PathFrames names and says how much was left out (pure).</summary>
        internal static string JoinPath(List<string> names, int hooks)
        {
            var kept = new List<string>();
            foreach (var n in names ?? new List<string>())
            {
                if (n == null || (kept.Count > 0 && kept[kept.Count - 1] == n)) continue;
                kept.Add(n);
            }
            if (kept.Count == 0) return hooks > 0 ? $"({hooks} hook frames, nothing else readable)" : "";
            int more = Math.Max(0, kept.Count - PathFrames);
            return string.Join(" < ", kept.Take(PathFrames)) + (more > 0 ? $" < ... {more} more" : "") + (hooks > 0 ? $" ({hooks} hook frames left out)" : "");
        }

        // The code path that is dealing this hit, read off the live stack: innermost first, so the frame right after the game's
        // blast or bullet code is whatever fired it.
        static string DefaultPath()
        {
            var frames = new System.Diagnostics.StackTrace(false).GetFrames();
            var names = new List<string>();
            int hooks = 0;
            if (frames == null) return "";
            foreach (var f in frames)
            {
                MethodBase mb = null;
                try { mb = f.GetMethod(); } catch { }
                if (mb == null) continue;
                bool hook = false;
                try
                {
                    var ps = mb.GetParameters();
                    hook = ps.Length > 0 && typeof(Delegate).IsAssignableFrom(ps[0].ParameterType);
                }
                catch { }
                string type = null;
                try { type = mb.DeclaringType?.FullName; } catch { }
                string n = FrameName(type, mb.Name, hook);
                if (n == null)
                {
                    if (hook && type != null && !type.StartsWith("System.", StringComparison.Ordinal)) hooks++;
                    continue;
                }
                names.Add(n);
            }
            return JoinPath(names, hooks);
        }

        /// <summary>Finds every member in Needs, or throws naming all that are missing.</summary>
        internal static Dictionary<string, MemberInfo> ResolveAll(Func<string, Type> typeOf)
        {
            var found = new Dictionary<string, MemberInfo>(StringComparer.Ordinal);
            var missing = new List<string>();
            foreach (var row in Needs)
            {
                var p = row.Split('|');
                Type t = typeOf(p[0]);
                MemberInfo mi = null;
                if (t != null)
                {
                    if (p[2] == "event") mi = t.GetEvent(p[1], Any);
                    else if (p[2] == "field") mi = t.GetField(p[1], Any);
                    else if (p[2] == "property") mi = t.GetProperty(p[1], Any);
                    else if (p[2] == "method") mi = t.GetMethods(Any).FirstOrDefault(x => x.Name == p[1] && x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType.Name == p[3]);
                }
                if (mi == null) missing.Add(p[0] + "." + p[1]);
                else found[p[0] + "|" + p[1]] = mi;
            }
            if (missing.Count > 0) throw new MissingMemberException(string.Join(", ", missing));
            return found;
        }

        /// <summary>The resolved members, by what they are read for.</summary>
        internal sealed class Members
        {
            public FieldInfo Victim, VictimBody, Info, Attacker, AttackerBody, AttackerTeam, OwnerMaster, DotType, Dealt, Before;
            public FieldInfo Damage, Crit, Inflictor, Position, Procs, ProcCoefficient, DamageType, TypeBase, TypeExtended, TypeSource, UmbraItem;
            public PropertyInfo BodyDamage, CorePosition, Inventory, FullHealth;
            public MethodInfo BodyName, MasterName, ItemCount;

            static T Get<T>(Dictionary<string, MemberInfo> m, string key) where T : MemberInfo
            {
                if (m != null && m.TryGetValue(key, out var v) && v is T t) return t;
                throw new MissingMemberException(key.Replace('|', '.'));
            }

            internal static Members From(Dictionary<string, MemberInfo> m)
            {
                const string R = "RoR2.DamageReport|", I = "RoR2.DamageInfo|", C = "RoR2.DamageTypeCombo|", B = "RoR2.CharacterBody|";
                return new Members
                {
                    Victim = Get<FieldInfo>(m, R + "victim"), VictimBody = Get<FieldInfo>(m, R + "victimBody"), Info = Get<FieldInfo>(m, R + "damageInfo"),
                    Attacker = Get<FieldInfo>(m, R + "attacker"), AttackerBody = Get<FieldInfo>(m, R + "attackerBody"),
                    AttackerTeam = Get<FieldInfo>(m, R + "attackerTeamIndex"), OwnerMaster = Get<FieldInfo>(m, R + "attackerOwnerMaster"),
                    DotType = Get<FieldInfo>(m, R + "dotType"), Dealt = Get<FieldInfo>(m, R + "damageDealt"), Before = Get<FieldInfo>(m, R + "combinedHealthBeforeDamage"),
                    Damage = Get<FieldInfo>(m, I + "damage"), Crit = Get<FieldInfo>(m, I + "crit"), Inflictor = Get<FieldInfo>(m, I + "inflictor"),
                    Position = Get<FieldInfo>(m, I + "position"), Procs = Get<FieldInfo>(m, I + "procChainMask"),
                    ProcCoefficient = Get<FieldInfo>(m, I + "procCoefficient"), DamageType = Get<FieldInfo>(m, I + "damageType"),
                    TypeBase = Get<FieldInfo>(m, C + "damageType"), TypeExtended = Get<FieldInfo>(m, C + "damageTypeExtended"), TypeSource = Get<FieldInfo>(m, C + "damageSource"),
                    BodyDamage = Get<PropertyInfo>(m, B + "damage"), CorePosition = Get<PropertyInfo>(m, B + "corePosition"), Inventory = Get<PropertyInfo>(m, B + "inventory"),
                    FullHealth = Get<PropertyInfo>(m, "RoR2.HealthComponent|fullCombinedHealth"),
                    BodyName = Get<MethodInfo>(m, "RoR2.Util|GetBestBodyName"), MasterName = Get<MethodInfo>(m, "RoR2.Util|GetBestMasterName"),
                    ItemCount = Get<MethodInfo>(m, "RoR2.Inventory|GetItemCountEffective"), UmbraItem = Get<FieldInfo>(m, "RoR2.RoR2Content+Items|InvadingDoppelganger"),
                };
            }
        }

        static string Short(string name)
        {
            if (name == null) return null;
            while (name.EndsWith("(Clone)", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 7);
            return name.Trim();
        }

        // In the game only, from here down.
        static Hit DefaultLight(object report)
        {
            if (members == null)
            {
                var asm = report.GetType().Assembly;
                members = Members.From(ResolveAll(n => asm.GetType(n)));
            }
            var m = members;
            var h = new Hit { Dealt = (float)m.Dealt.GetValue(report), Before = (float)m.Before.GetValue(report), Info = m.Info.GetValue(report) };
            var health = m.Victim.GetValue(report) as UnityEngine.Object;
            if (health != null) h.Full = (float)m.FullHealth.GetValue(health, null);
            return h;
        }

        static Hit DefaultFull(object report, Hit h)
        {
            var m = members;
            var body = m.VictimBody.GetValue(report) as Component;
            GameObject victimObject = body != null ? body.gameObject : null;
            if (victimObject != null)
            {
                h.Victim = (string)m.BodyName.Invoke(null, new object[] { victimObject });
                h.VictimBody = Short(victimObject.name);
            }
            var attacker = m.Attacker.GetValue(report) as GameObject;
            if (attacker != null)
            {
                h.Attacker = (string)m.BodyName.Invoke(null, new object[] { attacker });
                h.AttackerBody = Short(attacker.name);
                h.Self = victimObject != null && attacker == victimObject;
            }
            var attackerBody = m.AttackerBody.GetValue(report) as Component;
            if (attackerBody != null)
            {
                h.AttackerTeam = m.AttackerTeam.GetValue(report)?.ToString();
                h.AttackerDamage = (float)m.BodyDamage.GetValue(attackerBody, null);
                try
                {
                    var inventory = m.Inventory.GetValue(attackerBody, null) as UnityEngine.Object;
                    var umbraItem = m.UmbraItem.GetValue(null) as UnityEngine.Object;
                    h.Umbra = inventory != null && umbraItem != null && (int)m.ItemCount.Invoke(inventory, new object[] { umbraItem }) > 0;
                }
                catch { }
            }
            var owner = m.OwnerMaster.GetValue(report) as UnityEngine.Object;
            if (owner != null) h.Owner = (string)m.MasterName.Invoke(null, new object[] { owner });
            string dot = m.DotType.GetValue(report)?.ToString();
            if (dot != null && dot != "None") h.Dot = dot;
            if (h.Info != null)
            {
                var inflictor = m.Inflictor.GetValue(h.Info) as GameObject;
                if (inflictor != null) h.Inflictor = Short(inflictor.name);
                h.Raw = (float)m.Damage.GetValue(h.Info);
                h.Crit = (bool)m.Crit.GetValue(h.Info);
                h.ProcCoefficient = (float)m.ProcCoefficient.GetValue(h.Info);
                h.Procs = (m.Procs.GetValue(h.Info)?.ToString() ?? "").Trim('(', ')');
                object combo = m.DamageType.GetValue(h.Info);
                h.DamageType = m.TypeBase.GetValue(combo)?.ToString();
                string extended = m.TypeExtended.GetValue(combo)?.ToString();
                if (!string.IsNullOrEmpty(extended) && extended != "Generic" && extended != "0") h.DamageType += " + " + extended;
                h.Source = m.TypeSource.GetValue(combo)?.ToString();
                if (body != null) h.Distance = ((Vector3)m.Position.GetValue(h.Info) - (Vector3)m.CorePosition.GetValue(body, null)).magnitude;
            }
            return h;
        }

        internal static void ResetRun() { seen.Clear(); lines = 0; capSaid = false; lastInfo = null; }
        internal static void ResetForTest()
        {
            ResetRun();
            Running = false; broken = false; failures = 0; filterType = null; filter = null; members = null;
            Where = () => "in test (stage 1 t=0:00)"; Path = DefaultPath; Light = DefaultLight; Full = DefaultFull;
        }
    }

    /// <summary>
    /// [8. Diagnostics] HookTimer, the part that runs before any mod loads: it listens for every hook a mod puts on one of the
    /// watched game methods and remembers the mod's method. It touches MonoMod only, never a game or Unity type, so it is safe
    /// at preload. MonoMod calls OnHook for every hook in the session (thousands), so it does one name lookup and returns.
    /// </summary>
    public static class HookTimerCapture
    {
        /// <summary>Game methods whose hooks are timed, as "Type::Method" (every overload). Chosen from the hook census of the
        /// 2026-11-10 log: the damage, stat, buff, inventory and per-frame methods that carry the most mod hooks.</summary>
        internal static readonly string[] Paths =
        {
            "RoR2.CharacterBody::RecalculateStats", "RoR2.CharacterBody::FixedUpdate", "RoR2.CharacterBody::OnInventoryChanged",
            "RoR2.CharacterBody::AddTimedBuff", "RoR2.CharacterBody::OnSkillActivated", "RoR2.CharacterBody::OnBuffFirstStackGained",
            "RoR2.CharacterBody::OnBuffFinalStackLost", "RoR2.CharacterBody::UpdateAllTemporaryVisualEffects", "RoR2.CharacterBody::UpdateBuffs",
            "RoR2.CharacterBody::AddBuff", "RoR2.CharacterBody::RemoveBuff", "RoR2.CharacterBody::SetBuffCount",
            "RoR2.HealthComponent::TakeDamage", "RoR2.HealthComponent::TakeDamageProcess", "RoR2.HealthComponent::Heal",
            "RoR2.HealthComponent::UpdateLastHitTime", "RoR2.HealthComponent::ServerFixedUpdate",
            "RoR2.GlobalEventManager::OnHitEnemy", "RoR2.GlobalEventManager::ProcessHitEnemy", "RoR2.GlobalEventManager::OnHitAll",
            "RoR2.GlobalEventManager::OnHitAllProcess", "RoR2.GlobalEventManager::OnCharacterDeath",
            "RoR2.CharacterModel::UpdateOverlays", "RoR2.CharacterMaster::OnInventoryChanged",
            "RoR2.EquipmentSlot::UpdateTargets", "RoR2.EquipmentSlot::Update", "RoR2.UI.SkillIcon::LateUpdate", "RoR2.UI.HealthBar::UpdateBarInfos",
            "RoR2.DotController::InflictDot", "RoR2.DotController::AddDot", "RoR2.DeathRewards::OnKilledServer", "RoR2.SetStateOnHurt::OnTakeDamageServer",
            "RoR2.BulletAttack::Fire", "RoR2.OverlapAttack::Fire", "RoR2.BlastAttack::Fire", "RoR2.Projectile.ProjectileManager::FireProjectile",
            "RoR2.Run::FixedUpdate", "RoR2.EntityStateMachine::FixedUpdate", "RoR2.CharacterAI.BaseAI::FixedUpdate",
        };

        static readonly object gate = new object();
        static readonly List<KeyValuePair<string, MethodBase>> seen = new List<KeyValuePair<string, MethodBase>>();
        static readonly HashSet<MethodBase> seenSet = new HashSet<MethodBase>();
        static readonly HashSet<string> keys = new HashSet<string>(Paths, StringComparer.Ordinal);
        static readonly HashSet<string> names = new HashSet<string>(Paths.Select(p => p.Substring(p.IndexOf("::", StringComparison.Ordinal) + 2)), StringComparer.Ordinal);
        internal static bool Installed;

        public static void Install()
        {
            if (Installed) return;
            try
            {
                MonoMod.RuntimeDetour.Hook.OnDetour += OnHook;
                Installed = true;
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Hook timer: can't watch hooks being added, not applied: " + e.Message);
            }
        }

        // MonoMod stops applying a hook when this returns false, so it always returns true and never throws.
        internal static bool OnHook(MonoMod.RuntimeDetour.Hook hook, MethodBase from, MethodBase to, object target)
        {
            try { Note(from, to); } catch { }
            return true;
        }

        internal static void Note(MethodBase from, MethodBase to)
        {
            if (from == null || to == null || !names.Contains(from.Name)) return;
            var type = from.DeclaringType;
            if (type == null) return;
            string key = type.FullName + "::" + from.Name;
            if (!keys.Contains(key)) return;
            lock (gate) { if (seenSet.Add(to)) seen.Add(new KeyValuePair<string, MethodBase>(key, to)); }
        }

        internal static KeyValuePair<string, MethodBase>[] Snapshot() { lock (gate) return seen.ToArray(); }

        internal static void ResetForTest() { lock (gate) { seen.Clear(); seenSet.Clear(); } }
    }

    /// <summary>
    /// [8. Diagnostics] HookTimer: names the mods whose code costs the most inside the game's busiest methods. The lag tracer
    /// times those methods whole; on 2026-11-10 every one of them cost 40 to 80% more per call than two days earlier, after about
    /// twenty mods were added, and nothing could say which mod. From the first run start this wraps a small timer around
    /// (a) each mod method hooked onto a watched game method, (b) other mods' Harmony patches on it, (c) the handlers of the
    /// static events raised inside it (R2API's stat event, the game's damage, death, heal and inventory events) and (d) the
    /// game method itself. Calls nest (a hook calls the next hook, which calls the game), so each timer subtracts the timed
    /// calls made inside it: a mod is charged only for its own code. What is left on the game method is the game's own code
    /// plus IL hooks and instance callbacks the timer can't see. The timer's own cost is measured once and taken out, or a
    /// hook that does nothing would be charged for being timed. Reports go to the log every 5 minutes, at each stage change
    /// and at run end: mods ranked by time, then single hooks. Log only, nothing in the game changes, but it rebuilds a few
    /// hundred mod methods with the timer inside (one more stack frame per hook), so it is off unless switched on. Nothing is
    /// patched at load: HookTimerCapture only records hooks there. Each PC measures itself.
    /// </summary>
    public static class HookTimer
    {
        internal const int MaxTimed = 1000;             // methods that may carry the timer
        internal const int MaxDepth = 128;              // nested timed calls
        internal const double PassBudgetMs = 10000;     // one patching pass may hold the game this long; the rest waits for the next stage
        internal const double ReportEverySeconds = 300;
        internal const string GameBucket = "(game code and IL hooks)";
        const string HarmonyId = "local.compatshield.hooktimer";
        const string Own = "part of the timers";

        /// <summary>Static events raised inside the watched methods: assembly, type, backing field, name in the report.</summary>
        internal static readonly string[][] Events =
        {
            new[] { "R2API.RecalculateStats", "R2API.RecalculateStatsAPI", "_getStatCoefficients", "RecalculateStatsAPI.GetStatCoefficients" },
            new[] { "RoR2", "RoR2.GlobalEventManager", "onServerDamageDealt", "GlobalEventManager.onServerDamageDealt" },
            new[] { "RoR2", "RoR2.GlobalEventManager", "onDeathProcServer", "GlobalEventManager.onDeathProcServer (onCharacterDeathGlobal)" },
            new[] { "RoR2", "RoR2.GlobalEventManager", "onCharacterDeathServer", "GlobalEventManager.onCharacterDeathServer" },
            new[] { "RoR2", "RoR2.CharacterBody", "onBodyInventoryChangedGlobal", "CharacterBody.onBodyInventoryChangedGlobal" },
            new[] { "RoR2", "RoR2.Inventory", "onInventoryChangedGlobal", "Inventory.onInventoryChangedGlobal" },
            new[] { "RoR2", "RoR2.HealthComponent", "onCharacterHealServer", "HealthComponent.onCharacterHealServer" },
        };

        /// <summary>What a timed call carries from its start to its end. Public: Harmony declares a local of this type in other mods' methods.</summary>
        public struct Mark { public long Start; public int Depth; }

        internal sealed class Entry
        {
            public readonly string Label, Mod, Path, Kind;      // Kind: game, hook, patch, handler
            // Ticks = own time in Stopwatch ticks, timed calls made inside left out. Kids = how many timed calls it made.
            public long Calls, Ticks, Max, Kids, StageCalls, StageTicks, StageMax, StageKids;
            // False until its first timed call has ended. That call pays for compiling the method with the timer in it, so it
            // is kept out of every count above. Never set back: a method is compiled once, not once a run.
            public bool Warm;
            // The other methods and events this one also sits on, when a mod puts one hook or patch method on several.
            public List<string> Also;
            public Entry(string label, string mod, string path, string kind) { Label = label; Mod = mod; Path = path; Kind = kind; }

            public void AlsoOn(string path)
            {
                if (path == null || path == Path) return;
                if (Also == null) Also = new List<string>();
                if (!Also.Contains(path)) Also.Add(path);
            }

            /// <summary>Where it sits, for a report line: one name, two names, or the first and how many more.</summary>
            public string Where()
            {
                if (Also == null || Also.Count == 0) return Path;
                return Also.Count == 1 ? Path + " and " + Also[0] : $"{Path} and {Also.Count} more";
            }
        }

        internal static bool Running;
        internal static Func<string> Scene = () => LagTracer.Scene();
        static bool hooked, ticking, calibrated, cut, capHit, saidStart;
        static bool stopped;    // a failed patch left a game method without its mods' changes: nothing more is patched this session
        static int mainThread = -1, depth, frames, skipCount, failCount, stagePart;
        static long misses, runStart, stageStart;
        // First calls left out of the counts: how many and their own time, for the run and for the stage so far.
        static long firstCalls, firstTicks, stageFirstCalls, stageFirstTicks;
        // What one timed call costs, in Stopwatch ticks: the part that lands inside its own measurement and the part left in its caller.
        static double insideTicks, outsideTicks, overheadNs = -1;
        static string stageName = "?";
        static readonly long[] child = new long[MaxDepth];
        static readonly int[] kids = new int[MaxDepth];
        static readonly Dictionary<MethodBase, Entry> byMethod = new Dictionary<MethodBase, Entry>();
        static readonly List<Entry> entries = new List<Entry>();
        static readonly HashSet<MethodBase> refused = new HashSet<MethodBase>();
        static readonly Dictionary<string, SortedSet<string>> ilOwners = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        static Harmony harmony;

        public static void Apply()
        {
            if (hooked) return;
            hooked = true;
            if (!HookTimerCapture.Installed) return;    // its own warning said why
            try
            {
                var run = Type.GetType("RoR2.Run, RoR2");
                foreach (var pair in new[] { new[] { "onRunStartGlobal", nameof(OnRunStart) }, new[] { "onRunDestroyGlobal", nameof(OnRunDestroy) } })
                {
                    var ev = run?.GetEvent(pair[0], BindingFlags.Public | BindingFlags.Static);
                    if (ev == null) throw new MissingMemberException("RoR2.Run." + pair[0] + " not found");
                    ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(HookTimer).GetMethod(pair[1], BindingFlags.NonPublic | BindingFlags.Static)));
                }
                CompatShieldPatcher.Log.LogInfo($"Hook timer: from the first run start, times each mod's hooks, patches and event handlers on {HookTimerCapture.Paths.Length} busy game methods " +
                    $"and logs the slowest mods and hooks every {ReportEverySeconds / 60:0} minutes, at each stage change and at run end. Log only; expect a pause of about 10 seconds at the first run start and a shorter one at the next stage");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Hook timer: can't watch runs, not applied: " + e.Message);
            }
        }

        // Run start, main thread, every mod loaded and hooked: the first place the timers are put on.
        static void OnRunStart(object run)
        {
            try
            {
                Running = false;
                if (stopped) return;
                mainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
                if (harmony == null) harmony = new Harmony(HarmonyId);
                if (!ticking) { SubscribeTicks(); ticking = true; }
                Pass("the run start");
                if (stopped) return;
                if (!calibrated)
                {
                    calibrated = true;
                    overheadNs = Calibrate();
                    if (overheadNs >= 0) CompatShieldPatcher.Log.LogInfo($"Hook timer: each timed call costs about {overheadNs:0} ns; that is taken out of the numbers in the reports");
                }
                ResetRun();
                stageName = "before the first stage";
                Running = true;
            }
            catch (Exception e)
            {
                Running = false;
                CompatShieldPatcher.Log.LogWarning("Hook timer: couldn't start for this run: " + e.Message);
            }
        }

        static void OnRunDestroy(object run)
        {
            bool was = Running;
            Running = false;
            if (!was) return;
            try
            {
                EmitStage(true);
                foreach (var line in Report("whole run", Seconds(runStart, Now()), false)) CompatShieldPatcher.Log.LogInfo(line);
                if (misses > 0) CompatShieldPatcher.Log.LogDebug($"Hook timer: {misses} timed calls could not be matched to a method");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogDebug("Hook timer: run report failed: " + e.Message);
            }
        }

        static void OnStageStart(object stage)
        {
            if (!Running) return;
            try
            {
                EmitStage(true);
                stageName = SafeScene();
                stagePart = 0;
                Pass("a stage change");     // hooks and handlers added since the last pass
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogDebug("Hook timer: stage report failed: " + e.Message);
            }
        }

        // Every rendered frame, outside every timed call. A timed call that threw never reached its end, so the nesting level
        // it claimed is given back here; without this an error loop would use the levels up in seconds.
        static void OnFrame()
        {
            depth = 0;
            if (!Running || (++frames & 63) != 0) return;
            try
            {
                if (Seconds(stageStart, Now()) >= ReportEverySeconds) EmitStage(false);
            }
            catch { }
        }

        /// <summary>RoR2Application.onUpdate is needed (this throws without it); the stage event only adds a report at each stage change.</summary>
        static void SubscribeTicks()
        {
            var app = Type.GetType("RoR2.RoR2Application, RoR2");
            var onUpdate = app?.GetEvent("onUpdate", BindingFlags.Public | BindingFlags.Static);
            if (onUpdate == null) throw new MissingMemberException("RoR2.RoR2Application.onUpdate not found");
            onUpdate.AddEventHandler(null, Delegate.CreateDelegate(onUpdate.EventHandlerType, typeof(HookTimer).GetMethod(nameof(OnFrame), BindingFlags.NonPublic | BindingFlags.Static)));
            try
            {
                var onStage = Type.GetType("RoR2.Stage, RoR2")?.GetEvent("onStageStartGlobal", BindingFlags.Public | BindingFlags.Static);
                if (onStage == null) throw new MissingMemberException("RoR2.Stage.onStageStartGlobal not found");
                onStage.AddEventHandler(null, Delegate.CreateDelegate(onStage.EventHandlerType, typeof(HookTimer).GetMethod(nameof(OnStageStart), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogDebug("Hook timer: no report at stage changes: " + e.Message);
            }
        }

        // ---------------------------------------------------------------- the timer itself

        static void Enter(out Mark __state)
        {
            __state = Open();
            if (__state.Depth >= 0) __state.Start = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        static void Exit(Mark __state, MethodBase __originalMethod)
        {
            if (__state.Depth < 0 || __state.Start == 0) return;
            Close(__state, System.Diagnostics.Stopwatch.GetTimestamp(), __originalMethod);
        }

        /// <summary>Claims the next nesting level, or Depth -1 when this call is not timed (no run, another thread, too deep).</summary>
        internal static Mark Open()
        {
            var mark = new Mark { Depth = -1 };
            if (!Running || System.Threading.Thread.CurrentThread.ManagedThreadId != mainThread) return mark;
            int d = depth;
            if (d >= MaxDepth) return mark;
            child[d] = 0;
            kids[d] = 0;
            depth = d + 1;
            mark.Depth = d;
            return mark;
        }

        /// <summary>
        /// Ends a timed call: its own time is the whole time less the timed calls made inside it, and the whole time is handed
        /// to the call it was made from. Setting the level back to this call's own also drops any call inside it that threw.
        /// A Start of 0 means Harmony skipped Enter (another prefix stopped the method first), and a level at or above the
        /// current one means the frame ended under it: neither is counted. Levels still held above this one belong to calls
        /// inside it that threw and were caught: the timed calls counted on those levels ran inside this call too, so their
        /// time is taken out here as well, or it would be counted twice.
        /// </summary>
        internal static void Close(Mark mark, long now, MethodBase method)
        {
            int d = mark.Depth;
            if (d < 0 || mark.Start == 0 || d >= depth) return;
            long whole = now - mark.Start, inner = 0;
            int made = 0;
            for (int i = d; i < depth; i++) { inner += child[i]; made += kids[i]; }
            long own = whole - inner;
            if (own < 0) own = 0;
            depth = d;
            if (d > 0) { child[d - 1] += whole; kids[d - 1]++; }
            if (method == null || !byMethod.TryGetValue(method, out var e)) { misses++; return; }
            if (!e.Warm)
            {
                // The first call pays for compiling the method with the timer in it. That is the timer's doing, not the
                // mod's, so it stays out of the total, the average and the slowest call, and the reports give the sum apart.
                e.Warm = true;
                firstCalls++; firstTicks += own;
                stageFirstCalls++; stageFirstTicks += own;
                return;
            }
            e.Calls++; e.Ticks += own; e.Kids += made;
            e.StageCalls++; e.StageTicks += own; e.StageKids += made;
            if (own > e.Max) e.Max = own;
            if (own > e.StageMax) e.StageMax = own;
        }

        static HarmonyMethod Patch(string name, int priority)
        {
            return new HarmonyMethod(typeof(HookTimer).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)) { priority = priority };
        }

        // ---------------------------------------------------------------- putting the timers on

        static void Pass(string when)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int before = byMethod.Count;
            cut = false;
            var originals = new List<KeyValuePair<MethodBase, string>>();
            foreach (string key in HookTimerCapture.Paths)
                foreach (var m in Resolve(key)) originals.Add(new KeyValuePair<MethodBase, string>(m, Short(key)));
            var hooks = HookTimerCapture.Snapshot();
            // Mod code first and the game methods last: a timer on a game method makes the runtime compile that method again,
            // and by then the mod methods it calls should already carry theirs.
            foreach (var kv in hooks) Time(kv.Value, Short(kv.Key), "hook", clock);
            foreach (var o in originals)
            {
                foreach (var pm in PatchMethods(o.Key)) Time(pm, o.Value, "patch", clock);
                NoteIlOwners(o.Key, o.Value);
            }
            foreach (var ev in Events)
                foreach (var h in Handlers(ev)) Time(h, ev[3], "handler", clock);
            foreach (var o in originals) Time(o.Key, o.Value, "game", clock);
            if (stopped) return;
            int added = byMethod.Count - before;
            if (added == 0 && saidStart) return;
            saidStart = true;
            Func<string, int> count = kind => entries.Count(e => e.Kind == kind);
            CompatShieldPatcher.Log.LogInfo($"Hook timer: timing {byMethod.Count} methods after {when} ({count("game")} game methods, {count("hook")} mod hooks, {count("patch")} Harmony patches, " +
                $"{count("handler")} event handlers); {added} added in {clock.ElapsedMilliseconds} ms" +
                (hooks.Length == 0 ? "; no mod hook was seen being added, so hooks are not timed (another mod may have replaced the listener)" : "") +
                (skipCount > 0 ? $"; {skipCount} can't be timed (generated, generic or framework code: their time counts toward the method that calls them)" : "") +
                (failCount > 0 ? $"; {failCount} refused the timer and were left alone" : "") +
                (cut ? "; out of time for this pass, the rest are added at the next stage" : "") +
                (capHit ? $"; the limit of {MaxTimed} is reached, the rest are not timed" : ""));
        }

        /// <summary>
        /// Puts the timer on one method, once. A method that can't take it is remembered and never tried again. A method
        /// that already carries it and turns up on another game method or event has that one added to where it sits.
        /// </summary>
        static void Time(MethodBase raw, string path, string kind, System.Diagnostics.Stopwatch clock)
        {
            var m = Canon(raw);
            if (stopped || m == null) return;
            if (byMethod.TryGetValue(m, out var known)) { known.AlsoOn(path); return; }
            if (refused.Contains(m)) return;
            string why = WhyNot(m);
            if (why != null)
            {
                refused.Add(m);
                if (why != Own)
                {
                    skipCount++;
                    CompatShieldPatcher.Log.LogDebug($"Hook timer: not timing {LabelOf(m)} on {path}: {why}");
                }
                return;
            }
            if (kind != "game")     // the game methods are few and every report needs them, so the limits never drop them
            {
                if (byMethod.Count >= MaxTimed) { capHit = true; return; }
                if (clock != null && clock.Elapsed.TotalMilliseconds > PassBudgetMs) { cut = true; return; }
            }
            try
            {
                harmony.Patch(m, prefix: Patch(nameof(Enter), Priority.First), postfix: Patch(nameof(Exit), Priority.Last));
            }
            catch (Exception e)
            {
                // Harmony registers a patch before it compiles it, so a failed one is taken back off.
                try { harmony.Unpatch(m, HarmonyPatchType.All, HarmonyId); } catch { }
                refused.Add(m);
                failCount++;
                if (Stripped(m))
                {
                    // Harmony rebuilds the method through MonoMod, which first drops the built version and then runs every
                    // mod's IL hook on it again. One of them failed on the second run, so the method is back to plain game code.
                    stopped = true;
                    Running = false;
                    CompatShieldPatcher.Log.LogWarning($"Hook timer: adding the timer to {LabelOf(m)} failed, and that left the method without the changes other mods make to it. " +
                        $"Restart the game before playing on, and set HookTimer = false if this repeats. The timer is off for this session ({e.GetType().Name}: {e.Message})");
                    return;
                }
                CompatShieldPatcher.Log.LogDebug($"Hook timer: {LabelOf(m)} refused the timer: {e.GetType().Name}: {e.Message}");
                return;
            }
            var entry = new Entry(LabelOf(m), kind == "game" ? GameBucket : ModOf(m), path, kind);
            byMethod[m] = entry;
            entries.Add(entry);
        }

        /// <summary>Why a method is left alone, or null when it can carry the timer.</summary>
        internal static string WhyNot(MethodBase m)
        {
            if (m == null) return "no method";
            if (!(m is MethodInfo)) return "a constructor";
            var t = m.DeclaringType;
            if (t == null || m.GetType().Name.IndexOf("Dynamic", StringComparison.Ordinal) >= 0) return "generated at run time";
            string asm = t.Assembly.GetName().Name ?? "";
            if (t.Assembly == typeof(HookTimer).Assembly && (t.Name.StartsWith("HookTimer", StringComparison.Ordinal) || t.Name.StartsWith("LagTracer", StringComparison.Ordinal))) return Own;
            if (t.Assembly.IsDynamic || asm.StartsWith("DMDASM", StringComparison.Ordinal) || t.Name.StartsWith("DMD<", StringComparison.Ordinal)) return "generated at run time";
            foreach (string f in Framework)
                if (asm == f || asm.StartsWith(f + ".", StringComparison.Ordinal)) return "framework code";
            if (m.IsAbstract) return "abstract";
            if (m.IsGenericMethod || m.ContainsGenericParameters || t.IsGenericType) return "generic";
            const System.Reflection.MethodImplAttributes noBody = System.Reflection.MethodImplAttributes.InternalCall
                | System.Reflection.MethodImplAttributes.Native | System.Reflection.MethodImplAttributes.Runtime;
            if ((m.MethodImplementationFlags & noBody) != 0 || (m.Attributes & System.Reflection.MethodAttributes.PinvokeImpl) != 0) return "no managed body";
            try { if (m.GetMethodBody() == null) return "no managed body"; }
            catch { return "unreadable body"; }
            return null;
        }

        // Timing the code the timer and the hooks themselves run on would be a loop waiting to happen.
        static readonly string[] Framework = { "0Harmony", "MonoMod", "Mono.Cecil", "BepInEx", "mscorlib", "netstandard", "System", "UnityEngine", "Unity" };

        /// <summary>The one MethodBase Harmony hands back as __originalMethod: the method as its declaring type sees it.</summary>
        internal static MethodBase Canon(MethodBase m)
        {
            try
            {
                if (m != null && m.DeclaringType != null && !m.DeclaringType.IsGenericType && !m.GetType().Name.Contains("Dynamic"))
                    return MethodBase.GetMethodFromHandle(m.MethodHandle, m.DeclaringType.TypeHandle) ?? m;
            }
            catch { }
            return m;
        }

        static IEnumerable<MethodBase> Resolve(string key)
        {
            int at = key.IndexOf("::", StringComparison.Ordinal);
            var type = FindType("RoR2", key.Substring(0, at));
            if (type == null) return Enumerable.Empty<MethodBase>();
            string name = key.Substring(at + 2);
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            return type.GetMethods(all).Where(m => m.Name == name).Cast<MethodBase>().ToList();
        }

        static Type FindType(string asm, string type)
        {
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                    if (a.GetName().Name == asm) { var t = a.GetType(type); if (t != null) return t; }
            }
            catch { }
            return null;
        }

        /// <summary>"CharacterBody.RecalculateStats" from "RoR2.CharacterBody::RecalculateStats".</summary>
        internal static string Short(string key)
        {
            int at = key.IndexOf("::", StringComparison.Ordinal);
            string type = key.Substring(0, at);
            return type.Substring(type.LastIndexOf('.') + 1) + "." + key.Substring(at + 2);
        }

        static IEnumerable<MethodBase> PatchMethods(MethodBase original)
        {
            var found = new List<MethodBase>();
            try
            {
                var info = Harmony.GetPatchInfo(original);
                if (info == null) return found;
                foreach (var p in info.Prefixes.Concat(info.Postfixes).Concat(info.Finalizers))
                    if (p != null && p.owner != HarmonyId && p.PatchMethod != null) found.Add(p.PatchMethod);
            }
            catch { }
            return found;
        }

        /// <summary>The methods subscribed to one static event right now, the game's own left out.</summary>
        internal static List<MethodBase> Handlers(string[] ev)
        {
            var found = new List<MethodBase>();
            try
            {
                const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                var type = FindType(ev[0], ev[1]);
                var field = type?.GetField(ev[2], any) ?? type?.GetField("_" + ev[2], any);
                if (!(field?.GetValue(null) is Delegate d)) return found;
                foreach (var one in d.GetInvocationList())
                {
                    var m = one?.Method;
                    if (m?.DeclaringType != null && m.DeclaringType.Assembly.GetName().Name != "RoR2") found.Add(m);
                }
            }
            catch { }
            return found;
        }

        /// <summary>Which mods edit this game method's IL: their time can't be split from the game's, so the report names them.</summary>
        static void NoteIlOwners(MethodBase original, string path)
        {
            try
            {
                var map = typeof(MonoMod.RuntimeDetour.ILHook).GetField("_Map", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as System.Collections.IDictionary;
                object ctx = map != null && map.Contains(original) ? map[original] : null;
                var chain = ctx?.GetType().GetField("Chain", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(ctx) as System.Collections.IEnumerable;
                foreach (object o in chain ?? new object[0])
                {
                    string asm = (o as MonoMod.RuntimeDetour.ILHook)?.Manipulator?.Method?.DeclaringType?.Assembly.GetName().Name;
                    if (asm == null || asm == "0Harmony" || asm.StartsWith("MonoMod", StringComparison.Ordinal)) continue;
                    if (!ilOwners.TryGetValue(path, out var set)) ilOwners[path] = set = new SortedSet<string>(StringComparer.Ordinal);
                    set.Add(asm);
                }
            }
            catch { }
            try
            {
                // A Harmony transpiler edits the method's code the same way, so its owner is named too.
                var info = Harmony.GetPatchInfo(original);
                if (info == null) return;
                foreach (var t in info.Transpilers)
                {
                    string asm = t?.PatchMethod?.DeclaringType?.Assembly.GetName().Name;
                    if (asm == null) continue;
                    if (!ilOwners.TryGetValue(path, out var set)) ilOwners[path] = set = new SortedSet<string>(StringComparer.Ordinal);
                    set.Add(asm);
                }
            }
            catch { }
        }

        /// <summary>
        /// True when MonoMod's rebuild of a method stopped half way: its IL hooks and Harmony patches are still registered but
        /// none is in effect (the built version was dropped and the new one never made). Only read after a failed patch.
        /// </summary>
        internal static bool Stripped(MethodBase m)
        {
            try
            {
                const BindingFlags inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var map = typeof(MonoMod.RuntimeDetour.ILHook).GetField("_Map", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as System.Collections.IDictionary;
                if (map == null || m == null) return false;
                object ctx;
                lock (map) { if (!map.Contains(m)) return false; ctx = map[m]; }
                if (ctx == null) return false;
                var built = ctx.GetType().GetField("Detour", inst);
                if (built == null || built.GetValue(ctx) != null) return false;
                if (!(ctx.GetType().GetField("Chain", inst)?.GetValue(ctx) is System.Collections.IEnumerable chain)) return false;
                foreach (object o in chain)
                    if (o is MonoMod.RuntimeDetour.ILHook hook && hook.IsApplied) return true;
                return false;
            }
            catch { return false; }
        }

        /// <summary>"Mod:Type.Method"; a lambda's compiler-made holder is replaced by its outer type and method.</summary>
        internal static string LabelOf(MethodBase m)
        {
            var type = m.DeclaringType;
            if (type == null) return m.Name;
            string typeName = type.Name;
            if (typeName.StartsWith("<", StringComparison.Ordinal) && type.DeclaringType != null) typeName = type.DeclaringType.Name;
            string method = m.Name;
            int close = method.IndexOf('>');
            if (method.StartsWith("<", StringComparison.Ordinal) && close > 1) method = method.Substring(1, close - 1);
            return $"{type.Assembly.GetName().Name}:{typeName}.{method}";
        }

        static string ModOf(MethodBase m) => m.DeclaringType?.Assembly.GetName().Name ?? "?";

        // ---------------------------------------------------------------- what the timer itself costs

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static int Probe(int x) { return x + 1; }

        /// <summary>
        /// Nanoseconds one timed call adds, measured on a method of our own with and without the timer; -1 if it can't be
        /// measured. The timed run also shows how much of that lands inside the call's own measurement (the rest lands in its
        /// caller), which is what the reports take out. Measured after the first pass, so the lookup table is at its real size.
        /// </summary>
        internal static double Calibrate()
        {
            var m = typeof(HookTimer).GetMethod(nameof(Probe), BindingFlags.NonPublic | BindingFlags.Static);
            const int n = 20000, rounds = 5;
            int v = 0;
            bool was = Running, patched = false;
            int thread = mainThread;
            var probe = new Entry("probe", "probe", "probe", "probe") { Warm = true };   // its first call must not land in the run's first-call sum
            try
            {
                if (harmony == null) harmony = new Harmony(HarmonyId);
                for (int i = 0; i < 2000; i++) v = Probe(v);
                // Five rounds each way and the middle one kept, so one garbage collection or a busy moment can't set the number.
                var plains = new List<double>();
                for (int r = 0; r < rounds; r++)
                {
                    long t0 = Now();
                    for (int i = 0; i < n; i++) v = Probe(v);
                    plains.Add(Now() - t0);
                }
                double plain = Middle(plains);
                harmony.Patch(m, prefix: Patch(nameof(Enter), Priority.First), postfix: Patch(nameof(Exit), Priority.Last));
                patched = true;
                byMethod[m] = probe;
                mainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
                Running = true;
                depth = 0;
                for (int i = 0; i < 2000; i++) v = Probe(v);
                var totals = new List<double>();
                var insides = new List<double>();
                for (int r = 0; r < rounds; r++)
                {
                    depth = 0;
                    Open();     // a caller's level, as in the game: each timed call also hands its time up to the call it was made from
                    probe.Calls = probe.Ticks = 0;
                    long t0 = Now();
                    for (int i = 0; i < n; i++) v = Probe(v);
                    long timed = Now() - t0;
                    totals.Add(Math.Max(0, (timed - plain) / n));
                    insides.Add(probe.Calls > 0 ? probe.Ticks / (double)probe.Calls : 0);
                }
                double total = Middle(totals);
                double inside = Math.Min(total, Middle(insides));
                insideTicks = inside;
                outsideTicks = total - inside;
                return total * 1e9 / System.Diagnostics.Stopwatch.Frequency;
            }
            catch
            {
                insideTicks = outsideTicks = 0;
                return -1;
            }
            finally
            {
                Running = was;
                mainThread = thread;
                depth = 0;
                byMethod.Remove(m);
                if (patched) { try { harmony.Unpatch(m, HarmonyPatchType.All, HarmonyId); } catch { } }
            }
        }

        // ---------------------------------------------------------------- reports

        internal static double Middle(List<double> values) { values.Sort(); return values[values.Count / 2]; }
        static long Now() => System.Diagnostics.Stopwatch.GetTimestamp();
        static double Seconds(long from, long to) => (to - from) / (double)System.Diagnostics.Stopwatch.Frequency;
        internal static double Ms(double ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        static string SafeScene() { try { return Scene() ?? "?"; } catch { return "?"; } }

        /// <summary>Own time with the timer's cost taken out: the part inside each of its calls and the part each timed call it made left behind.</summary>
        internal static double Net(long ticks, long calls, long made)
        {
            double v = ticks - calls * insideTicks - made * outsideTicks;
            return v > 0 ? v : 0;
        }

        static void ResetRun()
        {
            foreach (var e in entries) e.Calls = e.Ticks = e.Max = e.Kids = e.StageCalls = e.StageTicks = e.StageMax = e.StageKids = 0;
            misses = 0;
            depth = 0;
            stagePart = 0;
            firstCalls = firstTicks = stageFirstCalls = stageFirstTicks = 0;
            runStart = stageStart = Now();
        }

        /// <summary>Logs the stage so far and starts its counters again. Under 5 s or with nothing timed it only restarts them.</summary>
        static void EmitStage(bool stageEnd)
        {
            long now = Now();
            double secs = Seconds(stageStart, now);
            if (secs >= 5 && entries.Any(e => e.StageCalls > 0))
            {
                stagePart++;
                string title = "stage " + stageName + (stagePart > 1 || !stageEnd ? " part " + stagePart : "");
                foreach (var line in Report(title, secs, true)) CompatShieldPatcher.Log.LogInfo(line);
            }
            foreach (var e in entries) e.StageCalls = e.StageTicks = e.StageMax = e.StageKids = 0;
            stageFirstCalls = stageFirstTicks = 0;
            stageStart = now;
        }

        /// <summary>
        /// The report lines for a stage (its counters) or the whole run: the total, mods ranked by their own time, the slowest
        /// single hooks, and for the whole run the watched game methods with the split between game code and mods.
        /// </summary>
        internal static List<string> Report(string title, double seconds, bool stage)
        {
            var lines = new List<string>();
            Func<Entry, long> C = e => stage ? e.StageCalls : e.Calls;
            Func<Entry, long> M = e => stage ? e.StageMax : e.Max;
            Func<Entry, double> T = e => stage ? Net(e.StageTicks, e.StageCalls, e.StageKids) : Net(e.Ticks, e.Calls, e.Kids);
            var live = entries.Where(e => C(e) > 0).ToList();
            if (live.Count == 0 || seconds <= 0)
            {
                lines.Add($"Hook timer: {title}: nothing was timed");
                return lines;
            }
            double total = live.Sum(T);
            long calls = live.Sum(C);
            double totalMs = Ms(total), costMs = Ms(calls * (insideTicks + outsideTicks));
            long firsts = stage ? stageFirstCalls : firstCalls;
            double firstMs = Ms(stage ? stageFirstTicks : firstTicks);
            lines.Add($"Hook timer: {title}, {seconds:0} s: {totalMs:0} ms inside the watched game methods and events ({totalMs / seconds:0.0} ms each second, {totalMs / (seconds * 10):0.0}% of the time) " +
                $"over {calls} timed calls" + (overheadNs >= 0 ? $"; the timer itself added about {costMs:0} ms on top, already taken out" : "") +
                (firsts > 0 ? $"; {firsts} first calls ({firstMs:0} ms, mostly compiling the method once with the timer in it) are left out of every number" : "") +
                ". Each line is that code's own time, calls it makes into other timed code left out");
            int topMods = stage ? 8 : 12, topHooks = stage ? 8 : 15;
            int rank = 0;
            foreach (var g in live.GroupBy(e => e.Mod).Select(g => new { Mod = g.Key, Ticks = g.Sum(T), Calls = g.Sum(C), Max = g.Max(M) }).OrderByDescending(g => g.Ticks).Take(topMods))
                lines.Add($"Hook timer:   mod {++rank}. {g.Mod}: {Ms(g.Ticks):0} ms ({Ms(g.Ticks) / seconds:0.00} ms each second, {(total > 0 ? 100.0 * g.Ticks / total : 0):0}% of the total), " +
                    $"{g.Calls} calls, slowest call {Ms(g.Max):0.00} ms");
            rank = 0;
            foreach (var e in live.Where(e => e.Kind != "game").OrderByDescending(T).Take(topHooks))
                lines.Add($"Hook timer:   hook {++rank}. {e.Label} ({e.Kind} on {e.Where()}): {Ms(T(e)):0} ms, {Ms(T(e)) / C(e):0.0000} ms a call, {C(e)} calls, slowest {Ms(M(e)):0.00} ms");
            if (stage) return lines;
            rank = 0;
            // A hook or patch method that sits on several game methods has one count for all of them, so it can't be put
            // under any one method: it is left out of the method lines and summed on a line of its own.
            foreach (var g in live.Where(e => e.Kind != "handler" && e.Also == null).GroupBy(e => e.Path)
                .Select(g => new { Path = g.Key, Ticks = g.Sum(T), Game = g.Where(e => e.Kind == "game").Sum(T), Calls = g.Where(e => e.Kind == "game").Sum(C) })
                .OrderByDescending(g => g.Ticks).Take(8))
            {
                string il = ilOwners.TryGetValue(g.Path, out var set) && set.Count > 0 ? "; IL hooks (counted as game code) by " + string.Join(", ", set) : "";
                lines.Add($"Hook timer:   method {++rank}. {g.Path}: {Ms(g.Ticks):0} ms in {g.Calls} calls ({(g.Calls > 0 ? Ms(g.Ticks) / g.Calls : 0):0.0000} ms a call), " +
                    $"game code {Ms(g.Game):0} ms, mod hooks and patches {Ms(g.Ticks - g.Game):0} ms{il}");
            }
            var shared = live.Where(e => e.Kind != "handler" && e.Also != null).OrderByDescending(T).ToList();
            if (shared.Count > 0)
                lines.Add($"Hook timer:   not in the method lines: {Ms(shared.Sum(T)):0} ms in " +
                    (shared.Count == 1 ? "1 hook or patch method that sits" : $"{shared.Count} hook and patch methods that each sit") + " on more than one game method, so the time can't be split between them (" +
                    string.Join(", ", shared.Take(3).Select(e => $"{e.Label} {Ms(T(e)):0} ms")) + (shared.Count > 3 ? ", ..." : "") + ")");
            return lines;
        }

        // ---------------------------------------------------------------- test seams

        internal static void ResetForTest()
        {
            Running = false;
            depth = 0; misses = 0; skipCount = 0; failCount = 0; stagePart = 0; frames = 0;
            cut = capHit = saidStart = stopped = false;
            overheadNs = -1; insideTicks = outsideTicks = 0;
            firstCalls = firstTicks = stageFirstCalls = stageFirstTicks = 0;
            stageName = "?";
            byMethod.Clear(); entries.Clear(); refused.Clear(); ilOwners.Clear();
            mainThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            if (harmony == null) harmony = new Harmony(HarmonyId);
            runStart = stageStart = Now();
        }

        internal static bool TimeForTest(MethodBase m, string path, string kind) { Time(m, path, kind, null); return byMethod.ContainsKey(Canon(m)); }
        internal static void UntimeForTest(MethodBase m) { try { harmony.Unpatch(Canon(m), HarmonyPatchType.All, HarmonyId); } catch { } byMethod.Remove(Canon(m)); }
        internal static Entry EntryOf(MethodBase m) { return byMethod.TryGetValue(Canon(m), out var e) ? e : null; }
        /// <summary>A stand-in entry whose first call is already behind it; ColdForTest puts that call back.</summary>
        internal static Entry AddEntryForTest(MethodBase m, string label, string mod, string path, string kind)
        {
            var e = new Entry(label, mod, path, kind) { Warm = true };
            byMethod[m] = e; entries.Add(e);
            return e;
        }
        internal static void ColdForTest(Entry e) { e.Warm = false; }
        internal static void AlsoForTest(Entry e, string path) { e.AlsoOn(path); }
        internal static long[] FirstsForTest => new[] { firstCalls, firstTicks, stageFirstCalls, stageFirstTicks };
        internal static void StageEndForTest() { EmitStage(true); }
        internal static void CountForTest(Entry e, long calls, long ticks, long max, long made)
        {
            e.Calls = e.StageCalls = calls; e.Ticks = e.StageTicks = ticks; e.Max = e.StageMax = max; e.Kids = e.StageKids = made;
        }
        internal static void NoteIlOwnerForTest(string path, string mod)
        {
            if (!ilOwners.TryGetValue(path, out var set)) ilOwners[path] = set = new SortedSet<string>(StringComparer.Ordinal);
            set.Add(mod);
        }
        internal static void SetCostForTest(double inside, double outside)
        {
            insideTicks = inside; outsideTicks = outside;
            overheadNs = (inside + outside) * 1e9 / System.Diagnostics.Stopwatch.Frequency;
        }
        internal static int DepthForTest { get { return depth; } set { depth = value; } }
        internal static long MissesForTest => misses;
        internal static bool StoppedForTest => stopped;
        internal static string IlOwnersForTest(MethodBase original, string path)
        {
            NoteIlOwners(original, path);
            return ilOwners.TryGetValue(path, out var set) ? string.Join(", ", set) : "";
        }
        internal static double InsideForTest => insideTicks;
        internal static double OutsideForTest => outsideTicks;
        internal static void FrameForTest() { OnFrame(); }
    }

    /// <summary>
    /// [4. Lag and Hang Protection] PhysicsCatchUpCap: when a frame runs long, Unity runs extra FixedUpdate steps to catch game time up, up to
    /// maximumDeltaTime / fixedDeltaTime = 20 steps per frame at the game's default 0.333 s. Each step ticks every body, AI and
    /// state machine (the lag tracer measured those at ~15 ms per frame with 37 monsters), so a slow frame makes the next one slower:
    /// every laggy window on 2026-10-19 ran 3-6 physics steps per frame. Capping maximumDeltaTime at 0.1 s allows at most 6 steps
    /// per frame; under heavy load game time slows a little instead of the frame rate collapsing. Set at each run start, on the main
    /// thread, never on the load path. Host and clients each apply their own.
    /// </summary>
    public static class PhysicsCatchUpCap
    {
        internal static float Cap;
        // Swappable for the harness (no Unity there).
        internal static Action<float> SetMax = v => Time.maximumDeltaTime = v;
        internal static Func<float> GetMax = () => Time.maximumDeltaTime;
        internal static Func<float> FixedStep = () => Time.fixedDeltaTime;
        static bool hooked;
        internal static int Applied;   // how many run starts set it (read by the harness)
        static bool logged;

        public static void Apply(float cap)
        {
            Cap = cap;
            if (cap <= 0f || hooked) return;
            hooked = true;
            try
            {
                var run = Type.GetType("RoR2.Run, RoR2");
                var ev = run?.GetEvent("onRunStartGlobal", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) throw new MissingMemberException("RoR2.Run.onRunStartGlobal not found");
                ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(PhysicsCatchUpCap).GetMethod(nameof(OnRunStart), BindingFlags.NonPublic | BindingFlags.Static)));
                CompatShieldPatcher.Log.LogInfo($"Physics catch-up cap: from each run start, a frame runs at most {cap:0.00} s of physics catch-up (Time.maximumDeltaTime), " +
                    "so one slow frame can't stack up to 20 physics steps on the next");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Physics catch-up cap: can't watch run starts, not applied: " + e.Message);
            }
        }

        static void OnRunStart(object run)
        {
            try { ApplyNow(); }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Physics catch-up cap: couldn't set it this run: " + e.Message); }
        }

        internal static void ApplyNow()
        {
            if (Cap <= 0f) return;
            float before = GetMax();
            SetMax(Cap);
            Applied++;
            if (logged) return;
            logged = true;
            float step = FixedStep();
            int steps = step > 0f ? (int)Math.Floor(Cap / step + 0.0001f) : 0;
            CompatShieldPatcher.Log.LogInfo($"Physics catch-up: maximumDeltaTime set to {Cap:0.00} s (at most {steps} physics steps per frame; the game had {before:0.00} s)");
        }

        internal static void ResetForTest() { Cap = 0f; hooked = false; logged = false; Applied = 0; }
    }

    /// <summary>
    /// Caps how often one character redoes its full stat calculation. CharacterBody.FixedUpdate runs RecalculateStats on any physics tick
    /// that finds statsDirty set, and each run costs about 1.25 ms with this pack's stat hooks. Aetherium's Shielding Core sets the flag
    /// whenever a shield crosses zero, and buff wards refresh timed buffs on everything in range, so a stage with few enemies spent 3.5 s of
    /// every 10 s in there (13 fps, 2848 runs). The gate sits on that one read of the flag. The first change after a quiet spell applies at
    /// once; repeats inside the interval leave the flag set and are picked up at most one interval later, so no change is lost. Direct
    /// RecalculateStats calls (sprint toggle, Start) are untouched. Each PC limits its own copies of the bodies.
    /// </summary>
    public static class StatUpdateLimit
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal const int TopNames = 5;

        sealed class Entry { public float next; public int waits; public string name; }
        // By reference: UnityEngine.Object overrides Equals with a native call, and this lookup runs on every held tick.
        sealed class SameObject : IEqualityComparer<object>
        {
            bool IEqualityComparer<object>.Equals(object a, object b) => ReferenceEquals(a, b);
            int IEqualityComparer<object>.GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }

        static readonly Dictionary<object, Entry> bodies = new Dictionary<object, Entry>(new SameObject());
        static float interval;
        static long ran, waited;
        static bool patched, rewritten;
        static string problem;
        static Harmony patcher;
        static Assembly game;

        // Swappable, because the harness has no Unity clock or objects.
        internal static Func<float> Clock = () => Time.fixedTime;
        internal static Func<object, string> NameOf = o => (o as UnityEngine.Object)?.name ?? "?";

        public static void Apply(Harmony h, Assembly ror2, float seconds)
        {
            interval = seconds;
            if (seconds <= 0f || patcher != null) return;
            try
            {
                patcher = h;
                game = ror2;
                var ev = ror2.GetType("RoR2.Run")?.GetEvent("onRunStartGlobal", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) throw new MissingMemberException("RoR2.Run.onRunStartGlobal not found");
                ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(StatUpdateLimit).GetMethod(nameof(OnRunStart), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Stat limit: can't watch run starts, not applied: " + e.Message);
            }
        }

        static void OnRunStart(object run)
        {
            if (patched) return;
            patched = true;
            try
            {
                var fixedUpdate = game.GetType("RoR2.CharacterBody")?.GetMethod("FixedUpdate", Any);
                if (fixedUpdate == null) throw new MissingMethodException("RoR2.CharacterBody.FixedUpdate not found");
                patcher.Patch(fixedUpdate, transpiler: new HarmonyMethod(typeof(StatUpdateLimit).GetMethod(nameof(Transpiler), Any)));
                if (!rewritten)
                {
                    CompatShieldPatcher.Log.LogWarning("Stat limit: CharacterBody.FixedUpdate isn't the expected shape, not applied: " + problem);
                    return;
                }
                WatchStageEnds();
                CompatShieldPatcher.Log.LogInfo($"Stat limit: one character's automatic stat update now runs at most once every {interval:0.###} s; a single change still applies at once");
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Stat limit: can't patch CharacterBody.FixedUpdate, not applied: " + e.Message); }
        }

        // The stage line is only a report, so a missing event costs the line and nothing else.
        static void WatchStageEnds()
        {
            var handler = typeof(StatUpdateLimit).GetMethod(nameof(OnStageEdge), BindingFlags.NonPublic | BindingFlags.Static);
            foreach (var (type, name) in new[] { ("RoR2.Stage", "onStageStartGlobal"), ("RoR2.Run", "onRunDestroyGlobal") })
            {
                try
                {
                    var ev = game.GetType(type)?.GetEvent(name, BindingFlags.Public | BindingFlags.Static);
                    if (ev == null) { CompatShieldPatcher.Log.LogWarning($"Stat limit: {type}.{name} not found, no stage line from it"); continue; }
                    ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, handler));
                }
                catch (Exception e) { CompatShieldPatcher.Log.LogWarning($"Stat limit: can't watch {type}.{name}, no stage line from it: " + e.Message); }
            }
        }

        static void OnStageEdge(object source)
        {
            try { EndStage(); }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Stat limit: couldn't write the stage line: " + e.Message); }
        }

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var body = original.DeclaringType;
            var result = Rewrite(instructions.ToList(), body.GetField("statsDirty", Any), body.GetMethod("RecalculateStats", Any),
                typeof(StatUpdateLimit).GetMethod(nameof(Gate), Any), out problem);
            rewritten = problem == null;
            return result;
        }

        /// <summary>After the one read of the flag, adds "ldarg.0; call Gate", which is all it takes: Gate gets the flag and the body and leaves the answer for the branch.</summary>
        internal static List<CodeInstruction> Rewrite(List<CodeInstruction> code, FieldInfo dirty, MethodInfo recalc, MethodInfo gate, out string why)
        {
            why = null;
            if (dirty == null || recalc == null || gate == null) { why = "statsDirty or RecalculateStats is missing"; return code; }
            int at = -1, reads = 0;
            for (int i = 0; i < code.Count; i++)
                if (code[i].opcode == OpCodes.Ldfld && Equals(code[i].operand, dirty)) { at = i; reads++; }
            if (reads != 1) { why = $"expected one read of statsDirty, found {reads}"; return code; }
            bool shape = at + 3 < code.Count && (code[at + 1].opcode == OpCodes.Brfalse || code[at + 1].opcode == OpCodes.Brfalse_S)
                && code[at + 2].opcode == OpCodes.Ldarg_0 && (code[at + 3].opcode == OpCodes.Call || code[at + 3].opcode == OpCodes.Callvirt) && Equals(code[at + 3].operand, recalc);
            if (!shape) { why = "the read isn't followed by a branch and a RecalculateStats call"; return code; }
            code.Insert(at + 1, new CodeInstruction(OpCodes.Ldarg_0));
            code.Insert(at + 2, new CodeInstruction(OpCodes.Call, gate));
            return code;
        }

        /// <summary>Called from FixedUpdate in place of the plain flag check. False leaves the flag set, so the next tick asks again.</summary>
        public static bool Gate(bool dirty, object body)
        {
            if (!dirty) return false;
            try
            {
                float now = Clock();
                if (!bodies.TryGetValue(body, out var e))
                {
                    bodies[body] = new Entry { next = now + interval };
                    ran++;
                    return true;
                }
                if (now >= e.next)
                {
                    e.next = now + interval;
                    ran++;
                    return true;
                }
                waited++;
                e.waits++;
                if (e.name == null) e.name = NameOf(body);
                return false;
            }
            catch { return true; }   // never the reason a body stops updating
        }

        internal static string Summary()
        {
            if (ran == 0 && waited == 0) return null;
            var names = new Dictionary<string, long>();
            foreach (var e in bodies.Values)
            {
                if (e.waits == 0) continue;
                string n = (e.name ?? "?").Replace("(Clone)", "");
                names.TryGetValue(n, out long c);
                names[n] = c + e.waits;
            }
            string top = string.Join(", ", names.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Take(TopNames).Select(p => p.Key + " " + p.Value));
            return $"Stat limit: last stage ran {ran} automatic stat updates; physics ticks that waited: {waited}" + (top.Length > 0 ? ", most waiting: " + top : "");
        }

        internal static void EndStage()
        {
            string line = Summary();
            if (line != null) CompatShieldPatcher.Log.LogInfo(line);
            bodies.Clear();
            ran = waited = 0;
        }

        internal static void ResetForTest(float seconds) { interval = seconds; bodies.Clear(); ran = waited = 0; patched = rewritten = false; problem = null; patcher = null; game = null; }
    }

    /// <summary>
    /// ShareSuite 2.12.0 replaces the stage interactable credits in its PlaceTeleporter hook and divides them by _sacrificeOffset,
    /// which its Sacrifice hook sets to 2. That made up for older game code that halved the credits before PlaceTeleporter.
    /// Game 1.5.0 halves them after PlaceTeleporter (onPopulateCreditMultiplier), so both halvings land and a Sacrifice stage
    /// spends a quarter of its credits. Fix: put the offset back to 1 as soon as the Sacrifice hook sets it, leaving only the
    /// game halving. Stages whose SceneDef opts out of Sacrifice scaling get their full credits, as the game means them to.
    /// The patched method touches only its own field and the orig delegate, so compiling it at load runs no game static
    /// constructor (the PlaceTeleporter hook itself would be riskier). SceneDirector runs on the host, so only the host needs this.
    /// </summary>
    public static class ShareSuiteSacrificeCreditFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        internal const string HooksType = "ShareSuite.GeneralHooks";
        static FieldInfo offsetField;
        static bool announced;

        public static void Apply(Harmony h, Assembly asm)
        {
            try
            {
                var hooks = asm.GetType(HooksType);
                var setter = hooks?.GetMethod("SetSacrificeOffset", Any);
                var field = hooks?.GetField("_sacrificeOffset", Any);
                if (setter == null || field == null || field.FieldType != typeof(int))
                {
                    CompatShieldPatcher.Log.LogWarning("ShareSuite Sacrifice credit fix: GeneralHooks._sacrificeOffset or SetSacrificeOffset not found (mod updated?), not applied");
                    return;
                }
                offsetField = field;
                h.Patch(setter, postfix: new HarmonyMethod(typeof(ShareSuiteSacrificeCreditFix).GetMethod(nameof(ResetOffset), BindingFlags.NonPublic | BindingFlags.Static)));
                CompatShieldPatcher.Log.LogInfo("ShareSuite: its extra Sacrifice halving of stage interactable credits is off, so only the game halves them");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("ShareSuite Sacrifice credit fix failed to apply: " + e);
            }
        }

        // Runs inside the event that populates the stage, so it must never throw.
        static void ResetOffset()
        {
            try
            {
                offsetField.SetValue(null, 1);
                if (announced) return;
                announced = true;
                CompatShieldPatcher.Log.LogInfo("ShareSuite: cancelled its extra Sacrifice credit halving on this stage (logged once per launch)");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("ShareSuite Sacrifice credit fix: could not reset the offset: " + e.Message);
            }
        }
    }

    /// <summary>
    /// EnemiesReturns 0.7.29, Swift.DeathState: the first Swift to die loads the vanilla BeetleGuardDeathImpact prefab with
    /// Addressables.LoadAssetAsync, keeps it in a static (impactEffect), and in the load's Completed callback spawns it when
    /// the bird is on the ground, then calls Addressables.Release on the handle. On game 1.5.0 that spawn is the run's first
    /// use of effect 15 (BeetleGuardDeathImpact, a baked EffectDef that loads its prefab on demand through
    /// AssetAsyncReferenceManager, which shares the same Addressables operation). Its AssetOrDirectReference.WaitForCompletion
    /// pumps ResourceManager.Update, which runs that operation's deferred completion while the first completion is still
    /// running, so Swift's callback runs a second time, nested, and the handle is released twice. The operation is freed
    /// while the game's reference manager still lists it. From then on, for the rest of the session, effect 15's prefab is
    /// null: every Swift corpse throws "The Object you want to instantiate is null" each physics tick (FixedUpdate sets
    /// wasGrounded only after the spawn), vanilla Beetle Guards lose their death-slam effect (Death2 animation event), and the
    /// run-end reference dump throws "Attempting to use an invalid operation handle". That throw leaves the stale entry in
    /// place, leaks every asset the dump hadn't reached, and aborts Run.OnDestroy: the stage object stays, and Stage.Update
    /// then throws a NullReferenceException every 2 s.
    /// Fix: drop that Release, so Swift keeps the handle for the prefab it keeps in a static and the operation can't be freed
    /// under the game; and set wasGrounded before the landing spawn, so a broken effect throws once per corpse, not per tick.
    /// Swift's states run on every player, so each player needs this for their own game.
    /// </summary>
    public static class SwiftImpactFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal const string StateType = "EnemiesReturns.ModdedEntityStates.Swift.DeathState";
        // Labels are handled as boxed objects: the netstandard2.1 build can't name Harmony's Label type (CS7069).
        static readonly FieldInfo labelsField = typeof(CodeInstruction).GetField("labels");
        internal static string releaseWhy, landWhy;   // null once that part is patched

        public static void Apply(Harmony h, Assembly asm)
        {
            try
            {
                var state = asm.GetType(StateType);
                if (state == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Swift impact fix: EnemiesReturns' Swift DeathState not found (mod updated?), not applied");
                    return;
                }
                var callback = FindCallback(state);
                if (callback == null)
                    CompatShieldPatcher.Log.LogWarning("Swift impact fix: the impact-effect load callback (with its one Addressables.Release) not found (mod updated?), release part not applied");
                else
                {
                    releaseWhy = "unexpected IL";
                    h.Patch(callback, transpiler: new HarmonyMethod(typeof(SwiftImpactFix).GetMethod(nameof(KeepHandle), BindingFlags.NonPublic | BindingFlags.Static)));
                    if (releaseWhy == null)
                        CompatShieldPatcher.Log.LogInfo("Swift: its impact-effect load keeps its handle, so the Beetle Guard impact effect can't lose its prefab mid-session");
                    else
                        CompatShieldPatcher.Log.LogWarning($"Swift impact fix: {releaseWhy} in the impact-effect load callback, release part inactive");
                }
                var fixedUpdate = state.GetMethod("FixedUpdate", Any | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (fixedUpdate == null)
                    CompatShieldPatcher.Log.LogWarning("Swift impact fix: DeathState.FixedUpdate not found (mod updated?), landing part not applied");
                else
                {
                    landWhy = "unexpected IL";
                    h.Patch(fixedUpdate, transpiler: new HarmonyMethod(typeof(SwiftImpactFix).GetMethod(nameof(LandFirst), BindingFlags.NonPublic | BindingFlags.Static)));
                    if (landWhy == null)
                        CompatShieldPatcher.Log.LogInfo("Swift: a corpse counts as landed before its impact effect spawns, so a broken effect can't throw every tick");
                    else
                        CompatShieldPatcher.Log.LogWarning($"Swift impact fix: {landWhy} in DeathState.FixedUpdate, landing part inactive");
                }
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Swift impact fix failed to apply: " + e);
            }
        }

        // Addressables.Release<GameObject>(AsyncOperationHandle<GameObject>), the one call the callback ends with.
        internal static bool IsRelease(OpCode op, object operand) =>
            (op == OpCodes.Call || op == OpCodes.Callvirt) && operand is MethodInfo m && m.Name == "Release"
            && m.DeclaringType == typeof(Addressables) && m.IsGenericMethod && m.GetGenericArguments()[0] == typeof(GameObject)
            && m.GetParameters().Length == 1
            && m.GetParameters()[0].ParameterType == typeof(UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<GameObject>);

        // The compiler-generated Completed callback: an instance method on a nested type of DeathState that takes the
        // AsyncOperationHandle<GameObject>, stores impactEffect and calls Release exactly once. Found by shape, not by its
        // generated name, and only when exactly one method fits.
        internal static MethodInfo FindCallback(Type state)
        {
            var fits = new List<MethodInfo>();
            foreach (var t in state.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                foreach (var m in t.GetMethods(Any | BindingFlags.DeclaredOnly))
                {
                    var ps = m.GetParameters();
                    if (m.IsStatic || m.ReturnType != typeof(void) || ps.Length != 1
                        || ps[0].ParameterType != typeof(UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<GameObject>))
                        continue;
                    var body = PatchProcessor.ReadMethodBody(m).ToList();
                    if (body.Count(ci => IsRelease(ci.Key, ci.Value)) == 1
                        && body.Any(ci => ci.Key == OpCodes.Stsfld && ci.Value is FieldInfo f && f.Name == "impactEffect"))
                        fits.Add(m);
                }
            return fits.Count == 1 ? fits[0] : null;
        }

        // Release(handle) becomes pop: the same stack effect, and the instruction keeps its labels. (It also covers the
        // load-failure path, which the shipped IL merges before the Release; a vanilla GUID load doesn't fail.)
        static IEnumerable<CodeInstruction> KeepHandle(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var hits = code.FindAll(ci => IsRelease(ci.opcode, ci.operand));
            if (hits.Count != 1)
            {
                releaseWhy = hits.Count == 0 ? "no Addressables.Release call" : "more than one Addressables.Release call";
                return code;
            }
            hits[0].opcode = OpCodes.Pop;
            hits[0].operand = null;
            releaseWhy = null;
            return code;
        }

        static bool IsGroundedStore(CodeInstruction ci) =>
            ci.opcode == OpCodes.Stfld && ci.operand is FieldInfo f && f.Name == "wasGrounded" && f.FieldType == typeof(bool);

        // FixedUpdate is: if (!wasGrounded && isGrounded) { if (impactEffect) SimpleEffect(impactEffect, ...); wasGrounded = true; }
        // Insert a copy of "wasGrounded = true" in front of the impactEffect check. The original store stays (setting it twice
        // is harmless); a jump to the check moves to the new store, so every way into the spawn sets the flag first.
        static IEnumerable<CodeInstruction> LandFirst(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            int store = code.FindIndex(IsGroundedStore);
            if (store < 2 || code.Count(IsGroundedStore) != 1
                || code[store - 2].opcode != OpCodes.Ldarg_0 || code[store - 1].opcode != OpCodes.Ldc_I4_1)
            {
                landWhy = "not exactly one 'wasGrounded = true'";
                return code;
            }
            int check = code.FindIndex(ci => ci.opcode == OpCodes.Ldsfld && ci.operand is FieldInfo f && f.Name == "impactEffect");
            if (check < 0 || check > store
                || !code.Skip(check).Take(store - check).Any(ci => ci.opcode == OpCodes.Call && ci.operand is MethodInfo m && m.Name == "SimpleEffect"))
            {
                landWhy = "no impact-effect spawn before 'wasGrounded = true'";
                return code;
            }
            var first = new CodeInstruction(OpCodes.Ldarg_0);
            var moved = (System.Collections.IList)labelsField.GetValue(code[check]);
            var into = (System.Collections.IList)labelsField.GetValue(first);
            foreach (var l in moved) into.Add(l);
            moved.Clear();
            code.InsertRange(check, new[] { first, new CodeInstruction(OpCodes.Ldc_I4_1), new CodeInstruction(OpCodes.Stfld, code[store].operand) });
            landWhy = null;
            return code;
        }
    }

    /// <summary>
    /// Transpiler: split a branch-free method into statements (points where the IL stack is empty) and
    /// wrap each in try/catch, so one failing lookup is logged and skipped instead of aborting the rest.
    /// Leaves the method untouched if it contains any IL it doesn't fully understand.
    /// </summary>
    public static class SafeStatements
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var list = instructions.ToList();
            var segments = Split(list);
            if (segments == null)
            {
                CompatShieldPatcher.Log.LogWarning($"{__originalMethod.Name}: unexpected IL, left unchanged");
                return list;
            }
            var report = typeof(SafeStatements).GetMethod(nameof(Report));
            var output = new List<CodeInstruction>();
            foreach (var (start, end) in segments)
            {
                var seg = list.GetRange(start, end - start + 1);
                if (seg.All(i => i.opcode == OpCodes.Nop || i.opcode == OpCodes.Ret))
                {
                    output.AddRange(seg); // never wrap a bare ret (illegal inside try)
                    continue;
                }
                seg[0].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
                output.AddRange(seg);
                var label = new CodeInstruction(OpCodes.Ldstr, $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}: {LabelOf(seg)}");
                label.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginCatchBlock, typeof(Exception)));
                var call = new CodeInstruction(OpCodes.Call, report);
                call.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
                output.Add(label);
                output.Add(call);
            }
            return output;
        }

        public static void Report(Exception ex, string what)
        {
            CompatShieldPatcher.Log.LogWarning($"{what} failed and was skipped ({ex.GetType().Name}: {ex.Message})");
        }

        static string LabelOf(List<CodeInstruction> seg)
        {
            var last = seg.LastOrDefault(i => i.opcode != OpCodes.Nop);
            if (last != null && last.opcode == OpCodes.Stsfld && last.operand is FieldInfo f)
                return f.Name;
            var str = seg.FirstOrDefault(i => i.opcode == OpCodes.Ldstr);
            return str != null ? $"step using \"{str.operand}\"" : "step";
        }

        static List<(int, int)> Split(List<CodeInstruction> list)
        {
            var segments = new List<(int, int)>();
            int depth = 0, start = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (!StackEffect(list[i], out int pop, out int push))
                    return null;
                depth -= pop;
                if (depth < 0)
                    return null;
                depth += push;
                if (depth == 0)
                {
                    segments.Add((start, i));
                    start = i + 1;
                }
            }
            return start == list.Count ? segments : null;
        }

        static bool StackEffect(CodeInstruction ins, out int pop, out int push)
        {
            pop = push = 0;
            var op = ins.opcode;
            if (op.FlowControl == FlowControl.Branch || op.FlowControl == FlowControl.Cond_Branch || op.FlowControl == FlowControl.Throw)
                return false;
            if (op == OpCodes.Nop) return true;
            if (op == OpCodes.Ret) return true; // void methods only (checked by caller's IL shape)
            if (op == OpCodes.Ldstr || op == OpCodes.Ldsfld || op == OpCodes.Ldsflda || op == OpCodes.Ldnull
                || op == OpCodes.Ldc_I4 || op == OpCodes.Ldc_I4_S || op == OpCodes.Ldc_R4 || op == OpCodes.Ldc_R8
                || op == OpCodes.Ldc_I4_M1 || op == OpCodes.Ldc_I4_0 || op == OpCodes.Ldc_I4_1 || op == OpCodes.Ldc_I4_2
                || op == OpCodes.Ldc_I4_3 || op == OpCodes.Ldc_I4_4 || op == OpCodes.Ldc_I4_5 || op == OpCodes.Ldc_I4_6
                || op == OpCodes.Ldc_I4_7 || op == OpCodes.Ldc_I4_8 || op.Name.StartsWith("ldloc") || op.Name.StartsWith("ldarg"))
            { push = 1; return true; }
            if (op == OpCodes.Stsfld || op == OpCodes.Pop || op.Name.StartsWith("stloc") || op.Name.StartsWith("starg"))
            { pop = 1; return true; }
            if (op == OpCodes.Ldfld || op == OpCodes.Ldflda || op == OpCodes.Castclass || op == OpCodes.Isinst
                || op == OpCodes.Box || op == OpCodes.Unbox_Any || op.Name.StartsWith("conv."))
            { pop = 1; push = 1; return true; }
            if (op == OpCodes.Stfld) { pop = 2; return true; }
            if (op == OpCodes.Dup) { pop = 1; push = 2; return true; }
            if (op == OpCodes.Initobj) { pop = 1; return true; }
            if (op == OpCodes.Newobj && ins.operand is ConstructorInfo ctor)
            { pop = ctor.GetParameters().Length; push = 1; return true; }
            if ((op == OpCodes.Call || op == OpCodes.Callvirt) && ins.operand is MethodBase mb)
            {
                pop = mb.GetParameters().Length + (mb.IsStatic ? 0 : 1);
                push = mb is MethodInfo mi && mi.ReturnType != typeof(void) ? 1 : 0;
                return true;
            }
            return false;
        }
    }
    /// <summary>
    /// 1.4.43 moved the settings out of one long [Fixes] list into numbered categories and merged a few switches. BepInEx looks a
    /// setting up by section and key, so a moved setting would go back to its default and the old line would stay in the file as an
    /// orphan. This rewrites the file once, before BepInEx reads it, so saved values follow their settings. It keeps a copy of the
    /// old file. A merged switch stays on only if every switch it replaced was on.
    /// </summary>
    public static class ConfigMigration
    {
        internal static class Cat
        {
            internal const string Spawns = "1. Spawns and Enemies";
            internal const string Sacrifice = "2. Artifact of Sacrifice";
            internal const string Balance = "3. Balance";
            internal const string Lag = "4. Lag and Hang Protection";
            internal const string Errors = "5. Error and Crash Protection";
            internal const string Items = "6. Item Mod Fixes";
            internal const string Enemies = "7. Enemy and Survivor Mod Fixes";
            internal const string Diagnostics = "8. Diagnostics";
        }

        internal const string BackupSuffix = ".before-categories";

        // Old section, old key, new section, new key (blank = same key).
        internal static readonly string[][] Moves =
        {
            new[] { "Spawns", "SpawnCreditMultiplier", Cat.Spawns, "" },
            new[] { "Multitudes", "MonsterLimit", Cat.Spawns, "" },
            new[] { "Spawns", "PrinterSpawnMultiplier", Cat.Spawns, "" },
            new[] { "Spawns", "ScrapperSpawnMultiplier", Cat.Spawns, "" },
            new[] { "Spawns", "DroneSpawnMultiplier", Cat.Spawns, "" },
            new[] { "Spawns", "DroneCombinerSpawnMultiplier", Cat.Spawns, "" },
            new[] { "Spawns", "DroneNoMinimumStage", Cat.Spawns, "" },
            new[] { "Spawns", "DroneHealthMultiplier", Cat.Spawns, "" },
            new[] { "Sacrifice", "DropChanceMultiplier", Cat.Sacrifice, "" },
            new[] { "Sacrifice", "KeepInteractables", Cat.Sacrifice, "" },
            new[] { "Sacrifice", "KeptInteractableWeight", Cat.Sacrifice, "" },
            new[] { "Fixes", "ShareSuiteSacrificeCreditFix", Cat.Sacrifice, "" },
            new[] { "Survivors", "AcolyteAttackSpeedMultiplier", Cat.Balance, "" },
            new[] { "Fixes", "RouletteSpinCost", Cat.Balance, "" },
            new[] { "Artifacts", "DisableGenetics", Cat.Balance, "" },
            new[] { "Performance", "PhysicsCatchUpCap", Cat.Lag, "" },
            new[] { "Fixes", "DamageChainGuard", Cat.Lag, "" },
            new[] { "Fixes", "GasolineChainFix", Cat.Lag, "DamageChainGuard" },
            new[] { "Performance", "DamageChainQueueLimit", Cat.Lag, "" },
            new[] { "Performance", "DamageChainDrainMs", Cat.Lag, "" },
            new[] { "Fixes", "BlastChainCap", Cat.Lag, "" },
            new[] { "Performance", "BlastChainMaxDepth", Cat.Lag, "" },
            new[] { "Performance", "BlastChainBudgetMs", Cat.Lag, "" },
            new[] { "Fixes", "BlastTargetGuard", Cat.Errors, "ErrorLoopGuards" },
            new[] { "Fixes", "StatsRetryGuard", Cat.Errors, "ErrorLoopGuards" },
            new[] { "Fixes", "OverlapHitGuard", Cat.Errors, "ErrorLoopGuards" },
            new[] { "Fixes", "DotTickGuard", Cat.Errors, "ErrorLoopGuards" },
            new[] { "Fixes", "BulletHitGuard", Cat.Errors, "ErrorLoopGuards" },
            new[] { "Fixes", "OutdatedHookGuard", Cat.Errors, "" },
            new[] { "Fixes", "MovedAssetRedirect", Cat.Errors, "" },
            new[] { "Fixes", "BrokenHandlerGuard", Cat.Errors, "" },
            new[] { "Fixes", "StunDurationFix", Cat.Errors, "" },
            new[] { "Fixes", "NullDccsPoolEntryFix", Cat.Errors, "" },
            new[] { "Fixes", "InfernoHookChurnFix", Cat.Errors, "HookChurnFixes" },
            new[] { "Fixes", "RespawnHookChurnFix", Cat.Errors, "HookChurnFixes" },
            new[] { "Fixes", "AudioOverlapStopFix", Cat.Errors, "StuckSoundFixes" },
            new[] { "Fixes", "OrphanLoopGuard", Cat.Errors, "StuckSoundFixes" },
            new[] { "Fixes", "LunarPoolLoopStopFix", Cat.Errors, "StuckSoundFixes" },
            new[] { "Fixes", "BubbetsItemsFix", Cat.Items, "" },
            new[] { "Fixes", "TinkersSatchelItemIsolation", Cat.Items, "TinkersSatchelFixes" },
            new[] { "Fixes", "TinkersSatchelLanguageFix", Cat.Items, "TinkersSatchelFixes" },
            new[] { "Fixes", "LinearDamageFocusCrystalFix", Cat.Items, "" },
            new[] { "Fixes", "DaggerOfFrenzyGuard", Cat.Items, "" },
            new[] { "Fixes", "ZetAspectsDropFix", Cat.Items, "" },
            new[] { "Fixes", "HypercritNullFix", Cat.Items, "" },
            new[] { "Fixes", "PrintToInventoryScrappers", Cat.Items, "" },
            new[] { "Fixes", "InfernoMithrixFix", Cat.Enemies, "" },
            new[] { "Fixes", "InfernoLevelBoostFix", Cat.Enemies, "" },
            new[] { "Fixes", "InfernoCooldownScalingFix", Cat.Enemies, "" },
            new[] { "Fixes", "ThetaShieldLoopFix", Cat.Enemies, "" },
            new[] { "Fixes", "SwiftImpactFix", Cat.Enemies, "" },
            new[] { "Fixes", "FlowerRootFix", Cat.Enemies, "RexFlowerFixes" },
            new[] { "Fixes", "ScepterFlowerOwnerFix", Cat.Enemies, "RexFlowerFixes" },
            new[] { "Fixes", "ShamanModelFix", Cat.Enemies, "" },
            new[] { "Fixes", "ExtraSkillSlotsHudFix", Cat.Enemies, "" },
            new[] { "Fixes", "PaladinMaterialsFix", Cat.Enemies, "" },
            new[] { "Diagnostics", "LagTracer", Cat.Diagnostics, "" },
            new[] { "Diagnostics", "SoundTracer", Cat.Diagnostics, "" },
        };

        internal static void Run(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                int moved;
                var text = File.ReadAllText(path);
                var result = Migrate(text, out moved);
                if (moved == 0) return;
                if (!File.Exists(path + BackupSuffix)) File.Copy(path, path + BackupSuffix);
                File.WriteAllText(path, result, new System.Text.UTF8Encoding(false));
                CompatShieldPatcher.Log.LogInfo("Config: moved " + moved + " settings into the new categories and kept their values (old file saved as " + Path.GetFileName(path) + BackupSuffix + ")");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogError("Config: couldn't move settings into the new categories, moved ones are on their defaults this launch: " + e);
            }
        }

        internal static string Migrate(string text, out int moved)
        {
            moved = 0;
            var map = new Dictionary<string, string[]>();
            foreach (var m in Moves) map[m[0] + "\0" + m[1]] = m;
            var keep = new List<string>();
            var present = new HashSet<string>();
            var values = new Dictionary<string, string>();
            var order = new List<string[]>();
            string section = "";
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith("#")) { keep.Add(line); continue; }
                if (t.StartsWith("[") && t.EndsWith("]")) { section = t.Substring(1, t.Length - 2); keep.Add(line); continue; }
                var parts = t.Split(new[] { '=' }, 2);
                if (parts.Length != 2) { keep.Add(line); continue; }
                var key = parts[0].Trim();
                var value = parts[1].Trim();
                present.Add(section + "\0" + key);
                string[] move;
                if (!map.TryGetValue(section + "\0" + key, out move)) { keep.Add(line); continue; }
                moved++;
                var toKey = move[3].Length > 0 ? move[3] : move[1];
                var target = move[2] + "\0" + toKey;
                string earlier;
                if (values.TryGetValue(target, out earlier)) values[target] = IsFalse(earlier) || IsFalse(value) ? "false" : earlier;
                else { values[target] = value; order.Add(new[] { move[2], toKey, target }); }
            }
            if (moved == 0) return text;
            var output = new System.Text.StringBuilder(string.Join("\n", keep.ToArray()).TrimEnd());
            output.Append("\n");
            foreach (var entry in order)
            {
                if (present.Contains(entry[2])) continue;
                output.Append("\n[").Append(entry[0]).Append("]\n").Append(entry[1]).Append(" = ").Append(values[entry[2]]).Append("\n");
            }
            return output.ToString();
        }

        static bool IsFalse(string value) => string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

        // Swappable so the harness can stand in for whatever stopped BepInEx's save on 2026-11-09.
        internal static Action<ConfigFile> SaveConfig = c => c.Save();

        /// <summary>
        /// Puts every setting in the file under one heading per category. BepInEx does this itself whenever a setting is added, but on 2026-11-09 the file
        /// stayed as Run wrote it, with the old headings and no line for new settings, and nothing in the log said why. Values apply either way.
        /// </summary>
        internal static void EnsureSaved(ConfigFile cfg)
        {
            try
            {
                SaveConfig(cfg);
                if (Matches(File.ReadAllText(cfg.ConfigFilePath), cfg)) return;
                var owners = Harmony.GetPatchInfo(typeof(ConfigFile).GetMethod(nameof(ConfigFile.Save)))?.Owners;
                WriteDirectly(cfg);
                CompatShieldPatcher.Log.LogWarning("Config: BepInEx's save didn't update the file, so CompatShield wrote it itself (patches on the save: "
                    + (owners == null || owners.Count == 0 ? "none" : string.Join(", ", owners)) + ")");
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Config: couldn't tidy the settings file, so new settings may not show in the mod manager. Values still apply: " + e.Message);
            }
        }

        /// <summary>True when the text has every bound setting, one heading per category and no heading with nothing under it.</summary>
        internal static bool Matches(string text, ConfigFile cfg)
        {
            var headers = new Dictionary<string, int>();
            var keys = new HashSet<string>();
            var used = new HashSet<string>();
            string section = "";
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                if (t.StartsWith("[") && t.EndsWith("]"))
                {
                    section = t.Substring(1, t.Length - 2);
                    int seen;
                    headers.TryGetValue(section, out seen);
                    headers[section] = seen + 1;
                    continue;
                }
                int equals = t.IndexOf('=');
                if (equals <= 0) continue;
                keys.Add(section + "\0" + t.Substring(0, equals).Trim());
                used.Add(section);
            }
            return headers.Count == used.Count && headers.Values.All(n => n == 1) && cfg.Keys.All(d => keys.Contains(d.Section + "\0" + d.Key));
        }

        // The layout BepInEx's own Save writes. Settings no longer bound are dropped, which is the one difference.
        static void WriteDirectly(ConfigFile cfg)
        {
            using (var writer = new StreamWriter(cfg.ConfigFilePath, false, new System.Text.UTF8Encoding(false)))
                foreach (var category in cfg.Keys.GroupBy(d => d.Section).OrderBy(g => g.Key))
                {
                    writer.WriteLine("[" + category.Key + "]");
                    foreach (var definition in category)
                    {
                        var entry = cfg[definition];
                        writer.WriteLine();
                        entry.WriteDescription(writer);
                        writer.WriteLine(definition.Key + " = " + entry.GetSerializedValue());
                    }
                    writer.WriteLine();
                }
        }
    }

    /// <summary>
    /// The most enemies alive at once, summons included. MonsterLimit only reaches spawns that respect the team limit, and anything setting
    /// ignoreTeamMemberLimit walks past it: on 2026-11-09 a stage went from 16 enemies to 319 in ten game seconds that way and never recovered.
    /// Two of the game's death effects don't go through MasterSummon at all (Void Infestors, Malachite urchins), so they are stopped where they get
    /// their body. Boss characters and scripted boss parts are let through, because a fight waiting on them would never finish. New spawns only:
    /// nothing already alive is removed, and projectiles aren't counted. Host only, since only the host spawns anything.
    /// </summary>
    public static class HardEnemyCap
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal const int TopNames = 5;
        internal const int MaxNestedRefusals = 3, CallerSample = 8;
        const int TicksPerCheck = 60;
        static readonly string[] HostileTeams = { "Monster", "Lunar", "Void" };

        // A limit-ignoring spawn made from one of these belongs to a fight's script or can't cope with a refusal. DoppelgangerInvasionManager, the two
        // turret placers, Goobo clones and Starstorm 2's Nemesis event all use the result without checking it, and the turret and Nemesis ones would
        // then throw every tick. Turrets and clones are limited by their owner's deployable slots. Directors get no exemption: their bosses pass as
        // boss characters, and the game only hands a boss director boss-flagged cards.
        internal static readonly string[] ScriptedMarkers =
        {
            "ScriptedCombatEncounter", "EntityStates.BrotherMonster", "EntityStates.Missions.", "EntityStates.SolusAmalgamator", "SolusWing",
            "EntityStates.Totem", "ProvidenceController", "WeakPointChildSpawner", "VultureFightOverride", "CompulsionQuests",
            "DoppelgangerInvasionManager", "HandleConstructTurret", "EngiUtilitySpeedispenser", "GummyCloneProjectile", "GenericNemesisEvent",
        };

        // Further up the stack than these, code only caused a hit or a death. A death effect isn't part of whatever scripted thing did the killing.
        internal static readonly string[] DeathFrames = { "RoR2.GlobalEventManager", "RoR2.HealthComponent" };

        // What sits between whoever asked for a spawn and the summon itself: never the one to name in the log.
        internal static readonly HashSet<string> SpawnPlumbing = new HashSet<string> { "RoR2.MasterSummon", "RoR2.CharacterSpawnCard", "RoR2.MasterCopySpawnCard", "RoR2.SpawnCard", "RoR2.DirectorCore" };

        // The game's own death effects that skip MasterSummon: built by hand, then given a body through SpawnBodyHere.
        internal static readonly HashSet<string> DirectDeathSpawns = new HashSet<string> { "VoidInfestorMaster", "UrchinTurretMaster" };

        internal static int Cap;
        static int stopped, peak, ticks, nextReport, lastFrame, lastDepth, nested;
        static object lastDestroyed;
        static bool patched, warned;
        static Harmony patcher;
        static Assembly game;
        static readonly Dictionary<string, int> stoppedPrefabs = new Dictionary<string, int>();
        static readonly Dictionary<string, int> stoppedCallers = new Dictionary<string, int>();
        static readonly Dictionary<string, int> letThrough = new Dictionary<string, int>();

        static Func<object, object> ignoreRead, teamOverrideRead, summonerRead, prefabRead, bodyPrefabRead, masterTeamRead, championRead;
        static Type masterType, bodyType;
        static MethodInfo objectTeam, teamMembers;
        static PropertyInfo hostRead;
        static object[] hostileValues;

        // Swappable, because the harness has no Unity objects or running game.
        internal static Func<int> Alive = CountAlive;
        internal static Func<IEnumerable<string>> AliveNames = NamesAlive;
        internal static Func<string> Scene = () => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        internal static Func<object, bool> IgnoresLimit = s => (bool)ignoreRead(s);
        internal static Func<object, bool> SummonHostile = IsSummonHostile;
        internal static Func<object, bool> SummonChampion = IsSummonChampion;
        internal static Func<object, string> PrefabName = s => (prefabRead(s) as UnityEngine.Object)?.name ?? "?";
        internal static Func<List<MethodBase>> Frames = ReadFrames;
        internal static Func<object, string> MasterName = m => (m as Component)?.gameObject.name ?? "";
        internal static Func<object, bool> MasterHostile = m => Array.IndexOf(HostileTeams, masterTeamRead(m)?.ToString()) >= 0;
        internal static Action<object> DestroyMaster = m => UnityEngine.Object.Destroy(((Component)m).gameObject);
        internal static Func<int> Frame = () => UnityEngine.Time.frameCount;
        internal static Func<bool> IsHost = () => hostRead == null || (bool)hostRead.GetValue(null, null);

        public static void Apply(Harmony h, Assembly ror2, int cap)
        {
            Cap = cap;
            if (cap <= 0 || patcher != null) return;
            try
            {
                patcher = h;
                game = ror2;
                var ev = ror2.GetType("RoR2.Run")?.GetEvent("onRunStartGlobal", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) throw new MissingMemberException("RoR2.Run.onRunStartGlobal not found");
                ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(HardEnemyCap).GetMethod(nameof(OnRunStart), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e)
            {
                CompatShieldPatcher.Log.LogWarning("Enemy cap: can't watch run starts, not applied: " + e.Message);
            }
        }

        static void OnRunStart(object run)
        {
            if (patched) return;
            patched = true;
            try
            {
                Bind(game);
                var perform = game.GetType("RoR2.MasterSummon").GetMethod("Perform", Any);
                if (perform == null) throw new MissingMethodException("RoR2.MasterSummon.Perform not found");
                patcher.Patch(perform, prefix: new HarmonyMethod(typeof(HardEnemyCap).GetMethod(nameof(PerformPrefix), Any)));
                PatchDirectSpawns();
                Subscribe("RoR2.RoR2Application", "onFixedUpdate", nameof(OnTick));
                Subscribe("RoR2.Stage", "onStageStartGlobal", nameof(OnStageEdge));
                Subscribe("RoR2.Run", "onRunDestroyGlobal", nameof(OnStageEdge));
                CompatShieldPatcher.Log.LogInfo($"Enemy cap: at most {Cap} enemies alive at once, summons that ignore the team limit included; boss characters and scripted boss parts still spawn");
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Enemy cap: can't patch the summon code, not applied: " + e.Message); }
        }

        // On its own, because a game update that moves this must not read as the whole cap being off.
        static void PatchDirectSpawns()
        {
            try
            {
                var spawnBody = masterType.GetMethod("SpawnBodyHere", Any);
                if (spawnBody == null) throw new MissingMethodException("RoR2.CharacterMaster.SpawnBodyHere not found");
                patcher.Patch(spawnBody, prefix: new HarmonyMethod(typeof(HardEnemyCap).GetMethod(nameof(SpawnBodyPrefix), Any)));
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Enemy cap: Void Infestors and urchins from elite deaths aren't covered: " + e.Message); }
        }

        // The log lines and the once-a-second count are only reports, so a missing event costs those and nothing else.
        static void Subscribe(string type, string name, string handler)
        {
            try
            {
                var ev = game.GetType(type)?.GetEvent(name, BindingFlags.Public | BindingFlags.Static);
                if (ev == null) { CompatShieldPatcher.Log.LogWarning($"Enemy cap: {type}.{name} not found, some log lines won't appear"); return; }
                ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(HardEnemyCap).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning($"Enemy cap: can't watch {type}.{name}, some log lines won't appear: " + e.Message); }
        }

        internal static void Bind(Assembly ror2)
        {
            var summon = Need(ror2, "RoR2.MasterSummon");
            masterType = Need(ror2, "RoR2.CharacterMaster");
            bodyType = Need(ror2, "RoR2.CharacterBody");
            var teams = Need(ror2, "RoR2.TeamComponent");
            var teamIndex = Need(ror2, "RoR2.TeamIndex");
            ignoreRead = Reader(summon, "ignoreTeamMemberLimit");
            teamOverrideRead = Reader(summon, "teamIndexOverride");
            summonerRead = Reader(summon, "summonerBodyObject");
            prefabRead = Reader(summon, "masterPrefab");
            bodyPrefabRead = Reader(masterType, "bodyPrefab");
            masterTeamRead = Reader(masterType, "teamIndex");
            championRead = Reader(bodyType, "isChampion");
            objectTeam = teams.GetMethod("GetObjectTeam", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(GameObject) }, null)
                ?? throw new MissingMethodException("RoR2.TeamComponent.GetObjectTeam(GameObject) not found");
            teamMembers = teams.GetMethod("GetTeamMembers", BindingFlags.Public | BindingFlags.Static, null, new[] { teamIndex }, null)
                ?? throw new MissingMethodException("RoR2.TeamComponent.GetTeamMembers(TeamIndex) not found");
            hostileValues = HostileTeams.Select(n => Enum.Parse(teamIndex, n)).ToArray();
            // Only decides whether the count is logged, so a missing property costs nothing worse than a client's log line.
            hostRead = Type.GetType("UnityEngine.Networking.NetworkServer, com.unity.multiplayer-hlapi.Runtime")?.GetProperty("active", BindingFlags.Public | BindingFlags.Static);
        }

        static Type Need(Assembly ror2, string name) => ror2.GetType(name) ?? throw new MissingMemberException(name + " not found");

        static Func<object, object> Reader(Type type, string name)
        {
            var field = type.GetField(name, Any);
            if (field != null) return o => field.GetValue(o);
            var property = type.GetProperty(name, Any);
            if (property != null) return o => property.GetValue(o, null);
            throw new MissingMemberException($"{type.FullName}.{name} not found");
        }

        /// <summary>
        /// Past the cap, a summon stops here unless it is a boss or part of a fight's script. Ordinary summons get no exemption, because the game's
        /// own team limit never gave them one. Any problem lets the spawn through.
        /// </summary>
        internal static bool PerformPrefix(object __instance, ref object __result)
        {
            try
            {
                if (!SummonHostile(__instance) || Alive() < Cap) return true;
                List<MethodBase> frames = null;
                if (IgnoresLimit(__instance))
                {
                    if (SummonChampion(__instance)) { Bump(letThrough, "boss characters"); return true; }
                    frames = Frames();
                    string script = ScriptedBy(frames);
                    if (script != null) { Bump(letThrough, script); return true; }
                    if (AskingAgainInsideItself(frames.Count, Frame())) { Bump(letThrough, "summons that keep retrying when refused"); return true; }
                }
                else if (stopped % CallerSample == 0) frames = Frames();   // the walk is slow and these can come by the hundred, so a sample names them
                Stop(PrefabName(__instance), frames == null ? null : CallerOf(frames));
                __result = null;
                return false;
            }
            catch (Exception e)
            {
                WarnOnce("a summon", e);
                return true;
            }
        }

        // A refusal deeper in the stack than the last one, in the same frame, is a mod asking again from its own failure callback (EnemiesReturns' Lynx
        // Shaman does). Nothing can change while it recurses, so only letting one through ends it before the stack runs out.
        static bool AskingAgainInsideItself(int depth, int frame)
        {
            bool deeper = frame == lastFrame && depth > lastDepth;
            lastFrame = frame;
            lastDepth = depth;
            nested = deeper ? nested + 1 : 0;
            if (nested < MaxNestedRefusals) return false;
            nested = 0;
            return true;
        }

        /// <summary>The death effects that never reach MasterSummon: the master is already built and networked, so past the cap it goes, unspawned.</summary>
        internal static bool SpawnBodyPrefix(object __instance)
        {
            try
            {
                if (ReferenceEquals(__instance, lastDestroyed)) return false;
                string prefab = Tidy(MasterName(__instance));
                if (!DirectDeathSpawns.Contains(prefab) || !MasterHostile(__instance) || Alive() < Cap) return true;
                Stop(prefab, "game:death effects");
                lastDestroyed = __instance;
                DestroyMaster(__instance);
                return false;
            }
            catch (Exception e)
            {
                WarnOnce("a body spawn", e);
                return true;
            }
        }

        /// <summary>The script a spawn belongs to, or null. Walks outward from the spawn and gives up at the first frame that is only a hit or a death.</summary>
        internal static string ScriptedBy(IEnumerable<MethodBase> frames)
        {
            foreach (var m in frames)
            {
                if (m == null) continue;
                string text = (m.DeclaringType?.FullName ?? "") + "::" + m.Name;
                foreach (var marker in ScriptedMarkers)
                    if (text.Contains(marker)) return marker;
                foreach (var death in DeathFrames)
                    if (text.Contains(death)) return null;
            }
            return null;
        }

        /// <summary>Who asked for the spawn, with the spawn cards and the director between them and the summon left out.</summary>
        internal static string CallerOf(IEnumerable<MethodBase> frames) =>
            LagTracer.DirtySource(frames.Where(m => m != null && (LagTracer.IsWrapperFrame(m) || !SpawnPlumbing.Contains(m.DeclaringType?.FullName ?? ""))));

        static void Stop(string prefab, string caller)
        {
            stopped++;
            Bump(stoppedPrefabs, prefab);
            if (caller != null) Bump(stoppedCallers, caller);
        }

        static void Bump(Dictionary<string, int> counts, string key)
        {
            counts.TryGetValue(key, out int n);
            counts[key] = n + 1;
        }

        static void WarnOnce(string what, Exception e)
        {
            if (warned) return;
            warned = true;
            CompatShieldPatcher.Log.LogWarning($"Enemy cap: {what} couldn't be checked, so it went ahead (shown once): {e.Message}");
        }

        static string Tidy(string name) => (name ?? "?").Replace("(Clone)", "");

        static int CountAlive()
        {
            int total = 0;
            foreach (var team in hostileValues)
                total += ((System.Collections.ICollection)teamMembers.Invoke(null, new[] { team })).Count;
            return total;
        }

        static IEnumerable<string> NamesAlive()
        {
            var names = new List<string>();
            foreach (var team in hostileValues)
                foreach (var member in (System.Collections.IEnumerable)teamMembers.Invoke(null, new[] { team }))
                    names.Add((member as UnityEngine.Object)?.name);
            return names;
        }

        static bool IsSummonHostile(object summon)
        {
            object team = teamOverrideRead(summon);
            if (team == null)
            {
                var owner = summonerRead(summon) as GameObject;
                if (owner == null) return false;
                team = objectTeam.Invoke(null, new object[] { owner });
            }
            return Array.IndexOf(HostileTeams, team.ToString()) >= 0;
        }

        static bool IsSummonChampion(object summon)
        {
            var prefab = prefabRead(summon) as GameObject;
            if (prefab == null) return false;
            var master = prefab.GetComponent(masterType);
            if (master == null) return false;
            var bodyPrefab = bodyPrefabRead(master) as GameObject;
            if (bodyPrefab == null) return false;
            var body = bodyPrefab.GetComponent(bodyType);
            return body != null && (bool)championRead(body);
        }

        static List<MethodBase> ReadFrames()
        {
            var frames = new System.Diagnostics.StackTrace(false).GetFrames();
            var methods = new List<MethodBase>(frames?.Length ?? 0);
            if (frames != null)
                foreach (var f in frames) methods.Add(f.GetMethod());
            return methods;
        }

        static void OnTick()
        {
            if (++ticks < TicksPerCheck) return;
            ticks = 0;
            try { CheckNow(); }
            catch (Exception e) { WarnOnce("the once-a-second count", e); }
        }

        /// <summary>Once a second: keeps the peak, and names what is alive the first time a stage reaches the cap and each time the count doubles after that.</summary>
        internal static void CheckNow()
        {
            if (!IsHost()) return;   // refusals only happen on the host, so a client's count would read as a cap that failed
            int alive = Alive();
            if (alive > peak) peak = alive;
            if (alive < Cap || alive < nextReport) return;
            nextReport = alive * 2;
            var names = new Dictionary<string, int>();
            foreach (var name in AliveNames()) Bump(names, Tidy(name));
            CompatShieldPatcher.Log.LogWarning($"Enemy cap: {alive} enemies alive in {Scene()} (cap {Cap}). Most: {Top(names)}");
        }

        static string Top(Dictionary<string, int> counts) =>
            string.Join(", ", counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(TopNames).Select(kv => $"{kv.Key} {kv.Value}"));

        static void OnStageEdge(object source)
        {
            try { EndStage(); }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Enemy cap: couldn't write the stage line: " + e.Message); }
        }

        internal static void EndStage()
        {
            string line = Summary();
            Clear();
            if (line != null) CompatShieldPatcher.Log.LogInfo(line);
        }

        /// <summary>The stage line, or null when the stage never came near the cap.</summary>
        internal static string Summary()
        {
            if (stopped == 0 && peak < Cap && letThrough.Count == 0) return null;
            string line = $"Enemy cap: last stage stopped {stopped} spawn(s), most alive at once {peak} (cap {Cap})";
            if (stoppedPrefabs.Count > 0) line += "; stopped: " + Top(stoppedPrefabs);
            if (stoppedCallers.Count > 0) line += "; asked for by: " + Top(stoppedCallers);
            if (letThrough.Count > 0) line += "; let through: " + Top(letThrough);
            if (stopped == 0 && letThrough.Count == 0 && peak > Cap) line += "; nothing was stopped, so these came by a route the cap doesn't cover";
            return line;
        }

        static void Clear()
        {
            stopped = peak = ticks = nextReport = lastFrame = lastDepth = nested = 0;
            lastDestroyed = null;
            stoppedPrefabs.Clear();
            stoppedCallers.Clear();
            letThrough.Clear();
        }

        internal static void ResetForTest(int cap)
        {
            Cap = cap;
            warned = false;
            Clear();
        }
    }

    /// <summary>
    /// Sandswept's Hallowed Ichor takes over the run timer's Update for the rest of a run once anyone picks it up. Its first step uses the FreakyText it added to
    /// the timer's text, which a HUD without that text never got, so every update threw before reaching the line that sets the time: 49,400 errors in nine
    /// minutes on 2026-11-09, and the mod never calls the game's own Update in that state, so the timer stopped too. With no FreakyText to distort, the game's
    /// Update runs instead. The timer is local UI, so each PC needs this for its own game.
    /// </summary>
    public static class HallowedIchorTimerFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal const string ItemName = "Sandswept.Items.Lunars.HallowedIchor", JitterName = "Sandswept.Items.Lunars.FreakyText", TimerName = "RoR2.UI.RunTimerUIController";

        static Harmony patcher;
        static Assembly mod, game;
        static FieldInfo heldField, textField;
        static Type jitterType;
        static bool tried, loggedFirst, warned;

        // Swappable, because the harness has no Unity objects or running game.
        internal static Func<bool> Held = () => (bool)heldField.GetValue(null);
        internal static Func<object, bool> HasJitter = self =>
        {
            var text = textField.GetValue(self) as Component;
            return text != null && text.GetComponent(jitterType) != null;
        };

        public static void Apply(Harmony h, Assembly sandswept, Assembly ror2)
        {
            try
            {
                patcher = h;
                mod = sandswept;
                game = ror2;
                var ev = ror2.GetType("RoR2.Run")?.GetEvent("onRunStartGlobal", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) throw new MissingMemberException("RoR2.Run.onRunStartGlobal not found");
                ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(HallowedIchorTimerFix).GetMethod(nameof(OnRunStart), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Hallowed Ichor timer fix: can't watch run starts, not applied: " + e.Message); }
        }

        // At a run start, because patching at load would run the item's static constructor.
        static void OnRunStart(object run)
        {
            if (tried) return;
            tried = true;
            try
            {
                var item = mod.GetType(ItemName);
                var hook = item?.GetMethod("PerformJitterUI", Any);
                heldField = item?.GetField("anyoneHadHallowedIchorThisRun", Any);
                jitterType = mod.GetType(JitterName);
                textField = game.GetType(TimerName)?.GetField("runStopwatchTimerTextController", Any);
                if (hook == null || heldField == null || jitterType == null || textField == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Hallowed Ichor timer fix: the item or the run timer has changed (mod updated?), not applied");
                    return;
                }
                patcher.Patch(hook, prefix: new HarmonyMethod(typeof(HallowedIchorTimerFix).GetMethod(nameof(Prefix), Any)));
                CompatShieldPatcher.Log.LogInfo("Sandswept: Hallowed Ichor no longer stops the run timer on a HUD without its text effect");
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Hallowed Ichor timer fix: failed to apply: " + e.Message); }
        }

        internal static bool Prefix(Delegate orig, object self)
        {
            if (!UsePlainTimer(self)) return true;
            orig.DynamicInvoke(self);
            return false;
        }

        // Kept apart from the call to orig, so a problem in the game's own Update isn't reported as a problem here.
        internal static bool UsePlainTimer(object self)
        {
            try
            {
                if (!Held() || HasJitter(self)) return false;
                if (!loggedFirst)
                {
                    loggedFirst = true;
                    CompatShieldPatcher.Log.LogInfo("Hallowed Ichor: this HUD has no text effect to distort, so the run timer keeps counting normally");
                }
                return true;
            }
            catch (Exception e)
            {
                if (!warned)
                {
                    warned = true;
                    CompatShieldPatcher.Log.LogWarning("Hallowed Ichor timer fix: couldn't check the timer, so the mod's own code ran (shown once): " + e.Message);
                }
                return false;
            }
        }

        internal static void ResetForTest()
        {
            loggedFirst = false;
            warned = false;
        }
    }

    /// <summary>
    /// The game's sacrifice totem (EntityStates.Totem.SacrificeSuccess) moves its tether to its first pick every tick, before its own
    /// timer check. When that pick is already gone (the server clears picks that died during the windup, and a client may not find
    /// one), that line throws every tick, so the totem neither sacrifices nor leaves the state until something knocks it out of it:
    /// 534 errors in about 11 seconds on Helminth Hatchery on 2026-11-09. This points the tether at the next living pick, or puts the
    /// tether away when none is left, so the rest of FixedUpdate runs as the game wrote it. Every PC: each one moves its own tether.
    /// </summary>
    public static class TotemTetherGuard
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal const string StateName = "EntityStates.Totem.SacrificeSuccess", PoolName = "RoR2.EffectManager";

        static Harmony patcher;
        static Assembly game;
        static bool tried, loggedMoved, loggedPutAway, warned;
        static FieldInfo killedField, picksField, rootField, effectField, endField, helperField;
        static MethodInfo release;

        // Swappable, because the harness has no Unity objects.
        internal static Func<object, bool> Alive = o => o is UnityEngine.Object u && u != null;
        internal static Action<object> PutAway = RealPutAway;

        public static void Apply(Harmony h, Assembly ror2)
        {
            try
            {
                patcher = h;
                game = ror2;
                var ev = ror2.GetType("RoR2.Run")?.GetEvent("onRunStartGlobal", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) throw new MissingMemberException("RoR2.Run.onRunStartGlobal not found");
                ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(TotemTetherGuard).GetMethod(nameof(OnRunStart), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Totem tether guard: can't watch run starts, not applied: " + e.Message); }
        }

        // At a run start, like the other entity state fixes, so nothing here runs inside an assembly load.
        static void OnRunStart(object run)
        {
            if (tried) return;
            tried = true;
            try
            {
                var state = game.GetType(StateName);
                var tick = state?.GetMethod("FixedUpdate", Any | BindingFlags.DeclaredOnly);
                if (tick == null || !Bind(state))
                {
                    CompatShieldPatcher.Log.LogWarning("Totem tether guard: the sacrifice totem's state has changed (game updated?), not applied");
                    return;
                }
                release = game.GetType(PoolName)?.GetMethod("ReturnToPoolOrDestroyInstance", Any, null,
                    new[] { helperField.FieldType, typeof(UnityEngine.GameObject).MakeByRefType() }, null);
                if (release == null)
                {
                    CompatShieldPatcher.Log.LogWarning("Totem tether guard: the effect pool's release method has changed (game updated?), not applied");
                    return;
                }
                patcher.Patch(tick, prefix: new HarmonyMethod(typeof(TotemTetherGuard).GetMethod(nameof(Prefix), Any)));
                CompatShieldPatcher.Log.LogInfo("Game: a sacrifice totem whose first pick is gone now moves its tether on instead of throwing every tick");
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Totem tether guard: failed to apply: " + e.Message); }
        }

        internal static bool Bind(Type state)
        {
            killedField = state.GetField("_hasKilledAllies", Any);
            picksField = state.GetField("alliesToRapture", Any);
            rootField = state.GetField("tetherRootTransform", Any);
            effectField = state.GetField("tetherEffectInstance", Any);
            endField = state.GetField("tetherEffectEndTransform", Any);
            helperField = state.GetField("tetherEffectInstance_efh", Any);
            return killedField != null && killedField.FieldType == typeof(bool) && picksField != null && picksField.FieldType.IsArray
                && rootField != null && effectField != null && endField != null && helperField != null;
        }

        internal static void Prefix(object __instance)
        {
            try { Tidy(__instance); }
            catch (Exception e)
            {
                if (warned) return;
                warned = true;
                CompatShieldPatcher.Log.LogWarning("Totem tether guard: couldn't check a sacrifice totem's tether, so the game's code ran unchanged (shown once): " + e.Message);
            }
        }

        // 0: nothing to do. 1: the tether now follows the next living pick. 2: no living pick or tether end left, so it was put away.
        internal static int Tidy(object state)
        {
            if ((bool)killedField.GetValue(state) || !Alive(effectField.GetValue(state))) return 0;
            var picks = picksField.GetValue(state) as Array;
            bool ends = Alive(rootField.GetValue(state)) && Alive(endField.GetValue(state));
            if (ends && picks != null && picks.Length > 0 && Alive(picks.GetValue(0))) return 0;
            if (ends && picks != null)
            {
                for (int i = 1; i < picks.Length; i++)
                {
                    if (!Alive(picks.GetValue(i))) continue;
                    // A swap keeps every pick in the list, so the sacrifice at the end still reaches all of them.
                    object gone = picks.GetValue(0);
                    picks.SetValue(picks.GetValue(i), 0);
                    picks.SetValue(gone, i);
                    if (!loggedMoved)
                    {
                        loggedMoved = true;
                        CompatShieldPatcher.Log.LogInfo("Totem tether guard: a sacrifice totem's first pick was gone, so its tether now follows the next one (first time this launch)");
                    }
                    return 1;
                }
            }
            PutAway(state);
            if (!loggedPutAway)
            {
                loggedPutAway = true;
                CompatShieldPatcher.Log.LogInfo("Totem tether guard: a sacrifice totem had nothing left to tether, so its tether was put away (first time this launch)");
            }
            return 2;
        }

        // The same release the game makes when the sacrifice goes off. With the tether gone, the state's own check skips those lines.
        static void RealPutAway(object state)
        {
            release.Invoke(null, new object[] { helperField.GetValue(state), effectField.GetValue(state) });
            effectField.SetValue(state, null);
            helperField.SetValue(state, null);
            endField.SetValue(state, null);
        }

        internal static void ResetForTest()
        {
            loggedMoved = loggedPutAway = warned = false;
        }
    }

    // PaladinMod's shockwave ghost carries a follower that detaches a dust effect at Awake and moves it to the ghost every
    // tick. When the effect ends before the ghost does, or was never found, that move throws every tick (about 50 errors a
    // second) until the ghost is destroyed. The guard skips the move once the effect is gone.
    public static class PaladinShockwaveFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal const string FollowerName = "PaladinMod.Misc.StupidFuckingBullshit";

        static Harmony patcher;
        static Assembly mod;
        static FieldInfo effectField;
        static bool tried, loggedFirst, warned;

        // Swappable, because the harness has no Unity objects.
        internal static Func<object, bool> Alive = o => o is UnityEngine.Object u && u != null;

        public static void Apply(Harmony h, Assembly paladin, Assembly ror2)
        {
            try
            {
                patcher = h;
                mod = paladin;
                var ev = ror2.GetType("RoR2.Run")?.GetEvent("onRunStartGlobal", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) throw new MissingMemberException("RoR2.Run.onRunStartGlobal not found");
                ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(PaladinShockwaveFix).GetMethod(nameof(OnRunStart), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Paladin shockwave fix: can't watch run starts, not applied: " + e.Message); }
        }

        // At a run start, like the other mod guards: no shockwave exists before one.
        static void OnRunStart(object run)
        {
            if (tried) return;
            tried = true;
            try
            {
                var follower = mod.GetType(FollowerName);
                var tick = follower?.GetMethod("FixedUpdate", Any | BindingFlags.DeclaredOnly);
                if (tick == null || !Bind(follower))
                {
                    CompatShieldPatcher.Log.LogWarning("Paladin shockwave fix: the shockwave's effect follower has changed (mod updated?), not applied");
                    return;
                }
                patcher.Patch(tick, prefix: new HarmonyMethod(typeof(PaladinShockwaveFix).GetMethod(nameof(Prefix), Any)));
                CompatShieldPatcher.Log.LogInfo("PaladinMod: a shockwave whose dust effect is gone now stops moving it instead of throwing every tick");
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Paladin shockwave fix: failed to apply: " + e.Message); }
        }

        // The follower's one instance field holding a ParticleSystem, found by its type rather than its name.
        internal static bool Bind(Type follower)
        {
            var fields = follower.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(f => f.FieldType.Name == "ParticleSystem").ToArray();
            effectField = fields.Length == 1 ? fields[0] : null;
            return effectField != null;
        }

        internal static bool Prefix(object __instance)
        {
            try
            {
                if (Alive(effectField.GetValue(__instance))) return true;
                if (!loggedFirst)
                {
                    loggedFirst = true;
                    CompatShieldPatcher.Log.LogInfo("PaladinMod: a shockwave's dust effect was gone, so the shockwave stopped moving it (first time this launch)");
                }
                return false;
            }
            catch (Exception e)
            {
                if (!warned)
                {
                    warned = true;
                    CompatShieldPatcher.Log.LogWarning("Paladin shockwave fix: couldn't check a shockwave's dust effect, so the mod's code ran unchanged (shown once): " + e.Message);
                }
                return true;
            }
        }

        internal static void ResetForTest() { loggedFirst = warned = false; }
    }

    // Sandswept's damage trails (the Delta Construct's fire trail): when a trail's last flame burns out, the manager
    // recalculates the trail's bounds from an empty list and Unity throws. That ends the manager's tick early, so later
    // trails skip that tick's damage and finished trails aren't cleaned up. The guard leaves an empty trail's bounds as
    // they were; with no flames left nothing reads them.
    public static class SandsweptFireTrailFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal const string ManagerName = "Sandswept.Utils.DamageTrailManager";

        static Harmony patcher;
        static Assembly mod;
        static FieldInfo pointsField;
        static bool tried, loggedFirst, warned;

        public static void Apply(Harmony h, Assembly sandswept, Assembly ror2)
        {
            try
            {
                patcher = h;
                mod = sandswept;
                var ev = ror2.GetType("RoR2.Run")?.GetEvent("onRunStartGlobal", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) throw new MissingMemberException("RoR2.Run.onRunStartGlobal not found");
                ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(SandsweptFireTrailFix).GetMethod(nameof(OnRunStart), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Sandswept fire trail fix: can't watch run starts, not applied: " + e.Message); }
        }

        // At a run start, because patching at load would run the manager's static constructor.
        static void OnRunStart(object run)
        {
            if (tried) return;
            tried = true;
            try
            {
                var resize = mod.GetType(ManagerName)?.GetMethod("RecalculateTrailBounds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var args = resize?.GetParameters();
                if (args == null || args.Length != 1 || !Bind(args[0].ParameterType))
                {
                    CompatShieldPatcher.Log.LogWarning("Sandswept fire trail fix: the damage trail manager has changed (mod updated?), not applied");
                    return;
                }
                patcher.Patch(resize, prefix: new HarmonyMethod(typeof(SandsweptFireTrailFix).GetMethod(nameof(Prefix), Any)));
                CompatShieldPatcher.Log.LogInfo("Sandswept: a fire trail whose last flame burned out no longer throws when the game resizes it");
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Sandswept fire trail fix: failed to apply: " + e.Message); }
        }

        internal static bool Bind(Type trail)
        {
            pointsField = trail.GetField("activePoints", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return pointsField != null && typeof(System.Collections.ICollection).IsAssignableFrom(pointsField.FieldType);
        }

        internal static bool Prefix(object trail)
        {
            try
            {
                if (trail != null && pointsField.GetValue(trail) is System.Collections.ICollection points && points.Count > 0) return true;
                if (!loggedFirst)
                {
                    loggedFirst = true;
                    CompatShieldPatcher.Log.LogInfo("Sandswept: a fire trail's last flame burned out, so its size was left as it was (first time this launch)");
                }
                return false;
            }
            catch (Exception e)
            {
                if (!warned)
                {
                    warned = true;
                    CompatShieldPatcher.Log.LogWarning("Sandswept fire trail fix: couldn't check a fire trail, so the mod's code ran unchanged (shown once): " + e.Message);
                }
                return true;
            }
        }

        internal static void ResetForTest() { loggedFirst = warned = false; }
    }

    // StormyItems' Sharp Anchor keeps its zones in a list parallel to its body list. When a body that isn't in the body list
    // is removed, the mod still removes zone 0, so the two lists shift apart and a zone can point at a body with no
    // inventory (a body without a master). The mod's own upkeep skips such bodies, so that zone is never cleaned up, and
    // every server tick it reads the missing inventory for the holder's anchor count and throws (about 100 errors a second
    // on Lakes at Night, 2026-11-09). The throw comes before the buff, so the zone gave nothing in that state; the guard
    // skips the zone's tick when that lookup would throw.
    public static class SharpAnchorZoneFix
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        internal const string ZoneName = "StormyItems.Items.SharpAnchorZone";
        internal const string ItemName = "StormyItems.Items.SharpAnchor";
        internal const string MainName = "StormyItems.Main";

        static Harmony patcher;
        static Assembly mod;
        static FieldInfo zonesField, bodiesField;
        static PropertyInfo inventoryProp;
        static bool tried, loggedFirst, warned;

        // Swappable, because the harness has no Unity objects.
        internal static Func<object, object> GameObjectOf = o => (o as UnityEngine.Component)?.gameObject;

        public static void Apply(Harmony h, Assembly stormy, Assembly ror2)
        {
            try
            {
                patcher = h;
                mod = stormy;
                var ev = ror2.GetType("RoR2.Run")?.GetEvent("onRunStartGlobal", BindingFlags.Public | BindingFlags.Static);
                if (ev == null) throw new MissingMemberException("RoR2.Run.onRunStartGlobal not found");
                ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, typeof(SharpAnchorZoneFix).GetMethod(nameof(OnRunStart), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Sharp Anchor fix: can't watch run starts, not applied: " + e.Message); }
        }

        // At a run start, like the other mod guards: no zone exists before one.
        static void OnRunStart(object run)
        {
            if (tried) return;
            tried = true;
            try
            {
                var zone = mod.GetType(ZoneName);
                var tick = zone?.GetMethod("BuffOccupants", Any | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                var item = mod.GetType(ItemName);
                var main = mod.GetType(MainName);
                if (tick == null || item == null || main == null || !Bind(item, main))
                {
                    CompatShieldPatcher.Log.LogWarning("Sharp Anchor fix: the anchor zone or its lists have changed (mod updated?), not applied");
                    return;
                }
                patcher.Patch(tick, prefix: new HarmonyMethod(typeof(SharpAnchorZoneFix).GetMethod(nameof(Prefix), Any)));
                CompatShieldPatcher.Log.LogInfo("StormyItems: a Sharp Anchor zone that has lost its holder now skips its tick instead of throwing every tick");
            }
            catch (Exception e) { CompatShieldPatcher.Log.LogWarning("Sharp Anchor fix: failed to apply: " + e.Message); }
        }

        // The zone list on the item, the body list on the plugin, and the inventory property of the body type.
        internal static bool Bind(Type item, Type main)
        {
            zonesField = item.GetField("Zones", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            bodiesField = main.GetField("CharBodies", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            inventoryProp = null;
            if (zonesField == null || bodiesField == null) return false;
            if (!typeof(System.Collections.IList).IsAssignableFrom(zonesField.FieldType) || !typeof(System.Collections.IList).IsAssignableFrom(bodiesField.FieldType)) return false;
            var bodyType = bodiesField.FieldType.IsGenericType ? bodiesField.FieldType.GetGenericArguments()[0] : null;
            inventoryProp = bodyType?.GetProperty("inventory", BindingFlags.Public | BindingFlags.Instance);
            return inventoryProp != null && inventoryProp.GetIndexParameters().Length == 0;
        }

        // True when the mod's holder lookup for this zone would throw: the zone's slot points past the body list, at no
        // body, or at a body with no inventory. A zone not in the list makes the mod count 0 anchors without throwing.
        internal static bool WouldThrow(object zone)
        {
            var go = GameObjectOf(zone);
            if (go == null || !(zonesField.GetValue(null) is System.Collections.IList zones)) return false;
            int index = -1;
            for (int i = 0; i < zones.Count; i++)
                if (ReferenceEquals(zones[i], go)) { index = i; break; }
            if (index < 0) return false;
            var bodies = bodiesField.GetValue(null) as System.Collections.IList;
            if (bodies == null || index >= bodies.Count) return true;
            var body = bodies[index];
            return body == null || inventoryProp.GetValue(body, null) == null;
        }

        internal static bool Prefix(object __instance)
        {
            try
            {
                if (!WouldThrow(__instance)) return true;
                if (!loggedFirst)
                {
                    loggedFirst = true;
                    CompatShieldPatcher.Log.LogInfo("StormyItems: a Sharp Anchor zone had lost track of its holder, so its tick was skipped instead of throwing (first time this launch)");
                }
                return false;
            }
            catch (Exception e)
            {
                if (!warned)
                {
                    warned = true;
                    CompatShieldPatcher.Log.LogWarning("Sharp Anchor fix: couldn't check a zone's holder, so the mod's code ran unchanged (shown once): " + e.Message);
                }
                return true;
            }
        }

        internal static void ResetForTest() { loggedFirst = warned = false; }
    }
}
