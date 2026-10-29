using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ragdolls;
using SourceSharp.Shared.Bones;
using Xunit;

namespace SourceSharp.Tests.Ragdolls;

/// <summary>
/// RagdollBoltPin, c_stickybolt.cpp's CRagdollBoltEnumerator: a bolt's line
/// back along its flight, traced into the bodies; the element the first one
/// hit doubled in mass and tied to the world's object by a ballsocket, the
/// world's end at the bolt and the element's where the line entered it; the
/// skewer sound from the world at the body.
/// </summary>
public class RagdollBoltPinTests
{
    private static readonly EntityHandle Corpse = new(0x0003_0009u);
    private static readonly EntityHandle Other = new(0x0004_000Au);

    private const nint WorldObject = 0x9000;
    private static readonly Vector WorldAt = new(10f, 20f, 30f);

    private static readonly Vector Origin = new(100f, 0f, 50f);
    private static readonly Vector Flight = new(1f, 0f, 0f);

    private sealed class Bodies : IClientRagdolls
    {
        public readonly Dictionary<EntityHandle, nint[]> Elements = [];
        public readonly Dictionary<EntityHandle, Vector> Origins = [];

        public int Count => Elements.Count;
        public IReadOnlyCollection<EntityHandle> Owners => Elements.Keys;

        public nint ElementObject(EntityHandle owner, int physicsBone) =>
            Elements.TryGetValue(owner, out nint[]? list) && physicsBone >= 0 && physicsBone < list.Length
                ? list[physicsBone]
                : 0;

        public RagdollState State(EntityHandle owner, out Vector origin)
        {
            bool known = Origins.TryGetValue(owner, out origin);
            return known ? RagdollState.Present : RagdollState.None;
        }

        public bool Become(EntityHandle owner, Vector force, int forceBone, out RagdollRefusal refusal) =>
            throw new NotSupportedException();
        public bool IsRagdoll(EntityHandle owner) => Elements.ContainsKey(owner);
        public uint Serial(EntityHandle owner) => 0;
        public bool ApplyForceCenter(EntityHandle owner, Vector force) => false;
        public int WriteBones(EntityHandle owner, Span<BoneMatrix3x4> bones, Span<int> owned) => -1;
        public bool Destroy(EntityHandle owner) => false;
        public int DestroyAll() => 0;
        public int StepPoses() => 0;
    }

    private sealed class Hitboxes : IRagdollHitboxTrace
    {
        public readonly Dictionary<EntityHandle, RagdollHitboxHit> Answers = [];
        public readonly List<(EntityHandle Entity, Vector Start, Vector End)> Asked = [];

        public bool ClipRayToEntity(EntityHandle entity, Vector start, Vector end, out RagdollHitboxHit hit)
        {
            Asked.Add((entity, start, end));
            if (Answers.TryGetValue(entity, out hit))
                return true;
            hit = default;
            return false;
        }
    }

    private sealed class WorldObjectSource : IClientWorldPhysObject
    {
        public nint World { get; set; } = WorldObject;
    }

    private sealed class Sounds : IFxGameSounds
    {
        public readonly List<(string Script, Vector At)> Played = [];

        public bool Parameters(string script, out GameSoundParams parameters)
        {
            parameters = default;
            return false;
        }

        public void EmitAtPosition(in GameSoundParams parameters, float volume, int pitch, in Vector origin)
            => Played.Add(("wave " + parameters.Wave, origin));

        public void EmitScript(string script, in Vector origin) => Played.Add((script, origin));
    }

    private sealed record Rig(RagdollBoltPin Pin, FakeRagdollVPhysics Fake, Bodies Bodies, Hitboxes Hitboxes, WorldObjectSource World,
                              Sounds Sounds, nint[] Elements);

    /// <summary>One corpse of three elements, the line entering element 1 a quarter of the way back.</summary>
    private static Rig Make()
    {
        var fake = new FakeRagdollVPhysics();
        fake.Objects[WorldObject] = Placed(WorldObject, WorldAt, 0f);

        nint[] elements = [0x2000, 0x2010, 0x2020];
        for (int i = 0; i < elements.Length; i++)
            fake.Objects[elements[i]] = Placed(elements[i], new Vector(70f + i, 0f, 50f), 6f + i);

        var bodies = new Bodies();
        bodies.Elements[Corpse] = elements;
        bodies.Origins[Corpse] = new Vector(68f, 1f, 40f);

        var hitboxes = new Hitboxes();
        hitboxes.Answers[Corpse] = new RagdollHitboxHit(0.25f, new Vector(68f, 0f, 50f), 1);

        var world = new WorldObjectSource();
        var sounds = new Sounds();
        var pin = new RagdollBoltPin(bodies, hitboxes, fake, world, sounds);
        return new Rig(pin, fake, bodies, hitboxes, world, sounds, elements);
    }

