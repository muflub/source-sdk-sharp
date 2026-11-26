using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Combat;
using TF2.Server.Player;
using TF2.Server.Projectiles;
using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;

using TF2.Tests.Projectiles;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The stickbomb's kaboom (<c>CTFStickBomb::Smack</c>'s tail,
/// <c>shared/tf/tf_weapon_bottle.cpp:219-283</c>) through the melee arm: the
/// connected smack's blast beside the hit, its once-per-word gate, the swing's
/// crit and the item's two multipliers.
/// </summary>
public sealed class TFStickbombFacts
{
    /// <summary>The kaboom's bits, stock's <c>dmgType</c> (:257) before the crit's.</summary>
    private static readonly DamageType KaboomBits
        = DamageType.Blast | TFDamageBits.UseDistanceMod | TFDamageBits.Melee;

    /// <summary>
    /// The melee arm over the real blast applier, both over one recording
    /// engine: the hull hits the victim, whose middle stands under the blast
    /// the lift raised a unit (<see cref="TFBlastApplier"/> GroundLift), so
    /// the blast scores it at zero distance and pays whole.
    /// </summary>
    private sealed class KaboomRig
    {
        public static readonly EntityHandle Victim = MeleeRig.Victim;

        public RecordingEngine Engine { get; } = RecordingEngine.Default();
        public RecordingLog Log { get; } = new();
        public DamageHitGroups HitGroups { get; } = new();
        public TFDamageScopes Scopes { get; } = new();
        public DamageInflictors Inflictors { get; } = new();
        public ProjectileRig.FixedClasses Classes { get; } = new();
        public ProjectileRig.FixedOwners Owners { get; } = new();
        public ProjectileRig.Resupply Health { get; } = new();
        public ProjectileRig.DirectoryView Directory { get; } = new();
        public TFPlayerCarriers Carriers { get; } = new();
        public MeleeDamage Damage { get; }
        public TFBlastApplier Blast { get; }
        public TFMeleeEffects Effects { get; }

        /// <summary>The held stickbomb, whose word carries the spent bit.</summary>
        public WeaponNetState Held { get; } = new();

        /// <summary>The body the swing meets: an enemy unless the fact says else.</summary>
        public int VictimTeam { get; set; } = (int)TFTeam.Blue;

