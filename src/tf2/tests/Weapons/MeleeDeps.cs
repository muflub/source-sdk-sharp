using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Combat;
using TF2.Server.Player;
using TF2.Server.Projectiles;
using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;

namespace TF2.Tests.Weapons;

/// <summary>
/// The melee effects' dependencies, real and answering an idle game: the
/// effects take every dependency required, so a fact that means to drive one
/// hands its own holder; the rest come from here, the same for every suite.
/// The blast behind the default is the real applier over the same parts, its
/// engine reads the recording engine's.
/// </summary>
internal static class MeleeDeps
{
    /// <summary>The effects over <paramref name="engine"/>'s seams and <paramref name="damage"/>, every dependency a real holder.</summary>
    public static TFMeleeEffects Effects(RecordingEngine engine, IEngineDamage damage, DamageHitGroups hitGroups, ILog log,
                                         ITFWeaponOwners? owners = null, AttributeHooks? hooks = null,
                                         ILagCompensation? lag = null, TFDamageScopes? scopes = null,
                                         ITFPlayerConditions? conditions = null, TFBlastApplier? blast = null,
                                         ITFPlayerResupply? health = null, IEngineEntityRender? render = null,
                                         IEntityDirectory? directory = null)
    {
        var hold = owners ?? new NoTFWeaponOwners();
        var walk = hooks ?? new AttributeHooks();
        var supply = health ?? new IdleSupply();
        var world = directory ?? new NoDirectory();
        return new(engine, engine, engine, damage, hitGroups, log, hold, walk,
                   lag ?? new IdleLag(), scopes ?? new TFDamageScopes(), conditions ?? new NoConditions(),
                   blast ?? new TFBlastApplier(engine, damage, engine, new TFDamageScopes(), hitGroups,
                                               new DamageInflictors(), new NoClasses(), log, engine, hold, walk,
                                               supply, engine, engine, world, new TFPlayerCarriers(),
                                               DeclaredConVars.Fresh<TF2.Server.TFPreroundPushFromDamageEnableConVar>(), new TF2.Tests.Rules.ListHooks<TF2.Server.Projectiles.ITFSelfBlastHook>()),
                   supply, render ?? engine, world);
    }

    /// <summary>No classes: the blast's own class read, nobody any class.</summary>
    private sealed class NoClasses : TF2.Server.Econ.ITFPlayerClasses
    {
        public TFClass ClassOf(EntityHandle player) => TFClass.Undefined;
    }

    /// <summary>No lag compensation: the smack's bracket answers and moves nobody.</summary>
    private sealed class IdleLag : ILagCompensation
    {
        public void Start(EntityHandle player) { }

        public void Start(EntityHandle player, in UserCmd cmd) { }

        public void Finish(EntityHandle player) { }

        public bool IsCurrentlyDoingLagCompensation => false;

        public void NoteWeaponFired(EntityHandle player) { }
    }

    /// <summary>No resupply: a hundred-max health and nothing else, the arms' gate only.</summary>
    private sealed class IdleSupply : ITFPlayerResupply
    {
        public int MaxHealth(EntityHandle player) => 100;

        public void Regenerate(EntityHandle player, bool refillHealthAndAmmo = true) { }

        public int TakeHealth(EntityHandle player, float health, bool ignoreMaxHealth = false) => 0;

        public int MaxAmmo(EntityHandle player, TFAmmo ammo) => 0;

        public int GiveAmmo(EntityHandle player, int count, TFAmmo ammo) => 0;
    }

    /// <summary>No conditions: nobody holds one, nothing is added.</summary>
    private sealed class NoConditions : ITFPlayerConditions
    {
        public TFConditions? Of(int playerIndex) => null;

        public TFCondAdd Add(EntityHandle player, TFCond cond, float duration = TFConditions.Permanent,
                             EntityHandle provider = default) => default;

        public bool Remove(EntityHandle player, TFCond cond, bool ignoreDuration = false) => false;

        public TFCondMask RemoveAll(EntityHandle player) => default;

        public bool InCond(EntityHandle player, TFCond cond) => false;
    }

    /// <summary>An empty world: no entities of any classname, no instances.</summary>
    private sealed class NoDirectory : IEntityDirectory
    {
        public ScriptedEntity? Get(EntityHandle handle) => null;

        public ScriptedEntity? FindByName(string name) => null;

        public IEnumerable<ScriptedEntity> OfClass(string className) => [];

        public bool IsRegistered(string className) => false;

        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }
}
