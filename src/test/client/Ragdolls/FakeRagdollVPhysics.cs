using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Tests.Ragdolls;

/// <summary>
/// A vphysics a ragdoll can be built in without the engine: objects, groups,
/// collision sets and a scripted key parser, each recording what was asked of
/// it. Pointers are small numbers, never zero.
/// </summary>
public sealed class FakeRagdollVPhysics : IRagdollVPhysics
{
    public sealed class FakeObject
    {
        public nint Id;
        public nint Collide;
        public int SurfaceIndex;
        public RagdollObjectOverrides Overrides;
        public string SolidName = "";
        public Float3x4 Matrix = Float3x4.Identity;
        public bool LastTeleport;
        public float Mass = 1f;
        public ushort GameIndex;
        public ushort GameFlags;
        public bool CollisionsEnabled;
        public bool Asleep = true;
        public int Wakes;
        public int Sleeps;
        public Vector Velocity;
        public Vector AngularVelocity;
        public Vector AddedVelocity;
        public Vector AddedAngular;
        public Vector ForceCenter;
        public List<(Vector Force, Vector At)> ForceOffsets = [];
        public bool Destroyed;
        public List<(Vector Normal, nint Other)> Contacts = [];
        public int FrictionCleared;
        public int FrictionRecomputed;
        public (Vector Mins, Vector Maxs) Aabb;

        public Vector Position => new(Matrix[0, 3], Matrix[1, 3], Matrix[2, 3]);
    }

    public sealed class FakeConstraint
    {
        public nint Id;
        public nint Reference;
        public nint Attached;
        public nint Group;
        public bool Fixed;
        public ConstraintRagdollParams Ragdoll;
        public ConstraintFixedParams FixedParams;
        public bool Destroyed;
    }

    public sealed class FakeGroup
    {
        public nint Id;
        public ConstraintGroupParams Params;
        public int Activations;
        public bool ErrorState;
        public int ErrorCleared;
        public bool Destroyed;
    }

    public sealed class FakeSet
    {
        public nint Id;
        public int ModelIndex;
        public int MaxElements;
        public List<(int, int)> Enabled = [];
        public List<(int, int)> Disabled = [];
    }

    /// <summary>One block of a .phy's key text, as the parser walks it.</summary>
    public abstract record Block(string Name);

    public sealed record SolidBlock(int Index, string SolidName, string SurfaceProp) : Block("solid");

    public sealed record ConstraintBlock(ConstraintRagdollParams Params) : Block("ragdollconstraint");

    public sealed record CustomBlock(string BlockName, (string Key, string Value)[] Keys) : Block(BlockName);

    private nint _next = 0x1000;
    private nint Next() => _next += 0x10;

    public nint Env = 0x77;
    public List<Block> Blocks = [];
    public int SolidCount = -1;
    public readonly Dictionary<nint, FakeObject> Objects = [];
    public readonly Dictionary<nint, FakeConstraint> Constraints = [];
    public readonly Dictionary<nint, FakeGroup> Groups = [];
    public readonly Dictionary<int, FakeSet> Sets = [];
    public readonly Dictionary<string, int> Surfaces = new(StringComparer.OrdinalIgnoreCase) { ["default"] = 0 };
    public int ParsersCreated;
    public int ParsersDestroyed;
    public int SolidsReleased;
    public readonly List<nint> Active = [];
    public (Vector Mins, Vector Maxs)? Bounds;

    private sealed class Parser(List<Block> blocks)
    {
        public readonly List<Block> Blocks = blocks;
        public int At;
    }

    private readonly Dictionary<nint, Parser> _parsers = [];
    private readonly Dictionary<nint, (FakeObject Owner, int At)> _snapshots = [];

    public FakeObject Object(nint id) => Objects[id];

    public IEnumerable<FakeObject> Live => Objects.Values.Where(o => !o.Destroyed);

    // ---- the level -----------------------------------------------------------

    public nint Environment() => Env;

    // ---- the model -----------------------------------------------------------

    public RagdollCollide Collide(int modelIndex)
        => new(0x500, SolidCount >= 0 ? SolidCount : Blocks.OfType<SolidBlock>().Count(), 0x600);

    public nint CollideSolid(in RagdollCollide collide, int index) => 0x5000 + index;

    public bool ModelBounds(int modelIndex, out Vector mins, out Vector maxs)
    {
        mins = Bounds?.Mins ?? Vector.Zero;
        maxs = Bounds?.Maxs ?? Vector.Zero;
        return Bounds is not null;
    }

    public int SurfaceIndex(string name) => Surfaces.TryGetValue(name, out int index) ? index : -1;

    // ---- the key parser ------------------------------------------------------

    public nint KeyParserCreate(nint keyValues)
    {
        nint id = Next();
        _parsers[id] = new Parser(Blocks);
        ParsersCreated++;
        return id;
    }

