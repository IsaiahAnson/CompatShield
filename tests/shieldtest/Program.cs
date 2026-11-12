using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using MonoMod.RuntimeDetour.HookGen;

public static class Game { public static void SetCurrentLanguage(string lang, bool reload) { } }

// Shaped like Paladin's GatherMaterials: branch-free static field assignments, one of which crashes.
public static class FakeAsset {
    public static string a, b, c;
    class Ctl { public string ghost = "ghost"; }
    static Ctl Missing() => null;
    public static void Gather() {
        a = "first".ToUpper();
        b = Missing().ghost;           // NullReferenceException, like EvisProjectile's ProjectileController
        c = string.Concat("th", "ird");
    }
}

// Shaped like TinkersSatchel's Module.SetupAll_PluginAwake: three foreach passes over all items.
public class FakeModule {
    public static System.Collections.Generic.List<string> ran = new System.Collections.Generic.List<string>();
    public virtual void SetupConfig() { ran.Add(GetType().Name + ".Config"); }
    public virtual void SetupAttributes() { ran.Add(GetType().Name + ".Attributes"); }
    public virtual void SetupBehavior() { ran.Add(GetType().Name + ".Behavior"); }
    public static void SetupAll(System.Collections.Generic.IEnumerable<FakeModule> modules) {
        foreach (var m in modules) m.SetupConfig();
        foreach (var m in modules) m.SetupAttributes();
        foreach (var m in modules) m.SetupBehavior();
    }
}
public class ItemA : FakeModule { }
public class TimelostRum : FakeModule {   // throws like the real one did
    public override void SetupAttributes() { throw new TypeLoadException("Could not resolve type with token 01000156 from typeref (expected class 'orig_FireProjectile' in assembly '')"); }
}
public class ItemC : FakeModule { }

namespace ThinkInvisible.TinkersSatchel {
    // Stand-ins with TinkersSatchel's real type/member names; the original Update throws like the real one does.
    public class TinkersSatchelPlugin {
        public static int originalRan;
        public void Update() { originalRan++; throw new MissingMethodException("Method not found: void RoR2.Language.SetCurrentLanguage(string)"); }
    }
    public static class AutoConfigModule {
        public static bool globalStatsDirty, globalDropsDirty, globalLanguageDirty;
    }
    public class ReadOnlyFilingDictionary<T> : System.Collections.Generic.IEnumerable<T> {
        readonly System.Collections.Generic.List<T> items;
        public ReadOnlyFilingDictionary(System.Collections.Generic.List<T> items) { this.items = items; }
        public System.Collections.Generic.IEnumerator<T> GetEnumerator() => items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => items.GetEnumerator();
    }
    // Language members as in TinkersSatchel 6.0.1's Module; the stand-ins record what ran.
    public class Module {
        public static readonly System.Collections.Generic.List<Module> all = new System.Collections.Generic.List<Module>();
        public static readonly ReadOnlyFilingDictionary<Module> allModules = new ReadOnlyFilingDictionary<Module>(all);
        public static readonly System.Collections.Generic.List<string> langLog = new System.Collections.Generic.List<string>();
        public bool enabled { get; protected set; } = true;
        public bool languageInstalled { get; private set; }
        public virtual void RefreshPermanentLanguage() { langLog.Add(GetType().Name + ".Refresh"); AutoConfigModule.globalLanguageDirty = true; }
        public void InstallLanguage() { langLog.Add(GetType().Name + ".Install"); languageInstalled = true; AutoConfigModule.globalLanguageDirty = true; }
        public void UninstallLanguage() { langLog.Add(GetType().Name + ".Uninstall"); languageInstalled = false; AutoConfigModule.globalLanguageDirty = true; }
    }
    public class Mug : Module { }
    public class Unused : Module { public Unused() { enabled = false; } }
    public class BrokenText : Module { public override void RefreshPermanentLanguage() { throw new NullReferenceException("token table missing"); } }
    public class Reentrant : Module {   // something inside the refresh sets the language again
        public override void RefreshPermanentLanguage() { base.RefreshPermanentLanguage(); global::LangGame.SetCurrentLanguage("en", false); }
    }
    public class Wrench : Module { }
}

// Stand-in for the game's new two-argument Language.SetCurrentLanguage (NoInlining so the patched body is what runs).
public static class LangGame {
    public static int calls;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void SetCurrentLanguage(string newCurrentLanguageName, bool forceUnloadCurrentLanguage) { calls++; }
}

namespace RoR2 {
    // Stand-ins shaped like the game's team limit: TeamCatalog's TeamDefs and the check in MasterSummon.Perform.
    public enum TeamIndex : sbyte { None = -1, Neutral, Player, Monster, Lunar, Void }
    public class TeamDef { public int softCharacterLimit; }
    public static class TeamCatalog {
        static readonly TeamDef[] teamDefs = { new TeamDef { softCharacterLimit = 40 }, new TeamDef { softCharacterLimit = 20 },
            new TeamDef { softCharacterLimit = 40 }, new TeamDef { softCharacterLimit = 40 }, new TeamDef { softCharacterLimit = 40 } };
        public static TeamDef GetTeamDef(TeamIndex i) => (int)i >= 0 && (int)i < teamDefs.Length ? teamDefs[(int)i] : null;
    }
    public class TeamComponent {
        public static readonly System.Collections.Generic.List<TeamComponent>[] members = {
            new System.Collections.Generic.List<TeamComponent>(), new System.Collections.Generic.List<TeamComponent>(),
            new System.Collections.Generic.List<TeamComponent>(), new System.Collections.Generic.List<TeamComponent>(),
            new System.Collections.Generic.List<TeamComponent>() };
        public static System.Collections.ObjectModel.ReadOnlyCollection<TeamComponent> GetTeamMembers(TeamIndex i) => members[(int)i].AsReadOnly();
        public static void Fill(TeamIndex i, int n) { members[(int)i].Clear(); for (int k = 0; k < n; k++) members[(int)i].Add(new TeamComponent()); }
    }
    public class SpawnCard { }
    public class DirectorSpawnRequest { public TeamIndex? teamIndexOverride; public bool ignoreTeamMemberLimit; }
    public class SceneDirector { }
    public class EquipmentSlot { public int idx; public int equipmentIndex => idx; }
    public class Inventory { public int current = -1; public int currentEquipmentIndex => current; }
    public class CharacterBody { public EquipmentSlot slot; public Inventory inv = new Inventory(); public EquipmentSlot equipmentSlot => slot; public Inventory inventory => inv;
        // REX flower: Entangle stacks; "destroyed" = Unity-null (the harness swaps FlowerRootFix.IsGone to read it).
        // Like the game: SetBuffCount lowers the count first, then OnClientBuffsChanged runs the mods' hooks, and
        // EliteReworks2 / ClassicItemsReturns crash in GetComponent on a destroyed elite / Hit List target.
        public int entangle; public bool destroyed, eliteAffix, throwOther;
        public static int removeCalls;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void RemoveBuff(BuffDef buffDef) {
            removeCalls++;
            if (buffDef == RoR2Content.Buffs.Entangle) entangle = Math.Max(0, entangle - 1);
            if (destroyed && eliteAffix) throw new NullReferenceException("Overloading.CharacterBody_OnClientBuffsChanged: GetComponent on a destroyed body");
            if (throwOther) throw new InvalidOperationException("another mod's RemoveBuff hook failed");
        }
        public void RecalculateStats() { }
    }
    public class BuffDef { public string name; }
    public class RoR2Content { public static class Buffs { public static BuffDef Entangle = new BuffDef { name = "Entangle" }; } }
    // Shaped like the real AlterBuffDuration: scales a stun state's duration when it applies to that state.
    public class AlterBuffDuration {
        public float durationScalar = 1f;
        public bool affectsFreeze = true;
        public bool IsStunStateAffected(Type entityState) => affectsFreeze && entityState == typeof(EntityStates.FrozenState);
    }
    // Shaped like the real SetStateOnHurt: the list is filled only in Start(); GetModifiedDuration loops over it unguarded
    // (same IL shape: for (i = 0; i < buffDurationControllers.Length; i++)); SetFrozenInternal starts with that lookup.
    public class SetStateOnHurt {
        public AlterBuffDuration[] attached = new AlterBuffDuration[0];   // what GetComponents<AlterBuffDuration>() returns
        private AlterBuffDuration[] buffDurationControllers;
        public float frozenFor = -1f;
        public bool Started => buffDurationControllers != null;
        public void Start() { buffDurationControllers = attached; }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private float GetModifiedDuration(float duration, Type entityStateType) {
            for (int i = 0; i < buffDurationControllers.Length; i++) {
                AlterBuffDuration a = buffDurationControllers[i];
                if (a != null) duration *= a.IsStunStateAffected(entityStateType) ? a.durationScalar : 1f;
            }
            return duration;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void SetFrozenInternal(float duration) { frozenFor = GetModifiedDuration(duration, typeof(EntityStates.FrozenState)); }
    }
    public static class EquipmentCatalog { public static int GetEquipmentDef(int index) => index; }
    public class DamageReport { }
    // Gasoline stand-in: each proc's blast kills the next enemy, whose death procs again (nested), chainLeft times.
    public class GlobalEventManager {
        public static int calls, active, maxActive, chainLeft, throwAtCall = -1, deepThrowAt = -1;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void Boom() => throw new InvalidOperationException("thrown below the blast");
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void ProcIgniteOnKill(DamageReport damageReport, int igniteOnKillCount, CharacterBody victimBody, TeamIndex attackerTeamIndex) {
            calls++; active++;
            int me = calls;
            try {
                if (active > maxActive) maxActive = active;
                if (chainLeft-- > 0) ProcIgniteOnKill(damageReport, igniteOnKillCount, victimBody, attackerTeamIndex);
                if (me == throwAtCall) throw new InvalidOperationException("a damage hook failed mid-blast");
                if (me == deepThrowAt) Boom();
            } finally { active--; }
        }
    }
    public class RoR2Application {
        public static event Action onFixedUpdate;
        public static void Tick() => onFixedUpdate?.Invoke();
    }
    // The real Awake copies creditMultiplier into its money waves; the real check for "stage spawner" looks for a
    // SceneDirector/DirectorCore on the same GameObject (the harness swaps in onDirectorObject).
    public class CombatDirector {
        public float creditMultiplier = 1f;
        public float moneyWaveMultiplier;
        public bool onDirectorObject;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Awake() { moneyWaveMultiplier = creditMultiplier; }
    }
    public class MasterSummon {
        public bool ignoreTeamMemberLimit;
        public TeamIndex? teamIndexOverride = TeamIndex.Monster;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public object Perform() {
            var team = teamIndexOverride ?? TeamIndex.Monster;
            if (!ignoreTeamMemberLimit) {
                var def = TeamCatalog.GetTeamDef(team);
                if (def != null && TeamComponent.GetTeamMembers(team).Count >= def.softCharacterLimit) return null;
            }
            TeamComponent.members[(int)team].Add(new TeamComponent());
            return this;
        }
    }
}

namespace RoR2 {
    public class CharacterMaster { }
    // The real OnServerCharacterDeath keeps one struct local (UniquePickup) used after the roll: the patch's new locals must not clash.
    public struct UniquePickup { public int idx; public float pad; public bool isValid => idx > 0; }
    // Util's drop-chance math (5 x log2(spawnValue + 1); the real one reads spawnValue off the victim's DeathRewards) and
    // its roll, which records what it was handed (the real one rolls Random.Range, a Unity call).
    public static class Util {
        public static float lastChance = -1f, lastLuck = -1f;
        public static CharacterMaster lastMaster;
        public static int rolls;
        public static bool lucky;   // the roll passes
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static float GetExpAdjustedDropChancePercent(float baseChancePercent, int spawnValue) =>
            baseChancePercent * (float)(Math.Log(spawnValue + 1.0) / Math.Log(2.0));
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static bool CheckRoll(float percentChance, float luck = 0f, CharacterMaster effectOriginMaster = null) {
            rolls++; lastChance = percentChance; lastLuck = luck; lastMaster = effectOriginMaster;
            return lucky && percentChance > 0f;
        }
    }
}
namespace RoR2.Artifacts {
    // Shaped like SacrificeArtifactManager.OnServerCharacterDeath (private, reached through onDeathProcServer):
    // one CheckRoll(GetExpAdjustedDropChancePercent(5, victim), 0, null), then the drop.
    public static class SacrificeArtifactManager {
        public static int spawnValue = 2, drops, next = 7, lastDropped;
        public static float lastPad;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static UniquePickup Generate() => new UniquePickup { idx = next, pad = 3.25f };
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void Drop(UniquePickup p, float velocity) { drops++; lastDropped = p.idx; lastPad = p.pad; }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void OnServerCharacterDeath(DamageReport damageReport) {
            if (damageReport == null) return;
            if (Util.CheckRoll(Util.GetExpAdjustedDropChancePercent(5f, spawnValue), 0f, null)) {
                UniquePickup pickup = Generate();
                if (pickup.isValid) Drop(pickup, 20f);
            }
        }
        public static void Kill() => OnServerCharacterDeath(new DamageReport());
    }
    // The same roll with non-default luck/master arguments: patched before the RiskyMod-style hook (the in-game order).
    public static class SacrificeHarmonyFirst {
        public static readonly CharacterMaster master = new CharacterMaster();
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void OnServerCharacterDeath(DamageReport damageReport) {
            if (Util.CheckRoll(Util.GetExpAdjustedDropChancePercent(5f, SacrificeArtifactManager.spawnValue), 1.5f, master)) SacrificeArtifactManager.drops++;
        }
    }
    // Two rolls: not the shape the patch knows, so it must be left alone.
    public static class SacrificeTwoRolls {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void OnServerCharacterDeath(DamageReport damageReport) {
            Util.CheckRoll(1f, 0f, null);
            Util.CheckRoll(Util.GetExpAdjustedDropChancePercent(5f, SacrificeArtifactManager.spawnValue), 0f, null);
        }
    }
}

// Wwise's event-ID function (FNV-1 over the lowercased name), AudioOverlapFix's per-frame filter (keyed by event ID
// only, like the real one), and Addressables' key evaluation.
public static class AkSoundEngine {
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static uint GetIDFromString(string in_pszString) {
        uint h = 2166136261;
        foreach (var ch in in_pszString.ToLowerInvariant()) { h *= 16777619; h ^= ch; }
        return h;
    }
}
namespace RoR2 {
    // Same member names and kinds as the game's DccsPool and ClassicStageInfo, for the nulldccs mode.
    public class DccsPool {
        public class PoolEntry { public object dccs; public float weight = 1f; protected bool hasBeenSerialized = true; public PoolEntry BuiltInCode() { hasBeenSerialized = false; return this; } }
        public class ConditionalPoolEntry : PoolEntry { public object[] requiredExpansions = new object[0]; }
        public class Category { public string name; public float categoryWeight = 1f; public PoolEntry[] alwaysIncluded; public ConditionalPoolEntry[] includedIfConditionsMet; public PoolEntry[] includedIfNoConditionsMet; }
        public Category[] poolCategories;
    }
    public class ClassicStageInfo {
        private DccsPool interactableDccsPool;
        public DccsPool Pool => interactableDccsPool;
        public ClassicStageInfo(DccsPool pool) { interactableDccsPool = pool; }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Awake() { }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void SetInteractableDCCSP(DccsPool pool) { interactableDccsPool = pool; }
        // What the swap does once Mono has inlined the setter: the same write, with no prefix in the way.
        public void Store(DccsPool pool) { interactableDccsPool = pool; }
    }
    public class ClassicStageInfoVariation {
        private ClassicStageInfo classicStageInfo;
        private DccsPool interactableDccsPool;
        public ClassicStageInfoVariation(ClassicStageInfo stage, DccsPool pool) { classicStageInfo = stage; interactableDccsPool = pool; }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void SwapCSIValuesWithSelf() { if (interactableDccsPool != null) classicStageInfo.Store(interactableDccsPool); }
    }
}

namespace AudioOverlapFix {
    public static class Main {
        static readonly System.Collections.Generic.HashSet<uint> thisFrame = new System.Collections.Generic.HashSet<uint>();
        public static void NextFrame() => thisFrame.Clear();
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static bool tryPlaySound(uint eventID) => thisFrame.Add(eventID);
    }
    public static class SoundEnginePatcher {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static bool shouldPostEvent(uint eventID) => Main.tryPlaySound(eventID);
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static bool shouldPostEvent(string eventName) => shouldPostEvent(AkSoundEngine.GetIDFromString(eventName));
    }
}
// Shaped like ZetAspects' EarlyDropChanceHook: slot branch, then the inventory fallback that re-reads the missing slot,
// and orig at the very end (so a crash skips the game's death handling).
public static class ZetStandIn {
    static readonly System.Collections.Generic.List<int> aspects = new System.Collections.Generic.List<int> { 7 };
    public static int dropped = -1, origCalls;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void EarlyDropChanceHook(Action<object, object> orig, object self, RoR2.CharacterBody body) {
        int idx;
        int found = -1;
        if (body.equipmentSlot != null) {
            idx = body.equipmentSlot.equipmentIndex;
            if (idx != -1 && aspects.Contains(idx)) found = RoR2.EquipmentCatalog.GetEquipmentDef(body.equipmentSlot.equipmentIndex);
        } else {
            var inv = body.inventory;
            idx = inv.currentEquipmentIndex;
            if (idx != -1 && aspects.Contains(idx)) found = RoR2.EquipmentCatalog.GetEquipmentDef(body.equipmentSlot.equipmentIndex);   // the bug
        }
        // A later, unrelated slot read (not fed to GetEquipmentDef): the bounded match must leave it alone.
        if (found == -2) found = body.equipmentSlot.equipmentIndex;
        dropped = found;
        orig(self, body);
    }
}

// ZetAspects 2.10.10's version: the author's fix uses the stored index directly.
public static class ZetFixedStandIn {
    static readonly System.Collections.Generic.List<int> aspects = new System.Collections.Generic.List<int> { 7 };
    public static int dropped = -1;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public static void EarlyDropChanceHook(Action<object, object> orig, object self, RoR2.CharacterBody body) {
        int idx;
        int found = -1;
        if (body.equipmentSlot != null) {
            idx = body.equipmentSlot.equipmentIndex;
            if (idx != -1 && aspects.Contains(idx)) found = RoR2.EquipmentCatalog.GetEquipmentDef(body.equipmentSlot.equipmentIndex);
        } else {
            var inv = body.inventory;
            idx = inv.currentEquipmentIndex;
            if (idx != -1 && aspects.Contains(idx)) found = RoR2.EquipmentCatalog.GetEquipmentDef(idx);
        }
        dropped = found;
        orig(self, body);
    }
}

public class FakeAddressablesImpl {
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public object EvaluateKey(object obj) => obj;
}

namespace SS2.Items {
    // Relic of Force's receiver, called from TakeDamageProcess before the death check; the real one NREs on a gone inventory.
    public class RelicOfForce {
        public class Behavior {
            public static bool throwNull = true;
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
            public void OnDamageDealtServer(RoR2.DamageReport report) {
                if (throwNull) throw new NullReferenceException("attacker inventory gone");
            }
        }
    }
    // Strange Can's death handler: crashes on a gone attacker (KeyNotFoundException), or with anything else on demand.
    public class StrangeCan {
        public static bool throwKeyNotFound = true, throwOther;
        public static System.Collections.Generic.List<int> attackers = new System.Collections.Generic.List<int> { 1, 2 };
        public static int lastSeen;
        // Like the real handler, the crash happens inside a foreach (a try/finally), which the guard's try must nest around.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void OnCharacterDeathGlobal(RoR2.DamageReport report) {
            int seen = 0;
            foreach (var a in attackers) {
                seen += a;
                if (throwOther) throw new InvalidOperationException("unrelated bug");
                if (throwKeyNotFound) throw new System.Collections.Generic.KeyNotFoundException("The given key 'null' was not present in the dictionary.");
            }
            lastSeen = seen;
        }
    }
}

namespace PrintToInventory {
    // Shaped like PrintToInventory 1.3.1's Awake: bail out when ShareSuite is loaded, else read config into the statics.
    public class PrintToInventory {
        public static System.Collections.Generic.Dictionary<string, object> PluginInfos =
            new System.Collections.Generic.Dictionary<string, object> { ["com.funkfrog_sipondo.sharesuite"] = null };
        public static bool affectScrappers = true, affectPrinters = true, affectCauldrons = true, affectCleanse = true, affectWanderingChef = true;
        public static bool configScrapper = true;
        public static int disabled, finished;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Awake() {
            if (PluginInfos.ContainsKey("com.funkfrog_sipondo.sharesuite")) { disabled++; return; }
            affectCauldrons = true; affectPrinters = true; affectScrappers = configScrapper; affectCleanse = true; affectWanderingChef = true;
            finished++;
        }
    }
}

namespace GeneticsArtifact {
    public delegate void orig_TakeDamage(object self, object damageInfo);
    // Shaped like GeneEngineDriver.HealthComponent_TakeDamage: orig first, then its own (crashing) bookkeeping.
    public static class GeneEngineDriver {
        public static bool crashAfter, otherAfter;
        public static int bookkeepingDone;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void HealthComponent_TakeDamage(orig_TakeDamage orig, object self, object damageInfo) {
            orig(self, damageInfo);
            if (otherAfter) throw new InvalidOperationException("a different bug");
            if (crashAfter) throw new NullReferenceException("gene tracking");
            bookkeepingDone++;
        }
    }
}

namespace EntityStates { public class FrozenState { } }

namespace EntityStates.Treebot.TreebotFlower {
    public delegate void orig_RootPulse(TreebotFlower2Projectile self);
    // Shaped like the game's flower state (game 1.5.0). OnExit: unroot every rooted body in one foreach, clear the list,
    // then the exit sound/effect and base.OnExit; rootedBodies is private and only set on the server, like the real field.
    // RootPulse: owner (private in the game; Ancient Scepter reads it through a publicized reference) is only the attacker
    // of the pulse's hits, so a gone or missing owner is fine to the game.
    public class TreebotFlower2Projectile {
        System.Collections.Generic.List<RoR2.CharacterBody> rootedBodies;
        public static int exitSounds, baseExits;
        public void Root(params RoR2.CharacterBody[] bodies) { rootedBodies = new System.Collections.Generic.List<RoR2.CharacterBody>(bodies); }
        public bool Cleared => rootedBodies == null;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void OnExit() {
            if (rootedBodies != null) {
                foreach (RoR2.CharacterBody rootedBody in rootedBodies)
                    rootedBody.RemoveBuff(RoR2.RoR2Content.Buffs.Entangle);
                rootedBodies = null;
            }
            exitSounds++;   // Util.PlaySound(exitSoundString, gameObject) and the exit effect
            baseExits++;    // base.OnExit()
        }
        public static float radius = 10f;
        public UnityEngine.GameObject owner;
        public int pulses;
        public object lastAttacker = "none yet";
        public float lastRadius;
        public void RootPulse() { pulses++; lastAttacker = owner; lastRadius = radius; }
    }
    // A future OnExit that also does something else to each body: the fix must leave it alone.
    public class ChangedFlower {
        System.Collections.Generic.List<RoR2.CharacterBody> rootedBodies;
        public void OnExit() {
            if (rootedBodies != null) {
                foreach (RoR2.CharacterBody rootedBody in rootedBodies) {
                    rootedBody.RemoveBuff(RoR2.RoR2Content.Buffs.Entangle);
                    rootedBody.RecalculateStats();
                }
                rootedBodies = null;
            }
        }
    }
    // A future OnExit whose loop also hands each body to a helper outside CharacterBody: also left alone.
    public static class FlowerHelper { public static int touched; public static void Touch(RoR2.CharacterBody b) { touched++; } }
    public class ChangedFlowerHelper {
        System.Collections.Generic.List<RoR2.CharacterBody> rootedBodies;
        public void OnExit() {
            if (rootedBodies != null) {
                foreach (RoR2.CharacterBody rootedBody in rootedBodies) {
                    rootedBody.RemoveBuff(RoR2.RoR2Content.Buffs.Entangle);
                    FlowerHelper.Touch(rootedBody);
                }
                rootedBodies = null;
            }
        }
    }
    // Same loop as 1.5.0, but the exit effect after it changed: the loop is still recognized.
    public class ChangedExitFlower {
        System.Collections.Generic.List<RoR2.CharacterBody> rootedBodies;
        public static int effects;
        public void OnExit() {
            if (rootedBodies != null) {
                foreach (RoR2.CharacterBody rootedBody in rootedBodies)
                    rootedBody.RemoveBuff(RoR2.RoR2Content.Buffs.Entangle);
                rootedBodies = null;
            }
            effects += FlowerHelper.touched;
            System.Console.Write("");
        }
    }
}

// Shaped like StandaloneAncientScepter 1.1.37's TreebotFlower2_2.On_TreebotFlower2RootPulse: C# null check on owner,
// then GetComponent<CharacterBody>().skillLocator.GetSkill(slot).skillDef, all before orig.
namespace AncientScepter {
    public class TreebotFlower2_2 {
        public const string myDef = "FireFlower2Scepter";
        public static UnityEngine.GameObject scepterOwner;   // the REX holding the scepter
        public static int debuffPasses;
        // UnityEngine.GameObject.GetComponent<T> throws NullReferenceException on a destroyed object (log: GetComponent[T] IL_0021).
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static string SkillDefOf(UnityEngine.GameObject go) {
            if (!go) throw new NullReferenceException();
            return ReferenceEquals(go, scepterOwner) ? myDef : "FireFlower2";
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        void On_TreebotFlower2RootPulse(EntityStates.Treebot.TreebotFlower.orig_RootPulse orig, EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile self) {
            UnityEngine.GameObject owner = self.owner;
            string def = (object)owner == null ? null : SkillDefOf(owner);   // reference check only, like the shipped IL_0019 dup / brtrue.s
            bool flag = def == myDef;
            float radius = EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile.radius;
            if (flag) EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile.radius *= 2f;
            orig(self);
            EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile.radius = radius;
            if (flag) debuffPasses++;
        }
        // The game reaches the hook through MonoMod's hook delegate; the harness calls it directly.
        public void Pulse(EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile flower) => On_TreebotFlower2RootPulse(f => f.RootPulse(), flower);
    }
}

namespace ShareSuite {
    // Stand-in for ShareSuite 2.12.0 GeneralHooks, plus the order the game runs them in: the Sacrifice hook, the ShareSuite
    // override at PlaceTeleporter, then the game multiplier. NoInlining because Stage is compiled before the patch goes on.
    public static class GeneralHooks {
        private static int _sacrificeOffset = 1;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void SetSacrificeOffset(Action<object> orig, object sceneDirector) { _sacrificeOffset = 2; orig(sceneDirector); }
        public static int Stage(int credits, bool sacrifice, bool optsOut = false) {
            float multiplier = 1f;
            if (sacrifice) SetSacrificeOffset(_ => { if (!optsOut) multiplier *= 0.5f; }, null);
            int credit = (int)((double)credits * 1.0 / (double)_sacrificeOffset) + 0;
            _sacrificeOffset = 1;
            return (int)Math.Floor((float)credit * multiplier);
        }
    }
}

namespace SnoresvilleMultitudes {
    // Multitudes No Price Scale's copy spawner (the real one spawns with ignoreTeamMemberLimit forced on).
    public class FakeEntry { public bool Value { get; set; } }   // ConfigEntry<bool> needs BepInEx's paths set up
    public class MultitudesNoPriceScale {
        public static FakeEntry ConfigLiftMonsterLimit { get; set; }
        public static int copiesMade;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public UnityEngine.GameObject SpawnDuplicate(RoR2.SpawnCard card, RoR2.DirectorSpawnRequest request, UnityEngine.Vector3 origin) {
            RoR2.TeamComponent.members[(int)(request.teamIndexOverride ?? RoR2.TeamIndex.Monster)].Add(new RoR2.TeamComponent());
            copiesMade++;
            return null;
        }
    }
}

namespace RoR2.Projectile {
    // Shaped like the game's ProjectileController on 1.5.0: the old ghostPrefab field is empty, the ghost lives at
    // ghostPrefabAddress (an AssetReferenceT<GameObject> in the game; any key object here).
    public class ProjectileController { public InfernoStandIn.FakeGhost ghostPrefab; public object ghostPrefabAddress; }
}
namespace InfernoStandIn {
    // GameObject/Transform can't be created outside Unity: these carry the same member names (transform, localScale).
    public class FakeTransform { public UnityEngine.Vector3 localScale { get; set; } = new UnityEngine.Vector3(1f, 1f, 1f); }
    public class FakeGhost { public FakeTransform transform { get; } = new FakeTransform(); }
    public class FakePillar {
        public FakeTransform transform { get; } = new FakeTransform();
        public RoR2.Projectile.ProjectileController controller = new RoR2.Projectile.ProjectileController();
        public T GetComponent<T>() where T : class => controller as T;
    }
}
namespace On.EntityStates.BrotherMonster {
    // MMHOOK-shaped events; add/remove push and pop like MonoMod's HookEndpoint (removing a hook that isn't there is a no-op).
    public static class WeaponSlam {
        public delegate void orig_OnEnter(object self);
        public delegate void hook_OnEnter(orig_OnEnter orig, object self);
        public static readonly System.Collections.Generic.List<hook_OnEnter> attached = new System.Collections.Generic.List<hook_OnEnter>();
        public static event hook_OnEnter OnEnter { add { attached.Add(value); } remove { int i = attached.LastIndexOf(value); if (i >= 0) attached.RemoveAt(i); } }
    }
    public static class SlideIntroState {
        public delegate void orig_OnEnter(object self);
        public delegate void hook_OnEnter(orig_OnEnter orig, object self);
        public static readonly System.Collections.Generic.List<hook_OnEnter> attached = new System.Collections.Generic.List<hook_OnEnter>();
        public static event hook_OnEnter OnEnter { add { attached.Add(value); } remove { int i = attached.LastIndexOf(value); if (i >= 0) attached.RemoveAt(i); } }
    }
    public static class FistSlam {
        public delegate void orig_OnEnter(object self);
        public delegate void hook_OnEnter(orig_OnEnter orig, object self);
        public static readonly System.Collections.Generic.List<hook_OnEnter> attached = new System.Collections.Generic.List<hook_OnEnter>();
        public static event hook_OnEnter OnEnter { add { attached.Add(value); } remove { int i = attached.LastIndexOf(value); if (i >= 0) attached.RemoveAt(i); } }
    }
}
namespace On.RoR2 {
    // Same full names as MMHOOK's delegates, for the tracer's pass-through rule.
    public static class BuffWard { public delegate void orig_BuffTeam(); public delegate void orig_Refresh_float(); }
}

namespace On.EntityStates.NullifierMonster {
    public static class DeathState {
        public delegate void orig_OnEnter(object self);
        public delegate void hook_OnEnter(orig_OnEnter orig, object self);
        public static readonly System.Collections.Generic.List<hook_OnEnter> attached = new System.Collections.Generic.List<hook_OnEnter>();
        public static event hook_OnEnter OnEnter { add { attached.Add(value); } remove { int i = attached.LastIndexOf(value); if (i >= 0) attached.RemoveAt(i); } }
    }
}
namespace On.EntityStates.VoidMegaCrab {
    public static class DeathState {
        public delegate void orig_OnEnter(object self);
        public delegate void hook_OnEnter(orig_OnEnter orig, object self);
        public static readonly System.Collections.Generic.List<hook_OnEnter> attached = new System.Collections.Generic.List<hook_OnEnter>();
        public static event hook_OnEnter OnEnter { add { attached.Add(value); } remove { int i = attached.LastIndexOf(value); if (i >= 0) attached.RemoveAt(i); } }
    }
}
namespace Inferno.Skill_Misc {
    // Shaped like Inferno 1.7.8's Hooks (built Debug, like the shipped DLL, so locals stay as stloc/ldloc pairs).
    public static class Hooks {
        public static InfernoStandIn.FakePillar pillarProjectilePrefab = new InfernoStandIn.FakePillar();
        public static bool enableMithrixChanges = true, hasFired = true;
        public static float duration = 3.5f;
        public static int waveProjectileCount;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void WeaponSlam(On.EntityStates.BrotherMonster.WeaponSlam.orig_OnEnter orig, object self) {
            if (enableMithrixChanges) {
                waveProjectileCount = 8;
                var pillar = pillarProjectilePrefab;
                pillar.transform.localScale = new UnityEngine.Vector3(4f, 4f, 4f);
                var ghostPrefab = pillar.GetComponent<RoR2.Projectile.ProjectileController>().ghostPrefab;
                ghostPrefab.transform.localScale = new UnityEngine.Vector3(4f, 4f, 4f);   // Hooks.cs:258
                hasFired = false;
            }
            duration = 3f;
            orig(self);
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void CleanupPillar(On.EntityStates.BrotherMonster.WeaponSlam.orig_OnEnter orig, object self) {
            var pillar = pillarProjectilePrefab;
            pillar.transform.localScale = new UnityEngine.Vector3(1f, 1f, 1f);
            var ghostPrefab = pillar.GetComponent<RoR2.Projectile.ProjectileController>().ghostPrefab;
            ghostPrefab.transform.localScale = new UnityEngine.Vector3(1f, 1f, 1f);
            orig(self);
        }
        public static int shards;
        public static void SlideIntroState(On.EntityStates.BrotherMonster.SlideIntroState.orig_OnEnter orig, object self) { shards += 6; orig(self); }
        public static void FistSlam(On.EntityStates.BrotherMonster.FistSlam.orig_OnEnter orig, object self) { orig(self); }
        public static void CleanupDeathState(On.EntityStates.NullifierMonster.DeathState.orig_OnEnter orig, object self) { orig(self); }
        public static void CleanupDeathState2(On.EntityStates.VoidMegaCrab.DeathState.orig_OnEnter orig, object self) { orig(self); }
    }
    public static class HookBehavior {
        // Cached delegates, like C# 11's static method-group caching in the shipped DLL: "ldsfld; dup; brtrue L; ...; L: call add_".
        static On.EntityStates.BrotherMonster.SlideIntroState.hook_OnEnter slideCache;
        static On.EntityStates.BrotherMonster.WeaponSlam.hook_OnEnter pillarCache;
        static On.EntityStates.NullifierMonster.DeathState.hook_OnEnter deathCache;
        static On.EntityStates.VoidMegaCrab.DeathState.hook_OnEnter death2Cache;
        public static void ApplyHooks() {
            On.EntityStates.BrotherMonster.FistSlam.OnEnter += Hooks.FistSlam;
            On.EntityStates.BrotherMonster.SlideIntroState.OnEnter += slideCache ??= Hooks.SlideIntroState;
            On.EntityStates.BrotherMonster.WeaponSlam.OnEnter += Hooks.WeaponSlam;
            On.EntityStates.BrotherMonster.WeaponSlam.OnEnter -= pillarCache ??= Hooks.CleanupPillar;
            On.EntityStates.NullifierMonster.DeathState.OnEnter -= deathCache ??= Hooks.CleanupDeathState;
            On.EntityStates.VoidMegaCrab.DeathState.OnEnter -= death2Cache ??= Hooks.CleanupDeathState2;
        }
        // Like the real UndoHooks: everything removed, except the SlideIntroState line (adds: the bug) and CleanupPillar (added on purpose).
        public static void UndoHooks() {
            On.EntityStates.BrotherMonster.FistSlam.OnEnter -= Hooks.FistSlam;
            On.EntityStates.BrotherMonster.SlideIntroState.OnEnter += slideCache ??= Hooks.SlideIntroState;
            On.EntityStates.BrotherMonster.WeaponSlam.OnEnter -= Hooks.WeaponSlam;
            On.EntityStates.BrotherMonster.WeaponSlam.OnEnter += pillarCache ??= Hooks.CleanupPillar;
            On.EntityStates.NullifierMonster.DeathState.OnEnter += deathCache ??= Hooks.CleanupDeathState;
            On.EntityStates.VoidMegaCrab.DeathState.OnEnter += death2Cache ??= Hooks.CleanupDeathState2;
        }
    }
}

namespace EnemiesReturns.ModdedEntityStates.Swift {
    // Stand-in for EnemiesReturns 0.7.29's Swift DeathState: same type name, field names and call shape, but Unity-free
    // (impactEffect is an object, "on the ground" is a field). Loader.Completed is shaped like the compiler's <OnEnter>b__0:
    // store impactEffect, spawn it when the bird died on the ground, then Addressables.Release the handle. SimpleEffect
    // can run the callback again from inside the spawn, the way ResourceManager.Update re-runs the completion in game.
    public class DeathState {
        public static object impactEffect;
        public static int spawns, callbackRuns;
        public static bool throwOnSpawn;
        public static System.Action reenter;
        public bool wasGrounded, onGround;
        public static void SimpleEffect(object prefab) {
            spawns++;
            var again = reenter; reenter = null; again?.Invoke();
            if (throwOnSpawn) throw new System.ArgumentException("The Object you want to instantiate is null.");
        }
        static bool Exists(object o) => o != null;
        public void FixedUpdate() {
            if (!wasGrounded && onGround) {
                if (Exists(impactEffect)) SimpleEffect(impactEffect);
                wasGrounded = true;
            }
        }
        public sealed class Loader {
            public DeathState self;
            public UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<UnityEngine.GameObject> handler;
            public void Completed(UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<UnityEngine.GameObject> result) {
                callbackRuns++;
                impactEffect = "BeetleGuardDeathImpact";
                if (self.wasGrounded) SimpleEffect(impactEffect);
                UnityEngine.AddressableAssets.Addressables.Release<UnityEngine.GameObject>(handler);   // outside Unity this throws
            }
        }
    }
}

// Stand-ins shaped like PlayerRespawnSystem 2.2.0's run-lifetime objects: the member names and accessibility that
// RespawnHookChurnFix reaches by reflection (private fields, IsActive's protected setter), Unity-free so they can be built.
namespace RespawnStandIn {
    public abstract class RespawnController {
        bool isActive;
        public static System.Collections.Generic.List<string> log = new System.Collections.Generic.List<string>();
        public bool IsActive {
            get => isActive;
            protected set { if (isActive != value) { log.Add((value ? "Activating " : "Deactivating ") + GetType().Name); isActive = value; } }
        }
        public void Force(bool active) => IsActive = active;
    }
    public class MultiUserTimers { public int cleared; public void ClearTimers() => cleared++; }
    [System.AttributeUsage(System.AttributeTargets.Class)] public class AssociatedRespawnType : System.Attribute { }
    [AssociatedRespawnType]
    public class TimedRespawnController : RespawnController {
        public static bool useTimedRespawn = true;
        public static bool IsEnabled => useTimedRespawn;
        MultiUserTimers userRespawnTimers = new MultiUserTimers();
        public MultiUserTimers Timers => userRespawnTimers;
        public void BreakTimers() => userRespawnTimers = null;
    }
    [AssociatedRespawnType] public class TeleporterRespawnController : RespawnController { public static bool IsEnabled => true; }
    [AssociatedRespawnType] public class MithrixRespawnController : RespawnController { public static bool IsEnabled => true; }
    public enum RespawnType { Timed, Teleporter, Mithrix }
    public class PlayerRespawner {
        System.Collections.Generic.Queue<object> respawnQueue = new System.Collections.Generic.Queue<object>();
        public bool IsAdvancingStage { get; set; }
        public void Enqueue(object o) => respawnQueue.Enqueue(o);
        public int Queued => respawnQueue.Count;
    }
    public class PlayerRespawnSystem {
        System.Collections.Generic.Dictionary<RespawnType, RespawnController> respawnControllers = new System.Collections.Generic.Dictionary<RespawnType, RespawnController>();
        PlayerRespawner playerRespawner = new PlayerRespawner();
        public PlayerRespawnSystem() {
            if (TimedRespawnController.IsEnabled) respawnControllers[RespawnType.Timed] = new TimedRespawnController();
            respawnControllers[RespawnType.Teleporter] = new TeleporterRespawnController();
            respawnControllers[RespawnType.Mithrix] = new MithrixRespawnController();
        }
        public PlayerRespawner Respawner => playerRespawner;
        public T Controller<T>() where T : RespawnController { foreach (var c in respawnControllers.Values) if (c is T t) return t; return null; }
        public void BreakRespawner() => playerRespawner = null;
    }
    // Shaped like the plugin's two Run hook methods whose IL the transpilers edit (never instantiated: Unity only appears in the IL).
    public class SystemComponent : UnityEngine.MonoBehaviour { }
    public class PlayerRespawnSystemPlugin : UnityEngine.MonoBehaviour {
        SystemComponent playerRespawnSystem;
        UnityEngine.GameObject uiDeathTimerController;
        void Run_Awake(object orig, object self) {
            uiDeathTimerController.SetActive(true);
            playerRespawnSystem = gameObject.AddComponent<SystemComponent>();
        }
        void Run_OnDestroy(object orig, object self) {
            if (uiDeathTimerController) uiDeathTimerController.SetActive(false);
            if (playerRespawnSystem) UnityEngine.Object.Destroy(playerRespawnSystem);
        }
    }
}

public static class Program {
    delegate void OldOrig(string lang);
    delegate void OldHook(OldOrig orig, string lang);   // pre-update signature: no bool
    static void OldHandler(OldOrig orig, string lang) { }

    static int failures;
    static void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}"); if (!ok) failures++; }

    static int Main(string[] argv) {
        // Like BepInEx: serve BepInEx's own Mono.Cecil for any version MonoMod/Harmony ask for.
        var core = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Thunderstore Mod Manager\DataFolder\RiskOfRain2\profiles\Default\BepInEx\core");
        var profile = System.IO.Path.GetDirectoryName(core);
        var managed = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
            var name = new AssemblyName(e.Name).Name;
            if (name == "Mono.Cecil") return Assembly.LoadFrom(System.IO.Path.Combine(core, "Mono.Cecil.dll"));
            foreach (var dir in new[] { managed, core, System.IO.Path.Combine(profile, "plugins") }) {
                var hit = System.IO.Directory.GetFiles(dir, name + ".dll", System.IO.SearchOption.AllDirectories);
                if (hit.Length > 0) return Assembly.LoadFrom(hit[0]);
            }
            return null;
        };
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "paladin") return PaladinPatchCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "zet") return ZetPatchCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "configmove") return ConfigMapCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "hardcap") return HardCapCheck(managed, profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "hallowed") return HallowedCheck(managed, profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "cfgsave") return ConfigSaveCheck();
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "totem") return TotemCheck(managed);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "modguards") return ModGuardsCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "anchorguard") return AnchorGuardCheck(profile, managed);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "pileup") return PileupCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "lockeditems") return LockedItemsCheck(profile, managed);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "bighit") return BigHitCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "hooktimer") return HookTimerCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "statlimit") return StatLimitCheck(managed, profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "pti") return PtiPatchCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "sacrifice") return SacrificeIlCheck(managed);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "sackeep") return SacrificeKeepCheck(managed);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "flower") return FlowerPatchCheck(managed);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "scepter") return ScepterPatchCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "soh") return SohPatchCheck(managed);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "sohcall") return SohCallCheck(managed);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "inferno") return InfernoPatchCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "churn") return InfernoChurnCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "respawn") return RespawnChurnCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "lunarpool") return LunarPoolCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "shaman") return ShamanCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "tracer") return TracerCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "lag") return LagCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "physcap") return PhysCapCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "chain") return ChainCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "blastcap") return BlastCapCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "blastguard") return BlastGuardCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "statsguard") return StatsGuardCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "overlapguard") return OverlapGuardCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "dotguard") return DotGuardCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "bulletguard") return BulletGuardCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "acolyte") return AcolyteCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "lineardamage") return LinearDamageCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "skillslots") return SkillSlotsCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "dagger") return DaggerCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "theta") return ThetaCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "bubbets") return BubbetsCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "roulette") return RouletteCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "nulldccs") return NullDccsCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "genetics") return GeneticsCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "deathtimer") return DeathTimerCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "wiring") return WiringCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "guardil") return GuardIlCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "swift") return SwiftCheck(profile);
        if (Environment.GetEnvironmentVariable("SHIELDTEST_MODE") == "sharesuite") return ShareSuiteCreditCheck(profile, managed);
        return Run();
    }

    // RiskyMod 2.12.0's Sacrifice IL hook (RiskyMod.Tweaks.Artifact.Sacrifice), same logic: the 5 becomes its chance (8 with
    // Classic Items Returns), and GetExpAdjustedDropChancePercent's result is clamped to exactly that chance when above 0.
    static int riskyMisses;
    static void RiskyModSacrifice(MonoMod.Cil.ILContext il) {
        var c = new MonoMod.Cil.ILCursor(il);
        if (c.TryGotoNext(MonoMod.Cil.MoveType.After, x => MonoMod.Cil.ILPatternMatchingExt.MatchLdcR4(x, 5f)))
            c.EmitDelegate<Func<float, float>>(orig => 8f);
        else riskyMisses++;
        if (c.TryGotoNext(MonoMod.Cil.MoveType.After, x => x.OpCode == Mono.Cecil.Cil.OpCodes.Call
                && x.Operand is Mono.Cecil.MethodReference m && m.Name == "GetExpAdjustedDropChancePercent"))
            c.EmitDelegate<Func<float, float>>(orig => { float n = orig; if (orig > 0f) { if (n < 8f) n = 8f; if (n > 8f) n = 8f; } return n; });
        else riskyMisses++;
    }
    static bool Near(float a, float b) => Math.Abs(a - b) < 1e-3f;

    // Run the real Sacrifice transpiler over the shipped RoR2.dll IL (read only, nothing is compiled or called): proves it
    // finds exactly the one CheckRoll, leaves RiskyMod's two match points (ldc.r4 5, call GetExpAdjustedDropChancePercent)
    // in place ahead of its edit, and inserts a stack-neutral park/scale/restore. Compiling the patched method needs the JIT
    // to prepare it, which .NET Framework can't do for this RoR2 method (see the full-patch attempt below), so that part is
    // only provable in game (Mono).
    static int SacrificeIlCheck(string managed) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var sac = shieldType.Assembly.GetType("CompatShield.SacrificeDropTweak");
        var ror2 = Assembly.LoadFrom(System.IO.Path.Combine(managed, "RoR2.dll"));
        var death = ror2.GetType("RoR2.Artifacts.SacrificeArtifactManager").GetMethod("OnServerCharacterDeath", BindingFlags.NonPublic | BindingFlags.Static);
        // Metadata level (Mono.Cecil, nothing resolved by the runtime): the shipped body has exactly the shape IsRoll matches.
        var cecilModule = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var cecilBody = System.Linq.Enumerable.First(System.Linq.Enumerable.First(cecilModule.Types, t => t.FullName == "RoR2.Artifacts.SacrificeArtifactManager")
            .Methods, m => m.Name == "OnServerCharacterDeath").Body.Instructions;
        var cecilRolls = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(cecilBody, ins => ins.Operand is Mono.Cecil.MethodReference mr
            && mr.Name == "CheckRoll" && mr.DeclaringType.FullName == "RoR2.Util" && mr.ReturnType.FullName == "System.Boolean" && mr.Parameters.Count == 3
            && mr.Parameters[0].ParameterType.FullName == "System.Single" && mr.Parameters[1].ParameterType.FullName == "System.Single"
            && mr.Parameters[2].ParameterType.FullName == "RoR2.CharacterMaster"));
        int cecilFive = cecilBody.IndexOf(System.Linq.Enumerable.FirstOrDefault(cecilBody, ins => ins.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_R4 && (float)ins.Operand == 5f));
        int cecilExp = cecilBody.IndexOf(System.Linq.Enumerable.FirstOrDefault(cecilBody, ins => ins.Operand is Mono.Cecil.MethodReference mr && mr.Name == "GetExpAdjustedDropChancePercent"));
        Console.WriteLine($"  cecil: {cecilBody.Count} instructions, CheckRoll(float,float,CharacterMaster) x{cecilRolls.Count} at {(cecilRolls.Count > 0 ? cecilRolls[0].Offset.ToString("x4") : "-")}, ldc.r4 5 #{cecilFive}, GetExpAdjustedDropChancePercent #{cecilExp}");
        Check("sacrifice IL (metadata): exactly one CheckRoll(float, float, CharacterMaster), after ldc.r4 5 and GetExpAdjustedDropChancePercent",
            cecilRolls.Count == 1 && cecilFive >= 0 && cecilExp > cecilFive && cecilBody.IndexOf(cecilRolls[0]) > cecilExp);
        // Which member references this runtime can't resolve (why the runtime-level checks below may be skipped).
        foreach (var ins in cecilBody) {
            if (!(ins.Operand is Mono.Cecil.MemberReference mref)) continue;
            try { ror2.ManifestModule.ResolveMember(mref.MetadataToken.ToInt32()); }
            catch (Exception e) { Console.WriteLine($"  unresolvable on .NET Framework: {mref.FullName} ({e.GetType().Name}: {e.Message})"); }
        }
        System.Collections.Generic.List<CodeInstruction> original;
        try { original = PatchProcessor.GetOriginalInstructions(death); }
        catch (TypeLoadException e) {
            // Harmony can't read the body here, so build the same instruction list from Cecil: every operand the runtime can
            // resolve is the real reflection member (CheckRoll, GetExpAdjustedDropChancePercent, ...); the one it can't
            // (CreatePickupDroplet) stays a placeholder, which the transpiler never looks at.
            Console.WriteLine("  Harmony can't read the body on .NET Framework (" + e.Message + "); building it from Cecil instead");
            var ops = new System.Collections.Generic.Dictionary<short, System.Reflection.Emit.OpCode>();
            foreach (var f in typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.GetValue(null) is System.Reflection.Emit.OpCode op) ops[op.Value] = op;
            original = new System.Collections.Generic.List<CodeInstruction>();
            foreach (var ins in cecilBody) {
                object operand = ins.Operand;
                if (operand is Mono.Cecil.MemberReference mref) {
                    try { operand = ror2.ManifestModule.ResolveMember(mref.MetadataToken.ToInt32()); } catch { operand = mref.FullName; }
                }
                else if (operand is Mono.Cecil.Cil.Instruction target) operand = "IL_" + target.Offset.ToString("x4");
                else if (operand is Mono.Cecil.Cil.VariableDefinition v) operand = "V_" + v.Index;
                original.Add(new CodeInstruction(ops[ins.OpCode.Value], operand));
            }
        }
        var gen = new System.Reflection.Emit.DynamicMethod("ilcheck", typeof(void), Type.EmptyTypes).GetILGenerator();
        var transpiler = sac.GetMethod("Transpiler", BindingFlags.NonPublic | BindingFlags.Static);
        var patched = System.Linq.Enumerable.ToList((System.Collections.Generic.IEnumerable<CodeInstruction>)transpiler.Invoke(null,
            new object[] { System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(original, ci => ci.Clone())), gen }));
        bool matched = (bool)sac.GetField("matched", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        foreach (var ci in patched) Console.WriteLine("  | " + ci);
        int five = patched.FindIndex(ci => ci.opcode == System.Reflection.Emit.OpCodes.Ldc_R4 && ci.operand is float f && f == 5f);
        int exp = patched.FindIndex(ci => ci.operand is MethodInfo m && m.Name == "GetExpAdjustedDropChancePercent");
        int scale = patched.FindIndex(ci => ci.operand is MethodInfo m && m.Name == "Scale" && m.DeclaringType == sac);
        int roll = patched.FindIndex(ci => ci.operand is MethodInfo m && m.Name == "CheckRoll");
        Check("sacrifice IL: exactly one CheckRoll in the shipped method, matched", matched && original.FindAll(ci => ci.operand is MethodInfo m && m.Name == "CheckRoll").Count == 1);
        Check("sacrifice IL: ldc.r4 5 and the GetExpAdjustedDropChancePercent call (RiskyMod's match points) untouched, before the edit",
            five >= 0 && exp > five && scale > exp && original.Exists(ci => ci.opcode == System.Reflection.Emit.OpCodes.Ldc_R4 && ci.operand is float f && f == 5f));
        Check("sacrifice IL: stloc master, stloc luck, call Scale, ldloc luck, ldloc master, call CheckRoll",
            roll == scale + 3 && patched[scale - 2].opcode == System.Reflection.Emit.OpCodes.Stloc && patched[scale - 1].opcode == System.Reflection.Emit.OpCodes.Stloc
            && patched[scale + 1].opcode == System.Reflection.Emit.OpCodes.Ldloc && patched[scale + 2].opcode == System.Reflection.Emit.OpCodes.Ldloc
            && patched[scale - 2].operand == patched[scale + 2].operand && patched[scale - 1].operand == patched[scale + 1].operand
            && ((System.Reflection.Emit.LocalBuilder)patched[scale - 2].operand).LocalType.FullName == "RoR2.CharacterMaster"
            && ((System.Reflection.Emit.LocalBuilder)patched[scale - 1].operand).LocalType == typeof(float));
        Check("sacrifice IL: nothing else changed (original count + 5)", patched.Count == original.Count + 5);
        return failures + SacrificePatchCheck(managed, ror2);
    }

    // Full patch attempt on the real RoR2.dll, then a RiskyMod-style IL hook on top, as in the game (CompatShield patches
    // before RiskyMod's Awake). Prints the final IL around the roll when the JIT can prepare the method.
    static int SacrificePatchCheck(string managed, Assembly ror2) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var sac = shieldType.Assembly.GetType("CompatShield.SacrificeDropTweak");
        Exception err = null;
        try { sac.GetMethod("Apply").Invoke(null, new object[] { new Harmony("test.sacrifice"), ror2, 0.6f }); } catch (Exception e) { err = e; }
        var death = ror2.GetType("RoR2.Artifacts.SacrificeArtifactManager").GetMethod("OnServerCharacterDeath", BindingFlags.NonPublic | BindingFlags.Static);
        var info = Harmony.GetPatchInfo(death);
        Exception riskyErr = null;
        string finalIl = null;
        try {
            new MonoMod.RuntimeDetour.ILHook(death, RiskyModSacrifice);
            new MonoMod.RuntimeDetour.ILHook(death, il => finalIl = il.ToString());   // created last, so it sees every change
        } catch (Exception e) { riskyErr = e; }
        bool stillMatched = (bool)sac.GetField("matched", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        foreach (var l in logs) Console.WriteLine("  " + l.Split('\n')[0]);
        if (riskyErr != null) Console.WriteLine("  IL hook error: " + riskyErr.GetType().Name + ": " + riskyErr.Message);
        if (finalIl != null) {
            var lines = finalIl.Split('\n');
            int g = Array.FindIndex(lines, s => s.Contains("GetExpAdjustedDropChancePercent"));
            for (int i = Math.Max(0, g - 2); g >= 0 && i < lines.Length && i <= g + 16; i++) Console.WriteLine("  | " + lines[i].TrimEnd());
        }
        // .NET Framework can't prepare (JIT) this RoR2 method at all: even a bare MonoMod IL hook fails the same way. Mono can.
        bool jitBlocked = riskyErr is TypeLoadException && logs.Exists(l => l.Contains("IL Compile Error") && l.Contains("TypeLoadException"));
        if (jitBlocked) {
            Console.WriteLine("SKIP  sacrifice real: full patch + RiskyMod-style hook compile (this .NET Framework can't JIT the RoR2 method: " + riskyErr.Message + ")");
            return failures;
        }
        Check("sacrifice real: transpiler attached to OnServerCharacterDeath", info != null && info.Transpilers.Count == 1);
        Check("sacrifice real: the one CheckRoll found, patched method compiled", err == null
            && logs.Exists(l => l.Contains("item drop chance per kill now x0.6"))
            && !logs.Exists(l => l.Contains("unexpected IL") || l.Contains("failed to apply") || l.Contains("not found")));
        Check("sacrifice real: RiskyMod-style hook on top finds both its spots, everything compiles", riskyErr == null && riskyMisses == 0 && stillMatched);
        Check("sacrifice real: final IL scales right before CheckRoll, after RiskyMod's clamp", finalIl != null
            && finalIl.IndexOf("GetExpAdjustedDropChancePercent") < finalIl.IndexOf("SacrificeDropTweak::Scale")
            && finalIl.IndexOf("SacrificeDropTweak::Scale") < finalIl.IndexOf("RoR2.Util::CheckRoll"));
        return failures;
    }

    // Apply the real PrintToInventory fix to the real PrintToInventory.dll (patch only, never call Awake): proves the
    // transpiler finds the ShareSuite check in the shipped IL and that the patched Awake compiles.
    // The wiring between Finish()'s config and each fix: ApplyModFixes dispatches by assembly name exactly once (real
    // AncientScepter.dll and Inferno.dll, patch only), and every config key is in the 'Enabled fixes' line and the gate.
    static int WiringCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        var ns = BindingFlags.NonPublic | BindingFlags.Static;
        shieldType.GetField("Log", ns).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        shieldType.GetField("harmony", ns).SetValue(null, new Harmony("test.wiring"));
        shieldType.GetField("scepterFlowerFix", ns).SetValue(null, true);
        shieldType.GetField("infernoMithrixFix", ns).SetValue(null, true);
        shieldType.GetField("infernoBoostFix", ns).SetValue(null, true);
        shieldType.GetField("infernoCooldownFix", ns).SetValue(null, true);
        shieldType.GetField("infernoHookChurnFix", ns).SetValue(null, true);
        var apply = shieldType.GetMethod("ApplyModFixes", ns);
        var scepter = Assembly.LoadFrom(System.IO.Path.Combine(profile, @"plugins\amogus_lovers-StandaloneAncientScepter\AncientScepter.dll"));
        apply.Invoke(null, new object[] { scepter });
        apply.Invoke(null, new object[] { scepter });
        var hook = scepter.GetType("AncientScepter.TreebotFlower2_2").GetMethod("On_TreebotFlower2RootPulse", BindingFlags.NonPublic | BindingFlags.Instance);
        var info = Harmony.GetPatchInfo(hook);
        Check("wiring: AncientScepter.dll reaches the scepter fix exactly once", info != null && info.Prefixes.Count == 1);
        var inferno = Assembly.LoadFrom(System.IO.Path.Combine(profile, @"plugins\prodzpod-Inferno\Inferno.dll"));
        apply.Invoke(null, new object[] { inferno });
        int first = logs.FindAll(l => l.Contains("] Inferno")).Count;
        apply.Invoke(null, new object[] { inferno });
        Check("wiring: Inferno.dll reaches the Inferno fix exactly once", first > 0 && logs.FindAll(l => l.Contains("] Inferno")).Count == first);
        Check("wiring: the same Inferno load also applies both scaling fixes (one appliedFixes key)",
            logs.Exists(l => l.Contains("Level and Difficulty Boost will also find a level cap"))
            && logs.Exists(l => l.Contains("Enemy Cooldown Reduction Scaling now shortens enemy cooldowns"))
            && !logs.Exists(l => l.Contains("Inferno scaling fix")));
        var src = System.IO.File.ReadAllText(@"C:\Users\isaia\OneDrive\Desktop\RoR2 Mod Watcher\ror2-compat-shield\CompatShield.cs");
        int e0 = src.IndexOf("Log.LogInfo($\"Enabled fixes:");
        string enabled = src.Substring(e0, src.IndexOf(");", e0) - e0);
        int g0 = src.IndexOf("if (paladinFixEnabled ||");
        string gate = src.Substring(g0, src.IndexOf("{", g0) - g0);
        var missingLog = new System.Collections.Generic.List<string>();
        var missingGate = new System.Collections.Generic.List<string>();
        int binds = 0;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(src, "((?:\\w+ = )+)cfg\\.Bind\\(ConfigMigration\\.Cat\\.\\w+, \"(\\w+)\"")) {
            binds++;
            string key = m.Groups[2].Value;
            if (!enabled.Contains(key)) missingLog.Add(key);
            foreach (var field in m.Groups[1].Value.Split(new[] { " = " }, StringSplitOptions.RemoveEmptyEntries)) {
            if (field == "hookGuard" || field == "TinkersLanguageFixEnabled") continue;
            string inGate = (field == "damageChainQueueLimit" || field == "damageChainDrainMs") ? "damageChainGuard" : (field == "blastChainMaxDepth" || field == "blastChainBudgetMs") ? "blastChainCap" : field == "spawnCredits" ? "SpawnTweakWanted" : (field == "printerWeight" || field == "scrapperWeight" || field == "combinerWeight" || field == "droneNoMinStage" || field == "droneSpawnWeight" || field == "droneHealth") ? "InteractableTweakWanted" : field == "sacrificeDropMult" ? "SacrificeTweakWanted" : (field == "sacrificeKeepList" || field == "sacrificeKeptWeight") ? "SacrificeKeepWanted" : field;
            if (!gate.Contains(inGate)) missingGate.Add(field);
            }
        }
        Check("wiring: the settings block was found", binds >= 48);
        Console.WriteLine("  config keys missing from 'Enabled fixes': " + string.Join(", ", missingLog));
        Console.WriteLine("  fix flags missing from the AssemblyLoad gate: " + string.Join(", ", missingGate));
        Check("wiring: every config key is in the 'Enabled fixes' line", missingLog.Count == 0);
        Check("wiring: every fix flag is in the AssemblyLoad gate", missingGate.Count == 0);
        return failures;
    }

    static int ConfigMapCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var fix = shieldType.Assembly.GetType("CompatShield.ConfigMigration");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var migrate = fix.GetMethod("Migrate", all);
        string Run(string input, out int moved) { var a = new object[] { input, 0 }; var r = (string)migrate.Invoke(null, a); moved = (int)a[1]; return r; }

        const string errors = "5. Error and Crash Protection";
        int count;
        string sample = "## Settings file\r\n\r\n[Fixes]\r\n\r\n## why\r\nBlastTargetGuard = true\r\nStatsRetryGuard = false\r\nOverlapHitGuard = true\r\nStunDurationFix = false\r\nSomethingNew = 7\r\n\r\n[Sacrifice]\r\nKeepInteractables = iscShrineChance, iscCasinoChest\r\n\r\n[Multitudes]\r\nMonsterLimit = 30\r\n";
        string done = Run(sample, out count);
        Check("every known setting is counted as moved", count == 6);
        Check("a merged switch is off when any old one was off", done.Contains("[" + errors + "]\nErrorLoopGuards = false"));
        Check("a merged switch appears once", System.Text.RegularExpressions.Regex.Matches(done, "ErrorLoopGuards =").Count == 1);
        Check("a plain setting keeps its value in its new section", done.Contains("[" + errors + "]\nStunDurationFix = false"));
        Check("a number keeps its value", done.Contains("[1. Spawns and Enemies]\nMonsterLimit = 30"));
        Check("a list with commas keeps its value", done.Contains("KeepInteractables = iscShrineChance, iscCasinoChest"));
        Check("an unknown setting stays where it was", done.Contains("SomethingNew = 7"));
        Check("old lines are gone", !done.Contains("BlastTargetGuard") && !done.Contains("OverlapHitGuard"));
        int again;
        string twice = Run(done, out again);
        Check("a second pass changes nothing", again == 0 && twice == done);

        string allOn = Run("[Fixes]\nAudioOverlapStopFix = true\nOrphanLoopGuard = true\nLunarPoolLoopStopFix = true\n", out count);
        Check("a merged switch is on when every old one was on", count == 3 && allOn.Contains("StuckSoundFixes = true"));
        string off = Run("[Fixes]\nGasolineChainFix = false\nDamageChainGuard = true\n", out count);
        Check("Gasoline off keeps the merged chain guard off", off.Contains("DamageChainGuard = false"));
        string both = Run("[" + errors + "]\nStunDurationFix = true\n[Fixes]\nStunDurationFix = false\n", out count);
        Check("a value already in the new section wins", both.Contains("StunDurationFix = true") && !both.Contains("StunDurationFix = false"));
        string none = Run("# only comments\n", out count);
        Check("a file with nothing to move is untouched", count == 0 && none == "# only comments\n");

        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cfgmap-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try {
            string path = System.IO.Path.Combine(dir, "local.compatshield.cfg");
            System.IO.File.WriteAllText(path, sample);
            var runIt = fix.GetMethod("Run", all);
            runIt.Invoke(null, new object[] { path });
            string backup = path + (string)fix.GetField("BackupSuffix", all).GetValue(null);
            Check("the old file is kept as a copy", System.IO.File.Exists(backup) && System.IO.File.ReadAllText(backup) == sample);
            Check("the move is logged with the count", logs.Any(l => l.Contains("moved 6 settings")));
            typeof(BepInEx.Paths).GetProperty("BepInExConfigPath").SetValue(null, System.IO.Path.Combine(dir, "BepInEx.cfg")); // ConfigFile's static constructor opens this
            var bep = new BepInEx.Configuration.ConfigFile(path, true);
            var loop = bep.Bind(errors, "ErrorLoopGuards", true, "d");
            var limit = bep.Bind("1. Spawns and Enemies", "MonsterLimit", 0, "d");
            var keepList = bep.Bind("2. Artifact of Sacrifice", "KeepInteractables", "", "d");
            Check("BepInEx accepts the new section names and reads the merged value", loop.Value == false);
            Check("BepInEx reads a moved number", limit.Value == 30);
            Check("BepInEx reads a moved list", keepList.Value == "iscShrineChance, iscCasinoChest");
            bep.Save();
            string saved = System.IO.File.ReadAllText(path);
            Check("the saved file has no empty old section", !saved.Contains("[Multitudes]") && !saved.Contains("[Sacrifice]"));
            System.IO.File.WriteAllText(path, sample.Replace("MonsterLimit = 30", "MonsterLimit = 31"));
            runIt.Invoke(null, new object[] { path });
            Check("an existing backup is not overwritten", System.IO.File.ReadAllText(backup) == sample);
        } finally { System.IO.Directory.Delete(dir, true); }

        var src = System.IO.File.ReadAllText(@"C:\Users\isaia\OneDrive\Desktop\RoR2 Mod Watcher\ror2-compat-shield\CompatShield.cs");
        var cats = fix.GetNestedType("Cat", all).GetFields(all).ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue());
        var bound = new System.Collections.Generic.HashSet<string>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(src, "cfg\\.Bind\\(ConfigMigration\\.Cat\\.(\\w+), \"(\\w+)\""))
            bound.Add(cats[m.Groups[1].Value] + "\0" + m.Groups[2].Value);
        Check("every setting is bound in a numbered category", bound.Count >= 48 && System.Text.RegularExpressions.Regex.Matches(src, "cfg\\.Bind\\(\"").Count == 0);
        var moves = (string[][])fix.GetField("Moves", all).GetValue(null);
        var targets = new System.Collections.Generic.HashSet<string>(moves.Select(m => m[2] + "\0" + (m[3].Length > 0 ? m[3] : m[1])));
        Check("every move lands on a setting that exists", targets.All(bound.Contains));
        Check("the 58 old settings land on 48 new ones", moves.Length == 58 && targets.Count == 48);
        Check("no old setting is listed twice", moves.Select(m => m[0] + "\0" + m[1]).Distinct().Count() == moves.Length);

        var live = System.IO.Path.Combine(profile, "config", "local.compatshield.cfg");
        if (!System.IO.File.Exists(live)) { Console.WriteLine("SKIP  no saved config here"); return failures; }
        string liveText = System.IO.File.ReadAllText(live);
        if (liveText.Contains("[1. Spawns and Enemies]")) { Console.WriteLine("SKIP  the saved config already uses the new layout"); return failures; }
        string liveDone = Run(liveText, out count);
        string oldKeysLeft = string.Join(",", moves.Where(m => System.Text.RegularExpressions.Regex.IsMatch(liveDone, "^" + m[1] + " = ", System.Text.RegularExpressions.RegexOptions.Multiline) && m[3].Length > 0).Select(m => m[1]).ToArray());
        Check("the saved config moves every setting it has (" + count + ")", count == moves.Length);
        Check("no replaced switch is left in the saved config", oldKeysLeft.Length == 0);
        Check("the saved enemy cap carries over", liveDone.Contains("MonsterLimit = 30"));
        Check("the saved printer boost carries over", liveDone.Contains("PrinterSpawnMultiplier = 6"));
        Check("the saved drone health carries over", liveDone.Contains("DroneHealthMultiplier = 10"));
        Check("the saved Sacrifice list carries over", liveDone.Contains("iscShrineChance, iscCasinoChest, iscEquipmentBarrel, iscLunarChest, iscTripleShopEquip"));
        Check("Paladin stays off", liveDone.Contains("PaladinMaterialsFix = false"));
        Check("merged switches stay on", new[] { "ErrorLoopGuards", "StuckSoundFixes", "HookChurnFixes", "TinkersSatchelFixes", "RexFlowerFixes", "DamageChainGuard" }.All(k => liveDone.Contains(k + " = true")));
        return failures;
    }

    // ShareSuite Sacrifice credit fix. Stand-in: one stage through the real order, unpatched first so the baseline can fail.
    // Real files: Cecil over ShareSuite.dll and RoR2.dll for what the fix assumes, then ApplyModFixes on the shipped mod.
    static int ShareSuiteCreditCheck(string profile, string managed) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        var ns = BindingFlags.NonPublic | BindingFlags.Static;
        shieldType.GetField("Log", ns).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var fix = shieldType.Assembly.GetType("CompatShield.ShareSuiteSacrificeCreditFix");
        var fixApply = fix.GetMethod("Apply");
        var hooks = typeof(ShareSuite.GeneralHooks);
        var setter = hooks.GetMethod("SetSacrificeOffset", ns);

        Check("sharesuite baseline: with Sacrifice on, ShareSuite and the game both halve, so 220 credits become 55", ShareSuite.GeneralHooks.Stage(220, true) == 55);
        Check("sharesuite baseline: with Sacrifice off, 220 credits stay 220", ShareSuite.GeneralHooks.Stage(220, false) == 220);
        Check("sharesuite baseline: a stage that opts out of Sacrifice scaling is still halved by ShareSuite", ShareSuite.GeneralHooks.Stage(220, true, true) == 110);

        fixApply.Invoke(null, new object[] { new Harmony("test.sharesuite.miss"), typeof(object).Assembly });
        Check("sharesuite: an assembly without the hooks class gets a not applied warning and no patch",
            logs.Exists(l => l.Contains("[Warning") && l.Contains("not applied")) && Harmony.GetPatchInfo(setter) == null);
        logs.Clear();

        fixApply.Invoke(null, new object[] { new Harmony("test.sharesuite"), hooks.Assembly });
        Check("sharesuite stand-in: one postfix, announced, nothing inactive",
            Harmony.GetPatchInfo(setter)?.Postfixes.Count == 1 && logs.Exists(l => l.Contains("only the game halves them"))
            && !logs.Exists(l => l.Contains("not applied") || l.Contains("failed to apply")));
        Check("sharesuite stand-in: no stage line before a Sacrifice stage runs", !logs.Exists(l => l.Contains("cancelled its extra")));
        Check("sharesuite stand-in: Sacrifice off stays 220, and that does not count as a cancel",
            ShareSuite.GeneralHooks.Stage(220, false) == 220 && !logs.Exists(l => l.Contains("cancelled its extra")));
        Check("sharesuite stand-in: only the game halves now, 220 becomes 110", ShareSuite.GeneralHooks.Stage(220, true) == 110);
        Check("sharesuite stand-in: the launch stages double too, 280 becomes 140 and 340 becomes 170",
            ShareSuite.GeneralHooks.Stage(280, true) == 140 && ShareSuite.GeneralHooks.Stage(340, true) == 170);
        Check("sharesuite stand-in: a stage that opts out keeps all 220", ShareSuite.GeneralHooks.Stage(220, true, true) == 220);
        Check("sharesuite stand-in: the stage line is logged once per launch", logs.FindAll(l => l.Contains("cancelled its extra")).Count == 1);

        var ssPath = System.IO.Path.Combine(profile, "plugins", "FunkFrog-and-Sipondo-ShareSuite", "ShareSuite", "ShareSuite.dll");
        if (!System.IO.File.Exists(ssPath)) {
            Console.WriteLine("SKIP  sharesuite real: ShareSuite.dll not found (mod disabled or moved)");
            return failures;
        }
        var ssModule = Mono.Cecil.ModuleDefinition.ReadModule(ssPath);
        var gh = ssModule.Types.First(t => t.FullName == "ShareSuite.GeneralHooks");
        var offsetDef = gh.Fields.FirstOrDefault(f => f.Name == "_sacrificeOffset");
        Check("sharesuite real: _sacrificeOffset is a static int", offsetDef != null && offsetDef.IsStatic && offsetDef.FieldType.FullName == "System.Int32");
        var setBody = gh.Methods.Single(m => m.Name == "SetSacrificeOffset").Body.Instructions.ToList();
        int store = setBody.FindIndex(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Stsfld && i.Operand is Mono.Cecil.FieldReference f && f.Name == "_sacrificeOffset");
        Check("sharesuite real: SetSacrificeOffset stores 2 in the offset", store > 0 && setBody[store - 1].OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_2);
        var setRefs = setBody.Where(i => i.Operand is Mono.Cecil.MemberReference).Select(i => (Mono.Cecil.MemberReference)i.Operand).ToList();
        foreach (var r in setRefs) Console.WriteLine("  SetSacrificeOffset uses: " + r.FullName);
        Check("sharesuite real: the patched method touches only its own field and the orig delegate (safe to compile at load)",
            setRefs.Count == 2 && setRefs.All(r => r.Name == "_sacrificeOffset" || (r.Name == "Invoke" && r.DeclaringType.Name == "orig_OnPrePopulateSceneServer")));
        var ovBody = gh.Methods.Single(m => m.Name == "InteractibleCreditOverride").Body.Instructions.ToList();
        int read = ovBody.FindIndex(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldsfld && i.Operand is Mono.Cecil.FieldReference f && f.Name == "_sacrificeOffset");
        int div = read < 0 ? -1 : ovBody.FindIndex(read, i => i.OpCode == Mono.Cecil.Cil.OpCodes.Div);
        int credit = div < 0 ? -1 : ovBody.FindIndex(div, i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "set_interactableCredit");
        Console.WriteLine($"  InteractibleCreditOverride: offset read #{read}, div #{div}, credit store #{credit}");
        Check("sharesuite real: the PlaceTeleporter hook divides the credits by the offset", read >= 0 && div > read && div <= read + 3 && credit > div);
        var foreign = gh.Methods.Single(m => m.Name == ".cctor").Body.Instructions
            .Where(i => i.Operand is Mono.Cecil.MemberReference mr && mr.DeclaringType != null
                && !mr.DeclaringType.FullName.StartsWith("ShareSuite.") && !mr.DeclaringType.FullName.StartsWith("System.")).ToList();
        Check("sharesuite real: GeneralHooks..cctor touches no Unity or game member (Harmony runs it at load)", foreign.Count == 0);

        var gameModule = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var populate = gameModule.Types.First(t => t.FullName == "RoR2.SceneDirector").Methods.First(m => m.Name == "PopulateScene").Body.Instructions.ToList();
        int place = populate.FindIndex(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "PlaceTeleporter");
        int mult = populate.FindIndex(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "get_onPopulateCreditMultiplier");
        int gameCredit = mult < 0 ? -1 : populate.FindIndex(mult, i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "set_interactableCredit");
        Console.WriteLine($"  PopulateScene: PlaceTeleporter #{place}, multiplier #{mult}, credit store #{gameCredit}");
        Check("sharesuite real: the game applies its multiplier after PlaceTeleporter, so the ShareSuite override cannot have seen a halved budget",
            place >= 0 && mult > place && gameCredit > mult);
        var sacBody = gameModule.Types.First(t => t.FullName == "RoR2.Artifacts.SacrificeArtifactManager").Methods.First(m => m.Name == "OnPrePopulateSceneServer").Body.Instructions.ToList();
        Check("sharesuite real: Sacrifice halves through that multiplier unless the stage opts out",
            sacBody.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "set_onPopulateCreditMultiplier")
            && sacBody.Any(i => i.Operand is Mono.Cecil.MemberReference m && m.Name.Contains("preventSacrificeCreditScaling")));

        shieldType.GetField("harmony", ns).SetValue(null, new Harmony("test.sharesuite.wiring"));
        var dispatch = shieldType.GetMethod("ApplyModFixes", ns);
        var realAsm = Assembly.LoadFrom(ssPath);
        logs.Clear();
        shieldType.GetField("shareSuiteSacrificeFix", ns).SetValue(null, false);
        dispatch.Invoke(null, new object[] { realAsm });
        Check("wiring: with the setting off, ShareSuite.dll is left alone", !logs.Exists(l => l.Contains("ShareSuite")));
        shieldType.GetField("shareSuiteSacrificeFix", ns).SetValue(null, true);
        dispatch.Invoke(null, new object[] { realAsm });
        int once = logs.FindAll(l => l.Contains("ShareSuite")).Count;
        dispatch.Invoke(null, new object[] { realAsm });
        foreach (var l in logs) Console.WriteLine("  " + l);
        Check("wiring: ShareSuite.dll reaches the fix exactly once", once > 0 && logs.FindAll(l => l.Contains("ShareSuite")).Count == once);
        var failed = logs.FirstOrDefault(l => l.Contains("ShareSuite Sacrifice credit fix failed to apply"));
        if (failed != null && (failed.Contains("TypeLoadException") || failed.Contains("Non-abstract, non-.cctor method in an interface")
                || failed.Contains("ECall methods must be packaged") || failed.Contains("Could not load")))
            Console.WriteLine("SKIP  sharesuite real: patched method compiles (this runtime cannot load the types it touches; only the game log can confirm)");
        else
            Check("sharesuite real: patched and compiled against the shipped mod", failed == null && logs.Exists(l => l.Contains("only the game halves them")));
        return failures;
    }

    // Swift impact fix. Stand-ins: the load callback re-run inside its own spawn must finish without reaching
    // Addressables.Release (which throws outside Unity), and a corpse with a broken effect must throw once, not every tick.
    // Real EnemiesReturns 0.7.29: both transpilers over the shipped IL (no JIT), then ApplyModFixes twice (one apply, compiled
    // if .NET Framework can load the RoR2 types the patched methods touch).
    static int SwiftCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        var ns = BindingFlags.NonPublic | BindingFlags.Static;
        shieldType.GetField("Log", ns).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var fix = shieldType.Assembly.GetType("CompatShield.SwiftImpactFix");
        var findCallback = fix.GetMethod("FindCallback", ns);
        var keepHandle = fix.GetMethod("KeepHandle", ns);
        var landFirst = fix.GetMethod("LandFirst", ns);
        string Why(string f) => (string)fix.GetField(f, ns).GetValue(null);
        bool IsRel(CodeInstruction ci) => (bool)fix.GetMethod("IsRelease", ns).Invoke(null, new object[] { ci.opcode, ci.operand });
        bool IsGroundStore(CodeInstruction ci) => ci.opcode == System.Reflection.Emit.OpCodes.Stfld && ci.operand is FieldInfo f && f.Name == "wasGrounded";
        bool IsSpawn(CodeInstruction ci) => ci.opcode == System.Reflection.Emit.OpCodes.Call && ci.operand is MethodInfo m && m.Name == "SimpleEffect";
        var DS = typeof(EnemiesReturns.ModdedEntityStates.Swift.DeathState);

        // Baseline, unpatched: what the log shows.
        EnemiesReturns.ModdedEntityStates.Swift.DeathState.impactEffect = "x";
        EnemiesReturns.ModdedEntityStates.Swift.DeathState.throwOnSpawn = true;
        var corpse = new EnemiesReturns.ModdedEntityStates.Swift.DeathState { onGround = true };
        int throws = 0;
        for (int i = 0; i < 3; i++) { try { corpse.FixedUpdate(); } catch (ArgumentException) { throws++; } }
        Check("swift baseline: a broken effect throws on every tick and the corpse never counts as landed", throws == 3 && !corpse.wasGrounded);
        var standCb = (MethodInfo)findCallback.Invoke(null, new object[] { DS });
        Check("swift stand-in: the load callback is found by shape", standCb != null && standCb.Name == "Completed");
        Check("swift stand-in baseline: the callback calls Addressables.Release once",
            standCb != null && PatchProcessor.GetOriginalInstructions(standCb).Count(IsRel) == 1);

        fix.GetMethod("Apply").Invoke(null, new object[] { new Harmony("test.swift.standin"), DS.Assembly });
        Check("swift stand-in: both parts announced, nothing inactive",
            logs.Exists(l => l.Contains("keeps its handle")) && logs.Exists(l => l.Contains("counts as landed"))
            && !logs.Exists(l => l.Contains("inactive") || l.Contains("not found") || l.Contains("failed to apply")));
        logs.Clear();

        EnemiesReturns.ModdedEntityStates.Swift.DeathState.throwOnSpawn = false;
        EnemiesReturns.ModdedEntityStates.Swift.DeathState.spawns = 0;
        EnemiesReturns.ModdedEntityStates.Swift.DeathState.callbackRuns = 0;
        EnemiesReturns.ModdedEntityStates.Swift.DeathState.impactEffect = null;
        var dead = new EnemiesReturns.ModdedEntityStates.Swift.DeathState { wasGrounded = true };
        var loader = new EnemiesReturns.ModdedEntityStates.Swift.DeathState.Loader { self = dead };
        EnemiesReturns.ModdedEntityStates.Swift.DeathState.reenter = () => loader.Completed(default);
        Exception cbErr = null;
        try { loader.Completed(default); } catch (Exception e) { cbErr = e; }
        Console.WriteLine("  re-entrant callback: " + (cbErr == null ? "no exception" : cbErr.GetType().Name + ": " + cbErr.Message));
        Check("swift stand-in: the callback re-run inside its own spawn finishes both runs without reaching Addressables.Release",
            cbErr == null && EnemiesReturns.ModdedEntityStates.Swift.DeathState.callbackRuns == 2
            && EnemiesReturns.ModdedEntityStates.Swift.DeathState.spawns == 2
            && "BeetleGuardDeathImpact".Equals(EnemiesReturns.ModdedEntityStates.Swift.DeathState.impactEffect));

        EnemiesReturns.ModdedEntityStates.Swift.DeathState.throwOnSpawn = true;
        EnemiesReturns.ModdedEntityStates.Swift.DeathState.spawns = 0;
        var broken = new EnemiesReturns.ModdedEntityStates.Swift.DeathState { onGround = true };
        throws = 0;
        for (int i = 0; i < 3; i++) { try { broken.FixedUpdate(); } catch (ArgumentException) { throws++; } }
        Check("swift stand-in: a broken effect throws once per corpse, then the corpse counts as landed",
            throws == 1 && broken.wasGrounded && EnemiesReturns.ModdedEntityStates.Swift.DeathState.spawns == 1);
        EnemiesReturns.ModdedEntityStates.Swift.DeathState.throwOnSpawn = false;
        EnemiesReturns.ModdedEntityStates.Swift.DeathState.spawns = 0;
        var landed = new EnemiesReturns.ModdedEntityStates.Swift.DeathState { onGround = true };
        landed.FixedUpdate(); landed.FixedUpdate();
        Check("swift stand-in: a working effect spawns exactly once on landing", EnemiesReturns.ModdedEntityStates.Swift.DeathState.spawns == 1 && landed.wasGrounded);
        EnemiesReturns.ModdedEntityStates.Swift.DeathState.impactEffect = null;
        var early = new EnemiesReturns.ModdedEntityStates.Swift.DeathState { onGround = true };
        early.FixedUpdate();
        var air = new EnemiesReturns.ModdedEntityStates.Swift.DeathState { onGround = false };
        air.FixedUpdate();
        Check("swift stand-in: effect not loaded yet -> no spawn but landed; still in the air -> nothing changes",
            EnemiesReturns.ModdedEntityStates.Swift.DeathState.spawns == 1 && early.wasGrounded && !air.wasGrounded);

        // The real DLL (a scratch copy by default, so the profile file is never opened).
        var erPath = Environment.GetEnvironmentVariable("SHIELDTEST_ER")
            ?? System.IO.Path.Combine(profile, @"plugins\Risky_Sleeps-EnemiesReturns\Risky_Sleeps-EnemiesReturns\EnemiesReturns.dll");
        var er = Assembly.LoadFrom(erPath);
        var state = er.GetType("EnemiesReturns.ModdedEntityStates.Swift.DeathState");
        var realCb = state == null ? null : (MethodInfo)findCallback.Invoke(null, new object[] { state });
        Console.WriteLine($"  real: {er.GetName().Name} {er.GetName().Version} from {erPath}; callback = {realCb?.DeclaringType?.Name}.{realCb?.Name}");
        Check("swift real: the one load callback found (<>c__DisplayClass2_0.<OnEnter>b__0)",
            realCb != null && realCb.Name == "<OnEnter>b__0" && realCb.DeclaringType.Name == "<>c__DisplayClass2_0");
        if (realCb != null) {
            var orig = PatchProcessor.GetOriginalInstructions(realCb);
            int rel = orig.FindIndex(ci => IsRel(ci));
            var outCb = ((System.Collections.Generic.IEnumerable<CodeInstruction>)keepHandle.Invoke(null, new object[] { orig.Select(ci => ci.Clone()).ToList() })).ToList();
            Console.WriteLine($"  real callback: {orig.Count} instructions, Release at #{rel}; after: #{rel} = {(rel >= 0 && rel < outCb.Count ? outCb[rel].opcode.Name : "-")}, why = {Why("releaseWhy") ?? "null"}");
            Check("swift real IL: Release becomes pop, nothing else changes",
                Why("releaseWhy") == null && rel >= 0 && outCb.Count == orig.Count && !outCb.Any(ci => IsRel(ci))
                && outCb[rel].opcode == System.Reflection.Emit.OpCodes.Pop
                && Enumerable.Range(0, orig.Count).All(i => i == rel || (orig[i].opcode == outCb[i].opcode && Equals(orig[i].operand, outCb[i].operand))));
        }
        var fu = state?.GetMethod("FixedUpdate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
        if (fu != null) {
            var orig = PatchProcessor.GetOriginalInstructions(fu, out var gen);
            var outFu = ((System.Collections.Generic.IEnumerable<CodeInstruction>)landFirst.Invoke(null, new object[] { orig.Select(ci => ci.Clone()).ToList() })).ToList();
            int firstStore = outFu.FindIndex(IsGroundStore), spawn = outFu.FindIndex(IsSpawn), lastStore = outFu.FindLastIndex(IsGroundStore);
            Console.WriteLine($"  real FixedUpdate: {orig.Count} -> {outFu.Count} instructions; store #{firstStore}, SimpleEffect #{spawn}, original store #{lastStore}; why = {Why("landWhy") ?? "null"}");
            for (int i = 0; i < outFu.Count; i++) Console.WriteLine($"    {i,2} {outFu[i]}");
            Check("swift real IL: 'wasGrounded = true' now also runs before the SimpleEffect call, original store kept",
                Why("landWhy") == null && outFu.Count == orig.Count + 3 && firstStore >= 0 && firstStore < spawn && lastStore > spawn
                && outFu[firstStore - 2].opcode == System.Reflection.Emit.OpCodes.Ldarg_0 && outFu[firstStore - 1].opcode == System.Reflection.Emit.OpCodes.Ldc_I4_1);
        }

        // Wiring: the real assembly through ApplyModFixes twice reaches the fix once; the patch compiles if this runtime can.
        shieldType.GetField("harmony", ns).SetValue(null, new Harmony("test.swift.wiring"));
        shieldType.GetField("swiftImpactFix", ns).SetValue(null, true);
        var apply = shieldType.GetMethod("ApplyModFixes", ns);
        apply.Invoke(null, new object[] { er });
        int once = logs.FindAll(l => l.Contains("Swift")).Count;
        apply.Invoke(null, new object[] { er });
        foreach (var l in logs) Console.WriteLine("  " + l.Split('\n')[0]);
        Check("wiring: EnemiesReturns.dll reaches the Swift fix exactly once", once > 0 && logs.FindAll(l => l.Contains("Swift")).Count == once);
        var failed = logs.FirstOrDefault(l => l.Contains("Swift impact fix failed to apply"));
        if (failed != null && (failed.Contains("TypeLoadException") || failed.Contains("Non-abstract, non-.cctor method in an interface")
                || failed.Contains("ECall methods must be packaged") || failed.Contains("Could not load")))
            Console.WriteLine("SKIP  swift real: patched methods compile (this runtime can't load the RoR2 types they touch; only the game log can confirm)");
        else
            Check("swift real: both parts patched and compiled, nothing inactive",
                failed == null && logs.Exists(l => l.Contains("keeps its handle")) && logs.Exists(l => l.Contains("counts as landed"))
                && !logs.Exists(l => l.Contains("inactive") || l.Contains("not found")));
        if (realCb != null) {
            var info = Harmony.GetPatchInfo(realCb);
            Console.WriteLine($"  patch info on the callback: {(info == null ? "none" : info.Transpilers.Count + " transpiler(s)")}");
        }
        return failures;
    }

    // Run the guards' try/catch transpilers over the real shipped methods' IL (no JIT): each must accept its method.
    static int GuardIlCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var targets = new[] {
            ("AfterOrigGuard", @"plugins\Rico-GeneticArtifact", "GeneticsArtifact.dll", "GeneticsArtifact.GeneEngineDriver", "HealthComponent_TakeDamage", typeof(NullReferenceException)),
            ("HandlerGuard", @"plugins\TeamMoonstorm-Starstorm2", "Starstorm2.dll", "SS2.Items.StrangeCan", "OnCharacterDeathGlobal", typeof(System.Collections.Generic.KeyNotFoundException)),
            ("HandlerGuard", @"plugins\TeamMoonstorm-Starstorm2", "Starstorm2.dll", "SS2.Items.RelicOfForce+Behavior", "OnDamageDealtServer", typeof(NullReferenceException)),
            ("HandlerGuard", @"plugins\itsschwer-DamageLog", "DamageLog.dll", "DamageLog.DamageLog", "Record", typeof(NullReferenceException)),
        };
        foreach (var (guard, dir, file, typeName, methodName, ex) in targets) {
            string label = $"{guard} on {typeName}.{methodName}";
            try {
                var found = System.IO.Directory.Exists(System.IO.Path.Combine(profile, dir))
                    ? System.IO.Directory.GetFiles(System.IO.Path.Combine(profile, dir), file, System.IO.SearchOption.AllDirectories) : new string[0];
                if (found.Length == 0) {
                    Console.WriteLine($"SKIP  guard IL: {label} (mod not installed or disabled: no {file})");
                    continue;
                }
                var path = found[0];
                var asm = Assembly.LoadFrom(path);
                var method = asm.GetType(typeName).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance).First(m => m.Name == methodName);
                var g = shieldType.Assembly.GetType("CompatShield." + guard);
                var guarded = (System.Collections.IDictionary)g.GetField("guarded", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                guarded[method.DeclaringType.FullName + "::" + method.Name] = ValueTuple.Create(ex, "test note");
                var original = PatchProcessor.GetOriginalInstructions(method, out var generator);
                var output = ((System.Collections.Generic.IEnumerable<CodeInstruction>)g.GetMethod("Transpiler", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { original.Select(ci => ci).ToList(), generator, method })).ToList();
                var why = (string)g.GetField("why", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                int catches = output.Count(ci => ci.blocks.Any(b => b.blockType == ExceptionBlockType.BeginCatchBlock && b.catchType == ex));
                Console.WriteLine($"  {label}: {original.Count} -> {output.Count} instructions, why={why ?? "(wrapped)"}");
                Check($"guard IL: {label} accepted and wrapped in one catch ({ex.Name})", why == null && catches == 1 && output.Count > original.Count);
                // Now really patch it, so the JIT has to accept the wrapped IL (skipped where this runtime can't load the method).
                var logs = new System.Collections.Generic.List<string>();
                var listener = new Listener(logs);
                BepInEx.Logging.Logger.Listeners.Add(listener);
                g.GetMethod("Apply").Invoke(null, new object[] { new Harmony("test.guardil." + methodName), asm, typeName, methodName, ex, "test note" });
                BepInEx.Logging.Logger.Listeners.Remove(listener);
                foreach (var l in logs) Console.WriteLine("    " + string.Join(" | ", l.Split('\n').Where(x => x.Contains("Exception") || x.Contains("guard")).Take(4)));
                var fail = logs.FirstOrDefault(l => l.Contains("failed to apply"));
                if (fail != null && (fail.Contains("TypeLoadException") || fail.Contains("Non-abstract, non-.cctor method in an interface") || fail.Contains("ECall methods must be packaged")))
                    Console.WriteLine($"SKIP  guard JIT: {label} (this .NET Framework won't JIT a method that calls Unity internals or new RoR2 types; Mono does)");
                else
                    Check($"guard JIT: {label} patched and compiled", fail == null && logs.Exists(l => l.Contains("can no longer")));
            } catch (Exception e) {
                var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                if (inner is TypeLoadException || inner is System.IO.FileNotFoundException)
                    Console.WriteLine($"SKIP  guard IL: {label} (this .NET Framework can't read it: {inner.GetType().Name}: {inner.Message})");
                else Check($"guard IL: {label} ({inner.GetType().Name}: {inner.Message})", false);
            }
        }
        return failures;
    }

    static int PtiPatchCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var asm = Assembly.LoadFrom(System.IO.Path.Combine(profile, @"plugins\Moffein-PrintToInventory\PrintToInventory.dll"));
        Exception err = null;
        try { shieldType.Assembly.GetType("CompatShield.PrintToInventoryScrapperFix").GetMethod("Apply").Invoke(null, new object[] { new Harmony("test.pti"), asm }); }
        catch (Exception e) { err = e; }
        var awake = asm.GetType("PrintToInventory.PrintToInventory").GetMethod("Awake");
        var info = Harmony.GetPatchInfo(awake);
        foreach (var l in logs) Console.WriteLine("  " + l);
        Check("pti real: transpiler + finalizer attached to Awake", info != null && info.Transpilers.Count == 1 && info.Finalizers.Count == 1);
        Check("pti real: ShareSuite check found, patched Awake compiled", err == null
            && !logs.Exists(l => l.Contains("unexpected IL") || l.Contains("failed to apply") || l.Contains("not found")));
        return failures;
    }

    // Apply the real Paladin fix to the real PaladinMod.dll (patch only, never call it): proves the
    // transpiler understands Paladin's IL and that the try/catch-wrapped method compiles.
    static int PaladinPatchCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var dll = System.IO.Directory.GetFiles(System.IO.Path.Combine(profile, @"plugins\Paladin_Alliance-PaladinMod"), "PaladinMod.dll", System.IO.SearchOption.AllDirectories)[0];
        var asm = Assembly.LoadFrom(dll);
        var harmony = new Harmony("test.paladin");
        shieldType.Assembly.GetType("CompatShield.PaladinFix").GetMethod("Apply", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { harmony, asm });
        var gather = asm.GetType("PaladinMod.Modules.Asset").GetMethod("GatherMaterials", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        var info = Harmony.GetPatchInfo(gather);
        foreach (var l in logs) Console.WriteLine("  " + l);
        Check("Paladin: transpiler + postfix attached to GatherMaterials", info != null && info.Transpilers.Count == 1 && info.Postfixes.Count == 1);
        Check("Paladin: IL understood (not left unchanged)", !logs.Exists(l => l.Contains("unexpected IL")));
        Check("Paladin: fix applied without errors", logs.Exists(l => l.Contains("material setup made crash-proof")) && !logs.Exists(l => l.Contains("failed to apply")));
        return failures;
    }

    // Apply the real ZetAspects fix to the real ZetAspects.dll (patch only, never call it): proves the transpiler finds
    // the buggy re-read in the shipped IL and that the patched method compiles.
    static int ZetPatchCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var dll = System.IO.Path.Combine(profile, @"plugins\William758-ZetAspects\ZetAspects\ZetAspects.dll");
        var asm = Assembly.LoadFrom(dll);
        var fix = shieldType.Assembly.GetType("CompatShield.ZetAspectsDropFix");
        var hook = (MethodInfo)fix.GetMethod("FindHook").Invoke(null, new object[] { asm });
        Console.WriteLine("  hook: " + (hook == null ? "NOT FOUND" : hook.DeclaringType.FullName + "::" + hook.Name));
        Exception err = null;
        try { fix.GetMethod("Apply").Invoke(null, new object[] { new Harmony("test.zet"), hook }); } catch (Exception e) { err = e; }
        foreach (var l in logs) Console.WriteLine("  " + l);
        Check("zet real: found EarlyDropChanceHook in ZetAspects.dll", hook != null);
        // Since game 1.5.0, .NET Framework can't JIT methods touching RoR2 types with default interface methods; Mono can.
        if (logs.Exists(l => l.Contains("Non-abstract, non-.cctor method in an interface"))) {
            Console.WriteLine("SKIP  zet real: patch compile (this .NET Framework can't JIT the hook against game 1.5.0; confirmed in game by the 'already fixes' log line)");
            return failures;
        }
        Check("zet real: transpiler matched the buggy re-read", logs.Exists(l => l.Contains("no longer crash its drop hook")));
        Check("zet real: patched method compiled, no errors", err == null && !logs.Exists(l => l.Contains("failed to apply") || l.Contains("unexpected IL")));
        return failures;
    }

    static int FlowerPatchCheck(string managed) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var ror2 = Assembly.LoadFrom(System.IO.Path.Combine(managed, "RoR2.dll"));
        var fix = shieldType.Assembly.GetType("CompatShield.FlowerRootFix");
        var onExit = ror2.GetType("EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile").GetMethod("OnExit");
        var shipped = PatchProcessor.ReadMethodBody(onExit);
        bool loopOk = (bool)fix.GetMethod("LoopMatches", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { shipped });
        Exception err = null;
        try { fix.GetMethod("Apply").Invoke(null, new object[] { new Harmony("test.flower"), ror2 }); } catch (Exception e) { err = e; }
        var info = Harmony.GetPatchInfo(onExit);
        Check("flower real: shipped OnExit loop recognized", loopOk);
        Check("flower real: members found, IL accepted (no not-found / unexpected-IL warning)", err == null
            && !logs.Exists(l => l.Contains("REX flower fix: expected game members not found") || l.Contains("REX flower fix: unexpected IL")));
        Check("flower real: prefix registered on OnExit; only the harness-only ECall JIT refusal stops the detour",
            info != null && info.Prefixes.Count == 1
            && logs.TrueForAll(l => !l.Contains("REX flower fix failed to apply") || l.Contains("ECall methods must be packaged into a system module")));
        return failures;
    }

    // Apply the real Ancient Scepter flower fix to the real AncientScepter.dll (patch only, never call the hook): proves the
    // hook, its self parameter and the game's owner field are found in the shipped DLLs and that the patched hook compiles.
    // (Calling it isn't possible here: TreebotFlower2Projectile's class constructor needs Unity's Animator.StringToHash.)
    static int ScepterPatchCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var asm = Assembly.LoadFrom(System.IO.Path.Combine(profile, @"plugins\amogus_lovers-StandaloneAncientScepter\AncientScepter.dll"));
        Exception err = null;
        try { shieldType.Assembly.GetType("CompatShield.ScepterFlowerFix").GetMethod("Apply").Invoke(null, new object[] { new Harmony("test.scepter"), asm }); }
        catch (Exception e) { err = e; }
        var hook = asm.GetType("AncientScepter.TreebotFlower2_2").GetMethod("On_TreebotFlower2RootPulse", BindingFlags.NonPublic | BindingFlags.Instance);
        var info = Harmony.GetPatchInfo(hook);
        var owner = hook.GetParameters()[1].ParameterType.GetField("owner", BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var l in logs) Console.WriteLine("  " + l);
        Check("scepter real: prefix attached to On_TreebotFlower2RootPulse", info != null && info.Prefixes.Count == 1);
        Check("scepter real: owner is the game flower's private GameObject field", owner != null && owner.FieldType == typeof(UnityEngine.GameObject)
            && owner.DeclaringType.FullName == "EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile");
        Check("scepter real: announced, patched hook compiled, no errors", err == null
            && logs.Exists(l => l.Contains("REX flowers keep working after their owner's body is gone"))
            && !logs.Exists(l => l.Contains("failed to apply") || l.Contains("not found")));
        return failures;
    }

    // Apply the real fix to the real RoR2.dll (patch only, never call it): every member resolves in the shipped game, the
    // ___buffDurationControllers injection binds to the real field, and the patched method compiles.
    static int SohPatchCheck(string managed) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var ror2 = Assembly.LoadFrom(System.IO.Path.Combine(managed, "RoR2.dll"));
        Exception err = null;
        try { shieldType.Assembly.GetType("CompatShield.StunDurationFix").GetMethod("Apply").Invoke(null, new object[] { new Harmony("test.soh"), ror2 }); }
        catch (Exception e) { err = e; }
        var target = ror2.GetType("RoR2.SetStateOnHurt").GetMethod("GetModifiedDuration", BindingFlags.NonPublic | BindingFlags.Instance);
        var info = Harmony.GetPatchInfo(target);
        foreach (var l in logs) Console.WriteLine("  " + l);
        Check("soh real: prefix attached to SetStateOnHurt.GetModifiedDuration", info != null && info.Prefixes.Count == 1);
        Check("soh real: members found, patched method compiled", err == null && logs.Exists(l => l.Contains("only just spawned no longer crash"))
            && !logs.Exists(l => l.Contains("not found") || l.Contains("failed to apply")));
        return failures;
    }

    // Call the real GetModifiedDuration on a real, never-started SetStateOnHurt (uninitialized, so not a live Unity object):
    // vanilla throws the log's NullReferenceException; patched, it returns the duration through the fallback path.
    static int SohCallCheck(string managed) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var ror2 = Assembly.LoadFrom(System.IO.Path.Combine(managed, "RoR2.dll"));
        var soh = ror2.GetType("RoR2.SetStateOnHurt");
        var target = soh.GetMethod("GetModifiedDuration", BindingFlags.NonPublic | BindingFlags.Instance);
        object body = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(soh);
        Exception before = null;
        try { target.Invoke(body, new object[] { 2f, typeof(string) }); } catch (TargetInvocationException e) { before = e.InnerException; }
        Check("soh call: vanilla throws NullReferenceException on a never-started body", before is NullReferenceException);
        shieldType.Assembly.GetType("CompatShield.StunDurationFix").GetMethod("Apply").Invoke(null, new object[] { new Harmony("test.sohcall"), ror2 });
        object result = null; Exception after = null;
        try { result = target.Invoke(body, new object[] { 2f, typeof(string) }); } catch (TargetInvocationException e) { after = e.InnerException; }
        foreach (var l in logs) Console.WriteLine("  " + l);
        Check("soh call: patched real method returns the unmodified duration, no throw", after == null && result is float f && f == 2f);
        return failures;
    }

    // Real Inferno.dll through the real transpilers. A Harmony patch-only check can't run here: outside Unity's Mono,
    // RoR2's ProjectileController fails to load (TypeLoadException on LoopSoundManager+SoundLoopPtr, on .NET Framework and
    // CoreCLR alike), and both hooks touch it. So read the shipped IL with Cecil, keep its opcodes, order and locals, map the
    // operands the transpilers look at to this harness's same-named stand-ins (everything else stays a string), run
    // SlamTranspiler / UndoTranspiler on that, and check the result. Also confirm the game/MMHOOK members the fix relies on.
    static int InfernoPatchCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var fix = shieldType.Assembly.GetType("CompatShield.InfernoMithrixFix");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var managed = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var inferno = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(profile, @"plugins\prodzpod-Inferno\Inferno.dll"));
        var mmhook = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(profile, @"plugins\MMHOOK\MMHOOK_RoR2.dll"));
        var ror2 = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var opcodes = new System.Collections.Generic.Dictionary<string, System.Reflection.Emit.OpCode>();
        foreach (var f in typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            opcodes[((System.Reflection.Emit.OpCode)f.GetValue(null)).Name] = (System.Reflection.Emit.OpCode)f.GetValue(null);
        object Map(object operand) {
            if (operand is Mono.Cecil.FieldReference fr)
                return (object)Type.GetType(fr.DeclaringType.FullName.Replace('/', '+'))?.GetField(fr.Name, all) ?? fr.FullName;
            if (operand is Mono.Cecil.MethodReference mr) {
                if (mr.Name == "get_transform") return typeof(InfernoStandIn.FakeGhost).GetProperty("transform").GetGetMethod();
                if (mr.Name == "set_localScale") return typeof(InfernoStandIn.FakeTransform).GetProperty("localScale").GetSetMethod();
                var t = Type.GetType(mr.DeclaringType.FullName.Replace('/', '+'));
                return (object)t?.GetMethods(all).FirstOrDefault(m => m.Name == mr.Name && m.GetParameters().Length == mr.Parameters.Count) ?? mr.FullName;
            }
            if (operand is Mono.Cecil.Cil.VariableDefinition v) return v.Index;
            return operand?.ToString();
        }
        System.Collections.Generic.List<CodeInstruction> Read(string type, string method) {
            var md = inferno.GetType(type).Methods.First(m => m.Name == method);
            return md.Body.Instructions.Select(i => new CodeInstruction(opcodes[i.OpCode.Name], Map(i.Operand))).ToList();
        }
        System.Collections.Generic.List<CodeInstruction> Transpile(string transpiler, System.Collections.Generic.List<CodeInstruction> code) =>
            ((System.Collections.Generic.IEnumerable<CodeInstruction>)fix.GetMethod(transpiler, all).Invoke(null, new object[] { code })).ToList();
        bool IsCall(CodeInstruction ci, string name) => ci.operand is MethodInfo mi && mi.Name == name;

        foreach (var name in new[] { "WeaponSlam", "CleanupPillar" }) {
            var before = Read("Inferno.Skill_Misc.Hooks", name);
            var after = Transpile("SlamTranspiler", before.Select(ci => ci.Clone()).ToList());
            int mark = after.FindIndex(ci => IsCall(ci, "OrigReached"));
            Console.WriteLine($"  {name}: {before.Count} -> {after.Count} instructions; ghost read {fix.GetField("ghostRead", all).GetValue(null)}, orig marked {fix.GetField("origMarked", all).GetValue(null)}");
            Check($"inferno real: {name} ghost read guarded, orig call marked",
                fix.GetField("ghostRead", all).GetValue(null).ToString() == "Guarded" && (bool)fix.GetField("origMarked", all).GetValue(null));
            Check($"inferno real: {name} rewritten as expected (GhostOf+castclass, TransformOf, SetLocalScale, mark right before orig)",
                !after.Exists(ci => ci.operand is FieldInfo f && f.Name == "ghostPrefab")
                && after.FindIndex(ci => IsCall(ci, "GhostOf")) is int g && g >= 0 && after[g + 1].opcode == System.Reflection.Emit.OpCodes.Castclass
                && after.Count(ci => IsCall(ci, "TransformOf")) == 1 && after.Count(ci => IsCall(ci, "SetLocalScale")) == 1
                && mark >= 0 && IsCall(after[mark + 1], "Invoke") && after.Count == before.Count + 2);
        }
        var undoBefore = Read("Inferno.Skill_Misc.HookBehavior", "UndoHooks");
        var undoAfter = Transpile("UndoTranspiler", undoBefore.Select(ci => ci.Clone()).ToList());
        bool Slide(CodeInstruction ci, string n) => ci.operand is MethodInfo mi && mi.Name == n && mi.DeclaringType.FullName == "On.EntityStates.BrotherMonster.SlideIntroState";
        bool AnyAdd(CodeInstruction ci) => (ci.opcode == System.Reflection.Emit.OpCodes.Call || ci.opcode == System.Reflection.Emit.OpCodes.Callvirt) && ((ci.operand is MethodInfo mi && mi.Name.StartsWith("add_")) || (ci.operand is string s && s.Contains("::add_")));
        Console.WriteLine($"  UndoHooks: adds {undoBefore.Count(AnyAdd)} -> {undoAfter.Count(AnyAdd)}, SlideIntroState add {undoBefore.Count(ci => Slide(ci, "add_OnEnter"))} -> {undoAfter.Count(ci => Slide(ci, "add_OnEnter"))}");
        Check("inferno real: UndoHooks had one SlideIntroState add; now it removes it, other 3 adds kept",
            undoBefore.Count(ci => Slide(ci, "add_OnEnter")) == 1 && undoAfter.Count(ci => Slide(ci, "add_OnEnter")) == 0
            && undoAfter.Count(ci => Slide(ci, "remove_OnEnter")) == 1 && undoAfter.Count(AnyAdd) == 3 && (int)fix.GetField("swapped", all).GetValue(null) == 1);

        bool Acc(CodeInstruction ci, string prefix, string type) => ci.operand is MethodInfo mi && mi.Name.StartsWith(prefix) && mi.DeclaringType.FullName == type;
        var resetTypes = new[] { "On.EntityStates.BrotherMonster.WeaponSlam", "On.EntityStates.NullifierMonster.DeathState", "On.EntityStates.VoidMegaCrab.DeathState" };
        Console.WriteLine($"  UndoHooks: {undoBefore.Count} -> {undoAfter.Count} instructions, reset hooks made single: {fix.GetField("single", all).GetValue(null)}");
        Check("inferno real: UndoHooks' 3 reset-hook adds each become dup; remove_X; add_X",
            (int)fix.GetField("single", all).GetValue(null) == 3 && undoAfter.Count == undoBefore.Count + 6
            && resetTypes.All(t => {
                int a = undoAfter.FindIndex(ci => Acc(ci, "add_", t));
                return a >= 2 && undoAfter.Count(ci => Acc(ci, "add_", t)) == 1 && Acc(undoAfter[a - 1], "remove_", t)
                    && undoAfter[a - 2].opcode == System.Reflection.Emit.OpCodes.Dup
                    && undoBefore.Count(ci => Acc(ci, "remove_", t)) + 1 == undoAfter.Count(ci => Acc(ci, "remove_", t));
            }));

        // The members the fix relies on, in the shipped game and MMHOOK.
        var slideHooks = mmhook.GetType("On.EntityStates.BrotherMonster.SlideIntroState");
        var add = slideHooks?.Methods.FirstOrDefault(m => m.Name == "add_OnEnter");
        var rem = slideHooks?.Methods.FirstOrDefault(m => m.Name == "remove_OnEnter");
        Check("inferno real: MMHOOK SlideIntroState has add_/remove_OnEnter(hook_OnEnter), both static void",
            add != null && rem != null && add.IsStatic && rem.IsStatic && rem.Parameters.Count == 1
            && rem.Parameters[0].ParameterType.FullName == add.Parameters[0].ParameterType.FullName
            && rem.Parameters[0].ParameterType.FullName == "On.EntityStates.BrotherMonster.SlideIntroState/hook_OnEnter");
        var orig = mmhook.GetType("On.EntityStates.BrotherMonster.WeaponSlam").NestedTypes.First(t => t.Name == "orig_OnEnter").Methods.First(m => m.Name == "Invoke");
        var pc = ror2.GetType("RoR2.Projectile.ProjectileController");
        Check("inferno real: game has WeaponSlam/SlideIntroState.OnEnter(), ProjectileController.ghostPrefab + ghostPrefabAddress",
            orig.Parameters.Count == 1 && orig.Parameters[0].ParameterType.FullName == "EntityStates.BrotherMonster.WeaponSlam"
            && ror2.GetType("EntityStates.BrotherMonster.WeaponSlam").Methods.Any(m => m.Name == "OnEnter" && m.Parameters.Count == 0)
            && ror2.GetType("EntityStates.BrotherMonster.SlideIntroState").Methods.Any(m => m.Name == "OnEnter" && m.Parameters.Count == 0)
            && pc.Fields.Any(f => f.Name == "ghostPrefab" && f.FieldType.FullName == "UnityEngine.GameObject")
            && pc.Fields.Any(f => f.Name == "ghostPrefabAddress" && f.FieldType.FullName.StartsWith("UnityEngine.AddressableAssets.AssetReferenceT`1")));
        return failures;
    }

    // Stand-ins for the churn fix's difficulty read: game 1.5.0 has Run.selectedDifficulty as a property, older builds as a field.
    enum FakeDifficultyIndex { Easy = 0, Inferno = 7 }
    class FakeRunProp { public FakeDifficultyIndex selectedDifficulty { get; set; } }
    class FakeRunField { public FakeDifficultyIndex selectedDifficulty; }
    static bool FakeIsInferno(FakeDifficultyIndex d) => d == FakeDifficultyIndex.Inferno;

    // Inferno hook churn fix: the keep/skip state machine with stand-ins (Inferno's real ApplyHooks can't run here, see
    // InfernoPatchCheck), the real difficulty read on stand-in runs, then the members it relies on in the shipped Inferno.dll
    // and RoR2.dll.
    // RespawnHookChurnFix. Stand-ins: the keep/reuse/destroy state machine with the Unity touch points swapped out, the per-run
    // state reset on objects shaped like the mod's, both transpilers over stand-in IL of the same shape. Real PlayerRespawnSystem
    // 2.2.0: Resolve accepts it, both transpilers over its shipped IL (no JIT), Cecil counts of the 35 hooks, then ApplyModFixes
    // twice (one apply; compiled if this runtime can load the RoR2 and UNet types the patched methods touch).
    static int RespawnChurnCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        var ns = BindingFlags.NonPublic | BindingFlags.Static;
        shieldType.GetField("Log", ns).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var fix = shieldType.Assembly.GetType("CompatShield.RespawnHookChurnFix");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        object Get(string f) => fix.GetField(f, all).GetValue(null);
        void Put(string f, object v) => fix.GetField(f, all).SetValue(null, v);
        int Count(string f) => (int)Get(f);
        var reuse = fix.GetMethod("ReuseOrAdd", all).MakeGenericMethod(typeof(object));
        object Reuse() => reuse.Invoke(null, new object[] { null });
        void Keep(object s) => fix.GetMethod("KeepOrDestroy", all).Invoke(null, new[] { s });
        void Prefix() => fix.GetMethod("AwakePrefix", all).Invoke(null, null);
        void Postfix() => fix.GetMethod("AwakePostfix", all).Invoke(null, null);
        int Logged(string text) => logs.FindAll(l => l.Contains(text)).Count;

        var alive = new System.Collections.Generic.HashSet<object>();
        var destroyed = new System.Collections.Generic.List<object>();
        var destroyedNow = new System.Collections.Generic.List<object>();
        Put("IsAlive", new Func<object, bool>(o => alive.Contains(o)));
        Put("DestroyObject", new Action<object>(o => { destroyed.Add(o); alive.Remove(o); }));
        Put("DestroyObjectNow", new Action<object>(o => { destroyedNow.Add(o); alive.Remove(o); }));
        Put("systemType", typeof(RespawnStandIn.PlayerRespawnSystem));   // the controller-set check reads this assembly's stand-in types
        Put("AddSystem", new Func<UnityEngine.GameObject, Type, object>((go, t) => { var s = new RespawnStandIn.PlayerRespawnSystem(); alive.Add(s); return s; }));

        // Run 1: nothing kept, so the mod builds its system.
        Prefix(); var s1 = Reuse() as RespawnStandIn.PlayerRespawnSystem; Postfix();
        Check("respawn: the first run builds a new system (nothing kept yet) and tracks it as the one to keep", s1 != null && Count("Built") == 1 && Get("Kept") == s1 && (bool)Get("claimed") && destroyed.Count == 0);
        // The run leaves state behind: a queued respawn, a stage transition under way, Mithrix's fight still flagged, Timed stopped.
        s1.Respawner.Enqueue("respawn");
        s1.Respawner.IsAdvancingStage = true;
        s1.Controller<RespawnStandIn.MithrixRespawnController>().Force(true);
        s1.Controller<RespawnStandIn.TimedRespawnController>().Force(false);
        RespawnStandIn.RespawnController.log.Clear();
        var timers = s1.Controller<RespawnStandIn.TimedRespawnController>().Timers;
        // Run 1 end: kept instead of destroyed, timers cleared right away.
        Keep(s1);
        Check("respawn: at run end the system is kept, not destroyed, and its user timers are cleared",
            Get("Kept") == s1 && destroyed.Count == 0 && timers.cleared == 1 && Logged("kept its respawn hooks at run end") == 1);
        // Run 2: the kept system comes back reset to what a fresh Awake leaves behind.
        Prefix(); var s2 = Reuse(); Postfix();
        Check("respawn: the next run gets the same system back (no new one built)", ReferenceEquals(s2, s1) && Count("Built") == 1 && Count("Reused") == 1);
        Check("respawn: reset = queue empty, not advancing a stage, Timed active, Mithrix inactive, timers cleared again",
            s1.Respawner.Queued == 0 && !s1.Respawner.IsAdvancingStage
            && s1.Controller<RespawnStandIn.TimedRespawnController>().IsActive && !s1.Controller<RespawnStandIn.MithrixRespawnController>().IsActive
            && !s1.Controller<RespawnStandIn.TeleporterRespawnController>().IsActive && timers.cleared == 2);
        Check("respawn: the reset goes through the mod's own IsActive setter (logs 'Activating Timed' like a fresh Awake, 'Deactivating Mithrix')",
            RespawnStandIn.RespawnController.log.Count == 2 && RespawnStandIn.RespawnController.log.Contains("Activating TimedRespawnController")
            && RespawnStandIn.RespawnController.log.Contains("Deactivating MithrixRespawnController"));
        Check("respawn: after a reuse the postfix leaves the system alone (it was claimed)", Get("Kept") == s1 && destroyed.Count == 0 && (bool)Get("claimed"));
        Keep(s1);
        Check("respawn: a run end clears the claim, so the next Run.Awake starts unclaimed", !(bool)Get("claimed"));
        Check("respawn: 'kept' and 'reused' are each logged once", Logged("kept its respawn hooks at run end") == 1 && Logged("reused its respawn hooks") == 1);
        // Simulacrum: the mod's Run.Awake hook adds nothing, so the postfix destroys the kept system (hooks come out).
        Prefix(); Postfix();
        Check("respawn: a run in an ignored game mode destroys the kept system and says so",
            destroyed.Count == 1 && ReferenceEquals(destroyed[0], s1) && Get("Kept") == null && Count("DestroyedKept") == 1 && Logged("game mode it ignores started") == 1);
        // Run 3: fresh again.
        Prefix(); var s3 = Reuse(); Postfix();
        Check("respawn: the next normal run builds a fresh system", !ReferenceEquals(s3, s1) && Count("Built") == 2 && Get("Kept") == s3);
        // A run end that never reaches KeepOrDestroy (exception earlier in Run.OnDestroy's hook chain): the live system is still reused.
        Prefix(); var s3b = Reuse(); Postfix();
        Check("respawn: after a run end that skipped the keep, the live system is reused instead of a second one built beside it",
            ReferenceEquals(s3b, s3) && Count("Built") == 2 && destroyed.Count == 1);
        // A kept system whose Unity object died (destroyed by something else) is not reused and not destroyed again.
        Keep(s3); alive.Remove(s3);
        Prefix(); var s4 = Reuse(); Postfix();
        Check("respawn: a kept system that is no longer alive is dropped and a new one built", !ReferenceEquals(s4, s3) && Count("Built") == 3 && destroyed.Count == 1 && Get("Kept") == s4);
        // Dead kept at an ignored-mode start: nothing to destroy, nothing logged.
        Keep(s4); alive.Remove(s4);
        Prefix(); Postfix();
        Check("respawn: an ignored-mode start with a dead kept system destroys and logs nothing", Get("Kept") == null && destroyed.Count == 1 && Count("DestroyedKept") == 1 && Logged("game mode it ignores started") == 1);
        // Failure paths go the mod's way: destroy and rebuild, never a half-reset system beside a fresh one.
        var bad = new RespawnStandIn.PlayerRespawnSystem(); alive.Add(bad);
        bad.Controller<RespawnStandIn.TimedRespawnController>().BreakTimers();
        Keep(bad);
        Check("respawn: a keep that fails (timers missing) destroys the system as the mod does, logged once",
            destroyed.Count == 2 && ReferenceEquals(destroyed[1], bad) && Get("Kept") == null && Logged("keeping the respawn system at run end failed") == 1);
        var bad2 = new RespawnStandIn.PlayerRespawnSystem(); alive.Add(bad2); bad2.BreakRespawner();
        Put("Kept", bad2);
        Prefix(); var s5 = Reuse(); Postfix();
        Check("respawn: a reuse that fails (respawner missing) destroys the kept system IMMEDIATELY (singleton and hooks gone before the new one) and builds a new one, logged once",
            destroyed.Count == 2 && destroyedNow.Count == 1 && ReferenceEquals(destroyedNow[0], bad2) && !ReferenceEquals(s5, bad2) && Count("Built") == 4 && Get("Kept") == s5
            && Logged("reusing the kept respawn system failed") == 1);
        Check("respawn: the keep failure and the ignored-mode path use the deferred Destroy, like the mod", destroyed.Count == 2 && ReferenceEquals(destroyed[0], s1) && ReferenceEquals(destroyed[1], bad));
        // UseTimedRespawn toggled between runs (runtime config editor): the mod would build a different controller set, so it does.
        Keep(s5);
        RespawnStandIn.TimedRespawnController.useTimedRespawn = false;
        Prefix(); var s6 = Reuse() as RespawnStandIn.PlayerRespawnSystem; Postfix();
        Check("respawn: a kept system whose enabled controllers no longer match the mod's config is destroyed now and rebuilt (Timed off)",
            !ReferenceEquals(s6, s5) && destroyedNow.Count == 2 && ReferenceEquals(destroyedNow[1], s5) && Count("Built") == 5 && Get("Kept") == s6
            && s6.Controller<RespawnStandIn.TimedRespawnController>() == null && Logged("enabled respawn controllers changed") == 1);
        Keep(s6);
        Prefix(); var s7 = Reuse(); Postfix();
        Check("respawn: with the set unchanged (Timed still off) the kept system is reused", ReferenceEquals(s7, s6) && Count("Built") == 5 && Count("Reused") == 3);
        RespawnStandIn.TimedRespawnController.useTimedRespawn = true;
        foreach (var l in logs) Console.WriteLine("  " + l.Split('\n')[0]);
        logs.Clear();

        // The transpilers over stand-in IL of the shipped shape.
        var standPlugin = typeof(RespawnStandIn.PlayerRespawnSystemPlugin);
        var standAwake = standPlugin.GetMethod("Run_Awake", all);
        var standDestroy = standPlugin.GetMethod("Run_OnDestroy", all);
        var reuseT = fix.GetMethod("ReuseTranspiler", all);
        var keepT = fix.GetMethod("KeepTranspiler", all);
        System.Collections.Generic.List<CodeInstruction> Transpile(MethodInfo t, MethodBase m) =>
            ((System.Collections.Generic.IEnumerable<CodeInstruction>)t.Invoke(null, new object[] { PatchProcessor.GetOriginalInstructions(m).Select(ci => ci.Clone()).ToList() })).ToList();
        int Diff(System.Collections.Generic.List<CodeInstruction> a, System.Collections.Generic.List<CodeInstruction> b) =>
            a.Count != b.Count ? -1 : Enumerable.Range(0, a.Count).Count(i => a[i].opcode != b[i].opcode || !Equals(a[i].operand, b[i].operand));
        bool IsReuse(CodeInstruction ci, Type sys) => ci.opcode == System.Reflection.Emit.OpCodes.Call && ci.operand is MethodInfo m && m.Name == "ReuseOrAdd"
            && m.IsGenericMethod && m.GetGenericArguments()[0] == sys && m.DeclaringType == fix;
        bool IsKeep(CodeInstruction ci) => ci.opcode == System.Reflection.Emit.OpCodes.Call && ci.operand is MethodInfo m && m.Name == "KeepOrDestroy" && m.DeclaringType == fix;
        Put("systemType", typeof(RespawnStandIn.SystemComponent));
        Put("systemField", standPlugin.GetField("playerRespawnSystem", all));
        Put("swappedAdd", false); Put("swappedDestroy", false);
        var origAwake = PatchProcessor.GetOriginalInstructions(standAwake);
        var outAwake = Transpile(reuseT, standAwake);
        int at = outAwake.FindIndex(ci => IsReuse(ci, typeof(RespawnStandIn.SystemComponent)));
        Check("respawn stand-in IL: AddComponent<T>() becomes call ReuseOrAdd<T>, the one change, right before the stfld",
            (bool)Get("swappedAdd") && at >= 0 && Diff(origAwake, outAwake) == 1 && outAwake[at + 1].opcode == System.Reflection.Emit.OpCodes.Stfld
            && origAwake[at].opcode == System.Reflection.Emit.OpCodes.Callvirt);
        var origDestroy = PatchProcessor.GetOriginalInstructions(standDestroy);
        var outDestroy = Transpile(keepT, standDestroy);
        int kd = outDestroy.FindIndex(IsKeep);
        Check("respawn stand-in IL: Object.Destroy(playerRespawnSystem) becomes call KeepOrDestroy, the one change, right after the ldfld",
            (bool)Get("swappedDestroy") && kd >= 0 && Diff(origDestroy, outDestroy) == 1 && outDestroy[kd - 1].opcode == System.Reflection.Emit.OpCodes.Ldfld);
        Put("swappedAdd", false); Put("swappedDestroy", false);
        Check("respawn stand-in IL: a method without the pattern is left unchanged and not counted as swapped",
            Diff(origDestroy, Transpile(reuseT, standDestroy)) == 0 && Diff(origAwake, Transpile(keepT, standAwake)) == 0 && !(bool)Get("swappedAdd") && !(bool)Get("swappedDestroy"));
        Check("respawn stand-in IL: a no-match before the fix is applied warns nothing", Logged("no longer where it was") == 0);
        Put("applied", true);
        Transpile(reuseT, standDestroy); Transpile(keepT, standAwake);
        Check("respawn stand-in IL: a no-match on a re-run after the fix is applied warns once", Logged("no longer where it was") == 1);
        Put("applied", false);
        var resolve = fix.GetMethod("Resolve", all);
        var argsSelf = new object[] { typeof(Program).Assembly, null, null };
        var whySelf = (string)resolve.Invoke(null, argsSelf);
        Check("respawn: Resolve refuses an assembly without the mod's types (so a renamed mod means 'not applied', not a patch)", whySelf != null && whySelf.Contains("not found"));

        // The real mod.
        var dll = System.IO.Path.Combine(profile, @"plugins\Mordrog-PlayerRespawnSystem\PlayerRespawnSystem.dll");
        if (!System.IO.File.Exists(dll)) {
            Console.WriteLine("SKIP  respawn real: PlayerRespawnSystem.dll not installed or disabled (no " + dll + ")");
            return failures;
        }
        var mod = Assembly.LoadFrom(dll);
        Console.WriteLine($"  real: {mod.GetName().Name} {mod.GetName().Version} from {dll}");
        var args = new object[] { mod, null, null };
        var why = (string)resolve.Invoke(null, args);
        var realAwake = (MethodInfo)args[1]; var realDestroy = (MethodInfo)args[2];
        Console.WriteLine("  Resolve: " + (why ?? "ok") + $"; Run_Awake = {realAwake?.Name}, Run_OnDestroy = {realDestroy?.Name}, system = {((Type)Get("systemType"))?.FullName}");
        Check("respawn real: Resolve accepts the shipped mod (plugin hooks, playerRespawnSystem, respawner, controllers, IsActive setter, timers, one AddComponent + one Destroy)",
            why == null && realAwake != null && realDestroy != null);
        if (why == null) {
            Put("swappedAdd", false); Put("swappedDestroy", false);
            var ra = PatchProcessor.GetOriginalInstructions(realAwake);
            var oa = Transpile(reuseT, realAwake);
            int rat = oa.FindIndex(ci => IsReuse(ci, (Type)Get("systemType")));
            Console.WriteLine($"  real Run_Awake: {ra.Count} instructions, AddComponent at #{rat} -> {(rat >= 0 ? oa[rat].ToString() : "-")}");
            Check("respawn real IL: Run_Awake's AddComponent<PlayerRespawnSystem>() becomes ReuseOrAdd<PlayerRespawnSystem>, nothing else changes",
                (bool)Get("swappedAdd") && rat >= 0 && Diff(ra, oa) == 1 && oa[rat + 1].opcode == System.Reflection.Emit.OpCodes.Stfld);
            var rd = PatchProcessor.GetOriginalInstructions(realDestroy);
            var od = Transpile(keepT, realDestroy);
            int rkd = od.FindIndex(IsKeep);
            Console.WriteLine($"  real Run_OnDestroy: {rd.Count} instructions, Destroy at #{rkd} -> {(rkd >= 0 ? od[rkd].ToString() : "-")}");
            Check("respawn real IL: Run_OnDestroy's Object.Destroy(playerRespawnSystem) becomes KeepOrDestroy, nothing else changes",
                (bool)Get("swappedDestroy") && rkd >= 0 && Diff(rd, od) == 1 && od[rkd - 1].opcode == System.Reflection.Emit.OpCodes.Ldfld);
        }
        // Cecil over the shipped DLL: the 35 per-run hooks are exactly the add_ calls in the run-lifetime components' Awake
        // (and their remove_ calls in OnDestroy), and the default IgnoredGameModes is Simulacrum (InfiniteTowerRun).
        var module = Mono.Cecil.ModuleDefinition.ReadModule(dll);
        var lifetime = module.Types.Where(t => t.Name == "PlayerRespawner" || t.Name == "PlayerRespawnSystem" || t.BaseType?.Name == "RespawnController").ToList();
        int Calls(Mono.Cecil.TypeDefinition t, string method, string prefix) {
            var m = t.Methods.FirstOrDefault(x => x.Name == method);
            return m == null || !m.HasBody ? 0 : m.Body.Instructions.Count(i => i.Operand is Mono.Cecil.MethodReference mr && mr.Name.StartsWith(prefix) && mr.DeclaringType.FullName.StartsWith("On."));
        }
        int adds = lifetime.Sum(t => Calls(t, "Awake", "add_")), removes = lifetime.Sum(t => Calls(t, "OnDestroy", "remove_"));
        Console.WriteLine("  Cecil: " + string.Join(", ", lifetime.Select(t => $"{t.Name} +{Calls(t, "Awake", "add_")}/-{Calls(t, "OnDestroy", "remove_")}")));
        Check("respawn real: the run-lifetime components add 35 On hooks in Awake and remove 35 in OnDestroy (2 + 4 + 8 + 7x3)", lifetime.Count == 10 && adds == 35 && removes == 35);
        var pluginT = module.GetType("PlayerRespawnSystem.PlayerRespawnSystemPlugin");
        var init = pluginT?.Methods.FirstOrDefault(m => m.Name == "InitConfig");
        var strings = init == null ? new System.Collections.Generic.List<string>() : init.Body.Instructions.Where(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr).Select(i => (string)i.Operand).ToList();
        int ig = strings.IndexOf("IgnoredGameModes");
        Check("respawn real: the mod's IgnoredGameModes default is InfiniteTowerRun (Simulacrum), the one mode where the kept system is destroyed",
            ig >= 0 && ig + 1 < strings.Count && strings[ig + 1] == "InfiniteTowerRun");
        var ctrl = module.GetType("PlayerRespawnSystem.RespawnController");
        var setter = ctrl?.Properties.FirstOrDefault(p => p.Name == "IsActive")?.SetMethod;
        Check("respawn real: RespawnController.IsActive has a protected setter (the reset uses it, so the mod's own log lines appear)", setter != null && setter.IsFamily);

        // Wiring: the real assembly through ApplyModFixes twice reaches the fix once; the patches compile if this runtime can.
        shieldType.GetField("harmony", ns).SetValue(null, new Harmony("test.respawn.wiring"));
        shieldType.GetField("respawnHookChurnFix", ns).SetValue(null, true);
        var apply = shieldType.GetMethod("ApplyModFixes", ns);
        apply.Invoke(null, new object[] { mod });
        int once = logs.FindAll(l => l.Contains("PlayerRespawnSystem") || l.Contains("Respawn hook churn")).Count;
        apply.Invoke(null, new object[] { mod });
        foreach (var l in logs) Console.WriteLine("  " + l.Split('\n')[0]);
        Check("wiring: PlayerRespawnSystem.dll reaches the respawn fix exactly once", once > 0 && logs.FindAll(l => l.Contains("PlayerRespawnSystem") || l.Contains("Respawn hook churn")).Count == once);
        var failed = logs.FirstOrDefault(l => l.Contains("Respawn hook churn fix failed to apply"));
        if (failed != null && (failed.Contains("TypeLoadException") || failed.Contains("Non-abstract, non-.cctor method in an interface")
                || failed.Contains("ECall methods must be packaged") || failed.Contains("Could not load") || failed.Contains("FileNotFoundException")))
            Console.WriteLine("SKIP  respawn real: patched methods compile (this runtime can't load the RoR2/UNet types they touch; only the game log can confirm)");
        else
            Check("respawn real: both hook methods patched and compiled, announced, nothing 'not applied'",
                failed == null && logs.Exists(l => l.Contains("are now kept between runs")) && !logs.Exists(l => l.Contains("not applied")));
        if (realAwake != null) {
            var info = Harmony.GetPatchInfo(realAwake);
            var infoD = Harmony.GetPatchInfo(realDestroy);
            Console.WriteLine($"  patch info: Run_Awake {(info == null ? "none" : $"{info.Prefixes.Count} prefix, {info.Postfixes.Count} postfix, {info.Transpilers.Count} transpiler")}; " +
                $"Run_OnDestroy {(infoD == null ? "none" : $"{infoD.Transpilers.Count} transpiler")}");
            bool clean = (info == null || info.Prefixes.Count + info.Postfixes.Count + info.Transpilers.Count == 0) && (infoD == null || infoD.Transpilers.Count == 0);
            if (failed != null && !clean && (failed.Contains("ECall methods must be packaged") || failed.Contains("TypeLoadException") || failed.Contains("Non-abstract, non.cctor")))
                Console.WriteLine("SKIP  respawn real: a failed apply leaves neither hook method patched (Harmony's Unpatch recompiles the method, which this runtime can't JIT either; Mono can)");
            else if (failed != null)
                Check("respawn real: a failed apply leaves neither hook method patched (no half-fix)", clean);
        }
        return failures;
    }

    static int InfernoChurnCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var fix = shieldType.Assembly.GetType("CompatShield.InfernoHookChurnFix");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        bool Call(string name) => (bool)fix.GetMethod(name, all).Invoke(null, null);
        int Count(string name) => (int)fix.GetField(name, all).GetValue(null);
        bool Applied() => (bool)fix.GetField("Applied", all).GetValue(null);
        int undos = 0;
        fix.GetField("runStartHooked", all).SetValue(null, true);   // no RoR2.Run here
        fix.GetField("InvokeUndo", all).SetValue(null, new Action(() => undos++));
        object infernoRun = new object(), otherRun = new object(), unknownRun = new object();
        fix.GetField("IsInfernoRun", all).SetValue(null, new Func<object, bool?>(r => r == infernoRun ? true : r == otherRun ? false : (bool?)null));
        void RunStart(object r) => fix.GetMethod("OnRunStart", all).Invoke(null, new object[] { r });

        Check("churn: before any apply, Inferno's own UndoHooks runs (prefix lets it through)", Call("UndoPrefix") && !Applied());
        Check("churn: the first ApplyHooks runs for real and the hooks count as kept", Call("ApplyPrefix") && Applied());
        Check("churn: the run-end UndoHooks is skipped while the hooks are kept", !Call("UndoPrefix") && Count("SkippedUndos") == 1 && Applied());
        Check("churn: the next Inferno run's ApplyHooks is skipped (hooks already in)", !Call("ApplyPrefix") && Count("SkippedApplies") == 1);
        RunStart(infernoRun);
        Check("churn: an Inferno run start keeps the hooks", Applied() && undos == 0);
        RunStart(unknownRun);
        Check("churn: a run whose difficulty can't be read keeps the hooks and warns once",
            Applied() && undos == 0 && logs.Count(l => l.Contains("can't tell this run's difficulty")) == 1);
        RunStart(otherRun);
        Check("churn: another difficulty's run start takes the kept hooks out through Inferno's UndoHooks",
            !Applied() && undos == 1 && logs.Exists(l => l.Contains("were removed")));
        Check("churn: after that, the next Inferno run's ApplyHooks runs for real again", Call("ApplyPrefix") && Applied() && Count("SkippedApplies") == 1);
        Check("churn: 'kept' is logged once", logs.Count(l => l.Contains("kept its enemy skill hooks")) == 1);
        foreach (var l in logs) Console.WriteLine("  " + l);

        // The real difficulty read, with Inferno's IsInferno replaced by a stand-in of the same shape.
        fix.GetField("isInferno", all).SetValue(null, typeof(Program).GetMethod(nameof(FakeIsInferno), all));
        bool? Real(object r) => (bool?)fix.GetMethod("RunIsInferno", all).Invoke(null, new object[] { r });
        Check("churn: reads selectedDifficulty as a property (game 1.5.0) or a field, through Inferno's IsInferno; null when it can't",
            Real(new FakeRunProp { selectedDifficulty = FakeDifficultyIndex.Inferno }) == true
            && Real(new FakeRunProp { selectedDifficulty = FakeDifficultyIndex.Easy }) == false
            && Real(new FakeRunField { selectedDifficulty = FakeDifficultyIndex.Inferno }) == true
            && Real(new object()) == null && Real(null) == null);

        // The members the fix relies on, in the shipped Inferno and game.
        var managed = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var inferno = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(profile, @"plugins\prodzpod-Inferno\Inferno.dll"));
        var ror2 = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var behavior = inferno.GetType("Inferno.Skill_Misc.HookBehavior");
        var applyM = behavior?.Methods.FirstOrDefault(m => m.Name == "ApplyHooks");
        var undoM = behavior?.Methods.FirstOrDefault(m => m.Name == "UndoHooks");
        int Calls(Mono.Cecil.MethodDefinition m, string prefix) => m == null ? 0 : m.Body.Instructions.Count(i => i.Operand is Mono.Cecil.MethodReference mr && mr.Name.StartsWith(prefix));
        Console.WriteLine($"  Inferno.dll: ApplyHooks {Calls(applyM, "add_")} adds + {Calls(applyM, "remove_")} removes; UndoHooks {Calls(undoM, "add_")} adds + {Calls(undoM, "remove_")} removes");
        // Shipped IL: ApplyHooks adds 82 skill hooks and removes 3 reset hooks; UndoHooks removes 81 and adds 4, because its
        // SlideIntroState line is a += (Inferno's bug, which InfernoMithrixFix's UndoTranspiler turns into a -=).
        Check("churn real: Inferno has public static HookBehavior.ApplyHooks()/UndoHooks() with the 1.7.8 shape (82+3 / 81+4)",
            applyM != null && undoM != null && applyM.IsPublic && applyM.IsStatic && !applyM.HasParameters
            && undoM.IsPublic && undoM.IsStatic && !undoM.HasParameters
            && Calls(applyM, "add_") == 82 && Calls(applyM, "remove_") == 3 && Calls(undoM, "remove_") == 81 && Calls(undoM, "add_") == 4);
        var isInf = inferno.GetType("Inferno.Main")?.Methods.FirstOrDefault(m => m.Name == "IsInferno");
        Check("churn real: Inferno has public static bool Main.IsInferno(DifficultyIndex)",
            isInf != null && isInf.IsPublic && isInf.IsStatic && isInf.ReturnType.FullName == "System.Boolean"
            && isInf.Parameters.Count == 1 && isInf.Parameters[0].ParameterType.FullName == "RoR2.DifficultyIndex");
        var runT = ror2.GetType("RoR2.Run");
        var ev = runT.Events.FirstOrDefault(e => e.Name == "onRunStartGlobal");
        var selProp = runT.Properties.FirstOrDefault(p => p.Name == "selectedDifficulty");
        var selField = runT.Fields.FirstOrDefault(f => f.Name == "selectedDifficulty");
        Console.WriteLine($"  RoR2.Run: onRunStartGlobal {ev?.EventType.FullName ?? "missing"}; selectedDifficulty is a {(selProp != null ? "property" : selField != null ? "field" : "missing member")}");
        Check("churn real: game has public static event Action<Run> Run.onRunStartGlobal",
            ev != null && ev.AddMethod.IsPublic && ev.AddMethod.IsStatic && ev.EventType.FullName == "System.Action`1<RoR2.Run>");
        Check("churn real: Run.selectedDifficulty is a readable DifficultyIndex property or field",
            (selProp != null && selProp.GetMethod != null && selProp.GetMethod.IsPublic && selProp.PropertyType.FullName == "RoR2.DifficultyIndex")
            || (selField != null && selField.IsPublic && selField.FieldType.FullName == "RoR2.DifficultyIndex"));
        return failures;
    }

    // Lunar pool loop stop fix: the name filter and the stop path with a stand-in PlaySound (a MonoBehaviour can't be made outside
    // Unity), then the real members: ShopTerminalBehavior.PreStartClient, Util.PlaySound(string, GameObject), both Wwise events in
    // the game's Global.bnk (ShortID = FNV-1 32 of the lowercased name), and WolfoFixes posting the Play without the Stop.
    static uint Fnv1(string s) { uint h = 2166136261; foreach (byte b in System.Text.Encoding.UTF8.GetBytes(s.ToLowerInvariant())) { h *= 16777619; h ^= b; } return h; }
    static int LunarPoolCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var fix = shieldType.Assembly.GetType("CompatShield.LunarPoolLoopStopFix");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        bool Pool(string n) => (bool)fix.GetMethod("IsLunarPool", all).Invoke(null, new object[] { n });
        Check("lunar pool: matches WolfoFixes' names (LunarCauldron, WhiteToGreen / ShrineCleanse) and nothing else",
            Pool("LunarCauldron, WhiteToGreen") && Pool("LunarCauldron, GreenToRed(Clone)") && Pool("ShrineCleanse") && Pool("ShrineCleanse(Clone)")
            && !Pool("Duplicator") && !Pool("LunarShopTerminal") && !Pool("") && !Pool(null));
        var posted = new System.Collections.Generic.List<string>();
        fix.GetField("PlaySound", all).SetValue(null, new Func<string, UnityEngine.GameObject, uint>((n, go) => { posted.Add(n); return 1u; }));
        fix.GetMethod("StopOn", all).Invoke(null, new object[] { null });
        Check("lunar pool: a null pool posts nothing", posted.Count == 0 && (int)fix.GetField("Stopped", all).GetValue(null) == 0);
        Check("lunar pool: the Stop event is the Play event's twin", (string)fix.GetField("StopEvent", all).GetValue(null) == "Stop_ui_obj_lunarPool_idle_loop"
            && (string)fix.GetField("PlayEvent", all).GetValue(null) == "Play_ui_obj_lunarPool_idle_loop");

        var managed = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var ror2 = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var terminal = ror2.GetType("RoR2.ShopTerminalBehavior");
        var pre = terminal?.Methods.FirstOrDefault(m => m.Name == "PreStartClient");
        var play = ror2.GetType("RoR2.Util")?.Methods.FirstOrDefault(m => m.Name == "PlaySound" && m.Parameters.Count == 2
            && m.Parameters[0].ParameterType.FullName == "System.String" && m.Parameters[1].ParameterType.FullName == "UnityEngine.GameObject");
        Check("lunar pool real: game has public ShopTerminalBehavior.PreStartClient() and public static uint Util.PlaySound(string, GameObject)",
            pre != null && pre.IsPublic && !pre.HasParameters && play != null && play.IsPublic && play.IsStatic && play.ReturnType.FullName == "System.UInt32");
        var bank = System.IO.File.ReadAllBytes(@"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\StreamingAssets\Audio\GeneratedSoundBanks\Windows\Global.bnk");
        bool InBank(string ev) { var id = BitConverter.GetBytes(Fnv1(ev)); for (int i = 0; i + 3 < bank.Length; i++) if (bank[i] == id[0] && bank[i + 1] == id[1] && bank[i + 2] == id[2] && bank[i + 3] == id[3]) return true; return false; }
        Check("lunar pool real: Global.bnk has both Play_ui_obj_lunarPool_idle_loop and Stop_ui_obj_lunarPool_idle_loop",
            InBank("Play_ui_obj_lunarPool_idle_loop") && InBank("Stop_ui_obj_lunarPool_idle_loop") && !InBank("Play_ui_obj_lunarPool_not_a_real_event"));
        var wolfo = System.IO.Path.Combine(profile, @"plugins\Wolfo-WolfFixes\WolfoFixes.dll");
        if (!System.IO.File.Exists(wolfo)) { Console.WriteLine("SKIP  lunar pool real: WolfoFixes.dll not installed"); return failures; }
        var wolfoBytes = System.IO.File.ReadAllBytes(wolfo); // a user string can sit at an odd file offset, so read both alignments
        var text = System.Text.Encoding.Unicode.GetString(wolfoBytes) + System.Text.Encoding.Unicode.GetString(wolfoBytes, 1, wolfoBytes.Length - 1);
        Check("lunar pool real: WolfoFixes posts the Play and never the Stop (the bug this fix covers)",
            text.Contains("Play_ui_obj_lunarPool_idle_loop") && !text.Contains("Stop_ui_obj_lunarPool_idle_loop"));
        return failures;
    }

    // Shaman model fix, against the real ShamanMod.dll: the shipped IL has the one dead field store, game 1.5.0 has the property,
    // FieldToPropertyReEmit builds a method from the IL on disk with the store swapped (the reflection copy can't read it), and
    // RedirectTranspiler swaps the call in CharacterBase.InitializeCharacterModel's real IL.
    static int ShamanCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var dll = System.IO.Path.Combine(profile, @"plugins\StormRiders-Shaman\ShamanMod.dll");
        if (!System.IO.File.Exists(dll)) { Console.WriteLine("SKIP  shaman: ShamanMod.dll not installed"); return failures; }
        var shaman = Assembly.LoadFrom(dll);
        var prefabs = shaman.GetType("ShamanMod.Modules.Prefabs");
        var original = prefabs.GetMethods(all).First(m => m.Name == "SetupCharacterModel" && m.GetParameters().Length == 2);
        var oneArg = prefabs.GetMethods(all).First(m => m.Name == "SetupCharacterModel" && m.GetParameters().Length == 1);
        var module = Mono.Cecil.ModuleDefinition.ReadModule(dll);
        var src = module.GetType("ShamanMod.Modules.Prefabs").Methods.First(m => m.Name == "SetupCharacterModel" && m.Parameters.Count == 2);
        int stores = src.Body.Instructions.Count(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Stfld && i.Operand is Mono.Cecil.FieldReference f && f.Name == "invisibilityCount" && f.DeclaringType.FullName == "RoR2.CharacterModel");
        Check("shaman real: the shipped SetupCharacterModel stores CharacterModel.invisibilityCount exactly once (the 1.2.0 shape)", stores == 1);
        var managed = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var ror2 = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var cm = ror2.GetType("RoR2.CharacterModel");
        Check("shaman real: game 1.5.0 has CharacterModel.invisibilityCount as a property (no field), with a public int setter",
            cm.Fields.All(f => f.Name != "invisibilityCount") && cm.Properties.Any(p => p.Name == "invisibilityCount" && p.SetMethod != null && p.SetMethod.IsPublic && p.PropertyType.FullName == "System.Int32"));
        bool reflectionCopyFails;
        try { new MonoMod.Utils.DynamicMethodDefinition(original); reflectionCopyFails = false; } catch { reflectionCopyFails = true; }
        Console.WriteLine($"  reflection-based IL copy of the original fails on this runtime: {reflectionCopyFails}");

        var setter = Type.GetType("RoR2.CharacterModel, RoR2").GetProperty("invisibilityCount", all).GetSetMethod(true);
        var reemit = shieldType.Assembly.GetType("CompatShield.FieldToPropertyReEmit");
        var args = new object[] { original, dll, "RoR2.CharacterModel", "invisibilityCount", setter, null };
        MethodInfo built = null;
        try { built = (MethodInfo)reemit.GetMethod("Build", all).Invoke(null, args); }
        catch (TargetInvocationException e) when (e.InnerException is TypeLoadException || e.InnerException is System.IO.FileNotFoundException) { Console.WriteLine("SKIP  shaman: .NET Framework can't load a type the re-emit needs: " + e.InnerException.Message.Split('\n')[0]); return failures; }
        Console.WriteLine("  re-emit: " + (built == null ? (string)args[5] : $"{built.GetType().Name} {built.Name}, {built.GetParameters().Length} params, returns {built.ReturnType.Name}"));
        Check("shaman: FieldToPropertyReEmit builds the fixed SetupCharacterModel with the original's signature",
            built != null && built.ReturnType == original.ReturnType && built.GetParameters().Select(p => p.ParameterType).SequenceEqual(original.GetParameters().Select(p => p.ParameterType)));
        var bad = new object[] { oneArg, dll, "RoR2.CharacterModel", "invisibilityCount", setter, null };
        var none = (MethodInfo)reemit.GetMethod("Build", all).Invoke(null, bad);
        Check("shaman: a method without that store is refused with a reason", none == null && ((string)bad[5] ?? "").Contains("0 times"));

        // The transpiler on InitializeCharacterModel's real IL (read with Cecil, operands resolved to the loaded assembly).
        var fix = shieldType.Assembly.GetType("CompatShield.ShamanModelFix");
        fix.GetField("Fixed", all).SetValue(null, built);
        fix.GetField("Original", all).SetValue(null, original);
        fix.GetField("Redirected", all).SetValue(null, 0);
        var opcodes = new System.Collections.Generic.Dictionary<string, System.Reflection.Emit.OpCode>();
        foreach (var f in typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            opcodes[((System.Reflection.Emit.OpCode)f.GetValue(null)).Name] = (System.Reflection.Emit.OpCode)f.GetValue(null);
        object Map(object operand) {
            if (operand is Mono.Cecil.MethodReference mr) {
                var t = shaman.GetType(mr.DeclaringType.FullName.Replace('/', '+')) ?? Type.GetType(mr.DeclaringType.FullName.Replace('/', '+') + ", " + mr.DeclaringType.Scope.Name);
                return (object)t?.GetMethods(all).FirstOrDefault(m => m.Name == mr.Name && m.GetParameters().Length == mr.Parameters.Count) ?? mr.FullName;
            }
            if (operand is Mono.Cecil.FieldReference fr) return (object)shaman.GetType(fr.DeclaringType.FullName.Replace('/', '+'))?.GetField(fr.Name, all) ?? fr.FullName;
            return operand?.ToString();
        }
        var initDef = module.GetType("ShamanMod.Modules.Characters.CharacterBase").Methods.First(m => m.Name == "InitializeCharacterModel");
        var code = initDef.Body.Instructions.Select(i => new CodeInstruction(opcodes[i.OpCode.Name], Map(i.Operand))).ToList();
        var after = ((System.Collections.Generic.IEnumerable<CodeInstruction>)fix.GetMethod("RedirectTranspiler", all).Invoke(null, new object[] { code })).ToList();
        Check("shaman: RedirectTranspiler swaps the one SetupCharacterModel call in InitializeCharacterModel for the re-emitted method, nothing else",
            (int)fix.GetField("Redirected", all).GetValue(null) == 1 && after.Count == code.Count
            && after.Count(ci => ci.operand is MethodInfo mi && mi == built) == 1 && !after.Any(ci => ci.operand is MethodInfo mi2 && mi2 == original));

        // The body-name clash: the game now ships AcolyteBody, Shaman's AcolyteCharacter uses the same name, and RenamePrefix
        // turns it into ShamanAcolyteBody on the shared BodyInfo (stand-in here) while leaving other names alone.
        var keys = System.IO.File.ReadAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(Program).Assembly.Location), @"..\..\..\..\shielddata\catalog_keys.txt"));
        var acolyteCtor = module.GetType("ShamanMod.Modules.Characters.AcolyteCharacter").Methods.First(m => m.IsConstructor && !m.IsStatic);
        Check("shaman real: the game has RoR2/DLC4/Acolyte/AcolyteBody.prefab and Shaman's AcolyteCharacter names its body 'AcolyteBody' (the clash)",
            keys.Contains("RoR2/DLC4/Acolyte/AcolyteBody.prefab") && acolyteCtor.Body.Instructions.Any(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr && (string)i.Operand == "AcolyteBody"));
        var create = prefabs.GetMethods(all).FirstOrDefault(m => m.Name == "CreateBodyPrefab" && m.GetParameters().Length == 3 && m.GetParameters()[2].ParameterType.Name == "BodyInfo");
        var bodyInfoType = create?.GetParameters()[2].ParameterType;
        Console.WriteLine($"  BodyInfo type: {bodyInfoType?.FullName ?? "(not found)"}");
        Check("shaman real: Prefabs.CreateBodyPrefab(string, string, BodyInfo) and BodyInfo.bodyName exist (BodyInfo is in ShamanMod.Modules.Characters)",
            create != null && create.IsStatic && bodyInfoType?.FullName == "ShamanMod.Modules.Characters.BodyInfo" && bodyInfoType.GetField("bodyName", all)?.FieldType == typeof(string));
        fix.GetField("bodyNameField", all).SetValue(null, typeof(FakeBodyInfo).GetField("bodyName"));
        fix.GetField("Renamed", all).SetValue(null, 0);
        var acolyte = new FakeBodyInfo { bodyName = "AcolyteBody" }; var shamanBody = new FakeBodyInfo { bodyName = "ShamanBody" }; var angel = new FakeBodyInfo { bodyName = "AcolyteAngelBody" };
        foreach (var b in new object[] { acolyte, shamanBody, angel, null }) fix.GetMethod("RenamePrefix", all).Invoke(null, new object[] { b });
        Check("shaman: RenamePrefix renames only the clashing AcolyteBody to ShamanAcolyteBody (ShamanBody, AcolyteAngelBody and null untouched)",
            acolyte.bodyName == "ShamanAcolyteBody" && shamanBody.bodyName == "ShamanBody" && angel.bodyName == "AcolyteAngelBody" && (int)fix.GetField("Renamed", all).GetValue(null) == 1);
        return failures;
    }
    class FakeBodyInfo { public string bodyName; }

    // Sound tracer: the bookkeeping with string stand-ins for GameObjects (IsGone/Now swapped), the run-end report, the name
    // table from the real SoundbanksInfo.xml, and the real AkSoundEngine surface it patches.
    // Every UnityEngine.* method called by `root` or by anything it calls inside `own` (helpers, lambdas, nested types), as
    // "Caller -> Type.Method" strings. Methods are only followed through call/callvirt/newobj operands.
    static System.Collections.Generic.List<string> UnityCallsReachableFrom(MethodBase root, Assembly own) {
        var seen = new System.Collections.Generic.HashSet<MethodBase>();
        var queue = new System.Collections.Generic.Queue<MethodBase>();
        var hits = new System.Collections.Generic.List<string>();
        queue.Enqueue(root); seen.Add(root);
        while (queue.Count > 0) {
            var m = queue.Dequeue();
            System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<System.Reflection.Emit.OpCode, object>> body;
            try { body = PatchProcessor.ReadMethodBody(m).ToList(); } catch { continue; }
            foreach (var kv in body) {
                // Direct calls only: a delegate target (ldftn, e.g. Gasoline's onFixedUpdate handler) runs later, off the load path.
                if (kv.Key != System.Reflection.Emit.OpCodes.Call && kv.Key != System.Reflection.Emit.OpCodes.Callvirt && kv.Key != System.Reflection.Emit.OpCodes.Newobj) continue;
                if (!(kv.Value is MethodBase callee) || callee.DeclaringType == null) continue;
                var ns = callee.DeclaringType.Namespace ?? "";
                if (ns == "UnityEngine" || ns.StartsWith("UnityEngine.")) hits.Add(m.DeclaringType.Name + "." + m.Name + " -> " + callee.DeclaringType.Name + "." + callee.Name);
                else if (callee.DeclaringType.Assembly == own && seen.Add(callee)) queue.Enqueue(callee);
            }
        }
        return hits;
    }

    // Lag tracer: the window bookkeeping with a fake clock, scene and live counts; the Info/Debug split; the stage-load grace; the
    // immediate big-hitch line and its rate limit; the run summary; and the real game members it times and reads.
    static int LagCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var lag = shieldType.Assembly.GetType("CompatShield.LagTracer");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        float now = 0f; string scene = "golemplains"; int gen0 = 0; int monsters = 12;
        lag.GetMethod("ResetForTest", all).Invoke(null, null);
        lag.GetField("Now", all).SetValue(null, new Func<float>(() => now));
        lag.GetField("Scene", all).SetValue(null, new Func<string>(() => scene));
        lag.GetField("Gen0", all).SetValue(null, new Func<int>(() => gen0));
        lag.GetField("Heap", all).SetValue(null, new Func<long>(() => 1500L << 20));
        // Memory readings (1.4.54): off for the first checks (Windows gave nothing), scripted further down.
        var mem = new CompatShield.LagTracer.Mem();
        long faultsNow = -1;
        lag.GetField("Memory", all).SetValue(null, new Func<CompatShield.LagTracer.Mem>(() => { var m = mem; if (faultsNow >= 0) m.Faults = (uint)faultsNow; return m; }));
        lag.GetField("FaultCount", all).SetValue(null, new Func<long>(() => faultsNow));
        lag.GetField("RunClock", all).SetValue(null, new Func<string>(() => "stage 3 t=7:05"));
        lag.GetField("Counts", all).SetValue(null, new Func<CompatShield.LagTracer.Live>(() => new CompatShield.LagTracer.Live { Monsters = monsters, Bodies = monsters + 3, Projectiles = 40 }));
        lag.GetField("ProjectileNames", all).SetValue(null, new Func<System.Collections.Generic.IEnumerable<string>>(() => new[] { "FMJ", "FMJ", "Missile" }));   // the stub counts say 40 live, so the census runs: feed it names
        var frameSample = new CompatShield.LagTracer.FrameSample();   // Ok=false: the player exposes no frame timing
        lag.GetField("Realtime", all).SetValue(null, new Func<float>(() => now));
        var endOfFrame = lag.GetMethod("EndOfFrame", all);
        lag.GetField("FrameTimes", all).SetValue(null, new Func<CompatShield.LagTracer.FrameSample>(() => frameSample));
        var fixedStep = lag.GetMethod("FixedStep", all);
        var frame = lag.GetMethod("Frame", all);
        void F(float dtMs) { now += dtMs / 1000f; frame.Invoke(null, new object[] { dtMs / 1000f }); }
        int W() => (int)lag.GetProperty("Windows", all).GetValue(null);
        System.Collections.Generic.List<string> Lag() => logs.Where(l => l.Contains("] Lag")).ToList();
        var sink = lag.GetMethod("AddSinkForTest", all).Invoke(null, new object[] { "RecalculateStats" });
        var winTicks = sink.GetType().GetField("WinTicks"); var winCalls = sink.GetType().GetField("WinCalls");

        for (int i = 0; i < 100; i++) F(16.7f);
        Check("lag: nothing is measured outside a run", Lag().Count == 0 && W() == 0);
        // 1.4.14/1.4.15 crashed at launch: patching BulletAttack.Fire at load ran BulletAttack's static constructor, which calls
        // Unity's physics. Apply may only queue; the Harmony patches go in from the run-start handler (PatchSinks).
        var applyCalls = PatchProcessor.ReadMethodBody(lag.GetMethod("Apply", all)).Select(kv => kv.Value as MethodBase).Where(mb => mb != null).ToList();
        Check("lag: Apply queues the hot paths and never calls Harmony.Patch at load (PatchSinks does, at the first run start)",
            !applyCalls.Any(mb => mb.Name == "Patch" && mb.DeclaringType == typeof(Harmony)) && applyCalls.Any(mb => mb.Name == "Queue")
            && lag.GetMethod("PatchSinks", all) != null
            && PatchProcessor.ReadMethodBody(lag.GetMethod("OnRunStart", all)).Any(kv => kv.Value is MethodBase mb2 && mb2.Name == "PatchSinks")
            && PatchProcessor.ReadMethodBody(lag.GetMethod("PatchSinks", all)).Any(kv => kv.Value is MethodBase mb3 && mb3.Name == "Patch" && mb3.DeclaringType == typeof(Harmony)));
        lag.GetField("Running", all).SetValue(null, true);
        now = 0f;
        for (int i = 0; i < 605 && W() < 1; i++) { F(16.7f); fixedStep.Invoke(null, null); }
        var w1 = Lag();
        foreach (var l in w1) Console.WriteLine("  " + l);
        Check("lag: a smooth 10 s window logs one Debug line with the fps, the worst frame, the live counts, the hot paths, the GC, n/a frame timing and the fixed steps",
            W() == 1 && w1.Count == 1 && w1[0].StartsWith("[Debug:CompatShield] Lag: golemplains (stage 3 t=7:05) | 60 fps avg, worst frame 17 ms, 0 frame(s) over 100 ms | monsters 12, bodies 15, projectiles 40 | RecalculateStats 0x 0 ms | GC 0 collection(s), heap 1500 MB | frame cpu/gpu n/a | fixed steps ") && w1[0].Contains("(1.0/frame)")
            && w1[0].Contains("| projectiles top: FMJ 2, Missile 1"));
        Check("lag: with no memory readings the window line ends as before, with no RAM or page-fault part", w1.Count == 1 && w1[0].EndsWith("| projectiles top: FMJ 2, Missile 1") && !w1[0].Contains("RAM"));

        winTicks.SetValue(sink, (long)(System.Diagnostics.Stopwatch.Frequency * 0.32)); winCalls.SetValue(sink, 1500L);
        gen0 += 4; monsters = 58;
        for (int i = 0; i < 300; i++) { F(25f); fixedStep.Invoke(null, null); }
        F(300f); fixedStep.Invoke(null, null);
        var hitch = Lag().Where(l => l.Contains("ms frame in")).ToList();
        Check("lag: a 300 ms frame is logged at once, at Info, with the scene, the run clock and the live counts",
            hitch.Count == 1 && hitch[0] == "[Info:CompatShield] Lag: 300 ms frame in golemplains (stage 3 t=7:05); monsters 58, bodies 61, projectiles 40");
        for (int i = 0; i < 40; i++) { F(25f); fixedStep.Invoke(null, null); }
        F(300f); fixedStep.Invoke(null, null);
        Check("lag: a second big frame within 5 s is counted but not logged again", Lag().Count(l => l.Contains("ms frame in")) == 1);
        for (int i = 0; i < 400 && W() < 2; i++) { F(25f); fixedStep.Invoke(null, null); }
        var w2 = Lag().Where(l => l.Contains("| ") && l.Contains("monsters 58")).ToList();
        foreach (var l in w2) Console.WriteLine("  " + l);
        var fpsMatch = w2.Count == 1 ? System.Text.RegularExpressions.Regex.Match(w2[0], @"\| (\d+) fps avg") : null;
        int fps = fpsMatch != null && fpsMatch.Success ? int.Parse(fpsMatch.Groups[1].Value) : -1;
        Check("lag: a laggy window logs at Info with 2 frames over 100 ms, the worst frame, ~40 fps, the hot path's calls and ms, and 4 GCs",
            W() == 2 && w2.Count == 1 && w2[0].StartsWith("[Info:CompatShield] Lag: golemplains (stage 3 t=7:05) | ")
            && w2[0].Contains(" fps avg, worst frame 300 ms, 2 frame(s) over 100 ms | monsters 58, bodies 61, projectiles 40 | RecalculateStats 1500x 320 ms | GC 4 collection(s), heap 1500 MB")
            && fps >= 36 && fps <= 41);

        scene = "goolake"; monsters = 5;
        frameSample = new CompatShield.LagTracer.FrameSample { Ok = true, CpuMs = 30, GpuMs = 8 };
        F(2500f); fixedStep.Invoke(null, null);
        for (int i = 0; i < 700 && W() < 3; i++) { F(16.7f); fixedStep.Invoke(null, null); }
        var w3 = Lag().Where(l => l.Contains("Lag: goolake")).ToList();
        foreach (var l in w3) Console.WriteLine("  " + l);
        Check("lag: the 2.5 s frame right after a scene change is the stage load: not logged, not counted, and the next window is smooth again",
            W() == 3 && !Lag().Exists(l => l.Contains("2500 ms frame")) && w3.Count == 1 && w3[0].StartsWith("[Debug:CompatShield]") && w3[0].Contains("worst frame 17 ms, 0 frame(s) over 100 ms"));
        Check("lag: with frame timing exposed the window shows CPU and GPU ms, and one physics step per frame reads 1.0/frame",
            w3.Count == 1 && w3[0].Contains("| frame cpu 30 ms, gpu 8 ms | fixed steps ") && w3[0].Contains("(1.0/frame) | projectiles top: FMJ 2, Missile 1"));
        // The loading screen is its own scene, so the real stage-load frame can come 8 s after the scene change, past the grace.
        scene = "foggyswamp";
        for (int i = 0; i < 480; i++) { F(16.7f); fixedStep.Invoke(null, null); }   // 8 s in
        F(3000f);
        for (int i = 0; i < 700 && W() < 4; i++) { F(16.7f); fixedStep.Invoke(null, null); }
        var w4 = Lag().Where(l => l.Contains("Lag: foggyswamp")).ToList();
        foreach (var l in w4) Console.WriteLine("  " + l);
        Check("lag: a 3 s frame 8 s after a scene change is still the stage load: not logged, not counted; a later one would be",
            W() == 4 && !Lag().Exists(l => l.Contains("3000 ms frame")) && w4.Count == 1 && w4[0].Contains("worst frame 17 ms"));

        var summary = (System.Collections.Generic.List<string>)lag.GetMethod("Summary", all).Invoke(null, null);
        foreach (var l in summary) Console.WriteLine("  " + l);
        Check("lag: the run summary counts the windows, names the worst frame's place and counts, the peak monsters and the busiest hot path",
            summary.Count == 2 && summary[0].StartsWith("Lag summary: 4 window(s) of 10 s, 1 laggy (under 45 fps or a frame over 100 ms); ")
            && summary[0].Contains(" fps average; worst frame 300 ms at golemplains (stage 3 t=7:05), monsters 58, bodies 61, projectiles 40; peak monsters 58; busiest hot path RecalculateStats: 0.3 s in 1500 calls; frame cpu 30 ms / gpu 8 ms average")
            && summary[1] == "Lag summary: time by path over the run: RecalculateStats 0.3 s (of 40 s measured)");
        lag.GetMethod("ResetRun", all).Invoke(null, null);
        var empty = (System.Collections.Generic.List<string>)lag.GetMethod("Summary", all).Invoke(null, null);
        Check("lag: a run with no full window says so", empty.Count == 1 && empty[0].Contains("no full window"));
        // Per-frame subscribers: timed one by one, the top three named in the window line, the top ones in the summary.
        var sub1 = lag.GetMethod("AddSubSinkForTest", all).Invoke(null, new object[] { "RiskyMod:Foo.Bar(Update)" });
        var sub2 = lag.GetMethod("AddSubSinkForTest", all).Invoke(null, new object[] { "Aetherium:Baz.Tick(FixedUpdate)" });
        lag.GetMethod("ResetRun", all).Invoke(null, null);
        now += 1f; F(16.7f);   // opens a window
        sub1.GetType().GetField("WinTicks").SetValue(sub1, (long)(System.Diagnostics.Stopwatch.Frequency * 0.2)); sub1.GetType().GetField("WinCalls").SetValue(sub1, 600L);
        sub2.GetType().GetField("WinTicks").SetValue(sub2, (long)(System.Diagnostics.Stopwatch.Frequency * 0.05)); sub2.GetType().GetField("WinCalls").SetValue(sub2, 600L);
        for (int i = 0; i < 700 && W() < 1; i++) { F(11.7f); now += 0.005f; endOfFrame.Invoke(null, null); fixedStep.Invoke(null, null); }
        var wSub = Lag().Where(l => l.Contains("per-frame top")).ToList();
        foreach (var l in wSub) Console.WriteLine("  " + l);
        Check("lag: a window names the costliest per-frame subscribers by mod, biggest first",
            wSub.Count == 1 && wSub[0].EndsWith("| per-frame top: RiskyMod:Foo.Bar(Update) 200 ms, Aetherium:Baz.Tick(FixedUpdate) 50 ms"));
        Check("lag: the frame phases come from the end-of-frame marks: Update->eof 5 ms, eof->next Update 12 ms",
            wSub.Count == 1 && wSub[0].Contains("| phases: update->eof 5 ms, eof->update 12 ms |"));
        var summary2 = (System.Collections.Generic.List<string>)lag.GetMethod("Summary", all).Invoke(null, null);
        Check("lag: the run summary totals the mods' per-frame work", summary2.Any(l => l == "Lag summary: mods' per-frame work over the run: RiskyMod:Foo.Bar(Update) 0.2 s, Aetherium:Baz.Tick(FixedUpdate) 0.1 s"));
        // Stats-dirty attribution: the stack classifier and the window segment.
        var dirtySource = lag.GetMethod("DirtySource", all);
        var frames1 = new System.Collections.Generic.List<MethodBase> { typeof(string).GetMethod("Trim", Type.EmptyTypes), typeof(Harmony).GetMethod("UnpatchAll", new[] { typeof(string) }), typeof(Program).GetMethod("LagCheck", all), typeof(string).GetMethod("ToUpper", Type.EmptyTypes) };
        var frames2 = new System.Collections.Generic.List<MethodBase> { typeof(string).GetMethod("Trim", Type.EmptyTypes), typeof(UnityEngine.GameObject).GetMethod("SetActive") };
        Check("lag: the dirty-source classifier skips runtime, Harmony and Unity frames and names the first mod frame, or 'game' when there is none",
            (string)dirtySource.Invoke(null, new object[] { frames1, null }) == "shieldtest:Program.LagCheck" && (string)dirtySource.Invoke(null, new object[] { frames2, null }) == "game");
        Check("lag: MonoMod wrapper frames (DMD<T::M>, Trampoline<T::M>?n, Hook<T::M>?n) are skipped by name",
            (bool)lag.GetMethod("IsWrapperFrame", all).Invoke(null, new object[] { new FakeMethod("DMD<RoR2.CharacterBody::RecalculateStats>") })
            && (bool)lag.GetMethod("IsWrapperFrame", all).Invoke(null, new object[] { new FakeMethod("Trampoline<RoR2.CharacterBody::SetBuffCount>?-97003156") })
            && !(bool)lag.GetMethod("IsWrapperFrame", all).Invoke(null, new object[] { typeof(Program).GetMethod("TracerCheck", all) }));
        var frames3 = new System.Collections.Generic.List<MethodBase> { typeof(Program).GetMethod("FakeHook_SetBuffCount", all), typeof(Program).GetMethod("TracerCheck", all) };
        Check("lag: a mod method named after the dirtied method is a hook in the chain, not the caller: the classifier skips it",
            (string)dirtySource.Invoke(null, new object[] { frames3, "SetBuffCount" }) == "shieldtest:Program.TracerCheck"
            && (bool)lag.GetMethod("IsChainHook", all).Invoke(null, new object[] { typeof(Program).GetMethod("FakeHook_SetBuffCount", all), "SetBuffCount" })
            && !(bool)lag.GetMethod("IsChainHook", all).Invoke(null, new object[] { typeof(Program).GetMethod("TracerCheck", all), "SetBuffCount" }));
        lag.GetMethod("ResetRun", all).Invoke(null, null);
        now += 1f; F(16.7f);
        lag.GetMethod("Tally", all).Invoke(null, new object[] { "buff bdWarCry by RiskyMod:Foo.Bar", 64 });
        lag.GetMethod("Tally", all).Invoke(null, new object[] { "timed buff bdSlow by Aetherium:Baz.Tick", 16 });
        for (int i = 0; i < 700 && W() < 1; i++) { F(16.7f); fixedStep.Invoke(null, null); }
        var wDirty = Lag().Where(l => l.Contains("stats dirtied by")).ToList();
        foreach (var l in wDirty) Console.WriteLine("  " + l);
        Check("lag: a window names what dirtied stats and who, scaled back up from the 1-in-8 sample, biggest first",
            wDirty.Count == 1 && wDirty[0].Contains("| stats dirtied by: buff bdWarCry by RiskyMod:Foo.Bar ~64x, timed buff bdSlow by Aetherium:Baz.Tick ~16x | projectiles top: FMJ 2, Missile 1"));
        var summary3 = (System.Collections.Generic.List<string>)lag.GetMethod("Summary", all).Invoke(null, null);
        Check("lag: the run summary totals the dirtying sources", summary3.Any(l => l.StartsWith("Lag summary: stats dirtied by (sampled, over the run): buff bdWarCry by RiskyMod:Foo.Bar ~64x")));
        // The pause menu: frames keep drawing (slowly, with UI hitches) but physics never steps; such a window is Debug and never laggy.
        lag.GetMethod("ResetRun", all).Invoke(null, null);
        now += 1f; F(16.7f);
        for (int i = 0; i < 700 && W() < 1; i++) F(i % 50 == 0 ? 180f : 25f);
        var wPaused = Lag().Where(l => l.Contains("(paused)")).ToList();
        foreach (var l in wPaused) Console.WriteLine("  " + l);
        Check("lag: a window with frames but no physics steps is tagged (paused), logged at Debug and not counted as laggy",
            wPaused.Count == 1 && wPaused[0].StartsWith("[Debug:CompatShield] Lag: ") && wPaused[0].Contains(" (stage 3 t=7:05) (paused) | ") && (int)lag.GetProperty("LagWindows", all).GetValue(null) == 0);
        // Memory readings (1.4.54): free RAM, the game's size and page faults at the end of the window line, the long frame's own
        // page faults in the big-hitch line, and a memory line in the summary. Counts are running totals that can wrap.
        lag.GetMethod("ResetRun", all).Invoke(null, null);
        mem = new CompatShield.LagTracer.Mem { Ok = true, DiskOk = true, HardOk = true, FreeMb = 2970, TotalMb = 40853, GameMb = 13312, GamePrivateMb = 19251, DiskPages = 50000, Hard = uint.MaxValue - 10 };
        faultsNow = 1000;
        now += 1f; F(16.7f);   // opens the window: its starting counts
        for (int i = 0; i < 300; i++) { faultsNow += 10; F(25f); fixedStep.Invoke(null, null); }
        faultsNow += 4000; F(300f); fixedStep.Invoke(null, null);
        var memHitch = Lag().Where(l => l.Contains("ms frame in") && l.Contains("page faults")).ToList();
        foreach (var l in memHitch) Console.WriteLine("  " + l);
        Check("lag memory: a 300 ms frame's line adds the page faults the game took in that one frame and the free RAM",
            memHitch.Count == 1 && memHitch[0] == "[Info:CompatShield] Lag: 300 ms frame in foggyswamp (stage 3 t=7:05); monsters 5, bodies 8, projectiles 40; game page faults in this frame 4000; RAM 2.9 of 39.9 GB free");
        mem.FreeMb = 2150; mem.DiskPages = 50000 + 123456; mem.Hard = 801;   // the from-disk count wrapped past its 32-bit top: 812 more
        for (int i = 0; i < 400 && W() < 1; i++) { faultsNow += 10; F(25f); fixedStep.Invoke(null, null); }
        long windowFaults = faultsNow - 1000;
        var wMem = Lag().Where(l => l.Contains("| RAM: ")).ToList();
        foreach (var l in wMem) Console.WriteLine("  " + l);
        Check("lag memory: the window line ends with free RAM, the game's size and its part in RAM, its page faults, the worst frame's share, the ones read from disk and the whole PC's disk page reads",
            W() == 1 && wMem.Count == 1 && wMem[0].EndsWith("| RAM: 2.1 of 39.9 GB free, game 18.8 GB with 13.0 GB of it in RAM | page faults: game " + windowFaults
                + " (4000 in the worst frame), 812 from disk; whole PC read 123456 pages from disk"));
        Check("lag memory: the memory part comes after every part the Mod Watcher's parser reads in order (GC, fixed steps, projectiles top)",
            wMem.Count == 1 && wMem[0].IndexOf("| RAM: ") > wMem[0].IndexOf("| projectiles top: ") && wMem[0].IndexOf("| projectiles top: ") > wMem[0].IndexOf("| fixed steps ")
            && wMem[0].IndexOf("| fixed steps ") > wMem[0].IndexOf("| GC "));
        var summaryMem = (System.Collections.Generic.List<string>)lag.GetMethod("Summary", all).Invoke(null, null);
        foreach (var l in summaryMem.Where(x => x.Contains("memory"))) Console.WriteLine("  " + l);
        Check("lag memory: the run summary gives the lowest free RAM, the game's page faults, the ones read from disk with the worst window's place, and the PC's disk page reads",
            summaryMem.Count(l => l == "Lag summary: memory: free RAM fell to 2.1 of 39.9 GB; game page faults " + windowFaults
                + " in the measured windows, 812 of them read from disk (most in one window: 812 at foggyswamp (stage 3 t=7:05)); whole PC read 123456 pages from disk") == 1);
        // The slow from-disk count and the disk reads can be missing on their own; so can the per-frame count.
        mem.HardOk = false; mem.DiskOk = false; long keep = faultsNow; faultsNow = -1; mem.Faults = (uint)keep;
        for (int i = 0; i < 700 && W() < 2; i++) { F(16.7f); fixedStep.Invoke(null, null); }
        var wPart = Lag().Where(l => l.Contains("| RAM: ")).ToList();
        foreach (var l in wPart.Skip(1)) Console.WriteLine("  " + l);
        Check("lag memory: with no from-disk count, no disk reads and no per-frame count the line says n/a for those and drops the worst-frame share",
            W() == 2 && wPart.Count == 2 && wPart[1].EndsWith("| RAM: 2.1 of 39.9 GB free, game 18.8 GB with 13.0 GB of it in RAM | page faults: game 0, n/a from disk; whole PC read n/a pages from disk"));
        mem = new CompatShield.LagTracer.Mem();
        lag.GetMethod("ResetRun", all).Invoke(null, null);
        now += 1f; F(16.7f);
        for (int i = 0; i < 700 && W() < 1; i++) { F(16.7f); fixedStep.Invoke(null, null); }
        var summaryNone = (System.Collections.Generic.List<string>)lag.GetMethod("Summary", all).Invoke(null, null);
        Check("lag memory: a run with no readings has no memory line in its summary", W() == 1 && summaryNone.Count > 0 && !summaryNone.Any(l => l.Contains("memory")));
        // The real reader, on this (Windows) test process.
        var memReader = shieldType.Assembly.GetType("CompatShield.MemReader");
        var readMem = memReader.GetMethod("Read", all); var readFaults = memReader.GetMethod("Faults", all);
        var real = (CompatShield.LagTracer.Mem)readMem.Invoke(null, null);
        Console.WriteLine($"  real: free {real.FreeMb} of {real.TotalMb} MB, this process {real.GameMb} MB in RAM, {real.GamePrivateMb} MB private, faults {real.Faults}, PC disk pages {real.DiskPages} (ok {real.Ok}/{real.DiskOk})");
        Check("lag memory real: Windows gives free and total RAM, this process's size and page faults, and the PC's disk page reads",
            real.Ok && real.DiskOk && real.TotalMb > 1000 && real.FreeMb > 0 && real.FreeMb < real.TotalMb && real.GameMb > 5 && real.GamePrivateMb > 5 && real.Faults > 0 && real.DiskPages > 0);
        long faultsBefore = (long)readFaults.Invoke(null, null);
        var fresh = new byte[32 << 20];
        for (int i = 0; i < fresh.Length; i += 4096) fresh[i] = 1;   // first touch of 8192 new pages
        long faultsAfter = (long)readFaults.Invoke(null, null);
        GC.KeepAlive(fresh);
        Console.WriteLine($"  real: touching 32 MB of new memory raised this process's page-fault count by {faultsAfter - faultsBefore}");
        Check("lag memory real: the per-frame count is this process's own page faults (touching 8192 new pages raises it by thousands)",
            faultsBefore > 0 && faultsAfter - faultsBefore >= 2000 && faultsAfter - faultsBefore < 200000);
        var faultsCall = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), readFaults);
        var memCall = (Func<CompatShield.LagTracer.Mem>)Delegate.CreateDelegate(typeof(Func<CompatShield.LagTracer.Mem>), readMem);
        faultsCall(); memCall();
        var costWatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 20000; i++) faultsCall();
        double faultsUs = costWatch.Elapsed.TotalMilliseconds * 1000.0 / 20000;
        costWatch.Restart();
        for (int i = 0; i < 20000; i++) memCall();
        double memUs = costWatch.Elapsed.TotalMilliseconds * 1000.0 / 20000;
        Console.WriteLine($"  real: the per-frame count costs {faultsUs:0.0} microseconds a read, the full cheap reading {memUs:0.0}");
        Check("lag memory real: the per-frame count costs under 100 microseconds and the full cheap reading under 200 (measured: about 5 and 15)", faultsUs < 100 && memUs < 200);
        // The from-disk count is read from a 64-bit Windows structure. This harness is a 32-bit process, so here the read must
        // refuse cleanly; tools\memreader_x64.ps1 (run by run_harness.ps1) runs the real read in a 64-bit process, like the game.
        bool wide = Environment.Is64BitProcess;
        long hardNow = (long)memReader.GetMethod("HardNowForTest", all).Invoke(null, null);
        Console.WriteLine($"  real: 64-bit process {wide}; from-disk count read directly: {hardNow}");
        Check("lag memory real: in a 32-bit process the from-disk read refuses (-1) instead of reading the wrong offsets; in a 64-bit one it reads a count no bigger than all page faults",
            wide ? hardNow >= 0 && hardNow <= (long)readFaults.Invoke(null, null) : hardNow == -1);
        // The slow read lives on a background thread; the main-thread paths never call it, and the worker never logs.
        bool Calls(MethodBase from, string name) => PatchProcessor.ReadMethodBody(from).Any(kv => kv.Value is MethodBase mb5 && mb5.Name == name);
        Check("lag memory: the slow process-list read is called only by the background loop (and the test seam), never by Read, Faults or the tracer's frame code",
            Calls(memReader.GetMethod("Loop", all), "SampleHard") && !Calls(readMem, "SampleHard") && !Calls(readFaults, "SampleHard")
            && new[] { "Frame", "OpenWindow", "CloseWindow", "MemorySegment", "SafeMemory", "Summary", "OnRunStart" }.All(n => !Calls(lag.GetMethod(n, all), "SampleHard")));
        Check("lag memory: the background loop and the slow read never log (BepInEx's Unity log listener is main-thread only); the main thread logs a problem once",
            new[] { "Loop", "SampleHard" }.All(n => !PatchProcessor.ReadMethodBody(memReader.GetMethod(n, all)).Any(kv => kv.Value is MethodBase mb6 && mb6.DeclaringType == typeof(BepInEx.Logging.ManualLogSource)))
            && Calls(lag.GetMethod("MemorySegment", all), "TakeProblem"));
        Check("lag memory: the run-start handler starts the reader and the run-end handler stops it",
            Calls(lag.GetMethod("OnRunStart", all), "RunStarted") && Calls(lag.GetMethod("OnRunDestroy", all), "RunEnded"));
        memReader.GetMethod("RunStarted", all).Invoke(null, null);
        var workerThread = (System.Threading.Thread)memReader.GetField("worker", all).GetValue(null);
        if (wide)
        {
            var waitHard = System.Diagnostics.Stopwatch.StartNew();
            var live = (CompatShield.LagTracer.Mem)readMem.Invoke(null, null);
            while (!live.HardOk && waitHard.ElapsedMilliseconds < 10000) { System.Threading.Thread.Sleep(20); live = (CompatShield.LagTracer.Mem)readMem.Invoke(null, null); }
            Check("lag memory real: after a run start the background thread delivers a fresh from-disk count",
                live.HardOk && workerThread != null && workerThread.IsAlive && workerThread.Name == "CompatShield.MemReader" && live.Hard <= live.Faults);
            memReader.GetMethod("RunEnded", all).Invoke(null, null);
            Check("lag memory real: nothing went wrong, so there is no problem to log", memReader.GetMethod("TakeProblem", all).Invoke(null, null) == null);
        }
        else
        {
            bool ended = workerThread != null && workerThread.Join(5000);
            var after = (CompatShield.LagTracer.Mem)readMem.Invoke(null, null);
            string problem1 = (string)memReader.GetMethod("TakeProblem", all).Invoke(null, null);
            Console.WriteLine("  real: " + problem1);
            Check("lag memory real: in a 32-bit process the background thread gives up at once, the cheap readings keep working without a from-disk count, and the reason is handed to the main thread once",
                ended && workerThread.Name == "CompatShield.MemReader" && after.Ok && !after.HardOk
                && problem1 == "the count of the game's page faults read from disk is unavailable (the game is not running as a 64-bit process), so the lag lines show n/a for it"
                && memReader.GetMethod("TakeProblem", all).Invoke(null, null) == null);
            memReader.GetMethod("RunStarted", all).Invoke(null, null);
            Check("lag memory real: a later run start does not start another thread after the first gave up",
                ReferenceEquals(workerThread, memReader.GetField("worker", all).GetValue(null)) && !workerThread.IsAlive);
            memReader.GetMethod("RunEnded", all).Invoke(null, null);
        }
        // Back to no readings for the checks below.
        faultsNow = -1; mem = new CompatShield.LagTracer.Mem();
        lag.GetMethod("ResetRun", all).Invoke(null, null);

        var isFlip = lag.GetMethod("IsFlip", all);
        Check("lag: only a clear->set statsDirty flip across the call counts as that call's dirtying",
            (bool)isFlip.Invoke(null, new object[] { false, true }) && !(bool)isFlip.Invoke(null, new object[] { true, true }) && !(bool)isFlip.Invoke(null, new object[] { false, false }));
        Check("lag: PatchDirtySources pairs a prefix and a postfix (the flip is read across the call), not a prefix alone",
            PatchProcessor.ReadMethodBody(lag.GetMethod("PatchDirtySources", all)).Count(kv => kv.Value is MethodBase mb4 && mb4.Name == "Patch" && mb4.DeclaringType == typeof(Harmony)) == 2
            && lag.GetNestedType("DirtyPatches", all)?.GetMethod("DirtyPostfix", all) != null && lag.GetMethod("RecalcPrefix", all) != null
            && lag.GetNestedType("DirtyPatches", all) != typeof(CompatShield.LagTracer));   // its own class: a separate Harmony __state from the timing sink
        var topNames = lag.GetMethod("TopNames", all);
        Check("lag: the projectile census counts prefab names, biggest first, ties by name",
            (string)topNames.Invoke(null, new object[] { new[] { "FMJ", "Missile", "FMJ", "Spark", "Missile", "FMJ" }, 2 }) == "FMJ 3, Missile 2");
        var subName = lag.GetMethod("SubName", all);
        Check("lag: a lambda subscriber is named by its outer type and method, with the event",
            (string)subName.Invoke(null, new object[] { typeof(Program).GetMethod("LagCheck", all), "onFixedUpdate" }) == "shieldtest:Program.LagCheck(FixedUpdate)");

        var managed = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var ror2 = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var body = ror2.GetType("RoR2.CharacterBody"); var hc = ror2.GetType("RoR2.HealthComponent"); var gem = ror2.GetType("RoR2.GlobalEventManager");
        var recalc = body?.Methods.FirstOrDefault(m => m.Name == "RecalculateStats" && !m.HasParameters);
        var take = hc?.Methods.FirstOrDefault(m => m.Name == "TakeDamageProcess" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == "DamageInfo");
        var hit = gem?.Methods.FirstOrDefault(m => m.Name == "OnHitEnemy" && m.Parameters.Count == 2 && m.Parameters[0].ParameterType.Name == "DamageInfo" && m.Parameters[1].ParameterType.Name == "GameObject");
        Check("lag real: game 1.5.0 has CharacterBody.RecalculateStats(), HealthComponent.TakeDamageProcess(DamageInfo) and GlobalEventManager.OnHitEnemy(DamageInfo, GameObject)",
            recalc != null && !recalc.IsStatic && take != null && !take.IsStatic && hit != null && !hit.IsStatic);
        var team = ror2.GetType("RoR2.TeamComponent")?.Methods.FirstOrDefault(m => m.Name == "GetTeamMembers" && m.IsStatic && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == "TeamIndex");
        var list = body?.Fields.FirstOrDefault(f => f.Name == "readOnlyInstancesList" && f.IsStatic);
        var tracker = ror2.GetType("RoR2.InstanceTracker")?.Methods.FirstOrDefault(m => m.Name == "GetInstancesList" && m.IsStatic && m.HasGenericParameters);
        var run = ror2.GetType("RoR2.Run");
        var stopwatch = run?.Methods.FirstOrDefault(m => m.Name == "GetRunStopwatch" && !m.HasParameters);
        var stageClear = run?.Fields.FirstOrDefault(f => f.Name == "stageClearCount");
        var teamIdxT = ror2.GetType("RoR2.TeamIndex");
        var monsterIdx = teamIdxT?.Fields.FirstOrDefault(f => f.Name == "Monster");
        Check("lag real: TeamIndex has the three enemy teams the live count sums (Monster, Lunar, Void)",
            teamIdxT != null && new[] { "Monster", "Lunar", "Void" }.All(n => teamIdxT.Fields.Any(f => f.Name == n)));
        Check("lag real: the live counts' members exist (TeamComponent.GetTeamMembers(TeamIndex), TeamIndex.Monster, CharacterBody.readOnlyInstancesList, InstanceTracker.GetInstancesList<T>, Run.GetRunStopwatch, Run.stageClearCount)",
            team != null && list != null && tracker != null && stopwatch != null && stageClear != null && monsterIdx != null);
        var onEnable = ror2.GetType("RoR2.Projectile.ProjectileController")?.Methods.FirstOrDefault(m => m.Name == "OnEnable");
        bool tracked = onEnable?.Body?.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference mr && mr.DeclaringType.Name == "InstanceTracker" && mr.Name == "Add") ?? false;
        Check("lag real: ProjectileController registers with InstanceTracker in OnEnable, so the projectile count is live", tracked);
        var death = gem?.Methods.FirstOrDefault(m => m.Name == "OnCharacterDeath" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == "DamageReport");
        var invChanged = body?.Methods.FirstOrDefault(m => m.Name == "OnInventoryChanged" && !m.HasParameters);
        var bulletFire = ror2.GetType("RoR2.BulletAttack")?.Methods.FirstOrDefault(m => m.Name == "Fire" && !m.HasParameters);
        var fireProj = ror2.GetType("RoR2.Projectile.ProjectileManager")?.Methods.FirstOrDefault(m => m.Name == "FireProjectile" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == "FireProjectileInfo");
        var heal = hc?.Methods.FirstOrDefault(m => m.Name == "Heal" && m.Parameters.Count == 3 && m.Parameters[1].ParameterType.Name == "ProcChainMask");
        Check("lag real: the five extra hot paths exist (GlobalEventManager.OnCharacterDeath(DamageReport), CharacterBody.OnInventoryChanged(), BulletAttack.Fire(), ProjectileManager.FireProjectile(FireProjectileInfo), HealthComponent.Heal(float, ProcChainMask, bool))",
            death != null && invChanged != null && bulletFire != null && fireProj != null && heal != null);
        Check("lag real: Run has onRunStartGlobal and onRunDestroyGlobal", run.Events.Any(e => e.Name == "onRunStartGlobal") && run.Events.Any(e => e.Name == "onRunDestroyGlobal"));
        bool NoArg(string type, string name) { var t = ror2.GetType(type); return t != null && t.Methods.Any(m => m.Name == name && !m.HasParameters && !m.IsStatic); }
        Check("lag real: the per-frame paths exist (CharacterBody.FixedUpdate, BaseAI.FixedUpdate, EntityStateMachine.FixedUpdate, CharacterModel.UpdateOverlays, HUD.Update, RoR2Application.Update/FixedUpdate)",
            NoArg("RoR2.CharacterBody", "FixedUpdate") && NoArg("RoR2.CharacterAI.BaseAI", "FixedUpdate") && NoArg("RoR2.EntityStateMachine", "FixedUpdate")
            && NoArg("RoR2.CharacterModel", "UpdateOverlays") && NoArg("RoR2.UI.HUD", "Update") && NoArg("RoR2.RoR2Application", "Update") && NoArg("RoR2.RoR2Application", "FixedUpdate"));
        bool Has(string type, string name, params string[] ptypes) { var t = ror2.GetType(type); return t != null && t.Methods.Any(m => m.Name == name && m.Parameters.Count == ptypes.Length && m.Parameters.Select(p => p.ParameterType.Name).SequenceEqual(ptypes)); }
        Check("lag real: the stats-dirtying entry points exist (SetBuffCount(BuffIndex,int), AddTimedBuffInternal(BuffDef,float,float), MarkAllStatsDirty, OnInventoryChanged, UpdateOutOfCombatAndDanger, OnTeamLevelChanged, set_isSprinting, GetBuffCount(BuffIndex), BuffCatalog.GetBuffDef(BuffIndex))",
            Has("RoR2.CharacterBody", "SetBuffCount", "BuffIndex", "Int32") && Has("RoR2.CharacterBody", "AddTimedBuffInternal", "BuffDef", "Single", "Single") && Has("RoR2.CharacterBody", "MarkAllStatsDirty")
            && Has("RoR2.CharacterBody", "OnInventoryChanged") && Has("RoR2.CharacterBody", "UpdateOutOfCombatAndDanger") && Has("RoR2.CharacterBody", "OnTeamLevelChanged")
            && Has("RoR2.CharacterBody", "set_isSprinting", "Boolean") && Has("RoR2.CharacterBody", "GetBuffCount", "BuffIndex") && Has("RoR2.BuffCatalog", "GetBuffDef", "BuffIndex")
            && ror2.GetType("RoR2.CharacterBody").Fields.Any(f => f.Name == "statsDirty" && f.FieldType.FullName == "System.Boolean"));
        var appT = ror2.GetType("RoR2.RoR2Application");
        Check("lag real: RoR2Application has static onUpdate / onFixedUpdate / onLateUpdate delegate fields to scan",
            new[] { "onUpdate", "onFixedUpdate", "onLateUpdate" }.All(n => appT.Fields.Any(f => f.Name == n && f.IsStatic && f.FieldType.Name == "Action")));
        var core = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "UnityEngine.CoreModule.dll"));
        var ftm = core.GetType("UnityEngine.FrameTimingManager");
        Check("lag real: Unity 2021.3 has FrameTimingManager.CaptureFrameTimings / GetLatestTimings and FrameTiming.cpuFrameTime / gpuFrameTime",
            ftm != null && ftm.Methods.Any(m => m.Name == "CaptureFrameTimings") && ftm.Methods.Any(m => m.Name == "GetLatestTimings")
            && core.GetType("UnityEngine.FrameTiming")?.Fields.Any(f => f.Name == "cpuFrameTime") == true && core.GetType("UnityEngine.FrameTiming").Fields.Any(f => f.Name == "gpuFrameTime"));
        return failures;
    }

    // Damage chain guard: the depth bookkeeping with stubs (Prefix/Finalizer driven by hand, Replay and IsGone swapped), the
    // same-frame drain, nested queueing during a drain, the queue cap, the logs; the Hypercrit2 finalizer; and the real members.
    // Stand-ins for RoR2.BlastAttack: HandleHits(HitPoint[]) runs a callback, so a test can fire blasts from inside a blast.
    public class FakeBlast {
        public struct HitPoint { }
        public static int Ran;
        public static Action<FakeBlast> OnHits;
        public object attacker;
        public int level;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void HandleHits(HitPoint[] hits) { Ran++; OnHits?.Invoke(this); }
    }
    public class FakeStaticBlast { public struct HitPoint { } public static void HandleHits(HitPoint[] hits) { } }
    public class FakeNoHitPointBlast { public void HandleHits(int[] hits) { } }

    public class FakeNullDccsNoField { public void Awake() { } }
    public class FakeNullDccsNoAwake { public RoR2.DccsPool interactableDccsPool; }
    public class FakeNullDccsStaticAwake { public RoR2.DccsPool interactableDccsPool; public static void Awake() { } }
    public class FakeNullDccsNoSwap {
        public RoR2.DccsPool interactableDccsPool;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Awake() { }
    }
    public abstract class FakeNullDccsAbstractSwap {
        public RoR2.DccsPool interactableDccsPool;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Awake() { }
        public abstract void SetInteractableDCCSP(RoR2.DccsPool pool);
    }
    public class FakeRenamedEntry { public object cards; }
    public class FakeRenamedCategory { public FakeRenamedEntry[] alwaysIncluded, includedIfConditionsMet, includedIfNoConditionsMet; }
    public class FakeRenamedPool { public FakeRenamedCategory[] poolCategories; }
    public class FakeNullDccsRenamed { public FakeRenamedPool interactableDccsPool; public void Awake() { } }
    public class FakeOneListCategory { public RoR2.DccsPool.PoolEntry[] alwaysIncluded; }
    public class FakeOneListPool { public FakeOneListCategory[] poolCategories; }
    public class FakeNullDccsOneList { public FakeOneListPool interactableDccsPool; public void Awake() { } }

    // Null dccs pool entries. Stand-ins with the game's member names, unpatched first so the baseline can fail. Real files: Cecil over RoR2.dll and the mods that walk the pool.
    static int NullDccsCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var fix = shieldType.Assembly.GetType("CompatShield.NullDccsPoolEntryFix");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var dead = new System.Collections.Generic.HashSet<object>();
        var names = new System.Collections.Generic.Dictionary<object, string>();
        // The Unity-only defaults of Alive, NameOf and SceneOf are replaced here, so they never run in this harness. Only the game log shows them working.
        var alive = new Func<object, bool>(o => o != null && !dead.Contains(o));
        fix.GetField("Alive", all).SetValue(null, alive);
        fix.GetField("NameOf", all).SetValue(null, new Func<object, string>(o => o != null && names.TryGetValue(o, out var n) ? n : o?.ToString()));
        fix.GetField("SceneOf", all).SetValue(null, new Func<object, string>(o => "teststage"));
        int Total() => (int)fix.GetProperty("Total", all).GetValue(null);
        void Reset() { fix.GetMethod("ResetForTest", all).Invoke(null, null); logs.Clear(); dead.Clear(); names.Clear(); }
        var h = new Harmony("test.nulldccs");
        bool Patch(Type t) => (bool)fix.GetMethod("PatchPools", all).Invoke(null, new object[] { h, t });
        bool Warned() { bool w = logs.Count(l => l.Contains("not applied")) == 1; logs.Clear(); return w; }
        int PostfixCount(Type t) => Harmony.GetPatchInfo(t.GetMethod("Awake"))?.Postfixes.Count ?? 0;

        RoR2.DccsPool.PoolEntry E(object d, float w = 1f) => new RoR2.DccsPool.PoolEntry { dccs = d, weight = w };
        RoR2.DccsPool.ConditionalPoolEntry C(object d, float w, params object[] exp) => new RoR2.DccsPool.ConditionalPoolEntry { dccs = d, weight = w, requiredExpansions = exp };
        RoR2.DccsPool.Category Cat(string name, RoR2.DccsPool.PoolEntry[] always, RoR2.DccsPool.ConditionalPoolEntry[] cond, RoR2.DccsPool.PoolEntry[] none) =>
            new RoR2.DccsPool.Category { name = name, alwaysIncluded = always, includedIfConditionsMet = cond, includedIfNoConditionsMet = none };
        RoR2.DccsPool Pool(params RoR2.DccsPool.Category[] c) => new RoR2.DccsPool { poolCategories = c };
        RoR2.DccsPool Holed() => Pool(
            Cat("Standard", new[] { E("a", 2f), E(null, 9f), E("b", 3f) }, new[] { C(null, 4f, "DLC2"), C("c", 5f, "DLC1") }, new[] { E(null), E("d", 6f) }),
            Cat("Other", new[] { E("e") }, new RoR2.DccsPool.ConditionalPoolEntry[0], new RoR2.DccsPool.PoolEntry[0]));
        // What R2API's subscribers and QoLChests do: read every entry's dccs with no null check.
        int Walk(RoR2.DccsPool p) {
            int n = 0;
            foreach (var c in p.poolCategories) {
                foreach (var e in c.alwaysIncluded) { e.dccs.GetHashCode(); n++; }
                foreach (var e in c.includedIfConditionsMet) { e.dccs.GetHashCode(); n++; }
                foreach (var e in c.includedIfNoConditionsMet) { e.dccs.GetHashCode(); n++; }
            }
            return n;
        }
        bool WalkThrows(RoR2.DccsPool p) { try { Walk(p); return false; } catch (NullReferenceException) { return true; } }
        // A copy of the branches in DCCSBlender.GenerateWeightedCategorySelections for the two optional lists. The IL checks below tie it to the real method.
        System.Collections.Generic.List<string> Blend(RoR2.DccsPool.Category c, bool conditionsMet) {
            var picks = new System.Collections.Generic.List<string>();
            bool any = false;
            foreach (var e in c.includedIfConditionsMet) {
                if (e == null || e.dccs == null || !conditionsMet) continue;
                any = true;
                picks.Add(e.dccs + ":" + e.weight);
            }
            if (!any) foreach (var e in c.includedIfNoConditionsMet) if (e != null && e.dccs != null) picks.Add(e.dccs + ":" + e.weight);
            return picks;
        }
        // The alwaysIncluded loop in DCCSBlender.GetBlendedDCCS, which has no null check. The IL checks below tie this to the real method too.
        System.Collections.Generic.List<string> BlendAlways(RoR2.DccsPool.Category c) {
            var picks = new System.Collections.Generic.List<string>();
            foreach (var e in c.alwaysIncluded) { e.dccs.GetHashCode(); picks.Add(e.dccs + ":" + e.weight); }
            return picks;
        }
        string[] Cards(System.Collections.Generic.IEnumerable<RoR2.DccsPool.PoolEntry> list) => list.Select(e => e.dccs + ":" + e.weight).ToArray();

        Reset();
        Check("nulldccs: the baseline fails: a walker with no null check throws at an unpatched pool", WalkThrows(Holed()));
        Check("nulldccs: a missing ClassicStageInfo type is left alone and warned about", !Patch(null) && Warned());
        Check("nulldccs: a stage without the pool field is left alone", !Patch(typeof(FakeNullDccsNoField)) && Warned());
        Check("nulldccs: a stage without Awake is left alone", !Patch(typeof(FakeNullDccsNoAwake)) && Warned());
        Check("nulldccs: a static Awake is left alone", !Patch(typeof(FakeNullDccsStaticAwake)) && Warned());
        Check("nulldccs: a pool whose entries lost their dccs field is left alone, not read as every entry empty", !Patch(typeof(FakeNullDccsRenamed)) && Warned() && PostfixCount(typeof(FakeNullDccsRenamed)) == 0);
        Check("nulldccs: a category that lost one of its three lists is left alone", !Patch(typeof(FakeNullDccsOneList)) && Warned() && PostfixCount(typeof(FakeNullDccsOneList)) == 0);
        bool noSwap = Patch(typeof(FakeNullDccsNoSwap));
        Check("nulldccs: with no pool swap method Awake is still patched and the gap is warned about", noSwap && PostfixCount(typeof(FakeNullDccsNoSwap)) == 1 && logs.Count(l => l.Contains("swapped in later isn't covered")) == 1 && logs.Count(l => l.Contains("stage variant's pool may not be covered")) == 1);
        logs.Clear();
        bool abstractSwap = Patch(typeof(FakeNullDccsAbstractSwap));
        Check("nulldccs: a pool swap that can't be patched leaves Awake patched and reports the gap instead of saying not applied", abstractSwap && PostfixCount(typeof(FakeNullDccsAbstractSwap)) == 1 && logs.Count(l => l.Contains("can't patch SetInteractableDCCSP")) == 1 && !logs.Exists(l => l.Contains("not applied")));
        logs.Clear();
        Check("nulldccs: patches Awake and the pool swap and says so", Patch(typeof(RoR2.ClassicStageInfo)) && logs.Count(l => l.Contains("removed before anything walks the pool")) == 1 && !logs.Exists(l => l.Contains("not applied") || l.Contains("isn't covered")));
        var swapPatch = Harmony.GetPatchInfo(typeof(RoR2.ClassicStageInfo).GetMethod("SetInteractableDCCSP"));
        Check("nulldccs: Awake has one postfix and the swap has one prefix", PostfixCount(typeof(RoR2.ClassicStageInfo)) == 1 && swapPatch != null && swapPatch.Prefixes.Count == 1);

        var variantPatch = Harmony.GetPatchInfo(typeof(RoR2.ClassicStageInfoVariation).GetMethod("SwapCSIValuesWithSelf"));
        Check("nulldccs: the variant swap has one prefix", variantPatch != null && variantPatch.Prefixes.Count == 1);

        // The behaviour the fix exists for.
        Reset();
        var pool = Holed();
        names[pool] = "dpTestInteractables";
        var untouched = pool.poolCategories[1].alwaysIncluded;
        var stage = new RoR2.ClassicStageInfo(pool);
        stage.Awake();
        Check("nulldccs: after Awake a walker with no null check gets through every list", !WalkThrows(pool) && Walk(pool) == 5);
        var std = pool.poolCategories[0];
        Check("nulldccs: alwaysIncluded loses only the empty entry and keeps order and weights", Cards(std.alwaysIncluded).SequenceEqual(new[] { "a:2", "b:3" }));
        Check("nulldccs: includedIfConditionsMet loses only the empty entry", Cards(std.includedIfConditionsMet).SequenceEqual(new[] { "c:5" }));
        Check("nulldccs: includedIfNoConditionsMet loses only the empty entry", Cards(std.includedIfNoConditionsMet).SequenceEqual(new[] { "d:6" }));
        Check("nulldccs: the conditional list keeps its element type, so the game can still read it", std.includedIfConditionsMet.GetType() == typeof(RoR2.DccsPool.ConditionalPoolEntry[]) && std.alwaysIncluded.GetType() == typeof(RoR2.DccsPool.PoolEntry[]));
        Check("nulldccs: a kept entry is the same object, with its expansion list intact", std.includedIfConditionsMet[0].requiredExpansions.SequenceEqual(new object[] { "DLC1" }));
        Check("nulldccs: a list with nothing to remove keeps the very same array", ReferenceEquals(pool.poolCategories[1].alwaysIncluded, untouched));
        Check("nulldccs: the total counts every removed entry", Total() == 3);
        var cleaned = logs.Where(l => l.Contains("Null dccs fix: removed")).ToList();
        Check("nulldccs: one line for the pool, not one per list", cleaned.Count == 1);
        var line = cleaned.Count == 1 ? cleaned[0] : "";
        Check("nulldccs: the line names the count, pool, pool type, stage and where it was found", line.Contains("removed 3 pool entries from dpTestInteractables (DccsPool) on teststage, found at Awake"));
        Check("nulldccs: the line names each category, index, list and count", line.Contains("category \"Standard\" [0] alwaysIncluded: 1") && line.Contains("category \"Standard\" [0] includedIfConditionsMet: 1") && line.Contains("category \"Standard\" [0] includedIfNoConditionsMet: 1"));
        Check("nulldccs: the line leaves out a category with nothing removed", !line.Contains("Other"));
        Check("nulldccs: the line gives the reference kind, weight and serialized flag", line.Contains("empty reference, weight 9, serialized True"));
        Check("nulldccs: the line gives the expansions a conditional entry needed", line.Contains("empty reference, weight 4, serialized True, expansions DLC2"));
        logs.Clear();
        stage.Awake();
        Check("nulldccs: a cleaned pool logs nothing on a later visit and adds nothing to the total", logs.Count == 0 && Total() == 3);

        Reset();
        var pool2 = Pool(Cat("One", new[] { E(null, 1f) }, new RoR2.DccsPool.ConditionalPoolEntry[0], new RoR2.DccsPool.PoolEntry[0]));
        names[pool2] = "dpOne";
        new RoR2.ClassicStageInfo(pool2).Awake();
        Check("nulldccs: a single removal reads as one entry and leaves an empty array, not a null one", logs.Exists(l => l.Contains("removed 1 pool entry from dpOne")) && pool2.poolCategories[0].alwaysIncluded != null && pool2.poolCategories[0].alwaysIncluded.Length == 0);

        Reset();
        var built = Pool(Cat("B", new[] { E(null, 1f).BuiltInCode() }, new RoR2.DccsPool.ConditionalPoolEntry[0], new RoR2.DccsPool.PoolEntry[0]));
        new RoR2.ClassicStageInfo(built).Awake();
        Check("nulldccs: an entry built in code shows serialized False, which points at a mod", logs.Exists(l => l.Contains("serialized False")));

        Reset();
        var destroyed = new object();
        dead.Add(destroyed);
        var edge = Pool(Cat("Edge", new[] { null, E(destroyed, 7f), E("kept", 1f) }, null, new RoR2.DccsPool.PoolEntry[0]), null);
        new RoR2.ClassicStageInfo(edge).Awake();
        Check("nulldccs: an empty slot and a destroyed reference are both removed", Cards(edge.poolCategories[0].alwaysIncluded).SequenceEqual(new[] { "kept:1" }));
        Check("nulldccs: the line tells an empty slot and a destroyed object apart", logs.Exists(l => l.Contains("empty slot") && l.Contains("destroyed object, weight 7")));
        Check("nulldccs: a null list stays null and a null category stays in place", edge.poolCategories[0].includedIfConditionsMet == null && edge.poolCategories.Length == 2 && edge.poolCategories[1] == null);

        Reset();
        Exception escaped = null;
        var deadPool = Holed();
        dead.Add(deadPool);
        try {
            new RoR2.ClassicStageInfo(null).Awake();
            new RoR2.ClassicStageInfo(new RoR2.DccsPool()).Awake();
            new RoR2.ClassicStageInfo(Pool()).Awake();
            new RoR2.ClassicStageInfo(deadPool).Awake();
        } catch (Exception e) { escaped = e; }
        Check("nulldccs: no pool, no categories and a destroyed pool are all left alone quietly", escaped == null && logs.Count == 0);
        Check("nulldccs: a destroyed pool is not edited", deadPool.poolCategories[0].alwaysIncluded.Length == 3);

        // A pool swapped in after Start.
        Reset();
        var variant = Holed();
        names[variant] = "dpVariant";
        var swapper = new RoR2.ClassicStageInfo(Pool());
        swapper.SetInteractableDCCSP(variant);
        Check("nulldccs: a pool swapped in is clean by the time the stage holds it", ReferenceEquals(swapper.Pool, variant) && !WalkThrows(variant));
        Check("nulldccs: the swap line says where it was found", logs.Exists(l => l.Contains("from dpVariant (DccsPool) on teststage, found at SetInteractableDCCSP")));
        escaped = null;
        try { swapper.SetInteractableDCCSP(null); } catch (Exception e) { escaped = e; }
        Check("nulldccs: swapping in no pool still works", escaped == null && swapper.Pool == null);

        // The vanilla component's swap. Store stands in for the setter after inlining, so the setter's prefix can't help here.
        Reset();
        var variantPool = Holed();
        names[variantPool] = "dpVariant2";
        var target = new RoR2.ClassicStageInfo(Pool());
        new RoR2.ClassicStageInfoVariation(target, variantPool).SwapCSIValuesWithSelf();
        Check("nulldccs: a variant pool is clean even if the setter's prefix never runs", ReferenceEquals(target.Pool, variantPool) && !WalkThrows(variantPool));
        Check("nulldccs: the variant line says where it was found", logs.Exists(l => l.Contains("from dpVariant2 (DccsPool) on teststage, found at SwapCSIValuesWithSelf")));
        escaped = null;
        try { new RoR2.ClassicStageInfoVariation(target, null).SwapCSIValuesWithSelf(); } catch (Exception e) { escaped = e; }
        Check("nulldccs: a variant with no pool of its own still works", escaped == null && ReferenceEquals(target.Pool, variantPool));

        // Removal can't change what the game picks from the two optional lists.
        foreach (var met in new[] { true, false }) {
            var raw = Holed().poolCategories[0];
            var clean = Holed();
            new RoR2.ClassicStageInfo(clean).Awake();
            Check($"nulldccs: the blender picks the same cards and weights after removal (conditions met: {met})", Blend(raw, met).SequenceEqual(Blend(clean.poolCategories[0], met)) && Blend(raw, met).Count > 0);
        }
        RoR2.DccsPool OnlyEmptyConditional() => Pool(Cat("N", new RoR2.DccsPool.PoolEntry[0], new[] { C(null, 4f, "DLC2") }, new[] { E("fallback", 2f) }));
        var noFlagRaw = OnlyEmptyConditional().poolCategories[0];
        var noFlagClean = OnlyEmptyConditional();
        new RoR2.ClassicStageInfo(noFlagClean).Awake();
        Check("nulldccs: an empty conditional entry never counted as met, so the fallback list is picked both before and after", Blend(noFlagRaw, true).SequenceEqual(new[] { "fallback:2" }) && Blend(noFlagClean.poolCategories[0], true).SequenceEqual(new[] { "fallback:2" }));
        var alwaysRaw = Holed().poolCategories[0];
        var alwaysClean = Holed();
        new RoR2.ClassicStageInfo(alwaysClean).Awake();
        bool alwaysThrew = false;
        try { BlendAlways(alwaysRaw); } catch (NullReferenceException) { alwaysThrew = true; }
        Check("nulldccs: an empty alwaysIncluded entry aborts the game's loop before the fix, and the loop finishes after it", alwaysThrew && BlendAlways(alwaysClean.poolCategories[0]).SequenceEqual(new[] { "a:2", "b:3" }));

        // Never throws into the game.
        Reset();
        fix.GetField("Alive", all).SetValue(null, new Func<object, bool>(o => throw new InvalidOperationException("boom")));
        escaped = null;
        try { var bad = new RoR2.ClassicStageInfo(Holed()); bad.Awake(); bad.Awake(); bad.SetInteractableDCCSP(Holed()); } catch (Exception e) { escaped = e; }
        Check("nulldccs: an error while cleaning never reaches the game and warns once", escaped == null && logs.Count(l => l.Contains("couldn't clean a pool") && l.Contains("boom")) == 1);
        fix.GetField("Alive", all).SetValue(null, alive);

        // The run-start path.
        var onStart = fix.GetMethod("OnRunStart", all);
        fix.GetField("patcher", all).SetValue(null, new Harmony("test.nulldccs2"));
        fix.GetField("game", all).SetValue(null, typeof(Harmony).Assembly);
        Reset();
        onStart.Invoke(null, new object[] { null });
        Check("nulldccs: a game with no ClassicStageInfo is left alone and warned about", logs.Count(l => l.Contains("not applied")) == 1);
        logs.Clear();
        onStart.Invoke(null, new object[] { null });
        Check("nulldccs: the run-start handler only works once", logs.Count == 0);
        fix.GetField("game", all).SetValue(null, null);
        Reset();
        Exception startError = null;
        try { onStart.Invoke(null, new object[] { null }); } catch (Exception e) { startError = e; }
        Check("nulldccs: an error in the run-start handler is caught and warned about", startError == null && logs.Count(l => l.Contains("can't patch ClassicStageInfo, not applied")) == 1);
        fix.GetField("game", all).SetValue(null, typeof(RoR2.ClassicStageInfo).Assembly);
        Reset();
        int postfixesBefore = PostfixCount(typeof(RoR2.ClassicStageInfo));
        onStart.Invoke(null, new object[] { null });
        Check("nulldccs: the run-start handler finds RoR2.ClassicStageInfo by name and patches it", PostfixCount(typeof(RoR2.ClassicStageInfo)) == postfixesBefore + 1 && logs.Count(l => l.Contains("removed before anything walks the pool")) == 1);

        // The load path may only subscribe; patching there can run the game type's static constructor.
        var shieldDll = Mono.Cecil.ModuleDefinition.ReadModule(shieldType.Assembly.Location);
        var fixDef = shieldDll.GetType("CompatShield.NullDccsPoolEntryFix");
        bool Calls(Mono.Cecil.MethodDefinition m, string name) => m.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == name);
        var applyDef = fixDef.Methods.First(m => m.Name == "Apply");
        var startDef = fixDef.Methods.First(m => m.Name == "OnRunStart");
        Check("nulldccs: Apply (on the load path) subscribes to run starts and never patches", Calls(applyDef, "AddEventHandler") && !Calls(applyDef, "Patch") && !Calls(applyDef, "PatchPools"));
        Check("nulldccs: the patch happens from the run-start handler", Calls(startDef, "PatchPools"));

        // The real game members, and the premise that an entry has no other way to reach its cards.
        var managed = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var ror2 = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var csi = ror2.GetType("RoR2.ClassicStageInfo");
        var realPool = ror2.GetType("RoR2.DccsPool");
        var realCategory = realPool.NestedTypes.First(n => n.Name == "Category");
        var realEntry = realPool.NestedTypes.First(n => n.Name == "PoolEntry");
        var realConditional = realPool.NestedTypes.First(n => n.Name == "ConditionalPoolEntry");
        string FieldType(Mono.Cecil.TypeDefinition t, string n) => t.Fields.FirstOrDefault(f => f.Name == n)?.FieldType.FullName;
        Check("nulldccs real: ClassicStageInfo.interactableDccsPool is a DccsPool", FieldType(csi, "interactableDccsPool") == "RoR2.DccsPool");
        Check("nulldccs real: ClassicStageInfo has an instance Awake with no parameters", csi.Methods.Any(m => m.Name == "Awake" && m.Parameters.Count == 0 && !m.IsStatic));
        Check("nulldccs real: ClassicStageInfo has SetInteractableDCCSP(DccsPool)", csi.Methods.Any(m => m.Name == "SetInteractableDCCSP" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "RoR2.DccsPool"));
        Check("nulldccs real: DccsPool.poolCategories is a Category array", FieldType(realPool, "poolCategories") == "RoR2.DccsPool/Category[]");
        Check("nulldccs real: a category has the three entry lists with the expected element types",
            FieldType(realCategory, "alwaysIncluded") == "RoR2.DccsPool/PoolEntry[]" && FieldType(realCategory, "includedIfConditionsMet") == "RoR2.DccsPool/ConditionalPoolEntry[]" && FieldType(realCategory, "includedIfNoConditionsMet") == "RoR2.DccsPool/PoolEntry[]");
        Check("nulldccs real: a category has a name field for the log", FieldType(realCategory, "name") == "System.String");
        Check("nulldccs real: an entry has only dccs, weight and hasBeenSerialized, so an empty dccs has no other way to reach cards",
            realEntry.Fields.Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(new[] { "dccs", "hasBeenSerialized", "weight" }));
        Check("nulldccs real: a conditional entry adds only requiredExpansions", realConditional.Fields.Select(f => f.Name).SequenceEqual(new[] { "requiredExpansions" }));
        Check("nulldccs real: Run has onRunStartGlobal", ror2.GetType("RoR2.Run").Events.Any(e => e.Name == "onRunStartGlobal"));
        var blender = ror2.GetTypes().First(t => t.Name == "DCCSBlender");
        System.Collections.Generic.List<Mono.Cecil.Cil.Instruction> AvailableCalls(Mono.Cecil.MethodDefinition m) => m.Body.Instructions.Where(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "IsAvailable").ToList();
        bool NullBranchInto(Mono.Cecil.MethodDefinition m, Mono.Cecil.Cil.Instruction call) => m.Body.Instructions.Any(i => (i.OpCode == Mono.Cecil.Cil.OpCodes.Brtrue || i.OpCode == Mono.Cecil.Cil.OpCodes.Brtrue_S) && ReferenceEquals(i.Operand, call));
        var selectDef = blender.Methods.First(m => m.Name == "GenerateWeightedCategorySelections");
        var blendDef = blender.Methods.First(m => m.Name == "GetBlendedDCCS");
        var selectCalls = AvailableCalls(selectDef);
        Check("nulldccs real: DCCSBlender null checks every entry in the two optional lists, so removal can't change those picks", selectCalls.Count == 2 && selectCalls.All(c => NullBranchInto(selectDef, c)));
        var blendCalls = AvailableCalls(blendDef);
        Check("nulldccs real: DCCSBlender has no null check on alwaysIncluded, so an empty entry there aborts the game's own rebuild", blendCalls.Count == 1 && !NullBranchInto(blendDef, blendCalls[0]));
        var realVariation = ror2.GetType("RoR2.ClassicStageInfoVariation");
        var realVariantSwap = realVariation?.Methods.FirstOrDefault(m => m.Name == "SwapCSIValuesWithSelf" && m.Parameters.Count == 0 && !m.IsStatic);
        Check("nulldccs real: ClassicStageInfoVariation has a DccsPool interactableDccsPool and an instance SwapCSIValuesWithSelf", realVariation != null && FieldType(realVariation, "interactableDccsPool") == "RoR2.DccsPool" && realVariantSwap != null);
        Check("nulldccs real: SwapCSIValuesWithSelf calls the setter and is far too large for Mono to inline", realVariantSwap != null && Calls(realVariantSwap, "SetInteractableDCCSP") && realVariantSwap.Body.CodeSize > 100);

        // Who walks the pool, and that they all come after Awake.
        var plugins = System.IO.Path.Combine(profile, "plugins");
        System.Collections.Generic.List<Mono.Cecil.MethodReference> CallsIn(string dll, string label) {
            var file = System.IO.Directory.GetFiles(plugins, dll, System.IO.SearchOption.AllDirectories).FirstOrDefault();
            if (file == null) { Console.WriteLine($"SKIP  nulldccs real: {label} ({dll} not found: mod disabled or moved)"); return null; }
            return Mono.Cecil.ModuleDefinition.ReadModule(file).GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions).Select(i => i.Operand as Mono.Cecil.MethodReference).Where(r => r != null).ToList();
        }
        bool HooksStage(System.Collections.Generic.List<Mono.Cecil.MethodReference> refs, string add) => refs.Any(r => r.Name == add && r.DeclaringType.FullName == "On.RoR2.ClassicStageInfo");
        var r2api = CallsIn("R2API.Director.dll", "R2API hooks Start, not Awake");
        if (r2api != null) Check("nulldccs real: R2API.Director hooks ClassicStageInfo.Start, not Awake", HooksStage(r2api, "add_Start") && !HooksStage(r2api, "add_Awake"));
        var qol = CallsIn("QoLChests.dll", "QoLChests hooks Start, not Awake");
        if (qol != null) Check("nulldccs real: QoLChests hooks ClassicStageInfo.Start, not Awake", HooksStage(qol, "add_Start") && !HooksStage(qol, "add_Awake"));
        // The fix can't help MSU: the README and the class comment say so, and this is what backs them.
        var msuFile = System.IO.Directory.GetFiles(plugins, "MSU.Runtime.dll", System.IO.SearchOption.AllDirectories).FirstOrDefault();
        if (msuFile == null) Console.WriteLine("SKIP  nulldccs real: MSU reads only alwaysIncluded (MSU.Runtime.dll not found: mod disabled or moved)");
        else {
            var msuWalk = Mono.Cecil.ModuleDefinition.ReadModule(msuFile).GetTypes().SelectMany(t => t.Methods).FirstOrDefault(m => m.Name == "AddCustomInteractables" && m.HasBody);
            var msuFields = msuWalk?.Body.Instructions.Select(i => i.Operand as Mono.Cecil.FieldReference).Where(f => f != null).Select(f => f.Name).ToList();
            Check("nulldccs real: MSU has its own null check and reads only alwaysIncluded, so removing entries can't stop its error", msuFields != null && msuFields.Contains("alwaysIncluded") && !msuFields.Contains("includedIfConditionsMet") && !msuFields.Contains("includedIfNoConditionsMet") && Calls(msuWalk, "op_Implicit"));
        }
        // Every plugin, because the class comment says no installed mod hooks Awake through MonoMod.
        int scanned = 0;
        var awakeHookers = new System.Collections.Generic.List<string>();
        foreach (var dll in System.IO.Directory.GetFiles(plugins, "*.dll", System.IO.SearchOption.AllDirectories)) {
            if (System.IO.Path.GetFileName(dll).StartsWith("MMHOOK")) continue;
            try {
                using (var module = Mono.Cecil.ModuleDefinition.ReadModule(dll)) {
                    scanned++;
                    if (module.GetMemberReferences().Any(r => r.Name == "add_Awake" && (r.DeclaringType.FullName == "On.RoR2.ClassicStageInfo" || r.DeclaringType.FullName == "IL.RoR2.ClassicStageInfo"))) awakeHookers.Add(System.IO.Path.GetFileName(dll));
                }
            } catch (Exception) { }
        }
        Check($"nulldccs real: no installed plugin hooks ClassicStageInfo.Awake through MonoMod ({scanned} DLLs scanned{(awakeHookers.Count > 0 ? ": " + string.Join(", ", awakeHookers) : "")})", scanned > 50 && awakeHookers.Count == 0);
        return failures == 0 ? 0 : 1;
    }

    public class FakeGuardHealth { public string label; public object body; public bool brokenGetter; public float shield = 4f, barrier, health = 50f; public float fullShield => brokenGetter ? throw new InvalidOperationException("broken") : 10f; public float fullHealth => 100f; public override string ToString() => label; }
    public class FakeGuardBody { public object inventory, master, inputBank; }
    public class FakeGuardBlast {
        public struct BlastAttackDamageInfo { public object attacker, inflictor, hitHealthComponent; public int tag; }
        public static Action<int> OnHit;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void PerformDamageServer(in BlastAttackDamageInfo blastAttackDamageInfo) { OnHit?.Invoke(blastAttackDamageInfo.tag); }
    }
    public class FakeGuardInstance { public struct BlastAttackDamageInfo { } public void PerformDamageServer(in BlastAttackDamageInfo i) { } }
    public class FakeGuardByValue { public struct BlastAttackDamageInfo { } public static void PerformDamageServer(BlastAttackDamageInfo i) { } }
    public class FakeGuardNoInfo { public static void PerformDamageServer(int i) { } }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void GuardThrowA() { object o = null; o.GetHashCode(); }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void GuardThrowB() { throw new NullReferenceException("b"); }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void GuardThrowKind(bool other) { throw other ? (Exception)new InvalidOperationException("c") : new NullReferenceException("k"); }

    static int BlastGuardCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var guard = shieldType.Assembly.GetType("CompatShield.BlastTargetGuard");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var dead = new System.Collections.Generic.HashSet<object>();
        var bodies = new System.Collections.Generic.Dictionary<object, object>();
        var realName = new Func<object, string>(o => o.ToString());
        guard.GetField("Alive", all).SetValue(null, new Func<object, bool>(o => o != null && !dead.Contains(o)));
        guard.GetField("NameOf", all).SetValue(null, realName);
        guard.GetField("BodyOf", all).SetValue(null, new Func<object, object>(a => bodies.TryGetValue(a, out var b) ? b : null));
        int maxKinds = (int)guard.GetField("MaxKinds", all).GetRawConstantValue();
        int Total() => (int)guard.GetProperty("Total", all).GetValue(null);
        void Reset() { guard.GetMethod("ResetForTest", all).Invoke(null, null); logs.Clear(); dead.Clear(); FakeGuardBlast.OnHit = null; }
        System.Collections.Generic.List<string> Lines() => logs.Where(l => l.Contains("a hit failed")).ToList();
        void Hit(int tag, object victim, object attacker, object inflictor = null) {
            var info = new FakeGuardBlast.BlastAttackDamageInfo { attacker = attacker, inflictor = inflictor, hitHealthComponent = victim, tag = tag };
            FakeGuardBlast.PerformDamageServer(in info);
        }

        var h = new Harmony("test.blastguard");
        bool Patch(Type t) => (bool)guard.GetMethod("PatchGuard", all).Invoke(null, new object[] { h, t });
        bool Warned() { bool w = logs.Count(l => l.Contains("not applied")) == 1; logs.Clear(); return w; }
        Reset();
        Check("blastguard: an instance PerformDamageServer is left alone and warned about", !Patch(typeof(FakeGuardInstance)) && Warned());
        Check("blastguard: a by-value PerformDamageServer is left alone and warned about", !Patch(typeof(FakeGuardByValue)) && Warned());
        Check("blastguard: a blast type without the hit info struct is left alone", !Patch(typeof(FakeGuardNoInfo)) && Warned());
        Check("blastguard: a missing BlastAttack type is left alone", !Patch(null) && Warned());
        Check("blastguard: patches static void PerformDamageServer(in info) and says so", Patch(typeof(FakeGuardBlast)) && logs.Count(l => l.Contains("Blast guard: one target")) == 1);
        Check("blastguard: the finalizer returns Exception, the only form that can stop one", guard.GetMethod("Finalizer", all).ReturnType == typeof(Exception));

        var beetle = new FakeGuardHealth { label = "BeetleBody(Clone)", body = new FakeGuardBody { master = new object() } };
        var lemurian = new FakeGuardHealth { label = "LemurianBody(Clone)", body = new FakeGuardBody { inventory = new object(), master = new object() } };
        const string commando = "CommandoBody(Clone)", engi = "EngiBody(Clone)", delay = "DelayBlast(Clone)";
        bodies[commando] = new FakeGuardBody { inventory = new object(), master = new object() };

        // The behaviour the fix exists for: the blast carries on past a target that throws.
        Reset();
        var ran = new System.Collections.Generic.List<int>();
        FakeGuardBlast.OnHit = t => { ran.Add(t); if (t == 2) GuardThrowA(); };
        Exception escaped = null;
        try { foreach (var t in new[] { 1, 2, 3 }) Hit(t, beetle, commando, delay); } catch (Exception e) { escaped = e; }
        Check("blastguard: one target's exception does not reach the blast", escaped == null);
        Check("blastguard: the targets after the failed one still run", ran.SequenceEqual(new[] { 1, 2, 3 }));
        Check("blastguard: one failed hit is counted once", Total() == 1 && Lines().Count == 1);
        var line = Lines().First();
        Check("blastguard: the hit info passed by in reaches the finalizer, naming victim, attacker and inflictor", line.Contains("Victim BeetleBody(Clone)") && line.Contains("Attacker CommandoBody(Clone)") && line.Contains("Inflictor DelayBlast(Clone)"));
        Check("blastguard: the line says which victim parts are null", line.Contains("body ok, inventory null, master ok, shield 4/10, barrier 0, health 50/100"));
        Check("blastguard: the line says which attacker parts are null", line.Contains("Attacker CommandoBody(Clone): body ok, inventory ok, input bank null"));
        Check("blastguard: the line names the exception and the throw site", line.Contains("NullReferenceException") && line.Contains("GuardThrowA"));
        Check("blastguard: it is a warning, so a swallowed error stays visible", line.StartsWith("[Warning:CompatShield]"));

        Reset(); ran.Clear();
        FakeGuardBlast.OnHit = t => ran.Add(t);
        Hit(7, beetle, commando);
        Check("blastguard: a clean hit runs, logs nothing and counts nothing", ran.SequenceEqual(new[] { 7 }) && logs.Count == 0 && Total() == 0);

        // One line per kind.
        Reset();
        FakeGuardBlast.OnHit = t => { if (t == 1) GuardThrowA(); else if (t == 2) GuardThrowB(); else GuardThrowKind(t == 4); };
        for (int i = 0; i < 5; i++) Hit(1, beetle, commando);
        Check("blastguard: the same failure five times is one line and a count of 5", Lines().Count == 1 && Total() == 5 && logs.Count == 1);
        Hit(2, beetle, commando);
        Check("blastguard: a different throw site is a new line", Lines().Count == 2);
        Hit(3, beetle, commando);
        Check("blastguard: another throw site is another line", Lines().Count == 3 && Lines()[2].Contains("GuardThrowKind"));
        Hit(4, beetle, commando);
        Check("blastguard: a different exception type from the same line of code is a new line", Lines().Count == 4 && Lines()[3].Contains("InvalidOperationException"));
        Hit(1, lemurian, commando);
        Check("blastguard: a different victim is a new line", Lines().Count == 5);
        Hit(1, beetle, engi);
        Check("blastguard: a different attacker is a new line", Lines().Count == 6);

        Reset();
        FakeGuardBlast.OnHit = t => GuardThrowA();
        for (int i = 0; i < maxKinds + 10; i++) Hit(1, new FakeGuardHealth { label = "v" + i }, commando);
        Check("blastguard: new kinds stop at the cap and the count carries on", Lines().Count == maxKinds && Total() == maxKinds + 10);
        Check("blastguard: the last line says only new throw sites get a line from here", Lines().Last().Contains("only a new throw site") && !Lines().First().Contains("only a new throw site"));
        FakeGuardBlast.OnHit = t => GuardThrowB();
        Hit(1, beetle, commando);
        Check("blastguard: with the kinds full, a second bug at a new throw site still gets a line", Lines().Count == maxKinds + 1 && Lines().Last().Contains("GuardThrowB"));
        Hit(1, lemurian, commando);
        Check("blastguard: with the kinds full, the same bug on another victim gets no line", Lines().Count == maxKinds + 1);
        var sites = (System.Collections.Generic.HashSet<string>)guard.GetField("sites", all).GetValue(null);
        int maxSites = (int)guard.GetField("MaxSites", all).GetRawConstantValue();
        for (int i = 0; sites.Count < maxSites - 1; i++) sites.Add("filler" + i);
        FakeGuardBlast.OnHit = t => GuardThrowKind(false);
        Hit(1, beetle, commando);
        Check("blastguard: the last throw site line says logging has stopped", Lines().Count == maxKinds + 2 && Lines().Last().Contains("the last new one logged"));
        FakeGuardBlast.OnHit = t => GuardThrowKind(true);
        int beforeLast = Total();
        Hit(1, beetle, commando);
        Check("blastguard: past both caps nothing more is logged and the count carries on", Lines().Count == maxKinds + 2 && Total() == beforeLast + 1);

        Reset();
        FakeGuardBlast.OnHit = t => GuardThrowA();
        for (int i = 0; i < 1000; i++) Hit(1, beetle, commando);
        Check("blastguard: running totals appear at 10, 100 and 1000", new[] { 10, 100, 1000 }.All(n => logs.Count(l => l.Contains($"Blast guard: {n} failed hits")) == 1) && logs.Count == 4);

        // Whatever is missing, the finalizer must not throw.
        Reset();
        FakeGuardBlast.OnHit = t => GuardThrowA();
        Hit(1, null, null);
        Check("blastguard: no victim and no attacker are reported, not thrown on", Lines().Count == 1 && Lines()[0].Contains("Victim none: body unknown, inventory unknown") && Lines()[0].Contains("Attacker none: body unknown"));
        var gone = new FakeGuardHealth { label = "gone" }; dead.Add(gone);
        Hit(1, gone, engi);
        Check("blastguard: a destroyed victim is reported as destroyed", Lines().Count == 2 && Lines()[1].Contains("Victim destroyed") && Lines()[1].Contains("Attacker EngiBody(Clone): body null"));
        Hit(1, new FakeGuardHealth { label = "headless" }, engi);
        Check("blastguard: a victim with no body says so", Lines().Count == 3 && Lines()[2].Contains("body null, inventory unknown, master unknown"));
        Hit(1, new FakeGuardHealth { label = "broken", brokenGetter = true }, engi);
        Check("blastguard: a getter that throws reads as unknown", Lines().Count == 4 && Lines()[3].Contains("shield 4/unknown"));

        Reset();
        guard.GetField("NameOf", all).SetValue(null, new Func<object, string>(o => throw new InvalidOperationException("no name")));
        FakeGuardBlast.OnHit = t => GuardThrowA();
        escaped = null;
        try { Hit(1, beetle, commando); Hit(1, beetle, commando); } catch (Exception e) { escaped = e; }
        guard.GetField("NameOf", all).SetValue(null, realName);
        Check("blastguard: a failure while describing the hit is still swallowed, counted and warned about once", escaped == null && Total() == 2 && logs.Count(l => l.Contains("couldn't describe")) == 1);

        // Together with the blast cap, which counts the blasts running inside each other.
        var cap = shieldType.Assembly.GetType("CompatShield.BlastChainCap");
        cap.GetField("NowMs", all).SetValue(null, new Func<double>(() => 0));
        cap.GetField("AttackerName", all).SetValue(null, new Func<object, string>(o => "x"));
        cap.GetMethod("ResetForTest", all).Invoke(null, null);
        cap.GetMethod("PatchBlast", all).Invoke(null, new object[] { h, typeof(FakeBlast) });
        Reset();
        FakeGuardBlast.OnHit = t => GuardThrowA();
        bool afterInner = false;
        FakeBlast.OnHits = b => { if (b.level == 0) { new FakeBlast { level = 1 }.HandleHits(new FakeBlast.HitPoint[0]); afterInner = true; } else Hit(1, beetle, commando); };
        escaped = null;
        try { new FakeBlast { level = 0 }.HandleHits(new FakeBlast.HitPoint[0]); } catch (Exception e) { escaped = e; }
        Check("blastguard: a failed hit inside a nested blast leaves the outer blast running and the blast cap count at 0", escaped == null && afterInner && (int)cap.GetProperty("Depth", all).GetValue(null) == 0 && Total() == 1);

        // The run-start path.
        var onStart = guard.GetMethod("OnRunStart", all);
        guard.GetField("patcher", all).SetValue(null, new Harmony("test.blastguard2"));
        guard.GetField("game", all).SetValue(null, typeof(FakeGuardBlast).Assembly);
        Reset();
        onStart.Invoke(null, new object[] { null });
        Check("blastguard: a game with no BlastAttack is left alone and warned about", logs.Count(l => l.Contains("not applied")) == 1);
        logs.Clear();
        onStart.Invoke(null, new object[] { null });
        Check("blastguard: the run-start handler only works once", logs.Count == 0);
        guard.GetField("game", all).SetValue(null, null);
        Reset();
        Exception startError = null;
        try { onStart.Invoke(null, new object[] { null }); } catch (Exception e) { startError = e; }
        Check("blastguard: an error in the run-start handler is caught and warned about", startError == null && logs.Count(l => l.Contains("can't patch BlastAttack, not applied")) == 1);

        // The load path may only subscribe; patching there can run the game type's static constructor.
        var shieldDll = Mono.Cecil.ModuleDefinition.ReadModule(shieldType.Assembly.Location);
        var guardDef = shieldDll.GetType("CompatShield.BlastTargetGuard");
        bool Calls(Mono.Cecil.MethodDefinition m, string name) => m.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == name);
        var applyDef = guardDef.Methods.First(m => m.Name == "Apply"); var startDef = guardDef.Methods.First(m => m.Name == "OnRunStart");
        Check("blastguard: Apply (on the load path) subscribes to run starts and never patches", Calls(applyDef, "AddEventHandler") && !Calls(applyDef, "Patch") && !Calls(applyDef, "PatchGuard"));
        Check("blastguard: the patch happens from the run-start handler", Calls(startDef, "PatchGuard"));

        // The real game members.
        var managed = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var ror2 = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var blast = ror2.GetType("RoR2.BlastAttack");
        var info2 = blast.NestedTypes.FirstOrDefault(n => n.Name == "BlastAttackDamageInfo");
        var perform = blast.Methods.FirstOrDefault(m => m.Name == "PerformDamageServer" && m.Parameters.Count == 1);
        Check("blastguard real: game 1.5.0 has static void BlastAttack.PerformDamageServer(in BlastAttackDamageInfo)", info2 != null && perform != null && perform.IsStatic && perform.ReturnType.FullName == "System.Void" && perform.Parameters[0].ParameterType.IsByReference && perform.Parameters[0].ParameterType.GetElementType().Name == "BlastAttackDamageInfo");
        Check("blastguard real: the hit info has hitHealthComponent, attacker and inflictor", new[] { "hitHealthComponent", "attacker", "inflictor" }.All(n => info2.Fields.Any(f => f.Name == n)));
        bool Has(Mono.Cecil.TypeDefinition t, string n) => t.Fields.Any(f => f.Name == n) || t.Properties.Any(p => p.Name == n);
        var health = ror2.GetType("RoR2.HealthComponent"); var body = ror2.GetType("RoR2.CharacterBody");
        Check("blastguard real: HealthComponent has body, shield, fullShield, barrier, health and fullHealth", new[] { "body", "shield", "fullShield", "barrier", "health", "fullHealth" }.All(n => Has(health, n)));
        Check("blastguard real: CharacterBody has inventory, master and inputBank", new[] { "inventory", "master", "inputBank" }.All(n => Has(body, n)));
        Check("blastguard real: Run has onRunStartGlobal", ror2.GetType("RoR2.Run").Events.Any(e => e.Name == "onRunStartGlobal"));
        return failures == 0 ? 0 : 1;
    }

    public class FakeStatsSkills { public object primaryBonusStockSkill = new object(), secondaryBonusStockSkill, utilityBonusStockSkill = new object(), specialBonusStockSkill = new object(); }
    public class FakeStatsInventory { public System.Collections.Generic.List<int> itemAcquisitionOrder = new System.Collections.Generic.List<int>(); }
    public class FakeStatsBrokenInventory { public System.Collections.Generic.List<int> itemAcquisitionOrder { get { throw new InvalidOperationException("broken"); } } }
    public class FakeStatsGetter { public object inputBank { get { throw new InvalidOperationException("broken"); } } }
    public class FakeStatsBody {
        public string label = "BeetleBody(Clone)";
        public object inventory, master = new object(), healthComponent, skillLocator, equipmentSlot, characterMotor = new object(), inputBank, modelLocator = new object(), teamComponent = new object();
        public float level = 7f;
        public bool isPlayerControlled = true;
        private bool statsDirty;
        private int[] activeBuffsList = new int[4];
        private int activeBuffsListCount;
        public int calls;
        public Action fail;
        public bool Dirty { get { return statsDirty; } set { statsDirty = value; } }
        public void SetBuffs(params int[] b) { activeBuffsList = new int[b.Length + 3]; b.CopyTo(activeBuffsList, 0); activeBuffsListCount = b.Length; }
        public override string ToString() { return label; }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void RecalculateStats() { calls++; if (fail != null) fail(); statsDirty = false; }
    }
    public class FakeStatsTick {
        public static event Action onFixedUpdate;
        public static event Action<int> wrongShape;
        public static void Fire() { if (onFixedUpdate != null) onFixedUpdate(); }
        public static void Touch() { if (wrongShape != null) wrongShape(0); }
    }
    public class FakeStatsStatic { private bool statsDirty = false; public bool D { get { return statsDirty; } } public static void RecalculateStats() { } }
    public class FakeStatsNoFlag { public void RecalculateStats() { } }
    public class FakeStatsIntFlag { private int statsDirty = 0; public int D { get { return statsDirty; } } public void RecalculateStats() { } }
    public class FakeStatsReturns { private bool statsDirty = false; public bool D { get { return statsDirty; } } public int RecalculateStats() { return 0; } }
    public class FakeStatsNoTickYet { private bool statsDirty = false; public bool D { get { return statsDirty; } } [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] public void RecalculateStats() { } }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void StatsThrowA() { object o = null; o.GetHashCode(); }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void StatsThrowB() { throw new NullReferenceException("b"); }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void StatsThrowKind(bool other) { throw other ? (Exception)new InvalidOperationException("c") : new NullReferenceException("k"); }

    static int StatsGuardCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var guard = shieldType.Assembly.GetType("CompatShield.StatsRetryGuard");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var dead = new System.Collections.Generic.HashSet<object>();
        double now = 1000;
        int clockReads = 0;
        var aliveField = guard.GetField("Alive", all); var nameField = guard.GetField("NameOf", all);
        aliveField.SetValue(null, new Func<object, bool>(o => o != null && !dead.Contains(o)));
        nameField.SetValue(null, new Func<object, string>(o => o.ToString()));
        guard.GetField("ItemName", all).SetValue(null, new Func<object, string>(i => "Item" + i));
        guard.GetField("BuffName", all).SetValue(null, new Func<object, string>(b => "Buff" + b));
        guard.GetField("Clock", all).SetValue(null, new Func<double>(() => { clockReads++; return now; }));
        int maxKinds = (int)guard.GetField("MaxKinds", all).GetRawConstantValue();
        int maxSites = (int)guard.GetField("MaxSites", all).GetRawConstantValue();
        int maxListed = (int)guard.GetField("MaxListed", all).GetRawConstantValue();
        int Total() => (int)guard.GetProperty("Total", all).GetValue(null);
        int Early() => (int)guard.GetProperty("Early", all).GetValue(null);
        int Failing() => (int)guard.GetProperty("Failing", all).GetValue(null);
        void Reset() { guard.GetMethod("ResetForTest", all).Invoke(null, null); logs.Clear(); dead.Clear(); now = 1000; clockReads = 0; }
        System.Collections.Generic.List<string> Lines() => logs.Where(l => l.Contains("a stat update failed")).ToList();
        Exception Step(FakeStatsBody b) { try { if (b.Dirty) b.RecalculateStats(); return null; } catch (Exception e) { return e; } }
        FakeStatsBody Failing2(string label = "BeetleBody(Clone)") { var b = new FakeStatsBody { label = label, fail = StatsThrowA, Dirty = true }; Step(b); Step(b); return b; }

        var h = new Harmony("test.statsguard");
        var goodTick = typeof(FakeStatsTick).GetEvent("onFixedUpdate");
        bool Patch(Type t, EventInfo e) => (bool)guard.GetMethod("PatchGuard", all).Invoke(null, new object[] { h, t, e });
        bool Warned() { bool w = logs.Count(l => l.Contains("not applied")) == 1; logs.Clear(); return w; }
        Reset();
        Check("statsguard: a static RecalculateStats is left alone and warned about", !Patch(typeof(FakeStatsStatic), goodTick) && Warned());
        Check("statsguard: a body type with no statsDirty is left alone and warned about", !Patch(typeof(FakeStatsNoFlag), goodTick) && Warned());
        Check("statsguard: a statsDirty that is not a bool is left alone and warned about", !Patch(typeof(FakeStatsIntFlag), goodTick) && Warned());
        Check("statsguard: a RecalculateStats that returns a value is left alone and warned about", !Patch(typeof(FakeStatsReturns), goodTick) && Warned());
        Check("statsguard: a missing CharacterBody type is left alone and warned about", !Patch(null, goodTick) && Warned());
        Check("statsguard: no physics tick event means no patch, since waiting bodies could never retry", !Patch(typeof(FakeStatsNoTickYet), null) && Warned() && Harmony.GetPatchInfo(typeof(FakeStatsNoTickYet).GetMethod("RecalculateStats")) == null);
        Check("statsguard: a tick event of another shape means no patch", !Patch(typeof(FakeStatsNoTickYet), typeof(FakeStatsTick).GetEvent("wrongShape")) && Warned() && Harmony.GetPatchInfo(typeof(FakeStatsNoTickYet).GetMethod("RecalculateStats")) == null);
        Check("statsguard: patches the right shape and says so", Patch(typeof(FakeStatsBody), goodTick) && logs.Count(l => l.Contains("Stats guard: a character whose stat update keeps failing")) == 1);
        Check("statsguard: the finalizer returns void, so it cannot swallow or restart the exception", guard.GetMethod("Finalizer", all).ReturnType == typeof(void));

        // The behaviour the fix exists for.
        Reset();
        var b1 = new FakeStatsBody { fail = StatsThrowA, Dirty = true };
        var err = Step(b1);
        Check("statsguard: the error still reaches the caller, as it did without the guard", err is NullReferenceException);
        Check("statsguard: the error keeps its real stack trace", err != null && err.StackTrace.Contains("StatsThrowA"));
        Check("statsguard: the first failure leaves the flag set so the next tick retries", b1.Dirty && b1.calls == 1 && Total() == 1);
        err = Step(b1);
        Check("statsguard: the second failure in a row clears the flag and the error still reaches the caller", !b1.Dirty && b1.calls == 2 && err is NullReferenceException);
        var waits = new[] { 0.25, 0.5, 1.0, 2.0, 4.0, 8.0, 8.0, 8.0 };
        bool timing = true;
        foreach (var w in waits) {
            int before = b1.calls; double start = now;
            now = start + w - 0.01; FakeStatsTick.Fire(); Step(b1);
            timing &= b1.calls == before && !b1.Dirty;
            now = start + w; FakeStatsTick.Fire();
            timing &= b1.Dirty;
            Step(b1);
            timing &= b1.calls == before + 1 && !b1.Dirty;
        }
        Check("statsguard: retries come exactly 0.25, 0.5, 1, 2, 4, 8, 8 and 8 seconds apart and not before", timing);

        Reset();
        var busy = new FakeStatsBody { fail = StatsThrowA, Dirty = true };
        double t0 = now;
        for (int i = 1; i <= 3600; i++) { now = t0 + i / 60.0; FakeStatsTick.Fire(); Step(busy); }
        Check("statsguard: 60 seconds of failure at 60 ticks a second runs the method about a dozen times, not 3600 (" + busy.calls + ")", busy.calls >= 10 && busy.calls <= 16);

        Reset();
        var glitch = new FakeStatsBody { fail = StatsThrowA, Dirty = true };
        Step(glitch); glitch.fail = null; Step(glitch);
        Check("statsguard: a one-tick glitch retries on the next tick and is forgotten", glitch.calls == 2 && !glitch.Dirty && Failing() == 0);

        Reset();
        var heal = Failing2();
        now += 0.25; FakeStatsTick.Fire(); heal.fail = null; Step(heal);
        Check("statsguard: a body that recovers is forgotten and the game clears its flag", Failing() == 0 && !heal.Dirty && heal.calls == 3);
        heal.fail = StatsThrowA; heal.Dirty = true; Step(heal);
        Check("statsguard: a later failure starts over with a next-tick retry", heal.Dirty && heal.calls == 4);

        Reset();
        var again = Failing2();
        now += 0.1; again.Dirty = true; Step(again);
        Check("statsguard: a body marked dirty during the wait runs at once, because something changed", again.calls == 3);
        Check("statsguard: if that fails it counts as early and the flag is cleared again", Early() == 1 && !again.Dirty);
        again.fail = null; again.Dirty = true; Step(again);
        Check("statsguard: a success during the wait ends it", Failing() == 0 && !again.Dirty);
        now += 100; FakeStatsTick.Fire();
        Check("statsguard: the old timer does not mark a recovered body dirty", !again.Dirty);

        Reset();
        var gone = Failing2();
        dead.Add(gone); FakeStatsTick.Fire();
        Check("statsguard: a destroyed body is dropped on the next tick and never marked dirty", Failing() == 0 && !gone.Dirty);

        Reset();
        var first = Failing2("A"); var second = new FakeStatsBody { label = "B", fail = StatsThrowA, Dirty = true }; Step(second);
        Check("statsguard: each body has its own count", !first.Dirty && second.Dirty && Failing() == 2);
        now += 0.25; FakeStatsTick.Fire();
        Check("statsguard: a body on its first failure keeps its next-tick retry while another waits", first.Dirty && second.Dirty && first.calls == 2);

        Reset();
        var soon = Failing2("A"); now += 0.1; var later = Failing2("B");
        now += 0.2; FakeStatsTick.Fire();
        Check("statsguard: a due retry only marks the body that is due", soon.Dirty && !later.Dirty && soon.calls == 2 && later.calls == 2);

        Reset();
        var direct = new FakeStatsBody { label = "direct", fail = StatsThrowA };
        for (int i = 0; i < 3; i++) { try { direct.RecalculateStats(); } catch (NullReferenceException) { } }
        now += 100; FakeStatsTick.Fire();
        Check("statsguard: a direct call that fails with the flag already clear starts no retries the game never made", !direct.Dirty && direct.calls == 3 && Total() == 3);
        direct.Dirty = true; Step(direct);
        Check("statsguard: a body that then fails through the flag is held back straight away", !direct.Dirty && direct.calls == 4);

        Reset();
        var midWait = Failing2();
        try { midWait.RecalculateStats(); } catch (NullReferenceException) { }
        Check("statsguard: a direct call that fails during a wait counts as early", Early() == 1 && midWait.calls == 3 && !midWait.Dirty);
        now += 100; FakeStatsTick.Fire();
        Check("statsguard: the retry still comes after a direct call failed during the wait", midWait.Dirty);

        Reset();
        FakeStatsTick.Fire();
        var healthy = new FakeStatsBody { Dirty = true }; Step(healthy); FakeStatsTick.Fire();
        Check("statsguard: nothing failing means the tick does no work and a healthy update is untouched", clockReads == 0 && !healthy.Dirty && Total() == 0 && Failing() == 0);

        Reset();
        var ticking = Failing2();
        var realAlive = aliveField.GetValue(null);
        aliveField.SetValue(null, new Func<object, bool>(o => { throw new InvalidOperationException("boom"); }));
        Exception tickError = null; try { FakeStatsTick.Fire(); FakeStatsTick.Fire(); } catch (Exception e) { tickError = e; }
        aliveField.SetValue(null, realAlive);
        Check("statsguard: an error in the tick never escapes and is warned about once", tickError == null && logs.Count(l => l.Contains("logged once")) == 1);

        Reset();
        var realName = nameField.GetValue(null);
        nameField.SetValue(null, new Func<object, string>(o => { throw new InvalidOperationException("boom"); }));
        var quiet = new FakeStatsBody { fail = StatsThrowA, Dirty = true };
        var q1 = Step(quiet); var q2 = Step(quiet);
        nameField.SetValue(null, realName);
        Check("statsguard: a failure while describing never stops the wait or the real error", q1 is NullReferenceException && q2 is NullReferenceException && !quiet.Dirty && logs.Count(l => l.Contains("logged once")) == 1);

        // What the log line says.
        Reset();
        var rich = new FakeStatsBody { fail = StatsThrowA, Dirty = true, skillLocator = new FakeStatsSkills() };
        var bag = new FakeStatsInventory(); bag.itemAcquisitionOrder.Add(3); bag.itemAcquisitionOrder.Add(9); rich.inventory = bag; rich.SetBuffs(5, 6);
        Step(rich);
        string line = Lines().FirstOrDefault() ?? "";
        Check("statsguard log: names the body and the error", Lines().Count == 1 && line.Contains("BeetleBody(Clone)") && line.Contains("NullReferenceException"));
        Check("statsguard log: says which parts are there and which are null", line.Contains("inventory ok") && line.Contains("master ok") && line.Contains("healthComponent null") && line.Contains("inputBank null") && line.Contains("skillLocator ok") && line.Contains("teamComponent ok"));
        Check("statsguard log: says which stock skills are null", line.Contains("primary ok") && line.Contains("secondary null") && line.Contains("special ok"));
        Check("statsguard log: gives level and player control", line.Contains("Level 7") && line.Contains("player controlled True"));
        Check("statsguard log: names items and buffs, and only the live buffs", line.Contains("Items: Item3, Item9.") && line.Contains("Buffs: Buff5, Buff6."));
        Check("statsguard log: gives the throw site", line.Contains("Thrown at:") && line.Contains("StatsThrowA"));
        Step(rich);
        Check("statsguard log: the same kind gets no second line", Lines().Count == 1);

        Reset();
        var bare = new FakeStatsBody { fail = StatsThrowA, Dirty = true }; Step(bare);
        Check("statsguard log: no inventory and no buffs read as unknown and none, and stock skills as unknown", Lines()[0].Contains("Items: unknown.") && Lines()[0].Contains("Buffs: none.") && Lines()[0].Contains("primary unknown"));

        Reset();
        var broken = new FakeStatsBody { fail = StatsThrowA, Dirty = true, inventory = new FakeStatsBrokenInventory() }; Step(broken);
        Check("statsguard log: a getter that throws reads as unreadable and the line still goes out", Lines().Count == 1 && Lines()[0].Contains("Items: unreadable."));
        Check("statsguard log: a part with a broken getter reads as unreadable", (string)guard.GetMethod("Has", all).Invoke(null, new object[] { new FakeStatsGetter(), "inputBank" }) == "unreadable");

        Reset();
        var hoard = new FakeStatsBody { fail = StatsThrowA, Dirty = true }; var pile = new FakeStatsInventory();
        for (int i = 0; i < maxListed + 5; i++) pile.itemAcquisitionOrder.Add(i); hoard.inventory = pile; Step(hoard);
        Check("statsguard log: a long item list is cut and says how many more", Lines()[0].Contains("and 5 more") && !Lines()[0].Contains("Item" + maxListed + ","));

        Reset();
        var v1 = new FakeStatsBody { label = "A", fail = StatsThrowA, Dirty = true }; Step(v1);
        var v2 = new FakeStatsBody { label = "B", fail = StatsThrowA, Dirty = true }; Step(v2);
        Check("statsguard log: another body is another line", Lines().Count == 2);
        var v3 = new FakeStatsBody { label = "B", fail = StatsThrowB, Dirty = true }; Step(v3);
        Check("statsguard log: another throw site is another line", Lines().Count == 3 && Lines()[2].Contains("StatsThrowB"));
        var v4 = new FakeStatsBody { label = "B", fail = () => StatsThrowKind(false), Dirty = true }; Step(v4);
        var v5 = new FakeStatsBody { label = "B", fail = () => StatsThrowKind(true), Dirty = true }; Step(v5);
        Check("statsguard log: another exception type from the same line of code is another line", Lines().Count == 5 && Lines()[4].Contains("InvalidOperationException"));

        Reset();
        for (int i = 0; i < maxKinds; i++) { var k = new FakeStatsBody { label = "Body" + i, fail = StatsThrowA, Dirty = true }; Step(k); }
        Check("statsguard log: the last kind says only new throw sites get a line from here", Lines().Count == maxKinds && Lines().Last().Contains("only a new throw site") && !Lines().First().Contains("only a new throw site"));
        var bugTwo = new FakeStatsBody { label = "BodyX", fail = StatsThrowB, Dirty = true }; Step(bugTwo);
        Check("statsguard log: with the kinds full, a second bug at a new throw site still gets a line", Lines().Count == maxKinds + 1 && Lines().Last().Contains("StatsThrowB"));
        var sameBug = new FakeStatsBody { label = "BodyY", fail = StatsThrowA, Dirty = true }; Step(sameBug);
        Check("statsguard log: with the kinds full, the same bug on another body gets no line", Lines().Count == maxKinds + 1);
        var sites = (System.Collections.Generic.HashSet<string>)guard.GetField("sites", all).GetValue(null);
        for (int i = 0; sites.Count < maxSites - 1; i++) sites.Add("filler" + i);
        var lastSite = new FakeStatsBody { label = "BodyZ", fail = () => StatsThrowKind(false), Dirty = true }; Step(lastSite);
        Check("statsguard log: the last throw site says logging has stopped", Lines().Count == maxKinds + 2 && Lines().Last().Contains("the last new one logged"));
        int beforeLast = Total();
        var pastCaps = new FakeStatsBody { label = "BodyW", fail = () => StatsThrowKind(true), Dirty = true }; Step(pastCaps);
        Check("statsguard log: past both caps nothing more is logged and the count carries on", Lines().Count == maxKinds + 2 && Total() == beforeLast + 1);

        Reset();
        var many = new FakeStatsBody { fail = StatsThrowA };
        for (int i = 0; i < 1000; i++) { many.Dirty = true; Step(many); }
        var marks = logs.Where(l => l.Contains("failed stat updates so far")).ToList();
        Check("statsguard log: the running total shows at 10, 100 and 1000", marks.Count == 3 && marks[0].Contains(" 10 failed") && marks[1].Contains(" 100 failed") && marks[2].Contains(" 1000 failed"));
        Check("statsguard log: the running total says how many failures came early", marks[2].Contains("came early"));
        Check("statsguard: the wait never passes the longest wait, however many failures", (double)guard.GetMethod("WaitFor", all).Invoke(null, new object[] { int.MaxValue }) == 8.0);

        // The run-start handler, driven for real.
        var onStart = guard.GetMethod("OnRunStart", all);
        guard.GetField("patcher", all).SetValue(null, new Harmony("test.statsguard2"));
        guard.GetField("game", all).SetValue(null, typeof(FakeStatsBody).Assembly);
        Reset();
        onStart.Invoke(null, new object[] { null });
        Check("statsguard: a game with no CharacterBody is left alone and warned about", logs.Count(l => l.Contains("not applied")) == 1);
        logs.Clear();
        onStart.Invoke(null, new object[] { null });
        Check("statsguard: the run-start handler only works once", logs.Count == 0);
        guard.GetField("game", all).SetValue(null, null);
        Reset();
        Exception startError = null;
        try { onStart.Invoke(null, new object[] { null }); } catch (Exception e) { startError = e; }
        Check("statsguard: an error in the run-start handler is caught and warned about", startError == null && logs.Count(l => l.Contains("cannot patch CharacterBody, not applied")) == 1);

        // The load path may only subscribe; patching there can run the game type static constructor.
        var shieldDll = Mono.Cecil.ModuleDefinition.ReadModule(shieldType.Assembly.Location);
        var guardDef = shieldDll.GetType("CompatShield.StatsRetryGuard");
        bool Calls(Mono.Cecil.MethodDefinition m, string name) => m.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == name);
        var applyDef = guardDef.Methods.First(m => m.Name == "Apply"); var startDef = guardDef.Methods.First(m => m.Name == "OnRunStart");
        Check("statsguard: Apply (on the load path) subscribes to run starts and never patches", Calls(applyDef, "AddEventHandler") && !Calls(applyDef, "Patch") && !Calls(applyDef, "PatchGuard"));
        Check("statsguard: the patch happens from the run-start handler", Calls(startDef, "PatchGuard"));

        // The real game members and the premise of the fix.
        var managed = "C:/Program Files (x86)/Steam/steamapps/common/Risk of Rain 2/Risk of Rain 2_Data/Managed";
        var ror2 = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var body = ror2.GetType("RoR2.CharacterBody");
        bool Has(Mono.Cecil.TypeDefinition t, string n) => t.Fields.Any(f => f.Name == n) || t.Properties.Any(p => p.Name == n);
        var recalc = body.Methods.FirstOrDefault(m => m.Name == "RecalculateStats" && m.Parameters.Count == 0);
        Check("statsguard real: game 1.5.0 has instance void CharacterBody.RecalculateStats()", recalc != null && !recalc.IsStatic && recalc.ReturnType.FullName == "System.Void");
        var flag = body.Fields.FirstOrDefault(f => f.Name == "statsDirty");
        Check("statsguard real: statsDirty is an instance bool", flag != null && !flag.IsStatic && flag.FieldType.FullName == "System.Boolean");
        var code = recalc.Body.Instructions;
        var stores = code.Where(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Stfld && i.Operand is Mono.Cecil.FieldReference f && f.Name == "statsDirty").ToList();
        Check("statsguard real: the method clears the flag once, as its very last step", stores.Count == 1 && stores[0].Previous.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_0 && stores[0].Next.OpCode == Mono.Cecil.Cil.OpCodes.Ret);
        var fixedUpdate = body.Methods.FirstOrDefault(m => m.Name == "FixedUpdate").Body.Instructions.ToList();
        int read = fixedUpdate.FindIndex(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldfld && i.Operand is Mono.Cecil.FieldReference f && f.Name == "statsDirty");
        Check("statsguard real: FixedUpdate reads the flag and calls RecalculateStats", read >= 0 && fixedUpdate.Skip(read).Take(5).Any(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "RecalculateStats"));
        var tickDef = ror2.GetType("RoR2.RoR2Application").Events.FirstOrDefault(e => e.Name == "onFixedUpdate");
        Check("statsguard real: RoR2Application has a static Action onFixedUpdate", tickDef != null && tickDef.AddMethod.IsStatic && tickDef.EventType.FullName == "System.Action");
        Check("statsguard real: Run has onRunStartGlobal", ror2.GetType("RoR2.Run").Events.Any(e => e.Name == "onRunStartGlobal"));
        Check("statsguard real: CharacterBody has every part the log line names", new[] { "inventory", "master", "healthComponent", "skillLocator", "equipmentSlot", "characterMotor", "inputBank", "modelLocator", "teamComponent", "level", "isPlayerControlled", "activeBuffsList", "activeBuffsListCount" }.All(n => Has(body, n)));
        Check("statsguard real: SkillLocator has the four stock skills", new[] { "primary", "secondary", "utility", "special" }.All(n => Has(ror2.GetType("RoR2.SkillLocator"), n + "BonusStockSkill")));
        Check("statsguard real: Inventory has itemAcquisitionOrder", Has(ror2.GetType("RoR2.Inventory"), "itemAcquisitionOrder"));
        Check("statsguard real: the item and buff catalogs can name an index", ror2.GetType("RoR2.ItemCatalog").Methods.Count(m => m.Name == "GetItemDef" && m.IsStatic && m.IsPublic && m.Parameters.Count == 1) == 1 && ror2.GetType("RoR2.BuffCatalog").Methods.Count(m => m.Name == "GetBuffDef" && m.IsStatic && m.IsPublic && m.Parameters.Count == 1) == 1);
        return failures == 0 ? 0 : 1;
    }

    public class FakeHitDamage { public object attacker; [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] public void Modify(int m) { } }
    public class FakeHitBody { public object inventory, master, inputBank; }
    public class FakeHitHealth {
        public string label; public object body; public float shield = 4f, barrier, health = 50f, fullShield = 10f, fullHealth = 100f;
        public Action fail;
        public object gameObject { get { return this; } }
        public override string ToString() { return label; }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void TakeDamage(FakeHitDamage d) { FakeHit.Ran.Add("take " + label); if (fail != null) fail(); }
    }
    public class FakeHitEvents {
        public static FakeHitEvents instance = new FakeHitEvents();
        public static Action<object> FailEnemy, FailAll;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void OnHitEnemy(FakeHitDamage d, object victim) { FakeHit.Ran.Add("enemy " + victim); if (FailEnemy != null) FailEnemy(victim); }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void OnHitAll(FakeHitDamage d, object victim) { FakeHit.Ran.Add("all " + victim); if (FailAll != null) FailAll(victim); }
    }
    public class FakeHit {
        public static System.Collections.Generic.List<string> Ran = new System.Collections.Generic.List<string>();
        public class OverlapAttackMessage { public object attacker, inflictor; }
        public struct OverlapInfo { public FakeHitHealth health; }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void PerformDamageServer(OverlapAttackMessage attackData, System.Collections.Generic.List<OverlapInfo> hitList) {
            for (int i = 0; i < hitList.Count; i++) {
                OverlapInfo info = hitList[i];
                FakeHitHealth health = info.health;
                if (health != null) {
                    FakeHitDamage damage = new FakeHitDamage();
                    damage.attacker = attackData.attacker;
                    damage.Modify(0);
                    health.TakeDamage(damage);
                    FakeHitEvents.instance.OnHitEnemy(damage, health.gameObject);
                    FakeHitEvents.instance.OnHitAll(damage, health.gameObject);
                }
            }
        }
    }
    public class FakeHitBranchy {
        public class OverlapAttackMessage { public object attacker, inflictor; }
        public struct OverlapInfo { public FakeHitHealth health; }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void PerformDamageServer(OverlapAttackMessage attackData, System.Collections.Generic.List<OverlapInfo> hitList) {
            for (int i = 0; i < hitList.Count; i++) {
                FakeHitHealth health = hitList[i].health;
                FakeHitDamage damage = new FakeHitDamage();
                damage.Modify(0);
                health.TakeDamage(damage);
                if (attackData.attacker != null) FakeHitEvents.instance.OnHitEnemy(damage, health.gameObject);
                FakeHitEvents.instance.OnHitAll(damage, health.gameObject);
            }
        }
    }
    public class FakeHitInstance { public class OverlapAttackMessage { } public struct OverlapInfo { } public void PerformDamageServer(OverlapAttackMessage a, System.Collections.Generic.List<OverlapInfo> b) { } }
    public class FakeHitWrongList { public class OverlapAttackMessage { } public struct OverlapInfo { } public static void PerformDamageServer(OverlapAttackMessage a, System.Collections.Generic.List<int> b) { } }
    public class FakeHitNoMessage { public static void PerformDamageServer(object a, object b) { } }
    // The shape of OverlapAttack.Fire and ProjectileDotZone.FixedUpdate, with nothing guarding it but the patched damage loop.
    public class FakeHitZone {
        public System.Collections.Generic.List<FakeHit.OverlapInfo> overlapList = new System.Collections.Generic.List<FakeHit.OverlapInfo>();
        public FakeHit.OverlapAttackMessage message = new FakeHit.OverlapAttackMessage();
        public float fireStopwatch = 1f; public int fires;
        public void Tick() { if (fireStopwatch >= 1f) { fires++; FakeHit.PerformDamageServer(message, overlapList); overlapList.Clear(); fireStopwatch -= 1f; } }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void HitThrowA() { object o = null; o.GetHashCode(); }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void HitThrowB() { throw new NullReferenceException("b"); }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void HitThrowKind(bool other) { throw other ? (Exception)new InvalidOperationException("c") : new NullReferenceException("k"); }

    static int OverlapGuardCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var guard = shieldType.Assembly.GetType("CompatShield.OverlapHitGuard");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var dead = new System.Collections.Generic.HashSet<object>();
        var bodies = new System.Collections.Generic.Dictionary<object, object>();
        Func<object, bool> aliveNormal = o => o != null && !dead.Contains(o);
        guard.GetField("Alive", all).SetValue(null, aliveNormal);
        guard.GetField("NameOf", all).SetValue(null, new Func<object, string>(o => o.ToString()));
        guard.GetField("BodyOf", all).SetValue(null, new Func<object, object>(a => bodies.TryGetValue(a, out var b) ? b : null));
        int maxKinds = (int)guard.GetField("MaxKinds", all).GetRawConstantValue();
        int maxSites = (int)guard.GetField("MaxSites", all).GetRawConstantValue();
        int Total() => (int)guard.GetProperty("Total", all).GetValue(null);
        void Reset() { guard.GetMethod("ResetForTest", all).Invoke(null, null); logs.Clear(); dead.Clear(); FakeHit.Ran.Clear(); FakeHitEvents.FailEnemy = FakeHitEvents.FailAll = null; }
        System.Collections.Generic.List<string> Lines() => logs.Where(l => l.Contains("a hit failed")).ToList();
        FakeHitHealth H(string label, Action fail = null, object body = null) => new FakeHitHealth { label = label, fail = fail, body = body };
        void Hit(object attacker, object inflictor, params FakeHitHealth[] targets) {
            FakeHit.PerformDamageServer(new FakeHit.OverlapAttackMessage { attacker = attacker, inflictor = inflictor }, targets.Select(t => new FakeHit.OverlapInfo { health = t }).ToList());
        }

        // The shape check, on its own.
        var find = guard.GetMethod("FindRange", all);
        var stepOf = guard.GetMethod("StepOf", all, null, new[] { typeof(string), typeof(string), typeof(string), typeof(bool) }, null);
        string Step(string op, string flow, string called, bool isVoid) => (string)stepOf.Invoke(null, new object[] { op, flow, called, isVoid });
        Tuple<string, int, int> Find(params string[] s) { var a = new object[] { s.ToList(), -1, -1 }; var w = (string)find.Invoke(null, a); return Tuple.Create(w, (int)a[1], (int)a[2]); }
        Check("overlapguard: a local load is a local step, in every encoding", Step("ldloc.2", "Next", null, false) == "local" && Step("ldloc.s", "Next", null, false) == "local" && Step("ldloc", "Next", null, false) == "local");
        Check("overlapguard: loading a local's address is not a local step", Step("ldloca.s", "Next", null, false) == "other");
        Check("overlapguard: branches, returns and throws are jumps", Step("brtrue.s", "Cond_Branch", null, false) == "jump" && Step("br", "Branch", null, false) == "jump" && Step("ret", "Return", null, false) == "jump" && Step("throw", "Throw", null, false) == "jump");
        Check("overlapguard: a call says its name and whether it leaves a value", Step("callvirt", "Call", "TakeDamage", true) == "void:TakeDamage" && Step("call", "Call", "get_x", false) == "value:get_x");
        Check("overlapguard: a constructor or a function pointer is not a call step", Step("newobj", "Call", ".ctor", true) == "other" && Step("ldftn", "Next", "M", true) == "other");
        var good = Find("void:Modify", "local", "local", "void:TakeDamage", "other", "local", "local", "value:get_gameObject", "void:OnHitEnemy", "other", "local", "void:OnHitAll", "local", "jump");
        Check("overlapguard: the real shape is accepted, from the first argument through OnHitAll", good.Item1 == null && good.Item2 == 1 && good.Item3 == 11);
        var nops = Find("void:Modify", "nop", "nop", "local", "local", "void:TakeDamage", "nop", "void:OnHitEnemy", "nop", "void:OnHitAll", "nop");
        Check("overlapguard: nops from a debug build don't matter", nops.Item1 == null && nops.Item2 == 3 && nops.Item3 == 9);
        bool Refused(params string[] s) { var r = Find(s); return r.Item1 != null && r.Item2 == -1 && r.Item3 == -1; }
        string[] Body(string take) => new[] { "void:M", "local", "local", take, "void:OnHitEnemy", "void:OnHitAll", "local" };
        Check("overlapguard: no TakeDamage is refused", Refused("void:M", "local", "local", "void:OnHitEnemy", "void:OnHitAll", "local"));
        Check("overlapguard: a TakeDamage that returns a value is refused", Refused(Body("value:TakeDamage")));
        Check("overlapguard: two TakeDamage calls are refused", Refused("void:M", "local", "local", "void:TakeDamage", "local", "local", "void:TakeDamage", "void:OnHitEnemy", "void:OnHitAll", "local"));
        Check("overlapguard: no OnHitEnemy is refused", Refused("void:M", "local", "local", "void:TakeDamage", "void:OnHitAll", "local"));
        Check("overlapguard: no OnHitAll is refused", Refused("void:M", "local", "local", "void:TakeDamage", "void:OnHitEnemy", "local"));
        Check("overlapguard: the calls out of order are refused", Refused("void:M", "local", "local", "void:TakeDamage", "void:OnHitAll", "void:OnHitEnemy", "local"));
        Check("overlapguard: a target that isn't a local is refused", Refused("void:M", "other", "local", "void:TakeDamage", "void:OnHitEnemy", "void:OnHitAll", "local"));
        Check("overlapguard: damage info that isn't a local is refused", Refused("void:M", "local", "other", "void:TakeDamage", "void:OnHitEnemy", "void:OnHitAll", "local"));
        Check("overlapguard: a stretch that may start with a value on the stack is refused", Refused("value:M", "local", "local", "void:TakeDamage", "void:OnHitEnemy", "void:OnHitAll", "local"));
        Check("overlapguard: nothing before TakeDamage is refused", Refused("local", "local", "void:TakeDamage", "void:OnHitEnemy", "void:OnHitAll", "local"));
        Check("overlapguard: a jump in the middle is refused", Refused("void:M", "local", "local", "void:TakeDamage", "jump", "void:OnHitEnemy", "void:OnHitAll", "local"));
        Check("overlapguard: nothing after OnHitAll is refused", Refused("void:M", "local", "local", "void:TakeDamage", "void:OnHitEnemy", "void:OnHitAll"));

        // The wrapper's own refusals. Each returns before it needs a generator, and must leave the code as it was.
        var wrapRange = shieldType.Assembly.GetType("CompatShield.CatchWrap").GetMethod("WrapRange", all);
        System.Collections.Generic.List<CodeInstruction> Nops(int n) => Enumerable.Range(0, n).Select(_ => new CodeInstruction(System.Reflection.Emit.OpCodes.Nop)).ToList();
        string Wrap(System.Collections.Generic.List<CodeInstruction> c, int s, int e) => (string)wrapRange.Invoke(null, new object[] { c, s, e, null, typeof(Exception), new CodeInstruction[0], null });
        var plain = Nops(4);
        Check("overlapguard: wrapping a range that starts before the code is refused", Wrap(plain, -1, 1) != null && plain.Count == 4);
        Check("overlapguard: wrapping a range that ends before it starts is refused", Wrap(plain, 2, 1) != null && plain.Count == 4);
        Check("overlapguard: wrapping the last instruction, with nothing to carry on to, is refused", Wrap(plain, 1, 3) != null && plain.Count == 4);
        var labelled = Nops(4); labelled[1].labels.Add(new System.Reflection.Emit.Label());
        Check("overlapguard: a jump target inside the range is refused", Wrap(labelled, 0, 2) == "a jump into the guarded code" && labelled.Count == 4);
        var blocked = Nops(4); blocked[1].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        Check("overlapguard: exception handling inside the range is refused", Wrap(blocked, 0, 2) == "exception handling inside the guarded code" && blocked.Count == 4);
        var branching = Nops(4); branching[1] = new CodeInstruction(System.Reflection.Emit.OpCodes.Br, new System.Reflection.Emit.Label());
        Check("overlapguard: a branch inside the range is refused", Wrap(branching, 0, 2) == "a jump out of the guarded code" && branching.Count == 4);
        var returning = Nops(4); returning[1] = new CodeInstruction(System.Reflection.Emit.OpCodes.Ret);
        Check("overlapguard: a return inside the range is refused", Wrap(returning, 0, 2) == "a jump out of the guarded code" && returning.Count == 4);

        // Patching.
        var h = new Harmony("test.overlapguard");
        bool Patch(Type t) => (bool)guard.GetMethod("PatchGuard", all).Invoke(null, new object[] { h, t });
        bool Warned(string text) { bool w = logs.Count(l => l.Contains("not applied") && l.Contains(text)) == 1; logs.Clear(); return w; }
        Reset();
        Check("overlapguard: an instance PerformDamageServer is left alone and warned about", !Patch(typeof(FakeHitInstance)) && Warned("not found"));
        Check("overlapguard: a hit list of the wrong type is left alone and warned about", !Patch(typeof(FakeHitWrongList)) && Warned("not found"));
        Check("overlapguard: a type without the message class is left alone and warned about", !Patch(typeof(FakeHitNoMessage)) && Warned("not found"));
        Check("overlapguard: a missing OverlapAttack type is left alone and warned about", !Patch(null) && Warned("not found"));
        Check("overlapguard: a damage loop of another shape is refused and warned about", !Patch(typeof(FakeHitBranchy)) && Warned("a jump in the middle"));
        Exception refusedThrow = null;
        try { FakeHitBranchy.PerformDamageServer(new FakeHitBranchy.OverlapAttackMessage(), new System.Collections.Generic.List<FakeHitBranchy.OverlapInfo> { new FakeHitBranchy.OverlapInfo { health = H("x", HitThrowB) } }); } catch (Exception e) { refusedThrow = e; }
        Check("overlapguard: a refused method behaves exactly as it did", refusedThrow is NullReferenceException && Total() == 0);
        Check("overlapguard: patches the real shape and says so", Patch(typeof(FakeHit)) && logs.Count(l => l.Contains("Overlap guard: one target")) == 1);

        object commando = "CommandoBody(Clone)", engi = "EngiBody(Clone)", zone = "FireZone(Clone)", spikes = "SpikeZone(Clone)";
        bodies[commando] = new FakeHitBody { inventory = new object(), master = new object() };
        string[] Whole(string v) => new[] { "take " + v, "enemy " + v, "all " + v };

        // The behaviour the fix exists for.
        Reset();
        Exception escaped = null;
        try { Hit(commando, zone, H("a"), H("b", HitThrowA, new FakeHitBody { master = new object() }), H("c")); } catch (Exception e) { escaped = e; }
        Check("overlapguard: one target's exception does not reach the attack", escaped == null);
        Check("overlapguard: the target before it got its whole hit, the failed one stopped there, the one after got its whole hit", FakeHit.Ran.SequenceEqual(Whole("a").Concat(new[] { "take b" }).Concat(Whole("c"))));
        Check("overlapguard: one failed hit is counted once", Total() == 1 && Lines().Count == 1);
        var line = Lines()[0];
        Check("overlapguard: the line names the exception", line.Contains("NullReferenceException"));
        Check("overlapguard: the line describes the victim", line.Contains("Victim b: body ok, inventory null, master ok, shield 4/10, barrier 0, health 50/100"));
        Check("overlapguard: the line describes the attacker", line.Contains("Attacker CommandoBody(Clone): body ok, inventory ok, input bank null"));
        Check("overlapguard: the line names the inflictor", line.Contains("Inflictor FireZone(Clone)"));
        Check("overlapguard: the line gives the count", line.Contains("Failed hit 1 this session"));
        Check("overlapguard: the line gives the real throw site", line.Contains("Thrown at:") && line.Contains("HitThrowA"));

        Reset();
        FakeHitEvents.FailEnemy = v => { if (v.ToString() == "b") HitThrowB(); };
        escaped = null;
        try { Hit(commando, zone, H("a"), H("b"), H("c")); } catch (Exception e) { escaped = e; }
        Check("overlapguard: a throw from OnHitEnemy is caught, skips that target's OnHitAll, and the attack carries on", escaped == null && FakeHit.Ran.SequenceEqual(Whole("a").Concat(new[] { "take b", "enemy b" }).Concat(Whole("c"))) && Total() == 1);
        Reset();
        FakeHitEvents.FailAll = v => { if (v.ToString() == "a") HitThrowB(); };
        escaped = null;
        try { Hit(commando, zone, H("a"), H("c")); } catch (Exception e) { escaped = e; }
        Check("overlapguard: a throw from OnHitAll is caught and the attack carries on", escaped == null && FakeHit.Ran.SequenceEqual(Whole("a").Concat(Whole("c"))) && Total() == 1);

        Reset();
        Hit(commando, zone, H("a", HitThrowB), H("b", HitThrowB), H("c", HitThrowB));
        Check("overlapguard: every target failing is one failure each, and every target is tried", Total() == 3 && FakeHit.Ran.SequenceEqual(new[] { "take a", "take b", "take c" }));

        Reset();
        Hit(commando, zone, H("a"), H("b"));
        Check("overlapguard: an attack with no error logs nothing and counts nothing", Total() == 0 && logs.Count == 0 && FakeHit.Ran.Count == 6);
        Hit(commando, zone);
        Check("overlapguard: an empty hit list is fine", Total() == 0 && logs.Count == 0);

        // The zone loop itself.
        Reset();
        var z = new FakeHitZone();
        z.overlapList.Add(new FakeHit.OverlapInfo { health = H("a", HitThrowB) });
        z.overlapList.Add(new FakeHit.OverlapInfo { health = H("b") });
        z.Tick();
        z.Tick();
        Check("overlapguard: a zone clears its list and keeps its timing after a failed hit", z.overlapList.Count == 0 && z.fires == 1 && z.fireStopwatch == 0f);

        // Who gets a line.
        Reset();
        var bee = H("beetle", HitThrowA); var lem = H("lemurian", HitThrowA);
        Hit(commando, zone, bee);
        Hit(commando, zone, bee);
        Check("overlapguard: the same failure again is counted but gets no second line", Total() == 2 && Lines().Count == 1);
        Hit(engi, zone, bee);
        Check("overlapguard: a different attacker is a new line", Lines().Count == 2);
        Hit(commando, spikes, bee);
        Check("overlapguard: a different inflictor is a new line", Lines().Count == 3);
        Hit(commando, zone, lem);
        Check("overlapguard: a different victim is a new line", Lines().Count == 4);
        Hit(commando, zone, H("beetle", HitThrowB));
        Check("overlapguard: another throw site is a new line", Lines().Count == 5 && Lines()[4].Contains("HitThrowB"));
        Hit(commando, zone, H("beetle", () => HitThrowKind(false)));
        Hit(commando, zone, H("beetle", () => HitThrowKind(true)));
        Check("overlapguard: a different exception type from the same line of code is a new line", Lines().Count == 7 && Lines()[6].Contains("InvalidOperationException"));

        // What it says when things are missing.
        Reset();
        Hit(null, null, bee);
        Check("overlapguard: no attacker and no inflictor say so", Lines()[0].Contains("Attacker none: body unknown, inventory unknown, input bank unknown") && Lines()[0].Contains("Inflictor none"));
        Reset();
        dead.Add(engi); dead.Add(zone);
        Hit(engi, zone, bee);
        Check("overlapguard: a destroyed attacker and inflictor say so", Lines()[0].Contains("Attacker destroyed: body unknown") && Lines()[0].Contains("Inflictor destroyed"));
        Reset();
        Hit(engi, zone, bee);
        Check("overlapguard: an attacker with no body says so", Lines()[0].Contains("Attacker EngiBody(Clone): body null, inventory unknown"));
        Reset();
        var deadVictim = H("gone", HitThrowA); dead.Add(deadVictim);
        Hit(commando, zone, deadVictim);
        Check("overlapguard: a destroyed victim says so", Lines()[0].Contains("Victim destroyed: body unknown, inventory unknown, master unknown, shield unknown"));

        // The caps.
        Reset();
        for (int i = 0; i < maxKinds; i++) Hit(commando, zone, H("v" + i, HitThrowA));
        Check("overlapguard: every new kind logs up to the cap", Lines().Count == maxKinds && Total() == maxKinds);
        Check("overlapguard: the last line says only new throw sites get a line from here", Lines().Last().Contains("only a new throw site") && !Lines().First().Contains("only a new throw site"));
        Hit(commando, zone, H("v0", HitThrowB));
        Check("overlapguard: with the kinds full, a second bug at a new throw site still gets a line", Lines().Count == maxKinds + 1 && Lines().Last().Contains("HitThrowB"));
        Hit(commando, zone, H("vNew", HitThrowB));
        Check("overlapguard: with the kinds full, the same bug on another victim gets no line", Lines().Count == maxKinds + 1);
        var sites = (System.Collections.Generic.HashSet<string>)guard.GetField("sites", all).GetValue(null);
        for (int i = 0; sites.Count < maxSites - 1; i++) sites.Add("filler" + i);
        Hit(commando, zone, H("v0", () => HitThrowKind(false)));
        Check("overlapguard: the last throw site line says logging has stopped", Lines().Count == maxKinds + 2 && Lines().Last().Contains("the last new one logged"));
        int beforeLast = Total();
        Hit(commando, zone, H("v0", () => HitThrowKind(true)));
        Check("overlapguard: past both caps nothing more is logged and the count carries on", Lines().Count == maxKinds + 2 && Total() == beforeLast + 1);

        // The milestones.
        Reset();
        for (int i = 0; i < 100; i++) Hit(commando, zone, bee);
        Check("overlapguard: the running total shows at 10 and at 100, once each", logs.Count(l => l.Contains("10 failed hits caught so far")) == 1 && logs.Count(l => l.Contains("100 failed hits caught so far")) == 1 && logs.Count(l => l.Contains("failed hits caught so far")) == 2);

        // A failure inside the guard must not become a new failure.
        Reset();
        guard.GetField("Alive", all).SetValue(null, new Func<object, bool>(o => { throw new InvalidOperationException("broken alive"); }));
        escaped = null;
        try { Hit(commando, zone, H("a", HitThrowB), H("b", HitThrowB), H("c")); } catch (Exception e) { escaped = e; }
        guard.GetField("Alive", all).SetValue(null, aliveNormal);
        Check("overlapguard: a guard that can't describe the hit still stops the error and carries on", escaped == null && FakeHit.Ran.Last() == "all c" && Total() == 2);
        Check("overlapguard: that is said once", logs.Count(l => l.Contains("couldn't describe a failed hit")) == 1);

        // The run-start path.
        var onStart = guard.GetMethod("OnRunStart", all);
        guard.GetField("patcher", all).SetValue(null, new Harmony("test.overlapguard2"));
        guard.GetField("game", all).SetValue(null, typeof(FakeHit).Assembly);
        Reset();
        onStart.Invoke(null, new object[] { null });
        Check("overlapguard: a game with no OverlapAttack is left alone and warned about", logs.Count(l => l.Contains("not applied")) == 1);
        logs.Clear();
        onStart.Invoke(null, new object[] { null });
        Check("overlapguard: the run-start handler only works once", logs.Count == 0);
        guard.GetField("game", all).SetValue(null, null);
        Reset();
        Exception startError = null;
        try { onStart.Invoke(null, new object[] { null }); } catch (Exception e) { startError = e; }
        Check("overlapguard: an error in the run-start handler is caught and warned about", startError == null && logs.Count(l => l.Contains("can't patch OverlapAttack, not applied")) == 1);
        Reset();
        startError = null;
        try { guard.GetMethod("Apply", all).Invoke(null, new object[] { h, typeof(FakeHit).Assembly }); } catch (Exception e) { startError = e; }
        Check("overlapguard: a game with no run-start event is left alone and warned about", startError == null && logs.Count(l => l.Contains("can't watch run starts, not applied")) == 1);

        // The load path may only subscribe; patching there can run the game type's static constructor.
        var shieldDll = Mono.Cecil.ModuleDefinition.ReadModule(shieldType.Assembly.Location);
        var guardDef = shieldDll.GetType("CompatShield.OverlapHitGuard");
        bool Calls(Mono.Cecil.MethodDefinition m, string name) => m.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == name);
        var applyDef = guardDef.Methods.First(m => m.Name == "Apply"); var startDef = guardDef.Methods.First(m => m.Name == "OnRunStart");
        Check("overlapguard: Apply (on the load path) subscribes to run starts and never patches", Calls(applyDef, "AddEventHandler") && !Calls(applyDef, "Patch") && !Calls(applyDef, "PatchGuard"));
        Check("overlapguard: the patch happens from the run-start handler", Calls(startDef, "PatchGuard"));

        // The real game: the shape check runs over the shipped method, because this harness can't JIT it.
        var managed = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var ror2 = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var oa = ror2.GetType("RoR2.OverlapAttack");
        var msg = oa.NestedTypes.FirstOrDefault(n => n.Name == "OverlapAttackMessage");
        var oi = oa.NestedTypes.FirstOrDefault(n => n.Name == "OverlapInfo");
        var pds = oa.Methods.FirstOrDefault(m => m.Name == "PerformDamageServer" && m.Parameters.Count == 2);
        Check("overlapguard real: game 1.5.0 has static void PerformDamageServer(OverlapAttackMessage, List<OverlapInfo>)", msg != null && oi != null && pds != null && pds.IsStatic && pds.ReturnType.FullName == "System.Void" && pds.Parameters[0].ParameterType.FullName == msg.FullName && pds.Parameters[1].ParameterType.FullName.Contains("List`1") && pds.Parameters[1].ParameterType.FullName.Contains("OverlapInfo"));
        Check("overlapguard real: the message has attacker and inflictor", new[] { "attacker", "inflictor" }.All(n => msg.Fields.Any(f => f.Name == n)));
        var real = pds.Body.Instructions.ToList();
        var steps = real.Select(i => { var r = i.Operand as Mono.Cecil.MethodReference; return Step(i.OpCode.Name, i.OpCode.FlowControl.ToString(), r?.Name, r != null && r.ReturnType.FullName == "System.Void"); }).ToArray();
        var found = Find(steps);
        Check("overlapguard real: the shape check accepts the real damage loop", found.Item1 == null);
        if (found.Item1 == null) {
            var guarded = new System.Collections.Generic.HashSet<Mono.Cecil.Cil.Instruction>(real.Skip(found.Item2).Take(found.Item3 - found.Item2 + 1));
            Check("overlapguard real: the guarded stretch opens by loading the target", real[found.Item2].OpCode.Name == "ldloc.2" && real[found.Item2 + 1].OpCode.Name == "ldloc.3");
            Check("overlapguard real: the guarded stretch ends on OnHitAll", (real[found.Item3].Operand as Mono.Cecil.MethodReference)?.Name == "OnHitAll");
            Check("overlapguard real: the code after it is the loop counter", real[found.Item3 + 1].OpCode.Name == "ldloc.0" && real[found.Item3 + 2].OpCode.Name == "ldc.i4.1");
            Check("overlapguard real: nothing jumps into the guarded stretch", !real.Any(i => i.Operand is Mono.Cecil.Cil.Instruction t && guarded.Contains(t)));
            Check("overlapguard real: the local it hands the catch is a HealthComponent", pds.Body.Variables[2].VariableType.FullName == "RoR2.HealthComponent");
        }
        Check("overlapguard real: the method has no exception handling of its own", !pds.Body.HasExceptionHandlers);
        var fire = oa.Methods.First(m => m.Name == "Fire");
        var fireCode = fire.Body.Instructions.ToList();
        int processAt = fireCode.FindIndex(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "ProcessHits");
        int clearAt = fireCode.FindLastIndex(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "Clear");
        Check("overlapguard real: Fire clears its list only after dealing the hits, with no catch between", processAt >= 0 && clearAt > processAt && !fire.Body.HasExceptionHandlers);
        var zoneType = ror2.GetType("RoR2.Projectile.ProjectileDotZone");
        var zoneCode = zoneType.Methods.First(m => m.Name == "FixedUpdate").Body.Instructions.ToList();
        int fireAt = zoneCode.FindIndex(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "Fire");
        int stopwatchAt = fireAt < 0 ? -1 : zoneCode.FindIndex(fireAt + 1, i => i.OpCode.Name == "stfld" && i.Operand is Mono.Cecil.FieldReference f && f.Name == "fireStopwatch");
        int destroyAt = fireAt < 0 ? -1 : zoneCode.FindIndex(fireAt + 1, i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "Destroy");
        Check("overlapguard real: a zone takes the interval off its stopwatch only after Fire returns", fireAt >= 0 && stopwatchAt > fireAt);
        Check("overlapguard real: a zone can expire only after Fire returns", destroyAt > fireAt && fireAt >= 0);
        bool Has(Mono.Cecil.TypeDefinition t, string n) => t.Fields.Any(f => f.Name == n) || t.Properties.Any(p => p.Name == n);
        var health = ror2.GetType("RoR2.HealthComponent"); var body = ror2.GetType("RoR2.CharacterBody");
        Check("overlapguard real: HealthComponent has body, shield, fullShield, barrier, health and fullHealth", new[] { "body", "shield", "fullShield", "barrier", "health", "fullHealth" }.All(n => Has(health, n)));
        Check("overlapguard real: CharacterBody has inventory, master and inputBank", new[] { "inventory", "master", "inputBank" }.All(n => Has(body, n)));
        Check("overlapguard real: Run has onRunStartGlobal", ror2.GetType("RoR2.Run").Events.Any(e => e.Name == "onRunStartGlobal"));
        return failures == 0 ? 0 : 1;
    }

    // Stand-ins for DotController and BulletAttack. Everything patched or called is NoInlining, so Harmony and the shape check see the real calls.
    public class FakeDotDamage { public object attacker, inflictor; public float amount; }
    public class FakeDotHealth {
        public string label; public object body; public float shield = 4f, barrier, health = 50f, fullShield = 10f, fullHealth = 100f;
        public Action<object> fail;
        public override string ToString() { return label; }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void TakeDamage(FakeDotDamage d) { FakeDot.Ran.Add("tick " + d.attacker); if (fail != null) fail(d.attacker); }
    }
    public class FakeDot {
        public enum DotIndex { None = -1, Bleed = 0, Burn = 1 }
        public static System.Collections.Generic.List<string> Ran = new System.Collections.Generic.List<string>();
        public static int Rented, Returned;
        public FakeDotHealth target; public object inflictor; public object[] attackers = new object[0];
        public float timer = 1f; public int evaluations, remaining;
        public FakeDotHealth victimHealthComponent { [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] get { return target; } }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void EvaluateDotStacksForType(DotIndex dotIndex, float dt, out int remainingActive) {
            remainingActive = 0;
            evaluations++;
            Rented++;
            FakeDotDamage damageInfo = new FakeDotDamage();
            for (int i = 0; i < attackers.Length; i++) {
                damageInfo.amount = dt;
                damageInfo.inflictor = inflictor;
                damageInfo.attacker = attackers[i];
                victimHealthComponent.TakeDamage(damageInfo);
                remainingActive++;
            }
            Returned++;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Evaluate(DotIndex index) { int n; EvaluateDotStacksForType(index, 1f, out n); remaining = n; }
        // The shape of FixedUpdate: the DoT's timer is stored only after the evaluation returns.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void FixedTick(DotIndex index) { float next = timer - 1f; if (next <= 0f) { next += 3f; Evaluate(index); } timer = next; }
    }
    // What R2API.Dot leaves behind: a jump past the damage call for DoTs with their own damage code.
    public class FakeDotR2 {
        public enum DotIndex { None = -1, Bleed = 0, Burn = 1 }
        public FakeDotHealth target; public object[] attackers = new object[0];
        public FakeDotHealth victimHealthComponent { [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] get { return target; } }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static bool IsVanilla(DotIndex index) { return index == DotIndex.Bleed; }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void EvaluateDotStacksForType(DotIndex dotIndex, float dt, out int remainingActive) {
            remainingActive = 0;
            FakeDotDamage damageInfo = new FakeDotDamage();
            for (int i = 0; i < attackers.Length; i++) {
                damageInfo.attacker = attackers[i];
                if (IsVanilla(dotIndex)) victimHealthComponent.TakeDamage(damageInfo);
                remainingActive++;
            }
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public int Evaluate(DotIndex index) { int n; EvaluateDotStacksForType(index, 1f, out n); return n; }
    }
    public class FakeDotNoEnum { private void EvaluateDotStacksForType(int index, float dt, out int r) { r = 0; } }
    public class FakeDotStatic { public enum DotIndex { A } private static void EvaluateDotStacksForType(DotIndex index, float dt, out int r) { r = 0; } }
    public class FakeDotWrongReturn { public enum DotIndex { A } private int EvaluateDotStacksForType(DotIndex index, float dt, out int r) { r = 0; return 0; } }
    public class FakeDotTwice {
        public enum DotIndex { A }
        public FakeDotHealth target;
        public FakeDotHealth victimHealthComponent { [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] get { return target; } }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void EvaluateDotStacksForType(DotIndex index, float dt, out int r) {
            r = 0;
            FakeDotDamage damageInfo = new FakeDotDamage();
            damageInfo.amount = dt;
            victimHealthComponent.TakeDamage(damageInfo);
            damageInfo.amount = dt;
            victimHealthComponent.TakeDamage(damageInfo);
            r = 1;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Evaluate() { int n; EvaluateDotStacksForType(DotIndex.A, 1f, out n); }
    }

    public class FakeShotDamage { public object attacker, inflictor; }
    public class FakeShotHealth {
        public string label; public object body; public float shield = 4f, barrier, health = 50f, fullShield = 10f, fullHealth = 100f;
        public Action fail;
        public override string ToString() { return label; }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void TakeDamage(FakeShotDamage d) { FakeBullet.Ran.Add("take " + label); if (fail != null) fail(); }
    }
    public class FakeShotEvents {
        public static FakeShotEvents instance = new FakeShotEvents();
        public static Action FailEnemy, FailAll;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void OnHitEnemy(FakeShotDamage d, object victim) { FakeBullet.Ran.Add("enemy " + victim); if (FailEnemy != null) FailEnemy(); }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void OnHitAll(FakeShotDamage d, object victim) { FakeBullet.Ran.Add("all " + victim); if (FailAll != null) FailAll(); }
    }
    public class FakeBullet {
        public static System.Collections.Generic.List<string> Ran = new System.Collections.Generic.List<string>();
        public static bool Server = true;
        public class BulletHit { public object entityObject; public FakeShotHealth health; public bool canDamage = true; }
        public object owner, weapon;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool DefaultHitCallbackImplementation(FakeBullet bulletAttack, ref BulletHit hitInfo) {
            FakeShotHealth healthComponent = hitInfo.health;
            bool flag = healthComponent != null && hitInfo.canDamage;
            FakeShotDamage damageInfo = new FakeShotDamage();
            damageInfo.attacker = bulletAttack.owner;
            damageInfo.inflictor = bulletAttack.weapon;
            if (Server) {
                if (flag) {
                    healthComponent.TakeDamage(damageInfo);
                    FakeShotEvents.instance.OnHitEnemy(damageInfo, hitInfo.entityObject);
                }
                FakeShotEvents.instance.OnHitAll(damageInfo, hitInfo.entityObject);
            }
            return flag;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static bool Fire(FakeBullet bullet, BulletHit hit) { return DefaultHitCallbackImplementation(bullet, ref hit); }
    }
    public class FakeBulletBranchy {
        public class BulletHit { public object entityObject; public FakeShotHealth health; }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool DefaultHitCallbackImplementation(FakeBulletBranchy bulletAttack, ref BulletHit hitInfo) {
            FakeShotHealth healthComponent = hitInfo.health;
            FakeShotDamage damageInfo = new FakeShotDamage();
            if (healthComponent != null) {
                healthComponent.TakeDamage(damageInfo);
                if (damageInfo.attacker != null) FakeShotEvents.instance.OnHitEnemy(damageInfo, hitInfo.entityObject);
                FakeShotEvents.instance.OnHitAll(damageInfo, hitInfo.entityObject);
            }
            return true;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static bool Fire(FakeBulletBranchy bullet, BulletHit hit) { return DefaultHitCallbackImplementation(bullet, ref hit); }
    }
    public class FakeBulletNoHit { private static bool DefaultHitCallbackImplementation(FakeBulletNoHit b, object hit) { return true; } }
    public class FakeBulletInstance { public class BulletHit { } private bool DefaultHitCallbackImplementation(FakeBulletInstance b, ref BulletHit hit) { return true; } }
    public class FakeBulletWrongReturn { public class BulletHit { } private static void DefaultHitCallbackImplementation(FakeBulletWrongReturn b, ref BulletHit hit) { } }
    // The shape of Tinker's Satchel's Motion Tracker: the timer is stored only after the shot.
    public class FakeShotTimer {
        public FakeBullet gun = new FakeBullet(); public FakeBullet.BulletHit hit; public float timer = 1f; public int fires;
        public void Tick() { if (timer >= 1f) { fires++; FakeBullet.Fire(gun, hit); timer -= 1f; } }
    }

    public static class FakeRoulettePool { public static int SharedMoneyValue; public static bool On = true; public static bool SharedMoneyEnabled() { return On; } }
    public static class FakeRoulettePoolLong { public static long SharedMoneyValue; public static bool SharedMoneyEnabled() { return true; } }
    public static class FakeRoulettePoolNoSwitch { public static int SharedMoneyValue; }
    public static class FakeRouletteGeneral { public static bool Multi = true; public static bool IsMultiplayer() { return Multi; } }
    public static class FakeRouletteGeneralEmpty { }
    public class FakeRouletteMaster { public uint money { get; set; } }
    public class FakeRouletteMasterIntMoney { public int money { get; set; } }
    public static class FakeRouletteSpin {
        public static uint Price; public static bool ChargesPoolItself;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static object ExecuteSpinOnServer(FakeRouletteMaster master, object msg) {
            if (master == null || master.money < Price) return null;
            master.money -= Price;
            if (ChargesPoolItself) FakeRoulettePool.SharedMoneyValue -= (int)Price;
            return null;
        }
    }
    public static class FakeRouletteSpinWrongFirst { private static object ExecuteSpinOnServer(string who, object msg) { return null; } }
    public static class FakeRouletteSpinRenamed { private static object SpinOnServer(FakeRouletteMaster master, object msg) { return null; } }
    public class FakeAcoBody { public float baseAttackSpeed = 1f, levelAttackSpeed; }
    public class FakeAcoBodyNoLevel { public float baseAttackSpeed = 1f; }
    public class FakeAcoSwap { private float baseAttackSpeed = 1.2f; public float Value { get { return baseAttackSpeed; } } }
    public class FakeAcoSwapWrongType { private int baseAttackSpeed = 2; public int Value { get { return baseAttackSpeed; } } }
    public class FakeAcoPrefab { public object body, swap; }
    public class FakeAcoCatalog {
        public static object Found; public static string Asked;
        public static object FindBodyPrefab(string name) { Asked = name; return Found; }
        public static object FindBodyPrefab(int index) { Asked = "by index"; return null; }
    }
    public class FakeAcoNoFind { }

    static Mono.Cecil.Cil.VariableDefinition LocalLoadedBy(Mono.Cecil.MethodDefinition method, Mono.Cecil.Cil.Instruction load) {
        switch (load.OpCode.Name) {
            case "ldloc.0": return method.Body.Variables[0];
            case "ldloc.1": return method.Body.Variables[1];
            case "ldloc.2": return method.Body.Variables[2];
            case "ldloc.3": return method.Body.Variables[3];
            default: return load.Operand as Mono.Cecil.Cil.VariableDefinition;
        }
    }

    static int DotGuardCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var guard = shieldType.Assembly.GetType("CompatShield.DotTickGuard");
        var shape = shieldType.Assembly.GetType("CompatShield.HitShape");
        var logType = shieldType.Assembly.GetType("CompatShield.FailedHitLog");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var dead = new System.Collections.Generic.HashSet<object>();
        var bodies = new System.Collections.Generic.Dictionary<object, object>();
        Func<object, bool> aliveNormal = o => o != null && !dead.Contains(o);
        logType.GetField("Alive", all).SetValue(null, aliveNormal);
        logType.GetField("NameOf", all).SetValue(null, new Func<object, string>(o => o.ToString()));
        logType.GetField("BodyOf", all).SetValue(null, new Func<object, Type, object>((a, t) => bodies.TryGetValue(a, out var b) ? b : null));
        int maxKinds = (int)logType.GetField("MaxKinds", all).GetRawConstantValue();
        int maxSites = (int)logType.GetField("MaxSites", all).GetRawConstantValue();
        var failLog = guard.GetField("Failures", all).GetValue(null);
        int Total() => (int)guard.GetProperty("Total", all).GetValue(null);
        void Reset() { guard.GetMethod("ResetForTest", all).Invoke(null, null); logs.Clear(); dead.Clear(); FakeDot.Ran.Clear(); FakeDot.Rented = FakeDot.Returned = 0; }
        System.Collections.Generic.List<string> Lines() => logs.Where(l => l.Contains("a hit failed")).ToList();
        FakeDotHealth T(string label, object targetBody = null, Action<object> fail = null) => new FakeDotHealth { label = label, body = targetBody, fail = fail };
        FakeDot Dot(FakeDotHealth target, object inflictor, params object[] attackers) => new FakeDot { target = target, inflictor = inflictor, attackers = attackers };
        Action<object> FailOn(object who, Action thrower) => a => { if (Equals(a, who)) thrower(); };
        void Tick(object attacker, object inflictor, FakeDotHealth target, FakeDot.DotIndex dot = FakeDot.DotIndex.Bleed) { new FakeDot { target = target, inflictor = inflictor, attackers = new[] { attacker } }.Evaluate(dot); }

        // The words the shape checks read, and the two small helpers on them.
        var stepOf = shape.GetMethod("StepOf", all, null, new[] { typeof(string), typeof(string), typeof(string), typeof(bool) }, null);
        string Step(string op, string flow, string called, bool isVoid) => (string)stepOf.Invoke(null, new object[] { op, flow, called, isVoid });
        Check("dotguard: this is the code the shape checks read", stepOf != null);
        Check("dotguard: loading the first argument is its own step, and only the first", Step("ldarg.0", "Next", null, false) == "arg0" && Step("ldarg.1", "Next", null, false) == "other");
        Check("dotguard: setting a field is a store step, setting a static field is not", Step("stfld", "Next", null, false) == "store" && Step("stsfld", "Next", null, false) == "other");
        Check("dotguard: a nop is a nop step", Step("nop", "Next", null, false) == "nop");
        Check("dotguard: a local load is a local step, in every encoding", Step("ldloc.2", "Next", null, false) == "local" && Step("ldloc.s", "Next", null, false) == "local" && Step("ldloc", "Next", null, false) == "local");
        Check("dotguard: loading a local's address is not a local step", Step("ldloca.s", "Next", null, false) == "other");
        Check("dotguard: branches, returns and throws are jumps", Step("brtrue.s", "Cond_Branch", null, false) == "jump" && Step("br", "Branch", null, false) == "jump" && Step("ret", "Return", null, false) == "jump" && Step("throw", "Throw", null, false) == "jump");
        Check("dotguard: a call says its name and whether it leaves a value", Step("callvirt", "Call", "TakeDamage", true) == "void:TakeDamage" && Step("call", "Call", "get_x", false) == "value:get_x");
        Check("dotguard: a constructor or a function pointer is not a call step", Step("newobj", "Call", ".ctor", true) == "other" && Step("ldftn", "Next", "M", true) == "other");
        int Only(string name, params string[] s) => (int)shape.GetMethod("Only", all).Invoke(null, new object[] { s.ToList(), name });
        int Prev(int index, params string[] s) => (int)shape.GetMethod("Previous", all).Invoke(null, new object[] { s.ToList(), index });
        Check("dotguard: Only finds a step that appears once", Only("x", "a", "x", "b") == 1);
        Check("dotguard: Only refuses a missing or repeated step", Only("x", "a", "b") == -1 && Only("x", "x", "a", "x") == -1);
        Check("dotguard: Previous steps back over nops", Prev(2, "a", "nop", "nop") == 0 && Prev(1, "a", "b") == 1);
        Check("dotguard: Previous says when there is nothing before", Prev(1, "nop", "nop") == -1 && Prev(-1, "a") == -1);

        // The shape check, on its own.
        var find = guard.GetMethod("FindRange", all);
        Tuple<string, int, int> Find(params string[] s) { var a = new object[] { s.ToList(), -1, -1 }; var w = (string)find.Invoke(null, a); return Tuple.Create(w, (int)a[1], (int)a[2]); }
        bool Refused(params string[] s) { var r = Find(s); return r.Item1 != null && r.Item2 == -1 && r.Item3 == -1; }
        const string Get = "value:get_victimHealthComponent";
        var vanilla = Find("store", "arg0", Get, "local", "void:TakeDamage", "other");
        Check("dotguard: the game's own shape is accepted, from this through TakeDamage", vanilla.Item1 == null && vanilla.Item2 == 1 && vanilla.Item3 == 4);
        var r2Shape = Find("jump", "arg0", Get, "local", "void:TakeDamage", "other");
        Check("dotguard: the shape R2API.Dot leaves is accepted", r2Shape.Item1 == null && r2Shape.Item2 == 1 && r2Shape.Item3 == 4);
        var nops = Find("store", "nop", "nop", "arg0", Get, "local", "void:TakeDamage", "nop", "other");
        Check("dotguard: nops from a debug build don't matter", nops.Item1 == null && nops.Item2 == 3 && nops.Item3 == 6);
        Check("dotguard: no TakeDamage is refused", Refused("store", "arg0", Get, "local", "other"));
        Check("dotguard: a TakeDamage that returns a value is refused", Refused("store", "arg0", Get, "local", "value:TakeDamage", "other"));
        Check("dotguard: two TakeDamage calls are refused", Refused("store", "arg0", Get, "local", "void:TakeDamage", "store", "arg0", Get, "local", "void:TakeDamage", "other"));
        Check("dotguard: a victim that isn't this controller's is refused", Refused("store", "other", Get, "local", "void:TakeDamage", "other"));
        Check("dotguard: another getter is refused", Refused("store", "arg0", "value:get_other", "local", "void:TakeDamage", "other"));
        Check("dotguard: damage info that isn't a local is refused", Refused("store", "arg0", Get, "other", "void:TakeDamage", "other"));
        Check("dotguard: a stretch that may start with a value on the stack is refused", Refused("value:M", "arg0", Get, "local", "void:TakeDamage", "other"));
        Check("dotguard: nothing before the stretch is refused", Refused("arg0", Get, "local", "void:TakeDamage", "other"));
        Check("dotguard: nothing after TakeDamage is refused", Refused("store", "arg0", Get, "local", "void:TakeDamage"));

        // Patching.
        var h = new Harmony("test.dotguard");
        bool Patch(Type t) => (bool)guard.GetMethod("PatchGuard", all).Invoke(null, new object[] { h, t });
        bool Warned(string text) { bool w = logs.Count(l => l.Contains("not applied") && l.Contains(text)) == 1; logs.Clear(); return w; }
        Reset();
        Check("dotguard: a controller with no DotIndex is left alone and warned about", !Patch(typeof(FakeDotNoEnum)) && Warned("not found"));
        Check("dotguard: a static evaluate is left alone and warned about", !Patch(typeof(FakeDotStatic)) && Warned("not found"));
        Check("dotguard: an evaluate that returns a value is left alone and warned about", !Patch(typeof(FakeDotWrongReturn)) && Warned("not found"));
        Check("dotguard: a missing DotController is left alone and warned about", !Patch(null) && Warned("not found"));
        Check("dotguard: code of another shape is refused and warned about", !Patch(typeof(FakeDotTwice)) && Warned("the damage call isn't the expected shape"));
        Exception refusedThrow = null;
        try { new FakeDotTwice { target = T("x", null, _ => HitThrowB()) }.Evaluate(); } catch (Exception e) { refusedThrow = e; }
        Check("dotguard: a refused method behaves exactly as it did", refusedThrow is NullReferenceException && Total() == 0);
        Check("dotguard: patches the game's shape and says so", Patch(typeof(FakeDot)) && logs.Count(l => l.Contains("DoT guard: one attacker's error")) == 1);

        object commando = "CommandoBody(Clone)", engi = "EngiBody(Clone)", zone = "FireZone(Clone)", spikes = "SpikeZone(Clone)";
        bodies[commando] = new FakeHitBody { inventory = new object(), master = new object() };

        // The behaviour the fix exists for.
        Reset();
        var hurt = Dot(T("beetle", new FakeHitBody { master = new object() }, FailOn(commando, HitThrowA)), zone, engi, commando, spikes);
        Exception escaped = null;
        try { hurt.Evaluate(FakeDot.DotIndex.Bleed); } catch (Exception e) { escaped = e; }
        Check("dotguard: one attacker's exception does not end the tick", escaped == null);
        Check("dotguard: the attackers before and after it still deal their damage", FakeDot.Ran.SequenceEqual(new[] { "tick EngiBody(Clone)", "tick CommandoBody(Clone)", "tick SpikeZone(Clone)" }));
        Check("dotguard: the rest of the method still runs, so its rented lists go back", FakeDot.Rented == 1 && FakeDot.Returned == 1);
        Check("dotguard: the loop carries on counting", hurt.remaining == 3);
        Check("dotguard: one failed tick is counted once", Total() == 1 && Lines().Count == 1);
        var line = Lines()[0];
        Check("dotguard: the line says what carried on", line.Contains("DoT guard: a hit failed and the tick carried on"));
        Check("dotguard: the line names the exception", line.Contains("NullReferenceException"));
        Check("dotguard: the line describes the victim", line.Contains("Victim beetle: body ok, inventory null, master ok, shield 4/10, barrier 0, health 50/100"));
        Check("dotguard: the line describes the attacker", line.Contains("Attacker CommandoBody(Clone): body ok, inventory ok, input bank null"));
        Check("dotguard: the line names the inflictor", line.Contains("Inflictor FireZone(Clone)"));
        Check("dotguard: the line names the DoT", line.Contains("DoT Bleed."));
        Check("dotguard: the line gives the count", line.Contains("Failed hit 1 this session"));
        Check("dotguard: the line gives the real throw site", line.Contains("Thrown at:") && line.Contains("HitThrowA"));

        Reset();
        Dot(T("beetle", null, _ => HitThrowB()), zone, commando).Evaluate((FakeDot.DotIndex)7);
        Check("dotguard: a DoT added by a mod shows its number", Lines().Count == 1 && Lines()[0].Contains("DoT 7."));

        Reset();
        var repeat = Dot(T("beetle", null, _ => HitThrowB()), zone, commando);
        for (int i = 0; i < 9; i++) repeat.FixedTick(FakeDot.DotIndex.Burn);
        Check("dotguard: a failing tick runs once per interval, not every physics tick", repeat.evaluations == 3 && Total() == 3);

        Reset();
        Dot(T("beetle", null, _ => HitThrowB()), zone, commando, engi, spikes).Evaluate(FakeDot.DotIndex.Burn);
        Check("dotguard: every attacker failing is one failure each, and every attacker is tried", Total() == 3 && FakeDot.Ran.Count == 3);

        Reset();
        Dot(T("beetle"), zone, commando, engi).Evaluate(FakeDot.DotIndex.Burn);
        Check("dotguard: a tick with no error logs nothing and counts nothing", Total() == 0 && logs.Count == 0 && FakeDot.Ran.Count == 2 && FakeDot.Returned == 1);
        Dot(T("beetle"), zone).Evaluate(FakeDot.DotIndex.Burn);
        Check("dotguard: a tick with no attackers is fine", Total() == 0 && logs.Count == 0 && FakeDot.Returned == 2);

        // Who gets a line.
        Reset();
        var bee = T("beetle", null, _ => HitThrowA()); var lem = T("lemurian", null, _ => HitThrowA());
        Tick(commando, zone, bee);
        Tick(commando, zone, bee);
        Check("dotguard: the same failure again is counted but gets no second line", Total() == 2 && Lines().Count == 1);
        Tick(commando, zone, bee, FakeDot.DotIndex.Burn);
        Check("dotguard: another DoT with everything else the same gets no second line", Total() == 3 && Lines().Count == 1);
        Tick(engi, zone, bee);
        Check("dotguard: a different attacker is a new line", Lines().Count == 2);
        Tick(commando, spikes, bee);
        Check("dotguard: a different inflictor is a new line", Lines().Count == 3);
        Tick(commando, zone, lem);
        Check("dotguard: a different victim is a new line", Lines().Count == 4);
        Tick(commando, zone, T("beetle", null, _ => HitThrowB()));
        Check("dotguard: another throw site is a new line", Lines().Count == 5 && Lines()[4].Contains("HitThrowB"));
        Tick(commando, zone, T("beetle", null, _ => HitThrowKind(false)));
        Tick(commando, zone, T("beetle", null, _ => HitThrowKind(true)));
        Check("dotguard: a different exception type from the same line of code is a new line", Lines().Count == 7 && Lines()[6].Contains("InvalidOperationException"));

        // What it says when things are missing.
        Reset();
        Tick(null, null, bee);
        Check("dotguard: no attacker and no inflictor say so", Lines()[0].Contains("Attacker none: body unknown, inventory unknown, input bank unknown") && Lines()[0].Contains("Inflictor none"));
        Reset();
        dead.Add(engi); dead.Add(zone);
        Tick(engi, zone, bee);
        Check("dotguard: a destroyed attacker and inflictor say so", Lines()[0].Contains("Attacker destroyed: body unknown") && Lines()[0].Contains("Inflictor destroyed"));
        Reset();
        Tick(engi, zone, bee);
        Check("dotguard: an attacker with no body says so", Lines()[0].Contains("Attacker EngiBody(Clone): body null, inventory unknown"));
        Reset();
        var deadVictim = T("gone", null, _ => HitThrowA()); dead.Add(deadVictim);
        Tick(commando, zone, deadVictim);
        Check("dotguard: a destroyed victim says so", Lines()[0].Contains("Victim destroyed: body unknown, inventory unknown, master unknown, shield unknown"));

        // The caps.
        Reset();
        for (int i = 0; i < maxKinds; i++) Tick(commando, zone, T("v" + i, null, _ => HitThrowA()));
        Check("dotguard: every new kind logs up to the cap", Lines().Count == maxKinds && Total() == maxKinds);
        Check("dotguard: the last line says only new throw sites get a line from here", Lines().Last().Contains("only a new throw site") && !Lines().First().Contains("only a new throw site"));
        Tick(commando, zone, T("v0", null, _ => HitThrowB()));
        Check("dotguard: with the kinds full, a second bug at a new throw site still gets a line", Lines().Count == maxKinds + 1 && Lines().Last().Contains("HitThrowB"));
        Tick(commando, zone, T("vNew", null, _ => HitThrowB()));
        Check("dotguard: with the kinds full, the same bug on another victim gets no line", Lines().Count == maxKinds + 1);
        var sites = (System.Collections.Generic.HashSet<string>)logType.GetField("sites", all).GetValue(failLog);
        for (int i = 0; sites.Count < maxSites - 1; i++) sites.Add("filler" + i);
        Tick(commando, zone, T("v0", null, _ => HitThrowKind(false)));
        Check("dotguard: the last throw site line says logging has stopped", Lines().Count == maxKinds + 2 && Lines().Last().Contains("the last new one logged"));
        int beforeLast = Total();
        Tick(commando, zone, T("v0", null, _ => HitThrowKind(true)));
        Check("dotguard: past both caps nothing more is logged and the count carries on", Lines().Count == maxKinds + 2 && Total() == beforeLast + 1);

        // The milestones.
        Reset();
        for (int i = 0; i < 100; i++) Tick(commando, zone, bee);
        Check("dotguard: the running total shows at 10 and at 100, once each", logs.Count(l => l.Contains("10 failed hits caught so far")) == 1 && logs.Count(l => l.Contains("100 failed hits caught so far")) == 1 && logs.Count(l => l.Contains("failed hits caught so far")) == 2);

        // A failure inside the guard must not become a new failure.
        Reset();
        logType.GetField("Alive", all).SetValue(null, new Func<object, bool>(o => { throw new InvalidOperationException("broken alive"); }));
        escaped = null;
        try { Dot(T("beetle", null, _ => HitThrowB()), zone, commando, engi, spikes).Evaluate(FakeDot.DotIndex.Bleed); } catch (Exception e) { escaped = e; }
        logType.GetField("Alive", all).SetValue(null, aliveNormal);
        Check("dotguard: a guard that can't describe the hit still stops the error and carries on", escaped == null && FakeDot.Ran.Count == 3 && Total() == 3);
        Check("dotguard: that is said once", logs.Count(l => l.Contains("couldn't describe a failed hit")) == 1);

        // The shape R2API.Dot leaves, end to end. Last, because patching it replaces the enum the line reads names from.
        Reset();
        Check("dotguard: patches the shape R2API.Dot leaves", Patch(typeof(FakeDotR2)));
        escaped = null;
        int left = -1;
        try { left = new FakeDotR2 { target = T("beetle", null, FailOn(commando, HitThrowB)), attackers = new[] { commando, engi } }.Evaluate(FakeDotR2.DotIndex.Bleed); } catch (Exception e) { escaped = e; }
        Check("dotguard: with R2API's jump the error is still caught and the loop carries on", escaped == null && left == 2 && FakeDot.Ran.Count == 2 && Total() == 1);
        Reset();
        left = new FakeDotR2 { target = T("beetle", null, _ => HitThrowB()), attackers = new[] { commando, engi } }.Evaluate(FakeDotR2.DotIndex.Burn);
        Check("dotguard: a DoT R2API sends past the call still deals no damage there", left == 2 && FakeDot.Ran.Count == 0 && Total() == 0);

        // The run-start path.
        var onStart = guard.GetMethod("OnRunStart", all);
        guard.GetField("patcher", all).SetValue(null, new Harmony("test.dotguard2"));
        guard.GetField("game", all).SetValue(null, typeof(FakeDot).Assembly);
        Reset();
        onStart.Invoke(null, new object[] { null });
        Check("dotguard: a game with no DotController is left alone and warned about", logs.Count(l => l.Contains("not applied")) == 1);
        logs.Clear();
        onStart.Invoke(null, new object[] { null });
        Check("dotguard: the run-start handler only works once", logs.Count == 0);
        guard.GetField("game", all).SetValue(null, null);
        Reset();
        Exception startError = null;
        try { onStart.Invoke(null, new object[] { null }); } catch (Exception e) { startError = e; }
        Check("dotguard: an error in the run-start handler is caught and warned about", startError == null && logs.Count(l => l.Contains("can't patch DotController, not applied")) == 1);
        Reset();
        startError = null;
        try { guard.GetMethod("Apply", all).Invoke(null, new object[] { h, typeof(FakeDot).Assembly }); } catch (Exception e) { startError = e; }
        Check("dotguard: a game with no run-start event is left alone and warned about", startError == null && logs.Count(l => l.Contains("can't watch run starts, not applied")) == 1);

        // The load path may only subscribe; patching there can run the game type's static constructor.
        var shieldDll = Mono.Cecil.ModuleDefinition.ReadModule(shieldType.Assembly.Location);
        var guardDef = shieldDll.GetType("CompatShield.DotTickGuard");
        bool Calls(Mono.Cecil.MethodDefinition m, string name) => m.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == name);
        var applyDef = guardDef.Methods.First(m => m.Name == "Apply"); var startDef = guardDef.Methods.First(m => m.Name == "OnRunStart");
        Check("dotguard: Apply (on the load path) subscribes to run starts and never patches", Calls(applyDef, "AddEventHandler") && !Calls(applyDef, "Patch") && !Calls(applyDef, "PatchGuard"));
        Check("dotguard: the patch happens from the run-start handler", Calls(startDef, "PatchGuard"));

        // The real game: the shape check runs over the shipped method, because this harness can't JIT it.
        var managed = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var ror2 = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var dc = ror2.GetType("RoR2.DotController");
        var idx = dc.NestedTypes.FirstOrDefault(n => n.Name == "DotIndex");
        var ev = dc.Methods.FirstOrDefault(m => m.Name == "EvaluateDotStacksForType" && m.Parameters.Count == 3);
        Check("dotguard real: game 1.5.0 has instance void EvaluateDotStacksForType(DotIndex, float, out int)", idx != null && idx.IsEnum && ev != null && !ev.IsStatic && ev.ReturnType.FullName == "System.Void" && ev.Parameters[0].ParameterType.FullName == idx.FullName && ev.Parameters[1].ParameterType.FullName == "System.Single" && ev.Parameters[2].ParameterType.FullName == "System.Int32&");
        var real = ev.Body.Instructions.ToList();
        var steps = real.Select(i => { var r = i.Operand as Mono.Cecil.MethodReference; return Step(i.OpCode.Name, i.OpCode.FlowControl.ToString(), r?.Name, r != null && r.ReturnType.FullName == "System.Void"); }).ToArray();
        var found = Find(steps);
        Check("dotguard real: the shape check accepts the real damage call", found.Item1 == null);
        if (found.Item1 == null) {
            var guarded = new System.Collections.Generic.HashSet<Mono.Cecil.Cil.Instruction>(real.Skip(found.Item2).Take(found.Item3 - found.Item2 + 1));
            Check("dotguard real: the code before the stretch finishes a statement", real[found.Item2 - 1].OpCode.Name == "stfld");
            Check("dotguard real: the guarded stretch opens by loading this", real[found.Item2].OpCode.Name == "ldarg.0");
            Check("dotguard real: the guarded stretch ends on TakeDamage", (real[found.Item3].Operand as Mono.Cecil.MethodReference)?.Name == "TakeDamage");
            Check("dotguard real: nothing jumps into the guarded stretch", !real.Any(i => i.Operand is Mono.Cecil.Cil.Instruction t && guarded.Contains(t)));
            Check("dotguard real: the local it hands the catch is a DamageInfo", LocalLoadedBy(ev, real[found.Item2 + 2])?.VariableType.FullName == "RoR2.DamageInfo");
        }
        var fixedCode = dc.Methods.First(m => m.Name == "FixedUpdate").Body.Instructions.ToList();
        int evalAt = fixedCode.FindIndex(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "EvaluateDotStacksForType");
        int storeAt = evalAt < 0 ? -1 : fixedCode.FindIndex(evalAt + 1, i => i.OpCode.Name == "stelem.r4");
        Check("dotguard real: FixedUpdate stores the DoT's timer only after the evaluation returns", evalAt >= 0 && storeAt > evalAt);
        bool Has(Mono.Cecil.TypeDefinition t, string n) => t.Fields.Any(f => f.Name == n) || t.Properties.Any(p => p.Name == n);
        var healthDef = ror2.GetType("RoR2.HealthComponent"); var bodyDef = ror2.GetType("RoR2.CharacterBody"); var damageDef = ror2.GetType("RoR2.DamageInfo");
        Check("dotguard real: DotController has victimHealthComponent", Has(dc, "victimHealthComponent"));
        Check("dotguard real: DamageInfo has attacker and inflictor", Has(damageDef, "attacker") && Has(damageDef, "inflictor"));
        Check("dotguard real: HealthComponent has body, shield, fullShield, barrier, health and fullHealth", new[] { "body", "shield", "fullShield", "barrier", "health", "fullHealth" }.All(n => Has(healthDef, n)));
        Check("dotguard real: CharacterBody has inventory, master and inputBank", new[] { "inventory", "master", "inputBank" }.All(n => Has(bodyDef, n)));
        Check("dotguard real: Run has onRunStartGlobal", ror2.GetType("RoR2.Run").Events.Any(e => e.Name == "onRunStartGlobal"));
        return failures == 0 ? 0 : 1;
    }

    static int BulletGuardCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var guard = shieldType.Assembly.GetType("CompatShield.BulletHitGuard");
        var shape = shieldType.Assembly.GetType("CompatShield.HitShape");
        var logType = shieldType.Assembly.GetType("CompatShield.FailedHitLog");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var dead = new System.Collections.Generic.HashSet<object>();
        var bodies = new System.Collections.Generic.Dictionary<object, object>();
        Func<object, bool> aliveNormal = o => o != null && !dead.Contains(o);
        logType.GetField("Alive", all).SetValue(null, aliveNormal);
        logType.GetField("NameOf", all).SetValue(null, new Func<object, string>(o => o.ToString()));
        logType.GetField("BodyOf", all).SetValue(null, new Func<object, Type, object>((a, t) => bodies.TryGetValue(a, out var b) ? b : null));
        int Total() => (int)guard.GetProperty("Total", all).GetValue(null);
        void Reset() { guard.GetMethod("ResetForTest", all).Invoke(null, null); logs.Clear(); dead.Clear(); FakeBullet.Ran.Clear(); FakeBullet.Server = true; FakeShotEvents.FailEnemy = FakeShotEvents.FailAll = null; }
        System.Collections.Generic.List<string> Lines() => logs.Where(l => l.Contains("a hit failed")).ToList();
        FakeShotHealth S(string label, Action fail = null, object targetBody = null) => new FakeShotHealth { label = label, fail = fail, body = targetBody };
        FakeBullet.BulletHit On(FakeShotHealth target, bool canDamage = true) => new FakeBullet.BulletHit { entityObject = target?.label, health = target, canDamage = canDamage };
        bool Fire(object attacker, object inflictor, FakeShotHealth target, bool canDamage = true) => FakeBullet.Fire(new FakeBullet { owner = attacker, weapon = inflictor }, On(target, canDamage));
        string[] Whole(string v) => new[] { "take " + v, "enemy " + v, "all " + v };

        // The shape check, on its own.
        var find = guard.GetMethod("FindRange", all);
        var stepOf = shape.GetMethod("StepOf", all, null, new[] { typeof(string), typeof(string), typeof(string), typeof(bool) }, null);
        string Step(string op, string flow, string called, bool isVoid) => (string)stepOf.Invoke(null, new object[] { op, flow, called, isVoid });
        Tuple<string, int, int> Find(params string[] s) { var a = new object[] { s.ToList(), -1, -1 }; var w = (string)find.Invoke(null, a); return Tuple.Create(w, (int)a[1], (int)a[2]); }
        bool Refused(params string[] s) { var r = Find(s); return r.Item1 != null && r.Item2 == -1 && r.Item3 == -1; }
        var good = Find("jump", "local", "local", "void:TakeDamage", "other", "local", "other", "other", "other", "void:OnHitEnemy", "other");
        Check("bulletguard: the real shape is accepted, from the target through OnHitEnemy", good.Item1 == null && good.Item2 == 1 && good.Item3 == 9);
        var nops = Find("jump", "nop", "local", "local", "void:TakeDamage", "nop", "other", "void:OnHitEnemy", "nop", "other");
        Check("bulletguard: nops from a debug build don't matter", nops.Item1 == null && nops.Item2 == 2 && nops.Item3 == 7);
        Check("bulletguard: no TakeDamage is refused", Refused("jump", "local", "local", "void:OnHitEnemy", "other"));
        Check("bulletguard: a TakeDamage that returns a value is refused", Refused("jump", "local", "local", "value:TakeDamage", "void:OnHitEnemy", "other"));
        Check("bulletguard: two TakeDamage calls are refused", Refused("jump", "local", "local", "void:TakeDamage", "local", "local", "void:TakeDamage", "void:OnHitEnemy", "other"));
        Check("bulletguard: no OnHitEnemy is refused", Refused("jump", "local", "local", "void:TakeDamage", "other"));
        Check("bulletguard: two OnHitEnemy calls are refused", Refused("jump", "local", "local", "void:TakeDamage", "void:OnHitEnemy", "void:OnHitEnemy", "other"));
        Check("bulletguard: OnHitEnemy before TakeDamage is refused", Refused("jump", "void:OnHitEnemy", "local", "local", "void:TakeDamage", "other"));
        Check("bulletguard: a target that isn't a local is refused", Refused("jump", "other", "local", "void:TakeDamage", "void:OnHitEnemy", "other"));
        Check("bulletguard: damage info that isn't a local is refused", Refused("jump", "local", "other", "void:TakeDamage", "void:OnHitEnemy", "other"));
        Check("bulletguard: a stretch that may start with a value on the stack is refused", Refused("value:M", "local", "local", "void:TakeDamage", "void:OnHitEnemy", "other"));
        Check("bulletguard: a stretch that doesn't start right after the jump is refused", Refused("store", "local", "local", "void:TakeDamage", "void:OnHitEnemy", "other"));
        Check("bulletguard: nothing before TakeDamage is refused", Refused("local", "local", "void:TakeDamage", "void:OnHitEnemy", "other"));
        Check("bulletguard: a jump in the middle is refused", Refused("jump", "local", "local", "void:TakeDamage", "jump", "void:OnHitEnemy", "other"));
        Check("bulletguard: nothing after OnHitEnemy is refused", Refused("jump", "local", "local", "void:TakeDamage", "void:OnHitEnemy"));

        // Patching.
        var h = new Harmony("test.bulletguard");
        bool Patch(Type t) => (bool)guard.GetMethod("PatchGuard", all).Invoke(null, new object[] { h, t });
        bool Warned(string text) { bool w = logs.Count(l => l.Contains("not applied") && l.Contains(text)) == 1; logs.Clear(); return w; }
        Reset();
        Check("bulletguard: a type with no BulletHit is left alone and warned about", !Patch(typeof(FakeBulletNoHit)) && Warned("not found"));
        Check("bulletguard: an instance callback is left alone and warned about", !Patch(typeof(FakeBulletInstance)) && Warned("not found"));
        Check("bulletguard: a callback that returns nothing is left alone and warned about", !Patch(typeof(FakeBulletWrongReturn)) && Warned("not found"));
        Check("bulletguard: a missing BulletAttack is left alone and warned about", !Patch(null) && Warned("not found"));
        Check("bulletguard: code of another shape is refused and warned about", !Patch(typeof(FakeBulletBranchy)) && Warned("a jump in the middle"));
        Exception refusedThrow = null;
        try { FakeBulletBranchy.Fire(new FakeBulletBranchy(), new FakeBulletBranchy.BulletHit { health = S("x", HitThrowB) }); } catch (Exception e) { refusedThrow = e; }
        Check("bulletguard: a refused method behaves exactly as it did", refusedThrow is NullReferenceException && Total() == 0);
        Check("bulletguard: patches the real shape and says so", Patch(typeof(FakeBullet)) && logs.Count(l => l.Contains("Bullet guard: one target's error")) == 1);

        object commando = "CommandoBody(Clone)", engi = "EngiBody(Clone)", rifle = "Rifle(Clone)";
        bodies[commando] = new FakeHitBody { inventory = new object(), master = new object() };

        // The behaviour the fix exists for.
        Reset();
        Exception escaped = null;
        bool result = false;
        try { result = Fire(commando, rifle, S("a", HitThrowA, new FakeHitBody { master = new object() })); } catch (Exception e) { escaped = e; }
        Check("bulletguard: one target's exception does not reach whoever fired the bullet", escaped == null);
        Check("bulletguard: the failed target skips OnHitEnemy and still gets OnHitAll", FakeBullet.Ran.SequenceEqual(new[] { "take a", "all a" }));
        Check("bulletguard: the bullet still reports its hit", result);
        Check("bulletguard: one failed hit is counted once", Total() == 1 && Lines().Count == 1);
        var line = Lines()[0];
        Check("bulletguard: the line says what carried on", line.Contains("Bullet guard: a hit failed and the bullet carried on"));
        Check("bulletguard: the line names the exception", line.Contains("NullReferenceException"));
        Check("bulletguard: the line describes the victim", line.Contains("Victim a: body ok, inventory null, master ok, shield 4/10, barrier 0, health 50/100"));
        Check("bulletguard: the line describes the attacker", line.Contains("Attacker CommandoBody(Clone): body ok, inventory ok, input bank null"));
        Check("bulletguard: the line names the inflictor and goes straight on to the count", line.Contains("Inflictor Rifle(Clone). Failed hit 1 this session"));
        Check("bulletguard: the line gives the real throw site", line.Contains("Thrown at:") && line.Contains("HitThrowA"));

        Reset();
        FakeShotEvents.FailEnemy = HitThrowB;
        escaped = null;
        try { Fire(commando, rifle, S("a")); } catch (Exception e) { escaped = e; }
        Check("bulletguard: a throw from OnHitEnemy is caught and OnHitAll still runs", escaped == null && FakeBullet.Ran.SequenceEqual(Whole("a")) && Total() == 1);

        Reset();
        FakeShotEvents.FailAll = HitThrowB;
        escaped = null;
        try { Fire(commando, rifle, S("a")); } catch (Exception e) { escaped = e; }
        Check("bulletguard: a throw from OnHitAll is not caught, because a jump target sits before it", escaped is NullReferenceException && FakeBullet.Ran.SequenceEqual(Whole("a")) && Total() == 0);

        Reset();
        result = Fire(commando, rifle, S("a"));
        Check("bulletguard: a bullet with no error logs nothing and counts nothing", result && FakeBullet.Ran.SequenceEqual(Whole("a")) && Total() == 0 && logs.Count == 0);

        Reset();
        result = Fire(commando, rifle, S("a"), false);
        Check("bulletguard: a hit it can't damage still deals no damage", !result && FakeBullet.Ran.SequenceEqual(new[] { "all a" }) && Total() == 0);

        Reset();
        FakeBullet.Server = false;
        result = Fire(commando, rifle, S("a", HitThrowB));
        Check("bulletguard: a client does none of it", result && FakeBullet.Ran.Count == 0 && Total() == 0);

        // The Motion Tracker's loop.
        Reset();
        var tracker = new FakeShotTimer { gun = new FakeBullet { owner = commando, weapon = rifle }, hit = On(S("a", HitThrowB)) };
        tracker.Tick();
        tracker.Tick();
        tracker.Tick();
        Check("bulletguard: an item that fires on a timer keeps its timing after a failed hit", tracker.fires == 1 && tracker.timer == 0f && Total() == 1);

        // Who gets a line: the rules are shared with the DoT guard and tested there; this confirms the bullet guard hands them the right parts.
        Reset();
        var bee = S("beetle", HitThrowA);
        Fire(commando, rifle, bee);
        Fire(commando, rifle, bee);
        Check("bulletguard: the same failure again is counted but gets no second line", Total() == 2 && Lines().Count == 1);
        Fire(engi, rifle, bee);
        Check("bulletguard: a different attacker is a new line", Lines().Count == 2);
        Fire(commando, "Other(Clone)", bee);
        Check("bulletguard: a different inflictor is a new line", Lines().Count == 3);
        Fire(commando, rifle, S("lemurian", HitThrowA));
        Check("bulletguard: a different victim is a new line", Lines().Count == 4);
        Reset();
        for (int i = 0; i < 10; i++) Fire(commando, rifle, bee);
        Check("bulletguard: its running total is its own", logs.Count(l => l.Contains("Bullet guard: 10 failed hits caught so far")) == 1);

        // A failure inside the guard must not become a new failure.
        Reset();
        logType.GetField("Alive", all).SetValue(null, new Func<object, bool>(o => { throw new InvalidOperationException("broken alive"); }));
        escaped = null;
        try { Fire(commando, rifle, S("a", HitThrowB)); } catch (Exception e) { escaped = e; }
        logType.GetField("Alive", all).SetValue(null, aliveNormal);
        Check("bulletguard: a guard that can't describe the hit still stops the error and carries on", escaped == null && FakeBullet.Ran.Last() == "all a" && Total() == 1);
        Check("bulletguard: that is said once", logs.Count(l => l.Contains("couldn't describe a failed hit")) == 1);

        // The run-start path.
        var onStart = guard.GetMethod("OnRunStart", all);
        guard.GetField("patcher", all).SetValue(null, new Harmony("test.bulletguard2"));
        guard.GetField("game", all).SetValue(null, typeof(FakeBullet).Assembly);
        Reset();
        onStart.Invoke(null, new object[] { null });
        Check("bulletguard: a game with no BulletAttack is left alone and warned about", logs.Count(l => l.Contains("not applied")) == 1);
        logs.Clear();
        onStart.Invoke(null, new object[] { null });
        Check("bulletguard: the run-start handler only works once", logs.Count == 0);
        guard.GetField("game", all).SetValue(null, null);
        Reset();
        Exception startError = null;
        try { onStart.Invoke(null, new object[] { null }); } catch (Exception e) { startError = e; }
        Check("bulletguard: an error in the run-start handler is caught and warned about", startError == null && logs.Count(l => l.Contains("can't patch BulletAttack, not applied")) == 1);
        Reset();
        startError = null;
        try { guard.GetMethod("Apply", all).Invoke(null, new object[] { h, typeof(FakeBullet).Assembly }); } catch (Exception e) { startError = e; }
        Check("bulletguard: a game with no run-start event is left alone and warned about", startError == null && logs.Count(l => l.Contains("can't watch run starts, not applied")) == 1);

        // The load path may only subscribe; BulletAttack's static constructor touches Unity physics and has crashed the game from the load path before.
        var shieldDll = Mono.Cecil.ModuleDefinition.ReadModule(shieldType.Assembly.Location);
        var guardDef = shieldDll.GetType("CompatShield.BulletHitGuard");
        bool Calls(Mono.Cecil.MethodDefinition m, string name) => m.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == name);
        var applyDef = guardDef.Methods.First(m => m.Name == "Apply"); var startDef = guardDef.Methods.First(m => m.Name == "OnRunStart");
        Check("bulletguard: Apply (on the load path) subscribes to run starts and never patches", Calls(applyDef, "AddEventHandler") && !Calls(applyDef, "Patch") && !Calls(applyDef, "PatchGuard"));
        Check("bulletguard: the patch happens from the run-start handler", Calls(startDef, "PatchGuard"));

        // The real game.
        var managed = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var ror2 = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var ba = ror2.GetType("RoR2.BulletAttack");
        var hit = ba.NestedTypes.FirstOrDefault(n => n.Name == "BulletHit");
        var cb = ba.Methods.FirstOrDefault(m => m.Name == "DefaultHitCallbackImplementation" && m.Parameters.Count == 2);
        Check("bulletguard real: game 1.5.0 has static bool DefaultHitCallbackImplementation(BulletAttack, ref BulletHit)", hit != null && cb != null && cb.IsStatic && cb.ReturnType.FullName == "System.Boolean" && cb.Parameters[0].ParameterType.FullName == ba.FullName && cb.Parameters[1].ParameterType.FullName == hit.FullName + "&");
        var real = cb.Body.Instructions.ToList();
        var steps = real.Select(i => { var r = i.Operand as Mono.Cecil.MethodReference; return Step(i.OpCode.Name, i.OpCode.FlowControl.ToString(), r?.Name, r != null && r.ReturnType.FullName == "System.Void"); }).ToArray();
        var found = Find(steps);
        Check("bulletguard real: the shape check accepts the real damage calls", found.Item1 == null);
        if (found.Item1 == null) {
            var guarded = new System.Collections.Generic.HashSet<Mono.Cecil.Cil.Instruction>(real.Skip(found.Item2).Take(found.Item3 - found.Item2 + 1));
            Check("bulletguard real: the first local it hands the catch is a HealthComponent", LocalLoadedBy(cb, real[found.Item2])?.VariableType.FullName == "RoR2.HealthComponent");
            Check("bulletguard real: the second local it hands the catch is a DamageInfo", LocalLoadedBy(cb, real[found.Item2 + 1])?.VariableType.FullName == "RoR2.DamageInfo");
            Check("bulletguard real: the guarded stretch ends on OnHitEnemy", (real[found.Item3].Operand as Mono.Cecil.MethodReference)?.Name == "OnHitEnemy");
            Check("bulletguard real: nothing jumps into the guarded stretch", !real.Any(i => i.Operand is Mono.Cecil.Cil.Instruction t && guarded.Contains(t)));
        }
        Check("bulletguard real: the method has no exception handling of its own", !cb.Body.HasExceptionHandlers);
        bool Has(Mono.Cecil.TypeDefinition t, string n) => t.Fields.Any(f => f.Name == n) || t.Properties.Any(p => p.Name == n);
        var damageDef = ror2.GetType("RoR2.DamageInfo");
        Check("bulletguard real: DamageInfo has attacker and inflictor", Has(damageDef, "attacker") && Has(damageDef, "inflictor"));
        Check("bulletguard real: Run has onRunStartGlobal", ror2.GetType("RoR2.Run").Events.Any(e => e.Name == "onRunStartGlobal"));
        return failures == 0 ? 0 : 1;
    }

    static int AcolyteCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var tweak = shieldType.Assembly.GetType("CompatShield.AcolyteTweak");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        tweak.GetField("ComponentOf", all).SetValue(null, new Func<object, Type, object>((p, t) => { var prefab = (FakeAcoPrefab)p; return prefab.body != null && prefab.body.GetType() == t ? prefab.body : prefab.swap != null && prefab.swap.GetType() == t ? prefab.swap : null; }));
        void Reset() { tweak.GetMethod("ResetForTest", all).Invoke(null, null); logs.Clear(); FakeAcoCatalog.Asked = null; FakeAcoCatalog.Found = null; }
        bool Scale(Type catalog, Type bodyType, Type swapType, float mult) => (bool)tweak.GetMethod("Scale", all).Invoke(null, new object[] { catalog, bodyType, swapType, mult });
        bool Warned(string text) => logs.Count(l => l.Contains(text)) == 1;
        bool Wanted(float mult) => (bool)tweak.GetMethod("Wanted", all).Invoke(null, new object[] { mult });
        bool Close(float a, float b) => Math.Abs(a - b) < 0.001f;

        Check("acolyte: 1.5 is wanted, and so is slowing down", Wanted(1.5f) && Wanted(0.5f));
        Check("acolyte: 1 is the game's default, so nothing to do", !Wanted(1f) && !Wanted(1.0005f));
        Check("acolyte: 0 and below are ignored", !Wanted(0f) && !Wanted(-2f));

        // The change itself.
        Reset();
        var body = new FakeAcoBody(); var swap = new FakeAcoSwap();
        FakeAcoCatalog.Found = new FakeAcoPrefab { body = body, swap = swap };
        Check("acolyte: scales when everything is there", Scale(typeof(FakeAcoCatalog), typeof(FakeAcoBody), typeof(FakeAcoSwap), 1.5f));
        Check("acolyte: asks the catalog for AcolyteBody by name, not the overload that takes an index", FakeAcoCatalog.Asked == "AcolyteBody");
        Check("acolyte: the normal form is 50% faster", Close(body.baseAttackSpeed, 1.5f));
        Check("acolyte: the transformed form is 50% faster too", Close(swap.Value, 1.8f));
        Check("acolyte: a level bonus of zero stays zero", body.levelAttackSpeed == 0f);
        Check("acolyte: the log says what changed", Warned("Acolyte: base attack speed 1 -> 1.5, transformed form 1.2 -> 1.8") && logs.Exists(l => l.Contains("(1.5x)")));
        Check("acolyte: nothing to warn about", !logs.Exists(l => l.Contains("not applied") || l.Contains("only the normal form")));

        Reset();
        body = new FakeAcoBody { levelAttackSpeed = 0.1f };
        FakeAcoCatalog.Found = new FakeAcoPrefab { body = body, swap = new FakeAcoSwap() };
        Scale(typeof(FakeAcoCatalog), typeof(FakeAcoBody), typeof(FakeAcoSwap), 1.5f);
        Check("acolyte: a level bonus is scaled with the base", Close(body.levelAttackSpeed, 0.15f));

        // What it does when the game isn't what it expects.
        Reset();
        Check("acolyte: no catalog is left alone and warned about", !Scale(null, typeof(FakeAcoBody), typeof(FakeAcoSwap), 1.5f) && Warned("expected game members not found") && Warned("not applied"));
        Reset();
        Check("acolyte: no CharacterBody is left alone and warned about", !Scale(typeof(FakeAcoCatalog), null, typeof(FakeAcoSwap), 1.5f) && Warned("expected game members not found"));
        Reset();
        Check("acolyte: a catalog with no lookup by name is left alone and warned about", !Scale(typeof(FakeAcoNoFind), typeof(FakeAcoBody), typeof(FakeAcoSwap), 1.5f) && Warned("expected game members not found"));
        Reset();
        Check("acolyte: a body with no level field is left alone and warned about", !Scale(typeof(FakeAcoCatalog), typeof(FakeAcoBodyNoLevel), typeof(FakeAcoSwap), 1.5f) && Warned("expected game members not found"));
        Reset();
        Check("acolyte: a game with no Acolyte is left alone and warned about", !Scale(typeof(FakeAcoCatalog), typeof(FakeAcoBody), typeof(FakeAcoSwap), 1.5f) && Warned("AcolyteBody not found, not applied"));
        Reset();
        var noBody = new FakeAcoPrefab { swap = new FakeAcoSwap() };
        FakeAcoCatalog.Found = noBody;
        Check("acolyte: a prefab with no CharacterBody is left alone and warned about", !Scale(typeof(FakeAcoCatalog), typeof(FakeAcoBody), typeof(FakeAcoSwap), 1.5f) && Warned("AcolyteBody not found, not applied") && Close(((FakeAcoSwap)noBody.swap).Value, 1.2f));
        Reset();
        body = new FakeAcoBody();
        FakeAcoCatalog.Found = new FakeAcoPrefab { body = body };
        Check("acolyte: no transformed form still changes the normal form", Scale(typeof(FakeAcoCatalog), typeof(FakeAcoBody), typeof(FakeAcoSwap), 1.5f) && Close(body.baseAttackSpeed, 1.5f));
        Check("acolyte: and says only the normal form changed", Warned("only the normal form changed") && !logs.Exists(l => l.Contains("transformed form 1")));
        Reset();
        body = new FakeAcoBody();
        FakeAcoCatalog.Found = new FakeAcoPrefab { body = body };
        Check("acolyte: a game with no swap controller still changes the normal form", Scale(typeof(FakeAcoCatalog), typeof(FakeAcoBody), null, 1.5f) && Close(body.baseAttackSpeed, 1.5f) && Warned("only the normal form changed"));
        Reset();
        body = new FakeAcoBody();
        var wrongSwap = new FakeAcoSwapWrongType();
        FakeAcoCatalog.Found = new FakeAcoPrefab { body = body, swap = wrongSwap };
        Check("acolyte: a swap value of another type is left alone", Scale(typeof(FakeAcoCatalog), typeof(FakeAcoBody), typeof(FakeAcoSwapWrongType), 1.5f) && wrongSwap.Value == 2 && Warned("only the normal form changed"));

        // Apply and the run-start path.
        Reset();
        Exception startError = null;
        try { tweak.GetMethod("Apply", all).Invoke(null, new object[] { typeof(FakeAcoCatalog).Assembly, 1f }); } catch (Exception e) { startError = e; }
        Check("acolyte: a multiplier of 1 does nothing at all", startError == null && logs.Count == 0);
        Reset();
        startError = null;
        try { tweak.GetMethod("Apply", all).Invoke(null, new object[] { typeof(FakeAcoCatalog).Assembly, 1.5f }); } catch (Exception e) { startError = e; }
        Check("acolyte: a game with no run-start event is left alone and warned about", startError == null && Warned("can't watch run starts, not applied"));
        var onStart = tweak.GetMethod("OnRunStart", all);
        tweak.GetField("multiplier", all).SetValue(null, 1.5f);
        tweak.GetField("game", all).SetValue(null, typeof(FakeAcoCatalog).Assembly);
        Reset();
        onStart.Invoke(null, new object[] { null });
        Check("acolyte: a game with no catalog is left alone and warned about at run start", Warned("expected game members not found"));
        logs.Clear();
        onStart.Invoke(null, new object[] { null });
        Check("acolyte: the run-start handler only works once, so the speed can't compound", logs.Count == 0);
        tweak.GetField("game", all).SetValue(null, null);
        Reset();
        startError = null;
        try { onStart.Invoke(null, new object[] { null }); } catch (Exception e) { startError = e; }
        Check("acolyte: an error in the run-start handler is caught and warned about", startError == null && Warned("couldn't change attack speed, not applied"));

        // Nothing may touch a prefab on the load path.
        var shieldDll = Mono.Cecil.ModuleDefinition.ReadModule(shieldType.Assembly.Location);
        var tweakDef = shieldDll.GetType("CompatShield.AcolyteTweak");
        bool Calls(Mono.Cecil.MethodDefinition m, string name) => m.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == name);
        var applyDef = tweakDef.Methods.First(m => m.Name == "Apply"); var startDef = tweakDef.Methods.First(m => m.Name == "OnRunStart");
        Check("acolyte: Apply (on the load path) subscribes to run starts and never scales", Calls(applyDef, "AddEventHandler") && !Calls(applyDef, "Scale"));
        Check("acolyte: the change happens from the run-start handler", Calls(startDef, "Scale"));

        // The real game.
        var managed = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var ror2 = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var bodyDef = ror2.GetType("RoR2.CharacterBody"); var swapDef = ror2.GetType("RoR2.AcolyteBodySwapController"); var catalog = ror2.GetType("RoR2.BodyCatalog");
        Check("acolyte real: CharacterBody has float baseAttackSpeed and levelAttackSpeed fields", new[] { "baseAttackSpeed", "levelAttackSpeed" }.All(n => bodyDef.Fields.Any(f => f.Name == n && f.FieldType.FullName == "System.Single")));
        Check("acolyte real: AcolyteBodySwapController has a float baseAttackSpeed field", swapDef != null && swapDef.Fields.Any(f => f.Name == "baseAttackSpeed" && f.FieldType.FullName == "System.Single"));
        Check("acolyte real: BodyCatalog has static FindBodyPrefab(string)", catalog.Methods.Any(m => m.Name == "FindBodyPrefab" && m.IsStatic && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.String"));
        var swapCode = swapDef.Methods.First(m => m.Name == "SwapBodyInternal").Body.Instructions.ToList();
        Check("acolyte real: the transformation writes the controller's own value onto the body", swapCode.Any(i => i.OpCode.Name == "ldfld" && i.Operand is Mono.Cecil.FieldReference f && f.Name == "baseAttackSpeed" && f.DeclaringType.Name == "AcolyteBodySwapController" && i.Next.OpCode.Name == "stfld" && i.Next.Operand is Mono.Cecil.FieldReference g && g.Name == "baseAttackSpeed" && g.DeclaringType.Name == "CharacterBody"));
        Check("acolyte real: Run has onRunStartGlobal", ror2.GetType("RoR2.Run").Events.Any(e => e.Name == "onRunStartGlobal"));
        return failures == 0 ? 0 : 1;
    }

    public class FakeDagDef { }
    public class FakeDagLocator { public FakeDagSkill primary; }
    public class FakeDagBody { public FakeDagLocator Loc; public FakeDagLocator skillLocator => Loc; }
    public class FakeDagSkill { public FakeDagBody Body; public FakeDagDef Def; public FakeDagBody characterBody => Body; public FakeDagDef skillDef => Def; }
    public static class FakeDag {
        public static int Origs, Boosts;
        public static bool op_Equality(object a, object b) => ReferenceEquals(a, b);
        public static void Orig() { Origs++; }
        public static void Boost() { Boosts++; }
    }
    class FakeDagPlain {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static int Run() => 1;
    }

    // The harness is a Debug build, whose compiled "if" has a different shape from the mod's, so the mod's shape is written out by hand.
    static System.Collections.Generic.List<HarmonyLib.CodeInstruction> DagShape(System.Reflection.Emit.ILGenerator il) {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        HarmonyLib.CodeInstruction Op(System.Reflection.Emit.OpCode o, object operand = null) => new HarmonyLib.CodeInstruction(o, operand);
        var callvirt = System.Reflection.Emit.OpCodes.Callvirt;
        var call = System.Reflection.Emit.OpCodes.Call;
        var ldarg = System.Reflection.Emit.OpCodes.Ldarg_0;
        var end = il.DefineLabel();
        var last = Op(call, typeof(FakeDag).GetMethod("Orig", all));
        last.labels.Add(end);
        return new System.Collections.Generic.List<HarmonyLib.CodeInstruction> {
            Op(ldarg),
            Op(callvirt, typeof(FakeDagSkill).GetMethod("get_characterBody", all)),
            Op(callvirt, typeof(FakeDagBody).GetMethod("get_skillLocator", all)),
            Op(System.Reflection.Emit.OpCodes.Ldfld, typeof(FakeDagLocator).GetField("primary", all)),
            Op(callvirt, typeof(FakeDagSkill).GetMethod("get_skillDef", all)),
            Op(ldarg),
            Op(callvirt, typeof(FakeDagSkill).GetMethod("get_skillDef", all)),
            Op(call, typeof(FakeDag).GetMethod("op_Equality", all)),
            Op(System.Reflection.Emit.OpCodes.Brfalse, end),
            Op(call, typeof(FakeDag).GetMethod("Boost", all)),
            last,
            Op(System.Reflection.Emit.OpCodes.Ret),
        };
    }

    static Action<FakeDagSkill> DagEmit(Func<System.Reflection.Emit.ILGenerator, System.Collections.Generic.List<HarmonyLib.CodeInstruction>, System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction>> transform) {
        var dm = new System.Reflection.Emit.DynamicMethod("dag", typeof(void), new[] { typeof(FakeDagSkill) }, typeof(FakeDag).Module, true);
        var il = dm.GetILGenerator();
        foreach (var ci in transform(il, DagShape(il))) {
            foreach (var label in ci.labels) il.MarkLabel(label);
            if (ci.operand == null) il.Emit(ci.opcode);
            else if (ci.operand is MethodInfo m) il.Emit(ci.opcode, m);
            else if (ci.operand is FieldInfo f) il.Emit(ci.opcode, f);
            else il.Emit(ci.opcode, (System.Reflection.Emit.Label)ci.operand);
        }
        return (Action<FakeDagSkill>)dm.CreateDelegate(typeof(Action<FakeDagSkill>));
    }

    static int DaggerCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var fix = shieldType.Assembly.GetType("CompatShield.DaggerOfFrenzyFix");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        int failed = 0;
        void Check(string what, bool ok) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) failed++; }

        var transpiler = fix.GetMethod("Transpiler", all);
        System.Collections.Generic.List<HarmonyLib.CodeInstruction> Transpile(System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> list, System.Reflection.Emit.ILGenerator il) =>
            ((System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction>)transpiler.Invoke(null, new object[] { list, il })).ToList();
        bool Throws(Action<FakeDagSkill> f, FakeDagSkill s) { try { f(s); return false; } catch (NullReferenceException) { return true; } }

        var def = new FakeDagDef();
        var noLocator = new FakeDagSkill { Body = new FakeDagBody(), Def = def };
        var noPrimary = new FakeDagSkill { Body = new FakeDagBody { Loc = new FakeDagLocator() }, Def = def };
        var primary = new FakeDagSkill { Body = new FakeDagBody { Loc = new FakeDagLocator() }, Def = def };
        primary.Body.Loc.primary = primary;
        var secondary = new FakeDagSkill { Body = primary.Body, Def = new FakeDagDef() };

        var raw = DagEmit((il, list) => list);
        Check("without the guard a body with no skill locator throws", Throws(raw, noLocator));
        Check("without the guard a body with no primary skill throws", Throws(raw, noPrimary));

        var guarded = DagEmit((il, list) => Transpile(list, il));
        Check("the mod's shape is recognised", (bool)fix.GetField("matched", all).GetValue(null));
        FakeDag.Origs = 0; FakeDag.Boosts = 0;
        Check("no skill locator no longer throws", !Throws(guarded, noLocator));
        Check("no skill locator still runs the skill, without the buff", FakeDag.Origs == 1 && FakeDag.Boosts == 0);
        FakeDag.Origs = 0; FakeDag.Boosts = 0;
        Check("no primary skill no longer throws", !Throws(guarded, noPrimary));
        Check("no primary skill still runs the skill, without the buff", FakeDag.Origs == 1 && FakeDag.Boosts == 0);
        FakeDag.Origs = 0; FakeDag.Boosts = 0;
        guarded(primary);
        Check("the primary skill still reaches the buff code", FakeDag.Origs == 1 && FakeDag.Boosts == 1);
        FakeDag.Origs = 0; FakeDag.Boosts = 0;
        guarded(secondary);
        Check("another skill still skips the buff code", FakeDag.Origs == 1 && FakeDag.Boosts == 0);

        int warned = logs.Count;
        var il2 = new System.Reflection.Emit.DynamicMethod("dag2", typeof(void), new[] { typeof(FakeDagSkill) }, typeof(FakeDag).Module, true).GetILGenerator();
        var wrong = DagShape(il2);
        wrong.RemoveAt(4);
        int wrongCount = wrong.Count;
        Check("a different shape is left unchanged", Transpile(wrong, il2).Count == wrongCount);
        Check("a different shape logs not applied", logs.Skip(warned).Any(l => l.Contains("found 0") && l.Contains("not applied")));
        warned = logs.Count;
        var twice = DagShape(il2);
        twice.InsertRange(9, DagShape(il2).Take(9));
        int twiceCount = twice.Count;
        Check("two matches are left unchanged", Transpile(twice, il2).Count == twiceCount);
        Check("two matches log not applied", logs.Skip(warned).Any(l => l.Contains("found 2") && l.Contains("not applied")));

        var h = new HarmonyLib.Harmony("shieldtest.dagger");
        warned = logs.Count;
        h.Patch(typeof(FakeDagPlain).GetMethod("Run", all), transpiler: new HarmonyLib.HarmonyMethod(transpiler));
        Check("Harmony binds the transpiler and a plain method keeps working", FakeDagPlain.Run() == 1 && logs.Skip(warned).Any(l => l.Contains("not applied")));

        fix.GetMethod("Apply", all).Invoke(null, new object[] { h, typeof(object).Assembly, typeof(object).Assembly });
        Check("a game without the run start event logs not applied", logs.Any(l => l.Contains("can't watch run starts") && l.Contains("not applied")));
        var onStart = fix.GetMethod("OnRunStart", all);
        Check("the run start handler binds to an event delegate", Delegate.CreateDelegate(typeof(Action<string>), onStart, false) != null);
        warned = logs.Count;
        onStart.Invoke(null, new object[] { null });
        onStart.Invoke(null, new object[] { null });
        Check("a mod without the method logs not applied, once", logs.Skip(warned).Count(l => l.Contains("ApplyDaggerAttackSpeedBuff not found") && l.Contains("not applied")) == 1);

        const string managedDir = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var modPath = System.IO.Path.Combine(profile, "plugins", "irule4567-Smite_Items", "Smite Items.dll");
        var ror2Path = System.IO.Path.Combine(managedDir, "RoR2.dll");
        if (!System.IO.File.Exists(modPath) || !System.IO.File.Exists(ror2Path)) {
            Console.WriteLine("SKIP  Smite Items or the game is not installed here");
            return failed;
        }
        var resolver = new Mono.Cecil.DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managedDir);
        var read = new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver };
        var asmDef = Mono.Cecil.AssemblyDefinition.ReadAssembly(modPath, read);
        Check("the assembly is named Smite Items", asmDef.Name.Name == "Smite Items");
        var type = asmDef.MainModule.GetType("Smite_Items.Equipment.DaggerOfFrenzy");
        var methods = type?.Methods.Where(m => m.Name == "ApplyDaggerAttackSpeedBuff").ToList();
        Check("the hook has one overload", methods != null && methods.Count == 1);
        var ins = methods?.FirstOrDefault()?.Body.Instructions;
        string Name(Mono.Cecil.Cil.Instruction i) => (i.Operand as Mono.Cecil.MemberReference)?.Name;
        int hits = 0;
        for (int i = 0; ins != null && i + 6 < ins.Count; i++)
            if (Name(ins[i]) == "get_skillLocator" && Name(ins[i + 1]) == "primary" && Name(ins[i + 2]) == "get_skillDef"
                && ins[i + 3].OpCode.Name.StartsWith("ldarg") && Name(ins[i + 4]) == "get_skillDef" && Name(ins[i + 5]) == "op_Equality"
                && (ins[i + 6].OpCode == Mono.Cecil.Cil.OpCodes.Brfalse || ins[i + 6].OpCode == Mono.Cecil.Cil.OpCodes.Brfalse_S)) hits++;
        Check("the mod's code has the expected check exactly once", hits == 1);
        var game = Mono.Cecil.AssemblyDefinition.ReadAssembly(ror2Path, read).MainModule;
        Check("Run.onRunStartGlobal is an event", game.GetType("RoR2.Run")?.Events.Any(e => e.Name == "onRunStartGlobal") == true);
        Check("SkillLocator.primary is a field", game.GetType("RoR2.SkillLocator")?.Fields.Any(f => f.Name == "primary") == true);
        return failed;
    }

    class FakeTheta {
        public bool Auth = true, Shield, Boom;
        public int Enters, Exits;
        public void OnEnter() { Enters++; }
        public void OnExit() { Exits++; }
    }

    static int ThetaCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var fix = shieldType.Assembly.GetType("CompatShield.ThetaShieldLoopFix");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        int failed = 0;
        void Check(string what, bool ok) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) failed++; }

        int backOffs = 0;
        fix.GetField("IsAuthority", all).SetValue(null, (Func<object, bool>)(s => ((FakeTheta)s).Auth));
        fix.GetField("HasShield", all).SetValue(null, (Func<object, bool>)(s => ((FakeTheta)s).Shield));
        fix.GetField("BackOff", all).SetValue(null, (Action<object>)(s => { if (((FakeTheta)s).Boom) throw new InvalidOperationException("boom"); backOffs++; }));
        var h = new HarmonyLib.Harmony("shieldtest.theta");
        h.Patch(typeof(FakeTheta).GetMethod("OnEnter"), postfix: new HarmonyLib.HarmonyMethod(fix.GetMethod("EnterPostfix", all)));
        h.Patch(typeof(FakeTheta).GetMethod("OnExit"), prefix: new HarmonyLib.HarmonyMethod(fix.GetMethod("ExitPrefix", all)));

        var bail = new FakeTheta();
        bail.OnEnter();
        Check("a failed cast still runs the mod's OnEnter", bail.Enters == 1);
        Check("a failed cast goes on the retry delay", backOffs == 1);
        Check("the first failed cast is logged", logs.Count(l => l.Contains("stopped a failed shield cast")) == 1);
        bail.OnExit();
        Check("a failed cast skips the exit with the scene search and sound", bail.Exits == 0);
        var bail2 = new FakeTheta();
        bail2.OnEnter(); bail2.OnExit();
        Check("each failed cast backs off", backOffs == 2 && bail2.Exits == 0);
        Check("the failed cast log line appears once", logs.Count(l => l.Contains("stopped a failed shield cast")) == 1);

        backOffs = 0;
        var real = new FakeTheta { Shield = true };
        real.OnEnter();
        Check("a real cast has no retry delay", backOffs == 0);
        real.OnExit();
        Check("a real cast runs its exit", real.Exits == 1);

        var lost = new FakeTheta { Shield = true };
        lost.OnEnter();
        lost.Shield = false;
        lost.OnExit();
        Check("a real cast whose shield was destroyed still runs its exit", lost.Exits == 1);

        var remote = new FakeTheta { Auth = false };
        remote.OnEnter();
        remote.OnExit();
        Check("a copy without authority has no retry delay", backOffs == 0);
        Check("a copy without authority runs its exit", remote.Exits == 1);

        var boom = new FakeTheta { Boom = true };
        bool threw = false;
        try { boom.OnEnter(); boom.OnExit(); } catch (Exception) { threw = true; }
        Check("a retry delay failure doesn't reach the game", !threw);
        Check("a retry delay failure logs a warning", logs.Any(l => l.Contains("couldn't delay a failed cast")));

        int warned = logs.Count;
        var patch = fix.GetMethod("Patch", all);
        patch.Invoke(null, new object[] { h, typeof(object).Assembly, typeof(object).Assembly });
        Check("a mod without CastShield logs not applied", logs.Skip(warned).Any(l => l.Contains("CastShield") && l.Contains("not applied")));
        warned = logs.Count;
        fix.GetMethod("Apply", all).Invoke(null, new object[] { h, typeof(object).Assembly, typeof(object).Assembly });
        Check("a game without the run start event logs not applied", logs.Skip(warned).Any(l => l.Contains("can't watch run starts") && l.Contains("not applied")));
        var onStart = fix.GetMethod("OnRunStart", all);
        Check("the run start handler binds to an event delegate", Delegate.CreateDelegate(typeof(Action<string>), onStart, false) != null);
        warned = logs.Count;
        onStart.Invoke(null, new object[] { null });
        onStart.Invoke(null, new object[] { null });
        Check("the run start handler patches once", logs.Skip(warned).Count(l => l.Contains("not applied")) == 1);

        const string managedDir = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var modPath = System.IO.Path.Combine(profile, "plugins", "SandsweptTeam-Sandswept", "Sandswept.dll");
        var ror2Path = System.IO.Path.Combine(managedDir, "RoR2.dll");
        if (!System.IO.File.Exists(modPath) || !System.IO.File.Exists(ror2Path)) {
            Console.WriteLine("SKIP  Sandswept or the game is not installed here");
            return failed;
        }
        var resolver = new Mono.Cecil.DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managedDir);
        var read = new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver };
        var asmDef = Mono.Cecil.AssemblyDefinition.ReadAssembly(modPath, read);
        Check("the assembly is named Sandswept", asmDef.Name.Name == "Sandswept");
        var state = asmDef.MainModule.GetType("Sandswept.Enemies.ThetaConstruct.CastShield");
        Check("CastShield exists and derives from BaseSkillState", state?.BaseType?.FullName == "EntityStates.BaseSkillState");
        Check("shieldInstance is a GameObject field", state?.Fields.FirstOrDefault(f => f.Name == "shieldInstance")?.FieldType.FullName == "UnityEngine.GameObject");
        var enter = state?.Methods.FirstOrDefault(m => m.Name == "OnEnter" && !m.HasParameters);
        var exit = state?.Methods.FirstOrDefault(m => m.Name == "OnExit" && !m.HasParameters);
        Check("CastShield declares OnEnter and OnExit", enter != null && exit != null);
        string Name(Mono.Cecil.Cil.Instruction i) => (i.Operand as Mono.Cecil.MemberReference)?.Name;
        var ins = enter?.Body.Instructions.ToList();
        int setAt = ins?.FindIndex(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Stfld && Name(i) == "shieldInstance") ?? -1;
        int deductAt = ins?.FindIndex(i => Name(i) == "DeductStock") ?? -1;
        Check("OnEnter stores the shield before it deducts stock", setAt >= 0 && deductAt > setAt);
        Check("OnEnter has the early exits that skip the stock", ins?.Count(i => Name(i) == "SetNextStateToMain") >= 2);
        Check("OnExit is where the scene search is reached", exit?.Body.Instructions.Any(i => Name(i) == "IsShieldedByOther") == true);
        var game = Mono.Cecil.AssemblyDefinition.ReadAssembly(ror2Path, read).MainModule;
        var es = game.GetType("EntityStates.EntityState");
        Check("EntityState.isAuthority exists", es?.Properties.Any(p => p.Name == "isAuthority") == true);
        Check("EntityState.skillLocator exists", es?.Properties.Any(p => p.Name == "skillLocator") == true);
        var baseExit = es?.Methods.FirstOrDefault(m => m.Name == "OnExit" && !m.HasParameters);
        Check("EntityState.OnExit does nothing, so skipping it loses nothing", baseExit != null && baseExit.Body.Instructions.All(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Nop || i.OpCode == Mono.Cecil.Cil.OpCodes.Ret));
        Check("BaseState doesn't override OnExit", game.GetType("EntityStates.BaseState")?.Methods.Any(m => m.Name == "OnExit") == false);
        Check("BaseSkillState doesn't override OnExit", game.GetType("EntityStates.BaseSkillState")?.Methods.Any(m => m.Name == "OnExit") == false);
        Check("SkillLocator.primary is a field", game.GetType("RoR2.SkillLocator")?.Fields.Any(f => f.Name == "primary") == true);
        var gs = game.GetType("RoR2.GenericSkill");
        Check("GenericSkill.DeductStock takes one int", gs?.Methods.Any(m => m.Name == "DeductStock" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.Int32") == true);
        Check("GenericSkill.cooldownRemaining can be read", gs?.Properties.FirstOrDefault(p => p.Name == "cooldownRemaining")?.GetMethod != null);
        var sw = gs?.Properties.FirstOrDefault(p => p.Name == "rechargeStopwatch");
        Check("GenericSkill.rechargeStopwatch can be read and set", sw?.GetMethod != null && sw?.SetMethod != null);
        Check("Run.onRunStartGlobal is an event", game.GetType("RoR2.Run")?.Events.Any(e => e.Name == "onRunStartGlobal") == true);
        return failed;
    }

    class FakeSkillIcon { }
    class FakeExtraHud {
        public static int Ran;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static FakeSkillIcon CopyUISkillSlot(string actionName, FakeSkillIcon skillsContainer) { Ran++; return skillsContainer; }
    }

    static int SkillSlotsCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var fix = shieldType.Assembly.GetType("CompatShield.SkillSlotsHudFix");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        int failed = 0;
        void Check(string what, bool ok) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) failed++; }

        var h = new HarmonyLib.Harmony("shieldtest.skillslots");
        fix.GetMethod("Apply", all).Invoke(null, new object[] { h, typeof(object).Assembly });
        Check("an assembly without the types logs not applied", logs.Any(l => l.Contains("CopyUISkillSlot") && l.Contains("not applied")));

        var fake = typeof(FakeExtraHud).GetMethod("CopyUISkillSlot", all);
        h.Patch(fake, prefix: new HarmonyLib.HarmonyMethod(fix.GetMethod("Prefix", all)));
        var build = fix.GetField("Build", all);
        var source = new FakeSkillIcon();
        var built = new FakeSkillIcon();
        string seenAction = null;
        object seenIcon = null;
        build.SetValue(null, new Func<string, object, object>((a, i) => { seenAction = a; seenIcon = i; return built; }));
        FakeExtraHud.Ran = 0;
        var result = fake.Invoke(null, new object[] { "FirstExtraSkill", source });
        Check("a built icon is what the method returns", ReferenceEquals(result, built));
        Check("a built icon skips the mod's own code", FakeExtraHud.Ran == 0);
        Check("the prefix receives the action and the icon", seenAction == "FirstExtraSkill" && ReferenceEquals(seenIcon, source));

        build.SetValue(null, new Func<string, object, object>((a, i) => null));
        FakeExtraHud.Ran = 0;
        result = fake.Invoke(null, new object[] { "FirstExtraSkill", source });
        Check("with the normal HUD the mod's own code runs", FakeExtraHud.Ran == 1 && ReferenceEquals(result, source));

        build.SetValue(null, new Func<string, object, object>((a, i) => { throw new InvalidOperationException("boom"); }));
        FakeExtraHud.Ran = 0;
        int warned = logs.Count;
        fake.Invoke(null, new object[] { "FirstExtraSkill", source });
        fake.Invoke(null, new object[] { "SecondExtraSkill", source });
        Check("a failed build falls back to the mod's own code", FakeExtraHud.Ran == 2);
        Check("a failed build is reported once", logs.Skip(warned).Count(l => l.Contains("boom")) == 1);

        var offset = fix.GetMethod("AboveOffset", all);
        float Above(float r, float s, float y) => (float)offset.Invoke(null, new object[] { r, s, y });
        Check("the row sits a little over one icon up", Math.Abs(Above(100f, 0f, 1f) - 115f) < 0.01f);
        Check("an unbuilt rect falls back to the icon's size", Math.Abs(Above(0f, 80f, 1f) - 92f) < 0.01f);
        Check("no size at all still moves it up", Above(0f, 0f, 1f) > 1f);
        Check("the icon's scale is included", Math.Abs(Above(100f, 0f, 0.5f) - 57.5f) < 0.01f);
        Check("a flipped or zero scale still moves it up", Above(100f, 0f, -1f) > 1f && Above(100f, 0f, 0f) > 1f);

        const string managedDir = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var ror2Path = System.IO.Path.Combine(managedDir, "RoR2.dll");
        var uiPath = System.IO.Path.Combine(managedDir, "UnityEngine.UI.dll");
        var modPath = System.IO.Path.Combine(profile, "plugins", "KingEnderBrine-ExtraSkillSlots", "ExtraSkillSlots", "ExtraSkillSlots.dll");
        if (!System.IO.File.Exists(modPath) || !System.IO.File.Exists(ror2Path) || !System.IO.File.Exists(uiPath)) {
            Console.WriteLine("SKIP  ExtraSkillSlots or the game is not installed here");
            return failed;
        }
        var resolver = new Mono.Cecil.DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managedDir);
        var read = new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver };
        var mod = Mono.Cecil.AssemblyDefinition.ReadAssembly(modPath, read).MainModule;
        var hud = mod.GetType("ExtraSkillSlots.ExtraHud");
        Check("ExtraHud has no static constructor to run at patch time", hud != null && !hud.Methods.Any(m => m.IsConstructor && m.IsStatic));
        var copy = hud?.Methods.Where(m => m.Name == "CopyUISkillSlot").ToList();
        Check("CopyUISkillSlot has one overload", copy != null && copy.Count == 1);
        var method = copy?.FirstOrDefault();
        Check("CopyUISkillSlot takes (string, SkillIcon) and returns SkillIcon", method != null && method.IsStatic && method.Parameters.Count == 2
            && method.Parameters[0].ParameterType.FullName == "System.String" && method.Parameters[1].ParameterType.FullName == "RoR2.UI.SkillIcon" && method.ReturnType.FullName == "RoR2.UI.SkillIcon");
        Check("the mod still looks for the panel by the name the fix tests", method != null && method.Body.Instructions.Any(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr && (string)i.Operand == "SkillBackgroundPanel"));
        var awake = hud?.Methods.FirstOrDefault(m => m.Name == "Awake");
        Check("Awake builds four icons through it", awake != null && awake.Body.Instructions.Count(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "CopyUISkillSlot") == 4);
        Check("ExtraHud.Update exists for the per-frame placement", hud != null && hud.Methods.Any(m => m.Name == "Update" && m.Parameters.Count == 0 && !m.IsStatic));
        Check("the placement postfix takes no arguments", fix.GetMethod("KeepPlaced", all) != null && fix.GetMethod("KeepPlaced", all).GetParameters().Length == 0);
        var sw = mod.GetType("ExtraSkillSlots.SkillInputDisplaySwitch");
        Check("SkillInputDisplaySwitch.modifierGO is a GameObject field", sw != null && sw.Fields.Any(f => f.Name == "modifierGO" && f.FieldType.FullName == "UnityEngine.GameObject"));
        var action = mod.GetType("ExtraSkillSlots.RewiredAction");
        var cctor = action?.Methods.FirstOrDefault(m => m.IsConstructor && m.IsStatic);
        Check("the modifier action is still named ExtraModifier", cctor != null && cctor.Body.Instructions.Any(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr && (string)i.Operand == "ExtraModifier"));

        var game = Mono.Cecil.AssemblyDefinition.ReadAssembly(ror2Path, read).MainModule;
        var controller = game.GetType("RoR2.InputBindingDisplayController");
        Check("InputBindingDisplayController.actionName is a string field", controller != null && controller.Fields.Any(f => f.Name == "actionName" && f.FieldType.FullName == "System.String"));
        Check("MPEventSystemLocator is in RoR2.UI", game.GetType("RoR2.UI.MPEventSystemLocator") != null);

        var ui = Mono.Cecil.AssemblyDefinition.ReadAssembly(uiPath, read).MainModule;
        Check("LayoutGroup exists", ui.GetType("UnityEngine.UI.LayoutGroup") != null);
        var element = ui.GetType("UnityEngine.UI.LayoutElement");
        Check("LayoutElement.ignoreLayout has a setter", element != null && element.Properties.Any(p => p.Name == "ignoreLayout" && p.SetMethod != null));
        var rebuilder = ui.GetType("UnityEngine.UI.LayoutRebuilder");
        Check("ForceRebuildLayoutImmediate has one public overload", rebuilder != null && rebuilder.Methods.Count(m => m.Name == "ForceRebuildLayoutImmediate" && m.IsPublic && m.IsStatic) == 1);
        return failed;
    }

    class FakeFocusCrystal {
        public static bool Ran;
        void RemoveVanilla(MonoMod.Cil.ILContext il) { Ran = true; }
    }

    static int LinearDamageCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var fix = shieldType.Assembly.GetType("CompatShield.LinearDamageFix");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        int failed = 0;
        void Check(string what, bool ok) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) failed++; }
        const string managedDir = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var ror2Path = System.IO.Path.Combine(managedDir, "RoR2.dll");
        var modPath = System.IO.Path.Combine(profile, "plugins", "Risky_Lives-LinearDamage", "LinearDamage.dll");
        var prefix = fix.GetMethod("Prefix", all);
        Check("prefix returns bool and takes il", prefix != null && prefix.ReturnType == typeof(bool) && prefix.GetParameters().Length == 1 && prefix.GetParameters()[0].Name == "il");
        var h = new HarmonyLib.Harmony("shieldtest.lineardamage");
        fix.GetMethod("Apply", all).Invoke(null, new object[] { h, typeof(object).Assembly });
        Check("an assembly without the type logs not applied", logs.Any(l => l.Contains("RemoveVanilla not found") && l.Contains("not applied")));
        if (!System.IO.File.Exists(modPath) || !System.IO.File.Exists(ror2Path)) {
            Console.WriteLine("SKIP  LinearDamage or the game is not installed here");
            return failed;
        }

        var resolver = new Mono.Cecil.DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managedDir);
        var read = new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver };
        bool Load(Mono.Cecil.Cil.Instruction i) => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldsfld && i.Operand is Mono.Cecil.FieldReference f && f.Name == "NearbyDamageBonus" && f.DeclaringType.Name == "Items";
        bool Count(Mono.Cecil.Cil.Instruction i) => i.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt && i.Operand is Mono.Cecil.MethodReference m && m.Name == "GetItemCountEffective";

        // Each case edits a fresh copy of the real TakeDamageProcess the way a mod's hook would, then runs the prefix on it.
        bool Run(Action<Mono.Cecil.MethodDefinition, int> edit, out int added, out System.Collections.Generic.List<Mono.Cecil.Cil.Instruction> after, out int at) {
            var game = Mono.Cecil.AssemblyDefinition.ReadAssembly(ror2Path, read);
            var hc = game.MainModule.GetType("RoR2.HealthComponent").Methods.First(m => m.Name == "TakeDamageProcess");
            int load = hc.Body.Instructions.ToList().FindIndex(Load);
            edit(hc, load);
            int before = hc.Body.Instructions.Count;
            bool result;
            using (var il = new MonoMod.Cil.ILContext(hc)) result = (bool)prefix.Invoke(null, new object[] { il });
            added = hc.Body.Instructions.Count - before;
            after = hc.Body.Instructions.ToList();
            at = after.FindIndex(load, Count);
            return result;
        }
        void Hook(Mono.Cecil.MethodDefinition hc, int load, int pos) {
            var p = hc.Body.GetILProcessor();
            var v = hc.Body.Variables[0];
            var invoke = hc.Module.ImportReference(typeof(object).GetMethod("ToString"));
            var anchor = hc.Body.Instructions[load + pos];
            p.InsertBefore(anchor, p.Create(Mono.Cecil.Cil.OpCodes.Ldloc, v));
            p.InsertBefore(anchor, p.Create(Mono.Cecil.Cil.OpCodes.Call, invoke));
            p.InsertBefore(anchor, p.Create(Mono.Cecil.Cil.OpCodes.Stloc, v));
        }

        int added, at;
        System.Collections.Generic.List<Mono.Cecil.Cil.Instruction> body;
        bool own = Run((hc, load) => { Check("game 1.5.0 still has the item load and count side by side", Count(hc.Body.Instructions[load + 1])); }, out added, out body, out at);
        Check("side by side: the mod's own hook runs and nothing is added", own && added == 0);

        bool ran = Run((hc, load) => { Hook(hc, load, 1); Hook(hc, load, 1); }, out added, out body, out at);
        Check("two mods' code in between: the prefix does the removal", !ran && added == 2);
        Check("count is replaced by zero right after the real call", at > 0 && body[at + 1].OpCode == Mono.Cecil.Cil.OpCodes.Pop && body[at + 2].OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_0);
        Check("the success line is logged", logs.Any(l => l.Contains("no longer counts twice")));

        bool otherItem = Run((hc, load) => {
            var p = hc.Body.GetILProcessor();
            var ins = hc.Body.Instructions;
            var otherField = ins.Skip(load + 2).First(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldsfld && i.Operand is Mono.Cecil.FieldReference f && f.DeclaringType.Name == "Items").Operand as Mono.Cecil.FieldReference;
            var anchor = ins[load + 1];
            p.InsertBefore(anchor, p.Create(Mono.Cecil.Cil.OpCodes.Ldsfld, otherField));
            p.InsertBefore(anchor, p.Create(Mono.Cecil.Cil.OpCodes.Pop));
        }, out added, out body, out at);
        Check("another item's load in between: left to the mod", otherItem && added == 0);

        bool branch = Run((hc, load) => {
            var p = hc.Body.GetILProcessor();
            var anchor = hc.Body.Instructions[load + 1];
            p.InsertBefore(anchor, p.Create(Mono.Cecil.Cil.OpCodes.Br, anchor));
        }, out added, out body, out at);
        Check("a branch in between: left to the mod", branch && added == 0);

        bool far = Run((hc, load) => {
            var p = hc.Body.GetILProcessor();
            var anchor = hc.Body.Instructions[load + 1];
            for (int i = 0; i < 60; i++) p.InsertBefore(anchor, p.Create(Mono.Cecil.Cil.OpCodes.Nop));
        }, out added, out body, out at);
        Check("too much code in between: left to the mod", far && added == 0);
        Check("the shape warning is logged once", logs.Count(l => l.Contains("isn't the expected shape")) == 1);

        // Harmony binds the prefix by parameter name and really skips the mod's method only when it did the work.
        var fake = typeof(FakeFocusCrystal).GetMethod("RemoveVanilla", all);
        h.Patch(fake, prefix: new HarmonyLib.HarmonyMethod(prefix));
        void CallFake(Action<Mono.Cecil.MethodDefinition, int> edit) {
            var game = Mono.Cecil.AssemblyDefinition.ReadAssembly(ror2Path, read);
            var hc = game.MainModule.GetType("RoR2.HealthComponent").Methods.First(m => m.Name == "TakeDamageProcess");
            edit(hc, hc.Body.Instructions.ToList().FindIndex(Load));
            FakeFocusCrystal.Ran = false;
            using (var il = new MonoMod.Cil.ILContext(hc)) fake.Invoke(new FakeFocusCrystal(), new object[] { il });
        }
        CallFake((hc, load) => { });
        Check("patched: adjacent code still runs the mod's method", FakeFocusCrystal.Ran);
        CallFake((hc, load) => Hook(hc, load, 1));
        Check("patched: separated code skips the mod's method", !FakeFocusCrystal.Ran);

        // The real mod is the shape the fix assumes, and patching it at load can't start a Unity static constructor.
        using (var modDef = Mono.Cecil.AssemblyDefinition.ReadAssembly(modPath, read)) {
            Check("assembly name is LinearDamage", modDef.Name.Name == "LinearDamage");
            var type = modDef.MainModule.GetType("LinearDamage.Tweaks.Items.FocusCrystal");
            var remove = type?.Methods.FirstOrDefault(m => m.Name == "RemoveVanilla");
            Check("RemoveVanilla exists once", type != null && type.Methods.Count(m => m.Name == "RemoveVanilla") == 1);
            Check("RemoveVanilla takes ILContext il", remove != null && remove.Parameters.Count == 1 && remove.Parameters[0].Name == "il" && remove.Parameters[0].ParameterType.FullName == "MonoMod.Cil.ILContext");
            Check("FocusCrystal has no static constructor", type != null && !type.Methods.Any(m => m.Name == ".cctor"));
            var strings = type.NestedTypes.SelectMany(n => n.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Where(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr).Select(i => (string)i.Operand).ToList();
            Check("the mod still matches the same item and count call", strings.Contains("NearbyDamageBonus") && strings.Contains("GetItemCountEffective"));
        }
        return failed;
    }

    static int BubbetsCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var fix = shieldType.Assembly.GetType("CompatShield.BubbetsFix");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        int failed = 0;
        void Check(string what, bool ok) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) failed++; }
        const string managedDir = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var ror2Path = System.IO.Path.Combine(managedDir, "RoR2.dll");
        var bubbetsPath = System.IO.Path.Combine(profile, "plugins", "Bubbet-BubbetsItems", "BubbetsItems", "BubbetsItems.dll");
        if (!System.IO.File.Exists(bubbetsPath) || !System.IO.File.Exists(ror2Path)) {
            Console.WriteLine("SKIP  Bubbets Items or the game is not installed here");
            return failed;
        }

        var gameResolver = new Mono.Cecil.DefaultAssemblyResolver();
        gameResolver.AddSearchDirectory(managedDir);
        var gameRead = new Mono.Cecil.ReaderParameters { AssemblyResolver = gameResolver };

        // The preloader shim: Inventory.RemoveItem(ItemDef, int) is added once, forwards to RemoveItem2 and matches the mod's reference.
        using (var game = Mono.Cecil.AssemblyDefinition.ReadAssembly(ror2Path, gameRead))
        using (var modDef = Mono.Cecil.AssemblyDefinition.ReadAssembly(bubbetsPath)) {
            var inv = game.MainModule.GetType("RoR2.Inventory");
            bool Shim(Mono.Cecil.MethodDefinition m) => m.Name == "RemoveItem" && m.Parameters.Count == 2 && m.Parameters[0].ParameterType.FullName == "RoR2.ItemDef";
            Check("game 1.5.0 has no RemoveItem(ItemDef, int) of its own", !inv.Methods.Any(Shim));
            CompatShield.CompatShieldPatcher.Patch(game);
            Check("preloader adds RemoveItem(ItemDef, int)", inv.Methods.Count(Shim) == 1);
            CompatShield.CompatShieldPatcher.Patch(game);
            Check("adding it twice changes nothing", inv.Methods.Count(Shim) == 1);
            var shim = inv.Methods.First(Shim);
            var body = shim.Body.Instructions;
            Check("shim forwards to RemoveItem2", body.Any(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "RemoveItem2"));
            Check("shim is public, instance, returns void", shim.IsPublic && !shim.IsStatic && shim.ReturnType.FullName == "System.Void");
            var modRefs = modDef.MainModule.GetMemberReferences().OfType<Mono.Cecil.MethodReference>()
                .Where(r => r.Name == "RemoveItem" && r.DeclaringType.FullName == "RoR2.Inventory" && r.Parameters.Count == 2 && r.Parameters[0].ParameterType.FullName == "RoR2.ItemDef").ToList();
            Check("the mod's dead reference has the shim's shape", modRefs.Count > 0 && modRefs.All(r => r.ReturnType.FullName == "System.Void" && r.Parameters[1].ParameterType.FullName == "System.Int32" && r.HasThis));
            using (var ms = new System.IO.MemoryStream()) {
                game.Write(ms);
                ms.Position = 0;
                using (var again = Mono.Cecil.AssemblyDefinition.ReadAssembly(ms))
                    Check("the patched game assembly still writes and reads", again.MainModule.GetType("RoR2.Inventory").Methods.Any(Shim));
            }
            var other = Mono.Cecil.AssemblyDefinition.CreateAssembly(new Mono.Cecil.AssemblyNameDefinition("NotTheGame", new Version(1, 0)), "NotTheGame", Mono.Cecil.ModuleKind.Dll);
            CompatShield.CompatShieldPatcher.Patch(other);
            Check("preloader ignores other assemblies", other.MainModule.Types.Count == 1);
            Check("preloader asks for RoR2.dll", CompatShield.CompatShieldPatcher.TargetDLLs.SequenceEqual(new[] { "RoR2.dll" }));
        }

        // Real assemblies from here on: the fixes run against the shipped IL.
        ResolveEventHandler resolve = (s, e) => {
            var n = new AssemblyName(e.Name).Name + ".dll";
            foreach (var dir in new[] { managedDir, System.IO.Path.GetDirectoryName(bubbetsPath) }) {
                var p = System.IO.Path.Combine(dir, n);
                if (System.IO.File.Exists(p)) return Assembly.LoadFrom(p);
            }
            var found = System.IO.Directory.GetFiles(System.IO.Path.Combine(profile, "plugins"), n, System.IO.SearchOption.AllDirectories).Concat(System.IO.Directory.GetFiles(System.IO.Path.Combine(profile, "core"), n, System.IO.SearchOption.AllDirectories)).FirstOrDefault();
            return found != null ? Assembly.LoadFrom(found) : null;
        };
        AppDomain.CurrentDomain.AssemblyResolve += resolve;
        Assembly ror2, bubbets;
        Type soles, plate;
        try {
            ror2 = Assembly.LoadFrom(ror2Path);
            bubbets = Assembly.LoadFrom(bubbetsPath);
            soles = bubbets.GetType("BubbetsItems.Items.JelliedSoles", true);
            plate = bubbets.GetType("BubbetsItems.Items.RepulsionPlateMk2", true);
        }
        catch (Exception e) {
            Console.WriteLine("SKIP  real assemblies won't load in the harness: " + e.GetBaseException().Message);
            return failed;
        }
        var harmony = new HarmonyLib.Harmony("shieldtest.bubbets");
        var ilTakeDamage = soles.GetMethod("IlTakeDamage", all);
        Check("JelliedSoles.IlTakeDamage is there", ilTakeDamage != null);
        var rewrite = fix.GetMethod("RewriteSoles", all);
        var original = HarmonyLib.PatchProcessor.GetOriginalInstructions(ilTakeDamage);
        int before = original.Count;
        Check("the rewrite matches the shipped Jellied Soles hook", (bool)rewrite.Invoke(null, new object[] { original }));
        Check("the rewrite adds five instructions", original.Count == before + 5);
        var broken = HarmonyLib.PatchProcessor.GetOriginalInstructions(ilTakeDamage);
        broken.RemoveAll(c => c.opcode == System.Reflection.Emit.OpCodes.Ldsfld && c.operand is FieldInfo f && f.Name == "Stloc");
        var copy = broken.Select(c => c.opcode).ToList();
        Check("an unexpected shape is refused", !(bool)rewrite.Invoke(null, new object[] { broken }));
        Check("a refused shape is left untouched", broken.Select(c => c.opcode).SequenceEqual(copy));

        Check("the rewrite picks MoveType.AfterLabel", (int)MonoMod.Cil.MoveType.AfterLabel == 1);
        fix.GetMethod("Apply", all).Invoke(null, new object[] { harmony, bubbets, ror2 });
        Check("Jellied Soles reports rewritten", (bool)fix.GetField("SolesRewritten", all).GetValue(null));

        // .NET Framework refuses the hook predicates (they name a private game struct; the game runtime does not check), so the real run below may SKIP.
        // This part walks the game IL the way the predicates do: adaptiveArmor, then the next ble, then where it lands.
        using (var game = Mono.Cecil.AssemblyDefinition.ReadAssembly(ror2Path, gameRead)) {
            var body = game.MainModule.GetType("RoR2.HealthComponent").Methods.First(m => m.Name == "TakeDamageProcess").Body;
            var ins = body.Instructions;
            int armor = -1;
            for (int i = 0; i < ins.Count && armor < 0; i++)
                if (ins[i].OpCode == Mono.Cecil.Cil.OpCodes.Ldfld && ins[i].Operand is Mono.Cecil.FieldReference f && f.Name == "adaptiveArmor") armor = i;
            Check("the game still reads adaptiveArmor", armor > 0);
            int ble = armor;
            while (ble < ins.Count && ins[ble].OpCode != Mono.Cecil.Cil.OpCodes.Ble && ins[ble].OpCode != Mono.Cecil.Cil.OpCodes.Ble_S) ble++;
            Check("a ble follows it", ble < ins.Count);
            if (ble < ins.Count) {
                var landing = (Mono.Cecil.Cil.Instruction)ins[ble].Operand;
                var local = landing.Operand as Mono.Cecil.Cil.VariableDefinition;
                Check("the branch lands on a struct being built, so the float the mod assumed is not there", landing.OpCode.Name.StartsWith("ldloca") && local != null && local.VariableType.FullName != "System.Single");
            }
        }

        // The whole chain on the real damage method: the mod's own manipulators, as Harmony will run them, over the game's IL.
        using (var game = Mono.Cecil.AssemblyDefinition.ReadAssembly(ror2Path, gameRead)) {
            var hc = game.MainModule.GetType("RoR2.HealthComponent").Methods.First(m => m.Name == "TakeDamageProcess");
            int count = hc.Body.Instructions.Count;
            try {
                using (var il = new MonoMod.Cil.ILContext(hc)) {
                    ilTakeDamage.Invoke(null, new object[] { il });
                    var list = hc.Body.Instructions;
                    int nop = -1;
                    for (int i = 0; i < list.Count - 1; i++)
                        if (list[i].OpCode == Mono.Cecil.Cil.OpCodes.Nop && list[i + 1].OpCode.Name.StartsWith("stloc") && i > 5 && list[i - 1].OpCode == Mono.Cecil.Cil.OpCodes.Call && list[i - 4].OpCode == Mono.Cecil.Cil.OpCodes.Ldarg_0 && list[i - 5].OpCode.Name.StartsWith("ldloc")) nop = i;
                    Check("Jellied Soles emits its hook on the real damage method", nop > 0);
                    if (nop > 0) {
                        var load = list[nop - 5];
                        var store = list[nop + 1];
                        var v = load.Operand as Mono.Cecil.Cil.VariableDefinition;
                        Check("the hook loads a float local", v != null && v.VariableType.FullName == "System.Single");
                        Check("the hook stores back into the same local", ReferenceEquals(store.Operand, v));
                        Check("the hook sits right before the DamageMacroData build", list[nop + 2].OpCode.Name.StartsWith("ldloca"));
                        Check("no branch skips the hook", !list.Any(x => x.Operand is MonoMod.Cil.ILLabel l && ReferenceEquals(l.Target, list[nop + 2])));
                        Check("branches land on the hook", list.Any(x => x.Operand is MonoMod.Cil.ILLabel l && ReferenceEquals(l.Target, load)));
                    }
                }
            }
            catch (Exception e) { Console.WriteLine("SKIP  Jellied Soles real IL run: " + e.GetBaseException().GetType().Name + " " + e.GetBaseException().Message); }
        }
        using (var game = Mono.Cecil.AssemblyDefinition.ReadAssembly(ror2Path, gameRead)) {
            var hc = game.MainModule.GetType("RoR2.HealthComponent").Methods.First(m => m.Name == "TakeDamageProcess");
            try {
                using (var il = new MonoMod.Cil.ILContext(hc)) {
                    plate.GetMethod("TakeDamageHook", all).Invoke(null, new object[] { il });
                    var list = hc.Body.Instructions;
                    int call = -1;
                    for (int i = 0; i < list.Count; i++)
                        if (list[i].OpCode == Mono.Cecil.Cil.OpCodes.Call && list[i].Operand is Mono.Cecil.MethodReference r && r.Name == "DoMk2ArmorPlates") call = i;
                    Check("Repulsion Plate hook lands on the real damage method", call > 1);
                    Check("Repulsion Plate hook ran once", (int)fix.GetField("PlateHooked", all).GetValue(null) == 1);
                    if (call > 1) {
                        Check("it follows a Mathf.Max", list[call - 2].Operand is Mono.Cecil.MethodReference r2 && r2.Name == "Max");
                        var next = list[call + 1];
                        var v = next.Operand as Mono.Cecil.Cil.VariableDefinition;
                        Check("its result goes into the local the armor plate line reads", next.OpCode.Name.StartsWith("stloc") && v != null && v.Index == 12);
                    }
                }
            }
            catch (Exception e) { Console.WriteLine("SKIP  Repulsion Plate real IL run: " + e.GetBaseException().GetType().Name + " " + e.GetBaseException().Message); }
        }

        // Member lookups for the fixes that wait for a run. The patches themselves can only be proved in the game.
        var inventoryT = ror2.GetType("RoR2.Inventory");
        var methods = inventoryT.GetMethods(all);
        var equipTarget = methods.FirstOrDefault(m => m.Name == "SetEquipmentInternal" && m.GetParameters().Length == 2 && m.GetParameters().All(p => p.ParameterType.IsByRef));
        Check("SetEquipmentInternal(in, in) exists", equipTarget != null);
        var eqBase = bubbets.GetType("BubbetsItems.EquipmentBase");
        Check("EquipmentBase members", eqBase.GetProperty("Equipments", all) != null && (eqBase.GetField("EquipmentDef", all) != null || eqBase.GetProperty("EquipmentDef", all) != null)
            && eqBase.GetMethod("OnEquip", all) != null && eqBase.GetMethod("OnUnEquip", all) != null && eqBase.GetMethod("OnEquipmentSwap", all) != null);
        Check("three callers of SetEquipmentSlotAndSet", eqBase.GetMethods(all).Count(m => m.Name == "CmdSetEquipmentSlotAndSet" || m.Name == "CmdExecuteEquipmentSlotAndSet" || (m.Name == "SetEquipmentSlotAndSet" && m.GetParameters().Length == 1)) == 3);
        Check("TrySetActiveEquipmentLocation exists", methods.Any(m => m.Name == "TrySetActiveEquipmentLocation" && m.GetParameters().Length == 1));
        Check("EquipmentSlot.inventory exists", ror2.GetType("RoR2.EquipmentSlot").GetField("inventory", all) != null);
        var loc = ror2.GetType("RoR2.EquipmentLocation");
        Check("EquipmentLocation slot and set are uint", loc.GetField("slot", all)?.FieldType == typeof(uint) && loc.GetField("set", all)?.FieldType == typeof(uint));
        Check("GetEquipment returns a plain EquipmentState", methods.Any(m => m.Name == "GetEquipment" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsByRef && !m.ReturnType.IsByRef));
        var barData = bubbets.GetType("BubbetsItems.Behaviours.ExtraHealthBarSegments+BarData");
        var slug = bubbets.GetType("BubbetsItems.Items.BarrierItems.EternalSlug+SlugData");
        Check("BarData and SlugData found", barData != null && slug != null);
        var barField = barData?.GetField("Bar", all);
        var styleProp = barField?.FieldType.GetProperty("style", all);
        Check("HealthBar.style is a property", styleProp != null);
        Check("style carries both bar styles", styleProp?.PropertyType.GetField("barrierBarStyle", all) != null && styleProp?.PropertyType.GetField("lowHealthOverStyle", all) != null);
        Check("BarStyle.sizeDelta exists", barData?.GetMethod("GetStyle", all)?.ReturnType.GetField("sizeDelta", all) != null);
        var scrap = bubbets.GetType("BubbetsItems.Items.VoidJunkToScrapTier1");
        var pay = ror2.GetType("RoR2.CostTypeCatalog").GetMethods(all).FirstOrDefault(m => m.IsStatic && m.Name.StartsWith("<Init>g__PayCostItems|") && m.GetParameters().Length == 2);
        var prefix = scrap?.GetMethod("PrefixItemPayCost", all);
        Check("the game's PayCostItems is found by its new name", pay != null);
        Check("Void Scrap's prefix parameters match it", pay != null && prefix != null && pay.GetParameters().Select(p => p.Name + p.ParameterType).SequenceEqual(prefix.GetParameters().Select(p => p.Name + p.ParameterType)));
        AppDomain.CurrentDomain.AssemblyResolve -= resolve;
        Console.WriteLine(failed == 0 ? "bubbets: all passed" : "bubbets: " + failed + " failed");
        return failed;
    }

    static int RouletteCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var fix = shieldType.Assembly.GetType("CompatShield.RouletteSpinCost");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        int NewPool(uint before, uint after, int poolBefore, int poolNow) => (int)fix.GetMethod("NewPool", all).Invoke(null, new object[] { before, after, poolBefore, poolNow });
        bool Patch(Type spin, Type master, Type pool, Type general) => (bool)fix.GetMethod("Patch", all).Invoke(null, new object[] { new HarmonyLib.Harmony("roulette-test"), spin, master, pool, general });
        bool Warned(string text) => logs.Count(l => l.Contains(text)) == 1;
        void Reset() { fix.GetMethod("ResetForTest", all).Invoke(null, null); logs.Clear(); }

        Check("roulette: a paid spin comes out of the pool", NewPool(100, 50, 100, 100) == 50);
        Check("roulette: a free spin leaves the pool alone", NewPool(100, 100, 100, 100) == 100);
        Check("roulette: gold that went up leaves the pool alone", NewPool(100, 150, 100, 100) == 100);
        Check("roulette: a pool something else already charged isn't charged twice", NewPool(100, 50, 100, 50) == 50);
        Check("roulette: a pool that moved for another reason is left alone", NewPool(100, 50, 100, 70) == 70);
        Check("roulette: the pool can't go below zero", NewPool(100, 50, 30, 30) == 0);

        // What it does when a mod isn't what it expects. These return before patching anything.
        Reset();
        Check("roulette: no ItemRoulette type is left alone and warned about", !Patch(null, typeof(FakeRouletteMaster), typeof(FakeRoulettePool), typeof(FakeRouletteGeneral)) && Warned("not applied"));
        Reset();
        Check("roulette: a renamed spin method is left alone and warned about", !Patch(typeof(FakeRouletteSpinRenamed), typeof(FakeRouletteMaster), typeof(FakeRoulettePool), typeof(FakeRouletteGeneral)) && Warned("not applied"));
        Reset();
        Check("roulette: a spin method that doesn't take the player first is left alone", !Patch(typeof(FakeRouletteSpinWrongFirst), typeof(FakeRouletteMaster), typeof(FakeRoulettePool), typeof(FakeRouletteGeneral)) && Warned("not applied"));
        Reset();
        Check("roulette: a pool of another type is left alone", !Patch(typeof(FakeRouletteSpin), typeof(FakeRouletteMaster), typeof(FakeRoulettePoolLong), typeof(FakeRouletteGeneral)) && Warned("not applied"));
        Reset();
        Check("roulette: no shared-money switch is left alone", !Patch(typeof(FakeRouletteSpin), typeof(FakeRouletteMaster), typeof(FakeRoulettePoolNoSwitch), typeof(FakeRouletteGeneral)) && Warned("not applied"));
        Reset();
        Check("roulette: no multiplayer check is left alone", !Patch(typeof(FakeRouletteSpin), typeof(FakeRouletteMaster), typeof(FakeRoulettePool), typeof(FakeRouletteGeneralEmpty)) && Warned("not applied"));
        Reset();
        Check("roulette: gold that isn't a uint is left alone", !Patch(typeof(FakeRouletteSpin), typeof(FakeRouletteMasterIntMoney), typeof(FakeRoulettePool), typeof(FakeRouletteGeneral)) && Warned("not applied"));

        // The real patch on a stand-in spin, with ShareSuite's next-frame sync played by hand.
        Reset();
        Check("roulette: patches when both mods are the expected shape", Patch(typeof(FakeRouletteSpin), typeof(FakeRouletteMaster), typeof(FakeRoulettePool), typeof(FakeRouletteGeneral)));
        Check("roulette: says so in the log, with nothing to warn about", Warned("paid spins now come out of ShareSuite's shared gold") && !logs.Exists(l => l.Contains("not applied")));
        var spin = typeof(FakeRouletteSpin).GetMethod("ExecuteSpinOnServer", all);
        var player = new FakeRouletteMaster();
        void SetUp(int gold, uint price) { FakeRoulettePool.SharedMoneyValue = gold; player.money = (uint)gold; FakeRouletteSpin.Price = price; FakeRoulettePool.On = true; FakeRouletteGeneral.Multi = true; FakeRouletteSpin.ChargesPoolItself = false; logs.Clear(); }
        void Spin() => spin.Invoke(null, new object[] { player, null });
        void NextFrame() => player.money = (uint)FakeRoulettePool.SharedMoneyValue;

        SetUp(200, 50); Spin(); NextFrame();
        Check("roulette: a spin's price stays gone after ShareSuite's sync", player.money == 150 && FakeRoulettePool.SharedMoneyValue == 150);
        Check("roulette: each spin is logged with the price and what's left", Warned("Roulette: $50 charged to the shared gold pool, $150 left"));
        Spin(); NextFrame();
        Check("roulette: a second spin charges again", player.money == 100 && FakeRoulettePool.SharedMoneyValue == 100);
        SetUp(40, 50); Spin(); NextFrame();
        Check("roulette: a spin you can't afford costs nothing and logs nothing", player.money == 40 && FakeRoulettePool.SharedMoneyValue == 40 && logs.Count == 0);
        SetUp(200, 0); Spin(); NextFrame();
        Check("roulette: a free spin costs nothing and logs nothing", player.money == 200 && FakeRoulettePool.SharedMoneyValue == 200 && logs.Count == 0);
        SetUp(200, 50); FakeRoulettePool.On = false; Spin();
        Check("roulette: with shared money off the pool isn't touched", player.money == 150 && FakeRoulettePool.SharedMoneyValue == 200 && logs.Count == 0);
        SetUp(200, 50); FakeRouletteGeneral.Multi = false; Spin();
        Check("roulette: in single player the pool isn't touched", player.money == 150 && FakeRoulettePool.SharedMoneyValue == 200 && logs.Count == 0);
        SetUp(200, 50); FakeRouletteSpin.ChargesPoolItself = true; Spin(); NextFrame();
        Check("roulette: a future ItemRoulette that charges the pool itself isn't charged twice", player.money == 150 && FakeRoulettePool.SharedMoneyValue == 150);
        SetUp(200, 50);
        Exception spinError = null;
        try { spin.Invoke(null, new object[] { null, null }); } catch (Exception e) { spinError = e; }
        Check("roulette: no player is ignored without an error", spinError == null && FakeRoulettePool.SharedMoneyValue == 200 && logs.Count == 0);

        // Nothing may run a game type's static constructor on the load path.
        var shieldDll = Mono.Cecil.ModuleDefinition.ReadModule(shieldType.Assembly.Location);
        var fixDef = shieldDll.GetType("CompatShield.RouletteSpinCost");
        bool Calls(Mono.Cecil.MethodDefinition m, string name) => m.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == name);
        var applyDef = fixDef.Methods.First(m => m.Name == "Apply"); var startDef = fixDef.Methods.First(m => m.Name == "OnRunStart");
        Check("roulette: Apply (on the load path) subscribes to run starts and never patches", Calls(applyDef, "AddEventHandler") && !Calls(applyDef, "Patch"));
        Check("roulette: the patch happens from the run-start handler", Calls(startDef, "Patch"));

        // The real mods.
        var plugins = System.IO.Path.Combine(profile, "plugins");
        var managed = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var ror2 = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var moneyProp = ror2.GetType("RoR2.CharacterMaster").Properties.FirstOrDefault(p => p.Name == "money");
        Check("roulette real: CharacterMaster.money is a uint property with a getter", moneyProp != null && moneyProp.PropertyType.FullName == "System.UInt32" && moneyProp.GetMethod != null);
        Check("roulette real: Run has onRunStartGlobal", ror2.GetType("RoR2.Run").Events.Any(e => e.Name == "onRunStartGlobal"));
        var rouletteDll = System.IO.Path.Combine(plugins, "HorinisTeam-ItemRoulette", "ItemRoulette.dll");
        var shareDll = System.IO.Path.Combine(plugins, "FunkFrog-and-Sipondo-ShareSuite", "ShareSuite", "ShareSuite.dll");
        if (!System.IO.File.Exists(rouletteDll) || !System.IO.File.Exists(shareDll)) { Console.WriteLine("SKIP  roulette real: ItemRoulette or ShareSuite isn't installed"); return failures == 0 ? 0 : 1; }
        var roulette = Mono.Cecil.ModuleDefinition.ReadModule(rouletteDll);
        var share = Mono.Cecil.ModuleDefinition.ReadModule(shareDll);
        var spinDef = roulette.GetType("ItemRoulette.Core.ServerSpinService")?.Methods.Where(m => m.Name == "ExecuteSpinOnServer").ToList();
        Check("roulette real: ItemRoulette has one ExecuteSpinOnServer taking the CharacterMaster first", spinDef != null && spinDef.Count == 1 && spinDef[0].Parameters.Count > 0 && spinDef[0].Parameters[0].ParameterType.FullName == "RoR2.CharacterMaster");
        Check("roulette real: the spin method still takes gold from the player", spinDef != null && spinDef.Count == 1 && Calls(spinDef[0], "set_money"));
        var poolDef = share.GetType("ShareSuite.MoneySharingHooks");
        Check("roulette real: ShareSuite's SharedMoneyValue is a static int", poolDef.Fields.Any(f => f.Name == "SharedMoneyValue" && f.IsStatic && f.FieldType.FullName == "System.Int32"));
        Check("roulette real: ShareSuite has SharedMoneyEnabled()", poolDef.Methods.Any(m => m.Name == "SharedMoneyEnabled" && m.IsStatic && m.Parameters.Count == 0 && m.ReturnType.FullName == "System.Boolean"));
        Check("roulette real: ShareSuite has IsMultiplayer()", share.GetType("ShareSuite.GeneralHooks").Methods.Any(m => m.Name == "IsMultiplayer" && m.IsStatic && m.Parameters.Count == 0 && m.ReturnType.FullName == "System.Boolean"));
        Check("roulette real: ShareSuite's Update still writes the pool over player gold (the reason for this fix)", share.Types.SelectMany(t => t.Methods).Any(m => m.Name == "Update" && m.HasBody && Calls(m, "set_money") && m.Body.Instructions.Any(i => i.Operand is Mono.Cecil.FieldReference f && f.Name == "SharedMoneyValue")));
        return failures == 0 ? 0 : 1;
    }

    static int BlastCapCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var cap = shieldType.Assembly.GetType("CompatShield.BlastChainCap");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var prefix = cap.GetMethod("Prefix", all); var fin = cap.GetMethod("Finalizer", all);
        double clockMs = 0;   // a hand-driven clock, so the time limit never depends on how fast this machine runs the test
        cap.GetField("NowMs", all).SetValue(null, new Func<double>(() => clockMs));
        cap.GetField("AttackerName", all).SetValue(null, new Func<object, string>(o => o is FakeBlast f ? "level" + f.level : (string)o));
        void Reset(int maxDepth, float budgetMs) { cap.GetMethod("ResetForTest", all).Invoke(null, null); cap.GetMethod("Configure", all).Invoke(null, new object[] { maxDepth, budgetMs }); clockMs = 0; logs.Clear(); }
        int Depth() => (int)cap.GetProperty("Depth", all).GetValue(null);
        bool Pre(string who, out bool state) { var a = new object[] { who, false }; bool r = (bool)prefix.Invoke(null, a); state = (bool)a[1]; return r; }
        void Fin(bool state) => fin.Invoke(null, new object[] { state });
        System.Collections.Generic.List<string> Chains() => logs.Where(l => l.Contains("Blast chain: stopped")).ToList();

        // Depth: the outer blast plus 3 inside it run; the next ones are stopped and never counted.
        Reset(4, 100f);
        var ran = new System.Collections.Generic.List<bool>(); var states = new System.Collections.Generic.List<bool>();
        for (int i = 0; i < 6; i++) { ran.Add(Pre("blast" + i, out bool st)); states.Add(st); }
        Check("blastcap: 4 blasts inside each other run; the 5th and 6th are stopped", ran.Take(4).All(x => x) && !ran[4] && !ran[5] && Depth() == 4);
        Check("blastcap: a blast that ran is marked for its finalizer, a stopped one is not", states.Take(4).All(x => x) && !states[4] && !states[5]);
        Fin(false); Fin(false);
        Check("blastcap: a stopped blast's finalizer changes nothing", Depth() == 4 && Chains().Count == 0);
        Fin(true); Fin(true); Fin(true);
        Check("blastcap: nothing is logged until the outermost blast returns", Depth() == 1 && Chains().Count == 0);
        Fin(true);
        Check("blastcap: when the outermost blast returns, one line says what was stopped and whose blast was first",
            Depth() == 0 && Chains().Count == 1 && Chains()[0].Contains("stopped 2 nested blasts") &&Chains()[0].Contains("(2 too deep, 0 too late)") && Chains()[0].Contains("3 ran") && Chains()[0].Contains("came from blast4"));

        // Counters start clean for the next chain, and a chain that lost nothing logs nothing.
        logs.Clear();
        Pre("root", out bool s1); Pre("inner", out bool s2); Fin(s2); Fin(s1);
        Check("blastcap: a chain that stopped nothing logs nothing, and the last chain's counts are gone", Chains().Count == 0 && Depth() == 0);
        Pre("root", out s1); for (int i = 0; i < 3; i++) Pre("inner", out s2);
        Pre("deep", out bool s3); Fin(s3);
        Fin(true); Fin(true); Fin(true); Fin(true);
        Check("blastcap: the next chain's line counts only its own blasts", Chains().Count == 1 && Chains()[0].Contains("stopped 1 nested blast (") &&Chains()[0].Contains("came from deep"));

        // A blast that isn't inside another always runs, even at the tightest settings and long after any earlier chain.
        Reset(1, 100f);
        clockMs = 1000000;
        bool rootRan = Pre("root", out s1); bool innerRan = Pre("inner", out s2);
        Check("blastcap: at depth limit 1 the outer blast runs and any blast inside it is stopped", rootRan && s1 && !innerRan && !s2 && Depth() == 1);
        Fin(s2); Fin(s1);

        // Time limit: blasts 2 or more deep stop when the allowance runs out, measured from the outermost blast, boundary included.
        Reset(10, 100f);
        Pre("root", out s1); Pre("level1", out bool sLevel1);
        clockMs = 99.9; bool early = Pre("early", out s2); Fin(s2);
        clockMs = 100; bool atLimit = Pre("atlimit", out s2); bool stateAtLimit = s2; Fin(s2);
        clockMs = 500; bool late = Pre("late", out s2); Fin(s2);
        Check("blastcap: a blast 2 deep runs before the allowance is gone and is stopped at and after it", early && !atLimit && !stateAtLimit && !late && Depth() == 2);
        Fin(sLevel1); Fin(s1);
        Check("blastcap: time-stopped blasts are counted as too late, and the line gives the chain's length", Chains().Count == 1 && Chains()[0].Contains("(0 too deep, 2 too late)") && Chains()[0].Contains("took 500 ms") && Chains()[0].Contains("came from atlimit"));

        // A blast directly inside the outermost one is ordinary fan-out (Brilliant Behemoth), so it gets far more room.
        Reset(10, 100f);
        Pre("root", out s1);
        clockMs = 399.9; bool fanEarly = Pre("fan", out s2); Fin(s2);
        clockMs = 400; bool fanLate = Pre("fanlate", out s2); Fin(s2);
        Check("blastcap: a blast directly inside the outermost one runs until the allowance is 3 times overdrawn", fanEarly && !fanLate && Depth() == 1);
        Fin(s1);

        // The allowance is shared: a chain that used it up leaves less for the next, until time passes between chains.
        Reset(10, 100f);
        Pre("root", out s1); Pre("level1", out sLevel1); clockMs = 150; Fin(sLevel1); Fin(s1);
        Pre("root2", out s1); Pre("level1b", out sLevel1); bool sharedStopped = !Pre("deep", out s2); Fin(s2); Fin(sLevel1); Fin(s1);
        Check("blastcap: the next chain starts with what the last one left, not a fresh allowance", sharedStopped && Depth() == 0);
        clockMs = 349;
        Pre("root3", out s1); Pre("level1c", out sLevel1); bool stillDry = !Pre("deep3", out s2); Fin(s2); Fin(sLevel1); Fin(s1);
        clockMs = 353;
        Pre("root4", out s1); Pre("level1d", out sLevel1); bool refilled = Pre("deep4", out s2); Fin(s2); Fin(sLevel1); Fin(s1);
        Check("blastcap: the allowance refills at a quarter of the time between chains", stillDry && refilled);
        clockMs = 10000000;
        Pre("root5", out s1); Pre("level1e", out sLevel1);
        clockMs = 10000099.5; bool fullEarly = Pre("deep5", out s2); Fin(s2);
        clockMs = 10000100; bool fullLate = Pre("deep6", out s2); Fin(s2); Fin(sLevel1); Fin(s1);
        Check("blastcap: the allowance never builds past its full size", fullEarly && !fullLate);

        Reset(10, 100f);
        Pre("plain", out s1); clockMs = 200; Fin(s1);
        Pre("root", out s1); Pre("level1", out sLevel1); bool plainFree = Pre("deep", out s2); Fin(s2); Fin(sLevel1); Fin(s1);
        Check("blastcap: a blast with nothing inside it doesn't use the allowance", plainFree);

        Reset(10, 100f);
        Pre("root", out s1); Pre("level1", out sLevel1); clockMs = 1000000; Fin(sLevel1); Fin(s1);
        clockMs = 1001599;
        Pre("root2", out s1); Pre("level1b", out sLevel1); bool hugeStillOff = !Pre("deep", out s2); Fin(s2); Fin(sLevel1); Fin(s1);
        clockMs = 1001603;
        Pre("root3", out s1); Pre("level1c", out sLevel1); bool hugeBackOn = Pre("deep", out s2); Fin(s2); Fin(sLevel1); Fin(s1);
        Check("blastcap: one huge chain shuts deep blasts off for at most 1.6 s of time between chains", hugeStillOff && hugeBackOn);

        Reset(10, 0f);
        Pre("root", out s1); Pre("level1", out sLevel1); clockMs = 1e9; bool noLimit = Pre("inner", out s2); Fin(s2); Fin(sLevel1); Fin(s1);
        Check("blastcap: time limit 0 means no time limit", noLimit && Chains().Count == 0);

        // The log can't flood.
        Reset(1, 0f);
        for (int i = 0; i < 250; i++) { Pre("root", out s1); Pre("inner", out s2); Fin(s2); Fin(s1); }
        Check("blastcap: at most 200 chain lines per session", Chains().Count == 200);

        cap.GetMethod("Configure", all).Invoke(null, new object[] { 0, -5f });
        Check("blastcap: settings below the floor become depth 1 and no time limit", (int)cap.GetField("MaxDepth", all).GetValue(null) == 1 && (double)cap.GetField("BudgetMs", all).GetValue(null) == 0);

        // The patch binds to a real method: nested fake blasts go through Harmony's generated wrapper.
        Reset(4, 0f);
        var h = new Harmony("test.blastcap");
        var patch = cap.GetMethod("PatchBlast", all);
        bool Patched(Type t) => (bool)patch.Invoke(null, new object[] { h, t });
        Check("blastcap: the patch binds to HandleHits(HitPoint[]) and logs its one line", Patched(typeof(FakeBlast)) && logs.Count(l => l.Contains("stops firing once 4 are running inside each other, so item combos can't hang the game")) == 1);
        FakeBlast.Ran = 0;
        FakeBlast.OnHits = b => { if (b.level < 8) new FakeBlast { level = b.level + 1 }.HandleHits(new FakeBlast.HitPoint[0]); };
        new FakeBlast().HandleHits(new FakeBlast.HitPoint[0]);
        Check("blastcap: through Harmony, a loop of blasts stops at the depth limit and the count returns to 0", FakeBlast.Ran == 4 && Depth() == 0);
        Check("blastcap: Harmony passes the blast itself, so the line names the first one stopped", Chains().Count == 1 && Chains()[0].Contains("came from level4"));
        FakeBlast.Ran = 0;
        FakeBlast.OnHits = b => { for (int i = 0; i < 3 && b.level < 3; i++) new FakeBlast { level = b.level + 1 }.HandleHits(new FakeBlast.HitPoint[0]); };
        new FakeBlast().HandleHits(new FakeBlast.HitPoint[0]);
        Check("blastcap: several blasts side by side at one level all run (only depth is limited here)", FakeBlast.Ran == 1 + 3 + 9 + 27 && Depth() == 0);
        FakeBlast.OnHits = b => { if (b.level < 2) new FakeBlast { level = b.level + 1 }.HandleHits(new FakeBlast.HitPoint[0]); else throw new InvalidOperationException("boom"); };
        Exception thrown = null;
        try { new FakeBlast().HandleHits(new FakeBlast.HitPoint[0]); } catch (Exception e) { thrown = e; }
        Check("blastcap: an exception inside a blast still reaches the caller and the count returns to 0", thrown is InvalidOperationException && Depth() == 0);
        FakeBlast.OnHits = null;

        // Hits waiting in DamageChainGuard's line: each replay's blast starts with no blast on the stack, and all of them share one allowance.
        var guard = shieldType.Assembly.GetType("CompatShield.DamageChainGuard");
        guard.GetMethod("ResetForTest", all).Invoke(null, null);
        guard.GetMethod("Configure", all).Invoke(null, new object[] { 256, 0f });
        guard.GetField("NowMs", all).SetValue(null, new Func<double>(() => clockMs));
        guard.GetField("IsGone", all).SetValue(null, new Func<object, bool>(o => false));
        guard.GetField("Replay", all).SetValue(null, new Action<object, object>((self, info) => new FakeBlast().HandleHits(new FakeBlast.HitPoint[0])));
        int guardDepth = (int)guard.GetField("MaxDepth", all).GetValue(null);
        bool GuardPre(string who) => (bool)guard.GetMethod("Prefix", all).Invoke(null, new object[] { who, "hit-" + who, false });
        void GuardFin(bool state) => guard.GetMethod("Finalizer", all).Invoke(null, new object[] { state });
        Reset(8, 100f);
        FakeBlast.Ran = 0;
        FakeBlast.OnHits = b => { clockMs += 30; if (b.level < 6) new FakeBlast { level = b.level + 1 }.HandleHits(new FakeBlast.HitPoint[0]); };
        for (int i = 0; i < guardDepth + 8; i++) GuardPre("hc" + i);
        for (int i = 0; i < 8; i++) GuardFin(false);
        for (int i = 0; i < guardDepth; i++) GuardFin(true);
        Check("blastcap: 8 hits replayed from the damage chain guard's line share one allowance (4 levels, then 2, then only the outer blast; 32 blasts without sharing)", FakeBlast.Ran == 16 && Depth() == 0 && (int)guard.GetProperty("QueuedCount", all).GetValue(null) == 0);
        FakeBlast.OnHits = null;

        // The run-start handler: one try per session, and a failure is a warning, because an exception would stop other mods' run-start handlers.
        Reset(4, 0f);
        cap.GetField("patcher", all).SetValue(null, h);
        cap.GetField("game", all).SetValue(null, typeof(FakeBlast).Assembly);   // has no RoR2.BlastAttack
        var onStart = cap.GetMethod("OnRunStart", all);
        onStart.Invoke(null, new object[] { null }); onStart.Invoke(null, new object[] { null });
        Check("blastcap: the run-start handler tries once, and a missing BlastAttack is one warning", logs.Count(l => l.Contains("not applied")) == 1);
        Reset(4, 0f);
        cap.GetField("game", all).SetValue(null, null);
        Exception startError = null;
        try { onStart.Invoke(null, new object[] { null }); } catch (Exception e) { startError = e; }
        Check("blastcap: an error in the run-start handler is caught and warned about", startError == null && logs.Count(l => l.Contains("can't patch BlastAttack, not applied")) == 1);

        logs.Clear();
        Check("blastcap: a static HandleHits is left alone and warned about", !Patched(typeof(FakeStaticBlast)) && logs.Count(l => l.Contains("not applied")) == 1);
        Check("blastcap: a blast type without HitPoint is left alone", !Patched(typeof(FakeNoHitPointBlast)));
        Check("blastcap: a missing BlastAttack type is left alone", !Patched(null));

        // The load path may only subscribe; patching there can run the game type's static constructor.
        var shieldDll = Mono.Cecil.ModuleDefinition.ReadModule(shieldType.Assembly.Location);
        var capDef = shieldDll.GetType("CompatShield.BlastChainCap");
        bool Calls(Mono.Cecil.MethodDefinition m, string name) => m.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == name);
        var applyDef = capDef.Methods.First(m => m.Name == "Apply"); var startDef = capDef.Methods.First(m => m.Name == "OnRunStart");
        Check("blastcap: Apply (on the load path) subscribes to run starts and never patches", Calls(applyDef, "AddEventHandler") && !Calls(applyDef, "Patch") && !Calls(applyDef, "PatchBlast"));
        Check("blastcap: the patch happens from the run-start handler", Calls(startDef, "PatchBlast"));

        // The real game members.
        var managed = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var ror2 = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var blast = ror2.GetType("RoR2.BlastAttack");
        var handle = blast?.Methods.FirstOrDefault(m => m.Name == "HandleHits" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.IsArray && m.Parameters[0].ParameterType.GetElementType().Name == "HitPoint");
        Check("blastcap real: game 1.5.0 has an instance, void BlastAttack.HandleHits(HitPoint[])", handle != null && !handle.IsStatic && handle.ReturnType.FullName == "System.Void");
        Check("blastcap real: BlastAttack has the attacker field and Run has onRunStartGlobal", blast.Fields.Any(f => f.Name == "attacker") && ror2.GetType("RoR2.Run").Events.Any(e => e.Name == "onRunStartGlobal"));
        var callers = new System.Collections.Generic.List<string>();
        foreach (var t in ror2.GetTypes()) foreach (var m in t.Methods) { if (!m.HasBody) continue; foreach (var i in m.Body.Instructions) if (i.Operand is Mono.Cecil.MethodReference r && r.DeclaringType.FullName == "RoR2.BlastAttack" && (r.Name == "HandleHits" || r.Name == "PerformDamageServer")) callers.Add(t.Name + "." + m.Name + ">" + r.Name); }
        Check("blastcap real: Fire is the only caller of HandleHits, and PerformDamageServer's only callers are HandleHits, the client-report message and Providence's mines",
            callers.Count(c => c.EndsWith(">HandleHits")) == 1 && callers.Contains("BlastAttack.Fire>HandleHits") && callers.Count(c => c.EndsWith(">PerformDamageServer")) == 3 && callers.Contains("BlastAttack.HandleHits>PerformDamageServer") && callers.Contains("BlastAttack.HandleReportBlastAttackDamage>PerformDamageServer") && callers.Contains("ProvidenceLocalMineController.DetonateMineLocal>PerformDamageServer"));
        return failures == 0 ? 0 : 1;
    }

    static int ChainCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var guard = shieldType.Assembly.GetType("CompatShield.DamageChainGuard");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        guard.GetMethod("ResetForTest", all).Invoke(null, null);
        var replayed = new System.Collections.Generic.List<string>();
        var prefix = guard.GetMethod("Prefix", all); var fin = guard.GetMethod("Finalizer", all);
        bool Pre(object self, object info) { var a = new object[] { self, info, false }; bool r = (bool)prefix.Invoke(null, a); return r; }
        void Fin(bool state) => fin.Invoke(null, new object[] { state });
        int Depth() => (int)guard.GetProperty("Depth", all).GetValue(null);
        int Queued() => (int)guard.GetProperty("QueuedCount", all).GetValue(null);
        guard.GetField("IsGone", all).SetValue(null, new Func<object, bool>(o => (o as string) == "gone"));
        var depthsSeen = new System.Collections.Generic.List<int>();
        guard.GetField("Replay", all).SetValue(null, new Action<object, object>((s, i) => { replayed.Add(s + ":" + i); depthsSeen.Add(Depth()); }));
        int maxDepth = (int)guard.GetField("MaxDepth", all).GetValue(null);
        double clockMs = 0;   // a hand-driven clock, so the time limit never depends on how fast this machine runs the test
        guard.GetField("NowMs", all).SetValue(null, new Func<double>(() => clockMs));

        // A chain MaxDepth+2 deep: the first MaxDepth links run inline, the next two are queued, and they run once the outermost returns.
        var ran = new System.Collections.Generic.List<bool>();
        for (int i = 0; i < maxDepth + 2; i++) ran.Add(Pre("hc" + i, "hit" + i));
        Check($"chain: the first {maxDepth} nested hits run inline, the two beyond are skipped and queued",
            ran.Take(maxDepth).All(x => x) && !ran[maxDepth] && !ran[maxDepth + 1] && Depth() == maxDepth && Queued() == 2 && replayed.Count == 0);
        Fin(false); Fin(false);
        Check("chain: a skipped hit's finalizer changes nothing", Depth() == maxDepth && Queued() == 2);
        for (int i = 0; i < maxDepth - 1; i++) Fin(true);
        Check("chain: nothing is replayed until the outermost hit returns", Depth() == 1 && replayed.Count == 0);
        Fin(true);
        Check("chain: when the outermost hit returns the queued hits run in order through the replay, and the queue is empty",
            Depth() == 0 && Queued() == 0 && replayed.Count == 2 && replayed[0] == $"hc{maxDepth}:hit{maxDepth}" && replayed[1] == $"hc{maxDepth + 1}:hit{maxDepth + 1}");
        Check("chain: the first deferral is logged once", logs.Count(l => l.Contains("was queued to run after the outer hit")) == 1);
        Check("chain: a replay runs as link 1 (the outermost hit's outer hooks are still under it), and depth is 0 again afterwards", depthsSeen.Count == 2 && depthsSeen.All(d => d == 1) && Depth() == 0);

        // Replays that nest: a replayed hit that itself goes MaxDepth+1 deep queues into the same drain loop.
        replayed.Clear();
        guard.GetField("Replay", all).SetValue(null, new Action<object, object>((s, i) => {
            replayed.Add(s + ":" + i);
            if ((string)i == "deep") { int trues = 0; for (int k = 0; k <= maxDepth; k++) if (Pre("n" + k, "nested" + k)) trues++; for (int k = 0; k < trues; k++) Fin(true); }
        }));
        for (int i = 0; i < maxDepth; i++) Pre("a" + i, "x");
        Pre("gone", "lost"); Pre("b", "deep");
        for (int i = 0; i < maxDepth; i++) Fin(true);
        Check("chain: a destroyed target is skipped, a replay's own deep chain (starting as link 1) queues its 3rd and 4th links into the same loop, and the loop ends empty",
            Depth() == 0 && Queued() == 0 && replayed.Count == 3 && replayed[0] == "b:deep" && replayed[1] == $"n{maxDepth - 1}:nested{maxDepth - 1}" && replayed[2] == $"n{maxDepth}:nested{maxDepth}");

        // Cap: beyond QueueLimit the chain is dropped with one warning.
        int cap = (int)guard.GetField("QueueLimit", all).GetValue(null);
        guard.GetMethod("ResetForTest", all).Invoke(null, null); replayed.Clear(); logs.Clear();
        guard.GetField("Replay", all).SetValue(null, new Action<object, object>((s, i) => replayed.Add("r")));
        for (int i = 0; i < maxDepth; i++) Pre("c" + i, "x");
        for (int i = 0; i < cap + 5; i++) Pre("d", "y");
        Check("chain: the queue stops at the cap and warns once", Queued() == cap && logs.Count(l => l.Contains("the rest of this chain is dropped")) == 1);
        for (int i = 0; i < maxDepth; i++) Fin(true);
        Check("chain: a capped queue still drains fully", replayed.Count == cap && Queued() == 0);
        // A replay that throws is logged and the rest still runs.
        guard.GetMethod("ResetForTest", all).Invoke(null, null); replayed.Clear(); logs.Clear();
        guard.GetField("Replay", all).SetValue(null, new Action<object, object>((s, i) => { if ((string)i == "boom") throw new InvalidOperationException("boom"); replayed.Add("ok"); }));
        for (int i = 0; i < maxDepth; i++) Pre("e" + i, "x");
        Pre("f", "boom"); Pre("g", "fine");
        for (int i = 0; i < maxDepth; i++) Fin(true);
        Check("chain: a queued hit that throws is logged and the next one still runs", replayed.Count == 1 && Depth() == 0 && logs.Count(l => l.Contains("a queued hit threw")) == 1);

        // The limits: a huge chain spreads over physics ticks instead of freezing one frame.
        var budgetField = guard.GetField("DrainBudgetMs", all);
        var limitField = guard.GetField("QueueLimit", all);
        var tickDrain = guard.GetMethod("TickDrain", all);
        var replayField = guard.GetField("Replay", all);
        void ResetLimits() { guard.GetMethod("ResetForTest", all).Invoke(null, null); replayed.Clear(); logs.Clear(); clockMs = 0; }
        void QueueDeep(int count) { for (int i = 0; i < maxDepth; i++) Pre("h" + i, "x"); for (int i = 0; i < count; i++) Pre("w", "hit"); }
        void Unwind() { for (int i = 0; i < maxDepth; i++) Fin(true); }
        void TickOnce() => tickDrain.Invoke(null, null);
        Action<object, object> costs(double ms) => (s, i) => { replayed.Add("r"); clockMs += ms; };

        ResetLimits();
        replayField.SetValue(null, costs(1));
        QueueDeep(10); Unwind();
        Check("chain limit: the drain stops when this tick's 4 ms are used and the rest keep waiting", replayed.Count == 4 && Queued() == 6 && Depth() == 0);
        Pre("z", "direct"); Fin(true);
        Check("chain limit: another hit finishing in the same tick adds no drain time", replayed.Count == 4 && Queued() == 6 && Depth() == 0);
        TickOnce();
        Check("chain limit: the next physics tick runs another 4 ms of them", replayed.Count == 8 && Queued() == 2);
        TickOnce();
        Check("chain limit: the backlog finishes and is logged once with its size and length", replayed.Count == 10 && Queued() == 0
            && logs.Count(l => l.Contains("up to 10 waiting hits was spread over 3 physics ticks (0 dropped)")) == 1);

        ResetLimits();
        replayField.SetValue(null, costs(10));
        QueueDeep(3); Unwind();
        Check("chain limit: one slow hit can pass the limit, but only that one", replayed.Count == 1 && Queued() == 2);
        TickOnce();
        Check("chain limit: the overrun is paid back from later ticks", replayed.Count == 1 && Queued() == 2);
        TickOnce();
        Check("chain limit: work resumes once the overrun is paid back", replayed.Count == 2 && Queued() == 1);

        ResetLimits();
        limitField.SetValue(null, 5);
        replayField.SetValue(null, costs(1));
        QueueDeep(5); Unwind();
        Check("chain limit: a partial drain leaves room in the line", replayed.Count == 4 && Queued() == 1);
        QueueDeep(6);
        Check("chain limit: the queue limit counts only hits still waiting, and the drop warns once", Queued() == 5 && logs.Count(l => l.Contains("the rest of this chain is dropped")) == 1);
        Unwind();
        for (int i = 0; i < 2; i++) TickOnce();
        Check("chain limit: a backlog that lost hits reports how many", Queued() == 0 && logs.Count(l => l.Contains("(2 dropped)")) == 1);

        ResetLimits();
        budgetField.SetValue(null, 0.0);
        replayField.SetValue(null, costs(1000));
        QueueDeep(10); Unwind();
        Check("chain limit: 0 ms means no time limit, the whole line runs at once", replayed.Count == 10 && Queued() == 0);

        Action<object, object> selfFeeding(double ms) => (s, i) => {
            replayed.Add("r"); clockMs += ms;
            int trues = 0; for (int k = 0; k < maxDepth; k++) if (Pre("s" + k, "y")) trues++;
            for (int k = 0; k < trues; k++) Fin(true);
        };
        ResetLimits();
        budgetField.SetValue(null, 0.0); limitField.SetValue(null, 8);
        replayField.SetValue(null, selfFeeding(0));
        QueueDeep(1); Unwind();
        int backlogCap = (int)guard.GetField("MaxBacklogHits", all).GetValue(null);
        Check("chain limit: with no time limit a self-feeding chain still stops at the backlog limit", replayed.Count == backlogCap && Queued() == 0 && Depth() == 0);
        ResetLimits();
        replayField.SetValue(null, selfFeeding(1));
        QueueDeep(1); Unwind();
        Check("chain limit: a self-feeding chain runs only its 4 ms per tick", replayed.Count == 4 && Queued() == 1 && Depth() == 0);
        ResetLimits();
        limitField.SetValue(null, 3);
        replayField.SetValue(null, selfFeeding(1));
        QueueDeep(1); Unwind();
        Check("chain limit: hits that already ran in this drain don't count against the line", replayed.Count == 4 && Queued() == 1);
        ResetLimits();
        replayField.SetValue(null, selfFeeding(0));
        QueueDeep(1); Unwind();
        Check("chain limit: a self-feeding chain ends at the backlog limit even with time to spare", replayed.Count == backlogCap && Queued() == 0 && Depth() == 0);
        Check("chain limit: reaching the backlog limit warns once and reports the drop", logs.Count(l => l.Contains("the rest of this chain is dropped")) == 1 && logs.Count(l => l.Contains("(1 dropped)")) == 1);
        replayField.SetValue(null, costs(0));
        QueueDeep(1);
        Check("chain limit: the next backlog starts fresh", Queued() == 1);
        Unwind();

        ResetLimits();
        var configure = guard.GetMethod("Configure", all);
        configure.Invoke(null, new object[] { -5, -1f });
        Check("chain limit: nonsense settings are clamped", (int)limitField.GetValue(null) == 1 && (double)budgetField.GetValue(null) == 0.0);
        configure.Invoke(null, new object[] { 300, 2.5f });
        Check("chain limit: valid settings are taken", (int)limitField.GetValue(null) == 300 && (double)budgetField.GetValue(null) == 2.5);
        ResetLimits();

        // Hypercrit2 finalizer.
        var hyper = shieldType.Assembly.GetType("CompatShield.HypercritNullFix");
        var hf = hyper.GetMethod("Finalizer", all);
        var args = new object[] { new NullReferenceException(), 2.5f };
        var swallowed = hf.Invoke(null, args);
        var args2 = new object[] { new InvalidOperationException("x"), 2.5f };
        var kept = hf.Invoke(null, args2);
        Check("hypercrit: a NullReferenceException is swallowed with the result forced to 0 (no crit); any other exception is handed back untouched",
            swallowed == null && (float)args[1] == 0f && kept == args2[0] && (float)args2[1] == 2.5f);

        // Through real Harmony patches on a stand-in (instance method, parameter named damageInfo): proves the object-typed
        // bindings, __state reaching the finalizer, and the skipped original.
        guard.GetMethod("ResetForTest", all).Invoke(null, null); logs.Clear();
        var hm = new Harmony("test.chain");
        var standin = typeof(FakeHealth).GetMethod("TakeDamage");
        hm.Patch(standin, prefix: new HarmonyMethod(prefix), finalizer: new HarmonyMethod(fin));
        guard.GetField("Replay", all).SetValue(null, new Action<object, object>((s, i) => ((FakeHealth)s).TakeDamage(i)));
        FakeHealth.DepthWanted = 7; FakeHealth.Calls.Clear();
        new FakeHealth { Id = 1 }.TakeDamage(1);
        Console.WriteLine("  harmony-bound: " + string.Join(" ", FakeHealth.Calls));
        Check("chain harmony: a 7-deep self-nesting hit through real patches runs 3 links inline, queues the 4th, replays it as link 1 (2 more inline), queues again, and every link ran exactly once in order",
            Depth() == 0 && Queued() == 0 && FakeHealth.Calls.SequenceEqual(new[] { "1:1", "1:2", "1:3", "1:4", "1:5", "1:6", "1:7" }) && logs.Count(l => l.Contains("was queued")) == 1);
        hm.UnpatchSelf();

        var managed = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var ror2 = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var hc = ror2.GetType("RoR2.HealthComponent");
        var take = hc?.Methods.FirstOrDefault(m => m.Name == "TakeDamage" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == "DamageInfo");
        var proc = hc?.Methods.FirstOrDefault(m => m.Name == "TakeDamageProcess" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == "DamageInfo");
        bool callsProcess = take?.Body?.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "TakeDamageProcess") ?? false;
        int calls = take?.Body?.Instructions.Count(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Call || i.OpCode.Code == Mono.Cecil.Cil.Code.Callvirt) ?? 0;
        Check("chain real: game 1.5.0's HealthComponent.TakeDamage(DamageInfo) is a thin server-check wrapper that calls TakeDamageProcess(DamageInfo), so replaying through TakeDamageProcess is the same hit",
            take != null && !take.IsStatic && take.IsPublic && proc != null && !proc.IsStatic && callsProcess && calls <= 4);
        var dll = System.IO.Path.Combine(profile, @"plugins\prodzpod-Hypercrit2\NotHypercrit.dll");
        if (!System.IO.File.Exists(dll)) { Console.WriteLine("SKIP  hypercrit real: NotHypercrit.dll not installed"); return failures; }
        var nh = Mono.Cecil.ModuleDefinition.ReadModule(dll);
        var gecs = nh.GetType("NotHypercrit.Crit")?.Methods.Where(m => m.Name == "GetEffectiveCount").ToList();
        var gec = gecs?.FirstOrDefault(m => m.Parameters.Count == 3 && m.Parameters[0].ParameterType.FullName == "System.Single" && m.Parameters[2].ParameterType.FullName == "System.Boolean");
        Check("hypercrit real: NotHypercrit.Crit has two GetEffectiveCount overloads and the fix picks the static float (float, CharacterBody, bool) one that throws (the 1-parameter one comes first in metadata)",
            gecs != null && gecs.Count == 2 && gec != null && gec.IsStatic && gec.ReturnType.FullName == "System.Single" && gecs[0].Parameters.Count == 1);
        return failures;
    }

    // Artifact of Genetics switch: the force-off prefix and the lobby hiding with stand-in defs (TokenOf swapped), plus the real members.
    // Sacrifice exceptions. Stand-ins for the director types: listed cards lose the skip flag, the rest keep it, a category that keeps
    // some cards and loses others is scaled once per stage, and the real names are logged once. Real game: the members, the parameter
    // name Harmony binds to, both game checks of the flag, and the four card names his list uses.
    class SkSpawnCard { public string name; public bool skipSpawnWhenSacrificeArtifactEnabled; public float weightScalarWhenSacrificeArtifactEnabled = 1f; }
    class SkCard { public SkSpawnCard sc; public SkSpawnCard GetSpawnCard() { return sc; } }
    struct SkCategory { public string name; public SkCard[] cards; public float selectionWeight; }
    class SkSelection { public SkCategory[] categories; }
    class SkNoFlag { public string name; }

    static int SacrificeKeepCheck(string managed) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var t = shieldType.Assembly.GetType("CompatShield.SacrificeKeepTweak");
        t.GetField("NameOf", all).SetValue(null, new Func<object, string>(o => ((SkSpawnCard)o).name));
        Func<string, string[]> parse = s => (string[])t.GetMethod("ParseList", all).Invoke(null, new object[] { s });
        Check("sackeep: the list splits on commas, semicolons and spaces, and drops blanks and repeats (any case)",
            parse(" iscA, iscB;iscA  iscC ,,ISCB").SequenceEqual(new[] { "iscA", "iscB", "iscC" }) && parse("").Length == 0 && parse(null).Length == 0);
        Action<string, float> reset = (l, w) => t.GetMethod("ResetForTest", all).Invoke(null, new object[] { l, w });
        Func<Type, Type, Type, Type, bool> bind = (a, b, c, d) => (bool)t.GetMethod("Bind", all).Invoke(null, new object[] { a, b, c, d });
        Action<object> adjust = s => t.GetMethod("Adjust", all).Invoke(null, new object[] { s });
        Check("sackeep: a game whose spawn card has no skip flag is refused (the 'not applied' path)",
            !bind(typeof(SkSelection), typeof(SkCategory), typeof(SkCard), typeof(SkNoFlag)));
        Check("sackeep: the stand-in director types bind", bind(typeof(SkSelection), typeof(SkCategory), typeof(SkCard), typeof(SkSpawnCard)));
        Func<string, bool, SkSpawnCard> sc = (n, skip) => new SkSpawnCard { name = n, skipSpawnWhenSacrificeArtifactEnabled = skip };
        Func<SkSpawnCard, SkCard> card = s => new SkCard { sc = s };
        var chest = sc("iscChest1", true); var barrel = sc("iscEquipmentBarrel", true); var lunar = sc("iscLunarChest", true);
        var sandy = sc("iscShrineChanceSandy", true); var blood = sc("iscShrineBlood", false); var drone = sc("iscBrokenDrone1", false);
        var zero = sc("iscLunarChestZero", true); zero.weightScalarWhenSacrificeArtifactEnabled = 0f;
        Func<SkSelection> stage = () => new SkSelection { categories = new[] {
            new SkCategory { name = "Chests", selectionWeight = 45f, cards = new[] { card(chest), card(barrel), card(lunar), card(zero) } },
            new SkCategory { name = "Shrines", selectionWeight = 10f, cards = new[] { card(sandy), card(blood) } },
            new SkCategory { name = "Drones", selectionWeight = 14f, cards = new[] { card(drone), null } } } };
        reset("iscShrineChance, iscEquipmentBarrel,iscLunarChest", 0.5f);
        var one = stage();
        adjust(one);
        Check("sackeep: listed cards lose the skip flag (prefix match covers the Sandy shrine); unlisted ones keep it",
            !barrel.skipSpawnWhenSacrificeArtifactEnabled && !lunar.skipSpawnWhenSacrificeArtifactEnabled && !sandy.skipSpawnWhenSacrificeArtifactEnabled
            && chest.skipSpawnWhenSacrificeArtifactEnabled && !blood.skipSpawnWhenSacrificeArtifactEnabled);
        Check("sackeep: a kept card with a zero Sacrifice weight scalar is raised to 1, others untouched",
            zero.weightScalarWhenSacrificeArtifactEnabled == 1f && barrel.weightScalarWhenSacrificeArtifactEnabled == 1f);
        Check("sackeep: only the category that keeps some cards and loses others is scaled (Chests 45 to 22.5, Shrines and Drones as they were)",
            one.categories[0].selectionWeight == 22.5f && one.categories[1].selectionWeight == 10f && one.categories[2].selectionWeight == 14f);
        Check("sackeep: the log gives the scaled category's real share, counting only categories with cards left (22.5 of 46.5 is 48%)",
            logs.Exists(l => l.Contains("category Chests keeps some") && l.Contains("weight 45 now 22.5") && l.Contains("about 48%")));
        int lines = logs.Count;
        Check("sackeep: the first stage logs the real names with their category",
            logs.Exists(l => l.Contains("still blocked") && l.Contains("Chests/iscChest1")) && logs.Exists(l => l.Contains("kept (first seen") && l.Contains("Chests/iscEquipmentBarrel") && l.Contains("Shrines/iscShrineChanceSandy")));
        var two = stage();
        adjust(two);
        Check("sackeep: the next stage (flags already cleared, fresh copy) scales Chests once more, not twice, and logs nothing new",
            two.categories[0].selectionWeight == 22.5f && two.categories[1].selectionWeight == 10f && logs.Count == lines);
        chest = sc("iscChest1", true); barrel = sc("iscEquipmentBarrel", true);
        var whole = new SkSelection { categories = new[] { new SkCategory { name = "Chests", selectionWeight = 45f, cards = new[] { card(chest), card(barrel) } } } };
        reset("iscEquipmentBarrel", -3f);
        adjust(whole);
        Check("sackeep: a negative weight counts as 1 (the category keeps its full weight)", whole.categories[0].selectionWeight == 45f && !barrel.skipSpawnWhenSacrificeArtifactEnabled);
        var untouched = sc("iscEquipmentBarrel", true);
        var none = new SkSelection { categories = new[] { new SkCategory { name = "Chests", selectionWeight = 45f, cards = new[] { card(untouched) } } } };
        reset("", 0.5f);
        adjust(none);
        Check("sackeep: an empty list changes nothing", untouched.skipSpawnWhenSacrificeArtifactEnabled && none.categories[0].selectionWeight == 45f);
        var prefix = t.GetMethod("Prefix", all);
        lines = logs.Count;
        bool threw = false;
        try { prefix.Invoke(null, new object[] { new object() }); prefix.Invoke(null, new object[] { new object() }); } catch { threw = true; }
        Check("sackeep: a failure inside the prefix never reaches the game and is logged once", !threw && logs.Count == lines + 1 && logs[logs.Count - 1].Contains("logged once"));
        lines = logs.Count;
        t.GetMethod("Apply", all).Invoke(null, new object[] { new Harmony("test.sackeep"), typeof(Program).Assembly, "iscFoo", 1f });
        Check("sackeep: an assembly without the game's types warns 'not applied' and doesn't throw", logs.Count == lines + 1 && logs[lines].Contains("not applied"));

        var ror2 = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var man = ror2.GetType("RoR2.Artifacts.SacrificeArtifactManager");
        var filter = man?.Methods.FirstOrDefault(m => m.Name == "OnGenerateInteractableCardSelection");
        Check("sackeep real: OnGenerateInteractableCardSelection(SceneDirector, DirectorCardCategorySelection) is static and its selection parameter has the name the prefix binds to",
            filter != null && filter.IsStatic && filter.Parameters.Count == 2 && filter.Parameters[0].ParameterType.FullName == "RoR2.SceneDirector"
            && filter.Parameters[1].ParameterType.FullName == "RoR2.DirectorCardCategorySelection" && filter.Parameters[1].Name == prefix.GetParameters()[0].Name);
        var isc = ror2.GetType("RoR2.InteractableSpawnCard");
        Func<Mono.Cecil.MethodDefinition, bool> readsFlag = m => m.HasBody && m.Body.Instructions.Any(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldfld && ((Mono.Cecil.FieldReference)i.Operand).Name == "skipSpawnWhenSacrificeArtifactEnabled");
        Check("sackeep real: the spawn card has both Sacrifice fields, and both the stage filter and InteractableSpawnCard.Spawn read the flag",
            isc?.Fields.Any(f => f.Name == "skipSpawnWhenSacrificeArtifactEnabled" && f.FieldType.FullName == "System.Boolean") == true
            && isc.Fields.Any(f => f.Name == "weightScalarWhenSacrificeArtifactEnabled" && f.FieldType.FullName == "System.Single")
            && isc.Methods.Any(m => m.Name == "Spawn" && readsFlag(m)) && man.Methods.Any(readsFlag));
        var dccs = ror2.GetType("RoR2.DirectorCardCategorySelection");
        var cat = dccs?.NestedTypes.FirstOrDefault(n => n.Name == "Category");
        Check("sackeep real: the selection's categories, each with name, cards and selectionWeight, and DirectorCard.GetSpawnCard() exist",
            dccs?.Fields.Any(f => f.Name == "categories") == true && cat != null && cat.IsValueType && cat.Fields.Any(f => f.Name == "name") && cat.Fields.Any(f => f.Name == "cards")
            && cat.Fields.Any(f => f.Name == "selectionWeight") && ror2.GetType("RoR2.DirectorCard")?.Methods.Any(m => m.Name == "GetSpawnCard" && !m.HasParameters) == true);
        var catalog = System.IO.Path.GetFullPath(System.IO.Path.Combine(managed, @"..\StreamingAssets\aa\catalog.json"));
        var names = new[] { "iscShrineChance", "iscCasinoChest", "iscEquipmentBarrel", "iscLunarChest" };
        if (!System.IO.File.Exists(catalog)) { Console.WriteLine("SKIP  sackeep real: catalog.json not found"); return failures; }
        var text = System.IO.File.ReadAllText(catalog);
        Check("sackeep real: the four card names in his list exist in the game's asset catalog", names.All(n => text.Contains(n + ".asset")));
        return failures;
    }

    static int GeneticsCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var sw = shieldType.Assembly.GetType("CompatShield.ArtifactDisabler");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        sw.GetMethod("ResetForTest", all).Invoke(null, null);
        sw.GetField("TokenOf", all).SetValue(null, new Func<object, string>(o => o as string));
        var prefix = sw.GetMethod("SetPrefix", all);
        bool Set(object def, bool wanted) { var a = new object[] { def, wanted }; prefix.Invoke(null, a); return (bool)a[1]; }
        Check("genetics: enabling the Genetics artifact is turned into off, once logged; disabling it and other artifacts are untouched",
            Set("GENETIC_ARTIFACT_NAME_TOKEN", true) == false && Set("GENETIC_ARTIFACT_NAME_TOKEN", true) == false && Set("GENETIC_ARTIFACT_NAME_TOKEN", false) == false
            && Set("ARTIFACT_COMMAND_NAME", true) == true && Set(null, true) == true && logs.Count(l => l.Contains("a vote tried to enable it")) == 1);
        var excluded = new System.Collections.Generic.List<object>();
        var choices = new object[] { "c-command", "c-genetics", "c-none", "c-glass", "c-genetics2" };
        int n = (int)sw.GetMethod("HideIn", all).Invoke(null, new object[] { choices, new Func<object, object>(c =>
            (string)c == "c-genetics" || (string)c == "c-genetics2" ? "GENETIC_ARTIFACT_NAME_TOKEN" : (string)c == "c-none" ? null : "ARTIFACT_" + c), new Action<object>(c => excluded.Add(c)) });
        Check("genetics: only the Genetics choices are hidden (both on/off choices), choices without an artifact are skipped",
            n == 2 && excluded.Count == 2 && excluded.Contains("c-genetics") && excluded.Contains("c-genetics2"));

        var managed = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var ror2 = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var ram = ror2.GetType("RoR2.RunArtifactManager")?.Methods.FirstOrDefault(m => m.Name == "SetArtifactEnabledServer" && m.Parameters.Count == 2 && m.Parameters[0].ParameterType.Name == "ArtifactDef" && m.Parameters[1].ParameterType.FullName == "System.Boolean");
        var rc = ror2.GetType("RoR2.RuleCatalog");
        var init = rc?.Methods.FirstOrDefault(m => m.Name == "Init" && !m.HasParameters && m.IsStatic);
        var list = rc?.Fields.FirstOrDefault(f => f.Name == "allChoicesDefs" && f.IsStatic);
        var rcd = ror2.GetType("RoR2.RuleChoiceDef");
        var catalogGet = ror2.GetType("RoR2.ArtifactCatalog")?.Methods.FirstOrDefault(m => m.Name == "GetArtifactDef" && m.IsStatic && m.Parameters.Count == 1);
        Check("genetics real: game 1.5.0 has RunArtifactManager.SetArtifactEnabledServer(ArtifactDef, bool), RuleCatalog.Init() [SystemInitializer] with its allChoicesDefs list, RuleChoiceDef.artifactIndex / excludeByDefault, ArtifactCatalog.GetArtifactDef(index) and ArtifactDef.nameToken",
            ram != null && !ram.IsStatic && init != null && init.CustomAttributes.Any(a => a.AttributeType.Name == "SystemInitializerAttribute") && list != null
            && rcd?.Fields.Any(f => f.Name == "artifactIndex") == true && rcd.Fields.Any(f => f.Name == "excludeByDefault") && catalogGet != null
            && ror2.GetType("RoR2.ArtifactDef")?.Fields.Any(f => f.Name == "nameToken") == true);
        var dll = System.IO.Path.Combine(profile, @"plugins\Rico-GeneticArtifact\GeneticsArtifact.dll");
        if (!System.IO.File.Exists(dll)) { Console.WriteLine("SKIP  genetics real: GeneticsArtifact.dll not installed"); return failures; }
        var text = System.Text.Encoding.Unicode.GetString(System.IO.File.ReadAllBytes(dll));
        Check("genetics real: the GeneticArtifact mod names its artifact GENETIC_ARTIFACT_NAME_TOKEN and gates its hooks on IsArtifactEnabled",
            text.Contains("GENETIC_ARTIFACT_NAME_TOKEN") && System.Text.Encoding.UTF8.GetString(System.IO.File.ReadAllBytes(dll)).Contains("IsArtifactEnabled"));
        return failures;
    }

    static void FakeHook_SetBuffCount() { }   // stands in for a mod's On-hook named after the method it hooks

    // A MethodBase with a chosen name, for the wrapper-frame test (only Name is read).
    sealed class NamedType : TypeDelegator {
        readonly string name; public NamedType(string n) : base(typeof(object)) { name = n; }
        public override string FullName => name;
    }

    sealed class FakeMethod : MethodInfo {
        readonly string name; readonly Type declaring;
        public FakeMethod(string n, Type d = null) { name = n; declaring = d ?? typeof(Program); }
        public override string Name => name;
        public override Type DeclaringType => declaring;
        public override Type ReflectedType => declaring;
        public override RuntimeMethodHandle MethodHandle => throw new NotSupportedException();
        public override MethodAttributes Attributes => MethodAttributes.Static;
        public override ICustomAttributeProvider ReturnTypeCustomAttributes => throw new NotSupportedException();
        public override MethodInfo GetBaseDefinition() => this;
        public override object[] GetCustomAttributes(bool inherit) => new object[0];
        public override object[] GetCustomAttributes(Type t, bool inherit) => new object[0];
        public override MethodImplAttributes GetMethodImplementationFlags() => MethodImplAttributes.IL;
        public override ParameterInfo[] GetParameters() => new ParameterInfo[0];
        public override object Invoke(object obj, BindingFlags f, Binder b, object[] p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
        public override bool IsDefined(Type t, bool inherit) => false;
    }


    static void FakeLotus(On.RoR2.BuffWard.orig_BuffTeam orig, object self) { }
    static void FakeOverload(On.RoR2.BuffWard.orig_Refresh_float orig, object self) { }
    public sealed class FakeDirtyBody {
        public bool statsDirty; public int runs;
        public void RecalculateStats() { runs++; statsDirty = false; }
    }

    // Stat update limit: the per-body gate, the stage line, the FixedUpdate rewrite (run for real as IL), the shipped game's IL, and the tracer's pass-through rule.
    sealed class FakeSummonResult { }
    sealed class FakeSummonTarget {
        public int runs;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public FakeSummonResult Perform() { runs++; return new FakeSummonResult(); }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void SpawnBodyHere() { runs++; }
    }

    sealed class FakeIchorTarget {
        public int body;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void PerformJitterUI(Action<object> orig, object self) { body++; }
    }

    // Hallowed Ichor's run timer: when the mod's hook is left alone, when the game's Update runs instead, and the names the fix relies on in the shipped mod.
    static int HallowedCheck(string managed, string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var fix = shieldType.Assembly.GetType("CompatShield.HallowedIchorTimerFix");
        bool held = false, jitter = false, blowUp = false;
        fix.GetField("Held", all).SetValue(null, new Func<bool>(() => { if (blowUp) throw new InvalidOperationException("boom"); return held; }));
        fix.GetField("HasJitter", all).SetValue(null, new Func<object, bool>(o => jitter));
        var prefix = fix.GetMethod("Prefix", all);
        fix.GetMethod("ResetForTest", all).Invoke(null, null);
        int origRuns = 0; object seen = null;
        Action<object> orig = o => { origRuns++; seen = o; };
        Func<bool> Run = () => (bool)prefix.Invoke(null, new object[] { orig, "timer" });

        Check("hallowed: before anyone holds the item, the mod's hook runs", Run() && origRuns == 0);
        held = true; jitter = true;
        Check("hallowed: with its text effect in place, the mod's hook runs", Run() && origRuns == 0);
        jitter = false; logs.Clear();
        Check("hallowed: with no text effect, the mod's hook is skipped and the game's Update runs on the same timer", !Run() && origRuns == 1 && (string)seen == "timer");
        Run();
        Check("hallowed: every update goes to the game's code", origRuns == 2);
        Check("hallowed: the change is logged once", logs.Count(l => l.Contains("run timer keeps counting normally")) == 1);
        blowUp = true; logs.Clear(); origRuns = 0;
        Check("hallowed: a problem leaves the mod's hook to run", Run() && origRuns == 0);
        Run();
        Check("hallowed: the problem is logged once", logs.Count(l => l.Contains("couldn't check the timer")) == 1);
        blowUp = false;

        var h = new Harmony("test.hallowed");
        h.Patch(typeof(FakeIchorTarget).GetMethod("PerformJitterUI"), prefix: new HarmonyMethod(prefix));
        var target = new FakeIchorTarget(); origRuns = 0;
        held = true; jitter = false;
        target.PerformJitterUI(orig, "timer");
        Check("hallowed: patched with no text effect, the hook body doesn't run and the game's Update does", target.body == 0 && origRuns == 1);
        jitter = true;
        target.PerformJitterUI(orig, "timer");
        Check("hallowed: patched with the text effect, the hook body runs", target.body == 1 && origRuns == 1);

        var ror2 = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var timer = ror2.GetType((string)fix.GetField("TimerName", all).GetValue(null));
        Check("hallowed real: the game's run timer has the text field the fix reads", timer?.Fields.Any(f => f.Name == "runStopwatchTimerTextController") == true);
        var dll = System.IO.Directory.GetFiles(System.IO.Path.Combine(profile, "plugins"), "Sandswept.dll", System.IO.SearchOption.AllDirectories).FirstOrDefault();
        if (dll == null) { Console.WriteLine("SKIP  hallowed real: Sandswept.dll not installed"); return failures; }
        var mod = Mono.Cecil.ModuleDefinition.ReadModule(dll);
        var item = mod.GetType((string)fix.GetField("ItemName", all).GetValue(null));
        var hook = item?.Methods.FirstOrDefault(m => m.Name == "PerformJitterUI");
        Check("hallowed real: the hook takes (orig, self) by those names, as the prefix does",
            hook != null && hook.Parameters.Count == 2 && hook.Parameters[0].Name == "orig" && hook.Parameters[1].Name == "self" && hook.Parameters[0].ParameterType.Name == "orig_Update");
        Check("hallowed real: the item has its static held flag", item?.Fields.Any(f => f.Name == "anyoneHadHallowedIchorThisRun" && f.IsStatic && f.FieldType.FullName == "System.Boolean") == true);
        Check("hallowed real: the mod has its text effect type", mod.GetType((string)fix.GetField("JitterName", all).GetValue(null)) != null);
        Check("hallowed real: the hook still reads the text effect", hook?.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "GetComponent" && r.ToString().Contains("FreakyText")) == true);
        return failures;
    }

    // Stand-in with the Paladin shockwave follower's shape: one ParticleSystem field, and a FixedUpdate that throws when it is gone.
    sealed class FakePaladinFollower {
        public sealed class ParticleSystem { public bool dead; }
        public object unrelated;
        public ParticleSystem effect;
        public int ticks;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void FixedUpdate() { string at = effect.ToString(); ticks++; }
    }
    sealed class FakeTwoEffects { public FakePaladinFollower.ParticleSystem a, b; }
    // Stand-ins with Sandswept's damage trail shape: a trail's flames list, and a resize that throws on an empty one.
    sealed class FakeTrail { public System.Collections.Generic.List<int> activePoints = new System.Collections.Generic.List<int>(); public int resized; }
    static class FakeTrailManager {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void RecalculateTrailBounds(FakeTrail trail) {
            if (trail.activePoints.Count == 0) throw new ArgumentException("Zero-sized array is not allowed.");
            trail.resized++;
        }
    }

    // Paladin shockwave and Sandswept fire trail guards: each skips one mod line that throws once its object is gone.
    static int ModGuardsCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        var pal = shieldType.Assembly.GetType("CompatShield.PaladinShockwaveFix");
        bool blowUp = false;
        pal.GetField("Alive", all).SetValue(null, new Func<object, bool>(o => { if (blowUp) throw new InvalidOperationException("boom"); return o is FakePaladinFollower.ParticleSystem p && !p.dead; }));
        var palBind = pal.GetMethod("Bind", all);
        Check("paladin: no match on a follower with two effects", !(bool)palBind.Invoke(null, new object[] { typeof(FakeTwoEffects) }));
        Check("paladin: no match on a follower with no effect", !(bool)palBind.Invoke(null, new object[] { typeof(FakeTrail) }));
        Check("paladin: binds to the one effect field by its type", (bool)palBind.Invoke(null, new object[] { typeof(FakePaladinFollower) }));
        pal.GetMethod("ResetForTest", all).Invoke(null, null);
        var palPrefix = pal.GetMethod("Prefix", all);
        Func<FakePaladinFollower, bool> PalRun = f => (bool)palPrefix.Invoke(null, new object[] { f });
        Check("paladin: with its effect alive, the follower runs as before", PalRun(new FakePaladinFollower { effect = new FakePaladinFollower.ParticleSystem() }));
        Check("paladin: with its effect missing or gone, the move is skipped",
            !PalRun(new FakePaladinFollower()) && !PalRun(new FakePaladinFollower { effect = new FakePaladinFollower.ParticleSystem { dead = true } }));
        Check("paladin: skipping is logged once", logs.Count(l => l.Contains("stopped moving it")) == 1);
        blowUp = true;
        bool ranAnyway = PalRun(new FakePaladinFollower()) && PalRun(new FakePaladinFollower());
        Check("paladin: a problem in the check lets the mod's code run, logged once", ranAnyway && logs.Count(l => l.Contains("couldn't check a shockwave's dust effect")) == 1);
        blowUp = false;

        var bare = new FakePaladinFollower();
        bool palBaseline = false;
        try { bare.FixedUpdate(); } catch (NullReferenceException) { palBaseline = true; }
        Check("paladin: unguarded, the stand-in throws like the mod did", palBaseline && bare.ticks == 0);
        var h = new Harmony("test.modguards");
        h.Patch(typeof(FakePaladinFollower).GetMethod("FixedUpdate"), prefix: new HarmonyMethod(palPrefix));
        var gone = new FakePaladinFollower();
        var live = new FakePaladinFollower { effect = new FakePaladinFollower.ParticleSystem() };
        bool palClean = true;
        try { for (int i = 0; i < 3; i++) { gone.FixedUpdate(); live.FixedUpdate(); } } catch { palClean = false; }
        Check("paladin: guarded, a shockwave with no effect ticks without throwing and a live one still moves it", palClean && gone.ticks == 0 && live.ticks == 3);

        var trailFix = shieldType.Assembly.GetType("CompatShield.SandsweptFireTrailFix");
        var trailBind = trailFix.GetMethod("Bind", all);
        Check("trail: no match without a flames list", !(bool)trailBind.Invoke(null, new object[] { typeof(FakePaladinFollower) }));
        Check("trail: binds to the trail's flames list", (bool)trailBind.Invoke(null, new object[] { typeof(FakeTrail) }));
        trailFix.GetMethod("ResetForTest", all).Invoke(null, null);
        var trailPrefix = trailFix.GetMethod("Prefix", all);
        Func<object, bool> TrailRun = t => (bool)trailPrefix.Invoke(null, new object[] { t });
        var withFlame = new FakeTrail();
        withFlame.activePoints.Add(1);
        Check("trail: with flames left, the resize runs as before", TrailRun(withFlame));
        Check("trail: with no flames left, the resize is skipped", !TrailRun(new FakeTrail()) && !TrailRun(new FakeTrail { activePoints = null }) && !TrailRun(null));
        Check("trail: skipping is logged once", logs.Count(l => l.Contains("its size was left as it was")) == 1);
        Check("trail: a problem in the check lets the mod's code run, logged once",
            TrailRun("not a trail") && TrailRun("not a trail") && logs.Count(l => l.Contains("couldn't check a fire trail")) == 1);

        bool trailBaseline = false;
        try { FakeTrailManager.RecalculateTrailBounds(new FakeTrail()); } catch (ArgumentException) { trailBaseline = true; }
        Check("trail: unguarded, the stand-in throws like the mod did", trailBaseline);
        h.Patch(typeof(FakeTrailManager).GetMethod("RecalculateTrailBounds"), prefix: new HarmonyMethod(trailPrefix));
        var empty = new FakeTrail();
        var full = new FakeTrail();
        full.activePoints.Add(1);
        bool trailClean = true;
        try { FakeTrailManager.RecalculateTrailBounds(empty); FakeTrailManager.RecalculateTrailBounds(full); } catch { trailClean = false; }
        Check("trail: guarded, an empty trail doesn't throw and a trail with flames is still resized", trailClean && empty.resized == 0 && full.resized == 1);

        var plugins = System.IO.Path.Combine(profile, "plugins");
        var palDll = System.IO.Directory.GetFiles(plugins, "PaladinMod.dll", System.IO.SearchOption.AllDirectories).FirstOrDefault();
        if (palDll == null) Console.WriteLine("SKIP  paladin real: PaladinMod.dll not installed");
        else {
            var m = Mono.Cecil.ModuleDefinition.ReadModule(palDll);
            var t = m.GetType((string)pal.GetField("FollowerName", all).GetValue(null));
            var fx = t?.Fields.Where(f => !f.IsStatic && f.FieldType.Name == "ParticleSystem").ToList();
            Check("paladin real: the follower still has one effect field, a UnityEngine.ParticleSystem", fx != null && fx.Count == 1 && fx[0].FieldType.FullName == "UnityEngine.ParticleSystem");
            var tick = t?.Methods.FirstOrDefault(x => x.Name == "FixedUpdate")?.Body.Instructions.ToList();
            Check("paladin real: FixedUpdate starts by reading that effect",
                tick != null && fx != null && fx.Count == 1 && tick.Take(4).Any(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldfld && ((Mono.Cecil.FieldReference)i.Operand).Name == fx[0].Name));
            bool added = t != null && m.GetTypes().SelectMany(x => x.Methods).Where(x => x.HasBody).SelectMany(x => x.Body.Instructions)
                .Any(i => i.Operand is Mono.Cecil.GenericInstanceMethod g && g.Name == "AddComponent" && g.GenericArguments.Any(a => a.FullName == t.FullName));
            Check("paladin real: the mod still adds the follower to an object (the shockwave ghost)", added);
        }

        var ssDll = System.IO.Directory.GetFiles(plugins, "Sandswept.dll", System.IO.SearchOption.AllDirectories).FirstOrDefault();
        if (ssDll == null) Console.WriteLine("SKIP  trail real: Sandswept.dll not installed");
        else {
            var m = Mono.Cecil.ModuleDefinition.ReadModule(ssDll);
            var mgr = m.GetType((string)trailFix.GetField("ManagerName", all).GetValue(null));
            var resize = mgr?.Methods.FirstOrDefault(x => x.Name == "RecalculateTrailBounds");
            Check("trail real: the manager still resizes a trail with one static method taking the trail", resize != null && resize.IsStatic && resize.Parameters.Count == 1);
            var trailType = resize != null && resize.Parameters.Count == 1 ? resize.Parameters[0].ParameterType.Resolve() : null;
            var points = trailType?.Fields.FirstOrDefault(f => f.Name == "activePoints");
            Check("trail real: the trail keeps its flames in a List", points != null && points.FieldType.FullName.StartsWith("System.Collections.Generic.List`1"));
            Check("trail real: the resize builds the bounds from those flames",
                resize?.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "CalculateBounds") == true);
            var fu = mgr?.Methods.FirstOrDefault(x => x.Name == "FixedUpdate")?.Body.Instructions.ToList();
            int removeAll = fu == null ? -1 : fu.FindIndex(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "RemoveAll");
            int resizeCall = fu == null ? -1 : fu.FindIndex(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "RecalculateTrailBounds");
            Check("trail real: the manager's tick resizes a trail right after removing its burnt-out flames", removeAll >= 0 && resizeCall > removeAll);
        }
        return failures;
    }

    // Stand-ins with StormyItems' Sharp Anchor shape: a zone list parallel to a body list, and a zone tick that reads the
    // holder's inventory through the zone's slot.
    sealed class FakeAnchorBody { public object inventory { get; set; } }
    static class FakeStormyItem { public static System.Collections.Generic.List<object> Zones = new System.Collections.Generic.List<object>(); }
    static class FakeStormyMain { public static System.Collections.Generic.List<FakeAnchorBody> CharBodies = new System.Collections.Generic.List<FakeAnchorBody>(); }
    sealed class FakeAnchorZone {
        public object go = new object();
        public int buffed;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void BuffOccupants() {
            int slot = -1;
            for (int i = 0; i < FakeStormyItem.Zones.Count; i++) if (ReferenceEquals(FakeStormyItem.Zones[i], go)) { slot = i; break; }
            if (slot >= 0) FakeStormyMain.CharBodies[slot].inventory.GetHashCode();
            buffed++;
        }
    }
    static class FakeNoZones { public static int Zones; }

    // Sharp Anchor guard: skips a zone's tick only when the mod's holder lookup would throw.
    // Physics pile-up tracer: the long-frame trigger with a fake clock and scene (grace after a scene change, the stage-load frame
    // skipped once, the 10 s rate limit, nothing outside a run), the census line built from stand-in colliders, the 4 m cube key,
    // a failing census caught and logged once, and a load path that touches nothing of Unity's.
    static int PileupCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var t = shieldType.Assembly.GetType("CompatShield.PhysicsPileupTracer");
        Func<string, float, float, float, int, bool, CompatShield.PhysicsPileupTracer.Item> Moving = (n, x, y, z, owner, trig) => {
            long cell = (long)t.GetMethod("CellOf", all).Invoke(null, new object[] { x, y, z });
            return new CompatShield.PhysicsPileupTracer.Item { Name = n, Owner = owner, Trigger = trig, Moving = true, Cell = cell };
        };

        // A pile: 15 ammo packs with a solid collider and a trigger each, 12 of them inside one 4 m cube; 10 projectiles spread
        // out; one body; 50 pieces of level geometry.
        var census = new System.Collections.Generic.List<CompatShield.PhysicsPileupTracer.Item>();
        for (int i = 0; i < 15; i++) {
            float x = i < 12 ? 1f + i * 0.1f : 100f + i * 10f;
            census.Add(Moving("AmmoPack", x, 2f, 3f, 1000 + i, false));
            census.Add(Moving("AmmoPack", x, 2f, 3f, 1000 + i, true));
        }
        for (int i = 0; i < 10; i++) census.Add(Moving("CrocoDiseaseProjectile", 300f + i * 20f, 0f, 0f, 2000 + i, false));
        census.Add(Moving("CrocoBody", -50f, 0f, 0f, 3000, false));
        for (int i = 0; i < 50; i++) census.Add(new CompatShield.PhysicsPileupTracer.Item { Moving = false });

        float clock = 100f; int scene = 1; bool boom = false;
        t.GetField("Now", all).SetValue(null, new Func<float>(() => clock));
        t.GetField("SceneId", all).SetValue(null, new Func<int>(() => scene));
        t.GetField("SceneName", all).SetValue(null, new Func<string>(() => "voidraid"));
        t.GetField("RunClock", all).SetValue(null, new Func<string>(() => "stage 12 t=50:01"));
        t.GetField("ClockMs", all).SetValue(null, new Func<double>(() => 0.0));
        t.GetField("Census", all).SetValue(null, new Func<System.Collections.Generic.List<CompatShield.PhysicsPileupTracer.Item>>(() => {
            if (boom) throw new InvalidOperationException("boom");
            return census;
        }));
        t.GetMethod("ResetForTest", all).Invoke(null, null);
        var frame = t.GetMethod("Frame", all);
        Action<float, float> At = (time, dt) => { clock = time; frame.Invoke(null, new object[] { dt }); };
        Func<int> Fired = () => (int)t.GetField("Fired", all).GetValue(null);
        Func<int> Lines = () => logs.Count(l => l.Contains("Physics pile-up:"));
        var running = t.GetField("Running", all);

        At(101f, 2f);
        Check("pileup: nothing happens outside a run", Fired() == 0 && Lines() == 0);
        running.SetValue(null, true);
        At(100f, 0.016f);
        At(103f, 0.8f);
        Check("pileup: a long frame in the first 6 s after a scene change is ignored", Fired() == 0);
        At(107f, 3f);
        Check("pileup: the multi-second stage-load frame after a scene change is skipped", Fired() == 0);
        At(108f, 0.4f);
        Check("pileup: a 400 ms frame takes no census", Fired() == 0);
        At(109f, 0.6f);
        string expected = "Physics pile-up: 600 ms frame in voidraid (stage 12 t=50:01); 41 moving colliders of 3 kinds, 24 in the most crowded 4 m spot, 50 static; most: " +
            "AmmoPack: 30 colliders on 15 objects, 15 triggers, 24 in one spot; CrocoDiseaseProjectile: 10 colliders on 10 objects, 1 in one spot; " +
            "CrocoBody: 1 colliders on 1 objects, 1 in one spot (census 0 ms)";
        string got = logs.LastOrDefault(l => l.Contains("Physics pile-up:")) ?? "";
        Check("pileup: a 600 ms frame logs one census line", Fired() == 1 && Lines() == 1);
        Check("pileup: the line names the pile, its triggers, the most in one spot and the static count", got.EndsWith(expected));
        if (!got.EndsWith(expected)) Console.WriteLine("  got: " + got);
        At(112f, 2f);
        Check("pileup: a second long frame within 10 s takes no census", Fired() == 1);
        At(119.5f, 6f);
        Check("pileup: a freeze-length frame 10 s later does (the load frame was already used up)", Fired() == 2 && logs.Last(l => l.Contains("Physics pile-up:")).Contains("6000 ms frame"));

        scene = 2;
        At(125f, 0.7f);
        Check("pileup: a new scene starts a new grace", Fired() == 2);
        At(132f, 6f);
        Check("pileup: the new scene's load frame is skipped once", Fired() == 2);
        At(133f, 6f);
        Check("pileup: the next long frame on that stage is counted", Fired() == 3);

        boom = true;
        Exception escaped = null;
        try { At(150f, 0.9f); At(161f, 0.9f); } catch (Exception e) { escaped = e; }
        Check("pileup: a failing census never escapes the frame and is warned about once",
            escaped == null && logs.Count(l => l.Contains("couldn't take a census")) == 1 && Lines() == 3);
        boom = false;
        At(172f, 0.9f);
        Check("pileup: censuses resume after a failure", Lines() == 4);
        running.SetValue(null, false);
        At(190f, 5f);
        Check("pileup: nothing after the run ends", Lines() == 4);

        // The line on its own: no colliders, a null list, unnamed objects, and only the top six kinds (ties by name).
        var describe = t.GetMethod("Describe", all);
        Func<System.Collections.Generic.List<CompatShield.PhysicsPileupTracer.Item>, string> Line = l => (string)describe.Invoke(null, new object[] { 700f, "in x (y)", l, 3.0 });
        string empty = Line(new System.Collections.Generic.List<CompatShield.PhysicsPileupTracer.Item>());
        Check("pileup: an empty census says none", empty.Contains("0 moving colliders of 0 kinds, 0 in the most crowded 4 m spot, 0 static; most: none (census 3 ms)"));
        string nulls = null;
        try { nulls = Line(null); } catch { }
        Check("pileup: a null census is treated as empty", nulls != null && nulls.Contains("most: none"));
        var eight = new System.Collections.Generic.List<CompatShield.PhysicsPileupTracer.Item>();
        foreach (var n in new[] { "H", "G", "F", "E", "D", "C", "B", "A" }) eight.Add(Moving(n, 0f, 0f, 0f, n[0], false));
        eight.Add(Moving(null, 0f, 0f, 0f, 9, false));
        eight.Add(Moving(null, 0f, 0f, 0f, 9, false));
        string top = Line(eight);
        Check("pileup: only the six biggest kinds are listed, ties in name order", top.Contains("most: ?: 2 colliders on 1 objects, 2 in one spot; A: 1") && top.Contains("10 in the most crowded 4 m spot") && top.Contains("; E: 1") && !top.Contains("; F: 1") && top.Contains("10 moving colliders of 9 kinds"));

        // The 4 m cube key.
        Func<float, float, float, long> Cell = (x, y, z) => (long)t.GetMethod("CellOf", all).Invoke(null, new object[] { x, y, z });
        Check("pileup: points in one 4 m cube share a key", Cell(0.1f, 0.1f, 0.1f) == Cell(3.9f, 3.9f, 3.9f));
        Check("pileup: the next cube over has another key on each axis", Cell(0.1f, 0.1f, 0.1f) != Cell(4.1f, 0.1f, 0.1f) && Cell(0.1f, 0.1f, 0.1f) != Cell(0.1f, 4.1f, 0.1f) && Cell(0.1f, 0.1f, 0.1f) != Cell(0.1f, 0.1f, 4.1f));
        Check("pileup: just below zero is a different cube from just above", Cell(-0.1f, 0f, 0f) != Cell(0.1f, 0f, 0f) && Cell(-0.1f, 0f, 0f) == Cell(-3.9f, 0f, 0f));
        Check("pileup: far-apart points don't collide", Cell(1000f, 0f, 0f) != Cell(0f, 1000f, 0f) && Cell(0f, 1000f, 0f) != Cell(0f, 0f, 1000f));
        bool nanOk = true;
        try { Cell(float.NaN, float.PositiveInfinity, -1e30f); } catch { nanOk = false; }
        Check("pileup: a NaN or huge position doesn't throw", nanOk);

        // The load path: Apply only subscribes to run events; the ticker is made at run start.
        var apply = t.GetMethod("Apply", all);
        var unityFromApply = UnityCallsReachableFrom(apply, shieldType.Assembly);
        Check("pileup: Apply (on the load path) reaches nothing of Unity's", unityFromApply.Count == 0);
        if (unityFromApply.Count > 0) Console.WriteLine("  " + string.Join("; ", unityFromApply));
        var unityFromStart = UnityCallsReachableFrom(t.GetMethod("OnRunStart", all), shieldType.Assembly);
        Check("pileup: the ticker is created from the run-start handler", unityFromStart.Any(s => s.Contains("GameObject..ctor")) && unityFromStart.Any(s => s.Contains("AddComponent")));
        // No real RoR2 here: Apply must take its "not applied" path quietly (the game log confirms the real subscribe).
        Exception applyError = null;
        try { apply.Invoke(null, null); } catch (Exception e) { applyError = e; }
        Check("pileup: Apply never throws; without RoR2.Run it warns once instead of claiming success",
            applyError == null && logs.Count(l => l.Contains("Physics pile-up tracer: during a run")) + logs.Count(l => l.Contains("Physics pile-up tracer: can't watch runs")) == 1);
        var gameDll = System.IO.Path.Combine(@"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed", "RoR2.dll");
        if (!System.IO.File.Exists(gameDll)) Console.WriteLine("SKIP  pileup: RoR2.dll not found");
        else {
            var runDef = Mono.Cecil.ModuleDefinition.ReadModule(gameDll).GetType("RoR2.Run");
            Check("pileup: the real RoR2.Run has static run start and destroy events",
                runDef != null && runDef.Events.Any(e => e.Name == "onRunStartGlobal" && e.AddMethod.IsStatic && e.AddMethod.IsPublic)
                && runDef.Events.Any(e => e.Name == "onRunDestroyGlobal" && e.AddMethod.IsStatic && e.AddMethod.IsPublic));
        }
        var ticker = shieldType.Assembly.GetType("CompatShield.PhysicsPileupTicker");
        Check("pileup: the ticker's Update drives Frame", ticker != null && UnityCallsReachableFrom(ticker.GetMethod("Update", all), shieldType.Assembly).Any(s => s.Contains("get_unscaledDeltaTime")));
        t.GetMethod("ResetForTest", all).Invoke(null, null);
        return failures;
    }

    // Locked items: the name list, the lock on stand-in rules (On choice excluded, titles marked once, other rules untouched),
    // the same mask rule the game uses, the wait for the rule list on the tick, shape changes logged as not applied, and the
    // real game members the lock relies on.
    public class LkChoice {
        public string localName;
        public string tooltipNameToken;
        public Func<LkChoice, string> getTooltipName = c => "name of " + c.tooltipNameToken;
        public bool excludeByDefault;
    }
    public class LkRule {
        public readonly string globalName;
        public readonly System.Collections.Generic.List<LkChoice> choices = new System.Collections.Generic.List<LkChoice>();
        public int defaultChoiceIndex;
        public LkRule(string name, params string[] names) {
            globalName = name;
            foreach (var n in names) choices.Add(new LkChoice { localName = n, tooltipNameToken = name + "." + n });
        }
    }
    public class LkBareChoice { public string localName; public bool excludeByDefault; }
    public class LkBareRule { public string globalName; public System.Collections.Generic.List<LkBareChoice> choices = new System.Collections.Generic.List<LkBareChoice>(); }
    public class LkOldChoice { public string localName; }
    public class LkOldRule { public string globalName; public System.Collections.Generic.List<LkOldChoice> choices = new System.Collections.Generic.List<LkOldChoice>(); }

    static int LockedItemsCheck(string profile, string managed) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var t = shieldType.Assembly.GetType("CompatShield.LockedItems");
        string prefix = (string)t.GetField("Prefix", all).GetValue(null);
        Check("locked: the marker matches TinkersSatchel's", prefix == "<color=#FF0000>[CONFIG DISABLED] </color>");

        var parse = t.GetMethod("Parse", all);
        var parsed = (System.Collections.Generic.HashSet<string>)parse.Invoke(null, new object[] { " ChainLightning, Items.Firework ,, equipment.JetBoots , chainlightning" });
        Check("locked: the list is trimmed, prefixes dropped, blanks and repeats ignored", parsed.Count == 3 && parsed.Contains("ChainLightning") && parsed.Contains("Firework") && parsed.Contains("JetBoots"));
        Check("locked: an empty setting is an empty list", ((System.Collections.Generic.HashSet<string>)parse.Invoke(null, new object[] { null })).Count == 0
            && ((System.Collections.Generic.HashSet<string>)parse.Invoke(null, new object[] { " , " })).Count == 0);

        Func<System.Collections.Generic.List<LkRule>> build = () => new System.Collections.Generic.List<LkRule> {
            new LkRule("Difficulty", "Easy", "Normal", "Hard"),
            new LkRule("Artifacts.Firework", "On", "Off"),
            new LkRule("Items.Bear", "On", "Off"),
            new LkRule("Items.ChainLightning", "On", "Off"),
            new LkRule("Equipment.JetBoots", "On", "Off"),
            new LkRule("Items.ChainLightningVoid", "On", "Off"),
        };
        var lockRules = t.GetMethod("LockRules", all);
        Func<System.Collections.IEnumerable, string, System.Collections.Generic.List<string>, System.Collections.Generic.List<string>, int[]> run = (rules, list, names, missing) => {
            var a = new object[] { rules, parse.Invoke(null, new object[] { list }), names, missing, 0 };
            int n = (int)lockRules.Invoke(null, a);
            return new[] { n, (int)a[4] };
        };
        var set = build();
        var lockedNames = new System.Collections.Generic.List<string>();
        var missingNames = new System.Collections.Generic.List<string>();
        var r1 = run(set, "chainlightning, JetBoots, Firework, Nope", lockedNames, missingNames);
        Check("locked: two of four names match a rule", r1[0] == 2 && lockedNames.Count == 2);
        Check("locked: an artifact of the same name and a name with no rule are reported, not locked", missingNames.Count == 2 && missingNames.Contains("Firework") && missingNames.Contains("Nope"));
        Check("locked: every title could be marked", r1[1] == 0);
        Func<string, LkRule> rule = n => set.Find(x => x.globalName == n);
        Check("locked: the item's On choice is excluded and Off is not", rule("Items.ChainLightning").choices[0].excludeByDefault && !rule("Items.ChainLightning").choices[1].excludeByDefault);
        Check("locked: equipment locks the same way", rule("Equipment.JetBoots").choices[0].excludeByDefault && !rule("Equipment.JetBoots").choices[1].excludeByDefault);
        Check("locked: other items, a longer name, artifacts and difficulty are untouched",
            !rule("Items.Bear").choices.Exists(c => c.excludeByDefault) && !rule("Items.ChainLightningVoid").choices.Exists(c => c.excludeByDefault)
            && !rule("Artifacts.Firework").choices.Exists(c => c.excludeByDefault) && !rule("Difficulty").choices.Exists(c => c.excludeByDefault));
        var on = rule("Items.ChainLightning").choices[0];
        var off = rule("Items.ChainLightning").choices[1];
        Check("locked: both titles carry the marker in front of the old title",
            on.getTooltipName(on) == prefix + "name of Items.ChainLightning.On" && off.getTooltipName(off) == prefix + "name of Items.ChainLightning.Off");
        var bear = rule("Items.Bear").choices[0];
        Check("locked: an unlocked item's title is unchanged", bear.getTooltipName(bear) == "name of Items.Bear.On");
        var again = run(set, "ChainLightning", new System.Collections.Generic.List<string>(), new System.Collections.Generic.List<string>());
        Check("locked: locking twice marks the title once", again[0] == 1 && on.getTooltipName(on) == prefix + "name of Items.ChainLightning.On");

        // A title another mod already marked (TinkersSatchel puts the same text in the name) is not marked twice.
        var marked = new LkRule("Items.TKSATLoom", "On", "Off");
        marked.choices[0].getTooltipName = c => prefix + "Unraveling Loom";
        run(new[] { marked }, "TKSATLoom", new System.Collections.Generic.List<string>(), new System.Collections.Generic.List<string>());
        Check("locked: a title that already has the marker is left as it is", marked.choices[0].getTooltipName(marked.choices[0]) == prefix + "Unraveling Loom");
        var broken = new LkRule("Items.Broken", "On", "Off");
        broken.choices[0].getTooltipName = c => { throw new InvalidOperationException("boom"); };
        run(new[] { broken }, "Broken", new System.Collections.Generic.List<string>(), new System.Collections.Generic.List<string>());
        Check("locked: a title function that throws still gives the marker", broken.choices[0].getTooltipName(broken.choices[0]) == prefix);

        // The game's mask rule (ResolveChoiceMask) and its fallback (EnforceValidRuleChoices), on the stand-ins.
        Func<LkRule, int, int> resolve = (rl, chosen) => {
            Func<LkChoice, bool> ok = c => !c.excludeByDefault;
            if (ok(rl.choices[chosen])) return chosen;
            if (ok(rl.choices[rl.defaultChoiceIndex])) return rl.defaultChoiceIndex;
            return rl.choices.FindIndex(c => ok(c));
        };
        Check("locked: a vote for On resolves to Off on a locked item", resolve(rule("Items.ChainLightning"), 0) == 1 && resolve(rule("Equipment.JetBoots"), 0) == 1);
        Check("locked: a vote for On stays On on an unlocked item", resolve(rule("Items.Bear"), 0) == 0);

        // Older or changed shapes.
        var bare = new LkBareRule { globalName = "Items.Bare" };
        bare.choices.Add(new LkBareChoice { localName = "On" });
        bare.choices.Add(new LkBareChoice { localName = "Off" });
        var rb = run(new[] { bare }, "Bare", new System.Collections.Generic.List<string>(), new System.Collections.Generic.List<string>());
        Check("locked: with no title function the lock is on and both titles are counted", rb[0] == 1 && rb[1] == 2 && bare.choices[0].excludeByDefault);
        var old = new LkOldRule { globalName = "Items.Old" };
        old.choices.Add(new LkOldChoice { localName = "On" });
        bool threw = false;
        try { run(new[] { old }, "Old", new System.Collections.Generic.List<string>(), new System.Collections.Generic.List<string>()); }
        catch (TargetInvocationException e) { threw = e.InnerException is InvalidOperationException; }
        Check("locked: a choice with no excludeByDefault flag is a shape error", threw);

        // The tick: nothing until the rule list is ready, then one lock, one unsubscribe, and silence.
        bool ready = false; int readyAsked = 0, unsub = 0;
        var tickSet = build();
        t.GetField("Wanted", all).SetValue(null, parse.Invoke(null, new object[] { "ChainLightning, Nope" }));
        t.GetField("Ready", all).SetValue(null, new Func<bool>(() => { readyAsked++; return ready; }));
        t.GetField("Rules", all).SetValue(null, new Func<System.Collections.IEnumerable>(() => tickSet));
        t.GetField("Unsubscribe", all).SetValue(null, new Action(() => unsub++));
        t.GetField("Done", all).SetValue(null, false);
        t.GetField("Ticks", all).SetValue(null, 0);
        var tick = t.GetMethod("Tick", all);
        int before = logs.Count;
        for (int i = 0; i < 60; i++) tick.Invoke(null, null);
        Check("locked: while the rule list is not ready it is asked every 25th tick and nothing is locked",
            readyAsked == 3 && unsub == 0 && !tickSet.Exists(x => x.choices.Exists(c => c.excludeByDefault)) && logs.Count == before);
        ready = true;
        for (int i = 0; i < 60; i++) tick.Invoke(null, null);
        Check("locked: once ready it locks and stops listening exactly once", unsub == 1 && tickSet.Find(x => x.globalName == "Items.ChainLightning").choices[0].excludeByDefault && (bool)t.GetField("Done", all).GetValue(null));
        Check("locked: the log names what was locked", logs.FindAll(l => l.Contains("Locked items: 1 of 2 locked off") && l.EndsWith(": ChainLightning")).Count == 1);
        Check("locked: the log names what was not found", logs.FindAll(l => l.Contains("no item or equipment is named Nope")).Count == 1);

        t.GetField("Rules", all).SetValue(null, new Func<System.Collections.IEnumerable>(() => new[] { old }));
        t.GetField("Wanted", all).SetValue(null, parse.Invoke(null, new object[] { "Old" }));
        t.GetField("Done", all).SetValue(null, false);
        t.GetField("Ticks", all).SetValue(null, 0);
        unsub = 0;
        for (int i = 0; i < 60; i++) tick.Invoke(null, null);
        Check("locked: a shape error is logged as not applied once and the tick stops", unsub == 1 && logs.FindAll(l => l.Contains("Locked items: not applied")).Count == 1);

        var apply = t.GetMethod("Apply", all);
        before = logs.Count;
        apply.Invoke(null, new object[] { null, "  " });
        Check("locked: an empty setting does nothing and logs nothing", logs.Count == before);
        apply.Invoke(null, new object[] { null, "ChainLightning" });
        Check("locked: without the game's rule list it logs not applied", logs.Count == before + 1 && logs[before].Contains("rule list was not found") && logs[before].Contains("not applied"));

        string ror2Path = System.IO.Path.Combine(managed, "RoR2.dll");
        if (!System.IO.File.Exists(ror2Path)) { Console.WriteLine("SKIP  the game is not installed here"); return failures; }
        var resolver = new Mono.Cecil.DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managed);
        var game = Mono.Cecil.AssemblyDefinition.ReadAssembly(ror2Path, new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver }).MainModule;
        var choiceDef = game.GetType("RoR2.RuleChoiceDef");
        var ruleDef = game.GetType("RoR2.RuleDef");
        var catalog = game.GetType("RoR2.RuleCatalog");
        Func<Mono.Cecil.TypeDefinition, string, Mono.Cecil.FieldDefinition> fld = (ty, n) => ty?.Fields.FirstOrDefault(f => f.Name == n);
        Check("game: RuleChoiceDef.excludeByDefault is a bool field", fld(choiceDef, "excludeByDefault")?.FieldType.FullName == "System.Boolean");
        Check("game: RuleChoiceDef.localName and tooltipNameToken are string fields", fld(choiceDef, "localName")?.FieldType.FullName == "System.String" && fld(choiceDef, "tooltipNameToken")?.FieldType.FullName == "System.String");
        Check("game: RuleChoiceDef.getTooltipName is a Func of the choice to a string", fld(choiceDef, "getTooltipName")?.FieldType.FullName == "System.Func`2<RoR2.RuleChoiceDef,System.String>");
        Check("game: RuleDef.globalName is a string field and choices a list of choices", fld(ruleDef, "globalName")?.FieldType.FullName == "System.String"
            && fld(ruleDef, "choices")?.FieldType.FullName == "System.Collections.Generic.List`1<RoR2.RuleChoiceDef>");
        Check("game: RuleCatalog.allRuleDefs is a static list of rules", fld(catalog, "allRuleDefs") != null && fld(catalog, "allRuleDefs").IsStatic
            && fld(catalog, "allRuleDefs").FieldType.FullName == "System.Collections.Generic.List`1<RoR2.RuleDef>");
        var avail = fld(catalog, "availability");
        Check("game: RuleCatalog.availability is static and has a bool 'available'", avail != null && avail.IsStatic
            && avail.FieldType.Resolve()?.Properties.Any(p => p.Name == "available" && p.PropertyType.FullName == "System.Boolean") == true);
        Check("game: RoR2Application.onFixedUpdate is a static Action event", game.GetType("RoR2.RoR2Application")?.Events.Any(e => e.Name == "onFixedUpdate" && e.EventType.FullName == "System.Action" && e.AddMethod.IsStatic) == true);
        Func<string, string, string, bool> builds = (method, ruleName, choice) => {
            var m = ruleDef?.Methods.FirstOrDefault(x => x.Name == method);
            var strings = m?.Body.Instructions.Where(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr).Select(i => (string)i.Operand).ToList();
            return strings != null && strings.Contains(ruleName) && strings.Contains(choice) && strings.Contains("Off");
        };
        Check("game: item rules are named Items.NAME with an On and an Off choice", builds("FromItem", "Items.", "On"));
        Check("game: equipment rules are named Equipment.NAME with an On and an Off choice", builds("FromEquipment", "Equipment.", "On"));
        var pre = game.GetType("RoR2.PreGameController");
        var mask = pre?.Methods.FirstOrDefault(m => m.Name == "ResolveChoiceMask");
        Check("game: the lobby mask reads excludeByDefault and then enforces valid choices",
            mask != null && mask.Body.Instructions.Any(i => (i.Operand as Mono.Cecil.FieldReference)?.Name == "excludeByDefault")
            && mask.Body.Instructions.Any(i => (i.Operand as Mono.Cecil.MethodReference)?.Name == "EnforceValidRuleChoices"));
        var cctor = catalog?.Methods.FirstOrDefault(m => m.Name == ".cctor");
        Check("game: RuleCatalog's static constructor creates no Unity objects",
            cctor == null || !cctor.Body.Instructions.Any(i => (i.Operand as Mono.Cecil.MethodReference)?.DeclaringType.FullName.StartsWith("UnityEngine.") == true));
        return failures;
    }

    // Big-hit tracer: the size test, the path's frame filter, the log line, the caps and the run-end summary, the whole flow
    // through stand-in damage and death events (monsters turned away unread, small hits, fall damage, a death already logged
    // as a big hit), failing readers, the member list against stand-ins and against the real RoR2.dll, and the load path.
    public static class Bh {
        public class GameObject { }
        public class CharacterMaster { }
        public class ItemDef { }
        public class Body {
            public bool isPlayerControlled { get; set; }
            public float damage { get; private set; }
            public int corePosition { get; private set; }
            public object inventory { get; private set; }
        }
        public class Health { public float fullCombinedHealth { get { return 0f; } } }
        public struct Combo { public int damageType; public int damageTypeExtended; public int damageSource; }
        public class Info { public float damage; public bool crit; public object inflictor; public int position; public int procChainMask; public float procCoefficient; public Combo damageType; }
        public class Report {
            public Health victim; public Body victimBody; public Info damageInfo; public object attacker; public Body attackerBody; public int attackerTeamIndex;
            public CharacterMaster attackerOwnerMaster; public int dotType; public float damageDealt; public float combinedHealthBeforeDamage;
            // Read only by this test's stand-in readers.
            public float full = 1000f; public string type = "AOE"; public string attackerName = "MercBody"; public string inflictorName = "NovaThing";
        }
        public static class Events {
            public static event Action<Report> onServerDamageDealt;
            public static event Action<Report> onCharacterDeathServer;
            public static void Damage(Report r) { var h = onServerDamageDealt; if (h != null) h(r); }
            public static void Death(Report r) { var h = onCharacterDeathServer; if (h != null) h(r); }
        }
        public static class OneEvent {
            public static event Action<Report> onServerDamageDealt;
            public static int Count { get { var h = onServerDamageDealt; return h == null ? 0 : h.GetInvocationList().Length; } }
        }
        public static class Util {
            public static string GetBestBodyName(GameObject o) { return null; }
            public static string GetBestMasterName(CharacterMaster m) { return null; }
        }
        public class Inventory {
            public int GetItemCountEffective(int index) { return 0; }
            public int GetItemCountEffective(ItemDef def) { return 0; }
        }
        public static class Items { public static ItemDef InvadingDoppelganger; }
        public class NoBody { public object victimBody; }
        // Shaped like a hook handler: its first parameter is the delegate it passes the call on to.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static string HookShaped(Func<string> orig) { string s = orig(); return s + ""; }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static string Outer(int unused, Func<string> path) { string s = HookShaped(path); return s + ""; }
    }

    // ---- hook timer stand-ins: a caller, the call inside it, a call that throws, a hook on the stand-in RecalculateStats
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int HtOuter(int x) { System.Threading.Thread.Sleep(15); return HtInner(x) + 1; }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int HtInner(int x) { System.Threading.Thread.Sleep(60); return x * 2; }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int HtCatcher(int x) { try { HtThrower(x); } catch (InvalidOperationException) { } return x; }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int HtThrower(int x) { if (x >= 0) throw new InvalidOperationException("boom"); return x; }
    static int htHookRuns;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void HtRecalcHook(Action<RoR2.CharacterBody> orig, RoR2.CharacterBody self) { htHookRuns++; System.Threading.Thread.Sleep(30); orig(self); }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void HtOtherHook(Action<RoR2.CharacterBody> orig, RoR2.CharacterBody self) { orig(self); }
    static T HtGeneric<T>(T x) { return x; }
    class HtBox<T> { public static int Plain(int x) { return x; } }
    abstract class HtAbstract { public abstract void Go(); }
    class HtWrongType { public void RecalculateStats() { } }
    static void HtHandler(int x) { }
    static int _htIlRuns;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int HtIlTarget(int x) { return x + 7; }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int HtTranspiled(int x) { return x + 9; }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int HtCapA(int x) { return x; }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int HtCapB(int x) { return x; }
    static System.Collections.Generic.IEnumerable<CodeInstruction> HtSameCode(System.Collections.Generic.IEnumerable<CodeInstruction> code) { return code; }
    static event Action<int> HtEvent;
    static Action<int> _htBacking = HtHandler;

    static int HookTimerCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var ht = shieldType.Assembly.GetType("CompatShield.HookTimer");
        var cap = shieldType.Assembly.GetType("CompatShield.HookTimerCapture");
        var me = MethodBase.GetCurrentMethod().DeclaringType;
        string asmName = me.Assembly.GetName().Name;
        Func<string, object[], object> H = (name, args) => ht.GetMethod(name, all).Invoke(null, args);
        Func<string, object[], object> C = (name, args) => cap.GetMethod(name, all).Invoke(null, args);
        Func<string, MethodBase> Mine = name => me.GetMethod(name, all);
        Func<object, string, long> F = (entry, field) => (long)entry.GetType().GetField(field).GetValue(entry);
        Action<bool> Run = on => ht.GetField("Running", all).SetValue(null, on);
        var depthProp = ht.GetProperty("DepthForTest", all);
        Func<int> Depth = () => (int)depthProp.GetValue(null);
        Func<CompatShield.HookTimer.Mark> Open = () => (CompatShield.HookTimer.Mark)H("Open", null);
        Action<CompatShield.HookTimer.Mark, long, MethodBase> Close = (mark, now, m) => H("Close", new object[] { mark, now, m });
        Func<MethodBase, object> EntryOf = m => H("EntryOf", new object[] { m });
        long ms = System.Diagnostics.Stopwatch.Frequency / 1000;
        Func<long, double> ToMs = ticks => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        MethodBase outer = Mine("HtOuter"), inner = Mine("HtInner"), catcher = Mine("HtCatcher"), thrower = Mine("HtThrower");
        var recalc = typeof(RoR2.CharacterBody).GetMethod("RecalculateStats");

        // ---- the listener that runs before any mod loads
        var paths = (string[])cap.GetField("Paths", all).GetValue(null);
        Func<System.Collections.Generic.KeyValuePair<string, MethodBase>[]> Seen = () => (System.Collections.Generic.KeyValuePair<string, MethodBase>[])C("Snapshot", null);
        C("ResetForTest", null);
        C("Note", new object[] { recalc, Mine("HtRecalcHook") });
        Check("hooktimer: a hook on a watched method is remembered with its Type::Method key",
            Seen().Length == 1 && Seen()[0].Key == "RoR2.CharacterBody::RecalculateStats" && Seen()[0].Value == Mine("HtRecalcHook"));
        C("Note", new object[] { recalc, Mine("HtRecalcHook") });
        Check("hooktimer: the same hook added again is kept once", Seen().Length == 1);
        C("Note", new object[] { outer, Mine("HtOtherHook") });
        C("Note", new object[] { typeof(HtWrongType).GetMethod("RecalculateStats"), Mine("HtOtherHook") });
        C("Note", new object[] { null, Mine("HtOtherHook") });
        C("Note", new object[] { recalc, null });
        Check("hooktimer: an unwatched method, a watched name on another type and a null are ignored", Seen().Length == 1);
        Check("hooktimer: the listener always tells MonoMod to go on, even with nothing to read", (bool)C("OnHook", new object[] { null, null, null, null }));
        Check("hooktimer: the watch list has no duplicate and every entry is Type::Method",
            paths.Length >= 30 && paths.Distinct().Count() == paths.Length && paths.All(p => p.StartsWith("RoR2.") && p.IndexOf("::") > 5 && !p.EndsWith("::")));
        var onDetour = typeof(MonoMod.RuntimeDetour.Hook).GetField("OnDetour", BindingFlags.Public | BindingFlags.Static);
        Check("hooktimer: MonoMod's Hook.OnDetour is the public static callback the listener expects",
            onDetour != null && onDetour.FieldType == typeof(Func<MonoMod.RuntimeDetour.Hook, MethodBase, MethodBase, object, bool>));
        C("Install", null);
        C("Install", null);
        var listeners = (onDetour?.GetValue(null) as Delegate)?.GetInvocationList() ?? new Delegate[0];
        Check("hooktimer: Install subscribes once, however often it is called",
            (bool)cap.GetField("Installed", all).GetValue(null) && listeners.Count(d => d.Method.DeclaringType == cap) == 1);
        var realHook = new MonoMod.RuntimeDetour.Hook(recalc, (MethodInfo)Mine("HtOtherHook"));
        Check("hooktimer: a real MonoMod hook on a watched method reaches the listener with the mod's own method",
            Seen().Length == 2 && Seen()[1].Value == Mine("HtOtherHook") && Seen()[1].Key == "RoR2.CharacterBody::RecalculateStats");
        realHook.Dispose();

        // ---- nesting maths, by hand
        H("ResetForTest", null);
        var eOuter = H("AddEntryForTest", new object[] { outer, "o", "ModO", "P", "hook" });
        var eInner = H("AddEntryForTest", new object[] { inner, "i", "ModI", "P", "game" });
        Check("hooktimer: nothing is timed outside a run", Open().Depth == -1 && Depth() == 0);
        Run(true);
        for (int round = 0; round < 2; round++) {
            var a = Open(); a.Start = 50;
            var b = Open(); b.Start = 100;
            if (round == 0) Check("hooktimer: nested calls take levels 0 and 1", a.Depth == 0 && b.Depth == 1 && Depth() == 2);
            Close(b, 160, inner);
            Close(a, 200, outer);
        }
        Check("hooktimer: the inner call keeps its whole time, the outer one only what is left (60 and 90 of 150)",
            F(eInner, "Ticks") == 120 && F(eOuter, "Ticks") == 180 && F(eInner, "Calls") == 2 && F(eOuter, "Calls") == 2 && Depth() == 0);
        Check("hooktimer: the outer call counts the timed calls it made, the inner one made none", F(eOuter, "Kids") == 2 && F(eInner, "Kids") == 0);
        Check("hooktimer: the slowest call is kept for the run and for the stage", F(eInner, "Max") == 60 && F(eOuter, "Max") == 90 && F(eOuter, "StageMax") == 90);
        {
            var a = Open(); a.Start = 1000;
            var lost = Open(); lost.Start = 1010;      // throws: never closed
            Close(a, 1100, outer);
            Check("hooktimer: a call that threw gives its level back when its caller ends, and its time stays with the caller",
                Depth() == 0 && F(eOuter, "Ticks") == 280 && F(eOuter, "Calls") == 3);
            Close(lost, 1200, inner);
            Check("hooktimer: a late end for a level already given back is ignored", F(eInner, "Calls") == 2 && Depth() == 0);
        }
        {
            var a = Open(); a.Start = 2000;
            H("FrameForTest", null);
            Close(a, 2100, outer);
            Check("hooktimer: the frame tick clears the levels, and a call left open across it is not counted", Depth() == 0 && F(eOuter, "Calls") == 3);
        }
        Close(default(CompatShield.HookTimer.Mark), 5000, outer);
        Check("hooktimer: an end with no start (Harmony skipped the prefix) is ignored", F(eOuter, "Calls") == 3 && Depth() == 0);
        {
            var p = Open(); p.Start = 3000;
            var lost = Open(); lost.Start = 3010;      // throws inside p and is caught there: never closed
            var c = Open(); c.Start = 3020;            // p goes on and makes another timed call, one level higher
            Close(c, 3050, inner);
            Close(p, 3100, outer);
            Check("hooktimer: a timed call made after a caught throw is taken out of its caller once (30 of 100) and counted as a call it made",
                lost.Depth == 1 && c.Depth == 2 && Depth() == 0 && F(eInner, "Ticks") == 150 && F(eInner, "Calls") == 3
                && F(eOuter, "Ticks") == 350 && F(eOuter, "Calls") == 4 && F(eOuter, "Kids") == 3);
        }
        depthProp.SetValue(null, 128);
        var deep = Open();
        Check("hooktimer: past 128 nested calls nothing more is timed", deep.Depth == -1 && Depth() == 128);
        depthProp.SetValue(null, 0);
        var other = new CompatShield.HookTimer.Mark();
        var thread = new System.Threading.Thread(() => other = Open());
        thread.Start(); thread.Join();
        Check("hooktimer: a call on another thread is not timed", other.Depth == -1 && Depth() == 0);
        {
            var a = Open(); a.Start = 10;
            long before = (long)ht.GetProperty("MissesForTest", all).GetValue(null);
            Close(a, 20, catcher);
            Check("hooktimer: a timed call for an unknown method is counted as a miss, not a crash",
                (long)ht.GetProperty("MissesForTest", all).GetValue(null) == before + 1 && Depth() == 0);
        }
        // ---- a method's first timed call (it pays for compiling) stays out of every count
        Func<long[]> Firsts = () => (long[])ht.GetProperty("FirstsForTest", all).GetValue(null);
        {
            H("ResetForTest", null);
            var cOuter = H("AddEntryForTest", new object[] { outer, "o", "ModO", "P", "hook" });
            var cInner = H("AddEntryForTest", new object[] { inner, "i", "ModI", "P", "game" });
            H("ColdForTest", new object[] { cOuter });
            H("ColdForTest", new object[] { cInner });
            Run(true);
            var a = Open(); a.Start = 1000;
            var b = Open(); b.Start = 1100;
            Close(b, 6100, inner);      // inner's first call: 5000 of its own
            Close(a, 9000, outer);      // outer's first call: 8000 whole, 3000 of its own
            Check("hooktimer: a method's first timed call is not counted: no call, no time, no slowest call",
                F(cInner, "Calls") == 0 && F(cInner, "Ticks") == 0 && F(cInner, "Max") == 0 && F(cInner, "StageCalls") == 0
                && F(cOuter, "Calls") == 0 && F(cOuter, "Ticks") == 0 && F(cOuter, "Kids") == 0 && F(cOuter, "StageMax") == 0 && Depth() == 0);
            Check("hooktimer: the first calls left out are summed apart, own time only (2 calls, 5000 + 3000)",
                Firsts()[0] == 2 && Firsts()[1] == 8000 && Firsts()[2] == 2 && Firsts()[3] == 8000);
            a = Open(); a.Start = 10000;
            b = Open(); b.Start = 10010;
            Close(b, 10070, inner);
            Close(a, 10100, outer);
            Check("hooktimer: from the second call on a method counts as before, and the slowest call no longer hides one (60 and 40 of 100)",
                F(cInner, "Calls") == 1 && F(cInner, "Ticks") == 60 && F(cInner, "Max") == 60 && F(cOuter, "Calls") == 1 && F(cOuter, "Ticks") == 40
                && F(cOuter, "Max") == 40 && F(cOuter, "Kids") == 1 && Firsts()[0] == 2);
            // A first call made inside a warm method: the caller still loses the whole of it, so compiling is not charged to the caller.
            H("ColdForTest", new object[] { cInner });
            a = Open(); a.Start = 20000;
            b = Open(); b.Start = 20010;
            Close(b, 27010, inner);
            Close(a, 27100, outer);
            Check("hooktimer: a first call made inside another timed call is charged to neither (the caller keeps 100 of 7100)",
                F(cOuter, "Ticks") == 140 && F(cOuter, "Calls") == 2 && F(cOuter, "Kids") == 2 && F(cInner, "Calls") == 1 && Firsts()[0] == 3 && Firsts()[1] == 15000);
            H("SetCostForTest", new object[] { 0.0, 0.0 });
            var firstRun = (System.Collections.Generic.List<string>)H("Report", new object[] { "whole run", 100.0, false });
            Check("hooktimer: the report header says how many first calls were left out and how long they took",
                firstRun[0].Contains("over 3 timed calls; the timer itself added about 0 ms on top, already taken out; 3 first calls (")
                && firstRun[0].Contains(" ms, mostly compiling the method once with the timer in it) are left out of every number. Each line is"));
            H("StageEndForTest", null);
            Check("hooktimer: a stage change starts the stage's first-call sum again and keeps the run's", Firsts()[0] == 3 && Firsts()[2] == 0 && Firsts()[3] == 0);
            Run(false);
            H("ResetForTest", null);
            Check("hooktimer: a reset clears the first-call sums", Firsts().All(v => v == 0));
        }
        H("SetCostForTest", new object[] { 2.0, 3.0 });
        Check("hooktimer: the timer's own cost is taken out (2 per call inside, 3 per timed call made) and never goes below zero",
            (double)H("Net", new object[] { 100L, 10L, 5L }) == 65.0 && (double)H("Net", new object[] { 10L, 10L, 5L }) == 0.0);

        // ---- which methods can carry the timer
        Func<MethodBase, string> Why = m => (string)H("WhyNot", new object[] { m });
        Check("hooktimer: an ordinary mod method can be timed", Why(outer) == null);
        Check("hooktimer: generated, generic, abstract and framework methods and constructors are left alone",
            Why(new System.Reflection.Emit.DynamicMethod("x", typeof(void), Type.EmptyTypes)) == "generated at run time"
            && Why(Mine("HtGeneric")) == "generic" && Why(typeof(HtBox<int>).GetMethod("Plain")) == "generic"
            && Why(typeof(HtAbstract).GetMethod("Go")) == "abstract" && Why(typeof(HtWrongType).GetConstructor(Type.EmptyTypes)) == "a constructor"
            && Why(typeof(Harmony).GetMethod("UnpatchSelf")) == "framework code" && Why(typeof(string).GetMethod("Trim", Type.EmptyTypes)) == "framework code"
            && Why(typeof(MonoMod.RuntimeDetour.Hook).GetMethod("Apply")) == "framework code" && Why(null) == "no method");
        Check("hooktimer: the timer never times itself or the lag tracer",
            Why(ht.GetMethod("Open", all)) == "part of the timers" && Why(shieldType.Assembly.GetType("CompatShield.LagTracer").GetMethod("ModName", all)) == "part of the timers");
        Func<int, int> lambda = x => x + 1;
        Check("hooktimer: labels read Mod:Type.Method, a lambda as its outer type and method",
            (string)H("LabelOf", new object[] { outer }) == asmName + ":" + me.Name + ".HtOuter"
            && (string)H("LabelOf", new object[] { lambda.Method }) == asmName + ":" + me.Name + ".HookTimerCheck");
        Check("hooktimer: a watched method reads Type.Method in reports",
            (string)H("Short", new object[] { "RoR2.UI.SkillIcon::LateUpdate" }) == "SkillIcon.LateUpdate"
            && (string)H("Short", new object[] { "RoR2.CharacterBody::RecalculateStats" }) == "CharacterBody.RecalculateStats");

        // ---- event handlers
        HtEvent += HtHandler;
        HtEvent += x => { };
        var hs = (System.Collections.Generic.List<MethodBase>)H("Handlers", new object[] { new[] { asmName, me.FullName, "HtEvent", "x" } });
        var hb = (System.Collections.Generic.List<MethodBase>)H("Handlers", new object[] { new[] { asmName, me.FullName, "htBacking", "x" } });
        var hn = (System.Collections.Generic.List<MethodBase>)H("Handlers", new object[] { new[] { asmName, me.FullName, "NoSuchEvent", "x" } });
        Check("hooktimer: every handler of a static event is found, by the event's name or its underscore field, and a missing event is no error",
            hs.Count == 2 && hs[0] == Mine("HtHandler") && hb.Count == 1 && hn.Count == 0);
        HtEvent = null;

        // ---- real Harmony timers on real methods
        H("ResetForTest", null);
        bool t1 = (bool)H("TimeForTest", new object[] { outer, "Path.A", "hook" });
        bool t2 = (bool)H("TimeForTest", new object[] { inner, "Path.A", "game" });
        var rOuter = EntryOf(outer); var rInner = EntryOf(inner);
        bool t3 = (bool)H("TimeForTest", new object[] { outer, "Path.A", "hook" });
        Check("hooktimer: the timer goes on a method once, and asking again changes nothing", t1 && t2 && t3 && rOuter != null && ReferenceEquals(rOuter, EntryOf(outer)));
        Func<object, string> Where = entry => (string)entry.GetType().GetMethod("Where").Invoke(entry, null);
        Check("hooktimer: asked again for the same game method, a timed method still names one place", Where(rOuter) == "Path.A");
        H("TimeForTest", new object[] { outer, "Path.B", "patch" });
        Check("hooktimer: a timed method found on a second game method names both, and keeps its one timer",
            Where(rOuter) == "Path.A and Path.B" && ReferenceEquals(rOuter, EntryOf(outer)) && (Harmony.GetPatchInfo(outer)?.Prefixes.Count ?? 0) == 1);
        H("TimeForTest", new object[] { outer, "Path.C", "patch" });
        H("TimeForTest", new object[] { outer, "Path.B", "patch" });
        H("TimeForTest", new object[] { outer, "Path.D", "handler" });
        Check("hooktimer: on more than two it names the first and counts the rest, each one once", Where(rOuter) == "Path.A and 3 more" && Where(rInner) == "Path.A");
        int quiet = HtOuter(1);
        Check("hooktimer: outside a run the wrapped methods run as before and nothing is counted", quiet == 3 && F(rOuter, "Calls") == 0 && F(rInner, "Calls") == 0 && Firsts()[0] == 0);
        Run(true);
        int r0 = HtOuter(0);
        Check("hooktimer: the first real call of each wrapped method is left out of its counts and summed apart (" + ToMs(Firsts()[1]).ToString("0") + " ms)",
            r0 == 1 && F(rOuter, "Calls") == 0 && F(rInner, "Calls") == 0 && F(rOuter, "Ticks") == 0 && Firsts()[0] == 2 && ToMs(Firsts()[1]) >= 60);
        int r1 = HtOuter(1), r2 = HtOuter(5);
        Run(false);
        Check("hooktimer: wrapped methods return what they returned before", r1 == 3 && r2 == 11);
        Check("hooktimer: both calls are counted, the outer one made two timed calls", F(rOuter, "Calls") == 2 && F(rInner, "Calls") == 2 && F(rOuter, "Kids") == 2 && Depth() == 0);
        Check("hooktimer: the inner method is charged its own 120 ms and the outer one only its own 30 ms (" + ToMs(F(rInner, "Ticks")).ToString("0") + " and " + ToMs(F(rOuter, "Ticks")).ToString("0") + ")",
            ToMs(F(rInner, "Ticks")) >= 100 && ToMs(F(rOuter, "Ticks")) >= 20 && ToMs(F(rOuter, "Ticks")) < 100 && F(rOuter, "Ticks") < F(rInner, "Ticks"));
        H("TimeForTest", new object[] { catcher, "Path.B", "hook" });
        H("TimeForTest", new object[] { thrower, "Path.B", "hook" });
        Run(true);
        HtCatcher(3);       // the catcher's first call, left out
        int r3 = HtCatcher(4);
        Run(false);
        Check("hooktimer: a wrapped method that throws still throws to its caller, is not counted, and leaves no level claimed",
            r3 == 4 && F(EntryOf(catcher), "Calls") == 1 && F(EntryOf(thrower), "Calls") == 0 && Depth() == 0);
        Check("hooktimer: a generic method and the timer's own methods are refused",
            !(bool)H("TimeForTest", new object[] { Mine("HtGeneric"), "Path.C", "hook" }) && !(bool)H("TimeForTest", new object[] { ht.GetMethod("Open", all), "Path.C", "hook" }));

        // A real MonoMod hook, the way a mod adds one: the hook and the game method are timed apart.
        var body = new RoR2.CharacterBody();
        using (new MonoMod.RuntimeDetour.Hook(recalc, (MethodInfo)Mine("HtRecalcHook"))) {
            bool g = (bool)H("TimeForTest", new object[] { recalc, "CharacterBody.RecalculateStats", "game" });
            bool h = (bool)H("TimeForTest", new object[] { Mine("HtRecalcHook"), "CharacterBody.RecalculateStats", "hook" });
            Run(true);
            recalc.Invoke(body, null);      // the first call of each, left out
            recalc.Invoke(body, null);
            recalc.Invoke(body, null);
            Run(false);
            var eg = EntryOf(recalc); var eh = EntryOf(Mine("HtRecalcHook"));
            Check("hooktimer: with a real hook on the game method, both take the timer and both are counted", g && h && htHookRuns == 3 && F(eh, "Calls") == 2 && F(eg, "Calls") == 2);
            Check("hooktimer: the hook is charged its own time and the game method under it almost none (" + ToMs(F(eh, "Ticks")).ToString("0") + " and " + ToMs(F(eg, "Ticks")).ToString("0.00") + " ms)",
                ToMs(F(eh, "Ticks")) >= 50 && ToMs(F(eg, "Ticks")) < 10 && F(eh, "Kids") == 2 && (string)eg.GetType().GetField("Mod").GetValue(eg) == "(game code and IL hooks)"
                && (string)eh.GetType().GetField("Mod").GetValue(eh) == asmName);
            H("UntimeForTest", new object[] { recalc });
            H("UntimeForTest", new object[] { Mine("HtRecalcHook") });
        }

        double cost = (double)H("Calibrate", null);
        Console.WriteLine("NOTE  hooktimer: one timed call costs about " + cost.ToString("0") + " ns on this PC under .NET Framework");
        Check("hooktimer: the timer measures its own cost, then takes itself back off the probe and leaves the run state as it was",
            cost >= 0 && cost < 100000 && !(bool)ht.GetField("Running", all).GetValue(null) && Depth() == 0 && EntryOf(ht.GetMethod("Probe", all)) == null
            && (Harmony.GetPatchInfo(ht.GetMethod("Probe", all))?.Prefixes.Count ?? 0) == 0);
        double insideCost = (double)ht.GetProperty("InsideForTest", all).GetValue(null), outsideCost = (double)ht.GetProperty("OutsideForTest", all).GetValue(null);
        double wholeCost = cost * System.Diagnostics.Stopwatch.Frequency / 1e9;
        Check("hooktimer: the measured cost splits into a part inside the call and a part left with its caller, and the two add up to the whole",
            insideCost >= 0 && outsideCost >= 0 && Math.Abs(insideCost + outsideCost - wholeCost) < 0.001 * Math.Max(1.0, wholeCost));
        var odd = new System.Collections.Generic.List<double> { 9, 1, 500, 3, 2 };
        Check("hooktimer: the middle of five rounds is kept, so one slow round does not set the cost", (double)H("Middle", new object[] { odd }) == 3.0);

        // ---- the limit on timed methods never drops a game method
        H("ResetForTest", null);
        int filler = 0;
        foreach (var fm in typeof(string).Assembly.GetTypes().SelectMany(ty => ty.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)).Distinct().Take(1000)) {
            H("AddEntryForTest", new object[] { fm, "f", "Filler", "P", "hook" });
            filler++;
        }
        bool capMod = (bool)H("TimeForTest", new object[] { Mine("HtCapA"), "Path.Cap", "hook" });
        bool capGame = (bool)H("TimeForTest", new object[] { Mine("HtCapB"), "Path.Cap", "game" });
        Check("hooktimer: at the limit of 1000 timed methods a mod hook is left out but a game method still takes the timer", filler == 1000 && !capMod && capGame);
        H("UntimeForTest", new object[] { Mine("HtCapB") });

        // ---- who edits a method's code: IL hooks and Harmony transpilers
        H("ResetForTest", null);
        var ilTarget = Mine("HtIlTarget");
        _htIlRuns = 0;
        var ilHook = new MonoMod.RuntimeDetour.ILHook(ilTarget, il => { if (++_htIlRuns > 1) throw new InvalidOperationException("this IL hook fails when it is run again"); });
        Check("hooktimer: a mod's IL hook on a method is named as an owner of its code",
            (string)H("IlOwnersForTest", new object[] { ilTarget, "Stand.Il" }) == asmName);
        var testHarmony = new Harmony("shieldtest.hooktimer");
        testHarmony.Patch(Mine("HtTranspiled"), transpiler: new HarmonyMethod((MethodInfo)Mine("HtSameCode")));
        Check("hooktimer: a mod's Harmony transpiler on a method is named as an owner of its code too",
            (string)H("IlOwnersForTest", new object[] { Mine("HtTranspiled"), "Stand.Tr" }) == asmName && HtTranspiled(1) == 10);
        testHarmony.UnpatchSelf();

        // ---- a timer that can't go on because another mod's IL hook fails when MonoMod runs it again
        Check("hooktimer: a method whose hooks are all in effect is not reported as stripped", !(bool)H("Stripped", new object[] { ilTarget }) && !(bool)H("Stripped", new object[] { outer }));
        Run(true);
        bool tStrip = (bool)H("TimeForTest", new object[] { ilTarget, "Stand.Il", "game" });
        bool stoppedNow = (bool)ht.GetProperty("StoppedForTest", all).GetValue(null);
        Check("hooktimer: when adding the timer leaves a method without its mods' changes, it warns once to restart and switches itself off",
            !tStrip && stoppedNow && !(bool)ht.GetField("Running", all).GetValue(null)
            && logs.Count(l => l.Contains("left the method without the changes other mods make to it") && l.Contains("HtIlTarget") && l.Contains("Restart the game")) == 1);
        Check("hooktimer: once switched off it puts no timer on anything else", !(bool)H("TimeForTest", new object[] { Mine("HtCapA"), "Path.Cap", "hook" }));
        _htIlRuns = int.MinValue;       // the stand-in hook stops failing, so MonoMod can rebuild the method and take the hook off
        try { ilHook.Dispose(); } catch (Exception e) { Console.WriteLine("NOTE  hooktimer: taking the failing stand-in hook off: " + e.GetType().Name); }
        Check("hooktimer: the stand-in method works again once its failing hook is gone", HtIlTarget(1) == 8);

        // ---- reports
        H("ResetForTest", null);
        Func<MethodBase, string, string, string, string, object> Add = (m, label, mod, path, kind) => H("AddEntryForTest", new object[] { m, label, mod, path, kind });
        var g1 = Add(outer, "RoR2:CharacterBody.RecalculateStats", "(game code and IL hooks)", "CharacterBody.RecalculateStats", "game");
        var a1 = Add(inner, "ModA:Hooks.Recalc", "ModA", "CharacterBody.RecalculateStats", "hook");
        var b1 = Add(catcher, "ModB:Hooks.Recalc", "ModB", "CharacterBody.RecalculateStats", "hook");
        var a2 = Add(thrower, "ModA:Stats.OnStats", "ModA", "RecalculateStatsAPI.GetStatCoefficients", "handler");
        Func<string, double, bool, System.Collections.Generic.List<string>> Report = (title, secs, stage) => (System.Collections.Generic.List<string>)H("Report", new object[] { title, secs, stage });
        var none = Report("whole run", 100.0, false);
        Check("hooktimer: a report with no calls says so in one line", none.Count == 1 && none[0] == "Hook timer: whole run: nothing was timed");
        H("CountForTest", new object[] { g1, 1000L, 500 * ms, 2 * ms, 3000L });
        H("CountForTest", new object[] { a1, 1000L, 300 * ms, 5 * ms, 0L });
        H("CountForTest", new object[] { b1, 1000L, 100 * ms, 1 * ms, 0L });
        H("CountForTest", new object[] { a2, 1000L, 100 * ms, 1 * ms, 0L });
        H("NoteIlOwnerForTest", new object[] { "CharacterBody.RecalculateStats", "ModC" });
        var run = Report("whole run", 100.0, false);
        foreach (var l in run) Console.WriteLine("      " + l);
        Check("hooktimer: the header gives the total, the rate and the share of the time",
            run[0].StartsWith("Hook timer: whole run, 100 s: 1000 ms inside the watched game methods and events (10.0 ms each second, 1.0% of the time) over 4000 timed calls. Each line is"));
        Check("hooktimer: mods are ranked by their own time, the game's own code as one of them",
            run[1] == "Hook timer:   mod 1. (game code and IL hooks): 500 ms (5.00 ms each second, 50% of the total), 1000 calls, slowest call 2.00 ms"
            && run[2] == "Hook timer:   mod 2. ModA: 400 ms (4.00 ms each second, 40% of the total), 2000 calls, slowest call 5.00 ms"
            && run[3] == "Hook timer:   mod 3. ModB: 100 ms (1.00 ms each second, 10% of the total), 1000 calls, slowest call 1.00 ms");
        Check("hooktimer: single hooks are ranked next, with the method or event each one sits on",
            run[4] == "Hook timer:   hook 1. ModA:Hooks.Recalc (hook on CharacterBody.RecalculateStats): 300 ms, 0.3000 ms a call, 1000 calls, slowest 5.00 ms"
            && run[5] == "Hook timer:   hook 2. ModB:Hooks.Recalc (hook on CharacterBody.RecalculateStats): 100 ms, 0.1000 ms a call, 1000 calls, slowest 1.00 ms"
            && run[6] == "Hook timer:   hook 3. ModA:Stats.OnStats (handler on RecalculateStatsAPI.GetStatCoefficients): 100 ms, 0.1000 ms a call, 1000 calls, slowest 1.00 ms");
        Check("hooktimer: the run report ends with each game method split into game code and mods, naming IL hook owners",
            run.Count == 8 && run[7] == "Hook timer:   method 1. CharacterBody.RecalculateStats: 900 ms in 1000 calls (0.9000 ms a call), game code 500 ms, mod hooks and patches 400 ms; IL hooks (counted as game code) by ModC");
        var stageLines = Report("stage golemplains", 100.0, true);
        Check("hooktimer: a stage report has the header, mods and hooks but no method lines",
            stageLines.Count == 7 && stageLines[0].StartsWith("Hook timer: stage golemplains, 100 s: 1000 ms") && !stageLines.Any(l => l.Contains("method 1.")));
        H("SetCostForTest", new object[] { 0.05 * ms, 0.1 * ms });
        var net = Report("whole run", 100.0, false);
        Check("hooktimer: with a measured timer cost the report says so and takes it out (game 500 - 50 - 300 = 150 ms, hook 300 - 50 = 250 ms)",
            net[0].Contains("; the timer itself added about 600 ms on top, already taken out. Each line is")
            && net.Any(l => l.Contains("mod 2. (game code and IL hooks): 150 ms")) && net.Any(l => l.Contains("hook 1. ModA:Hooks.Recalc (hook on CharacterBody.RecalculateStats): 250 ms, 0.2500 ms a call")));

        // ---- one patch method on several game methods: named with all of them, and kept out of the per-method split
        H("ResetForTest", null);
        var sg = Add(outer, "RoR2:CharacterBody.OnInventoryChanged", "(game code and IL hooks)", "CharacterBody.OnInventoryChanged", "game");
        var sp = Add(inner, "ModA:Patches.Dirty", "ModA", "CharacterBody.OnInventoryChanged", "patch");
        var sh = Add(catcher, "ModB:Hooks.Inv", "ModB", "CharacterBody.OnInventoryChanged", "hook");
        H("AlsoForTest", new object[] { sp, "CharacterBody.AddBuff" });
        H("AlsoForTest", new object[] { sp, "CharacterBody.RemoveBuff" });
        H("AlsoForTest", new object[] { sp, "CharacterBody.AddBuff" });
        H("AlsoForTest", new object[] { sp, "CharacterBody.OnInventoryChanged" });
        H("CountForTest", new object[] { sg, 4L, 40 * ms, 2 * ms, 0L });
        H("CountForTest", new object[] { sp, 12000L, 600 * ms, 1 * ms, 0L });
        H("CountForTest", new object[] { sh, 4L, 20 * ms, 1 * ms, 0L });
        var sharedRun = Report("whole run", 100.0, false);
        foreach (var l in sharedRun) Console.WriteLine("      " + l);
        Check("hooktimer: a patch method that sits on several game methods says so in its hook line, one that sits on one names it",
            sharedRun.Contains("Hook timer:   hook 1. ModA:Patches.Dirty (patch on CharacterBody.OnInventoryChanged and 2 more): 600 ms, 0.0500 ms a call, 12000 calls, slowest 1.00 ms")
            && sharedRun.Contains("Hook timer:   hook 2. ModB:Hooks.Inv (hook on CharacterBody.OnInventoryChanged): 20 ms, 5.0000 ms a call, 4 calls, slowest 1.00 ms"));
        Check("hooktimer: its time is not charged to the first of those methods (60 ms in 4 calls, not 660), and still counts for its mod",
            sharedRun.Contains("Hook timer:   method 1. CharacterBody.OnInventoryChanged: 60 ms in 4 calls (15.0000 ms a call), game code 40 ms, mod hooks and patches 20 ms")
            && sharedRun.Any(l => l.StartsWith("Hook timer:   mod 1. ModA: 600 ms")) && sharedRun[0].Contains(": 660 ms inside the watched"));
        Check("hooktimer: the run report ends by naming the time it could not put under one method",
            sharedRun.Count == 8 && sharedRun[7] == "Hook timer:   not in the method lines: 600 ms in 1 hook or patch method that sits on more than one game method, so the time can't be split between them (ModA:Patches.Dirty 600 ms)");
        H("AlsoForTest", new object[] { sh, "Inventory.onInventoryChangedGlobal" });
        var sharedTwo = Report("whole run", 100.0, false);
        Check("hooktimer: with two such methods the line counts both and lists them by time, and a method with only game code left still gets its line",
            sharedTwo.Contains("Hook timer:   hook 2. ModB:Hooks.Inv (hook on CharacterBody.OnInventoryChanged and Inventory.onInventoryChangedGlobal): 20 ms, 5.0000 ms a call, 4 calls, slowest 1.00 ms")
            && sharedTwo.Contains("Hook timer:   method 1. CharacterBody.OnInventoryChanged: 40 ms in 4 calls (10.0000 ms a call), game code 40 ms, mod hooks and patches 0 ms")
            && sharedTwo.Last() == "Hook timer:   not in the method lines: 620 ms in 2 hook and patch methods that each sit on more than one game method, so the time can't be split between them (ModA:Patches.Dirty 600 ms, ModB:Hooks.Inv 20 ms)");
        var sharedStage = Report("stage lakes", 100.0, true);
        Check("hooktimer: a stage report names the several methods in the hook line and has no method or left-out line",
            sharedStage.Any(l => l.Contains("(patch on CharacterBody.OnInventoryChanged and 2 more)")) && !sharedStage.Any(l => l.Contains("not in the method lines") || l.Contains("method 1.")));

        // ---- Apply never throws. The harness resolves the real RoR2.dll when it is installed, so Apply either subscribes to the real
        // run events (the start line) or warns once; never both, never neither.
        Exception applyError = null;
        try { H("Apply", null); } catch (Exception e) { applyError = e; }
        Check("hooktimer: Apply never throws; it says once either that it is watching runs or that it is not applied",
            applyError == null && logs.Count(l => l.Contains("Hook timer: from the first run start")) + logs.Count(l => l.Contains("Hook timer: can't watch runs, not applied")) == 1);

        // ---- the real members it relies on
        var ilMap = typeof(MonoMod.RuntimeDetour.ILHook).GetField("_Map", BindingFlags.NonPublic | BindingFlags.Static);
        var ilContext = typeof(MonoMod.RuntimeDetour.ILHook).GetNestedType("Context", BindingFlags.Public | BindingFlags.NonPublic);
        Check("hooktimer: MonoMod's ILHook keeps the per-method chain where the timer reads IL hook owners (_Map, Context.Chain)",
            ilMap != null && typeof(System.Collections.IDictionary).IsAssignableFrom(ilMap.FieldType) && ilContext?.GetField("Chain", all) != null
            && typeof(System.Collections.IEnumerable).IsAssignableFrom(ilContext.GetField("Chain", all).FieldType));
        var events = (string[][])ht.GetField("Events", all).GetValue(null);
        var gameDll = System.IO.Path.Combine(@"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed", "RoR2.dll");
        if (!System.IO.File.Exists(gameDll)) Console.WriteLine("SKIP  hooktimer: RoR2.dll not found");
        else using (var game = Mono.Cecil.AssemblyDefinition.ReadAssembly(gameDll)) {
            var bad = new System.Collections.Generic.List<string>();
            foreach (string key in paths) {
                int at = key.IndexOf("::");
                var td = game.MainModule.GetType(key.Substring(0, at));
                if (td == null || !td.Methods.Any(m => m.Name == key.Substring(at + 2) && m.HasBody)) bad.Add(key);
            }
            Check("hooktimer: every watched method is in the real RoR2.dll with a body (" + paths.Length + " checked" + (bad.Count > 0 ? "; missing " + string.Join(", ", bad) : "") + ")", bad.Count == 0);
            var badEvents = new System.Collections.Generic.List<string>();
            foreach (var ev in events.Where(e => e[0] == "RoR2")) {
                var td = game.MainModule.GetType(ev[1]);
                var field = td?.Fields.FirstOrDefault(f => f.IsStatic && (f.Name == ev[2] || f.Name == "_" + ev[2]));
                if (field == null) badEvents.Add(ev[1] + "." + ev[2]);
            }
            Check("hooktimer: every game event whose handlers are timed has a static delegate field in the real RoR2.dll (" + events.Count(e => e[0] == "RoR2") + " checked" + (badEvents.Count > 0 ? "; missing " + string.Join(", ", badEvents) : "") + ")", badEvents.Count == 0);
            Func<string, string, bool> StaticEvent = (type, name) => game.MainModule.GetType(type)?.Events.Any(e => e.Name == name && e.AddMethod != null && e.AddMethod.IsStatic && e.AddMethod.IsPublic) == true;
            Check("hooktimer: the run, frame and stage events it subscribes to are public static events in the real RoR2.dll",
                StaticEvent("RoR2.Run", "onRunStartGlobal") && StaticEvent("RoR2.Run", "onRunDestroyGlobal") && StaticEvent("RoR2.RoR2Application", "onUpdate") && StaticEvent("RoR2.Stage", "onStageStartGlobal"));
            var onUpdate = game.MainModule.GetType("RoR2.RoR2Application").Events.First(e => e.Name == "onUpdate");
            var onStage = game.MainModule.GetType("RoR2.Stage").Events.First(e => e.Name == "onStageStartGlobal");
            var onRun = game.MainModule.GetType("RoR2.Run").Events.First(e => e.Name == "onRunStartGlobal");
            Check("hooktimer: those events take the handler shapes the timer gives them (no argument for the frame, one object for stage and run)",
                onUpdate.EventType.FullName == "System.Action" && onStage.EventType.FullName == "System.Action`1<RoR2.Stage>" && onRun.EventType.FullName == "System.Action`1<RoR2.Run>");
        }
        var statEvent = events.First(e => e[0] != "RoR2");
        var r2Dll = System.IO.Path.Combine(profile, "plugins", "RiskofThunder-R2API_RecalculateStats", "R2API.RecalculateStats", "R2API.RecalculateStats.dll");
        if (!System.IO.File.Exists(r2Dll)) Console.WriteLine("SKIP  hooktimer: R2API.RecalculateStats.dll not found");
        else using (var r2api = Mono.Cecil.AssemblyDefinition.ReadAssembly(r2Dll)) {
            var td = r2api.MainModule.GetType(statEvent[1]);
            Check("hooktimer: R2API's stat event keeps its handlers in the static field the timer reads, in the assembly it names",
                r2api.Name.Name == statEvent[0] && td?.Fields.Any(f => f.IsStatic && (f.Name == statEvent[2] || f.Name == "_" + statEvent[2])) == true);
        }
        return failures;
    }

    static int BigHitCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var t = shieldType.Assembly.GetType("CompatShield.BigHitTracer");
        Func<string, object[], object> Call = (name, args) => t.GetMethod(name, all).Invoke(null, args);
        Func<int> Lines = () => logs.Count(l => l.Contains("Big hit:") || l.Contains("Player death:"));
        Call("ResetForTest", null);

        // The size test.
        Func<float, float, float, bool> Q = (d, b, f) => (bool)Call("Qualifies", new object[] { d, b, f });
        Check("bighit: a hit for 25% of full health counts", Q(250f, 1000f, 1000f));
        Check("bighit: a hit just under 25% does not", !Q(249f, 1000f, 1000f));
        Check("bighit: a small hit that takes the last health counts", Q(10f, 10f, 1000f));
        Check("bighit: a small hit that leaves health does not", !Q(10f, 11f, 1000f));
        Check("bighit: no damage, no health pool or a NaN never counts", !Q(0f, 0f, 1000f) && !Q(50f, 100f, 0f) && !Q(float.NaN, 100f, 1000f) && !Q(500f, 100f, float.NaN));

        // The path's frame filter.
        Func<string, string, bool, string> FN = (ty, me, hk) => (string)Call("FrameName", new object[] { ty, me, hk });
        Check("bighit: an ordinary frame reads Type.Method, nested types with dots", FN("SomeMod.Items+Nova", "Fire", false) == "SomeMod.Items.Nova.Fire");
        Check("bighit: a hook handler (orig delegate first) is left out", FN("SomeMod.Hooks", "HealthComponent_TakeDamage", true) == null);
        Check("bighit: MonoMod's copy of a patched method reads as the method",
            FN(null, "DMD<RoR2.BlastAttack::Fire>?12345", false) == "RoR2.BlastAttack.Fire"
            && FN("RoR2.BlastAttack", "DMD<System.Void RoR2.BlastAttack::HandleHits(RoR2.BlastAttack/HitPoint[])>", false) == "RoR2.BlastAttack.HandleHits");
        Check("bighit: the damage plumbing every hit shares is left out",
            FN("RoR2.HealthComponent", "TakeDamageProcess", false) == null && FN(null, "DMD<RoR2.HealthComponent::TakeDamage>?1", false) == null
            && FN("RoR2.GlobalEventManager", "ServerDamageDealt", false) == null);
        Check("bighit: hook glue, the runtime and the tracer itself are left out",
            FN(null, "Hook<SomeMod.Hooks::X>?1", false) == null && FN(null, "Trampoline<RoR2.HealthComponent::TakeDamage>?2", false) == null
            && FN("System.Action`1", "Invoke", false) == null && FN("On.RoR2.HealthComponent+orig_TakeDamage", "Invoke", false) == null
            && FN("CompatShield.BigHitTracer", "Consider", false) == null && FN(null, "Anything", false) == null && FN("X", null, false) == null);
        Check("bighit: other CompatShield frames stay (a replayed hit must show as one)", FN("CompatShield.DamageChainGuard", "Drain", false) == "CompatShield.DamageChainGuard.Drain");

        Func<System.Collections.Generic.List<string>, int, string> JP = (n, hk) => (string)Call("JoinPath", new object[] { n, hk });
        Check("bighit: the path joins innermost first with repeats collapsed", JP(new System.Collections.Generic.List<string> { "A", "A", "B", null, "C" }, 0) == "A < B < C");
        string longPath = JP(Enumerable.Range(1, 15).Select(i => "F" + i).ToList(), 3);
        Check("bighit: a long path keeps 12 frames and says what was left out", longPath.StartsWith("F1 < F2 < ") && longPath.EndsWith("F12 < ... 3 more (3 hook frames left out)") && !longPath.Contains("F13"));
        Check("bighit: an empty path is empty, or says only hooks were seen", JP(new System.Collections.Generic.List<string>(), 0) == "" && JP(null, 2) == "(2 hook frames, nothing else readable)");

        // The log line.
        var nova = new CompatShield.BigHitTracer.Hit { Victim = "Mentalize", VictimBody = "MercBody", Dealt = 1230f, Before = 1400f, Full = 1500f,
            Attacker = "Umbra of Mercenary", AttackerBody = "MercBody", AttackerTeam = "Monster", Umbra = true, Inflictor = "NovaThing", DamageType = "AOE",
            Source = "NoneSpecified", Procs = "", ProcCoefficient = 1f, Raw = 1800f, AttackerDamage = 300f, Distance = 9.4f };
        Func<CompatShield.BigHitTracer.Hit, string, string, int, string> D = (h, where, path, nth) => (string)Call("Describe", new object[] { h, where, path, nth });
        string line1 = D(nova, "in golemplains (stage 3 t=12:01)", "RoR2.BlastAttack.Fire < SomeMod.Nova.Detonate", 1);
        string want1 = "Big hit: Mentalize (MercBody) lost 82% of full health in one hit (1230 of 1500; had 1400) in golemplains (stage 3 t=12:01). From: Umbra of Mercenary (MercBody, team Monster, an Umbra). "
            + "Carried by: NovaThing. Type: AOE; no crit; proc coefficient 1; procs none; raw damage 1800 = 6x the attacker's damage stat; hit point 9 m from the victim. Path: RoR2.BlastAttack.Fire < SomeMod.Nova.Detonate";
        Check("bighit: the line names victim, share, attacker, carrier, type, procs, size and path", line1 == want1);
        if (line1 != want1) Console.WriteLine("  got: " + line1);
        var own = nova; own.Self = true; own.Umbra = false; own.Dealt = 1400f; own.Owner = "Player2"; own.Crit = true; own.Source = "Secondary"; own.Dot = "Burn";
        own.Procs = "Missile|ChainLightning"; own.ProcCoefficient = 0.25f; own.AttackerDamage = 0f; own.Inflictor = null;
        string line2 = D(own, null, null, 4);
        string want2 = "Big hit: Mentalize (MercBody) lost 93% of full health in one hit (1400 of 1500; had 1400, which took the last of it) (place unknown). From: Umbra of Mercenary (MercBody, team Monster, the victim's own body, minion of Player2). "
            + "Carried by: nothing named. Type: AOE, skill slot Secondary, damage over time Burn; crit; proc coefficient 0.25; procs Missile|ChainLightning; raw damage 1800; hit point 9 m from the victim. Hit 4 from this source, path left out";
        Check("bighit: a last-health hit from the victim's own body, a minion, a skill slot, a DoT and a left-out path all read right", line2 == want2);
        if (line2 != want2) Console.WriteLine("  got: " + line2);
        var dead = new CompatShield.BigHitTracer.Hit { Death = true, Victim = "Player2", VictimBody = "Enforcer2Body", Dealt = 5000f, Before = 0f, Full = 2000f };
        string line3 = D(dead, "in x (y)", "", 1);
        string want3 = "Player death: Player2 (Enforcer2Body) died to a hit of 5000 (250% of 2000 full health; had 0) in x (y). From: no attacker. Carried by: nothing named. "
            + "Type: ?; no crit; proc coefficient 0; procs none; raw damage 0; hit point 0 m from the victim. Path: (not readable)";
        Check("bighit: a death with nothing known still makes a full line", line3 == want3);
        if (line3 != want3) Console.WriteLine("  got: " + line3);

        // Caps and the summary.
        int paths = 0;
        t.GetField("Path", all).SetValue(null, new Func<string>(() => { paths++; return "P" + paths; }));
        Func<CompatShield.BigHitTracer.Hit, bool> Consider = h => (bool)Call("Consider", new object[] { h });
        for (int i = 0; i < 8; i++) Consider(nova);
        Check("bighit: one source gets 5 lines", Lines() == 5);
        Check("bighit: the path is on the first 2 and left out of the next 3", paths == 2 && logs.Count(l => l.Contains("Path: P")) == 2
            && logs.Count(l => l.Contains("path left out")) == 3 && logs.Any(l => l.Contains("Hit 5 from this source")));
        Check("bighit: the place comes from the Where seam", logs.Count(l => l.Contains(" in test (stage 1 t=0:00). From:")) == 5);
        var second = nova; second.Inflictor = "OtherThing";
        Check("bighit: another source still logs", Consider(second) && Lines() == 6);
        string sum = (string)Call("Summary", null);
        Check("bighit: the summary counts every hit per source, most first", sum == "Big hits this run: 9 from 2 source(s), most first: MercBody via NovaThing (AOE) x8; MercBody via OtherThing (AOE) x1");
        if (sum == null || !sum.EndsWith("x1")) Console.WriteLine("  got: " + sum);
        for (int i = 0; i < 70; i++) { var x = nova; x.Inflictor = "S" + i.ToString("00"); Consider(x); }
        Check("bighit: a run gets 60 lines and one notice", Lines() == 60 && logs.Count(l => l.Contains("Big-hit tracer: 60 lines this run")) == 1);
        sum = (string)Call("Summary", null);
        Check("bighit: past the cap hits are still counted, and the summary lists 8 sources", sum.StartsWith("Big hits this run: 79 from 72 source(s), most first: MercBody via NovaThing (AOE) x8; ") && sum.EndsWith("; ...") && sum.Split(';').Length == 9);
        Call("ResetRun", null);
        Check("bighit: a new run starts empty", Call("Summary", null) == null && Consider(nova) && Lines() == 61);
        t.GetField("Path", all).SetValue(null, new Func<string>(() => { throw new InvalidOperationException("no stack"); }));
        t.GetField("Where", all).SetValue(null, new Func<string>(() => { throw new InvalidOperationException("no scene"); }));
        bool considerOk = true;
        try { Consider(nova); } catch { considerOk = false; }
        Check("bighit: a failing path or place reader never escapes", considerOk && logs.Last().Contains("(place unknown). From:") && logs.Last().EndsWith("Path: (not readable)"));

        // The whole flow, through stand-in events.
        Call("ResetForTest", null);
        logs.Clear();
        int light = 0, full = 0;
        t.GetField("Light", all).SetValue(null, new Func<object, CompatShield.BigHitTracer.Hit>(r => {
            light++; var x = (Bh.Report)r;
            return new CompatShield.BigHitTracer.Hit { Dealt = x.damageDealt, Before = x.combinedHealthBeforeDamage, Full = x.full, Info = x.damageInfo };
        }));
        t.GetField("Full", all).SetValue(null, new Func<object, CompatShield.BigHitTracer.Hit, CompatShield.BigHitTracer.Hit>((r, h) => {
            full++; var x = (Bh.Report)r;
            h.Victim = "P"; h.VictimBody = "MercBody"; h.AttackerBody = x.attackerName; h.Inflictor = x.inflictorName; h.DamageType = x.type;
            return h;
        }));
        bool oneThrew = false, noneThrew = false;
        try { Call("Subscribe", new object[] { typeof(Bh.OneEvent) }); } catch (TargetInvocationException e) { oneThrew = e.InnerException is MissingMemberException; }
        try { Call("Subscribe", new object[] { null }); } catch (TargetInvocationException e) { noneThrew = e.InnerException is MissingMemberException; }
        Check("bighit: with one event missing nothing is subscribed", oneThrew && noneThrew && Bh.OneEvent.Count == 0);
        Call("Subscribe", new object[] { typeof(Bh.Events) });
        var player = new Bh.Body { isPlayerControlled = true };
        var monster = new Bh.Body();
        Func<Bh.Body, float, float, Bh.Report> R = (b, dealt, before) => new Bh.Report { victimBody = b, damageDealt = dealt, combinedHealthBeforeDamage = before, damageInfo = new Bh.Info() };
        Bh.Events.Damage(R(player, 900f, 1000f));
        Check("bighit: nothing is read outside a run", light == 0 && Lines() == 0);
        t.GetField("Running", all).SetValue(null, true);
        Bh.Events.Damage(R(monster, 900f, 1000f));
        Bh.Events.Damage(new Bh.Report());
        Bh.Events.Death(R(monster, 900f, 900f));
        Check("bighit: a hit on a monster, or on nothing, is turned away before anything is read", light == 0 && Lines() == 0);
        Bh.Events.Damage(R(player, 100f, 1000f));
        Check("bighit: a small hit on a player reads only the numbers", light == 1 && full == 0 && Lines() == 0);
        Bh.Events.Damage(R(player, 900f, 1000f));
        Check("bighit: a big hit on a player is logged", light == 2 && full == 1 && Lines() == 1 && logs.Last().Contains("Big hit: P (MercBody) lost 90% of full health"));
        var fall = R(player, 900f, 1000f); fall.type = "FallDamage, NonLethal";
        Bh.Events.Damage(fall);
        Check("bighit: fall damage is not a big hit", Lines() == 1);
        var lethal = R(player, 1000f, 1000f); lethal.inflictorName = "Killer";
        Bh.Events.Damage(lethal);
        Check("bighit: a hit that takes the last health says so", Lines() == 2 && logs.Last().Contains("which took the last of it"));
        Bh.Events.Death(lethal);
        Check("bighit: the death that follows that same hit is not logged twice", Lines() == 2);
        var quiet = R(player, 5f, 0f); quiet.inflictorName = "VoidFog";
        Bh.Events.Death(quiet);
        Check("bighit: a death with no big hit logged for it gets its own line", Lines() == 3 && logs.Last().Contains("Player death: P (MercBody) died to a hit of 5") && logs.Last().Contains("Carried by: VoidFog"));
        var fallDeath = R(player, 900f, 900f); fallDeath.type = "FallDamage";
        Bh.Events.Death(fallDeath);
        Check("bighit: a death from a fall is still logged", Lines() == 4);

        var bf = t.GetMethod("BuildPlayerFilter", all);
        var isPlayer = (Func<object, bool>)bf.Invoke(null, new object[] { typeof(Bh.Report) });
        Check("bighit: the compiled check is true only for a player's body", isPlayer(R(player, 1f, 1f)) && !isPlayer(R(monster, 1f, 1f)) && !isPlayer(new Bh.Report()));
        bool shapeThrew = false;
        try { bf.Invoke(null, new object[] { typeof(Bh.NoBody) }); } catch (TargetInvocationException e) { shapeThrew = e.InnerException is MissingMemberException; }
        Check("bighit: a report with no player flag is reported as a changed shape", shapeThrew);

        // Failing readers.
        int tried = 0;
        t.GetField("Light", all).SetValue(null, new Func<object, CompatShield.BigHitTracer.Hit>(r => { tried++; throw new InvalidOperationException("boom"); }));
        bool escaped = false;
        try { for (int i = 0; i < 25; i++) Bh.Events.Damage(R(player, 900f, 1000f)); } catch { escaped = true; }
        Check("bighit: a failing read never escapes, is warned about once, and the tracer stops after 20",
            !escaped && tried == 20 && logs.Count(l => l.Contains("Big-hit tracer: couldn't read a hit (logged once): InvalidOperationException: boom")) == 1
            && logs.Count(l => l.Contains("Big-hit tracer: stopped after 20 hits it couldn't read")) == 1);
        Call("ResetForTest", null);
        t.GetField("Running", all).SetValue(null, true);
        tried = 0;
        t.GetField("Light", all).SetValue(null, new Func<object, CompatShield.BigHitTracer.Hit>(r => { tried++; throw new MissingMemberException("RoR2.X.y"); }));
        for (int i = 0; i < 3; i++) Bh.Events.Damage(R(player, 900f, 1000f));
        Check("bighit: a missing game member stops it at once with a not applied line", tried == 1 && logs.Count(l => l.Contains("Big-hit tracer: the game's damage report changed shape, not applied: RoR2.X.y")) == 1);

        // The live path.
        Call("ResetForTest", null);
        var pathFn = (Func<string>)t.GetField("Path", all).GetValue(null);
        string live = Bh.Outer(0, pathFn);
        bool liveOk = live.Contains("Bh.Outer") && !live.Contains("HookShaped") && live.Contains("(1 hook frames left out)") && !live.Contains("BigHitTracer");
        Check("bighit: the live path names the caller and leaves the hook-shaped frame out", liveOk);
        if (!liveOk) Console.WriteLine("  got: " + live);

        // The member list: found on stand-ins shaped like the game's types, and the code asks for nothing outside it.
        Func<string, Type> standIn = n => n == "RoR2.GlobalEventManager" ? typeof(Bh.Events) : n == "RoR2.DamageReport" ? typeof(Bh.Report) : n == "RoR2.DamageInfo" ? typeof(Bh.Info)
            : n == "RoR2.DamageTypeCombo" ? typeof(Bh.Combo) : n == "RoR2.CharacterBody" ? typeof(Bh.Body) : n == "RoR2.HealthComponent" ? typeof(Bh.Health)
            : n == "RoR2.Util" ? typeof(Bh.Util) : n == "RoR2.Inventory" ? typeof(Bh.Inventory) : n == "RoR2.RoR2Content+Items" ? typeof(Bh.Items) : null;
        var resolveAll = t.GetMethod("ResolveAll", all);
        var needs = (string[])t.GetField("Needs", all).GetValue(null);
        System.Collections.Generic.Dictionary<string, MemberInfo> found = null;
        string resolveError = null;
        try { found = (System.Collections.Generic.Dictionary<string, MemberInfo>)resolveAll.Invoke(null, new object[] { standIn }); } catch (TargetInvocationException e) { resolveError = e.InnerException.Message; }
        Check("bighit: every listed member is found on the stand-ins", found != null && found.Count == needs.Length);
        if (resolveError != null) Console.WriteLine("  missing: " + resolveError);
        var membersType = t.GetNestedType("Members", all);
        object members = null;
        string fromError = null;
        try { members = membersType.GetMethod("From", all).Invoke(null, new object[] { found }); } catch (TargetInvocationException e) { fromError = e.InnerException.Message; }
        Check("bighit: the reader asks only for members on the list, each of the kind listed",
            members != null && membersType.GetFields(all).Where(f => typeof(MemberInfo).IsAssignableFrom(f.FieldType)).All(f => f.GetValue(members) != null));
        if (fromError != null) Console.WriteLine("  asked for: " + fromError);
        Check("bighit: the item count that takes an ItemDef is the one chosen",
            found != null && ((MethodInfo)found["RoR2.Inventory|GetItemCountEffective"]).GetParameters()[0].ParameterType == typeof(Bh.ItemDef));
        string miss = null;
        try { resolveAll.Invoke(null, new object[] { new Func<string, Type>(n => n == "RoR2.Util" ? null : standIn(n)) }); } catch (TargetInvocationException e) { miss = e.InnerException is MissingMemberException ? e.InnerException.Message : null; }
        Check("bighit: missing members are all named at once", miss == "RoR2.Util.GetBestBodyName, RoR2.Util.GetBestMasterName");

        // The load path: Apply only subscribes to run events; the damage and death events wait for the first run start.
        var apply = t.GetMethod("Apply", all);
        var unityFromApply = UnityCallsReachableFrom(apply, shieldType.Assembly);
        Check("bighit: Apply (on the load path) reaches nothing of Unity's", unityFromApply.Count == 0);
        if (unityFromApply.Count > 0) Console.WriteLine("  " + string.Join("; ", unityFromApply));
        var unityFromStart = UnityCallsReachableFrom(t.GetMethod("OnRunStart", all), shieldType.Assembly);
        Check("bighit: the run-start handler creates no Unity object", !unityFromStart.Any(s => s.Contains("GameObject..ctor") || s.Contains("AddComponent")));
        Exception applyError = null;
        try { apply.Invoke(null, null); } catch (Exception e) { applyError = e; }
        Check("bighit: Apply never throws; without RoR2.Run it warns once instead of claiming success",
            applyError == null && logs.Count(l => l.Contains("Big-hit tracer: during a run")) + logs.Count(l => l.Contains("Big-hit tracer: can't watch runs")) == 1);

        var gameDll = System.IO.Path.Combine(@"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed", "RoR2.dll");
        if (!System.IO.File.Exists(gameDll)) Console.WriteLine("SKIP  bighit: RoR2.dll not found");
        else {
            var mod = Mono.Cecil.ModuleDefinition.ReadModule(gameDll);
            var bad = new System.Collections.Generic.List<string>();
            foreach (var row in needs) {
                var p = row.Split('|');
                var td = mod.GetType(p[0].Replace('+', '/'));
                bool ok = td != null && (
                    p[2] == "event" ? td.Events.Any(e => e.Name == p[1] && e.AddMethod.IsStatic && e.AddMethod.IsPublic && e.EventType.FullName == "System.Action`1<RoR2.DamageReport>")
                    : p[2] == "field" ? td.Fields.Any(f => f.Name == p[1])
                    : p[2] == "property" ? td.Properties.Any(pr => pr.Name == p[1] && pr.GetMethod != null)
                    : td.Methods.Any(me => me.Name == p[1] && me.Parameters.Count == 1 && me.Parameters[0].ParameterType.Name == p[3]));
                if (!ok) bad.Add(row);
            }
            Check("bighit: every game member the tracer reads is in the real RoR2.dll (" + needs.Length + " checked)", bad.Count == 0);
            if (bad.Count > 0) Console.WriteLine("  missing: " + string.Join("; ", bad));
            Func<string, string, string> FieldKind = (type, name) => { var td = mod.GetType(type); var f = td == null ? null : td.Fields.FirstOrDefault(x => x.Name == name); return f == null ? null : f.FieldType.FullName; };
            Func<string, string, string> PropKind = (type, name) => { var td = mod.GetType(type); var pr = td == null ? null : td.Properties.FirstOrDefault(x => x.Name == name); return pr == null ? null : pr.PropertyType.FullName; };
            Check("bighit: the report's fields are the kinds the reader casts them to",
                FieldKind("RoR2.DamageReport", "damageDealt") == "System.Single" && FieldKind("RoR2.DamageReport", "combinedHealthBeforeDamage") == "System.Single"
                && FieldKind("RoR2.DamageReport", "attacker") == "UnityEngine.GameObject" && FieldKind("RoR2.DamageReport", "victimBody") == "RoR2.CharacterBody"
                && FieldKind("RoR2.DamageReport", "attackerBody") == "RoR2.CharacterBody" && FieldKind("RoR2.DamageReport", "victim") == "RoR2.HealthComponent"
                && FieldKind("RoR2.DamageReport", "damageInfo") == "RoR2.DamageInfo");
            Check("bighit: the damage info's fields are the kinds the reader casts them to",
                FieldKind("RoR2.DamageInfo", "damage") == "System.Single" && FieldKind("RoR2.DamageInfo", "crit") == "System.Boolean"
                && FieldKind("RoR2.DamageInfo", "procCoefficient") == "System.Single" && FieldKind("RoR2.DamageInfo", "inflictor") == "UnityEngine.GameObject"
                && FieldKind("RoR2.DamageInfo", "position") == "UnityEngine.Vector3" && FieldKind("RoR2.DamageInfo", "damageType") == "RoR2.DamageTypeCombo");
            Check("bighit: the body and health properties are the kinds the reader casts them to",
                PropKind("RoR2.CharacterBody", "isPlayerControlled") == "System.Boolean" && PropKind("RoR2.CharacterBody", "damage") == "System.Single"
                && PropKind("RoR2.CharacterBody", "corePosition") == "UnityEngine.Vector3" && PropKind("RoR2.CharacterBody", "inventory") == "RoR2.Inventory"
                && PropKind("RoR2.HealthComponent", "fullCombinedHealth") == "System.Single");
            var util = mod.GetType("RoR2.Util");
            var inv = mod.GetType("RoR2.Inventory");
            var items = mod.GetType("RoR2.RoR2Content/Items");
            Check("bighit: the name helpers are static and return text, the item count returns a number, the Umbra item is a static ItemDef",
                util.Methods.Any(me => me.Name == "GetBestBodyName" && me.IsStatic && me.ReturnType.FullName == "System.String" && me.Parameters.Count == 1 && me.Parameters[0].ParameterType.FullName == "UnityEngine.GameObject")
                && util.Methods.Any(me => me.Name == "GetBestMasterName" && me.IsStatic && me.ReturnType.FullName == "System.String" && me.Parameters.Count == 1 && me.Parameters[0].ParameterType.FullName == "RoR2.CharacterMaster")
                && inv.Methods.Any(me => me.Name == "GetItemCountEffective" && !me.IsStatic && me.ReturnType.FullName == "System.Int32" && me.Parameters.Count == 1 && me.Parameters[0].ParameterType.FullName == "RoR2.ItemDef")
                && items != null && items.Fields.Any(f => f.Name == "InvadingDoppelganger" && f.IsStatic && f.FieldType.FullName == "RoR2.ItemDef"));
            var hc = mod.GetType("RoR2.HealthComponent");
            var process = hc.Methods.FirstOrDefault(me => me.Name == "TakeDamageProcess");
            Check("bighit: the game still reports each hit through GlobalEventManager.ServerDamageDealt",
                process != null && process.HasBody && process.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference && ((Mono.Cecil.MethodReference)i.Operand).FullName.Contains("RoR2.GlobalEventManager::ServerDamageDealt")));
        }
        Call("ResetForTest", null);
        return failures;
    }

    static int AnchorGuardCheck(string profile, string managed) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        var fix = shieldType.Assembly.GetType("CompatShield.SharpAnchorZoneFix");
        var bind = fix.GetMethod("Bind", all);
        Check("anchor: no match when the item has no zone list", !(bool)bind.Invoke(null, new object[] { typeof(FakeNoZones), typeof(FakeStormyMain) }));
        Check("anchor: no match when the body list is missing", !(bool)bind.Invoke(null, new object[] { typeof(FakeStormyItem), typeof(FakeStormyItem) }));
        Check("anchor: binds to the zone list, the body list and the body's inventory", (bool)bind.Invoke(null, new object[] { typeof(FakeStormyItem), typeof(FakeStormyMain) }));
        bool blowUp = false;
        fix.GetField("GameObjectOf", all).SetValue(null, new Func<object, object>(o => { if (blowUp) throw new InvalidOperationException("boom"); return (o as FakeAnchorZone)?.go; }));
        fix.GetMethod("ResetForTest", all).Invoke(null, null);
        var prefix = fix.GetMethod("Prefix", all);
        Func<object, bool> Run = z => (bool)prefix.Invoke(null, new object[] { z });

        // Slot 0 empty (the mod adds null for a body with no zone), then zones for a held body, a body with no inventory,
        // no body, and a slot past the end of the body list.
        var held = new FakeAnchorZone(); var noInv = new FakeAnchorZone(); var noBody = new FakeAnchorZone(); var pastEnd = new FakeAnchorZone();
        var stray = new FakeAnchorZone(); var noObject = new FakeAnchorZone { go = null };
        FakeStormyItem.Zones.Clear(); FakeStormyMain.CharBodies.Clear();
        FakeStormyItem.Zones.AddRange(new object[] { null, held.go, noInv.go, noBody.go, pastEnd.go });
        FakeStormyMain.CharBodies.AddRange(new[] { new FakeAnchorBody { inventory = new object() }, new FakeAnchorBody { inventory = new object() }, new FakeAnchorBody(), null });
        Check("anchor: a zone whose holder has an inventory runs as before", Run(held));
        Check("anchor: a zone not in the list runs as before (the mod counts 0 anchors there)", Run(stray));
        Check("anchor: a zone with no object runs as before and doesn't match an empty slot", Run(noObject));
        Check("anchor: a zone whose holder has no inventory is skipped", !Run(noInv));
        Check("anchor: a zone whose slot holds no body is skipped", !Run(noBody));
        Check("anchor: a zone whose slot is past the body list is skipped", !Run(pastEnd));
        Check("anchor: skipping is logged once", logs.Count(l => l.Contains("its tick was skipped")) == 1);
        blowUp = true;
        bool ranAnyway = Run(noInv) && Run(noInv);
        Check("anchor: a problem in the check lets the mod's code run, logged once", ranAnyway && logs.Count(l => l.Contains("couldn't check a zone's holder")) == 1);
        blowUp = false;

        bool baseline = false;
        try { noInv.BuffOccupants(); } catch (NullReferenceException) { baseline = true; }
        Check("anchor: unguarded, the stand-in throws like the mod did", baseline && noInv.buffed == 0);
        var h = new Harmony("test.anchorguard");
        h.Patch(typeof(FakeAnchorZone).GetMethod("BuffOccupants"), prefix: new HarmonyMethod(prefix));
        bool clean = true;
        try { for (int i = 0; i < 3; i++) { noInv.BuffOccupants(); pastEnd.BuffOccupants(); held.BuffOccupants(); } } catch { clean = false; }
        Check("anchor: guarded, a lost zone ticks without throwing and a held zone still buffs", clean && noInv.buffed == 0 && pastEnd.buffed == 0 && held.buffed == 3);
        FakeStormyItem.Zones.Clear(); FakeStormyMain.CharBodies.Clear();

        var gameModule = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var getInv = gameModule.GetType("RoR2.CharacterBody")?.Methods.FirstOrDefault(m => m.Name == "get_inventory" && !m.HasParameters);
        var invBody = getInv?.Body.Instructions.Where(i => i.OpCode != Mono.Cecil.Cil.OpCodes.Nop).ToList();
        Check("anchor real: CharacterBody.inventory is a plain field read, so the guard's read has no side effects",
            invBody != null && invBody.Count == 3 && invBody[1].OpCode == Mono.Cecil.Cil.OpCodes.Ldfld && invBody[2].OpCode == Mono.Cecil.Cil.OpCodes.Ret);

        var plugins = System.IO.Path.Combine(profile, "plugins");
        var dll = System.IO.Directory.GetFiles(plugins, "StormyItems.dll", System.IO.SearchOption.AllDirectories).FirstOrDefault();
        if (dll == null) { Console.WriteLine("SKIP  anchor real: StormyItems.dll not installed"); return failures; }
        var m = Mono.Cecil.ModuleDefinition.ReadModule(dll);
        var zoneType = m.GetType((string)fix.GetField("ZoneName", all).GetValue(null));
        var itemType = m.GetType((string)fix.GetField("ItemName", all).GetValue(null));
        var mainType = m.GetType((string)fix.GetField("MainName", all).GetValue(null));
        var zonesF = itemType?.Fields.FirstOrDefault(f => f.Name == "Zones");
        var bodiesF = mainType?.Fields.FirstOrDefault(f => f.Name == "CharBodies");
        Check("anchor real: the item keeps its zones in a static List of GameObject",
            zonesF != null && zonesF.IsStatic && zonesF.FieldType.FullName == "System.Collections.Generic.List`1<UnityEngine.GameObject>");
        Check("anchor real: the plugin keeps its bodies in a static List of CharacterBody",
            bodiesF != null && bodiesF.IsStatic && bodiesF.FieldType.FullName == "System.Collections.Generic.List`1<RoR2.CharacterBody>");
        var buff = zoneType?.Methods.FirstOrDefault(x => x.Name == "BuffOccupants" && !x.HasParameters && !x.IsStatic);
        var tick = zoneType?.Methods.FirstOrDefault(x => x.Name == "FixedUpdate")?.Body.Instructions;
        Check("anchor real: the zone's FixedUpdate runs BuffOccupants, an instance method with no arguments",
            buff != null && tick != null && tick.Any(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "BuffOccupants"));
        var buffIl = buff?.Body.Instructions.ToList();
        int countAt = buffIl == null ? -1 : buffIl.FindIndex(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "GetHolderAnchorCount");
        int addAt = buffIl == null ? -1 : buffIl.FindIndex(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "AddTimedBuff");
        Check("anchor real: BuffOccupants reads the holder's anchor count before it adds the buff", countAt >= 0 && addAt > countAt);
        var lookup = zoneType?.Methods.FirstOrDefault(x => x.Name == "GetHolderAnchorCount")?.Body.Instructions;
        Check("anchor real: the holder lookup reads the zone list, the body list and the body's inventory",
            lookup != null
            && lookup.Any(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldsfld && ((Mono.Cecil.FieldReference)i.Operand).Name == "Zones")
            && lookup.Any(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldsfld && ((Mono.Cecil.FieldReference)i.Operand).Name == "CharBodies")
            && lookup.Any(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "get_inventory"));
        return failures;
    }

    // Stand-in with the sacrifice totem state's field names. Its FixedUpdate throws the way the game's does when the first pick is gone.
    sealed class FakeTotemState {
        public bool _hasKilledAllies;
        public object[] alliesToRapture = new object[0];
        public object tetherRootTransform, tetherEffectInstance, tetherEffectEndTransform, tetherEffectInstance_efh;
        public int ticks;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void FixedUpdate() {
            if (!_hasKilledAllies && tetherEffectInstance != null) {
                string from = tetherRootTransform.ToString();
                string to = tetherEffectEndTransform.ToString() + alliesToRapture[0].ToString();
            }
            ticks++;
        }
    }

    // Sacrifice totem tether: the guard keeps the state's FixedUpdate from throwing every tick when its first pick is gone.
    static int TotemCheck(string managed) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var fix = shieldType.Assembly.GetType("CompatShield.TotemTetherGuard");
        bool blowUp = false; int putAway = 0;
        fix.GetField("Alive", all).SetValue(null, new Func<object, bool>(o => { if (blowUp) throw new InvalidOperationException("boom"); return o != null && !"dead".Equals(o); }));
        fix.GetField("PutAway", all).SetValue(null, new Action<object>(o => {
            putAway++;
            var s = (FakeTotemState)o;
            s.tetherEffectInstance = null; s.tetherEffectInstance_efh = null; s.tetherEffectEndTransform = null;
        }));
        Check("totem: binds to a state with the game's field names", (bool)fix.GetMethod("Bind", all).Invoke(null, new object[] { typeof(FakeTotemState) }));
        fix.GetMethod("ResetForTest", all).Invoke(null, null);
        var tidy = fix.GetMethod("Tidy", all);
        Func<FakeTotemState, int> Tidy = s => (int)tidy.Invoke(null, new object[] { s });
        Func<object[], FakeTotemState> Make = picks => new FakeTotemState {
            alliesToRapture = picks, tetherRootTransform = "root", tetherEffectInstance = "tether", tetherEffectEndTransform = "end", tetherEffectInstance_efh = "pool" };

        var s1 = Make(new object[] { "a", "b" });
        Check("totem: with its first pick alive, nothing changes", Tidy(s1) == 0 && (string)s1.alliesToRapture[0] == "a" && putAway == 0);
        var s2 = Make(new object[] { null, "dead", "c" });
        Check("totem: a gone first pick swaps with the next living one, skipping dead ones",
            Tidy(s2) == 1 && (string)s2.alliesToRapture[0] == "c" && (string)s2.alliesToRapture[1] == "dead" && s2.alliesToRapture[2] == null && putAway == 0);
        Check("totem: the swap keeps the tether", (string)s2.tetherEffectInstance == "tether");
        var s3 = Make(new object[] { "dead", null });
        Check("totem: with no living pick left, the tether is put away", Tidy(s3) == 2 && putAway == 1 && s3.tetherEffectInstance == null);
        Check("totem: once put away there is nothing left to do", Tidy(s3) == 0 && putAway == 1);
        Check("totem: an empty pick list puts the tether away", Tidy(Make(new object[0])) == 2 && putAway == 2);
        Check("totem: a missing pick list puts the tether away", Tidy(Make(null)) == 2 && putAway == 3);
        var s4 = Make(new object[] { "a" }); s4.tetherEffectEndTransform = null;
        Check("totem: a tether with no end is put away even with a living pick", Tidy(s4) == 2 && putAway == 4);
        var s5 = Make(new object[] { "a", "b" }); s5.tetherRootTransform = "dead";
        Check("totem: a tether whose root is gone is put away", Tidy(s5) == 2 && putAway == 5);
        var s6 = Make(new object[] { null }); s6._hasKilledAllies = true;
        Check("totem: after the sacrifice it leaves the state alone", Tidy(s6) == 0 && putAway == 5);
        var s7 = Make(new object[] { null }); s7.tetherEffectInstance = null;
        Check("totem: with no tether it leaves the state alone", Tidy(s7) == 0 && putAway == 5);
        Check("totem: moving and putting away each log once",
            logs.Count(l => l.Contains("tether now follows the next one")) == 1 && logs.Count(l => l.Contains("tether was put away")) == 1);

        var prefix = fix.GetMethod("Prefix", all);
        blowUp = true; logs.Clear();
        bool threw = false;
        try {
            prefix.Invoke(null, new object[] { Make(new object[] { null }) });
            prefix.Invoke(null, new object[] { Make(new object[] { null }) });
        } catch { threw = true; }
        Check("totem: a problem in the check never reaches the game", !threw);
        Check("totem: that problem is logged once", logs.Count(l => l.Contains("couldn't check a sacrifice totem's tether")) == 1);
        blowUp = false;

        var unguarded = Make(new object[] { null, "b" });
        bool baseline = false;
        try { unguarded.FixedUpdate(); } catch (NullReferenceException) { baseline = true; }
        Check("totem: unguarded, the stand-in throws like the game did", baseline && unguarded.ticks == 0);
        var h = new Harmony("test.totem");
        h.Patch(typeof(FakeTotemState).GetMethod("FixedUpdate"), prefix: new HarmonyMethod(prefix));
        bool clean = true;
        var moved = Make(new object[] { null, "b" });
        var empty = Make(new object[] { null });
        try { for (int i = 0; i < 3; i++) { moved.FixedUpdate(); empty.FixedUpdate(); } } catch { clean = false; }
        Check("totem: guarded, every tick finishes with a gone first pick", clean && moved.ticks == 3 && empty.ticks == 3);
        Check("totem: guarded, the tether follows the living pick", (string)moved.alliesToRapture[0] == "b" && (string)moved.tetherEffectInstance == "tether");

        var ror2 = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var state = ror2.GetType((string)fix.GetField("StateName", all).GetValue(null));
        Func<string, string> FieldType = n => state?.Fields.FirstOrDefault(f => f.Name == n)?.FieldType.FullName;
        Check("totem real: the state still has its tether fields with the expected types",
            FieldType("_hasKilledAllies") == "System.Boolean" && FieldType("alliesToRapture") == "UnityEngine.GameObject[]"
            && FieldType("tetherRootTransform") == "UnityEngine.Transform" && FieldType("tetherEffectInstance") == "UnityEngine.GameObject"
            && FieldType("tetherEffectEndTransform") == "UnityEngine.Transform" && FieldType("tetherEffectInstance_efh") == "RoR2.EffectManagerHelper");
        var pool = ror2.GetType((string)fix.GetField("PoolName", all).GetValue(null));
        Check("totem real: the effect pool still has the release the fix calls",
            pool?.Methods.Any(m => m.Name == "ReturnToPoolOrDestroyInstance" && m.IsStatic && m.Parameters.Count == 2
                && m.Parameters[0].ParameterType.FullName == "RoR2.EffectManagerHelper" && m.Parameters[1].ParameterType.FullName == "UnityEngine.GameObject&") == true);
        var code = state?.Methods.FirstOrDefault(m => m.Name == "FixedUpdate")?.Body.Instructions.ToList();
        int killed = code == null ? -1 : code.FindIndex(i => i.Operand is Mono.Cecil.FieldReference f && f.Name == "_hasKilledAllies");
        int first = code == null ? -1 : code.FindIndex(i => i.Operand is Mono.Cecil.FieldReference f && f.Name == "alliesToRapture");
        Check("totem real: FixedUpdate still reads the first pick, after the same check the guard makes",
            killed >= 0 && first > killed && code[first + 1].OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_0 && code[first + 2].OpCode == Mono.Cecil.Cil.OpCodes.Ldelem_Ref);
        int sacrifice = code == null ? -1 : code.FindIndex(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "KillAlliesServer");
        Check("totem real: the sacrifice comes after the tether lines, so a throw there skipped it", sacrifice > first);
        return failures;
    }

    // The settings file: BepInEx's own save when it works, CompatShield's when it doesn't, and values surviving both.
    static int ConfigSaveCheck() {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var fix = shieldType.Assembly.GetType("CompatShield.ConfigMigration");
        var ensure = fix.GetMethod("EnsureSaved", all);
        var matches = fix.GetMethod("Matches", all);
        var seam = fix.GetField("SaveConfig", all);
        object realSave = seam.GetValue(null);
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "shield-cfgsave-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        string path = System.IO.Path.Combine(dir, "x.cfg");
        typeof(BepInEx.Paths).GetProperty("BepInExConfigPath").SetValue(null, System.IO.Path.Combine(dir, "BepInEx.cfg")); // ConfigFile's static constructor opens this
        const string raw = "[Fixes]\n## Old text\n\n[1. Spawns]\nKept = 7\n\n[1. Spawns]\nAlso = 9\n\n[Old]\n## nothing here\n";
        Func<BepInEx.Configuration.ConfigFile> Open = () => {
            var c = new BepInEx.Configuration.ConfigFile(path, false);
            c.SaveOnConfigSet = false;
            c.Bind("1. Spawns", "Kept", 1, "kept text");
            c.Bind("1. Spawns", "Also", 2, "also text");
            c.Bind("2. Other", "Added", true, "a setting the file has never had");
            return c;
        };
        Func<string> Text = () => System.IO.File.ReadAllText(path);
        Func<string, int> Headers = name => System.Text.RegularExpressions.Regex.Matches(Text(), "^\\[" + System.Text.RegularExpressions.Regex.Escape(name) + "\\]", System.Text.RegularExpressions.RegexOptions.Multiline).Count;
        Func<BepInEx.Configuration.ConfigFile, bool> Same = c => (bool)matches.Invoke(null, new object[] { Text(), c });

        // A working save.
        System.IO.File.WriteAllText(path, raw);
        var cfg = Open();
        Check("cfgsave: the file as the move leaves it doesn't count as saved", !Same(cfg));
        logs.Clear();
        ensure.Invoke(null, new object[] { cfg });
        Check("cfgsave: BepInEx's save is enough when it works, with no warning", Same(cfg) && logs.Count == 0);
        Check("cfgsave: one heading per category and the old ones gone", Headers("1. Spawns") == 1 && Headers("2. Other") == 1 && Headers("Fixes") == 0 && Headers("Old") == 0);
        Check("cfgsave: saved values stay and the new setting appears", Text().Contains("Kept = 7") && Text().Contains("Also = 9") && Text().Contains("Added = true"));

        // A save that does nothing, as seen on 2026-11-09.
        System.IO.File.WriteAllText(path, raw);
        cfg = Open();
        seam.SetValue(null, new Action<BepInEx.Configuration.ConfigFile>(c => { }));
        logs.Clear();
        ensure.Invoke(null, new object[] { cfg });
        seam.SetValue(null, realSave);
        Check("cfgsave: when BepInEx's save does nothing, the file is written anyway", Same(cfg));
        Check("cfgsave: and the log says so: " + string.Join("|", logs), logs.Count(l => l.Contains("BepInEx's save didn't update the file")) == 1);
        Check("cfgsave: the written file has one heading per category and no old ones", Headers("1. Spawns") == 1 && Headers("2. Other") == 1 && Headers("Fixes") == 0 && Headers("Old") == 0);
        Check("cfgsave: the written file keeps each setting's description", Text().Contains("a setting the file has never had"));
        var again = Open();
        Check("cfgsave: every value reads back the same", again.Bind("1. Spawns", "Kept", 1, "x").Value == 7 && again.Bind("1. Spawns", "Also", 2, "x").Value == 9 && again.Bind("2. Other", "Added", true, "x").Value);

        // Reading the file.
        System.IO.File.WriteAllText(path, "[1. Spawns]\nXKept = 7\nAlso = 9\n\n[2. Other]\nAdded = true\n");
        Check("cfgsave: a longer name holding the key's name doesn't count as the key", !Same(cfg));
        System.IO.File.WriteAllText(path, "[Fixes]\n## Old text\n\n[1. Spawns]\nKept = 7\nAlso = 9\n\n[2. Other]\nAdded = true\n");
        Check("cfgsave: a heading with nothing under it counts as untidy", !Same(cfg));
        System.IO.File.WriteAllText(path, "[1. Spawns]\nKept = 7\nAlso = 9\n\n[2. Other]\nAdded = true\n\n[Retired]\nGone = 1\n");
        Check("cfgsave: settings no longer used don't count as untidy, BepInEx keeps those", Same(cfg));

        // A problem must never stop the launch.
        seam.SetValue(null, new Action<BepInEx.Configuration.ConfigFile>(c => { throw new InvalidOperationException("boom"); }));
        logs.Clear();
        ensure.Invoke(null, new object[] { cfg });
        seam.SetValue(null, realSave);
        Check("cfgsave: a problem is logged and doesn't stop the launch", logs.Count(l => l.Contains("couldn't tidy the settings file")) == 1);
        System.IO.Directory.Delete(dir, true);
        return failures;
    }

    static int HardCapCheck(string managed, string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var cap = shieldType.Assembly.GetType("CompatShield.HardEnemyCap");
        Action<string, object> Set = (n, v) => cap.GetField(n, all).SetValue(null, v);
        int alive = 0;
        bool hostile = true, ignores = true, champion = false, masterHostile = true, blowUp = false;
        string master = "VoidInfestorMaster(Clone)";
        var frames = new System.Collections.Generic.List<MethodBase>();
        var names = new System.Collections.Generic.List<string> { "BeetleBody(Clone)", "BeetleBody(Clone)", "BeetleBody(Clone)", "WispBody(Clone)" };
        var destroyed = new System.Collections.Generic.List<object>();
        Set("Alive", new Func<int>(() => { if (blowUp) throw new InvalidOperationException("boom"); return alive; }));
        Set("AliveNames", new Func<System.Collections.Generic.IEnumerable<string>>(() => names));
        Set("Scene", new Func<string>(() => "goolake"));
        Set("SummonHostile", new Func<object, bool>(o => hostile));
        Set("IgnoresLimit", new Func<object, bool>(o => ignores));
        Set("SummonChampion", new Func<object, bool>(o => champion));
        Set("PrefabName", new Func<object, string>(o => "EngiMaster"));
        int frameNo = 1, walks = 0; bool host = true;
        Set("Frame", new Func<int>(() => frameNo));
        Set("IsHost", new Func<bool>(() => host));
        Set("Frames", new Func<System.Collections.Generic.List<MethodBase>>(() => { walks++; return frames; }));
        Set("MasterName", new Func<object, string>(o => master));
        Set("MasterHostile", new Func<object, bool>(o => masterHostile));
        Set("DestroyMaster", new Action<object>(o => destroyed.Add(o)));
        var reset = cap.GetMethod("ResetForTest", all);
        var perform = cap.GetMethod("PerformPrefix", all);
        var spawnBody = cap.GetMethod("SpawnBodyPrefix", all);
        var scriptedBy = cap.GetMethod("ScriptedBy", all);
        var callerOf = cap.GetMethod("CallerOf", all);
        var checkNow = cap.GetMethod("CheckNow", all);
        var summary = cap.GetMethod("Summary", all);
        var endStage = cap.GetMethod("EndStage", all);
        object result = null;
        Func<bool> P = () => { var a = new object[] { "summon", "untouched" }; bool go = (bool)perform.Invoke(null, a); result = a[1]; return go; };
        Func<object, bool> B = o => (bool)spawnBody.Invoke(null, new object[] { o });
        Func<string> S = () => (string)summary.Invoke(null, null);
        Action Tick = () => checkNow.Invoke(null, null);
        Action<int> Reset = n => reset.Invoke(null, new object[] { n });
        Func<MethodBase[], string> Script = f => (string)scriptedBy.Invoke(null, new object[] { new System.Collections.Generic.List<MethodBase>(f) });
        Func<MethodBase[], string> Caller = f => (string)callerOf.Invoke(null, new object[] { new System.Collections.Generic.List<MethodBase>(f) });
        MethodBase mod = typeof(Program).GetMethod("HardCapCheck", all);
        MethodBase scripted = new FakeMethod("DMD<RoR2.ScriptedCombatEncounter::Spawn>");
        MethodBase director = new FakeMethod("DMD<RoR2.CombatDirector::Spawn>");
        MethodBase death = new FakeMethod("DMD<RoR2.GlobalEventManager::OnCharacterDeath>");
        MethodBase hit = new FakeMethod("DMD<RoR2.HealthComponent::TakeDamage>");
        Action<MethodBase[]> Stack = f => { frames.Clear(); frames.AddRange(f); };

        // Which summons stop.
        Reset(80); Stack(new[] { mod });
        alive = 79;
        Check("hardcap: one under the cap, a limit-ignoring summon goes ahead", P() && (string)result == "untouched");
        alive = 80;
        Check("hardcap: at the cap, a mod's limit-ignoring summon stops and returns nothing", !P() && result == null);
        hostile = false;
        Check("hardcap: players' summons are never touched, however many enemies there are", P() && (string)result == "untouched");
        hostile = true; champion = true;
        Check("hardcap: a limit-ignoring boss character goes ahead past the cap", P());
        champion = false; Stack(new[] { scripted });
        Check("hardcap: a scripted boss part goes ahead past the cap", P());
        Stack(new[] { director });
        Check("hardcap: a director's limit-ignoring spawn of a non-boss stops at the cap", !P());
        champion = true;
        Check("hardcap: a director's limit-ignoring boss goes ahead, as a boss character", P());
        champion = false;
        ignores = false;
        Check("hardcap: an ordinary director spawn stops at the cap, it gets no exemption", !P());
        champion = true;
        Check("hardcap: an ordinary summon of a boss character stops too, as the game's own limit would", !P());
        champion = false; ignores = true;
        Stack(new[] { death, scripted });
        Check("hardcap: a death effect during a scripted fight isn't part of the script", !P());
        Stack(new[] { scripted, death });
        Check("hardcap: a scripted spawn that happens inside a death is still scripted", P());
        Stack(new[] { new FakeMethod("DMD<EntityStates.BrotherMonster.SpellChannelState::OnEnter>") as MethodBase });
        Check("hardcap: Mithrix's spires go ahead past the cap", P());

        // The stack walk and the naming.
        Check("hardcap: the walk stops at damage code", Script(new[] { hit, scripted }) == null);
        Check("hardcap: the walk names the marker it found", Script(new[] { mod, scripted }) == "ScriptedCombatEncounter");
        Check("hardcap: no marker means no script", Script(new[] { mod }) == null);
        Check("hardcap: an empty frame doesn't stop the walk", Script(new MethodBase[] { null, scripted }) == "ScriptedCombatEncounter");
        Check("hardcap: the mod asking for the spawn is named: " + Caller(new[] { new FakeMethod("DMD<RoR2.MasterSummon::Perform>") as MethodBase, mod }),
            Caller(new[] { new FakeMethod("DMD<RoR2.MasterSummon::Perform>") as MethodBase, mod }) == "shieldtest:Program.HardCapCheck");

        // Exemptions as they look on the stack when no mod has hooked the method: a plain type and method name.
        foreach (var plain in new[] {
            new[] { "RoR2.ScriptedCombatEncounter", "Spawn" },
            new[] { "RoR2.CharacterBody", "HandleConstructTurret" },
            new[] { "ThinkInvisible.TinkersSatchel.EngiUtilitySpeedispenser+PlaceDispenser", "Deploy" },
            new[] { "RoR2.Projectile.GummyCloneProjectile", "SpawnGummyClone" },
            new[] { "EntityStates.Events.GenericNemesisEvent", "SpawnNemesisBoss" } })
            Check("hardcap: an unhooked " + plain[0] + "." + plain[1] + " is exempt", Script(new MethodBase[] { new FakeMethod(plain[1], new NamedType(plain[0])) }) != null);
        Check("hardcap: a plain director frame is no exemption", Script(new MethodBase[] { new FakeMethod("Spawn", new NamedType("RoR2.CombatDirector")) }) == null);
        Check("hardcap: a plain death frame ends the walk", Script(new MethodBase[] { new FakeMethod("OnCharacterDeath", new NamedType("RoR2.GlobalEventManager")), scripted }) == null);

        // A failure callback that asks again from inside itself must end, and must not take the whole stack with it.
        Reset(80); alive = 80; champion = false; ignores = true;
        int asks = 0;
        Func<int, bool> Cast = null;
        Cast = level => {
            if (++asks > 500) return false;
            var deep = new System.Collections.Generic.List<MethodBase>();
            for (int i = 0; i < level * 4; i++) deep.Add(mod);
            Stack(deep.ToArray());
            return P() || Cast(level + 1);
        };
        Check("hardcap: a summon retrying inside its own failure callback ends on the fourth ask: " + asks, Cast(1) && asks == 4);
        Check("hardcap: the retry is named in the stage line: " + S(), S().Contains("stopped 3 spawn(s)") && S().Contains("let through: summons that keep retrying when refused 1"));
        Reset(80); Stack(new[] { mod });
        bool flat = true;
        for (int i = 0; i < 12; i++) flat &= !P();
        Check("hardcap: many refusals at the same depth in one frame all stay refused", flat);
        Reset(80);
        bool spread = true;
        for (int i = 1; i <= 12; i++) { frameNo++; var deep = new System.Collections.Generic.List<MethodBase>(); for (int j = 0; j < i; j++) deep.Add(mod); Stack(deep.ToArray()); spread &= !P(); }
        Check("hardcap: deeper refusals in different frames all stay refused", spread);

        // What the stack walk costs.
        Reset(80); alive = 80; Stack(new[] { mod }); ignores = false; walks = 0;
        for (int i = 0; i < 16; i++) P();
        Check("hardcap: ordinary refusals only sample the stack: " + walks + " " + S(), walks == 2 && S().Contains("stopped 16 spawn(s)") && S().Contains("asked for by: shieldtest:Program.HardCapCheck 2"));
        ignores = true; champion = true; walks = 0; P();
        Check("hardcap: a boss character needs no stack walk", walks == 0);
        champion = false;
        Check("hardcap: the real stack reader sees its caller", ((System.Collections.Generic.List<MethodBase>)cap.GetMethod("ReadFrames", all).Invoke(null, null)).Any(m => m != null && m.Name == "HardCapCheck"));

        // Reports.
        Reset(80); host = false; alive = 200; logs.Clear(); Tick();
        Check("hardcap: a client logs no count, because refusals only happen on the host", logs.Count == 0 && S() == null);
        host = true;
        Reset(80); alive = 80; Tick();
        Check("hardcap: exactly at the cap with nothing stopped isn't called a bypass", !S().Contains("route the cap doesn't cover"));
        Reset(80); alive = 90; Tick(); Stack(new[] { scripted }); P();
        Check("hardcap: past the cap through an exemption isn't called an unknown route", !S().Contains("route the cap doesn't cover") && S().Contains("let through: ScriptedCombatEncounter 1"));

        // The death effects that skip MasterSummon.
        Reset(80); alive = 80; destroyed.Clear();
        object m1 = new object();
        Check("hardcap: at the cap a Void Infestor gets no body and its master is removed", !B(m1) && destroyed.Count == 1 && ReferenceEquals(destroyed[0], m1));
        Check("hardcap: a second try on the same master stays stopped and counts once", !B(m1) && destroyed.Count == 1 && S().Contains("stopped 1 spawn(s)"));
        master = "UrchinTurretMaster(Clone)";
        Check("hardcap: a Malachite urchin is stopped the same way", !B(new object()) && destroyed.Count == 2);
        master = "BeetleMaster(Clone)";
        Check("hardcap: any other master's body is left alone", B(new object()) && destroyed.Count == 2);
        master = "VoidInfestorMaster(Clone)"; alive = 79;
        Check("hardcap: under the cap the infestor spawns", B(new object()) && destroyed.Count == 2);
        alive = 80; masterHostile = false;
        Check("hardcap: an infestor that isn't on an enemy team spawns", B(new object()) && destroyed.Count == 2);
        masterHostile = true;

        // A problem must never cost a spawn.
        Reset(80); blowUp = true; logs.Clear();
        Check("hardcap: a problem lets a summon through", P());
        P();
        Check("hardcap: a problem lets a body spawn through", B(new object()));
        Check("hardcap: the warning is shown once", logs.Count(l => l.Contains("couldn't be checked")) == 1);
        blowUp = false;

        // The log lines.
        Reset(80); logs.Clear(); Stack(new[] { mod });
        Check("hardcap: a quiet stage has no line", S() == null);
        alive = 79; Tick();
        Check("hardcap: nothing is logged under the cap", logs.Count == 0 && S() == null);
        alive = 80; Tick();
        Check("hardcap: reaching the cap names what is alive, without (Clone): " + string.Join("|", logs),
            logs.Count == 1 && logs[0].Contains("Enemy cap: 80 enemies alive in goolake (cap 80). Most: BeetleBody 3, WispBody 1"));
        alive = 120; Tick();
        Check("hardcap: a higher count that hasn't doubled logs nothing new", logs.Count == 1);
        alive = 160; Tick();
        Check("hardcap: doubling logs again", logs.Count == 2 && logs[1].Contains("160 enemies alive"));
        Check("hardcap: past the cap with nothing stopped, the line says another route did it: " + S(), S().Contains("most alive at once 160") && S().Contains("came by a route the cap doesn't cover"));
        logs.Clear();
        endStage.Invoke(null, null);
        Check("hardcap: a stage end writes the line and clears the counts", logs.Count(l => l.Contains("last stage stopped 0")) == 1 && S() == null);
        alive = 80; Tick();
        Check("hardcap: a new stage reports the cap again", logs.Count(l => l.Contains("80 enemies alive")) == 1);
        Reset(80); alive = 90; Tick(); P(); P(); master = "VoidInfestorMaster(Clone)"; B(new object());
        Stack(new[] { scripted }); P();
        string line = S();
        Check("hardcap: the stage line counts stops, names them and who asked: " + line,
            line == "Enemy cap: last stage stopped 3 spawn(s), most alive at once 90 (cap 80); stopped: EngiMaster 2, VoidInfestorMaster 1; asked for by: shieldtest:Program.HardCapCheck 2, game:death effects 1; let through: ScriptedCombatEncounter 1");

        // Harmony binds the real prefixes to methods shaped like the game's.
        Reset(80); alive = 80; destroyed.Clear(); Stack(new[] { mod });
        var h = new Harmony("test.hardcap");
        h.Patch(typeof(FakeSummonTarget).GetMethod("Perform"), prefix: new HarmonyMethod(perform));
        h.Patch(typeof(FakeSummonTarget).GetMethod("SpawnBodyHere"), prefix: new HarmonyMethod(spawnBody));
        var fake = new FakeSummonTarget();
        Check("hardcap: patched past the cap, the summon doesn't run and the caller gets null", fake.Perform() == null && fake.runs == 0);
        alive = 10;
        Check("hardcap: patched under the cap, the summon runs and returns its result", fake.Perform() != null && fake.runs == 1);
        alive = 80; fake.SpawnBodyHere();
        Check("hardcap: patched past the cap, the body spawn doesn't run", fake.runs == 1 && destroyed.Count == 1 && ReferenceEquals(destroyed[0], fake));
        Reset(80); alive = 10; fake.SpawnBodyHere();
        Check("hardcap: patched under the cap, the body spawn runs", fake.runs == 2);

        // The shipped game.
        var ror2 = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var gameMethods = ror2.GetTypes().SelectMany(t => t.Methods.Select(m => t.FullName + "::" + m.Name)).ToList();
        var modMarkers = new System.Collections.Generic.Dictionary<string, string> { { "GenericNemesisEvent", "Starstorm2.dll" }, { "EngiUtilitySpeedispenser", "TinkersSatchel.dll" } };
        foreach (string marker in (string[])cap.GetField("ScriptedMarkers", all).GetValue(null))
            if (!modMarkers.ContainsKey(marker)) Check("hardcap real: some game method matches the marker " + marker, gameMethods.Any(n => n.Contains(marker)));
        Func<Mono.Cecil.ModuleDefinition, string, bool> SpawnsPastLimit = (module, marker) => module.GetTypes()
            .SelectMany(t => t.Methods.Where(m => m.HasBody && (t.FullName + "::" + m.Name).Contains(marker)))
            .Any(m => m.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MemberReference r && r.Name.Contains("ignoreTeamMemberLimit")));
        foreach (string marker in new[] { "HandleConstructTurret", "GummyCloneProjectile" })
            Check("hardcap real: " + marker + " still asks for a limit-ignoring spawn", SpawnsPastLimit(ror2, marker));
        foreach (var pair in modMarkers) {
            var modDll = System.IO.Directory.GetFiles(System.IO.Path.Combine(profile, "plugins"), pair.Value, System.IO.SearchOption.AllDirectories).FirstOrDefault();
            if (modDll == null) { Console.WriteLine("SKIP  hardcap real: " + pair.Value + " not installed"); continue; }
            Check("hardcap real: " + pair.Key + " in " + pair.Value + " still asks for a limit-ignoring spawn", SpawnsPastLimit(Mono.Cecil.ModuleDefinition.ReadModule(modDll), pair.Key));
        }
        foreach (string marker in (string[])cap.GetField("DeathFrames", all).GetValue(null))
            Check("hardcap real: the game has " + marker, ror2.GetType(marker) != null);
        foreach (string plumbing in (System.Collections.Generic.IEnumerable<string>)cap.GetField("SpawnPlumbing", all).GetValue(null))
            Check("hardcap real: the game has " + plumbing, ror2.GetType(plumbing) != null);
        var onDeath = ror2.GetType("RoR2.GlobalEventManager").Methods.First(m => m.Name == "OnCharacterDeath" && m.HasBody);
        var texts = onDeath.Body.Instructions.Where(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr).Select(i => (string)i.Operand).ToList();
        foreach (string direct in (System.Collections.Generic.IEnumerable<string>)cap.GetField("DirectDeathSpawns", all).GetValue(null))
            Check("hardcap real: OnCharacterDeath still loads " + direct, texts.Any(t => t.Contains(direct)));
        Check("hardcap real: OnCharacterDeath still gives them a body through SpawnBodyHere",
            onDeath.Body.Instructions.Count(i => i.Operand is Mono.Cecil.MethodReference r && r.Name == "SpawnBodyHere") >= 2);
        Check("hardcap real: MasterSummon.Perform takes nothing and returns a CharacterMaster",
            ror2.GetType("RoR2.MasterSummon").Methods.Any(m => m.Name == "Perform" && !m.HasParameters && m.ReturnType.FullName == "RoR2.CharacterMaster"));
        Check("hardcap real: CharacterMaster.SpawnBodyHere takes nothing and returns nothing",
            ror2.GetType("RoR2.CharacterMaster").Methods.Any(m => m.Name == "SpawnBodyHere" && !m.HasParameters && m.ReturnType.FullName == "System.Void"));
        Check("hardcap real: the events the cap listens to exist",
            ror2.GetType("RoR2.RoR2Application").Events.Any(e => e.Name == "onFixedUpdate") && ror2.GetType("RoR2.Run").Events.Any(e => e.Name == "onRunStartGlobal")
            && ror2.GetType("RoR2.Run").Events.Any(e => e.Name == "onRunDestroyGlobal") && ror2.GetType("RoR2.Stage").Events.Any(e => e.Name == "onStageStartGlobal"));
        try {
            cap.GetMethod("Bind", all).Invoke(null, new object[] { Assembly.LoadFrom(System.IO.Path.Combine(managed, "RoR2.dll")) });
            Check("hardcap real: every member the cap reads by name is in the shipped game",
                cap.GetField("ignoreRead", all).GetValue(null) != null && cap.GetField("teamMembers", all).GetValue(null) != null && ((Array)cap.GetField("hostileValues", all).GetValue(null)).Length == 3);
        } catch (TargetInvocationException e) when (e.InnerException is MissingMemberException) {
            Check("hardcap real: every member the cap reads by name is in the shipped game: " + e.InnerException.Message, false);
        } catch (Exception e) when (e is TypeLoadException || e is System.IO.FileNotFoundException || e is System.IO.FileLoadException || e is BadImageFormatException
            || e is TargetInvocationException && (e.InnerException is TypeLoadException || e.InnerException is System.IO.FileNotFoundException || e.InnerException is System.IO.FileLoadException)) {
            Console.WriteLine("SKIP  hardcap real: reading the members by name (this runtime can't load the game types: " + e.GetType().Name + ")");
        }
        return failures;
    }

    static int StatLimitCheck(string managed, string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var lim = shieldType.Assembly.GetType("CompatShield.StatUpdateLimit");
        float now = 100f;
        lim.GetField("Clock", all).SetValue(null, new Func<float>(() => now));
        lim.GetField("NameOf", all).SetValue(null, new Func<object, string>(o => (string)o + "(Clone)"));
        var reset = lim.GetMethod("ResetForTest", all);
        var gate = lim.GetMethod("Gate", all);
        var summary = lim.GetMethod("Summary", all);
        var endStage = lim.GetMethod("EndStage", all);
        Func<bool, object, bool> G = (d, b) => (bool)gate.Invoke(null, new object[] { d, b });
        reset.Invoke(null, new object[] { 0.1f });

        object a = "DroneA", b2 = "DroneB";
        Check("statlimit: a clean flag stays clean and counts for nothing", !G(false, a) && summary.Invoke(null, null) == null);
        Check("statlimit: the first change on a body applies at once", G(true, a));
        now += 1f / 60f;
        Check("statlimit: a repeat one tick later waits", !G(true, a));
        now = 100.0999f;
        Check("statlimit: still waiting just inside the interval", !G(true, a));
        now = 100.1f;
        Check("statlimit: applies once the interval has passed", G(true, a));
        Check("statlimit: bodies have their own timers", G(true, b2));
        now = 500f;
        Check("statlimit: a change after a quiet spell applies at once", G(true, a));
        now += 0.01f; G(true, a); G(true, a); G(true, a);
        string line = (string)summary.Invoke(null, null);
        Check("statlimit: the stage line counts runs and waits and names the body without (Clone): " + line,
            line == "Stat limit: last stage ran 4 automatic stat updates; physics ticks that waited: 5, most waiting: DroneA 5");
        logs.Clear();
        endStage.Invoke(null, null);
        Check("statlimit: a stage end writes the line and clears the counts", logs.Any(l => l.Contains("last stage ran 4")) && summary.Invoke(null, null) == null);
        now += 0.01f;
        Check("statlimit: timers restart with the stage", G(true, a));
        logs.Clear();
        reset.Invoke(null, new object[] { 0.1f });
        endStage.Invoke(null, null);
        Check("statlimit: a stage with no updates writes no line", logs.Count == 0);

        // The rewrite, on the shape the compiler emits for "if (statsDirty) RecalculateStats();".
        var rewrite = lim.GetMethod("Rewrite", all);
        var dirtyField = typeof(FakeDirtyBody).GetField("statsDirty");
        var recalc = typeof(FakeDirtyBody).GetMethod("RecalculateStats");
        Func<System.Collections.Generic.List<CodeInstruction>> shape = () => new System.Collections.Generic.List<CodeInstruction> {
            new CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_0),
            new CodeInstruction(System.Reflection.Emit.OpCodes.Ldfld, dirtyField),
            new CodeInstruction(System.Reflection.Emit.OpCodes.Brfalse_S, "end"),
            new CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_0),
            new CodeInstruction(System.Reflection.Emit.OpCodes.Call, recalc),
            new CodeInstruction(System.Reflection.Emit.OpCodes.Ret) };
        var args = new object[] { shape(), dirtyField, recalc, gate, null };
        var code = (System.Collections.Generic.List<CodeInstruction>)rewrite.Invoke(null, args);
        Check("statlimit: the rewrite puts the gate between the flag read and the branch",
            args[4] == null && code.Count == 8 && code[2].opcode == System.Reflection.Emit.OpCodes.Ldarg_0 && Equals(code[3].operand, gate) && code[4].opcode == System.Reflection.Emit.OpCodes.Brfalse_S);
        var dm = new System.Reflection.Emit.DynamicMethod("statlimit_tick", typeof(void), new[] { typeof(FakeDirtyBody) }, typeof(Program), true);
        var il = dm.GetILGenerator();
        var end = il.DefineLabel();
        foreach (var c in code) {
            if (c.opcode == System.Reflection.Emit.OpCodes.Brfalse_S) il.Emit(System.Reflection.Emit.OpCodes.Brfalse, end);
            else if (c.opcode == System.Reflection.Emit.OpCodes.Ret) { il.MarkLabel(end); il.Emit(c.opcode); }
            else if (c.operand is FieldInfo fi) il.Emit(c.opcode, fi);
            else if (c.operand is MethodInfo mi) il.Emit(c.opcode, mi);
            else il.Emit(c.opcode);
        }
        var tick = (Action<FakeDirtyBody>)dm.CreateDelegate(typeof(Action<FakeDirtyBody>));
        lim.GetField("NameOf", all).SetValue(null, new Func<object, string>(o => "FakeDirtyBody"));
        reset.Invoke(null, new object[] { 0.1f });
        var body = new FakeDirtyBody();
        now = 700f;
        tick(body);
        Check("statlimit: run as IL, a clean body doesn't update", body.runs == 0);
        body.statsDirty = true; tick(body);
        Check("statlimit: run as IL, the first change updates and clears the flag", body.runs == 1 && !body.statsDirty);
        now += 1f / 60f; body.statsDirty = true; tick(body);
        Check("statlimit: run as IL, a repeat waits with the flag still set, so it isn't lost", body.runs == 1 && body.statsDirty);
        for (int t = 0; t < 4; t++) { now += 1f / 60f; body.statsDirty = true; tick(body); }
        Check("statlimit: run as IL, a body changing every tick keeps waiting through the interval", body.runs == 1 && body.statsDirty);
        now += 0.02f; tick(body);
        Check("statlimit: run as IL, the waiting change lands with no new change needed", body.runs == 2 && !body.statsDirty);
        reset.Invoke(null, new object[] { 0.1f });
        var busy = new FakeDirtyBody();
        now = 10f;
        for (int t = 0; t < 60; t++) { busy.statsDirty = true; tick(busy); now += 1f / 60f; }
        Check("statlimit: run as IL, 60 changes in a second become about 10 updates: " + busy.runs, busy.runs >= 8 && busy.runs <= 11);
        var twoReads = shape(); twoReads.Insert(1, new CodeInstruction(System.Reflection.Emit.OpCodes.Ldfld, dirtyField));
        args = new object[] { twoReads, dirtyField, recalc, gate, null };
        var left = (System.Collections.Generic.List<CodeInstruction>)rewrite.Invoke(null, args);
        Check("statlimit: a second read of the flag leaves the code alone and says why: " + args[4], left.Count == 7 && ((string)args[4]).Contains("found 2"));
        var noBranch = shape(); noBranch[2] = new CodeInstruction(System.Reflection.Emit.OpCodes.Pop);
        args = new object[] { noBranch, dirtyField, recalc, gate, null };
        left = (System.Collections.Generic.List<CodeInstruction>)rewrite.Invoke(null, args);
        Check("statlimit: an unexpected shape leaves the code alone and says why: " + args[4], left.Count == 6 && args[4] != null);

        // The tracer: a hook that only passed through orig isn't the cause.
        var tracer = shieldType.Assembly.GetType("CompatShield.LagTracer");
        var dirtySource = tracer.GetMethod("DirtySource", all);
        var lotus = typeof(Program).GetMethod("FakeLotus", all);
        var overload = typeof(Program).GetMethod("FakeOverload", all);
        var caller = typeof(Program).GetMethod("StatLimitCheck", all);
        Func<MethodBase[], string> Src = f => (string)dirtySource.Invoke(null, new object[] { new System.Collections.Generic.List<MethodBase>(f), null });
        Check("statlimit: a hook whose orig was on the stack is a pass-through, the game's method gets the credit",
            Src(new MethodBase[] { new FakeMethod("DMD<RoR2.BuffWard::BuffTeam>"), new FakeMethod("Trampoline<RoR2.BuffWard::BuffTeam>?-97003156"), lotus }) == "game:BuffWard.BuffTeam");
        Check("statlimit: with no orig on the stack, the hook made the change itself and is named", Src(new MethodBase[] { lotus }) == "shieldtest:Program.FakeLotus");
        Check("statlimit: a wrapper of some other method doesn't excuse the hook", Src(new MethodBase[] { new FakeMethod("DMD<RoR2.BuffWard::OnEnable>"), lotus }) == "shieldtest:Program.FakeLotus");
        Check("statlimit: a mod further up the stack, past the pass-through, is still named",
            Src(new MethodBase[] { new FakeMethod("DMD<RoR2.BuffWard::BuffTeam>"), lotus, caller }) == "shieldtest:Program.StatLimitCheck");
        Check("statlimit: an overload's orig_Name_type delegate matches its method", Src(new MethodBase[] { new FakeMethod("DMD<RoR2.BuffWard::Refresh>"), overload }) == "game:BuffWard.Refresh");

        // The shipped game.
        var ror2 = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        var bodyType = ror2.GetType("RoR2.CharacterBody");
        var fu = bodyType.Methods.Single(m => m.Name == "FixedUpdate" && !m.HasParameters);
        var reads = fu.Body.Instructions.Where(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldfld && ((Mono.Cecil.FieldReference)i.Operand).Name == "statsDirty").ToList();
        Check("statlimit real: FixedUpdate reads the statsDirty field exactly once", reads.Count == 1);
        var after = reads.Count == 1 ? reads[0].Next : null;
        Check("statlimit real: that read is followed by brfalse, ldarg.0 and a RecalculateStats call", after != null
            && (after.OpCode == Mono.Cecil.Cil.OpCodes.Brfalse || after.OpCode == Mono.Cecil.Cil.OpCodes.Brfalse_S)
            && after.Next.OpCode == Mono.Cecil.Cil.OpCodes.Ldarg_0
            && (after.Next.Next.OpCode == Mono.Cecil.Cil.OpCodes.Call || after.Next.Next.OpCode == Mono.Cecil.Cil.OpCodes.Callvirt)
            && ((Mono.Cecil.MethodReference)after.Next.Next.Operand).Name == "RecalculateStats");
        Check("statlimit real: Stage.onStageStartGlobal and Run.onRunDestroyGlobal exist",
            ror2.GetType("RoR2.Stage").Events.Any(e => e.Name == "onStageStartGlobal") && ror2.GetType("RoR2.Run").Events.Any(e => e.Name == "onRunDestroyGlobal"));
        var ilHookers = new System.Collections.Generic.List<string>();
        foreach (var dll in System.IO.Directory.GetFiles(System.IO.Path.Combine(profile, "plugins"), "*.dll", System.IO.SearchOption.AllDirectories)) {
            if (System.IO.Path.GetFileName(dll).StartsWith("MMHOOK", StringComparison.OrdinalIgnoreCase)) continue;
            try {
                using (var mod = Mono.Cecil.ModuleDefinition.ReadModule(dll)) {
                    if (!mod.GetMemberReferences().OfType<Mono.Cecil.MethodReference>().Any(r => r.Name == "add_FixedUpdate" && r.DeclaringType.FullName == "IL.RoR2.CharacterBody")) continue;
                    ilHookers.Add(System.IO.Path.GetFileName(dll));
                }
            } catch { }
        }
        Check("statlimit real: no mod rewrites CharacterBody.FixedUpdate's code, so nothing else matches on those instructions (found: " + string.Join(", ", ilHookers) + ")", ilHookers.Count == 0);
        return failures;
    }

    // Physics catch-up cap: the cap is set at each run start through the swappable setter, logged once, and a cap of 0 does nothing.
    static int PhysCapCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var cap = shieldType.Assembly.GetType("CompatShield.PhysicsCatchUpCap");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var set = new System.Collections.Generic.List<float>();
        float current = 0.3333333f;
        cap.GetMethod("ResetForTest", all).Invoke(null, null);
        cap.GetField("SetMax", all).SetValue(null, new Action<float>(v => { set.Add(v); current = v; }));
        cap.GetField("GetMax", all).SetValue(null, new Func<float>(() => current));
        cap.GetField("FixedStep", all).SetValue(null, new Func<float>(() => 1f / 60f));
        cap.GetField("Cap", all).SetValue(null, 0.1f);
        cap.GetMethod("ApplyNow", all).Invoke(null, null);
        cap.GetMethod("ApplyNow", all).Invoke(null, null);
        var lines = logs.Where(l => l.Contains("Physics catch-up:")).ToList();
        foreach (var l in lines) Console.WriteLine("  " + l);
        Check("physcap: each run start sets maximumDeltaTime to the cap; the change is logged once with the step count and the old value",
            set.Count == 2 && set.All(v => Math.Abs(v - 0.1f) < 1e-6) && (int)cap.GetField("Applied", all).GetValue(null) == 2 && lines.Count == 1
            && lines[0] == "[Info:CompatShield] Physics catch-up: maximumDeltaTime set to 0.10 s (at most 6 physics steps per frame; the game had 0.33 s)");
        cap.GetMethod("ResetForTest", all).Invoke(null, null);
        cap.GetField("SetMax", all).SetValue(null, new Action<float>(v => set.Add(v)));
        cap.GetMethod("ApplyNow", all).Invoke(null, null);
        Check("physcap: a cap of 0 sets nothing", set.Count == 2);
        var apply = cap.GetMethod("Apply", all);
        var unity = PatchProcessor.ReadMethodBody(apply).Where(kv => kv.Value is MethodBase mb && (mb.DeclaringType?.Namespace ?? "").StartsWith("UnityEngine")).ToList();
        Check("physcap: Apply (the load path) touches no Unity member; the setter runs from the run-start handler", unity.Count == 0);
        var managed = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var core = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "UnityEngine.CoreModule.dll"));
        var time = core.GetType("UnityEngine.Time");
        Check("physcap real: UnityEngine.Time has a settable maximumDeltaTime and fixedDeltaTime", time != null && time.Methods.Any(m => m.Name == "set_maximumDeltaTime") && time.Methods.Any(m => m.Name == "get_fixedDeltaTime"));
        var ror2 = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "RoR2.dll"));
        bool gameSetsIt = ror2.Types.SelectMany(ty => ty.Methods).Where(m => m.HasBody).Any(m => m.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "set_maximumDeltaTime"));
        Check("physcap real: game 1.5.0 never sets maximumDeltaTime itself (so the cap is not fighting the game)", !gameSetsIt);
        return failures;
    }

    static int TracerCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var tracer = shieldType.Assembly.GetType("CompatShield.SoundTracer");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        float now = 0f;
        tracer.GetMethod("ResetForTest", all).Invoke(null, null);
        tracer.GetField("Now", all).SetValue(null, new Func<float>(() => now));
        tracer.GetField("IsGone", all).SetValue(null, new Func<object, bool>(o => (o as string) == "gone"));
        void Rec(string name, string target, string obj, int id, uint playing = 0) => tracer.GetMethod("Record", all).Invoke(null, new object[] { name, target, obj, id, playing });
        int Tracked() => (int)tracer.GetProperty("Tracked", all).GetValue(null);
        bool Loop(string n) => (bool)tracer.GetMethod("IsLoopName", all).Invoke(null, new object[] { n });
        Check("tracer: loop-type names (loop / _LP / Idle) are recognised, one-shots are not",
            Loop("Play_GG_Boss_SolusAmalgamator_Flame_Loop") && Loop("Play_Boss_SolusWing_LaserHit_LP") && Loop("Play_GG_Boss_SolusAmalgamator_Idle")
            && !Loop("Play_wisp_death") && !Loop("Play_item_proc_crit"));
        now = 10f; Rec("Play_GG_Boss_SolusAmalgamator_Flame_Loop", "gone", "SolusAmalgamatorFlamethrowerCannonBody(Clone)", 11);
        now = 12f; Rec("Play_ui_obj_lunarPool_idle_loop", "gone", "ShrineCleanse", 22);
        now = 13f; Rec("Stop_ui_obj_lunarPool_idle_loop", "gone", "ShrineCleanse", 22);
        now = 14f; Rec("Play_GG_Boss_SolusAmalgamator_Flame_Loop", "alive", "Cannon2", 33);
        now = 15f; Rec("Stop_GG_Boss_SolusAmalgamator_Flame_Stop", "alive", "Cannon2", 33);
        now = 18f; Rec("Play_wisp_death", "gone", "WispBody(Clone)", 44);
        now = 19f; Rec("Play_item_proc_chain_loop", "alive", "PlayerBody", 55);
        Check("tracer: an exact Stop_ and a same-family Stop_ (Flame_Stop for Flame_Loop) end the loop on that object; others stay tracked", Tracked() == 3);
        now = 20f; tracer.GetMethod("OnRunDestroy", all).Invoke(null, new object[] { null });
        tracer.GetMethod("Tick", all).Invoke(null, new object[] { 21f });
        Check("tracer: no report before the 2.5 s settle", !logs.Exists(l => l.Contains("run ended")));
        now = 23f;
        tracer.GetMethod("PostEventPostfix", all).Invoke(null, new object[] { new object[] { "Stop_ui_menu_hover", null }, 1u });
        Check("tracer: a PostEvent after the settle delivers the run-end report (no MonoBehaviour of our own)", logs.Exists(l => l.Contains("run ended")));
        tracer.GetMethod("Tick", all).Invoke(null, new object[] { 23f });
        // 1.4.8-1.4.10 crashed the game at launch: Apply ran new GameObject + AddComponent from the AssemblyLoad event, inside
        // MonoManager's assembly load. Nothing reachable from ApplyModFixes (every fix's Apply, their helpers and lambdas) may call
        // into UnityEngine; patch bodies are reached only by name, so they are not walked.
        var loadPath = UnityCallsReachableFrom(shieldType.GetMethod("ApplyModFixes", all), shieldType.Assembly);
        foreach (var l in loadPath) Console.WriteLine("  load path calls: " + l);
        Check("tracer: nothing reachable from ApplyModFixes calls UnityEngine (it runs while MonoManager is still loading assemblies)",
            loadPath.Count == 0 && shieldType.Assembly.GetType("CompatShield.SoundTracerTicker") == null);
        var report = logs.Where(l => l.Contains("Sound tracer")).ToList();
        foreach (var l in logs) Console.WriteLine("  " + l);
        Check("tracer: the run-end report names the orphaned loop with its object and age, not the one-shot or the live loop",
            report.Exists(l => l.Contains("1 loop-type sound(s)")) && logs.Exists(l => l.Contains("Flame_Loop on 'SolusAmalgamatorFlamethrowerCannonBody(Clone)' (last started 10.0 s before"))
            && !logs.Exists(l => l.Contains("wisp_death on")) && !logs.Exists(l => l.Contains("chain_loop on")));
        Check("tracer: the report lists the last sounds around the run end", logs.Exists(l => l.Contains("last sounds around the run end") && l.Contains("Play_wisp_death@'WispBody(Clone)'(-2.0s)")));
        Check("tracer: gone objects are pruned after the report, live ones kept", Tracked() == 1);
        typeof(BepInEx.Paths).GetMethod("SetExecutablePath", all).Invoke(null, new object[] { @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2.exe", null, null, null });   // internal in 5.4.21
        var xml = (string)tracer.GetMethod("SoundbanksXmlPath", all).Invoke(null, null);
        Console.WriteLine("  xml path: " + xml);
        Check("tracer real: the soundbank XML path derived from BepInEx's Managed path exists (no Application.streamingAssetsPath on the load path)",
            xml != null && System.IO.File.Exists(xml));
        var table = (System.Collections.Generic.Dictionary<uint, string>)tracer.GetMethod("BuildNameTable", all).Invoke(null, new object[] { xml });
        Console.WriteLine($"  name table: {table?.Count} events");
        Check("tracer real: the name table from SoundbanksInfo.xml resolves Wwise IDs (3000+ events, Flame_Loop included)",
            table != null && table.Count > 3000 && table.TryGetValue(Fnv1("Play_GG_Boss_SolusAmalgamator_Flame_Loop"), out var fl) && fl == "Play_GG_Boss_SolusAmalgamator_Flame_Loop");
        tracer.GetMethod("SetNames", all).Invoke(null, new object[] { table });
        int before = Tracked();
        tracer.GetMethod("PostEventPostfix", all).Invoke(null, new object[] { new object[] { Fnv1("Play_GG_Boss_SolusAmalgamator_Idle"), null }, 4242u });
        Check("tracer: an ID-based PostEvent is recorded under its resolved name", Tracked() == before + 1);

        // The orphan loop guard: when an object is destroyed, the loop-type sounds it started and never stopped are stopped by
        // playing ID; one-shots on it and loops on other objects are untouched; a loop that got its Stop is already forgotten.
        tracer.GetMethod("ResetForTest", all).Invoke(null, null);
        var stoppedIds = new System.Collections.Generic.List<uint>();
        tracer.GetField("StopPlaying", all).SetValue(null, new Action<uint>(id => stoppedIds.Add(id)));
        now = 30f; Rec("Play_GG_Boss_SolusAmalgamator_Flame_Start", "alive", "Cannon(Clone)", 77, 701);
        now = 31f; Rec("Play_GG_Boss_SolusAmalgamator_Flame_Loop", "alive", "Cannon(Clone)", 77, 702);
        now = 32f; Rec("Play_GG_Boss_SolusAmalgamator_Idle", "alive", "Cannon(Clone)", 77, 703);
        now = 33f; Rec("Play_ui_obj_lunarPool_idle_loop", "alive", "ShrineCleanse", 88, 801);
        now = 34f; Rec("Stop_ui_obj_lunarPool_idle_loop", "alive", "ShrineCleanse", 88, 802);
        now = 35f; Rec("Play_lemurian_fireball_loop", "alive", "LemurianBody(Clone)", 99, 901);
        int n = (int)tracer.GetMethod("OnObjectGone", all).Invoke(null, new object[] { 77, "Cannon(Clone)" });
        var guardLogs = logs.Where(l => l.Contains("Orphan loop guard")).ToList();
        foreach (var l in guardLogs) Console.WriteLine("  " + l);
        Check("tracer guard: destroying the cannon stops its Flame_Loop and Idle loops by playing ID, not its one-shot Flame_Start nor the Lemurian's loop",
            n == 2 && stoppedIds.Count == 2 && stoppedIds.Contains(702u) && stoppedIds.Contains(703u) && !stoppedIds.Contains(701u) && !stoppedIds.Contains(901u)
            && (int)tracer.GetField("GuardStopped", all).GetValue(null) == 2 && Tracked() == 2);
        Check("tracer guard: logs what it stopped, once per sound, naming the object",
            guardLogs.Count == 2 && guardLogs.Exists(l => l.Contains("stopped Play_GG_Boss_SolusAmalgamator_Flame_Loop, which 'Cannon(Clone)' left playing")));
        int n2 = (int)tracer.GetMethod("OnObjectGone", all).Invoke(null, new object[] { 88, "ShrineCleanse" });
        Check("tracer guard: a pool whose hum was already stopped has nothing left to stop", n2 == 0 && stoppedIds.Count == 2);
        int n3 = (int)tracer.GetMethod("OnObjectGone", all).Invoke(null, new object[] { 77, "Cannon(Clone)" });
        Check("tracer guard: a second destroy of the same object is a no-op", n3 == 0);
        var managedDir = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var hg = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managedDir, "HGUnityUtils.dll")).GetType("HG.AkGameObjManager");
        var akType = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managedDir, "AK.Wwise.Unity.API.dll")).GetType("AkSoundEngine");
        Check("tracer guard real: game has public static HG.AkGameObjManager.UnregisterDelayed(GameObject) and AkSoundEngine.StopPlayingID(uint, int)",
            hg != null && hg.Methods.Any(m => m.Name == "UnregisterDelayed" && m.IsPublic && m.IsStatic && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "UnityEngine.GameObject")
            && akType.Methods.Any(m => m.Name == "StopPlayingID" && m.IsStatic && m.Parameters.Count == 2 && m.Parameters[1].ParameterType.FullName == "System.Int32"));
        var managed = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var ak = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managed, "AK.Wwise.Unity.API.dll")).GetType("AkSoundEngine");
        int posts = ak.Methods.Count(m => m.Name == "PostEvent" && m.IsStatic && m.Parameters.Count >= 2 && m.Parameters[1].ParameterType.FullName == "UnityEngine.GameObject"
            && (m.Parameters[0].ParameterType.FullName == "System.String" || m.Parameters[0].ParameterType.FullName == "System.UInt32"));
        Check("tracer real: AkSoundEngine has the 8 PostEvent(string|uint, GameObject, ...) overloads and a StopAll()",
            posts == 8 && ak.Methods.Any(m => m.Name == "StopAll" && m.IsStatic && !m.HasParameters));
        return failures;
    }

    // Death-timer self-heal (part of RespawnHookChurnFix): the decision with UNet stand-ins, then the real members in
    // PlayerRespawnSystem.dll and the HLAPI.
    static int DeathTimerCheck(string profile) {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));
        var fix = shieldType.Assembly.GetType("CompatShield.RespawnHookChurnFix");
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        bool localActive = true; var known = new System.Collections.Generic.HashSet<string> { "4" }; var sets = new System.Collections.Generic.List<string>();
        fix.GetField("LocalClientActive", all).SetValue(null, new Func<bool>(() => localActive));
        fix.GetField("NetIdOf", all).SetValue(null, new Func<object, object>(b => ((string)b).Split(':')[1]));   // stand-in behaviour: "obj:netId"
        fix.GetField("NetIdEmpty", all).SetValue(null, new Func<object, bool>(id => (string)id == "0"));
        fix.GetField("FindLocal", all).SetValue(null, new Func<object, UnityEngine.GameObject>(id => known.Contains((string)id) ? (UnityEngine.GameObject)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(UnityEngine.GameObject)) : null));
        fix.GetField("SetLocal", all).SetValue(null, new Action<object, UnityEngine.GameObject>((id, go) => { sets.Add((string)id); known.Add((string)id); }));
        bool Heal(string b) => (bool)fix.GetMethod("EnsureDeathTimerLocal", all).Invoke(null, new object[] { b, null });
        Check("death timer: an object the host's client still knows is left alone", !Heal("timer:4") && sets.Count == 0);
        Check("death timer: an object it lost (netId 9) is registered again, once, and logged once", Heal("timer:9") && !Heal("timer:9") && sets.Count == 1 && sets[0] == "9"
            && logs.Count(l => l.Contains("had lost its death-timer object (netId 9)")) == 1);
        Check("death timer: an empty netId or a null behaviour does nothing", !Heal("timer:0") && !(bool)fix.GetMethod("EnsureDeathTimerLocal", all).Invoke(null, new object[] { null, null }) && sets.Count == 1);
        localActive = false;
        Check("death timer: nothing happens on a dedicated server or a pure client (no local client)", !Heal("timer:12") && sets.Count == 1);
        Check("death timer: Healed counts the re-registrations", (int)fix.GetField("Healed", all).GetValue(null) == 1);
        foreach (var l in logs) Console.WriteLine("  " + l);
        var prs = System.IO.Path.Combine(profile, @"plugins\Mordrog-PlayerRespawnSystem\Mordrog-PlayerRespawnSystem\PlayerRespawnSystem.dll");
        if (!System.IO.File.Exists(prs)) prs = System.IO.Directory.GetFiles(System.IO.Path.Combine(profile, "plugins"), "PlayerRespawnSystem.dll", System.IO.SearchOption.AllDirectories).FirstOrDefault();
        if (prs == null) { Console.WriteLine("SKIP  death timer real: PlayerRespawnSystem.dll not installed"); return failures; }
        var mod = Mono.Cecil.ModuleDefinition.ReadModule(prs);
        var ctrl = mod.GetType("PlayerRespawnSystem.UIDeathTimerController");
        Check("death timer real: UIDeathTimerController.CmdUpdateAllDeathTimers() exists on a NetworkBehaviour",
            ctrl != null && ctrl.BaseType.Name == "NetworkBehaviour" && ctrl.Methods.Any(m => m.Name == "CmdUpdateAllDeathTimers" && !m.HasParameters && !m.IsStatic));
        var managedDir = @"C:\Program Files (x86)\Steam\steamapps\common\Risk of Rain 2\Risk of Rain 2_Data\Managed";
        var hlapi = Mono.Cecil.ModuleDefinition.ReadModule(System.IO.Path.Combine(managedDir, "com.unity.multiplayer-hlapi.Runtime.dll"));
        var cs = hlapi.GetType("UnityEngine.Networking.ClientScene");
        Check("death timer real: HLAPI has ClientScene.FindLocalObject/SetLocalObject(NetworkInstanceId...), NetworkServer.localClientActive, NetworkBehaviour.netId, NetworkInstanceId.IsEmpty",
            cs.Methods.Any(m => m.Name == "FindLocalObject" && m.IsStatic && m.Parameters.Count == 1) && cs.Methods.Any(m => m.Name == "SetLocalObject" && m.IsStatic && m.Parameters.Count == 2)
            && hlapi.GetType("UnityEngine.Networking.NetworkServer").Properties.Any(p => p.Name == "localClientActive")
            && hlapi.GetType("UnityEngine.Networking.NetworkBehaviour").Properties.Any(p => p.Name == "netId")
            && hlapi.GetType("UnityEngine.Networking.NetworkInstanceId").Methods.Any(m => m.Name == "IsEmpty"));
        return failures;
    }

    static int Run() {
        var shieldType = typeof(CompatShield.CompatShieldPatcher);
        shieldType.GetField("Log", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, BepInEx.Logging.Logger.CreateLogSource("CompatShield"));
        var logs = new System.Collections.Generic.List<string>();
        BepInEx.Logging.Logger.Listeners.Add(new Listener(logs));

        var target = typeof(Game).GetMethod(nameof(Game.SetCurrentLanguage));
        Delegate hook = new OldHook(OldHandler);

        // Baseline: without the shield the outdated hook throws, like TinkersSatchel/Inferno in the log.
        Exception before = null;
        try { HookEndpointManager.Add(target, hook); } catch (Exception e) { before = e; }
        Console.WriteLine("  baseline exception: " + (before == null ? "none" : before.GetType().FullName + ": " + before.Message));
        Check("baseline: outdated hook throws 'doesn't match'", before is InvalidOperationException && before.Message.Contains("doesn't match"));
        Exception beforeRemove = null;
        try { HookEndpointManager.Remove(target, hook); } catch (Exception e) { beforeRemove = e; }
        Check("baseline: removing it throws 'Stack empty'", beforeRemove is InvalidOperationException && beforeRemove.Message.StartsWith("Stack empty"));

        var harmony = new Harmony("test.compatshield");
        var hs = shieldType.Assembly.GetType("CompatShield.HookShield");
        hs.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { harmony });

        Delegate hook2 = new OldHook(OldHandler);
        Exception after = null;
        try { HookEndpointManager.Add(target, hook2); } catch (Exception e) { after = e; }
        Check("shield: outdated hook is skipped (no exception)", after == null);
        Exception afterRemove = null;
        try { HookEndpointManager.Remove(target, hook2); } catch (Exception e) { afterRemove = e; }
        Check("shield: removing never-added hook is ignored", afterRemove == null);

        // Real errors must still surface: a null delegate method / other exception types are not swallowed.
        Exception other = null;
        try { HookEndpointManager.Add(null, hook2); } catch (Exception e) { other = e; }
        Check("shield: unrelated errors still thrown", other != null);

        // Paladin-style method: wrap statements, crashing one is skipped, later ones still run.
        harmony.Patch(typeof(FakeAsset).GetMethod(nameof(FakeAsset.Gather)),
            transpiler: new HarmonyMethod(typeof(CompatShield.SafeStatements).GetMethod("Transpiler")));
        Exception gatherErr = null;
        try { FakeAsset.Gather(); } catch (Exception e) { gatherErr = e; }
        Check("safe statements: method no longer throws", gatherErr == null);
        Check("safe statements: statement before the crash ran", FakeAsset.a == "FIRST");
        Check("safe statements: crashing statement skipped (field stays null)", FakeAsset.b == null);
        Check("safe statements: statement after the crash still ran", FakeAsset.c == "third");

        // Hook type removed by the update: simulate the TypeLoadException inside MonoMod's real Add path
        // (OnAdd handlers run inside HookEndpointManager.Add, where RoR2BepInExPack's logger also runs).
        Delegate marker = new OldHook(OldHandler);
        Func<MethodBase, Delegate, bool> thrower = (m, d) => {
            if (d == marker) throw new TypeLoadException("Could not resolve type with token 01000156 from typeref (expected class 'orig_FireProjectile' in assembly '')");
            return true;
        };
        HookEndpointManager.OnAdd += thrower;
        Exception tle = null;
        try { HookEndpointManager.Add(target, marker); } catch (Exception e) { tle = e; }
        HookEndpointManager.OnAdd -= thrower;
        if (tle != null) Console.WriteLine("  removed-type exception: " + tle.GetType().Name + " / " + tle.InnerException?.GetType().Name);
        Check("shield: hook with a removed hook type is skipped", tle == null);
        Exception otherTle = null;
        HookEndpointManager.OnAdd += (m, d) => { if (d == hook2) throw new TypeLoadException("some unrelated type load failure"); return true; };
        try { HookEndpointManager.Add(target, hook2); } catch (Exception e) { otherTle = e; }
        var otherInner = otherTle;
        while (otherInner is TargetInvocationException t && t.InnerException != null) otherInner = t.InnerException;
        Check("shield: unrelated TypeLoadExceptions still thrown", otherInner is TypeLoadException);

        // TinkersSatchel-style item loop: the middle item throws, the ones after it must still set up.
        CompatShield.ModuleIsolation.Apply(harmony, "FakeMod", typeof(FakeModule), typeof(FakeModule).GetMethod(nameof(FakeModule.SetupAll)),
            "SetupConfig", "SetupAttributes", "SetupBehavior");
        Exception loopErr = null;
        try { FakeModule.SetupAll(new FakeModule[] { new ItemA(), new TimelostRum(), new ItemC() }); } catch (Exception e) { loopErr = e; }
        Check("isolation: loop no longer throws", loopErr == null);
        Check("isolation: item after the broken one set up", FakeModule.ran.Contains("ItemC.Attributes") && FakeModule.ran.Contains("ItemC.Behavior"));
        Check("isolation: broken item's other steps still ran", FakeModule.ran.Contains("TimelostRum.Config") && FakeModule.ran.Contains("TimelostRum.Behavior"));
        Check("isolation: overrides still dispatched (not base methods)", !FakeModule.ran.Contains("TimelostRum.Attributes"));

        // TinkersSatchel auto-config: the plugin's Update must no longer reach the method that can't compile.
        var autoFix = shieldType.Assembly.GetType("CompatShield.TinkersAutoConfigFix");
        autoFix.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { harmony, typeof(Program).Assembly });
        var plugin = new ThinkInvisible.TinkersSatchel.TinkersSatchelPlugin();
        Exception upd = null;
        try { plugin.Update(); plugin.Update(); } catch (Exception e) { upd = e; }
        Check("auto-config: Update no longer throws every frame", upd == null);
        Check("auto-config: original (uncompilable) Update never runs", ThinkInvisible.TinkersSatchel.TinkersSatchelPlugin.originalRan == 0);
        ThinkInvisible.TinkersSatchel.AutoConfigModule.globalLanguageDirty = true;  // forces the reflection path (no RoR2 here)
        Exception upd2 = null;
        try { plugin.Update(); plugin.Update(); plugin.Update(); } catch (Exception e) { upd2 = e; }
        Check("auto-config: a failure inside the replacement is contained", upd2 == null);

        // TinkersSatchel language: redo its skipped hook's work after the game's new two-argument SetCurrentLanguage.
        var langFix = shieldType.Assembly.GetType("CompatShield.TinkersLanguageFix");
        var ts = ThinkInvisible.TinkersSatchel.Module.langLog;
        var wrench = new ThinkInvisible.TinkersSatchel.Wrench();
        wrench.InstallLanguage();   // already installed once, so the fix has to swap it out
        ThinkInvisible.TinkersSatchel.Module.all.AddRange(new ThinkInvisible.TinkersSatchel.Module[] {
            new ThinkInvisible.TinkersSatchel.Mug(), new ThinkInvisible.TinkersSatchel.Unused(),
            new ThinkInvisible.TinkersSatchel.BrokenText(), new ThinkInvisible.TinkersSatchel.Reentrant(), wrench });
        Check("language: found TinkersSatchel's language members", (bool)langFix.GetMethod("Init").Invoke(null, new object[] { typeof(Program).Assembly }));
        langFix.GetMethod("PatchTarget").Invoke(null, new object[] { harmony, typeof(LangGame).GetMethod(nameof(LangGame.SetCurrentLanguage)) });
        ts.Clear();
        ThinkInvisible.TinkersSatchel.AutoConfigModule.globalLanguageDirty = false;
        Exception langErr = null;
        try { LangGame.SetCurrentLanguage("en", true); } catch (Exception e) { langErr = e; }
        Console.WriteLine("  language calls: " + string.Join(", ", ts));
        Check("language: game method still runs, nothing thrown", langErr == null && LangGame.calls == 2);
        Check("language: enabled item gets its text installed", ts.Contains("Mug.Refresh") && ts.Contains("Mug.Install"));
        Check("language: disabled item only refreshes permanent text", ts.Contains("Unused.Refresh") && !ts.Contains("Unused.Install"));
        Check("language: installed text replaced (uninstall, then install)",
            ts.IndexOf("Wrench.Uninstall") >= 0 && ts.IndexOf("Wrench.Uninstall") < ts.IndexOf("Wrench.Install"));
        Check("language: a broken item doesn't stop the ones after it", ts.Contains("Reentrant.Install") && ts.Contains("Wrench.Install"));
        Check("language: nested language change doesn't rerun the loop", ts.FindAll(s => s == "Mug.Refresh").Count == 1);
        Check("language: dirty flag cleared (no reload loop)", !ThinkInvisible.TinkersSatchel.AutoConfigModule.globalLanguageDirty);
        LangGame.SetCurrentLanguage("en", false);
        Check("language: runs again on the next language change", ts.FindAll(s => s == "Mug.Refresh").Count == 2);
        Exception trigErr = null;   // no TinkersSatchel/RoR2 assemblies here: the trigger must back off quietly
        try { langFix.GetMethod("OnTinkersLanguageHookSkipped").Invoke(null, new object[] { harmony }); } catch (Exception e) { trigErr = e; }
        Check("language: trigger without the mod loaded doesn't throw", trigErr == null);

        // Monster limit (Multitudes): the game's own check uses MonsterLimit, and Multitudes' copies stop at it.
        SnoresvilleMultitudes.MultitudesNoPriceScale.ConfigLiftMonsterLimit = new SnoresvilleMultitudes.FakeEntry { Value = true };
        shieldType.Assembly.GetType("CompatShield.MonsterLimitTweak").GetMethod("Apply").Invoke(null,
            new object[] { harmony, typeof(Program).Assembly, typeof(Program).Assembly, 80 });
        RoR2.TeamComponent.Fill(RoR2.TeamIndex.Monster, 50);
        Check("limit: normal spawn allowed past the game's 40", new RoR2.MasterSummon().Perform() != null);
        Check("limit: game's monster limit is now 80", RoR2.TeamCatalog.GetTeamDef(RoR2.TeamIndex.Monster).softCharacterLimit == 80);
        RoR2.TeamComponent.Fill(RoR2.TeamIndex.Monster, 80);
        Check("limit: normal spawn blocked at 80", new RoR2.MasterSummon().Perform() == null);
        Check("limit: spawns the game exempts still happen at 80", new RoR2.MasterSummon { ignoreTeamMemberLimit = true }.Perform() != null);
        var multitudes = new SnoresvilleMultitudes.MultitudesNoPriceScale();
        RoR2.TeamComponent.Fill(RoR2.TeamIndex.Monster, 79);
        var dupReq = new RoR2.DirectorSpawnRequest { ignoreTeamMemberLimit = true };
        multitudes.SpawnDuplicate(null, dupReq, default);
        multitudes.SpawnDuplicate(null, dupReq, default);
        Check("limit: Multitudes copy made at 79, skipped at 80", SnoresvilleMultitudes.MultitudesNoPriceScale.copiesMade == 1
            && RoR2.TeamComponent.GetTeamMembers(RoR2.TeamIndex.Monster).Count == 80);
        RoR2.TeamComponent.Fill(RoR2.TeamIndex.Void, 10);
        multitudes.SpawnDuplicate(null, new RoR2.DirectorSpawnRequest { teamIndexOverride = RoR2.TeamIndex.Void }, default);
        Check("limit: copies counted against their own team", SnoresvilleMultitudes.MultitudesNoPriceScale.copiesMade == 2);

        // Spawn credits: only the stage's regular spawners earn more, and the boost lands in what Awake builds.
        var spawnFix = shieldType.Assembly.GetType("CompatShield.SpawnCreditTweak");
        spawnFix.GetField("IsStageSpawner", BindingFlags.NonPublic | BindingFlags.Static)
            .SetValue(null, new Func<object, bool>(d => ((RoR2.CombatDirector)d).onDirectorObject));
        spawnFix.GetMethod("Apply").Invoke(null, new object[] { harmony, typeof(Program).Assembly, 1.5f });
        var stageSpawner = new RoR2.CombatDirector { creditMultiplier = 0.75f, onDirectorObject = true };
        var teleporterSpawner = new RoR2.CombatDirector { creditMultiplier = 1f };
        stageSpawner.Awake();
        teleporterSpawner.Awake();
        Check("spawns: stage spawner earns 1.5x credits", Math.Abs(stageSpawner.moneyWaveMultiplier - 1.125f) < 1e-4);
        Check("spawns: teleporter/shrine spawners unchanged", teleporterSpawner.moneyWaveMultiplier == 1f);

        // AudioOverlapFix stop fix: duplicate Play events in a frame are still dropped, duplicate Stops never are.
        shieldType.Assembly.GetType("CompatShield.StopSoundFix").GetMethod("Apply").Invoke(null,
            new object[] { harmony, typeof(Program).Assembly, typeof(Program).Assembly });
        AudioOverlapFix.Main.NextFrame();
        bool play1 = AudioOverlapFix.SoundEnginePatcher.shouldPostEvent("Play_bell_loop");
        bool play2 = AudioOverlapFix.SoundEnginePatcher.shouldPostEvent("Play_bell_loop");
        bool stop1 = AudioOverlapFix.SoundEnginePatcher.shouldPostEvent("Stop_bell_loop");
        bool stop2 = AudioOverlapFix.SoundEnginePatcher.shouldPostEvent("Stop_bell_loop");
        Check("sound: duplicate Play in one frame still dropped", play1 && !play2);
        Check("sound: duplicate Stop by name always goes through", stop1 && stop2);
        uint stopId = AkSoundEngine.GetIDFromString("Stop_nullifier_loop");   // the game hashes names once, e.g. network sounds
        uint playId = AkSoundEngine.GetIDFromString("Play_nullifier_loop");
        AudioOverlapFix.Main.NextFrame();
        Check("sound: duplicate Stop by ID always goes through",
            AudioOverlapFix.SoundEnginePatcher.shouldPostEvent(stopId) && AudioOverlapFix.SoundEnginePatcher.shouldPostEvent(stopId));
        Check("sound: duplicate Play by ID still dropped",
            AudioOverlapFix.SoundEnginePatcher.shouldPostEvent(playId) && !AudioOverlapFix.SoundEnginePatcher.shouldPostEvent(playId));

        // Moved-asset redirect on this game build's real catalog keys: every moved path the mods use resolves to its new
        // path; gone and ambiguous paths, existing paths and non-game keys pass through unchanged.
        var redirect = shieldType.Assembly.GetType("CompatShield.AssetRedirect");
        redirect.GetMethod("Apply").Invoke(null, new object[] { harmony, typeof(FakeAddressablesImpl).GetMethod(nameof(FakeAddressablesImpl.EvaluateKey)) });
        // .NET Framework caps paths at 260 chars, and this harness's folder is deep: SHIELDTEST_DATA can point at a short copy.
        var data = Environment.GetEnvironmentVariable("SHIELDTEST_DATA")
            ?? System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(Program).Assembly.Location), @"..\..\.."));
        redirect.GetMethod("BuildIndex").Invoke(null, new object[] {
            System.Linq.Enumerable.Cast<object>(System.IO.File.ReadAllLines(System.IO.Path.Combine(data, "catalog_keys.txt"))) });
        var impl = new FakeAddressablesImpl();
        int movedOk = 0, movedBad = 0, keptOk = 0, keptBad = 0;
        foreach (var line in System.IO.File.ReadAllLines(System.IO.Path.Combine(data, "missing_keys.tsv"))) {
            var parts = line.Split('\t');
            var got = (string)impl.EvaluateKey(parts[0]);
            bool ok = got == (parts[1].Length > 0 ? parts[1] : parts[0]);
            if (parts[1].Length > 0) { if (ok) movedOk++; else movedBad++; } else { if (ok) keptOk++; else keptBad++; }
            if (!ok && movedBad + keptBad <= 3) Console.WriteLine($"  redirect mismatch: {parts[0]} -> {got}");
        }
        Console.WriteLine($"  redirect on real keys: {movedOk} moved ok, {movedBad} wrong; {keptOk} gone/ambiguous left alone, {keptBad} changed");
        Check("assets: every moved path the mods use resolves to its new path", movedBad == 0 && movedOk > 600);
        Check("assets: gone and ambiguous paths left alone", keptBad == 0 && keptOk > 0);
        Check("assets: existing paths unchanged",
            (string)impl.EvaluateKey("RoR2/Base/BeetleGuard/BeetleGuardBody.prefab") == "RoR2/Base/BeetleGuard/BeetleGuardBody.prefab");
        Check("assets: non-game keys unchanged",
            (string)impl.EvaluateKey("86212504c7e9f468db2300dc5932dc17") == "86212504c7e9f468db2300dc5932dc17" && impl.EvaluateKey(42) is int);

        // ZetAspects drop fix: the slotless-elite kill used to crash before orig; after the fix orig runs and the drop
        // uses the inventory's equipment. The slot branch is untouched.
        Action<object, object> origDeath = (s, b) => ZetStandIn.origCalls++;
        var slotless = new RoR2.CharacterBody { inv = new RoR2.Inventory { current = 7 } };
        Exception zetBefore = null;
        try { ZetStandIn.EarlyDropChanceHook(origDeath, null, slotless); } catch (Exception e) { zetBefore = e; }
        Check("zet: baseline slotless elite kill crashes before orig", zetBefore is NullReferenceException && ZetStandIn.origCalls == 0);
        shieldType.Assembly.GetType("CompatShield.ZetAspectsDropFix").GetMethod("Apply").Invoke(null,
            new object[] { harmony, typeof(ZetStandIn).GetMethod(nameof(ZetStandIn.EarlyDropChanceHook)) });
        Exception zetAfter = null;
        try { ZetStandIn.EarlyDropChanceHook(origDeath, null, slotless); } catch (Exception e) { zetAfter = e; }
        Check("zet: slotless elite kill no longer crashes, orig runs", zetAfter == null && ZetStandIn.origCalls == 1);
        Check("zet: drop uses the inventory's aspect", ZetStandIn.dropped == 7);
        ZetStandIn.EarlyDropChanceHook(origDeath, null, new RoR2.CharacterBody { slot = new RoR2.EquipmentSlot { idx = 7 } });
        Check("zet: elites with an equipment slot unchanged", ZetStandIn.dropped == 7 && ZetStandIn.origCalls == 2);
        ZetStandIn.EarlyDropChanceHook(origDeath, null, new RoR2.CharacterBody { inv = new RoR2.Inventory { current = 3 } });
        Check("zet: non-aspect equipment still means no drop", ZetStandIn.dropped == -1 && ZetStandIn.origCalls == 3);
        shieldType.Assembly.GetType("CompatShield.ZetAspectsDropFix").GetMethod("Apply").Invoke(null,
            new object[] { harmony, typeof(ZetFixedStandIn).GetMethod(nameof(ZetFixedStandIn.EarlyDropChanceHook)) });
        ZetFixedStandIn.EarlyDropChanceHook(origDeath, null, new RoR2.CharacterBody { inv = new RoR2.Inventory { current = 7 } });
        Check("zet: already-fixed version recognized and works", ZetFixedStandIn.dropped == 7);

        // Gasoline chain fix: a 7-link chain never nests more than one proc deep, and every link still happens.
        RoR2.GlobalEventManager.chainLeft = 6;
        RoR2.GlobalEventManager.ProcIgniteOnKill(null, 1, null, RoR2.TeamIndex.Player);
        Check("gasoline: baseline chain nests 7 deep", RoR2.GlobalEventManager.maxActive == 7 && RoR2.GlobalEventManager.calls == 7);
        shieldType.Assembly.GetType("CompatShield.GasolineChainFix").GetMethod("Apply").Invoke(null, new object[] { harmony, typeof(Program).Assembly });
        RoR2.GlobalEventManager.calls = RoR2.GlobalEventManager.maxActive = 0;
        RoR2.GlobalEventManager.chainLeft = 6;
        RoR2.GlobalEventManager.ProcIgniteOnKill(null, 1, null, RoR2.TeamIndex.Player);
        Console.WriteLine($"  gasoline: {RoR2.GlobalEventManager.calls} procs in the kill's own frame, max {RoR2.GlobalEventManager.maxActive} deep");
        Check("gasoline: never more than one proc on the stack", RoR2.GlobalEventManager.maxActive == 1);
        Check("gasoline: whole chain still happens in the same frame", RoR2.GlobalEventManager.calls == 7);
        RoR2.RoR2Application.Tick();
        Check("gasoline: nothing left queued, nothing runs twice", RoR2.GlobalEventManager.calls == 7);
        // A blast that throws partway through (another mod's damage hook) must not stop the links after it.
        RoR2.GlobalEventManager.calls = RoR2.GlobalEventManager.maxActive = 0;
        RoR2.GlobalEventManager.chainLeft = 6;
        RoR2.GlobalEventManager.throwAtCall = 3;
        Exception gasErr = null;
        try { RoR2.GlobalEventManager.ProcIgniteOnKill(null, 1, null, RoR2.TeamIndex.Player); } catch (Exception e) { gasErr = e; }
        RoR2.GlobalEventManager.throwAtCall = -1;
        Check("gasoline: a throwing link is contained, chain continues", gasErr == null && RoR2.GlobalEventManager.calls == 7);
        RoR2.GlobalEventManager.calls = 0;
        RoR2.GlobalEventManager.chainLeft = 2;
        RoR2.GlobalEventManager.ProcIgniteOnKill(null, 1, null, RoR2.TeamIndex.Player);
        Check("gasoline: depth unwound after the throw (next chain runs normally)", RoR2.GlobalEventManager.calls == 3 && RoR2.GlobalEventManager.maxActive == 1);
        // An error thrown below the outermost proc passes through its finalizer with its own trace (Boom heads it).
        RoR2.GlobalEventManager.calls = 0;
        RoR2.GlobalEventManager.chainLeft = 0;
        RoR2.GlobalEventManager.deepThrowAt = 1;
        Exception gasDeep = null;
        try { RoR2.GlobalEventManager.ProcIgniteOnKill(null, 1, null, RoR2.TeamIndex.Player); } catch (Exception e) { gasDeep = e; }
        RoR2.GlobalEventManager.deepThrowAt = -1;
        Check("gasoline: an error passing through keeps its own stack trace", gasDeep is InvalidOperationException
            && gasDeep.StackTrace != null && gasDeep.StackTrace.Split('\n')[0].Contains("Boom"));
        RoR2.GlobalEventManager.calls = RoR2.GlobalEventManager.maxActive = 0;
        RoR2.GlobalEventManager.chainLeft = 2;
        RoR2.GlobalEventManager.ProcIgniteOnKill(null, 1, null, RoR2.TeamIndex.Player);
        Check("gasoline: still one at a time after a pass-through", RoR2.GlobalEventManager.calls == 3 && RoR2.GlobalEventManager.maxActive == 1);

        // After-orig guard (Artifact of Genetics): its own crash after orig is caught; anything out of orig still escapes.
        int damageApplied = 0;
        GeneticsArtifact.orig_TakeDamage applyDamage = (s, d) => damageApplied++;
        GeneticsArtifact.orig_TakeDamage throwingOrig = (s, d) => throw new NullReferenceException("another mod deeper in the chain");
        GeneticsArtifact.GeneEngineDriver.crashAfter = true;
        Exception genBefore = null;
        try { GeneticsArtifact.GeneEngineDriver.HealthComponent_TakeDamage(applyDamage, null, null); } catch (Exception e) { genBefore = e; }
        Check("genetics: baseline crash after orig escapes the hook", genBefore is NullReferenceException && damageApplied == 1);
        shieldType.Assembly.GetType("CompatShield.AfterOrigGuard").GetMethod("Apply").Invoke(null, new object[] { harmony, typeof(Program).Assembly,
            "GeneticsArtifact.GeneEngineDriver", "HealthComponent_TakeDamage", typeof(NullReferenceException), "Genetics test note" });
        Exception genAfter = null;
        try { GeneticsArtifact.GeneEngineDriver.HealthComponent_TakeDamage(applyDamage, null, null); } catch (Exception e) { genAfter = e; }
        Check("genetics: its own crash after the damage is caught, damage applied", genAfter == null && damageApplied == 2);
        Exception genFromOrig = null;
        try { GeneticsArtifact.GeneEngineDriver.HealthComponent_TakeDamage(throwingOrig, null, null); } catch (Exception e) { genFromOrig = e; }
        Check("genetics: a crash coming out of orig still escapes", genFromOrig is NullReferenceException && genFromOrig.Message.Contains("deeper"));
        // The thrower's own frame (the orig lambda) must still head the trace: a finalizer rethrow would restart it at the hook.
        Check("genetics: a crash coming out of orig keeps its own stack trace", genFromOrig != null && genFromOrig.StackTrace != null
            && genFromOrig.StackTrace.Split('\n')[0].Contains("b__"));
        GeneticsArtifact.GeneEngineDriver.crashAfter = false;
        GeneticsArtifact.GeneEngineDriver.otherAfter = true;
        Exception genOther = null;
        try { GeneticsArtifact.GeneEngineDriver.HealthComponent_TakeDamage(applyDamage, null, null); } catch (Exception e) { genOther = e; }
        GeneticsArtifact.GeneEngineDriver.otherAfter = false;
        Check("genetics: other exception types still escape", genOther is InvalidOperationException);
        // Damage inside damage: the inner call crashes after its orig (caught); the outer call then finishes normally.
        GeneticsArtifact.orig_TakeDamage nested = (s, d) => {
            GeneticsArtifact.GeneEngineDriver.crashAfter = true;
            GeneticsArtifact.GeneEngineDriver.HealthComponent_TakeDamage(applyDamage, null, null);
            GeneticsArtifact.GeneEngineDriver.crashAfter = false;
        };
        int bookBefore = GeneticsArtifact.GeneEngineDriver.bookkeepingDone;
        Exception genNested = null;
        try { GeneticsArtifact.GeneEngineDriver.HealthComponent_TakeDamage(nested, null, null); } catch (Exception e) { genNested = e; }
        Check("genetics: nested calls keep their own after-orig state", genNested == null && GeneticsArtifact.GeneEngineDriver.bookkeepingDone == bookBefore + 1);

        // Handler guard: Strange Can's known crash no longer stops the death event reaching the handlers after it.
        var can = new SS2.Items.StrangeCan();
        int laterHandlerRuns = 0;
        Action<RoR2.DamageReport> deathEvent = can.OnCharacterDeathGlobal;
        deathEvent += r => laterHandlerRuns++;
        Exception guardBefore = null;
        try { deathEvent(new RoR2.DamageReport()); } catch (Exception e) { guardBefore = e; }
        Check("guard: baseline Strange Can crash stops the later handler", guardBefore is System.Collections.Generic.KeyNotFoundException && laterHandlerRuns == 0);
        shieldType.Assembly.GetType("CompatShield.HandlerGuard").GetMethod("Apply").Invoke(null, new object[] {
            harmony, typeof(Program).Assembly, "SS2.Items.StrangeCan", "OnCharacterDeathGlobal", typeof(System.Collections.Generic.KeyNotFoundException), "Strange Can test note" });
        Exception guardAfter = null;
        try { deathEvent(new RoR2.DamageReport()); deathEvent(new RoR2.DamageReport()); } catch (Exception e) { guardAfter = e; }
        Check("guard: the known crash is caught, later handler runs", guardAfter == null && laterHandlerRuns == 2);
        SS2.Items.StrangeCan.throwOther = true;
        Exception guardOther = null;
        try { deathEvent(new RoR2.DamageReport()); } catch (Exception e) { guardOther = e; }
        SS2.Items.StrangeCan.throwOther = false;
        Check("guard: any other exception still surfaces", guardOther is InvalidOperationException);
        SS2.Items.StrangeCan.throwKeyNotFound = false;
        SS2.Items.StrangeCan.lastSeen = 0;
        can.OnCharacterDeathGlobal(new RoR2.DamageReport());
        SS2.Items.StrangeCan.throwKeyNotFound = true;
        Check("guard: with no crash the handler runs its whole loop (nested try/finally intact)", SS2.Items.StrangeCan.lastSeen == 3);

        // Handler guard on a nested type (Relic of Force's Behavior): the damage receiver's crash no longer escapes into
        // TakeDamageProcess, so the death check after it still runs.
        var relic = new SS2.Items.RelicOfForce.Behavior();
        int deathChecks = 0;
        Action<RoR2.DamageReport> takeDamageTail = r => { relic.OnDamageDealtServer(r); deathChecks++; };
        Exception relicBefore = null;
        try { takeDamageTail(new RoR2.DamageReport()); } catch (Exception e) { relicBefore = e; }
        Check("relic: baseline crash skips the death check", relicBefore is NullReferenceException && deathChecks == 0);
        shieldType.Assembly.GetType("CompatShield.HandlerGuard").GetMethod("Apply").Invoke(null, new object[] {
            harmony, typeof(Program).Assembly, "SS2.Items.RelicOfForce+Behavior", "OnDamageDealtServer", typeof(NullReferenceException), "Relic test note" });
        Exception relicAfter = null;
        try { takeDamageTail(new RoR2.DamageReport()); takeDamageTail(new RoR2.DamageReport()); } catch (Exception e) { relicAfter = e; }
        Check("relic: crash caught, death check runs every time", relicAfter == null && deathChecks == 2);

        // PrintToInventory: with ShareSuite it turns itself off; with the fix it loads, scrappers only.
        var pti = typeof(PrintToInventory.PrintToInventory);
        bool PtiOthersOff() => !PrintToInventory.PrintToInventory.affectPrinters && !PrintToInventory.PrintToInventory.affectCauldrons
            && !PrintToInventory.PrintToInventory.affectCleanse && !PrintToInventory.PrintToInventory.affectWanderingChef;
        new PrintToInventory.PrintToInventory().Awake();
        Check("pti: baseline turns itself off with ShareSuite", PrintToInventory.PrintToInventory.disabled == 1 && PrintToInventory.PrintToInventory.finished == 0);
        var ptiFix = shieldType.Assembly.GetType("CompatShield.PrintToInventoryScrapperFix");
        ptiFix.GetMethod("Apply").Invoke(null, new object[] { harmony, typeof(Program).Assembly });
        new PrintToInventory.PrintToInventory().Awake();
        Check("pti: with ShareSuite it now loads", PrintToInventory.PrintToInventory.disabled == 1 && PrintToInventory.PrintToInventory.finished == 1);
        Check("pti: scrappers on, printers/cauldrons/cleanse/chef off", PrintToInventory.PrintToInventory.affectScrappers && PtiOthersOff());
        new PrintToInventory.PrintToInventory().Awake();
        Check("pti: stays scrapper-only on a second Awake", PrintToInventory.PrintToInventory.affectScrappers && PtiOthersOff() && PrintToInventory.PrintToInventory.finished == 2);
        PrintToInventory.PrintToInventory.configScrapper = false;
        new PrintToInventory.PrintToInventory().Awake();
        PrintToInventory.PrintToInventory.configScrapper = true;
        Check("pti: its own Scrapper = false is respected", !PrintToInventory.PrintToInventory.affectScrappers && PtiOthersOff());
        PrintToInventory.PrintToInventory.PluginInfos.Clear();
        new PrintToInventory.PrintToInventory().Awake();
        Check("pti: without ShareSuite everything works as before", PrintToInventory.PrintToInventory.affectScrappers && PrintToInventory.PrintToInventory.affectPrinters
            && PrintToInventory.PrintToInventory.affectCauldrons && PrintToInventory.PrintToInventory.affectCleanse && PrintToInventory.PrintToInventory.affectWanderingChef);
        ptiFix.GetMethod("Apply").Invoke(null, new object[] { harmony, typeof(string).Assembly });

        // Sacrifice drop chance: the multiplier lands on the final chance, after ShareSuite's divide and RiskyMod's clamp,
        // whichever IL hook comes first; luck/master pass through; a zero chance stays zero; other shapes are left alone.
        float log2of3 = (float)(Math.Log(3) / Math.Log(2));
        RoR2.Artifacts.SacrificeArtifactManager.Kill();
        Check("sacrifice: baseline vanilla chance 5 x log2(2+1) = 7.92%", Near(RoR2.Util.lastChance, 5f * log2of3));
        var shareSuiteHook = new MonoMod.RuntimeDetour.Hook(typeof(RoR2.Util).GetMethod(nameof(RoR2.Util.GetExpAdjustedDropChancePercent)),
            new Func<Func<float, int, float>, float, int, float>((orig, chance, value) => orig(chance / 3f, value)));   // ShareSuite: base / 3 players
        RoR2.Artifacts.SacrificeArtifactManager.Kill();
        Check("sacrifice: ShareSuite-style hook divides by 3 players (2.64%)", Near(RoR2.Util.lastChance, 5f / 3f * log2of3));
        var deathA = typeof(RoR2.Artifacts.SacrificeArtifactManager).GetMethod("OnServerCharacterDeath", BindingFlags.NonPublic | BindingFlags.Static);
        var riskyA = new MonoMod.RuntimeDetour.ILHook(deathA, RiskyModSacrifice);
        RoR2.Artifacts.SacrificeArtifactManager.Kill();
        Check("sacrifice: baseline RiskyMod-style clamp overrides ShareSuite (flat 8%)", Near(RoR2.Util.lastChance, 8f));
        var sac = shieldType.Assembly.GetType("CompatShield.SacrificeDropTweak");
        sac.GetMethod("Apply").Invoke(null, new object[] { harmony, typeof(Program).Assembly, 0.6f });
        RoR2.Artifacts.SacrificeArtifactManager.Kill();
        Check("sacrifice: x0.6 lands after RiskyMod's clamp (its hook added first): 4.8%", Near(RoR2.Util.lastChance, 4.8f));
        riskyA.Dispose();
        RoR2.Artifacts.SacrificeArtifactManager.Kill();
        Check("sacrifice: RiskyMod option off -> x0.6 on top of ShareSuite (1.58%)", Near(RoR2.Util.lastChance, 0.6f * 5f / 3f * log2of3));
        riskyA = new MonoMod.RuntimeDetour.ILHook(deathA, RiskyModSacrifice);
        RoR2.Artifacts.SacrificeArtifactManager.Kill();
        Check("sacrifice: RiskyMod hook re-added after the patch: still 4.8%", Near(RoR2.Util.lastChance, 4.8f));
        // Another mod adding and removing a hook rebuilds the method's chain: the transpiler runs again and still matches.
        new MonoMod.RuntimeDetour.ILHook(deathA, il => { }).Dispose();
        RoR2.Artifacts.SacrificeArtifactManager.Kill();
        Check("sacrifice: still 4.8% and matched after a chain rebuild", Near(RoR2.Util.lastChance, 4.8f)
            && (bool)sac.GetField("matched", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null));
        RoR2.Artifacts.SacrificeArtifactManager.spawnValue = 0;
        int rollsBefore = RoR2.Util.rolls;
        RoR2.Artifacts.SacrificeArtifactManager.Kill();
        RoR2.Artifacts.SacrificeArtifactManager.spawnValue = 2;
        Check("sacrifice: a victim with no spawn value still rolls 0%", RoR2.Util.rolls == rollsBefore + 1 && RoR2.Util.lastChance == 0f);
        int dropsBefore = RoR2.Artifacts.SacrificeArtifactManager.drops;
        RoR2.Util.lucky = true;
        RoR2.Artifacts.SacrificeArtifactManager.Kill();
        RoR2.Util.lucky = false;
        RoR2.Artifacts.SacrificeArtifactManager.Kill();
        Check("sacrifice: the roll's result still decides the drop", RoR2.Artifacts.SacrificeArtifactManager.drops == dropsBefore + 1);
        Check("sacrifice: the method's own struct local survives the patch",
            RoR2.Artifacts.SacrificeArtifactManager.lastDropped == RoR2.Artifacts.SacrificeArtifactManager.next && RoR2.Artifacts.SacrificeArtifactManager.lastPad == 3.25f);
        // A rebuild that loses the match after it once worked: warn once that drops are back to the unscaled chance.
        var matchedField = sac.GetField("matched", BindingFlags.NonPublic | BindingFlags.Static);
        var rollMethod = typeof(RoR2.Util).GetMethod(nameof(RoR2.Util.CheckRoll));
        Func<System.Collections.Generic.List<CodeInstruction>> twoRollBody = () => {
            var l = new System.Collections.Generic.List<CodeInstruction>();
            for (int i = 0; i < 2; i++) {
                l.Add(new CodeInstruction(System.Reflection.Emit.OpCodes.Ldc_R4, 1f));
                l.Add(new CodeInstruction(System.Reflection.Emit.OpCodes.Ldc_R4, 0f));
                l.Add(new CodeInstruction(System.Reflection.Emit.OpCodes.Ldnull));
                l.Add(new CodeInstruction(System.Reflection.Emit.OpCodes.Call, rollMethod));
                l.Add(new CodeInstruction(System.Reflection.Emit.OpCodes.Pop));
            }
            l.Add(new CodeInstruction(System.Reflection.Emit.OpCodes.Ret));
            return l;
        };
        var sacTranspile = sac.GetMethod("Transpiler", BindingFlags.NonPublic | BindingFlags.Static);
        var throwawayGen = new System.Reflection.Emit.DynamicMethod("sacrebuild", typeof(void), Type.EmptyTypes).GetILGenerator();
        matchedField.SetValue(null, true);
        var lostOnce = System.Linq.Enumerable.ToList((System.Collections.Generic.IEnumerable<CodeInstruction>)sacTranspile.Invoke(null, new object[] { twoRollBody(), throwawayGen }));
        int lostWarnings = logs.FindAll(l => l.Contains("multiplier is no longer applied")).Count;
        Check("sacrifice: a rebuild that loses the match returns the body unchanged and warns once",
            lostOnce.Count == 11 && !(bool)matchedField.GetValue(null) && lostWarnings == 1);
        sacTranspile.Invoke(null, new object[] { twoRollBody(), throwawayGen });
        Check("sacrifice: the lost-match warning is not repeated", logs.FindAll(l => l.Contains("multiplier is no longer applied")).Count == 1);
        matchedField.SetValue(null, false);   // the two-roll patch below models a first apply
        var sacTranspiler = new HarmonyMethod(sac.GetMethod("Transpiler", BindingFlags.NonPublic | BindingFlags.Static));
        var deathB = typeof(RoR2.Artifacts.SacrificeHarmonyFirst).GetMethod(nameof(RoR2.Artifacts.SacrificeHarmonyFirst.OnServerCharacterDeath));
        harmony.Patch(deathB, transpiler: sacTranspiler);
        var riskyB = new MonoMod.RuntimeDetour.ILHook(deathB, RiskyModSacrifice);
        RoR2.Artifacts.SacrificeHarmonyFirst.OnServerCharacterDeath(new RoR2.DamageReport());
        Check("sacrifice: patched first, RiskyMod hook after (in-game order): 4.8%", Near(RoR2.Util.lastChance, 4.8f));
        Check("sacrifice: luck and master reach CheckRoll unchanged", RoR2.Util.lastLuck == 1.5f
            && ReferenceEquals(RoR2.Util.lastMaster, RoR2.Artifacts.SacrificeHarmonyFirst.master));
        harmony.Patch(typeof(RoR2.Artifacts.SacrificeTwoRolls).GetMethod(nameof(RoR2.Artifacts.SacrificeTwoRolls.OnServerCharacterDeath)), transpiler: sacTranspiler);
        bool twoRollsMatched = (bool)sac.GetField("matched", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        RoR2.Artifacts.SacrificeTwoRolls.OnServerCharacterDeath(null);
        Check("sacrifice: a method with two rolls is not matched and left unchanged", !twoRollsMatched && Near(RoR2.Util.lastChance, 5f / 3f * log2of3));
        Exception sacMissing = null;
        try { sac.GetMethod("Apply").Invoke(null, new object[] { harmony, typeof(string).Assembly, 0.6f }); } catch (Exception e) { sacMissing = e; }
        Check("sacrifice: an assembly without the method is reported, not thrown", sacMissing == null);
        riskyA.Dispose(); riskyB.Dispose(); shareSuiteHook.Dispose();

        // REX flower expiry (FlowerRootFix)
        var TF = typeof(EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile);
        var b1 = new RoR2.CharacterBody { entangle = 1 };
        var bGone = new RoR2.CharacterBody { entangle = 1, destroyed = true, eliteAffix = true };
        var b2 = new RoR2.CharacterBody { entangle = 1 };
        var flower0 = new EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile();
        flower0.Root(b1, bGone, b2);
        Exception flowerBefore = null;
        try { flower0.OnExit(); } catch (Exception e) { flowerBefore = e; }
        Check("flower: baseline destroyed elite crashes OnExit, the enemy after it stays rooted, no exit sound",
            flowerBefore is NullReferenceException && b1.entangle == 0 && b2.entangle == 1
            && EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile.exitSounds == 0);
        var flowerFix = shieldType.Assembly.GetType("CompatShield.FlowerRootFix");
        var loopMatches = flowerFix.GetMethod("LoopMatches", BindingFlags.NonPublic | BindingFlags.Static);
        Check("flower: the game-shaped OnExit loop is recognized",
            (bool)loopMatches.Invoke(null, new object[] { PatchProcessor.ReadMethodBody(TF.GetMethod("OnExit")) }));
        Check("flower: an OnExit that does more per body is not taken over",
            !(bool)loopMatches.Invoke(null, new object[] { PatchProcessor.ReadMethodBody(typeof(EntityStates.Treebot.TreebotFlower.ChangedFlower).GetMethod("OnExit")) }));
        Check("flower: an OnExit whose loop also calls a non-CharacterBody helper per body is not taken over",
            !(bool)loopMatches.Invoke(null, new object[] { PatchProcessor.ReadMethodBody(typeof(EntityStates.Treebot.TreebotFlower.ChangedFlowerHelper).GetMethod("OnExit")) }));
        Check("flower: a changed exit effect after an unchanged loop is still recognized",
            (bool)loopMatches.Invoke(null, new object[] { PatchProcessor.ReadMethodBody(typeof(EntityStates.Treebot.TreebotFlower.ChangedExitFlower).GetMethod("OnExit")) }));
        flowerFix.GetField("IsGone", BindingFlags.NonPublic | BindingFlags.Static)
            .SetValue(null, new Func<object, bool>(b => b == null || ((RoR2.CharacterBody)b).destroyed));
        flowerFix.GetMethod("Apply").Invoke(null, new object[] { harmony, typeof(Program).Assembly });
        var c1 = new RoR2.CharacterBody { entangle = 1 };
        var cGoneElite = new RoR2.CharacterBody { entangle = 1, destroyed = true, eliteAffix = true };
        var cGonePlain = new RoR2.CharacterBody { entangle = 1, destroyed = true };
        var c2 = new RoR2.CharacterBody { entangle = 1 };
        var c3 = new RoR2.CharacterBody { entangle = 2 };   // also rooted by a second flower: only one stack comes off
        var flower1 = new EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile();
        flower1.Root(c1, cGoneElite, cGonePlain, null, c2, c3);
        int removesBefore = RoR2.CharacterBody.removeCalls;
        Exception flowerAfter = null;
        try { flower1.OnExit(); } catch (Exception e) { flowerAfter = e; }
        Check("flower: OnExit no longer throws; every living enemy unrooted, one stack each",
            flowerAfter == null && c1.entangle == 0 && c2.entangle == 0 && c3.entangle == 1);
        Check("flower: gone (and null) bodies are never touched", RoR2.CharacterBody.removeCalls - removesBefore == 3
            && cGoneElite.entangle == 1 && cGonePlain.entangle == 1);
        Check("flower: exit sound/effect and base.OnExit still run, list cleared",
            EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile.exitSounds == 1
            && EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile.baseExits == 1 && flower1.Cleared);
        removesBefore = RoR2.CharacterBody.removeCalls;
        flower1.OnExit();
        Check("flower: a second OnExit removes nothing twice", RoR2.CharacterBody.removeCalls == removesBefore && c3.entangle == 1);
        var d1 = new RoR2.CharacterBody { entangle = 1, throwOther = true };
        var d2 = new RoR2.CharacterBody { entangle = 1 };
        var flower2 = new EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile();
        flower2.Root(d1, d2);
        Exception flowerOther = null;
        try { flower2.OnExit(); } catch (Exception e) { flowerOther = e; }
        Check("flower: another mod's throw on one enemy is contained, the next is still unrooted",
            flowerOther == null && d1.entangle == 0 && d2.entangle == 0 && flower2.Cleared);
        int soundsBefore = EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile.exitSounds;
        var clientFlower = new EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile();
        var emptyFlower = new EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile();
        emptyFlower.Root();
        Exception flowerClient = null;
        try { clientFlower.OnExit(); emptyFlower.OnExit(); } catch (Exception e) { flowerClient = e; }
        Check("flower: client flower and empty list behave like the game",
            flowerClient == null && EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile.exitSounds == soundsBefore + 2 && emptyFlower.Cleared);
        var defaultGone = (Func<object, bool>)Delegate.CreateDelegate(typeof(Func<object, bool>),
            flowerFix.GetMethod("UnityGone", BindingFlags.NonPublic | BindingFlags.Static));
        Check("flower: default gone-check runs outside Unity (null gone, plain object alive)",
            defaultGone(null) && !defaultGone(new RoR2.CharacterBody()));

        // Ancient Scepter REX flower (ScepterFlowerFix)
        // Ancient Scepter REX flower: a destroyed owner used to throw before orig on every pulse; now it's treated as no owner.
        // Real UnityEngine.GameObjects: Unity's null check here is managed (m_CachedPtr != 0), so no engine call runs.
        var cachedPtr = typeof(UnityEngine.Object).GetField("m_CachedPtr", BindingFlags.NonPublic | BindingFlags.Instance);
        Func<UnityEngine.GameObject> liveObject = () => {
            var go = (UnityEngine.GameObject)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(UnityEngine.GameObject));
            cachedPtr.SetValue(go, new IntPtr(1));
            return go;
        };
        Action<UnityEngine.GameObject> destroy = go => cachedPtr.SetValue(go, IntPtr.Zero);   // what Object.Destroy leaves behind
        var scepterHook = new AncientScepter.TreebotFlower2_2();
        var deadRex = liveObject();
        destroy(deadRex);
        var orphan = new EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile { owner = deadRex };
        Exception sfBefore = null;
        try { scepterHook.Pulse(orphan); } catch (Exception e) { sfBefore = e; }
        Check("scepter flower: baseline destroyed owner throws before the game's pulse", sfBefore is NullReferenceException && orphan.pulses == 0);
        shieldType.Assembly.GetType("CompatShield.ScepterFlowerFix").GetMethod("Apply").Invoke(null, new object[] { harmony, typeof(Program).Assembly });
        Exception sfAfter = null;
        try { scepterHook.Pulse(orphan); scepterHook.Pulse(orphan); } catch (Exception e) { sfAfter = e; }
        Check("scepter flower: destroyed owner no longer throws, the game's pulse runs every time", sfAfter == null && orphan.pulses == 2);
        Check("scepter flower: treated as no owner (normal radius, no scepter debuffs, no attacker)",
            orphan.lastRadius == 10f && AncientScepter.TreebotFlower2_2.debuffPasses == 0 && (object)orphan.owner == null && orphan.lastAttacker == null);
        var rex = liveObject();
        AncientScepter.TreebotFlower2_2.scepterOwner = rex;
        var scepterFlower = new EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile { owner = rex };
        scepterHook.Pulse(scepterFlower);
        Check("scepter flower: live scepter owner still gets double radius and debuffs, owner kept",
            scepterFlower.lastRadius == 20f && AncientScepter.TreebotFlower2_2.debuffPasses == 1 && ReferenceEquals(scepterFlower.owner, rex)
            && ReferenceEquals(scepterFlower.lastAttacker, rex) && EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile.radius == 10f);
        var plainRex = liveObject();
        var plainFlower = new EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile { owner = plainRex };
        scepterHook.Pulse(plainFlower);
        Check("scepter flower: live owner without the scepter unchanged", plainFlower.lastRadius == 10f && ReferenceEquals(plainFlower.owner, plainRex)
            && ReferenceEquals(plainFlower.lastAttacker, plainRex) && AncientScepter.TreebotFlower2_2.debuffPasses == 1);
        var ownerless = new EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile();
        Exception ownerlessErr = null;
        try { scepterHook.Pulse(ownerless); } catch (Exception e) { ownerlessErr = e; }
        Check("scepter flower: no owner at all unchanged", ownerlessErr == null && ownerless.pulses == 1 && ownerless.lastAttacker == null);
        destroy(rex);   // the scepter REX dies with its flower still up
        Exception midLife = null;
        try { scepterHook.Pulse(scepterFlower); } catch (Exception e) { midLife = e; }
        Check("scepter flower: owner destroyed mid-life, next pulse runs at normal radius",
            midLife == null && scepterFlower.pulses == 2 && scepterFlower.lastRadius == 10f && AncientScepter.TreebotFlower2_2.debuffPasses == 1);
        shieldType.Assembly.GetType("CompatShield.ScepterFlowerFix").GetMethod("Apply").Invoke(null, new object[] { harmony, typeof(string).Assembly });
        // REVIEW additions: a later failed lookup must not undo the working fix, and a second apply must not stack a second prefix.
        var orphan2 = new EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile { owner = deadRex };
        Exception afterFailedApply = null;
        try { scepterHook.Pulse(orphan2); } catch (Exception e) { afterFailedApply = e; }
        Check("scepter flower: a later failed lookup doesn't undo the working fix", afterFailedApply == null && orphan2.pulses == 1 && (object)orphan2.owner == null);
        shieldType.Assembly.GetType("CompatShield.ScepterFlowerFix").GetMethod("Apply").Invoke(null, new object[] { harmony, typeof(Program).Assembly });
        var orphan3 = new EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile { owner = deadRex };
        Exception secondApplyErr = null;
        try { scepterHook.Pulse(orphan3); } catch (Exception e) { secondApplyErr = e; }
        Check("scepter flower: a second apply is skipped, still one prefix", secondApplyErr == null && orphan3.pulses == 1
            && Harmony.GetPatchInfo(typeof(AncientScepter.TreebotFlower2_2).GetMethod("On_TreebotFlower2RootPulse", BindingFlags.NonPublic | BindingFlags.Instance)).Prefixes.Count == 1);

        // Stun duration fix: a freeze on a body whose SetStateOnHurt.Start hasn't run no longer throws, so the rest of the
        // hit (TakeDamageProcess's tail: damage numbers, kill handling) still runs; the list is filled like Start does.
        var slowed = new RoR2.AlterBuffDuration { durationScalar = 0.5f };
        var fresh = new RoR2.SetStateOnHurt { attached = new[] { slowed } };
        int hitTails = 0;
        Action<RoR2.SetStateOnHurt> freezeTail = v => { v.SetFrozenInternal(2f); hitTails++; };
        Exception sohBefore = null;
        try { freezeTail(fresh); } catch (Exception e) { sohBefore = e; }
        Check("stun: baseline freeze before Start throws and skips the rest of the hit", sohBefore is NullReferenceException && hitTails == 0);
        var stunFix = shieldType.Assembly.GetType("CompatShield.StunDurationFix");
        var findField = stunFix.GetField("FindControllers", BindingFlags.NonPublic | BindingFlags.Static);
        findField.SetValue(null, new Func<object, Array>(s => ((RoR2.SetStateOnHurt)s).attached));
        stunFix.GetMethod("Apply").Invoke(null, new object[] { harmony, typeof(Program).Assembly });
        Exception sohAfter = null;
        try { freezeTail(fresh); } catch (Exception e) { sohAfter = e; }
        Check("stun: freeze before Start no longer throws, rest of the hit runs", sohAfter == null && hitTails == 1);
        Check("stun: list filled like Start, body's own modifier applied (2s x 0.5)", fresh.Started && Math.Abs(fresh.frozenFor - 1f) < 1e-5);
        var fresh2 = new RoR2.SetStateOnHurt { attached = new[] { slowed } };
        freezeTail(fresh2);
        Check("stun: a second unstarted body is filled too", fresh2.Started && Math.Abs(fresh2.frozenFor - 1f) < 1e-5);
        var started = new RoR2.SetStateOnHurt { attached = new[] { new RoR2.AlterBuffDuration { durationScalar = 3f } } };
        started.Start();
        started.attached = new RoR2.AlterBuffDuration[0];   // a started body keeps the list Start stored; the prefix must not touch it
        freezeTail(started);
        Check("stun: a started body runs vanilla untouched (2s x 3)", Math.Abs(started.frozenFor - 6f) < 1e-5);
        var plain = new RoR2.SetStateOnHurt();
        freezeTail(plain);
        Check("stun: body with no modifiers gets the plain duration", plain.Started && plain.frozenFor == 2f);
        findField.SetValue(null, new Func<object, Array>(s => null));          // destroyed / not a live object
        var gone = new RoR2.SetStateOnHurt { attached = new[] { slowed } };
        Exception goneErr = null;
        try { freezeTail(gone); } catch (Exception e) { goneErr = e; }
        Check("stun: list unavailable -> unmodified duration, hit still lands", goneErr == null && gone.frozenFor == 2f && !gone.Started);
        findField.SetValue(null, new Func<object, Array>(s => new object[] { slowed }));   // e.g. a Component[] from the wrong overload
        var wrongType = new RoR2.SetStateOnHurt();
        freezeTail(wrongType);
        Check("stun: wrong array type is never stored", wrongType.frozenFor == 2f && !wrongType.Started);
        findField.SetValue(null, new Func<object, Array>(s => throw new InvalidOperationException("lookup failed")));
        Exception throwErr = null;
        var broken1 = new RoR2.SetStateOnHurt();
        var broken2 = new RoR2.SetStateOnHurt();
        try { freezeTail(broken1); freezeTail(broken2); } catch (Exception e) { throwErr = e; }
        Check("stun: a throwing lookup is contained, unmodified duration used", throwErr == null && broken1.frozenFor == 2f && broken2.frozenFor == 2f);
        stunFix.GetMethod("Apply").Invoke(null, new object[] { harmony, typeof(string).Assembly });   // a game without the members

        {
        // Inferno Mithrix fix. Slam hook: crashes at the ghost resize before orig on 1.5.0 (ghostPrefab empty). After the
        // fix the ghost comes from its address, everything Inferno meant still happens, and orig always runs.
        var hooksT = typeof(Inferno.Skill_Misc.Hooks);
        var pc = Inferno.Skill_Misc.Hooks.pillarProjectilePrefab.controller;
        pc.ghostPrefabAddress = "pillar-ghost-guid";
        int slamOrig = 0, ghostLoads = 0;
        var addressGhost = new InfernoStandIn.FakeGhost();
        On.EntityStates.BrotherMonster.WeaponSlam.orig_OnEnter slamOrigDel = s => slamOrig++;
        Exception slamBefore = null;
        try { Inferno.Skill_Misc.Hooks.WeaponSlam(slamOrigDel, null); } catch (Exception e) { slamBefore = e; }
        Check("inferno: baseline slam crashes at the ghost resize, orig never runs", slamBefore is NullReferenceException && slamOrig == 0
            && Inferno.Skill_Misc.Hooks.hasFired && Inferno.Skill_Misc.Hooks.duration == 3.5f);
        // Run-end cleanup: baseline stacks the dash hook (run 1 start, end, run 2 start = 3 attached, like the log).
        Inferno.Skill_Misc.HookBehavior.ApplyHooks(); Inferno.Skill_Misc.HookBehavior.UndoHooks(); Inferno.Skill_Misc.HookBehavior.ApplyHooks();
        Check("inferno: baseline dash hook stacks to 3 after two run starts", On.EntityStates.BrotherMonster.SlideIntroState.attached.Count == 3);
        void ClearHooks() {
            On.EntityStates.BrotherMonster.SlideIntroState.attached.Clear();
            On.EntityStates.BrotherMonster.WeaponSlam.attached.Clear();
            On.EntityStates.BrotherMonster.FistSlam.attached.Clear();
            On.EntityStates.NullifierMonster.DeathState.attached.Clear();
            On.EntityStates.VoidMegaCrab.DeathState.attached.Clear();
        }
        ClearHooks();
        for (int i = 0; i < 3; i++) Inferno.Skill_Misc.HookBehavior.UndoHooks();   // three non-Inferno run ends
        Inferno.Skill_Misc.HookBehavior.ApplyHooks();                                 // then an Inferno run starts
        int basePillar = On.EntityStates.BrotherMonster.WeaponSlam.attached.Count(d => d.Method.Name == "CleanupPillar");
        Check("inferno: baseline reset hooks pile up: 2 CleanupPillar copies left inside the next Inferno run's slam hook",
            basePillar == 2 && On.EntityStates.BrotherMonster.WeaponSlam.attached.FindIndex(d => d.Method.Name == "WeaponSlam")
                > On.EntityStates.BrotherMonster.WeaponSlam.attached.FindLastIndex(d => d.Method.Name == "CleanupPillar"));
        ClearHooks();

        var infFix = shieldType.Assembly.GetType("CompatShield.InfernoMithrixFix");
        infFix.GetField("LoadGhost", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null,
            new Func<object, object>(a => { ghostLoads++; return (a as string) == "pillar-ghost-guid" ? addressGhost : null; }));
        infFix.GetMethod("Apply").Invoke(null, new object[] { harmony, typeof(Program).Assembly });
        Exception slamAfter = null;
        try { Inferno.Skill_Misc.Hooks.WeaponSlam(slamOrigDel, null); Inferno.Skill_Misc.Hooks.WeaponSlam(slamOrigDel, null); } catch (Exception e) { slamAfter = e; }
        Check("inferno: slam no longer crashes, orig runs every time", slamAfter == null && slamOrig == 2);
        Check("inferno: ghost from its address resized to 4x, pillar 4x", addressGhost.transform.localScale.x == 4f
            && Inferno.Skill_Misc.Hooks.pillarProjectilePrefab.transform.localScale.x == 4f);
        Check("inferno: rest of Inferno's slam setup ran (hasFired reset, duration 3)", !Inferno.Skill_Misc.Hooks.hasFired && Inferno.Skill_Misc.Hooks.duration == 3f);
        Check("inferno: ghost loaded once, then cached", ghostLoads == 1);
        Check("inferno: the game's ghost field stays untouched", pc.ghostPrefab == null);
        Exception cleanupErr = null;
        try { Inferno.Skill_Misc.Hooks.CleanupPillar(slamOrigDel, null); } catch (Exception e) { cleanupErr = e; }
        Check("inferno: CleanupPillar puts pillar and ghost back to 1x, orig runs", cleanupErr == null && slamOrig == 3
            && addressGhost.transform.localScale.x == 1f && Inferno.Skill_Misc.Hooks.pillarProjectilePrefab.transform.localScale.x == 1f);
        var fieldGhost = new InfernoStandIn.FakeGhost();
        pc.ghostPrefab = fieldGhost;
        Inferno.Skill_Misc.Hooks.WeaponSlam(slamOrigDel, null);
        Check("inferno: a filled ghostPrefab field is still used as before", fieldGhost.transform.localScale.x == 4f && ghostLoads == 1 && slamOrig == 4);
        Inferno.Skill_Misc.Hooks.pillarProjectilePrefab.controller = new RoR2.Projectile.ProjectileController();   // no field, no address
        Exception noGhostErr = null;
        try { Inferno.Skill_Misc.Hooks.WeaponSlam(slamOrigDel, null); } catch (Exception e) { noGhostErr = e; }
        Check("inferno: no ghost anywhere: only its resize is skipped, orig runs", noGhostErr == null && slamOrig == 5);
        Inferno.Skill_Misc.Hooks.pillarProjectilePrefab.controller = null;   // GetComponent finds no controller
        Exception noCtlErr = null;
        try { Inferno.Skill_Misc.Hooks.WeaponSlam(slamOrigDel, null); } catch (Exception e) { noCtlErr = e; }
        Check("inferno: no controller on the pillar: no crash, orig runs", noCtlErr == null && slamOrig == 6);
        var keepPillar = Inferno.Skill_Misc.Hooks.pillarProjectilePrefab;
        Inferno.Skill_Misc.Hooks.pillarProjectilePrefab = null;   // a different crash before orig (pillar.transform)
        Exception otherCrash = null;
        try { Inferno.Skill_Misc.Hooks.WeaponSlam(slamOrigDel, null); Inferno.Skill_Misc.Hooks.CleanupPillar(slamOrigDel, null); } catch (Exception e) { otherCrash = e; }
        Inferno.Skill_Misc.Hooks.pillarProjectilePrefab = keepPillar;
        Check("inferno: any other crash before orig: orig still runs", otherCrash == null && slamOrig == 8);
        int throwingCalls = 0;
        On.EntityStates.BrotherMonster.WeaponSlam.orig_OnEnter slamThrowingOrig = s => { throwingCalls++; throw new InvalidOperationException("a hook deeper in the chain"); };
        Exception fromOrig = null;
        try { Inferno.Skill_Misc.Hooks.WeaponSlam(slamThrowingOrig, null); } catch (Exception e) { fromOrig = e; }
        Check("inferno: a crash out of orig still escapes, orig not run twice", fromOrig is InvalidOperationException && throwingCalls == 1);
        Exception afterThrow = null;
        try { Inferno.Skill_Misc.Hooks.WeaponSlam(slamOrigDel, null); } catch (Exception e) { afterThrow = e; }
        Check("inferno: state unwound after that (next slam normal)", afterThrow == null && slamOrig == 9);

        // Run-end cleanup after the fix: one dash hook per Inferno run, none after it ends, intentional adds kept.
        int maxDash = 0;
        for (int run = 0; run < 3; run++) {
            Inferno.Skill_Misc.HookBehavior.ApplyHooks();
            maxDash = Math.Max(maxDash, On.EntityStates.BrotherMonster.SlideIntroState.attached.Count);
            Inferno.Skill_Misc.HookBehavior.UndoHooks();
        }
        Check("inferno: dash hook attached once per run, removed at run end", maxDash == 1 && On.EntityStates.BrotherMonster.SlideIntroState.attached.Count == 0);
        Exception nonInfernoEnd = null;
        try { Inferno.Skill_Misc.HookBehavior.UndoHooks(); } catch (Exception e) { nonInfernoEnd = e; }   // a non-Inferno run's end
        Check("inferno: run end with no dash hook attached is harmless", nonInfernoEnd == null && On.EntityStates.BrotherMonster.SlideIntroState.attached.Count == 0);
        Check("inferno: other cleanup lines unchanged (CleanupPillar added, FistSlam removed)",
            On.EntityStates.BrotherMonster.WeaponSlam.attached.Exists(d => d.Method.Name == "CleanupPillar")
            && !On.EntityStates.BrotherMonster.WeaponSlam.attached.Exists(d => d.Method.Name == "WeaponSlam")
            && On.EntityStates.BrotherMonster.FistSlam.attached.Count == 0);
        ClearHooks();
        for (int i = 0; i < 3; i++) Inferno.Skill_Misc.HookBehavior.UndoHooks();   // three non-Inferno run ends
        int pillarCopies = On.EntityStates.BrotherMonster.WeaponSlam.attached.Count(d => d.Method.Name == "CleanupPillar");
        int deathCopies = On.EntityStates.NullifierMonster.DeathState.attached.Count + On.EntityStates.VoidMegaCrab.DeathState.attached.Count;
        Inferno.Skill_Misc.HookBehavior.ApplyHooks();                                 // next Inferno run start
        Check("inferno: reset hooks never pile up: one copy each after 3 non-Inferno run ends, none once an Inferno run starts",
            pillarCopies == 1 && deathCopies == 2
            && !On.EntityStates.BrotherMonster.WeaponSlam.attached.Exists(d => d.Method.Name == "CleanupPillar")
            && On.EntityStates.BrotherMonster.WeaponSlam.attached.Exists(d => d.Method.Name == "WeaponSlam")
            && On.EntityStates.NullifierMonster.DeathState.attached.Count == 0 && On.EntityStates.VoidMegaCrab.DeathState.attached.Count == 0
            && On.EntityStates.BrotherMonster.SlideIntroState.attached.Count == 1);
        Inferno.Skill_Misc.HookBehavior.UndoHooks();
        Check("inferno: after that run ends: dash hook gone, one copy of each reset hook",
            On.EntityStates.BrotherMonster.SlideIntroState.attached.Count == 0
            && On.EntityStates.BrotherMonster.WeaponSlam.attached.Count == 1 && On.EntityStates.BrotherMonster.WeaponSlam.attached[0].Method.Name == "CleanupPillar"
            && On.EntityStates.NullifierMonster.DeathState.attached.Count == 1 && On.EntityStates.VoidMegaCrab.DeathState.attached.Count == 1);
        // A failed address load isn't retried every slam.
        var deadCtl = new RoR2.Projectile.ProjectileController { ghostPrefabAddress = "missing-guid" };
        int loadsBefore = ghostLoads;
        Inferno.Skill_Misc.Hooks.pillarProjectilePrefab.controller = deadCtl;
        Exception deadErr = null;
        try { Inferno.Skill_Misc.Hooks.WeaponSlam(slamOrigDel, null); Inferno.Skill_Misc.Hooks.WeaponSlam(slamOrigDel, null); } catch (Exception e) { deadErr = e; }
        Inferno.Skill_Misc.Hooks.pillarProjectilePrefab.controller = pc;
        Check("inferno: an address that gives no ghost is tried once, not on every slam", deadErr == null && ghostLoads == loadsBefore + 1 && slamOrig == 11);
        }

        Console.WriteLine("--- shield log ---");
        foreach (var l in logs) Console.WriteLine("  " + l);
        Check("stun: fix announced", logs.Exists(l => l.Contains("only just spawned no longer crash")));
        Check("stun: early fill noted once", logs.FindAll(l => l.Contains("duration modifiers early")).Count == 1);
        Check("stun: unmodified fallback noted once", logs.FindAll(l => l.Contains("used the unmodified duration for a body")).Count == 1);
        Check("stun: lookup failure logged once", logs.FindAll(l => l.Contains("couldn't look up a body's duration modifiers")).Count == 1);
        Check("stun: a game without the members is left alone", logs.Exists(l => l.Contains("Stun duration fix: expected game members not found")));
        Check("stun: never failed to apply", !logs.Exists(l => l.Contains("Stun duration fix failed to apply")));
        Check("logged the skipped hook", logs.Exists(l => l.Contains("Skipped an outdated hook") && l.Contains("SetCurrentLanguage")));
        Check("logged the skipped Paladin-style step by field name", logs.Exists(l => l.Contains("FakeAsset.Gather: b failed and was skipped")));
        Check("logged the removed-hook-type skip", logs.Exists(l => l.Contains("hook type no longer exists")));
        Check("logged the skipped item by name", logs.Exists(l => l.Contains("FakeMod: TimelostRum.SetupAttributes failed and was skipped")));
        Check("auto-config fix announced", logs.Exists(l => l.Contains("auto-config update now uses the new language API")));
        Check("auto-config failure logged only once", logs.FindAll(l => l.Contains("auto-config replacement failed")).Count == 1);
        Check("language: broken item's failure logged once", logs.FindAll(l => l.Contains("TinkersSatchel: BrokenText text refresh failed")).Count == 1);
        Check("language: missing mod reported as not applied", logs.Exists(l => l.Contains("TinkersSatchel language fix: expected members not found")));
        Check("limit: announced", logs.Exists(l => l.Contains("enemies capped at 80")));
        Check("limit: change logged once", logs.FindAll(l => l.Contains("enemy limit set to 80 (it was 40)")).Count == 1);
        Check("limit: warns while Lift Monster Limit is on", logs.FindAll(l => l.Contains("Lift Monster Limit is on")).Count == 1);
        Check("limit: skipped copies noted once", logs.FindAll(l => l.Contains("copies paused at 80")).Count == 1);
        Check("spawns: announced", logs.Exists(l => l.Contains("regular spawners earn 1.5x credits")));
        Check("spawns: boosted spawner logged with before/after", logs.Exists(l => l.Contains("CombatDirector credits 0.75x -> ")));
        Check("sound: stop fix announced", logs.Exists(l => l.Contains("stop events always go through now")));
        Check("sound: first let-through noted once", logs.FindAll(l => l.Contains("first stop event let through")).Count == 1);
        Check("assets: redirect announced", logs.Exists(l => l.Contains("moved game asset paths now load from their new location")));
        Check("assets: moves logged, capped at 25", logs.FindAll(l => l.Contains("Assets: 'RoR2/")).Count == 25);
        Check("genetics: guard applied (orig call found), caught crash noted once",
            logs.Exists(l => l.Contains("can no longer break the damage chain")) && logs.FindAll(l => l.Contains("Genetics test note")).Count == 1);
        Check("genetics/guard/gasoline: nothing passed through is logged by CompatShield (it is never caught)",
            !logs.Exists(l => l.Contains("passed through") && !l.Contains("Inferno:")));
        Check("inferno: an error from below its slam hook is logged with its real source (the finalizer rethrow restarts the trace)",
            logs.Exists(l => l.Contains("Inferno: an error from Mithrix's slam below Inferno's hook passed through Hooks.") && l.Contains("at ")));
        Check("guard: applied, and the caught crash noted once", logs.Exists(l => l.Contains("can no longer abort the event it handles"))
            && logs.FindAll(l => l.Contains("Strange Can test note")).Count == 1);
        Check("gasoline: fix announced and deferral noted once", logs.Exists(l => l.Contains("chain reactions now run one blast at a time"))
            && logs.FindAll(l => l.Contains("was run one blast at a time")).Count == 1);
        Check("gasoline: the throwing link logged with its full error", logs.Exists(l => l.Contains("a chained blast threw") && l.Contains("a damage hook failed mid-blast")));
        Check("zet: already-fixed version says so instead of warning", logs.Exists(l => l.Contains("already fixes the slotless-elite kill crash")));
        Check("relic: nested type found, guard announced, crash noted once", logs.Exists(l => l.Contains("SS2.Items.RelicOfForce+Behavior.OnDamageDealtServer can no longer abort"))
            && logs.FindAll(l => l.Contains("Relic test note")).Count == 1);
        Check("pti: announced once, no unexpected-IL warning", logs.FindAll(l => l.Contains("scrappers now put scrap straight into the player's inventory")).Count == 1
            && !logs.Exists(l => l.Contains("PrintToInventory scrapper fix: unexpected IL") || l.Contains("PrintToInventory scrapper fix failed")));
        Check("pti: a mod without the expected members is left alone", logs.Exists(l => l.Contains("PrintToInventory scrapper fix: expected members not found")));
        Check("sacrifice: announced once with the multiplier", logs.FindAll(l => l.Contains("Sacrifice: item drop chance per kill now x0.6")).Count == 1);
        Check("sacrifice: first scaled roll noted once (8% -> 4.8%)", logs.FindAll(l => l.Contains("Sacrifice: a kill rolled")).Count == 1
            && logs.Exists(l => l.Contains("a kill rolled 4.8% instead of 8% (shown once)")));
        Check("sacrifice: missing method reported as not applied", logs.Exists(l => l.Contains("Sacrifice drop chance: SacrificeArtifactManager.OnServerCharacterDeath not found")));
        Check("sacrifice: no unexpected-IL warning or apply failure from Apply", !logs.Exists(l => l.Contains("Sacrifice drop chance: unexpected IL") || l.Contains("Sacrifice drop chance tweak failed")));
        Check("flower: fix announced, no unexpected-IL or apply warning",
            logs.Exists(l => l.Contains("REX flower: an expiring flower now unroots every enemy"))
            && !logs.Exists(l => l.Contains("REX flower fix: unexpected IL") || l.Contains("REX flower fix: expected game members") || l.Contains("REX flower fix failed")));
        Check("flower: gone enemy noted once", logs.FindAll(l => l.Contains("skipped unrooting an enemy that was already gone (shown once)")).Count == 1);
        Check("flower: the other mod's throw logged with its error",
            logs.Exists(l => l.Contains("REX flower: unrooting one enemy threw") && l.Contains("another mod's RemoveBuff hook failed")));
        Check("scepter flower: announced, cleared owner noted once, nothing failed",
            logs.Exists(l => l.Contains("REX flowers keep working after their owner's body is gone"))
            && logs.FindAll(l => l.Contains("outlived its owner's body and keeps working (shown once)")).Count == 1
            && !logs.Exists(l => l.Contains("Ancient Scepter flower fix failed") || l.Contains("couldn't check a flower's owner")));
        Check("scepter flower: a mod without the hook is left alone", logs.Exists(l => l.Contains("Ancient Scepter flower fix: On_TreebotFlower2RootPulse or the flower's owner field not found")));
        Check("scepter flower: second apply noted", logs.Exists(l => l.Contains("Ancient Scepter flower fix: already applied, skipped a second apply")));
        Check("inferno: both slam hooks and the dash fix announced, no IL warnings",
            logs.FindAll(l => l.Contains("now finds the pillar ghost at its new address")).Count == 2
            && logs.Exists(l => l.Contains("run-end cleanup now removes its Mithrix dash"))
            && !logs.Exists(l => l.Contains("Inferno Mithrix fix:")));
        Check("inferno: ghost load and missing ghost each noted once", logs.FindAll(l => l.Contains("pillar ghost loaded from its new address")).Count == 1
            && logs.FindAll(l => l.Contains("pillar ghost not found")).Count == 1);
        Check("inferno: crash fallback noted once per hook", logs.FindAll(l => l.Contains("crashed before Mithrix's slam started")).Count == 2);
        Check("inferno: reset-hook dedupe announced for all 3", logs.Exists(l => l.Contains("keeps at most one copy of its 3 reset hooks")));
        Check("zet: fix announced, no unexpected-IL warning",
            logs.Exists(l => l.Contains("no longer crash its drop hook")) && !logs.Exists(l => l.Contains("ZetAspects drop fix: unexpected IL")));
        return failures;
    }

    // Stand-in for HealthComponent in the chain-guard Harmony test: nests into itself DepthWanted deep.
    public class FakeHealth {
        public static readonly System.Collections.Generic.List<string> Calls = new System.Collections.Generic.List<string>();
        public static int DepthWanted;
        public int Id;
        public void TakeDamage(object damageInfo) {
            int d = (int)damageInfo;
            Calls.Add(Id + ":" + d);
            if (d < DepthWanted) TakeDamage(d + 1);
        }
    }

    class Listener : ILogListener {
        readonly System.Collections.Generic.List<string> sink;
        public Listener(System.Collections.Generic.List<string> s) { sink = s; }
        public void LogEvent(object sender, LogEventArgs e) { sink.Add($"[{e.Level}:{e.Source.SourceName}] {e.Data}"); }
        public void Dispose() { }
    }
}
