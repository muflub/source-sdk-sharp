using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// An entity's physics init, managed (plan_physics_bones_native_zero, InitPhysics
/// to managed). Every fact is read off native's CStandaloneEntity::InitPhysics
/// and its spawn-time caller at 4602cf071 (shell_standalone_base.cpp
/// 2103-2107, 2323-2476), which are stock's VPhysicsInitNormal /
/// VPhysicsInitStatic over PhysModelCreate / PhysModelCreateUnmoveable.
/// </summary>
public class EntityPhysicsCreationTests
{
    internal static readonly EntityHandle Crate = new(0x0002_0005u);

    /// <summary>vphysics and the world's model reads: one model, one solid block, every call logged in order.</summary>
    internal sealed class FakeVPhysics : IPhysicsWorldVPhysics, IVPhysicsEntityCalls
    {
        public readonly List<string> Calls = [];
        public nint Env = 0x1000;
        public VCollideView Model = new(0x2000, 1, 0x2100);
        public nint SolidCollide = 0x2200;
        public bool HasSolidBlock = true;
        public EntitySolid Parsed = new(0, "metal", ObjectParams.Defaults(0x3000) with { Mass = 40f });
        public nint NextObject = 0x4000;
        public readonly Dictionary<nint, ushort> Flags = new();
        public readonly List<(Vector Origin, QAngle Angles, ObjectParams Params, bool IsStatic, int Surface, nint Collide)> Made = [];
        public readonly Dictionary<string, nint> Texts = new();
        private bool _finished;
        private bool _copied;

        public nint ScratchSolid => 0x7000;

        // ---- IPhysicsWorldVPhysics: what ModelSolidParse and the init read ----

        public nint Environment() => Env;

        public VCollideView Collide(int modelIndex) => modelIndex == 9 ? Model : default;

        public nint CollideSolid(in VCollideView collide, int index) => index == Parsed.Index ? SolidCollide : 0;

        public int SurfaceIndex(string name)
        {
            Calls.Add($"surface {name}");
            return 31;
        }

        public nint NativeText(string text)
        {
            if (!Texts.TryGetValue(text, out nint p))
                Texts[text] = p = 0x9000 + Texts.Count;
            return p;
        }

        public nint KeyParserCreate(nint keyValues)
        {
            _finished = !HasSolidBlock;
            return 0x77;
        }

        public void KeyParserDestroy(nint parser) { }

        public bool Finished(nint parser) => _finished;

        public string BlockName(nint parser) => "solid";

        public ParsedSolid ParseSolid(nint parser)
        {
            _finished = true;
            return new ParsedSolid(Parsed.Index, 1, Parsed.Params);
        }

        public void CopyParsedSolid(nint solid) => _copied = solid == ScratchSolid;

        public void ResetSolid(nint solid) => _copied = false;

        public void SetSolidEnableCollisions(nint solid, bool enable) { }

        public nint CreatePolyObjectStatic(nint collide, int surfaceIndex, in ObjectParams objectParams) => throw new NotSupportedException();

        public void MarkNeverDeleted(nint physicsObject) => throw new NotSupportedException();

        public void SetContents(nint physicsObject, uint contents) => throw new NotSupportedException();

        public nint CreateFluidController(nint waterObject, nint gameData) => throw new NotSupportedException();

        public ParsedFluid ParseFluid(nint parser) => throw new NotSupportedException();

        public void ParseSurfaceTable(nint parser, Span<int> table) => throw new NotSupportedException();

        public void SetWorldMaterialIndexTable(ReadOnlySpan<int> table) => throw new NotSupportedException();

        public void SkipBlock(nint parser) => throw new NotSupportedException();

        public bool SupportsVirtualMesh() => throw new NotSupportedException();

        public nint VirtualTerrainCollide(int index) => throw new NotSupportedException();

        public Vector SolidMassCenter;

        public Vector CollideMassCenter(nint solid) => SolidMassCenter;

        public void CreateStaticPropPhysics(nint environment) => throw new NotSupportedException();

        // ---- IVPhysicsEntityCalls -------------------------------------------

