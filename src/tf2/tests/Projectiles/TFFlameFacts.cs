using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server;
using TF2.Server.Combat;
using TF2.Server.Player;
using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;
using TF2.Tests.Buildings;
using TF2.Tests.Combat;
using TF2.Tests.Entities.Items;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Projectiles;

/// <summary>A condition set the facts hold: what was added, what was removed, what is active.</summary>
internal sealed class FakeConditions : ITFPlayerConditions
{
    public List<(EntityHandle Player, TFCond Cond, float Duration)> Added { get; } = new();
    public List<(EntityHandle Player, TFCond Cond)> Removed { get; } = new();
    private readonly HashSet<(EntityHandle, TFCond)> _active = new();

    public TFConditions? Of(int playerIndex) => null;

    public TFCondAdd Add(EntityHandle player, TFCond cond, float duration = TFConditions.Permanent, EntityHandle provider = default)
    {
        Added.Add((player, cond, duration));
        _active.Add((player, cond));
        return TFCondAdd.Added;
    }

    public bool Remove(EntityHandle player, TFCond cond, bool ignoreDuration = false)
    {
        Removed.Add((player, cond));
        return _active.Remove((player, cond));
    }

    public TFCondMask RemoveAll(EntityHandle player)
    {
        _active.Clear();
        return default;
    }

    public bool InCond(EntityHandle player, TFCond cond) => _active.Contains((player, cond));

    /// <summary>Plants a condition the seam did not.</summary>
    public void Grant(EntityHandle player, TFCond cond) => _active.Add((player, cond));
}

/// <summary>The flame's world: the recording engine, the real scopes, one flame.</summary>
internal sealed class FlameRig
{
    public static readonly EntityHandle Player = new(0x0001_0000u | 3);
    public static readonly EntityHandle Victim = new(0x0001_0000u | 5);
    public static readonly EntityHandle ManagerHandle = new(0x0006_0000u | 30);
    public static readonly EntityHandle RocketHandle = new(0x0003_0000u | 20);
    public static readonly EntityHandle PipebombHandle = new(0x0003_0000u | 21);
    public static readonly EntityHandle StickyHandle = new(0x0003_0000u | 22);

    public RecordingEngine Engine { get; } = RecordingEngine.Default();
    public RecordingLog Log { get; } = new();
    public TFDamageScopes Scopes { get; } = new();
    public ScopedDamage Damage { get; }
    public DamageHitGroups HitGroups { get; } = new();
    public FakeConditions Conditions { get; } = new();
    public ProjectileRig.DirectoryView Directory { get; } = new();
    public FakeOwners Owners { get; } = new();

    /// <summary>The carrier registry the old arm's stun asks, and the typed convars over it.</summary>
    public TFPlayerCarriers Carriers { get; } = new();

    /// <summary>The players the stun stack's condition asks read.</summary>
    public readonly FakeMapPlayers StunPlayers = new();

    public ConVarRegistry Convars { get; } = new(new StoredConVarBackend());

    /// <summary>The victim's stun stack, attached: the trigger facts' rig shape.</summary>
    public TFStunStack AttachStun(EntityHandle player)
    {
        StunPlayers.All[player] = new FakeMapPlayer();
        var stack = new TFStunStack(Carriers, StunPlayers, Engine);
        stack.Attach(player);
        return stack;
    }

    public TFFlameEffects Effects { get; }
    public TFRocket Rocket { get; }
    public TFPipebomb Pipebomb { get; }
    public TFStickybomb Stickybomb { get; }
    public RecordingServerSkins Skins { get; } = new();

    /// <summary>The real mover, over the double — a launch asks the engine nothing.</summary>
    public EntityMotion Motion { get; }