    public void KeyParserDestroy(nint parser)
    {
        _parsers.Remove(parser);
        ParsersDestroyed++;
    }

    public bool Finished(nint parser) => _parsers[parser].At >= _parsers[parser].Blocks.Count;

    public string BlockName(nint parser) => _parsers[parser].Blocks[_parsers[parser].At].Name;

    private Block Take(nint parser) => _parsers[parser].Blocks[_parsers[parser].At++];

    public RagdollSolid ParseSolid(nint parser)
    {
        var block = (SolidBlock)Take(parser);
        return new RagdollSolid(block.Index, block.SolidName, block.SurfaceProp, 0x900);
    }

    public void ReleaseSolid(in RagdollSolid solid) => SolidsReleased++;

    public ConstraintRagdollParams ParseRagdollConstraint(nint parser) => ((ConstraintBlock)Take(parser)).Params;

    public void ParseCustom(nint parser, IRagdollKeySink sink)
    {
        var block = (CustomBlock)Take(parser);
        foreach ((string key, string value) in block.Keys)
            sink.ParseKeyValue(key, value);
    }

    public void SkipBlock(nint parser) => Take(parser);

    // ---- collision sets ------------------------------------------------------

    public nint FindCollisionSet(int modelIndex) => Sets.TryGetValue(modelIndex, out FakeSet? set) ? set.Id : 0;

    public nint FindOrCreateCollisionSet(int modelIndex, int maxElementCount)
    {
        if (!Sets.TryGetValue(modelIndex, out FakeSet? set))
            Sets[modelIndex] = set = new FakeSet { Id = Next(), ModelIndex = modelIndex, MaxElements = maxElementCount };
        return set.Id;
    }

    public FakeSet SetById(nint id) => Sets.Values.Single(s => s.Id == id);

    public void EnableCollisions(nint collisionSet, int index0, int index1) => SetById(collisionSet).Enabled.Add((index0, index1));

    public void DisableCollisions(nint collisionSet, int index0, int index1) => SetById(collisionSet).Disabled.Add((index0, index1));

    // ---- creators ------------------------------------------------------------

    public nint CreateConstraintGroup(nint environment, in ConstraintGroupParams p)
    {
        nint id = Next();
        Groups[id] = new FakeGroup { Id = id, Params = p };
        return id;
    }

    public void DestroyConstraintGroup(nint environment, nint group)
    {
        if (Groups.TryGetValue(group, out FakeGroup? g))
            g.Destroyed = true;
    }

    public Func<RagdollSolid, float>? MassOf;

    public nint CreatePolyObject(nint environment, nint collide, int surfaceIndex, in RagdollSolid solid,
                                 in RagdollObjectOverrides overrides)
    {
        nint id = Next();
        Objects[id] = new FakeObject
        {
            Id = id, Collide = collide, SurfaceIndex = surfaceIndex, Overrides = overrides, SolidName = solid.Name,
            Mass = overrides.Mass ?? MassOf?.Invoke(solid) ?? 1f,
        };
        return id;
    }

    public nint CreateRagdollConstraint(nint environment, nint reference, nint attached, nint group,
                                        in ConstraintRagdollParams p)
    {
        nint id = Next();
        Constraints[id] = new FakeConstraint { Id = id, Reference = reference, Attached = attached, Group = group, Ragdoll = p };
        return id;
    }

    public nint CreateFixedConstraint(nint environment, nint reference, nint attached, nint group,
                                      in ConstraintFixedParams p)
    {
        nint id = Next();
        Constraints[id] = new FakeConstraint
        {
            Id = id, Reference = reference, Attached = attached, Group = group, Fixed = true, FixedParams = p,
        };
        return id;
    }

    public void DestroyConstraint(nint environment, nint constraint)
    {
        if (Constraints.TryGetValue(constraint, out FakeConstraint? c))
            c.Destroyed = true;
    }

    public void DestroyObject(nint environment, nint physicsObject)
    {
        if (Objects.TryGetValue(physicsObject, out FakeObject? o))
            o.Destroyed = true;
    }

    public int ActiveObjects(nint environment, ref nint[] buffer)
    {
        if (buffer.Length < Active.Count)
            buffer = new nint[Active.Count];
        Active.CopyTo(buffer);
        return Active.Count;
    }

    // ---- group ---------------------------------------------------------------

    public void GroupActivate(nint group)
    {
        Groups[group].Activations++;
        foreach (FakeObject o in Live)
            o.Asleep = false;
    }

    public bool GroupIsInErrorState(nint group) => Groups[group].ErrorState;

    public void GroupClearErrorState(nint group)
    {
        Groups[group].ErrorCleared++;
        Groups[group].ErrorState = false;
    }

    // ---- objects -------------------------------------------------------------