        public EntitySolid ReadSolid(nint solid)
            => solid == ScratchSolid && _copied ? Parsed : new EntitySolid(0, string.Empty, default);

        /// <summary>The override written into the scratch solid, and where it was answered to be.</summary>
        public Vector? WrittenMassCenter;
        public const nint MassCenterSlot = 0x5540;

        public nint WriteMassCenterOverride(nint solid, Vector local)
        {
            WrittenMassCenter = local;
            return MassCenterSlot;
        }

        public nint CreatePolyObject(nint environment, nint collide, int surfaceIndex, Vector origin, QAngle angles,
                                     in ObjectParams objectParams, bool isStatic)
        {
            nint made = NextObject;
            Made.Add((origin, angles, objectParams, isStatic, surfaceIndex, collide));
            Calls.Add(isStatic ? "create static" : "create");
            if (made != 0)
                Flags[made] = 0x0010;
            return made;
        }

        public List<(float Radius, int Material, Vector Origin, ObjectParams Params)> Spheres { get; } = new();

        public nint CreateSphereObject(nint environment, float radius, int materialIndex, Vector origin, QAngle angles,
                                       in ObjectParams objectParams, bool isStatic)
        {
            Spheres.Add((radius, materialIndex, origin, objectParams));
            Calls.Add("create sphere");
            return NextObject;
        }

        public void DestroyObject(nint environment, nint physicsObject) => Calls.Add($"destroy {physicsObject:x}");

        public ushort GameFlags(nint physicsObject) => Flags.TryGetValue(physicsObject, out ushort f) ? f : (ushort)0;

        public void SetGameFlags(nint physicsObject, ushort flags)
        {
            Flags[physicsObject] = flags;
            Calls.Add($"flags {flags:x}");
        }

        public void Sleep(nint physicsObject) => Calls.Add("sleep");

        public void Wake(nint physicsObject) => Calls.Add("wake");

        public readonly HashSet<nint> Static = [];

        public bool IsStatic(nint physicsObject) => Static.Contains(physicsObject);

        public void RemoveShadowController(nint physicsObject) => Calls.Add($"unshadow {physicsObject:x}");

        public void SetGameData(nint physicsObject, nint gameData) => Calls.Add($"gamedata {physicsObject:x} {gameData:x}");

        public void EnableMotion(nint physicsObject, bool enable) => Calls.Add($"motion {enable}");

        public void EnableGravity(nint physicsObject, bool enable) => Calls.Add($"gravity {enable}");

        public void RecheckCollisionFilter(nint physicsObject) => Calls.Add("recheck");

        public void EnableCollisions(nint physicsObject, bool enable) => Calls.Add($"collisions {physicsObject:x} {enable}");

        public void SetShadow(nint physicsObject, float maxSpeed, float maxAngularSpeed, bool allowPhysicsMovement, bool allowPhysicsRotation)
        {
            Calls.Add($"shadow {physicsObject:x} {maxSpeed} {maxAngularSpeed} {allowPhysicsMovement} {allowPhysicsRotation}");
            Shadowed.Add(physicsObject);
        }

        public readonly List<(nint Object, Vector Position, QAngle Angles, float Dt)> ShadowTargets = [];

        public void UpdateShadow(nint physicsObject, Vector position, QAngle angles, bool tempDisableGravity, float timeOffset)
            => ShadowTargets.Add((physicsObject, position, angles, timeOffset));

        public readonly HashSet<nint> Shadowed = [];

        public bool HasShadowController(nint physicsObject) => Shadowed.Contains(physicsObject);

        public void Teleport(nint physicsObject, Vector position, QAngle angles) => Calls.Add($"teleport {physicsObject:x} {position}");
    }

    /// <summary>The shell and its managed class as a test sets them.</summary>
    internal sealed class FakeHost(FakeVPhysics log) : IEntityPhysicsHost
    {
        public bool Exists = true;
        public EntityPhysicsFacts Facts = new(Crate, 9, new Vector(1, 2, 3), new QAngle(10, 20, 30),
                                              SolidFlags.NotSolid | SolidFlags.Trigger, "prop_physics");
        public nint Held;
        public bool SwapRefused;
        public bool SolidityFirst = true;
        public readonly List<string> Warnings = [];