    public FlameRig()
    {
        Damage = new ScopedDamage(Scopes);
        Convars.RegisterAll(typeof(TF2.Server.Medigun.TFPlayerHealing).Assembly);
        Effects = new TFFlameEffects(Engine, Engine, Directory, Engine, Engine, Conditions, Owners, Log,
                                     Convars.Get<TFAirBlastCrayConVar>()!,
                                     Convars.Get<TFFlameThrowerBurstZvelocityConVar>()!,
                                     Convars.Get<TFPushbackScaleConVar>()!,
                                     Convars.Get<TFPushbackScaleVerticalConVar>()!, Carriers);

        var motionLog = new RecordingLog();
        var motionDirectory = new EntityDirectory(motionLog);
        Motion = new EntityMotion(motionLog, Engine, Engine, Engine, Engine, Engine, motionDirectory,
            new PhysicsPushedEntities(new EnginePushWorld(Engine, Engine, Engine, motionDirectory), motionLog));
        var blast = new TFBlastApplier(Engine, Engine, Engine, Scopes, HitGroups, new DamageInflictors(),
                                       new ProjectileRig.FixedClasses(), Log,
                                       Engine, Owners, new TF2.Shared.Econ.AttributeHooks(),
                                       new ProjectileRig.Resupply(), Engine, Engine, new ProjectileRig.DirectoryView());
        Rocket = new TFRocket(Engine, Motion, blast, Owners, Engine, new TF2.Shared.Econ.AttributeHooks(),
                              new TFPlayerCarriers(), new RecordingEffectDispatch(), Engine);
        Rocket.BindAreas(TestServices.Areas(Engine));
        Rocket.Handle = RocketHandle;
        // The trail writes need bound storage, as the registry gives.
        Rocket.NetStates = NetStateBinding.AttachLocal(Rocket, Rocket.Handle,
                                                       new NetStateFields(new RecordingLog()));

        Pipebomb = new TFPipebomb(Engine, blast, Engine, Directory, Owners,
                                  new TF2.Shared.Econ.AttributeHooks(), Engine, Skins, Engine, Engine);
        Pipebomb.BindAreas(TestServices.Areas(Engine));
        Pipebomb.Handle = PipebombHandle;
        Pipebomb.NetStates = NetStateBinding.AttachLocal(Pipebomb, Pipebomb.Handle,
                                                         new NetStateFields(new RecordingLog()));

        Stickybomb = new TFStickybomb(Engine, blast, Engine, Engine, Engine, Engine, Directory, Engine);
        Stickybomb.BindAreas(TestServices.Areas(Engine));
        Stickybomb.Handle = StickyHandle;
        Stickybomb.RegisteredKind = ScriptedEntityKind.Prop; // TakesDamage, as the registration says
        Stickybomb.NetStates = NetStateBinding.AttachLocal(Stickybomb, StickyHandle,
                                                           new NetStateFields(new RecordingLog()));

        Engine.Returns("Entities.Create", ManagerHandle);
        Engine.Returns("Players.GetHandle", Player);
        Engine.Returns("Entities.GetEyeAngles", new Vector(0f, 0f, 0f));
        Engine.Returns("Entities.GetEyePosition", new Vector(0f, 0f, 64f));
        Engine.Returns("Entities.GetVelocity", default(Vector));
        Engine.Returns("Entities.GetWorldSpaceCenter", default(Vector));
        Engine.Returns("Entities.GetOrigin", default(Vector));
        Engine.Returns("Entities.IsPlayer", false);
        Engine.Returns("Entities.IsValid", true);
        Engine.Returns("Entities.GetIndex", 1);
        Engine.Returns("Players.GetTeam", 2);
        // The airblast's own weapon: the deflection retargets the rocket's
        // launcher to it (stock SetLauncher(GetActiveWeapon)); a fact's
        // Returns turns it.
        Engine.Returns("Players.GetActiveWeapon", "tf_flamethrower");
        // A target in the air unless a fact grounds them.
        Engine.Returns("Networking.EntityGetState", EntityState.None);
        Engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);
        // The sphere query answers its count; a fact's FillsSphere decides what is IN it.
        Engine.Returns("WorldQuery.EntitiesInSphere", 0);
        // A clear path, unless a fact says otherwise.
        Engine.Returns("WorldQuery.TraceLine", new TraceResult(false, 1f, default, default, EntityHandle.None));
        // Dry air: the Burn's water death reads the contents at the origin.
        Engine.Returns("WorldQuery.PointContents", default(Contents));
        Engine.Returns("Host.CurrentTime", 0f);
        // The life jitter's midpoint and the velocity spread's upper end, so a
        // fact can name the number it expects; a fact re-scripts what it varies.
        Engine.Returns("Host.RandomFloat", 1f);
    }

    /// <summary>A clear path.</summary>
    public static TraceResult Clear { get; } = new(false, 1f, default, default, EntityHandle.None);

    /// <summary>A path stopped part-way by <paramref name="who"/>.</summary>
    public static TraceResult Hit(EntityHandle who, Vector at) => new(true, 0.5f, at, default, who);
}