        public KaboomRig(IEngineEntityRender? render = null, IEntityDirectory? directory = null)
        {
            Held.BindLocal();
            Engine.ReturnsEachCall("Players.GetHandle",
                                   () => Engine.Calls[^1].Args[0] is int slot && slot == 1 ? MeleeRig.Owner : default(EntityHandle));
            Engine.Returns("Players.GetShootOrigin", new Vector(0f, 0f, 64f));
            Engine.Returns("Entities.GetEyeAngles", new Vector(0f, 0f, 0f));
            Engine.Returns("Entities.IsPlayer", true);
            Engine.Returns("Players.IsAlive", true);
            Engine.Returns("Entities.GetBounds", (new Vector(-12f, -12f, 0f), new Vector(12f, 12f, 72f)));
            Engine.ReturnsEachCall("Damage.TakeDamageAnswered",
                () => Engine.Calls[^1].Args[2] is float d && d > 0f ? 1 : 0);
            Engine.Returns("Networking.EntityGetState", EntityState.None);
            Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);
            Engine.Returns("Entities.GetIndex", 1);
            Engine.Returns("Entities.IsValid", true);
            Engine.Returns("Entities.GetHealth", 100);
            Engine.Returns("Entities.GetWorldSpaceCenter", new Vector(0f, 0f, 65f));
            Engine.Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f));
            Engine.ReturnsEachCall("Players.GetTeam",
                () => Engine.Calls[^1].Args[0] is EntityHandle h && h == Victim ? VictimTeam : (int)TFTeam.Red);
            Engine.ReturnsEachCall("WorldQuery.TraceLine",
                                   () => new TraceResult(false, 1f, default, default, EntityHandle.None));
            Engine.ReturnsEachCall("WorldQuery.TraceBox", () => MeleeRig.HullOn(Victim));
            Engine.Returns("WorldQuery.EntitiesInSphere", 1);
            Engine.FillsSphere(Victim);

            Damage = new MeleeDamage(HitGroups);
            Blast = new(Engine, Engine, Engine, Scopes, HitGroups, Inflictors, Classes, Log,
                        Engine, Owners, new(), Health, Engine, Engine, Directory, Carriers,
                        DeclaredConVars.Fresh<TF2.Server.TFPreroundPushFromDamageEnableConVar>(), new TF2.Tests.Rules.ListHooks<TF2.Server.Projectiles.ITFSelfBlastHook>());
            Effects = MeleeDeps.Effects(Engine, Damage, HitGroups, Log, owners: Owners,
                                        blast: Blast, health: Health,
                                        render: render, directory: directory);
        }

        /// <summary>One smack of the held stickbomb, the script's 55 unless the shot says else.</summary>
        public void Smack(ShotDamage? shot = null)
            => Effects.Melee(Attack(), WeaponFixtures.ParseExtra("tf_weapon_stickbomb"),
                             shot ?? new ShotDamage(55f),
                             new SwingTrace(TFWeaponTypes.SwingRange, TFMeleeBehaviour.SwingHull, false));

        /// <summary>The same attack each smack, so the spent bit is the held word's.</summary>
        private WeaponAttackContext Attack() => new(
            new WeaponType { Name = "tf_weapon_stickbomb", MeleeRange = TFWeaponTypes.SwingRange, MeleeDamage = 55f },
            Held, default!, 1f, true, new RecordingArms(), new WeaponCommand(1, 5, 3));
    }

    [Fact]
    public void A_teammates_shoulder_leaves_the_caber_its_blast()
    {
        // ConnectedHit is an enemy or the world (tf_weaponbase_melee.cpp:959-966).
        var rig = new KaboomRig { VictimTeam = (int)TFTeam.Red };

        rig.Smack();

        Assert.False(TFMeleeBehaviour.Spent(rig.Held));
    }

    private static List<EngineCall> KaboomsOf(KaboomRig rig)
        => rig.Engine.Calls
             .Where(c => c.Member is "Damage.TakeDamage" or "Damage.TakeDamageAnswered"
                         && c.Args[3] is DamageType t && (t & DamageType.Blast) != 0)
             .ToList();

    private static List<MeleeHit> SmacksOf(KaboomRig rig)
        => rig.Damage.Hits.Where(h => (h.Type & DamageType.Blast) == 0).ToList();

    [Fact]
    public void TheConnectedSmackDetonatesTheKaboomBesideTheHit()
    {
        // Smack's order (tf_weapon_bottle.cpp:221-271): the base's melee hit
        // first, then the blast — 75 whole at the centre, the bits above, the
        // victim the hit, and the word holding the spent bit out.
        var rig = new KaboomRig();

        rig.Smack();

        MeleeHit smack = Assert.Single(SmacksOf(rig));
        Assert.Equal(55f, smack.Damage, 3);
        Assert.Equal(KaboomRig.Victim, smack.Victim);

        EngineCall kaboom = Assert.Single(KaboomsOf(rig));
        Assert.Equal(75f, (float)kaboom.Args[2]!, 3);
        Assert.Equal(KaboomBits, (DamageType)kaboom.Args[3]!);
        Assert.Equal(KaboomRig.Victim, (EntityHandle)kaboom.Args[0]!);

        Assert.True(TFMeleeBehaviour.Spent(rig.Held));
    }

    [Fact]
    public void TheKaboomWaitsForTheWordTheDeployResets()
    {
        // m_iDetonated's gate (:224): the second swing of a spent stickbomb is
        // plain melee, until WeaponReset's clear — the deploy's
        // ResetBehaviourState — arms the kaboom again (:276-283).
        var rig = new KaboomRig();
        rig.Smack();
        rig.Smack();
        Assert.Empty(KaboomsOf(rig).Skip(1));

        rig.Held.ResetBehaviourState();
        rig.Smack();

        Assert.Equal(2, KaboomsOf(rig).Count);
        Assert.Equal(3, SmacksOf(rig).Count);
    }

    [Fact]
    public void TheKaboomCarriesTheSwingsCrit()
    {
        // dmgType's DMG_CRITICAL arm (:258-259): the swing's crit — the flag
        // the hit groups push reads, the multiplier the damage rules' — rides
        // the blast too.
        var rig = new KaboomRig();
        bool critical = false;
        rig.Engine.WhenCalled("Damage.TakeDamageAnswered", _ => critical = rig.HitGroups.Critical);

        rig.Smack(TFWeaponBehaviour.Critical(55f));

        Assert.Single(KaboomsOf(rig));
        Assert.True(critical);
    }

    [Fact]
    public void TheKaboomTakesTheItemsDamageAndRadius()
    {
        // The two CALL_ATTRIB_HOOK_FLOATs (:262, :267): mult_dmg over the 75
        // and mult_explosion_radius over the 100, the launcher item's.
        var rig = new KaboomRig();
        rig.Owners.Item = Item(("mult_dmg", AttributeDescriptionFormat.Percentage, 2f),
                               ("mult_explosion_radius", AttributeDescriptionFormat.Percentage, 1.5f));

        rig.Smack();

        Assert.Equal(150f, (float)Assert.Single(KaboomsOf(rig)).Args[2]!, 3);
    }

    /// <summary>
    /// Stock's <c>SwitchBodyGroups</c> (:226-228, :294-309): the detonation
    /// repaints the one attachment riding the swing owner's hands with the
    /// exploded caber, once per deploy — the spent bit's gate (:224) gives
    /// the plain second swing no second repaint.
    /// </summary>
    [Fact]
    public void TheDetonationBreaksTheBodygroupsOnTheAttachment()
    {
        var attached = new SsViewmodelAttachment { Handle = new EntityHandle(0x0001_0000u | 9) };
        attached.State.BindLocal();
        attached.State.OwnerIndex = 3;   // the rig's command carries owner index 3
        var rig = new KaboomRig(directory: new CaberDirectory(attached));

        rig.Smack();

        EngineCall painted = Assert.Single(rig.Engine.AllOf("EntityRender.SetModel"));
        Assert.Equal(attached.Handle, (EntityHandle)painted.Args[0]!);
        Assert.Equal(TFMeleeEffects.ExplodedCaber, painted.Args[1] as string);

        rig.Smack();
        Assert.Single(rig.Engine.AllOf("EntityRender.SetModel"));
    }

    /// <summary>A directory holding the one attachment riding the swing's owner.</summary>
    private sealed class CaberDirectory : IEntityDirectory
    {
        private readonly SsViewmodelAttachment _attached;

        public CaberDirectory(SsViewmodelAttachment attached) => _attached = attached;

        ScriptedEntity? IEntityDirectory.Get(EntityHandle handle)
            => handle == _attached.Handle ? _attached : null;
        public ScriptedEntity? FindByName(string name) => null;
        public IEnumerable<ScriptedEntity> OfClass(string className)
            => className == EntityNames.SsViewmodelAttachment ? [_attached] : [];
        public bool IsRegistered(string className) => className == EntityNames.SsViewmodelAttachment;
        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    /// <summary>An item carrying the named float attributes, as the rig's items read.</summary>
    private static IAttributeProvider Item(params (string Hook, AttributeDescriptionFormat Format, float Value)[] attributes)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        int defindex = 7100;
        foreach (var (hook, format, value) in attributes)
        {
            container.Set(AttributeValue.OfFloat(
                new AttributeDefinition(defindex++, "attr " + defindex, hook, format,
                                        AttributeEffectType.Neutral, false, false,
                                        AttributeValueType.Default, MergedKeys.Block("x")), value));
        }

        return container;
    }
}