        public bool TryFacts(EntityHandle entity, out EntityPhysicsFacts facts)
        {
            facts = Facts;
            return Exists;
        }

        public string ModelName(EntityHandle entity) => "models/crate.mdl";

        public nint PhysicsObject(EntityHandle entity) => Held;

        public void SetSolidity(EntityHandle entity, SolidType solidType, SolidFlags solidFlags)
            => log.Calls.Add($"solidity {solidType} {solidFlags}");

        public bool Swap(EntityHandle entity, nint physicsObject, out nint previous)
        {
            previous = 0;
            if (SwapRefused)
                return false;
            previous = Held;
            Held = physicsObject;
            log.Calls.Add($"hold {physicsObject:x}");
            return true;
        }

        public void DestroyPhysics(EntityHandle entity)
        {
            log.Calls.Add("destroy held");
            Held = 0;
        }

        public void Created(EntityHandle entity) => log.Calls.Add("created");

        public readonly List<MoveType> MoveTypes = [];

        public void SetMoveType(EntityHandle entity, MoveType moveType) => MoveTypes.Add(moveType);

        public bool SolidityBeforePhysics => SolidityFirst;

        public void Warning(string text) => Warnings.Add(text);

        public string? MassCenterName;

        public string? MassCenterOverrideName(EntityHandle entity) => MassCenterName;
    }

    private static (EntityPhysicsCreation Create, FakeVPhysics V, FakeHost Host) Make()
        => Make(new MassCenterOverrides());

    private static (EntityPhysicsCreation Create, FakeVPhysics V, FakeHost Host) Make(MassCenterOverrides massCenters)
    {
        var v = new FakeVPhysics();
        var host = new FakeHost(v);
        return (new EntityPhysicsCreation(v, v, host, massCenters), v, host);
    }

    // prop_sphere: CPhysSphere::CreateVPhysics (props.cpp) builds a real
    // twelve-unit sphere, SOLID_BBOX, from g_PhysDefaultObjectParams. Here it
    // used the model's hull, and ss_sandbox's ball rested 10.4 below stock's.
    [Fact]
    public void A_sphere_is_a_ball_of_its_radius_at_the_entitys_pose()
    {
        var (c, v, host) = Make();

        Assert.True(c.CreateSphere(Crate, 12f));

        var made = Assert.Single(v.Spheres);
        Assert.Equal((12f, 0, new Vector(1, 2, 3)), (made.Radius, made.Material, made.Origin));
        Assert.Equal(ObjectParams.Defaults(0).Mass, made.Params.Mass);
        Assert.Equal(PhysicsGameData.FromHandle(Crate), made.Params.GameData);
        Assert.Equal([MoveType.VPhysics], host.MoveTypes);

        int solidity = v.Calls.IndexOf(v.Calls.First(l => l.StartsWith("solidity BoundingBox")));
        Assert.True(solidity >= 0 && solidity < v.Calls.IndexOf("create sphere"),
                    $"SOLID_BBOX before the object: {string.Join(", ", v.Calls)}");
        Assert.Contains("created", v.Calls);
    }

    // A mover's shadow (CBaseEntity::VPhysicsInitShadow). Found by stock-ab's
    // solidity census (2026-10-26): stock's doors, buttons, plats, trains and
    // func_brushes hold a shadow and ours held nothing, so props passed
    // through every one of them.
    [Fact]
    public void A_shadow_is_a_moveable_object_set_to_follow_with_stocks_limits()
    {
        var (c, v, host) = Make();

        Assert.True(c.CreateShadow(Crate, false, false));

        Assert.False(Assert.Single(v.Made).IsStatic);
        Assert.Contains("shadow 4000 2000 3600 False False", v.Calls);
        Assert.Equal([(host.Held, new Vector(1, 2, 3), new QAngle(10, 20, 30), 0f)], v.ShadowTargets);
        Assert.DoesNotContain(v.Calls, l => l.StartsWith("solidity"));
        Assert.Empty(host.MoveTypes);
        Assert.Equal(2, c.PhysicsKind(Crate));
    }

