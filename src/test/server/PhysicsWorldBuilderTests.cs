using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The world's physics build, a model's solid parse, the surface sound precache
/// and the slide direction (plan_physics_bones_native_zero 2.6), each against
/// stock's physics_shared.cpp: PhysCreateWorld_Shared, PhysCreateVirtualTerrain,
/// PhysModelParseSolidByIndex, PrecachePhysicsSounds and PhysComputeSlideDirection.
/// </summary>
public class PhysicsWorldBuilderTests
{
    /// <summary>One block of a fake .phy key text.</summary>
    private sealed record Block(string Name, int Index = 0, uint Contents = 1, float Mass = 1f, string SurfaceProp = "");

    private sealed class FakeWorld : IPhysicsWorldVPhysics
    {
        public nint Env = 0x1000;
        public readonly Dictionary<int, VCollideView> Collides = new();
        public readonly Dictionary<int, nint> Solids = new();          // index -> collide
        public readonly Dictionary<int, nint> Terrain = new();
        public readonly List<Block> Blocks = [];
        public bool VirtualMesh = true;
        public readonly List<string> Calls = [];
        public readonly List<(nint Collide, int Surface, ObjectParams Params, nint Made)> Made = [];
        public readonly Dictionary<nint, uint> Contents = new();
        public readonly HashSet<nint> NeverDeleted = [];
        public readonly List<(nint Water, nint GameData)> Fluids = [];
        public int[]? MaterialTable;
        public readonly Dictionary<string, nint> Texts = new();
        public readonly HashSet<nint> Refuse = [];                     // collides CreatePolyObjectStatic answers 0 for
        public int ParserLive;
        private int _cursor;
        private nint _next = 0x5000;
        private ParsedSolid _last;

        public nint Environment() => Env;

        public VCollideView Collide(int modelIndex) => Collides.TryGetValue(modelIndex, out VCollideView v) ? v : default;

        public nint CollideSolid(in VCollideView collide, int index) => Solids.TryGetValue(index, out nint c) ? c : 0;
        public Vector CollideMassCenter(nint solid) => throw new NotSupportedException();

        public int SurfaceIndex(string name) => name == "default" ? 7 : name.Length;

        public nint NativeText(string text)
        {
            if (!Texts.TryGetValue(text, out nint p))
                Texts[text] = p = 0x9000 + Texts.Count;
            return p;
        }

        public string? TextAt(nint p) => Texts.FirstOrDefault(kv => kv.Value == p).Key;

        public nint CreatePolyObjectStatic(nint collide, int surfaceIndex, in ObjectParams objectParams)
        {
            nint made = Refuse.Contains(collide) ? 0 : _next++;
            Made.Add((collide, surfaceIndex, objectParams, made));
            Calls.Add($"create {collide:x}");
            return made;
        }

        public void MarkNeverDeleted(nint physicsObject) => NeverDeleted.Add(physicsObject);

        public void SetContents(nint physicsObject, uint contents) => Contents[physicsObject] = contents;

        public nint CreateFluidController(nint waterObject, nint gameData)
        {
            Fluids.Add((waterObject, gameData));
            return 1;
        }

        public nint KeyParserCreate(nint keyValues)
        {
            ParserLive++;
            _cursor = 0;
            Calls.Add("parser");
            return 0x77;
        }

        public void KeyParserDestroy(nint parser)
        {
            ParserLive--;
            Calls.Add("destroy");
        }

        public bool Finished(nint parser) => _cursor >= Blocks.Count;

        public string BlockName(nint parser) => Blocks[_cursor].Name;

        public ParsedSolid ParseSolid(nint parser)
        {
            Block b = Blocks[_cursor++];
            ObjectParams p = ObjectParams.Defaults(NativeText("DEFAULT"));
            p.Mass = b.Mass;
            Calls.Add($"solid {b.Index}");
            return _last = new ParsedSolid(b.Index, b.Contents, p);
        }

        public readonly Dictionary<nint, (ParsedSolid Solid, bool Enable)> Out = new();

        public void CopyParsedSolid(nint solid) => Out[solid] = (_last, Out.TryGetValue(solid, out var o) && o.Enable);

