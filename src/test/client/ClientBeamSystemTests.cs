using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Beams;
using SourceSharp.Client.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed beam system as native's <c>beams</c> and the executor see it:
/// the BeamInfo payload, the renderable pool, the draw by renderable, the
/// lifecycle, the wireframe arm and the reload carry. The arithmetic is
/// BeamStockReplayTests'; this is the wiring around it.
/// </summary>
public class ClientBeamSystemTests
{
    private const int Sprite = 7;

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 10f;
    }

    private sealed class Executor : IClientBeamExecutor
    {
        public readonly Dictionary<int, (Vector Origin, Vector Mins, Vector Maxs)> Renderables = [];
        public readonly List<string> Draws = [];
        public readonly List<bool> Wireframes = [];
        public int NextRenderable = 40;
        public bool Visible = true;

        public void View(bool insideView, Span<float> out42)
        {
            out42.Clear();
            out42[1] = -200;                    // origin y
            out42[4] = 1;                       // forward y
            out42[6] = 1;                       // right x
            out42[11] = 1;                      // up z
            out42[17] = 1;                      // main up z
            out42[18] = 1;                      // main right x
            out42[21] = 1; out42[26] = 1; out42[30] = 1; out42[36] = 1;
            out42[37] = 10f;                    // curtime
            out42[38] = 1f / 60f;               // frametime
            out42[39] = Camera.X; out42[40] = Camera.Y; out42[41] = Camera.Z;
        }

        // The render context's camera View answers (the view origin unless a test moves it).
        public Vector Camera = new(0, -200, 0);
        public readonly List<float[]> StripVertices = [];

        public bool ModelInfo(int modelIndex, out int frameCount)
        {
            frameCount = 1;
            return modelIndex == Sprite;
        }

        public int ModelIndex(string name) => name == "sprites/laserbeam.vmt" ? Sprite : -1;
        public EntityHandle EntityAt(int entry) => EntityHandle.None;
        public bool EntityHasModel(EntityHandle entity) => false;
        public int Facts;
        public Vector Attachment, Centre, Origin;
        public readonly List<bool> TriedAttachment = [];

        public int EntityFacts(EntityHandle entity, int attachment, bool tryAttachment, Span<float> out9)
        {
            TriedAttachment.Add(tryAttachment);
            out9.Clear();
            out9[0] = Attachment.X; out9[1] = Attachment.Y; out9[2] = Attachment.Z;
            out9[3] = Centre.X; out9[4] = Centre.Y; out9[5] = Centre.Z;
            out9[6] = Origin.X; out9[7] = Origin.Y; out9[8] = Origin.Z;
            return Facts;
        }

        public void BoxVisibility(in Vector mins, in Vector maxs, out bool visible, out bool culled)
        {
            visible = Visible;
            culled = false;
        }

        public Vector ScreenPosition(in Vector point) => point;

        public int RenderableAdd(in Vector origin, in Vector mins, in Vector maxs)
        {
            int index = NextRenderable++;
            Renderables[index] = (origin, mins, maxs);
            return index;
        }

        public void RenderableChanged(int renderable, in Vector origin, in Vector mins, in Vector maxs)
            => Renderables[renderable] = (origin, mins, maxs);

        public void RenderableRemove(int renderable) => Assert.True(Renderables.Remove(renderable),
            $"renderable {renderable} was removed once, after it was added");

        public bool BindSprite(int modelIndex, int frame, int renderMode, bool wireframe)
        {
            Draws.Add($"bind {modelIndex}");
            Wireframes.Add(wireframe);
            return modelIndex == Sprite;
        }

        public void SetHdrColorScale(int modelIndex, int renderMode, float scale) { }
        public bool Glow(int modelIndex, bool bind) => false;
        // Seventeen floats a vertex, two vertices a segment: the segment count.
        public void Strip(ReadOnlySpan<float> vertices)
        {
            Draws.Add($"strip {vertices.Length / 34}");
            StripVertices.Add(vertices.ToArray());
        }
        public void Mesh(int primitive, int primitives, int colorKind, ReadOnlySpan<float> vertices)
            => Draws.Add($"mesh {primitive} {vertices.Length / 9}");
    }

    // Every halo's sight line is clear.
    private sealed class AllVisible : IPixelVisibility
    {
        public float FractionVisible(in PixelVisParams p, ref int queryHandle) => 1;
        public float FractionVisibleNoHandle(in Vector position) => 1;
        public bool IsAvailable => true;
        public float GlowSightDistance(in Vector glowOrigin, bool shouldTrace) => 1;
    }

    /// <summary>The viewmodel conversion: moves one entity's points by an offset, as a recognisable answer.</summary>
    private sealed class ViewModelFormat : SourceSharp.Client.Standalone.IViewModelAttachmentFormat
    {
        public EntityHandle ViewModel = EntityHandle.None;

        public Vector ForWorldPass(EntityHandle entity, Vector attachment)
            => entity == ViewModel ? attachment + new Vector(100, 0, 0) : attachment;
    }

    private sealed class Rig
    {
        public readonly RecordingEngine Engine = new();
        public readonly Executor Exec = new();
        public readonly Clock Clock = new();
        public readonly RecordingLog Log = new();
        public readonly Dictionary<string, string?> EngineConVars = new()
        {
            ["sv_cheats"] = "0", ["mat_wireframe"] = "0", ["r_DrawBeams"] = "1",
        };
        public readonly ClientBeamSystem Beams;

        public Rig(string svCheats = "0", string matWireframe = "0")
        {
            Engine.Strict = false;
            EngineConVars["sv_cheats"] = svCheats;
            EngineConVars["mat_wireframe"] = matWireframe;
            Engine.ReturnsEachCall("Console.FindConVarValue",
                () => EngineConVars.TryGetValue((string)Engine.Last("Console.FindConVarValue")!.Value.Arg(0)!,
                                                out string? v) ? v : null);

            Beams = new ClientBeamSystem(Exec, Clock, Engine, Log, new AllVisible(), ViewModels);
        }

        public readonly ViewModelFormat ViewModels = new();

        public void Set(string name, string value) => EngineConVars[name] = value;

        public void Update() => Beams.Update(Clock.Now, 1f / 60f);
    }

    // A ring point around (0 0 64), 16 to 128 wide, as the temp entity
    // backends create one.
    private static bool CreateRingPoint(Rig rig)
    {
        long before = rig.Beams.Counters.Created;
        rig.Beams.CreateBeamRingPoint(new Vector(0, 0, 64), 16f, 128f, Sprite, -1, 0f, 2f, 4f, 4f, 0f, 0f, 255f,
                                      0f, 0, 0f, 255f, 128f, 0f, 0);
        return rig.Beams.Counters.Created == before + 1;
    }

    // A straight beam from the origin along x, its sprite named.
    private static BeamInfo PointsAlongX(float life) => new()
    {
        End = new Vector(100, 0, 0),
        Life = life,
        ModelName = "sprites/laserbeam.vmt",
    };

    [Fact]
    public void RDrawBeamsIsTheEnginesReadByNameAndAMissingOneIsStocksDefault()
    {
        var rig = new Rig();
        rig.EngineConVars.Remove("r_DrawBeams");
        CreateRingPoint(rig);
        rig.Update();

        rig.Beams.Draw(rig.Beams.Bookkeeping.Active[0].Renderable);

        Assert.Equal("bind 7", Assert.Single(rig.Exec.Draws, d => d.StartsWith("bind", StringComparison.Ordinal)));
    }

    [Fact]
    public void ABeamInfoRingPointIsCreatedAndGetsARenderable()
    {
        var rig = new Rig();

        Assert.True(CreateRingPoint(rig));

        Beam beam = Assert.Single(rig.Beams.Bookkeeping.Active);
        Assert.Equal(BeamType.RingPoint, beam.Type);
        Assert.Equal(Sprite, beam.ModelIndex);
        Assert.Equal(new Vector(0, 0, 64), beam.Attachment[2]);
        Assert.Equal(16f, beam.StartRadius);
        Assert.Equal(128f, beam.EndRadius);
        Assert.Equal(40, beam.Renderable);
        Assert.True(rig.Exec.Renderables.ContainsKey(40));
        Assert.Equal(1, rig.Beams.Counters.Created);
    }

    [Fact]
    public void ABeamInfoPointsBeamResolvesItsSpriteByName()
    {
        var rig = new Rig();

        rig.Beams.CreateBeamPoints(PointsAlongX(1f));
        Assert.Equal(Sprite, Assert.Single(rig.Beams.Bookkeeping.Active).ModelIndex);
    }

    [Fact]
    public void AStripFacesTheRenderContextsCameraNotTheViewOrigin()
    {
        var rig = new Rig();
        // Above the beam, where the view origin is off to one side of it.
        rig.Exec.Camera = new Vector(50, 0, 300);
        BeamInfo info = PointsAlongX(2f);
        info.Width = info.EndWidth = 4f;
        info.Brightness = 255f;
        info.Red = info.Green = info.Blue = 255f;
        rig.Beams.CreateBeamPoints(info);
        rig.Update();

        rig.Beams.Draw(Assert.Single(rig.Beams.Bookkeeping.Active).Renderable);

        float[] v = Assert.Single(rig.Exec.StripVertices);
        Assert.True(v.Length >= 4 * 17, "the beam drew no strip");
        for (int a = 0; a + 17 < v.Length; a += 34)
        {
            // Across a beam along x seen from above: tangent S lies along y.
            Assert.True(MathF.Abs(v[a + 12]) > 0.99f, $"vertex {a / 17}: tangent S is ({v[a + 11]}, {v[a + 12]}, {v[a + 13]})");
        }
    }

    [Fact]
    public void AnUpdateMovesTheRenderableToTheBeamsBounds()
    {
        var rig = new Rig();
        CreateRingPoint(rig);

        rig.Clock.Now += 0.5f;
        rig.Update();

        Beam beam = Assert.Single(rig.Beams.Bookkeeping.Active);
        var (origin, mins, maxs) = rig.Exec.Renderables[beam.Renderable];
        Assert.Equal(beam.RenderOrigin, origin);
        Assert.Equal(beam.Mins, mins);
        Assert.Equal(beam.Maxs, maxs);
        Assert.True(maxs.X > 0, "a growing ring has a box, not the zero one it was added with");
    }

    [Fact]
    public void TheRenderablesDrawIsTheBeamsRingThroughTheExecutor()
    {
        var rig = new Rig();
        CreateRingPoint(rig);
        rig.Clock.Now += 0.5f;
        rig.Update();
        int renderable = rig.Beams.Bookkeeping.Active[0].Renderable;

        rig.Beams.Draw(renderable);

        Assert.Equal("bind 7", rig.Exec.Draws[0]);
        Assert.StartsWith("mesh 3 ", rig.Exec.Draws[1]);           // a ring is a triangle strip mesh
        Assert.Equal(1, rig.Beams.Counters.Draws);
        Assert.Equal(1, rig.Beams.Counters.Meshes);
    }

    // An ss_beam's state as ClientBeam hands it over: a points beam 64 high.
    private static BeamInfo EntityBeam() => new()
    {
        Type = BeamType.Points,
        Start = new Vector(0, 0, 0),
        End = new Vector(0, 0, 64),
        ModelIndex = Sprite,
        HaloIndex = -1,
        Width = 4f,
        EndWidth = 4f,
        FadeLength = 32f,
        Brightness = 255f,
        Red = 120f, Green = 255f, Blue = 150f,
        Segments = -1,
    };

    [Fact]
    public void AnEntitysBeamIsDrawnNowAndLeavesNothingBehind()
    {
        // Stock's DrawBeam( C_Beam * ): a Beam_t on the stack, drawn at once.
        var rig = new Rig();

        rig.Beams.DrawEntityBeam(EntityBeam(), BeamFlags.None, 1f);

        Assert.Equal("bind 7", rig.Exec.Draws[0]);
        Assert.StartsWith("strip ", rig.Exec.Draws[1]);
        Assert.Equal(1, rig.Beams.Counters.Draws);
        Assert.Empty(rig.Beams.Bookkeeping.Active);
        Assert.Empty(rig.Exec.Renderables);
    }

    [Fact]
    public void AnEntitysShadeOutReachesTheStrip()
    {
        var plain = new Rig();
        plain.Beams.DrawEntityBeam(EntityBeam(), BeamFlags.None, 1f);
        var shaded = new Rig();
        shaded.Beams.DrawEntityBeam(EntityBeam(), BeamFlags.ShadeOut, 1f);

        Assert.NotEqual(plain.Exec.StripVertices[0], shaded.Exec.StripVertices[0]);
    }

    [Fact]
    public void OnlyTheDrawFlagsAreTakenFromTheEntity()
    {
        // The control for the fact above: FADEIN is a temp beam's life flag,
        // not one DrawBeam( C_Beam * ) copies from the entity.
        var plain = new Rig();
        plain.Beams.DrawEntityBeam(EntityBeam(), BeamFlags.None, 1f);
        var faded = new Rig();
        faded.Beams.DrawEntityBeam(EntityBeam(), BeamFlags.FadeIn, 1f);

        Assert.Equal(plain.Exec.StripVertices[0], faded.Exec.StripVertices[0]);
    }

    [Fact]
    public void ADrawOfAnUnknownRenderableDrawsNothing()
    {
        var rig = new Rig();
        CreateRingPoint(rig);

        rig.Beams.Draw(999);

        Assert.Empty(rig.Exec.Draws);
        Assert.Equal(0, rig.Beams.Counters.Draws);
    }

    // The pool's other renderables (IClientTranslucentRenderables).

    [Fact]
    public void AnotherRenderableInThePoolIsDrawnByItsOwnDraw()
    {
        var rig = new Rig();
        int drawn = 0;
        int handle = rig.Beams.Add(new Vector(1, 2, 3), new Vector(-4, -4, -4), new Vector(4, 4, 4), () => drawn++);

        rig.Beams.Changed(handle, new Vector(5, 6, 7), new Vector(-4, -4, -4), new Vector(4, 4, 4));
        rig.Beams.Draw(handle);

        Assert.Equal(1, drawn);
        Assert.Equal(new Vector(5, 6, 7), rig.Exec.Renderables[handle].Origin);
        Assert.Empty(rig.Exec.Draws);
    }

    [Fact]
    public void AnotherRenderablesRemovalLeavesTheBeamsAlone()
    {
        var rig = new Rig();
        CreateRingPoint(rig);
        var (beam, placed) = Assert.Single(rig.Exec.Renderables);
        int other = rig.Beams.Add(Vector.Zero, Vector.Zero, Vector.Zero, () => { });

        rig.Beams.Remove(beam);
        rig.Beams.Changed(beam, new Vector(9, 9, 9), Vector.Zero, Vector.Zero);
        Assert.Equal(2, rig.Exec.Renderables.Count);
        Assert.Equal(placed, rig.Exec.Renderables[beam]);

        rig.Beams.Remove(other);
        Assert.Equal([beam], rig.Exec.Renderables.Keys);
    }

    [Fact]
    public void ADeadBeamIsRetiredAndItsRenderableRemoved()
    {
        var rig = new Rig();
        CreateRingPoint(rig);

        rig.Clock.Now += 3f;           // past the two-second life
        rig.Update();

        Assert.Empty(rig.Beams.Bookkeeping.Active);
        Assert.Empty(rig.Exec.Renderables);
    }

    [Fact]
    public void ClearAndInitDropEveryBeamAndInitSizesTheTrailPool()
    {
        var rig = new Rig();
        CreateRingPoint(rig);

        rig.Beams.Lifecycle(2, 0);
        Assert.Empty(rig.Beams.Bookkeeping.Active);
        Assert.Empty(rig.Exec.Renderables);

        rig.Beams.Lifecycle(0, 100);
        Assert.Equal(512, rig.Beams.Bookkeeping.Trails);       // MAX( -particles, MIN_PARTICLES )
        rig.Beams.Lifecycle(0, -1);
        Assert.Equal(2048, rig.Beams.Bookkeeping.Trails);      // DEFAULT_PARTICLES
    }

    [Fact]
    public void RDrawBeamsZeroDrawsNothingAndTwoIsWireframe()
    {
        var rig = new Rig();
        CreateRingPoint(rig);
        rig.Clock.Now += 0.5f;
        rig.Update();
        int renderable = rig.Beams.Bookkeeping.Active[0].Renderable;

        rig.Set("r_DrawBeams", "0");
        rig.Update();
        rig.Beams.Draw(renderable);
        Assert.Empty(rig.Exec.Draws);

        rig.Set("r_DrawBeams", "2");
        rig.Update();
        rig.Beams.Draw(renderable);
        Assert.Equal([true], rig.Exec.Wireframes);
    }

    [Fact]
    public void MatWireframeIsWireframeOnlyWithCheats()
    {
        var off = new Rig(svCheats: "0", matWireframe: "1");
        CreateRingPoint(off);
        off.Update();
        off.Beams.Draw(off.Beams.Bookkeeping.Active[0].Renderable);

        var on = new Rig(svCheats: "1", matWireframe: "1");
        CreateRingPoint(on);
        on.Update();
        on.Beams.Draw(on.Beams.Bookkeeping.Active[0].Renderable);

        Assert.Equal([false], off.Exec.Wireframes);
        Assert.Equal([true], on.Exec.Wireframes);
    }

    [Fact]
    public void AReloadCarriesEveryBeamAndItsRenderable()
    {
        var rig = new Rig();
        CreateRingPoint(rig);
        rig.Clock.Now += 0.5f;
        rig.Update();
        Beam before = rig.Beams.Bookkeeping.Active[0];

        var writer = new ReloadWriter();
        rig.Beams.Save(writer);

        var restored = new Rig();
        restored.Beams.Restore(new ReloadReader(writer.ToArray()));

        Beam after = Assert.Single(restored.Beams.Bookkeeping.Active);
        Assert.Equal(before.Renderable, after.Renderable);
        Assert.Equal(before.Type, after.Type);
        Assert.Equal(before.Die, after.Die);
        Assert.Equal(before.Delta, after.Delta);
        Assert.Equal(before.Noise, after.Noise);
        Assert.Equal(rig.Beams.Counters.Created, restored.Beams.Counters.Created);

        // The restored system answers the renderable native still holds.
        restored.Beams.Draw(after.Renderable);
        Assert.Equal("bind 7", restored.Exec.Draws[0]);
    }

    [Fact]
    public void ATempEntityRingIsCountedAndARefusedOneToo()
    {
        var rig = new Rig();

        rig.Beams.CreateBeamRingPoint(new Vector(0, 0, 64), 16, 128, Sprite, -1, 0, 2, 4, 4, 0, 0, 255, 0, 0, 0,
                                      255, 128, 0, 0);
        rig.Beams.CreateBeamRingPoint(new Vector(0, 0, 64), 16, 128, -1, -1, 0, 2, 4, 4, 0, 0, 255, 0, 0, 0,
                                      255, 128, 0, 0);

        Assert.Equal(1, rig.Beams.Counters.Created);
        Assert.Equal(1, rig.Beams.Counters.Refused);
    }

    //-------------------------------------------------------------------------
    // Entity endpoints: BeamEndpoint's plan over native's facts.
    //-------------------------------------------------------------------------

    private static readonly EntityHandle Ent = new(0x00010020);
    private const BeamEntityFacts ManagedProp = BeamEntityFacts.Resolved | BeamEntityFacts.Renderable;

    private static (bool Answered, Vector Point, Rig Rig) Endpoint(BeamEntityFacts facts, bool hitboxes)
    {
        var rig = new Rig();
        rig.Exec.Facts = (int)facts;
        rig.Exec.Attachment = new Vector(1, 2, 3);
        rig.Exec.Centre = new Vector(4, 5, 6);
        rig.Exec.Origin = new Vector(7, 8, 9);

        var point = new Vector(-1, -1, -1);
        bool answered = rig.Beams.EntityPosition(Ent, 2, hitboxes, ref point);
        return (answered, point, rig);
    }

    [Fact]
    public void AnUnresolvedEndpointLeavesThePointAloneAndSaysSo()
    {
        var (answered, point, _) = Endpoint(BeamEntityFacts.None, hitboxes: false);
        Assert.False(answered);
        Assert.Equal(new Vector(-1, -1, -1), point);
    }

    [Fact]
    public void AnSdkEndpointIsNotAnsweredAsStocksCutBodyDidNot()
    {
        var (answered, point, _) = Endpoint(BeamEntityFacts.Resolved | BeamEntityFacts.Sdk | BeamEntityFacts.Renderable
                                            | BeamEntityFacts.Attachment, hitboxes: false);
        Assert.False(answered);
        Assert.Equal(new Vector(-1, -1, -1), point);
    }

    [Fact]
    public void SomethingThatDoesNotRenderIsNotFollowed()
    {
        var (answered, _, _) = Endpoint(BeamEntityFacts.Resolved, hitboxes: false);
        Assert.False(answered);
    }

    [Fact]
    public void AManagedEndpointTakesItsAttachmentWhenItAnswers()
    {
        var (answered, point, rig) = Endpoint(ManagedProp | BeamEntityFacts.Attachment, hitboxes: false);
        Assert.True(answered);
        Assert.Equal(new Vector(1, 2, 3), point);
        Assert.Equal([true], rig.Exec.TriedAttachment);
    }

    [Fact]
    public void ALocalViewmodelsAttachmentIsWhereTheWorldsProjectionDrawsIt()
    {
        // Stock's C_BaseViewModel::GetAttachment formats every attachment it
        // hands out, so a beam on the gun sits on the gun in the world's pass.
        var rig = new Rig();
        rig.ViewModels.ViewModel = Ent;
        rig.Exec.Facts = (int)(ManagedProp | BeamEntityFacts.Attachment);
        rig.Exec.Attachment = new Vector(1, 2, 3);

        var point = new Vector(-1, -1, -1);
        Assert.True(rig.Beams.EntityPosition(Ent, 2, false, ref point));
        Assert.Equal(new Vector(101, 2, 3), point);
    }

    [Fact]
    public void AManagedEndpointWithoutAnAttachmentTakesItsRenderOrigin()
    {
        var (answered, point, _) = Endpoint(ManagedProp | BeamEntityFacts.Collideable, hitboxes: false);
        Assert.True(answered);
        Assert.Equal(new Vector(7, 8, 9), point);
    }

    [Fact]
    public void AHitboxIndexIsRefusedOnceAndNotAskedAsAnAttachment()
    {
        var (answered, point, rig) = Endpoint(ManagedProp | BeamEntityFacts.Attachment, hitboxes: true);
        Assert.True(answered);
        Assert.Equal(new Vector(7, 8, 9), point);
        Assert.Equal([false], rig.Exec.TriedAttachment);

        Vector again = default;
        rig.Beams.EntityPosition(Ent, 2, true, ref again);
        Assert.Single(rig.Log.Lines, l => l.Warning && l.Text.Contains("HITBOX"));
    }

    [Fact]
    public void APlayerFallsBackToItsCentreAndNothingElseDoes()
    {
        var player = Endpoint(ManagedProp | BeamEntityFacts.Player | BeamEntityFacts.Collideable, hitboxes: false);
        var prop = Endpoint(ManagedProp | BeamEntityFacts.Collideable, hitboxes: false);

        Assert.Equal(new Vector(4, 5, 6), player.Point);
        Assert.Equal(new Vector(7, 8, 9), prop.Point);
        Assert.DoesNotContain(player.Rig.Log.Lines, l => l.Warning);
    }

    [Fact]
    public void BeamEndpointPlansMatchStocksTruthTable()
    {
        Assert.Equal(default, BeamEndpoint.PlanFor(false, true, true, true, true));
        Assert.Equal(BeamEndpointRoute.SdkEntity, BeamEndpoint.PlanFor(true, true, false, true, true).Route);
        Assert.Equal(default, BeamEndpoint.PlanFor(true, false, false, false, false));

        BeamEndpointPlan hitbox = BeamEndpoint.PlanFor(true, false, true, true, false);
        Assert.Equal(new BeamEndpointPlan(BeamEndpointRoute.ManagedRenderable, false, true,
                                          BeamEndpointFallback.RenderOrigin), hitbox);

        BeamEndpointPlan player = BeamEndpoint.PlanFor(true, false, true, false, true);
        Assert.Equal(new BeamEndpointPlan(BeamEndpointRoute.ManagedRenderable, true, false,
                                          BeamEndpointFallback.WorldSpaceCenter), player);
    }
}