    [Fact]
    public void Each_frame_the_shadow_is_sent_where_its_entity_now_is()
    {
        var (c, v, host) = Make();
        c.CreateShadow(Crate, false, false);
        v.ShadowTargets.Clear();
        host.Facts = host.Facts with { Origin = new Vector(1, 2, 13) };

        Assert.Equal(1, c.UpdateShadows(0.015f));

        Assert.Equal([(host.Held, new Vector(1, 2, 13), new QAngle(10, 20, 30), 0.015f)], v.ShadowTargets);
        Assert.DoesNotContain(v.Calls, l => l.StartsWith("teleport"));
    }

    [Fact]
    public void A_jump_further_than_a_shadow_can_travel_in_a_frame_is_a_teleport()
    {
        var (c, v, host) = Make();
        c.CreateShadow(Crate, false, false);
        v.ShadowTargets.Clear();
        host.Facts = host.Facts with { Origin = new Vector(1, 2, 3 + 40f) };

        c.UpdateShadows(0.015f);

        Assert.Contains(v.Calls, l => l.StartsWith("teleport 4000"));
        Assert.Equal(0f, Assert.Single(v.ShadowTargets).Dt);
    }

    [Fact]
    public void A_shadow_whose_object_has_gone_is_forgotten()
    {
        var (c, v, host) = Make();
        c.CreateShadow(Crate, false, false);
        host.Held = 0;

        Assert.Equal(0, c.UpdateShadows(0.015f));
        host.Held = 0x4000;
        Assert.Equal(0, c.UpdateShadows(0.015f));
    }

    [Fact]
    public void An_entity_that_already_has_an_object_is_given_no_sphere()
    {
        var (c, v, host) = Make();
        host.Held = 0x55;

        Assert.True(c.CreateSphere(Crate, 12f));
        Assert.Empty(v.Spheres);
    }

    // A brush's VPhysicsInitStatic (CFuncWall::CreateVPhysics): the object is
    // static and the brush stays SOLID_BSP with its MOVETYPE_PUSH. Here the
    // static brushes had no object, and ss_sandbox's belt cans sank into the
    // conveyor they stand on (z 10.1 against stock's 19.4).
    [Fact]
    public void A_brush_static_object_keeps_the_brushs_solidity_and_movetype()
    {
        var (c, v, host) = Make();

        Assert.True(c.CreateStatic(Crate));

        var made = Assert.Single(v.Made);
        Assert.True(made.IsStatic);
        Assert.DoesNotContain(v.Calls, l => l.StartsWith("solidity"));
        Assert.Empty(host.MoveTypes);
        Assert.Contains("created", v.Calls);
    }

    [Fact]
    public void Collisions_switch_on_the_held_object_and_nothing_without_one()
    {
        var (c, v, host) = Make();

        c.EnableCollisions(Crate, false);
        Assert.DoesNotContain(v.Calls, l => l.StartsWith("collisions"));

        host.Held = 0x55;
        c.EnableCollisions(Crate, false);
        c.EnableCollisions(Crate, true);
        Assert.Equal(["collisions 55 False", "collisions 55 True"], v.Calls.Where(l => l.StartsWith("collisions")));
    }

    [Fact]
    public void A_class_that_consults_the_overrides_is_made_with_its_mass_centre()
    {
        var list = new MassCenterOverrides();
        list.Add(MassCenterOverride.SnapToAxis("door", new Vector(0f, 0f, 0f), new Vector(0f, 0f, 1f)));
        var (c, v, host) = Make(list);
        host.Facts = host.Facts with { Origin = Vector.Zero, Angles = default };
        host.MassCenterName = "door";
        v.SolidMassCenter = new Vector(3f, 4f, 5f);

        Assert.True(c.Create(Crate, isStatic: false, asleep: false, debris: false));

        Assert.Equal(FakeVPhysics.MassCenterSlot, v.Made.Single().Params.MassCenterOverride);
        Assert.Equal(new Vector(0f, 0f, 5f), v.WrittenMassCenter);
    }