        public void ResetSolid(nint solid)
        {
            Calls.Add("reset");
            Out[solid] = (new ParsedSolid(0, 0, ObjectParams.Defaults(NativeText("DEFAULT"))), false);
        }

        public void SetSolidEnableCollisions(nint solid, bool enable) => Out[solid] = (Out[solid].Solid, enable);

        public ParsedFluid ParseFluid(nint parser)
        {
            Block b = Blocks[_cursor++];
            Calls.Add($"fluid {b.Index}");
            return new ParsedFluid(b.Index, b.SurfaceProp);
        }

        public void ParseSurfaceTable(nint parser, Span<int> table)
        {
            _cursor++;
            for (int i = 0; i < table.Length; i++)
                table[i] = i * 3;
        }

        public void SetWorldMaterialIndexTable(ReadOnlySpan<int> table) => MaterialTable = table.ToArray();

        public void SkipBlock(nint parser)
        {
            Calls.Add($"skip {Blocks[_cursor].Name}");
            _cursor++;
        }

        public bool SupportsVirtualMesh() => VirtualMesh;

        public nint VirtualTerrainCollide(int index) => Terrain.TryGetValue(index, out nint c) ? c : 0;

        public void CreateStaticPropPhysics(nint environment) => Calls.Add($"staticprops {environment:x}");
    }

    private static readonly ObjectParams Defaults = ObjectParams.Defaults(0x4242);

    private static FakeWorld World(int solidCount = 3, params Block[] blocks)
    {
        var w = new FakeWorld();
        w.Collides[PhysicsWorldBuilder.WorldModelIndex] = new VCollideView(0x100, solidCount, 0x200);
        for (int i = 0; i < solidCount; i++)
            w.Solids[i] = 0x300 + i;
        w.Blocks.AddRange(blocks);
        return w;
    }

    // ---- PhysCreateWorld_Shared ------------------------------------------------

    [Fact]
    public void TheWorldIsSolidZeroOnTheDefaultSurfaceNamedWorld()
    {
        FakeWorld w = World(1);

        nint world = PhysicsWorldBuilder.Create(w, 0, Defaults, staticProps: false);

        var only = Assert.Single(w.Made);
        Assert.Equal((nint)0x300, only.Collide);
        Assert.Equal(7, only.Surface);
        Assert.Equal("world", w.TextAt(only.Params.Name));
        Assert.Equal((nint)0, only.Params.GameData);
        Assert.Equal(Defaults.Mass, only.Params.Mass);
        Assert.Equal(Defaults.DragCoefficient, only.Params.DragCoefficient);
        Assert.Equal(only.Made, world);
        Assert.Contains(world, w.NeverDeleted);
    }

    [Fact]
    public void NoEnvironmentMakesNothingAndAsksNoStaticProps()
    {
        FakeWorld w = World(2);
        w.Env = 0;

        Assert.Equal((nint)0, PhysicsWorldBuilder.Create(w, 0, Defaults, staticProps: true));
        Assert.Empty(w.Made);
        Assert.Empty(w.Calls);
    }

    [Fact]
    public void NoSolidsMakesNoWorld()
    {
        FakeWorld w = World(0);

        Assert.Equal((nint)0, PhysicsWorldBuilder.Create(w, 0, Defaults, staticProps: false));
        Assert.Empty(w.Made);
    }

    [Fact]
    public void TheServersStaticPropsComeBeforeTheWorldAndTheClientsDoNot()
    {
        FakeWorld server = World(1);
        PhysicsWorldBuilder.Create(server, 0, Defaults, staticProps: true);
        Assert.Equal("staticprops 1000", server.Calls[0]);
        Assert.Equal("create 300", server.Calls[1]);

        FakeWorld client = World(1);
        PhysicsWorldBuilder.Create(client, 0, Defaults, staticProps: false);
        Assert.DoesNotContain(client.Calls, c => c.StartsWith("staticprops", StringComparison.Ordinal));
    }