    private static FakeRagdollVPhysics.FakeObject Placed(nint id, Vector at, float mass)
    {
        var o = new FakeRagdollVPhysics.FakeObject { Id = id, Mass = mass };
        o.Matrix[0, 3] = at.X;
        o.Matrix[1, 3] = at.Y;
        o.Matrix[2, 3] = at.Z;
        return o;
    }

    private static bool Same(Vector a, Vector b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    private static List<FakeRagdollVPhysics.FakeConstraint> Ballsockets(Rig rig) =>
        rig.Fake.Constraints.Values.Where(c => c.Ballsocket).ToList();

    [Fact]
    public void TheLineRunsFromTheBoltOneHundredTwentyEightUnitsBackAlongTheFlight()
    {
        Rig rig = Make();
        rig.Pin.Stick(Origin, Flight);

        Assert.True(rig.Hitboxes.Asked.Count == 1, $"the one body traced: {rig.Hitboxes.Asked.Count}");
        (EntityHandle entity, Vector start, Vector end) = rig.Hitboxes.Asked[0];
        Assert.True(entity == Corpse, "the corpse's hitboxes");
        Assert.True(Same(start, Origin), $"from the bolt: {start}");
        Assert.True(Same(end, new Vector(-28f, 0f, 50f)), $"to 128 units back: {end}");
    }

    [Fact]
    public void TheElementTheLineHitIsTiedToTheWorldByABallsocket()
    {
        Rig rig = Make();
        Assert.True(rig.Pin.Stick(Origin, Flight), "a body was pinned");

        List<FakeRagdollVPhysics.FakeConstraint> pins = Ballsockets(rig);
        Assert.True(pins.Count == 1, $"one ballsocket: {pins.Count}");
        Assert.True(pins[0].Reference == WorldObject, "the world's object is the reference");
        Assert.True(pins[0].Attached == rig.Elements[1], "the element physicsbone 1 names is attached");
        Assert.True(pins[0].Group == 0, "in no constraint group");
    }

    [Fact]
    public void TheWorldsEndIsAtTheBoltAndTheElementsWhereTheLineEnteredIt()
    {
        Rig rig = Make();
        rig.Pin.Stick(Origin, Flight);

        ConstraintBallsocketParams p = Ballsockets(rig)[0].BallsocketParams;
        // The fake's WorldToLocal subtracts the object's position.
        Assert.True(Same(p.ConstraintPosition0, Origin - WorldAt), $"the bolt, in the world object's space: {p.ConstraintPosition0}");
        Assert.True(Same(p.ConstraintPosition1, new Vector(68f, 0f, 50f) - new Vector(71f, 0f, 50f)),
                    $"the entry point, in the element's space: {p.ConstraintPosition1}");
    }

    [Fact]
    public void TheBallsocketTakesStocksDefaults()
    {
        Rig rig = Make();
        rig.Pin.Stick(Origin, Flight);

        ConstraintBreakableParams want = ConstraintBallsocketParams.Defaults().Constraint;
        ConstraintBreakableParams got = Ballsockets(rig)[0].BallsocketParams.Constraint;
        Assert.True(got.Strength == want.Strength && got.ForceLimit == want.ForceLimit && got.TorqueLimit == want.TorqueLimit
                    && got.BodyMassScale0 == want.BodyMassScale0 && got.BodyMassScale1 == want.BodyMassScale1
                    && got.IsActive == want.IsActive,
                    "constraint_ballsocketparams_t::Defaults()");
        Assert.True(want.IsActive != 0 && want.BodyMassScale0 == 1f, "and the defaults are not all zero");
    }

    [Fact]
    public void ThePinnedElementIsMadeTwiceAsHeavy()
    {
        Rig rig = Make();
        rig.Pin.Stick(Origin, Flight);

        Assert.True(rig.Fake.Objects[rig.Elements[1]].Mass == 14f, $"7 kg doubled: {rig.Fake.Objects[rig.Elements[1]].Mass}");
        Assert.True(rig.Fake.Objects[rig.Elements[0]].Mass == 6f && rig.Fake.Objects[rig.Elements[2]].Mass == 8f,
                    "the other elements keep theirs");
        Assert.True(rig.Fake.Objects[WorldObject].Mass == 0f, "and the world's is untouched");
    }

    [Fact]
    public void ThePinPlaysTheSkewerFromTheWorldAtTheBody()
    {
        Rig rig = Make();
        rig.Pin.Stick(Origin, Flight);

        Assert.True(rig.Sounds.Played.Count == 1, $"played once: {rig.Sounds.Played.Count}");
        Assert.True(rig.Sounds.Played[0].Script == "Weapon_Crossbow.BoltSkewer",
                    $"the script by name, so its own volume and pitch play (no SND_CHANGE_* flag): {rig.Sounds.Played[0].Script}");
        Assert.True(Same(rig.Sounds.Played[0].At, new Vector(68f, 1f, 40f)), $"at the body's origin: {rig.Sounds.Played[0].At}");
    }

    [Fact]
    public void ALineThatMissesTheBodyPinsNothing()
    {
        Rig rig = Make();
        rig.Hitboxes.Answers[Corpse] = new RagdollHitboxHit(1f, new Vector(-28f, 0f, 50f), 1);

        Assert.False(rig.Pin.Stick(Origin, Flight));
        Assert.True(Ballsockets(rig).Count == 0, "no ballsocket");
        Assert.True(rig.Fake.Objects[rig.Elements[1]].Mass == 7f, "no mass changed");
        Assert.True(rig.Sounds.Played.Count == 0, "no sound");
    }

    [Fact]
    public void AHitOnNoPhysicsBonePinsNothing()
    {
        Rig rig = Make();
        rig.Hitboxes.Answers[Corpse] = new RagdollHitboxHit(0.25f, new Vector(68f, 0f, 50f), -1);
        Assert.False(rig.Pin.Stick(Origin, Flight), "physicsbone -1");

        rig.Hitboxes.Answers[Corpse] = new RagdollHitboxHit(0.25f, new Vector(68f, 0f, 50f), 3);
        Assert.False(rig.Pin.Stick(Origin, Flight), "physicsbone past listCount");
        Assert.True(Ballsockets(rig).Count == 0, "no ballsocket");
    }

    [Fact]
    public void ABodyWithNoHitboxesIsPassedOver()
    {
        Rig rig = Make();
        rig.Hitboxes.Answers.Remove(Corpse);

        Assert.False(rig.Pin.Stick(Origin, Flight));
        Assert.True(Ballsockets(rig).Count == 0, "no ballsocket");
    }

    [Fact]
    public void WithNoWorldObjectNothingIsPinned()
    {
        Rig rig = Make();
        rig.World.World = 0;

        Assert.False(rig.Pin.Stick(Origin, Flight));
        Assert.True(Ballsockets(rig).Count == 0, "no ballsocket");
        Assert.True(rig.Fake.Objects[rig.Elements[1]].Mass == 7f, "no mass changed");
        Assert.True(rig.Sounds.Played.Count == 0, "no sound");
    }

    [Fact]
    public void OnlyOneBodyIsPinnedTheOneTheLineEntersFirst()
    {
        Rig rig = Make();
        nint other = 0x3000;
        rig.Fake.Objects[other] = Placed(other, new Vector(90f, 0f, 50f), 5f);
        rig.Bodies.Elements[Other] = [other];
        rig.Bodies.Origins[Other] = new Vector(90f, 0f, 40f);
        rig.Hitboxes.Answers[Other] = new RagdollHitboxHit(0.05f, new Vector(93.6f, 0f, 50f), 0);

        Assert.True(rig.Pin.Stick(Origin, Flight));

        List<FakeRagdollVPhysics.FakeConstraint> pins = Ballsockets(rig);
        Assert.True(pins.Count == 1, $"one ballsocket for two bodies on the line: {pins.Count}");
        Assert.True(pins[0].Attached == other, "the nearer body's element");
        Assert.True(rig.Fake.Objects[rig.Elements[1]].Mass == 7f, "the farther body is untouched");
        Assert.True(rig.Sounds.Played.Count == 1, "one sound");
    }

    [Fact]
    public void WithNoBodiesNothingIsTraced()
    {
        Rig rig = Make();
        rig.Bodies.Elements.Clear();

        Assert.False(rig.Pin.Stick(Origin, Flight));
        Assert.True(rig.Hitboxes.Asked.Count == 0, "no trace");
    }

    [Fact]
    public void APinIsRememberedWithTheElementsEndForTheReport()
    {
        Rig rig = Make();
        rig.Pin.Stick(Origin, Flight);

        Assert.True(rig.Pin.Pins == 1 && rig.Pin.Made.Count == 1, $"one pin: {rig.Pin.Pins}/{rig.Pin.Made.Count}");
        BoltPinRecord pin = rig.Pin.Made[0];
        Assert.True(pin.Owner == Corpse && pin.PhysicsBone == 1 && pin.Element == rig.Elements[1], "the body and its element");
        Assert.True(Same(pin.BoltPoint, Origin) && Same(pin.EntryPoint, new Vector(68f, 0f, 50f)), "the bolt and the entry point");
        Assert.True(pin.Constraint == Ballsockets(rig)[0].Id, "and the ballsocket made");

        Assert.True(rig.Pin.AttachedWorld(pin, out Vector at), "the element is still the body's");
        Assert.True(Same(at, new Vector(68f, 0f, 50f)), $"the element's end is where the line entered it: {at}");
    }

    [Fact]
    public void APinWhoseBodyWentIsNotReadBack()
    {
        Rig rig = Make();
        rig.Pin.Stick(Origin, Flight);
        rig.Bodies.Elements.Remove(Corpse);

        Assert.False(rig.Pin.AttachedWorld(rig.Pin.Made[0], out _), "the body went: its element is not read");
    }
}