    [Fact]
    public void A_class_that_does_not_leaves_the_override_for_one_that_does()
    {
        var list = new MassCenterOverrides();
        list.Add(MassCenterOverride.SnapToPoint("door", new Vector(1f, 0f, 0f)));
        var (c, v, host) = Make(list);
        host.MassCenterName = null;

        Assert.True(c.Create(Crate, isStatic: false, asleep: false, debris: false));

        Assert.Null(v.WrittenMassCenter);
        Assert.Equal(1, list.Count);
    }

    [Fact]
    public void A_static_object_never_consults_the_overrides()
    {
        var list = new MassCenterOverrides();
        list.Add(MassCenterOverride.SnapToPoint("door", new Vector(1f, 0f, 0f)));
        var (c, v, host) = Make(list);
        host.MassCenterName = "door";

        Assert.True(c.Create(Crate, isStatic: true, asleep: false, debris: false));

        Assert.Null(v.WrittenMassCenter);
        Assert.Equal(1, list.Count);
    }

    [Fact]
    public void AMoveableObjectMakesTheEntityVPhysics()
    {
        // VPhysicsInitNormal: SetMoveType( MOVETYPE_VPHYSICS ).
        var (c, _, host) = Make();

        Assert.True(c.Create(Crate, isStatic: false, asleep: false, debris: false));
        Assert.Equal([MoveType.VPhysics], host.MoveTypes);
    }

    [Fact]
    public void AStaticObjectLeavesTheEntityMoveTypeNone()
    {
        // VPhysicsInitStatic leaves the entity's own movetype: a solid
        // prop_dynamic stays MOVETYPE_NONE beside its static object.
        var (c, _, host) = Make();

        Assert.True(c.Create(Crate, isStatic: true, asleep: false, debris: false));
        Assert.Equal([MoveType.None], host.MoveTypes);
    }

    [Fact]
    public void AnEntityThatAlreadyHasAnObjectIsAnsweredYesAndGivenNoSecond()
    {
        var (c, v, host) = Make();
        host.Held = 0x55;

        Assert.True(c.Create(Crate, isStatic: false, asleep: false, debris: false));
        Assert.Empty(v.Made);
        Assert.Empty(v.Calls);
    }

    [Fact]
    public void NoModelIsRefusedAndSaidWithTheClassname()
    {
        var (c, v, host) = Make();
        host.Facts = host.Facts with { CollisionModelIndex = 0 };

        Assert.False(c.Create(Crate, false, false, false));
        Assert.Empty(v.Made);
        Assert.Equal("[SourceSharp] cannot simulate 'prop_physics': it has no model yet\n", Assert.Single(host.Warnings));
    }

    [Fact]
    public void NoEnvironmentIsRefusedAsNoModelYet()
    {
        var (c, v, host) = Make();
        v.Env = 0;

        Assert.False(c.Create(Crate, false, false, false));
        Assert.Empty(v.Made);
        Assert.Contains("it has no model yet", Assert.Single(host.Warnings));
    }

    [Fact]
    public void AModelWithNoCollisionDataIsRefusedAndNamed()
    {
        var (c, v, host) = Make();
        v.Model = v.Model with { SolidCount = 0 };

        Assert.False(c.Create(Crate, false, false, false));
        Assert.Empty(v.Made);
        Assert.Equal("[SourceSharp] cannot simulate 'prop_physics': model 'models/crate.mdl' has no collision data\n",
                     Assert.Single(host.Warnings));
    }

    [Fact]
    public void AModelWhoseKeysHoldNoSolidMakesNothing()
    {
        var (c, v, host) = Make();
        v.HasSolidBlock = false;

        Assert.False(c.Create(Crate, false, false, false));
        Assert.Empty(v.Made);
        Assert.Equal(0, host.Held);
    }

    [Fact]
    public void TheObjectCarriesTheParsedParamsTheHandleAsGameDataAndTheClassnameAsItsName()
    {
        var (c, v, _) = Make();

        Assert.True(c.Create(Crate, false, false, false));

        ObjectParams p = Assert.Single(v.Made).Params;
        Assert.Equal(40f, p.Mass);
        Assert.Equal(PhysicsGameData.FromHandle(Crate), p.GameData);
        Assert.Equal(Crate, PhysicsGameData.Owner(p.GameData));
        Assert.Equal(v.Texts["prop_physics"], p.Name);
    }