    [Fact]
    public void SolidZeroIsNotMadeTwice()
    {
        FakeWorld w = World(2, new Block("solid", 0));

        PhysicsWorldBuilder.Create(w, 0, Defaults, staticProps: false);

        Assert.Single(w.Made);
    }

    [Fact]
    public void AnotherSolidIsAWorldPieceWithItsParsedParamsAndContents()
    {
        FakeWorld w = World(3, new Block("solid", 2, Contents: 0x21, Mass: 55f), new Block("StaticSolid", 1, Contents: 0x4000));

        nint world = PhysicsWorldBuilder.Create(w, 0, Defaults, staticProps: false);

        Assert.Equal(3, w.Made.Count);
        var piece = w.Made[1];
        Assert.Equal((nint)0x302, piece.Collide);
        Assert.Equal(7, piece.Surface);
        Assert.Equal(55f, piece.Params.Mass);
        Assert.Equal(1, piece.Params.EnableCollisions);
        Assert.Equal("world", w.TextAt(piece.Params.Name));
        Assert.Equal(0x21u, w.Contents[piece.Made]);
        Assert.Contains(piece.Made, w.NeverDeleted);
        Assert.Equal(0x4000u, w.Contents[w.Made[2].Made]);
        Assert.Equal(w.Made[0].Made, world);
    }

    [Fact]
    public void WhenSolidZeroMakesNothingTheFirstPieceIsTheWorld()
    {
        FakeWorld w = World(3, new Block("solid", 1), new Block("solid", 2));
        w.Refuse.Add(0x300);

        nint world = PhysicsWorldBuilder.Create(w, 0, Defaults, staticProps: false);

        Assert.Equal(w.Made[1].Made, world);
        Assert.NotEqual((nint)0, world);
    }

    [Fact]
    public void APieceVphysicsRefusesGetsNoContents()
    {
        FakeWorld w = World(2, new Block("solid", 1));
        w.Refuse.Add(0x301);

        PhysicsWorldBuilder.Create(w, 0, Defaults, staticProps: false);

        Assert.Equal(2, w.Made.Count);
        Assert.Empty(w.Contents);
    }

    [Fact]
    public void ASolidPastTheCountOrWithNoCollideAsksForVirtualTerrain()
    {
        FakeWorld past = World(2, new Block("solid", 5));
        past.Terrain[3] = 0x800;
        past.Terrain[12] = 0x801;
        PhysicsWorldBuilder.Create(past, 0, Defaults, staticProps: false);
        Assert.Equal(3, past.Made.Count);
        Assert.Equal("vdisp_0003", past.TextAt(past.Made[1].Params.Name));
        Assert.Equal("vdisp_0012", past.TextAt(past.Made[2].Params.Name));
        Assert.Equal(7, past.Made[2].Surface);
        Assert.Equal(1, past.Made[2].Params.EnableCollisions);
        Assert.Contains(past.Made[2].Made, past.NeverDeleted);

        FakeWorld hole = World(3, new Block("solid", 2));
        hole.Solids.Remove(2);
        hole.Terrain[0] = 0x800;
        PhysicsWorldBuilder.Create(hole, 0, Defaults, staticProps: false);
        Assert.Equal("vdisp_0000", hole.TextAt(hole.Made[1].Params.Name));
    }

    [Fact]
    public void TheVirtualTerrainBlockIsSkippedAndAsksForTerrainOnlyWhenVphysicsHasVirtualMeshes()
    {
        FakeWorld with = World(1, new Block("virtualterrain"));
        with.Terrain[0] = 0x800;
        PhysicsWorldBuilder.Create(with, 0, Defaults, staticProps: false);
        Assert.Contains("skip virtualterrain", with.Calls);
        Assert.Equal(2, with.Made.Count);

        FakeWorld without = World(1, new Block("virtualterrain"));
        without.Terrain[0] = 0x800;
        without.VirtualMesh = false;
        PhysicsWorldBuilder.Create(without, 0, Defaults, staticProps: false);
        Assert.Single(without.Made);
    }