    public float Mass(nint physicsObject) => Objects[physicsObject].Mass;

    public Vector Position(nint physicsObject) => Objects[physicsObject].Position;

    public (Vector Position, QAngle Angles) PositionAngles(nint physicsObject)
        => (Objects[physicsObject].Position, QAngle.Zero);

    public Float3x4 PositionMatrix(nint physicsObject) => Objects[physicsObject].Matrix;

    public void SetPositionMatrix(nint physicsObject, in Float3x4 matrix, bool isTeleport)
    {
        Objects[physicsObject].Matrix = matrix;
        Objects[physicsObject].LastTeleport = isTeleport;
    }

    public void SetGameIndex(nint physicsObject, ushort index) => Objects[physicsObject].GameIndex = index;

    public ushort GameFlags(nint physicsObject) => Objects[physicsObject].GameFlags;

    public void SetGameFlags(nint physicsObject, ushort flags) => Objects[physicsObject].GameFlags = flags;

    public nint GameData(nint physicsObject)
        => Objects.TryGetValue(physicsObject, out FakeObject? o) ? o.Overrides.GameData : OtherGameData;

    /// <summary>The game data of an object this fake did not make (a contact's other object).</summary>
    public nint OtherGameData = 0x4242;

    public void EnableObjectCollisions(nint physicsObject, bool enable) => Objects[physicsObject].CollisionsEnabled = enable;

    public void Wake(nint physicsObject)
    {
        Objects[physicsObject].Asleep = false;
        Objects[physicsObject].Wakes++;
    }

    public void Sleep(nint physicsObject)
    {
        Objects[physicsObject].Asleep = true;
        Objects[physicsObject].Sleeps++;
    }

    public bool IsAsleep(nint physicsObject) => Objects[physicsObject].Asleep;

    public void ApplyForceCenter(nint physicsObject, Vector force) => Objects[physicsObject].ForceCenter += force;

    public void ApplyForceOffset(nint physicsObject, Vector force, Vector worldPosition)
        => Objects[physicsObject].ForceOffsets.Add((force, worldPosition));

    /// <summary>Translation only: the fake's objects never rotate.</summary>
    public Vector LocalToWorld(nint physicsObject, Vector local) => Objects[physicsObject].Position + local;

    public Vector LocalToWorldVector(nint physicsObject, Vector local) => local;

    /// <summary>A recognisable transform: the world vector doubled.</summary>
    public Vector WorldToLocalVector(nint physicsObject, Vector world) => world * 2f;

    public void AddVelocity(nint physicsObject, Vector linear, Vector angular)
    {
        Objects[physicsObject].AddedVelocity += linear;
        Objects[physicsObject].AddedAngular += angular;
    }

    public void SetVelocity(nint physicsObject, Vector linear, Vector angular)
    {
        Objects[physicsObject].Velocity = linear;
        Objects[physicsObject].AngularVelocity = angular;
    }

    public Vector VelocityAtPoint(nint physicsObject, Vector worldPosition) => Objects[physicsObject].Velocity;

    public nint ObjectCollide(nint physicsObject) => Objects[physicsObject].Collide;

    public (Vector Mins, Vector Maxs) CollideAabb(nint collide, Vector origin, QAngle angles)
    {
        FakeObject o = Objects.Values.First(x => x.Collide == collide && x.Position.Equals(origin));
        return (o.Aabb.Mins + origin, o.Aabb.Maxs + origin);
    }

    // ---- snapshots -----------------------------------------------------------

    public nint CreateFrictionSnapshot(nint physicsObject)
    {
        nint id = Next();
        _snapshots[id] = (Objects[physicsObject], 0);
        return id;
    }

    public void DestroyFrictionSnapshot(nint physicsObject, nint snapshot) => _snapshots.Remove(snapshot);

    public int SnapshotsOpen => _snapshots.Count;

    public bool SnapshotIsValid(nint snapshot) => _snapshots[snapshot].At < _snapshots[snapshot].Owner.Contacts.Count;

    public nint SnapshotObject(nint snapshot, int index)
        => index == 0 ? _snapshots[snapshot].Owner.Id : _snapshots[snapshot].Owner.Contacts[_snapshots[snapshot].At].Other;

    public Vector SnapshotSurfaceNormal(nint snapshot) => _snapshots[snapshot].Owner.Contacts[_snapshots[snapshot].At].Normal;

    public void SnapshotNext(nint snapshot) => _snapshots[snapshot] = (_snapshots[snapshot].Owner, _snapshots[snapshot].At + 1);

    public void SnapshotClearFrictionForce(nint snapshot) => _snapshots[snapshot].Owner.FrictionCleared++;

    public void SnapshotRecomputeFriction(nint snapshot) => _snapshots[snapshot].Owner.FrictionRecomputed++;
}