    [Fact]
    public void TheParsedSurfacepropIsLookedUpAndNoneIsMinusOne()
    {
        var (c, v, _) = Make();
        Assert.True(c.Create(Crate, false, false, false));
        Assert.Equal(31, v.Made[0].Surface);
        Assert.Contains("surface metal", v.Calls);

        var (c2, v2, _) = Make();
        v2.Parsed = v2.Parsed with { SurfaceProp = string.Empty };
        Assert.True(c2.Create(Crate, false, false, false));
        Assert.Equal(-1, v2.Made[0].Surface);
        Assert.DoesNotContain(v2.Calls, call => call.StartsWith("surface", StringComparison.Ordinal));
    }

    [Fact]
    public void TheObjectIsMadeFromTheParsedSolidAtTheEntitysPose()
    {
        var (c, v, _) = Make();

        Assert.True(c.Create(Crate, false, false, false));

        var made = Assert.Single(v.Made);
        Assert.Equal(v.SolidCollide, made.Collide);
        Assert.Equal(new Vector(1, 2, 3), made.Origin);
        Assert.Equal(new QAngle(10, 20, 30), made.Angles);
        Assert.False(made.IsStatic);
    }

    [Fact]
    public void StaticMakesAStaticObject()
    {
        var (c, v, _) = Make();

        Assert.True(c.Create(Crate, isStatic: true, false, false));

        Assert.True(Assert.Single(v.Made).IsStatic);
    }

    [Fact]
    public void StocksOrderSetsSolidityBeforeTheCreateWithNotSolidCleared()
    {
        var (c, v, _) = Make();

        Assert.True(c.Create(Crate, false, false, false));

        Assert.Equal(new[] { "surface metal", "solidity VPhysics Trigger", "create", "hold 4000", "wake", "created" }, v.Calls);
    }

    [Fact]
    public void TheOldOrderSetsSolidityAfterTheWake()
    {
        var (c, v, host) = Make();
        host.SolidityFirst = false;

        Assert.True(c.Create(Crate, false, false, false));

        Assert.Equal(new[] { "surface metal", "create", "hold 4000", "wake", "solidity VPhysics Trigger", "created" }, v.Calls);
    }

    [Fact]
    public void AFailedCreateAnswersNoAndHoldsNothingButLeavesTheSolidity()
    {
        var (c, v, host) = Make();
        v.NextObject = 0;

        Assert.False(c.Create(Crate, false, false, false));

        Assert.Equal(0, host.Held);
        Assert.Equal(new[] { "surface metal", "solidity VPhysics Trigger", "create" }, v.Calls);
    }

    [Fact]
    public void AsleepSleepsAndAwakeWakes()
    {
        var (c, v, _) = Make();
        Assert.True(c.Create(Crate, false, asleep: true, false));
        Assert.Contains("sleep", v.Calls);
        Assert.DoesNotContain("wake", v.Calls);
    }

    [Fact]
    public void DebrisAddsNoPlayerPickupToTheFlagsItHad()
    {
        var (c, v, _) = Make();

        Assert.True(c.Create(Crate, false, false, debris: true));

        Assert.Equal((ushort)(0x0010 | 0x0080), v.Flags[0x4000]);
    }

    [Fact]
    public void NotDebrisLeavesTheFlagsAlone()
    {
        var (c, v, _) = Make();

        Assert.True(c.Create(Crate, false, false, debris: false));

        Assert.Equal((ushort)0x0010, v.Flags[0x4000]);
        Assert.DoesNotContain(v.Calls, call => call.StartsWith("flags", StringComparison.Ordinal));
    }

    [Fact]
    public void AShellThatWillNotHoldTheObjectGetsItDestroyed()
    {
        var (c, v, host) = Make();
        host.SwapRefused = true;

        Assert.False(c.Create(Crate, false, false, false));

        Assert.Equal(new[] { "surface metal", "solidity VPhysics Trigger", "create", "destroy 4000" }, v.Calls);
    }