    [Fact]
    public void AFluidInRangeIsWaterWithTheWorldsParamsItsSurfaceAndAController()
    {
        FakeWorld w = World(3, new Block("fluid", 2, SurfaceProp: "slime"), new Block("fluid", 0), new Block("fluid", 3));

        PhysicsWorldBuilder.Create(w, 0x55, Defaults, staticProps: false);

        Assert.Equal(2, w.Made.Count);
        var water = w.Made[1];
        Assert.Equal((nint)0x302, water.Collide);
        Assert.Equal("slime".Length, water.Surface);
        Assert.Equal("fluid", w.TextAt(water.Params.Name));
        Assert.Equal((nint)0x55, water.Params.GameData);
        Assert.Equal(Defaults.Mass, water.Params.Mass);
        Assert.Contains(water.Made, w.NeverDeleted);
        Assert.Equal((water.Made, (nint)0x55), Assert.Single(w.Fluids));
    }

    [Fact]
    public void TheMaterialTableIsAHundredAndTwentyEightEntriesHandedToTheSurfaceTable()
    {
        FakeWorld w = World(1, new Block("materialtable"));

        PhysicsWorldBuilder.Create(w, 0, Defaults, staticProps: false);

        Assert.NotNull(w.MaterialTable);
        Assert.Equal(128, w.MaterialTable!.Length);
        Assert.Equal(381, w.MaterialTable[127]);
    }

    [Fact]
    public void AnUnknownBlockIsSkippedAndTheParserIsDestroyedOnce()
    {
        FakeWorld w = World(1, new Block("editparams"), new Block("solid", 0));

        PhysicsWorldBuilder.Create(w, 0, Defaults, staticProps: false);

        Assert.Contains("skip editparams", w.Calls);
        Assert.Equal(0, w.ParserLive);
        Assert.Single(w.Calls, c => c == "destroy");
    }

    [Fact]
    public void DispNamesAreFourDigitsZeroPadded()
    {
        Assert.Equal("vdisp_0000", PhysicsWorldBuilder.DispName(0));
        Assert.Equal("vdisp_2047", PhysicsWorldBuilder.DispName(2047));
        Assert.Equal(2048, PhysicsWorldBuilder.MaxMapDispInfo);
    }

    // ---- PhysModelParseSolidByIndex --------------------------------------------

    private static FakeWorld Model(params Block[] blocks)
    {
        var w = new FakeWorld();
        w.Collides[9] = new VCollideView(0x100, 4, 0x200);
        w.Blocks.AddRange(blocks);
        return w;
    }

    [Fact]
    public void AModelWithNoCollideParsesNothingAndLeavesTheSolidAlone()
    {
        var w = new FakeWorld();

        Assert.False(ModelSolidParse.Parse(w, 9, -1, 0x600));
        Assert.Empty(w.Out);
        Assert.Empty(w.Calls);
    }

    [Fact]
    public void AnyIndexTakesTheFirstSolidAndEnablesCollisions()
    {
        FakeWorld w = Model(new Block("editparams"), new Block("solid", 0, Mass: 12f), new Block("solid", 1));

        Assert.True(ModelSolidParse.Parse(w, 9, -1, 0x600));

        Assert.Equal(12f, w.Out[0x600].Solid.Params.Mass);
        Assert.True(w.Out[0x600].Enable);
        Assert.Equal(["reset", "parser", "skip editparams", "solid 0", "destroy"], w.Calls);
    }

    [Fact]
    public void AGivenIndexParsesPastTheOthers()
    {
        FakeWorld w = Model(new Block("solid", 0), new Block("solid", 2, Mass: 3f), new Block("solid", 3));

        Assert.True(ModelSolidParse.Parse(w, 9, 2, 0x600));

        Assert.Equal(2, w.Out[0x600].Solid.Index);
        Assert.Equal(["reset", "parser", "solid 0", "solid 2", "destroy"], w.Calls);
    }

    [Fact]
    public void NoMatchingSolidAnswersFalseButTheSolidIsStillResetAndEnabled()
    {
        FakeWorld w = Model(new Block("solid", 0));

        Assert.False(ModelSolidParse.Parse(w, 9, 3, 0x600));

        Assert.Equal(0, w.Out[0x600].Solid.Index);
        Assert.True(w.Out[0x600].Enable);
        Assert.Equal(0, w.ParserLive);
    }