    [Fact]
    public void AnEntityWithNoHandleIsSimulatedWithNoOwnerAndSaysSo()
    {
        var (c, v, host) = Make();
        host.Facts = host.Facts with { Handle = EntityHandle.None };

        Assert.True(c.Create(Crate, false, false, false));

        Assert.Equal(0, v.Made[0].Params.GameData);
        Assert.Contains("before it has a handle", Assert.Single(host.Warnings));
    }

    [Fact]
    public void SetStaticDestroysTheHeldObjectAndMakesTheKindAsked()
    {
        var (c, v, host) = Make();
        host.Held = 0x55;

        Assert.True(c.SetStatic(Crate, isStatic: true));

        Assert.Equal("destroy held", v.Calls[0]);
        Assert.True(Assert.Single(v.Made).IsStatic);
        Assert.Equal(0x4000, host.Held);
    }

    [Fact]
    public void APropKindIsSimulatedAfterItsSpawn()
    {
        var (c, v, _) = Make();

        Assert.True(c.Spawned(Crate, ScriptedEntityKind.Prop));

        Assert.False(Assert.Single(v.Made).IsStatic);
        Assert.Contains("wake", v.Calls);
    }

    [Theory]
    [InlineData(ScriptedEntityKind.Brush)]
    [InlineData(ScriptedEntityKind.Item)]
    [InlineData(ScriptedEntityKind.Projectile)]
    [InlineData(ScriptedEntityKind.Effect)]
    [InlineData(ScriptedEntityKind.Player)]
    public void NoOtherKindIsSimulatedByItsSpawn(ScriptedEntityKind kind)
    {
        var (c, v, _) = Make();

        Assert.False(c.Spawned(Crate, kind));
        Assert.Empty(v.Made);
    }

    [Fact]
    public void APropWhoseSpawnMadeAnObjectIsLeftWithIt()
    {
        var (c, v, host) = Make();
        host.Held = 0x55;

        Assert.False(c.Spawned(Crate, ScriptedEntityKind.Prop));
        Assert.Empty(v.Made);
    }

    [Fact]
    public void TheRegistrysSpawnAsksForAPropKindsObjectAfterTheClassSpawns()
    {
        var (c, v, _) = Make();
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        registry.SpawnPhysics = c;

        registry.Spawn(Crate, EntityNames.SimplePhysicsProp);

        Assert.False(Assert.Single(v.Made).IsStatic);
    }

    // CDynamicProp::CreateVPhysics makes a static object or none, never a
    // simulated one: a SOLID_NONE prop_dynamic_override has no object at all.
    [Fact]
    public void TheRegistrysSpawnAsksNothingForAPropWhoseSpawnDecidesItsOwn()
    {
        var (c, v, _) = Make();
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropDynamic).Assembly);
        registry.Services = TestServices.Services(out var engine);
        engine.Strict = false;
        engine.Returns("Precache.PrecacheModel", 3)
              .Returns("EntityRender.GetModelKeyValueText", null);
        registry.SpawnPhysics = c;

        registry.KeyValue(Crate, EntityNames.PropDynamicOverride, "model", "models/props_c17/chair.mdl");
        registry.Spawn(Crate, EntityNames.PropDynamicOverride);

        Assert.Empty(v.Made);
        Assert.False(engine.Called("EntityPhysics.InitStaticPhysics"));
    }

    [Fact]
    public void TheRegistrysSpawnAsksNothingForAnotherKind()
    {
        var (c, v, _) = Make();
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        registry.SpawnPhysics = c;

        registry.Spawn(Crate, EntityNames.InfoTarget);

        Assert.Empty(v.Made);
    }

    [Fact]
    public void APropWithNoModelIsLeftSilently()
    {
        var (c, v, host) = Make();
        host.Facts = host.Facts with { CollisionModelIndex = 0 };

        Assert.False(c.Spawned(Crate, ScriptedEntityKind.Prop));
        Assert.Empty(v.Made);
        Assert.Empty(host.Warnings);
    }
}