    // ---- PrecachePhysicsSounds -------------------------------------------------

    private sealed class FakeSounds : ISurfaceSoundTable
    {
        public readonly Dictionary<int, ushort[]> Names = new();
        public readonly Dictionary<int, short[]> Handles = new();
        public int Surfaces;

        public int Count() => Surfaces;

        public bool SoundNames(int surface, Span<ushort> names)
        {
            if (!Names.TryGetValue(surface, out ushort[]? n))
                return false;
            n.CopyTo(names);
            return true;
        }

        public string? Text(ushort index) => "S" + index;

        public void WriteHandles(int surface, ReadOnlySpan<short> handles) => Handles[surface] = handles.ToArray();
    }

    [Fact]
    public void EverySetNameIsPrecachedInFieldOrderAndAnUnsetOneIsTheInvalidHandle()
    {
        var table = new FakeSounds { Surfaces = 1 };
        table.Names[0] = [1, 0, 3, 4, 5, 6, 7, 8, 9, 10];
        var asked = new List<string>();

        int n = SurfaceSoundPrecache.Run(table, name => { asked.Add(name); return (short)(100 + asked.Count); });

        Assert.Equal(9, n);
        Assert.Equal(["S1", "S3", "S4", "S5", "S6", "S7", "S8", "S9", "S10"], asked);
        Assert.Equal([101, -1, 102, 103, 104, 105, 106, 107, 108, 109], table.Handles[0]);
    }

    [Fact]
    public void ASurfaceVphysicsDoesNotAnswerIsLeftAlone()
    {
        var table = new FakeSounds { Surfaces = 3 };
        table.Names[1] = new ushort[10];

        SurfaceSoundPrecache.Run(table, _ => 5);

        Assert.Equal([1], table.Handles.Keys);
        Assert.All(table.Handles[1], h => Assert.Equal(SurfaceSoundPrecache.InvalidHandle, h));
    }

    // ---- PhysComputeSlideDirection ---------------------------------------------

    [Fact]
    public void VelocityIntoAnImmovableContactIsClippedToItsPlane()
    {
        var (v, _) = PhysicsSlideDirection.Compute(new Vector(10, 4, 0), Vector.Zero,
                                                   [new SlideContact(new Vector(1, 0, 0), false, 0f)], 50f, false);
        Assert.Equal(new Vector(0, 4, 0), v);
    }

    [Fact]
    public void VelocityAwayFromTheContactIsKept()
    {
        var (v, _) = PhysicsSlideDirection.Compute(new Vector(-10, 4, 0), Vector.Zero,
                                                   [new SlideContact(new Vector(1, 0, 0), false, 0f)], 50f, false);
        Assert.Equal(new Vector(-10, 4, 0), v);
    }

    [Fact]
    public void AMoveableContactClipsOnlyWhenHeavierThanTheMinimum()
    {
        SlideContact light = new(new Vector(0, 0, 1), true, 50f);
        SlideContact heavy = new(new Vector(0, 0, 1), true, 50.5f);

        Assert.Equal(new Vector(0, 0, 3), PhysicsSlideDirection.Compute(new Vector(0, 0, 3), Vector.Zero, [light], 50f, false).Velocity);
        Assert.Equal(Vector.Zero, PhysicsSlideDirection.Compute(new Vector(0, 0, 3), Vector.Zero, [heavy], 50f, false).Velocity);
    }

    [Fact]
    public void TheAngularVelocityKeepsOnlyItsPartAlongTheNormalWhenAskedFor()
    {
        SlideContact wall = new(new Vector(0, 1, 0), false, 0f);

        Assert.Equal(new Vector(0, 2, 0), PhysicsSlideDirection.Compute(Vector.Zero, new Vector(5, 2, 9), [wall], 1f, true).AngularVelocity);
        Assert.Equal(new Vector(5, 2, 9), PhysicsSlideDirection.Compute(Vector.Zero, new Vector(5, 2, 9), [wall], 1f, false).AngularVelocity);
    }
}
