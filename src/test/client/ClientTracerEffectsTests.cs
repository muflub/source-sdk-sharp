using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The three tracer effects (fx_tracer.cpp) as managed handlers, the dispatch
/// table answering a managed handler before the native registrations, the
/// effect list across a managed reload, and the native entries reaching it.
///
/// <para>
/// THE CALLBACKS' PLAYER ARM IS NOT IN THE RECORDING, and cannot be: stock's
/// <c>C_BasePlayer::GetLocalPlayer()</c> is NULL in this mod, so every recorded
/// <c>Tracer</c> and <c>ParticleTracer</c> callback returned before drawing
/// (<c>effectlist-stock.tsv.gz</c>, <c>player=0</c>). The managed handlers take
/// the viewer from the engine's slot (the dead arm, ported), and what they do
/// with it is stock's callback read from its source; what THAT calls
/// (<c>FX_Tracer</c>, <c>FX_PlayerTracer</c>, <c>FX_TracerSound</c>) is replayed
/// against the recording in ClientEffectListTests.
/// </para>
/// </summary>
public class ClientTracerEffectsTests
{
    private sealed class Rig
    {
        public readonly FxTestRig Fx = new();
        public readonly RecordingEngine Console = TestServices.Fake();
        public readonly ClientEffectList List;
        public readonly ClientSideEffectFactory Factory;
        public bool Draw = true;
        public readonly TracerSupport Tracers;

        public Rig(string developer = "0")
        {
            Console.Returns("Console.FindConVarValue", developer);
            List = new ClientEffectList(Fx, Console);
            Factory = new ClientSideEffectFactory(Fx, List, Fx, () => true);
            Tracers = new TracerSupport(Fx, Console, () => Draw);
        }
    }

    private static EffectData Shot(int entIndex = 5, int flags = 0, float scale = 0f)
        => new()
        {
            Start = new Vector(0f, 0f, 64f),
            Origin = new Vector(1000f, 0f, 64f),
            EntIndex = entIndex,
            Flags = flags,
            Scale = scale,
            AttachmentIndex = 1,
            HitBox = 7,
        };

    [Fact]
    public void ATracerWithNoViewerDrawsNothing()
    {
        var rig = new Rig();
        rig.Fx.LocalPlayerAnswer = 0;

        new TracerEffect(rig.Tracers, rig.Factory).Dispatch(Shot());

        Assert.True(rig.List.Count == 0 && rig.Fx.Randoms.Count == 0, "stock's NULL-player return, before any draw");
    }

    [Fact]
    public void ATracerFromSomeoneElseIsAStreakAtTheDefaultSpeed()
    {
        var rig = new Rig();
        rig.Fx.LocalPlayerAnswer = 1;
        rig.Fx.Randoms.Enqueue((64f, 128f, 100f));
        rig.Fx.Randoms.Enqueue((0.75f, 0.9f, 0.8f));

        new TracerEffect(rig.Tracers, rig.Factory).Dispatch(Shot(entIndex: 5, scale: 0f));

        Assert.True(rig.List.Count == 1 && rig.List[0] is FxDiscreetLine, "one streak (FX_Tracer's discreet line)");
        var s = ((FxDiscreetLine)rig.List[0]!).State;
        Assert.True(s.Velocity == TracerSupport.TracerSpeed, "a zero scale is TRACER_SPEED, 5000");
        Vector shot = new(1000f, 0f, 0f);
        float dist = FxMath.Normalize(ref shot);
        Assert.True(s.Length == 100f && s.Scale == 0.8f && s.Clip == dist,
                    "with FX_Tracer's draws and the shot's length (VectorNormalize's answer) as its clip");
        Assert.True(rig.Fx.Sounds.Count == 0, "and no near-miss without TRACER_FLAG_WHIZ");
    }

    [Fact]
    public void TracersOffDrawNothing()
    {
        var rig = new Rig { Draw = false };
        rig.Fx.LocalPlayerAnswer = 1;

        new TracerEffect(rig.Tracers, rig.Factory).Dispatch(Shot());
        new ParticleTracerEffect(rig.Tracers, rig.Factory, rig.Fx).Dispatch(Shot());

        Assert.True(rig.List.Count == 0 && rig.Fx.Particles.Count == 0, "r_drawtracers 0");
    }

    [Fact]
    public void TheLocalPlayersOwnShotIsAPlayerTracerOffTheRightOfTheEye()
    {
        var rig = new Rig();
        rig.Fx.LocalPlayerAnswer = 5;
        rig.Fx.ViewAnglesAnswer = new QAngle(0f, 90f, 0f);
        rig.Fx.Randoms.Enqueue((-24f, 64f, 0f));
        rig.Fx.Randoms.Enqueue((0.1f, 0.6f, 0.5f));
        rig.Fx.Randoms.Enqueue((0.5f, 0.75f, 0.6f));

        new TracerEffect(rig.Tracers, rig.Factory).Dispatch(Shot(entIndex: 5));

        MoveVec.AngleVectors(new Vector(0f, 90f, 0f), out _, out Vector right, out _);
        Vector foo = FxMath.MA(new Vector(0f, 0f, 64f), 4, right);
        foo.Z -= 0.5f;
        Vector dir = FxMath.Sub(new Vector(1000f, 0f, 64f), foo);
        float length = FxMath.Normalize(ref dir);
        Vector dStart = FxMath.MA(foo, 8 + 0f, dir);

        Assert.True(rig.List.Count == 1 && rig.List[0] is FxStaticLine, "FX_PlayerTracer's static streak, not a discreet line");
        var s = ((FxStaticLine)rig.List[0]!).State;
        Assert.True(s.Start.X == dStart.X && s.Start.Y == dStart.Y && s.Start.Z == dStart.Z,
                    "starting 8 units along the shot from four units right of the eye and half a unit down");
        Assert.True(length > 256, "a long enough shot");
    }

    [Fact]
    public void TheOriginIsTheAttachmentWhenTheEffectAsksForIt()
    {
        var rig = new Rig(developer: "1");
        rig.Fx.AttachmentAnswer = 1;
        rig.Fx.AttachmentOrigin = new Vector(5f, 6f, 7f);
        Assert.True(rig.Tracers.Origin(Shot(flags: TracerSupport.FlagUseAttachment)) is { X: 5f, Y: 6f, Z: 7f }, "the attachment");

        rig.Fx.AttachmentAnswer = 0;
        Assert.True(rig.Tracers.Origin(Shot(flags: TracerSupport.FlagUseAttachment)) is { X: 0f, Y: 0f, Z: 64f },
                    "the dispatch's start when the model has no such attachment");
        Assert.True(rig.Console.Last("Console.Msg")!.Value.ArgAs<string>(0)
                    == "GetTracerOrigin: Couldn't find attachment 1 on model models/test.mdl\n", "with stock's DevMsg");

        rig.Fx.AttachmentAnswer = -1;
        Assert.True(rig.Tracers.Origin(Shot(flags: TracerSupport.FlagUseAttachment)) is { X: 0f, Y: 0f, Z: 64f },
                    "and the start when there is no renderable");
        Assert.True(rig.Tracers.Origin(Shot(flags: 0)) is { X: 0f, Y: 0f, Z: 64f }, "and always without the flag");
    }

    [Fact]
    public void AParticleTracerDispatchesItsSystemAlongTheShot()
    {
        var rig = new Rig();
        rig.Fx.LocalPlayerAnswer = 1;
        rig.Fx.Listener = new Vector(500f, 1f, 64f);
        rig.Fx.Now = 3f;
        rig.Fx.Randoms.Enqueue((0.1f, 0.1f, 0.1f));

        new ParticleTracerEffect(rig.Tracers, rig.Factory, rig.Fx).Dispatch(Shot(flags: TracerSupport.FlagWhiz));

        Assert.True(rig.Fx.Particles.Count == 1, "one particle system");
        var p = rig.Fx.Particles[0];
        Assert.True(p.System == 7 && p.Ent == 5, "the system index the hitbox field carries, owned by the shooter");
        Assert.True(p.Start.X == 0f && p.End.X == 1000f && p.Angles.Yaw == 0f && p.Angles.Pitch == 0f, "from start to end, pointing along it");
        Assert.True(rig.Fx.Sounds.Count == 1, "and the near-miss, which TRACER_FLAG_WHIZ asks for");
    }

    [Fact]
    public void TracerSoundTakesItsTypeFromTheFlags()
    {
        var rig = new Rig();
        rig.Fx.Listener = new Vector(500f, 1f, 64f);
        rig.Fx.Now = 3f;
        rig.Fx.Randoms.Enqueue((0.1f, 0.1f, 0.1f));

        new TracerSoundEffect(rig.Tracers, rig.Factory).Dispatch(Shot(flags: ClientSideEffectFactory.TracerTypeGunship));

        Assert.True(rig.Fx.Sounds.Count == 1 && rig.Fx.Sounds[0].Name == "Bullets.GunshipNearmiss", "the gunship's near-miss");
    }

    // ---- the dispatch table's managed half ------------------------------------------

    private sealed class Natives(params string[] names) : IClientEffectRegistrations
    {
        public readonly List<int> Runs = [];
        public int Count => names.Length;
        public string? Name(int index) => index < names.Length ? names[index] : null;
        public void Run(int index, in EffectData data) => Runs.Add(index);
    }

    private sealed class Handler(string name) : IClientEffectHandler
    {
        public int Runs;
        public string EffectName => name;
        public void Dispatch(in EffectData data) => Runs++;
    }

    [Fact]
    public void AManagedHandlerAnswersBeforeTheNativeRegistrations()
    {
        var natives = new Natives("Tracer", "Smoke");
        RecordingEngine console = TestServices.Fake();
        var handler = new Handler("Tracer");
        var dispatch = new ClientEffectDispatch(natives, console, new ClientEffectHandlers([handler]));

        dispatch.Dispatch("tracer", new EffectData());
        dispatch.Dispatch("Smoke", new EffectData());

        Assert.True(handler.Runs == 1 && natives.Runs.SequenceEqual([1]), "Tracer ran in C#; Smoke still ran natively");
        Assert.True(dispatch.Ran == 2 && dispatch.Unclaimed == 0, "both counted as ran");
    }

    // ---- reload ----------------------------------------------------------------------

    [Fact]
    public void LiveEffectsCrossAManagedReloadMidLife()
    {
        var before = new Rig();
        before.Fx.ViewAnswer = new FxView(new Vector(0f, -100f, 0f), new Vector(0f, 1f, 0f), 1280);
        before.Factory.AddLine(new FxLineData
        {
            Start = new Vector(0f, 0f, 0f), End = new Vector(10f, 0f, 0f), StartVelocity = new Vector(0f, 0f, 5f),
            StartAlpha = 1f, EndAlpha = 0.2f, StartScale = 1f, EndScale = 3f, DieTime = 1f, Material = before.Fx.Id("effects/spark"),
        });
        before.Factory.AddQuad(new Vector(0f, 0f, 10f), new Vector(0f, 0f, 1f), 4f, 20f, 0.3f, 1f, 0.1f, 0.4f, 10f, 30f,
                               new Vector(1f, 0.5f, 0.5f), 1.5f, "effects/blood_core", FxQuadFlags.BiasScale | FxQuadFlags.ColorFade);
        before.Factory.AddStaticLine(new Vector(0f, 0f, 0f), new Vector(0f, 50f, 0f), 2f, 1f, "effects/spark", FxStaticLineFlags.FlipVertical);
        before.Factory.AddDiscreetLine(new Vector(0f, 0f, 0f), new Vector(1f, 0f, 0f), 2000f, 64f, 500f, 0.8f, 0.5f, "effects/spark");
        before.List.DrawEffects(0.05);

        var writer = new ReloadWriter();
        before.List.Save(writer);

        var after = new Rig();
        after.Fx.ViewAnswer = before.Fx.ViewAnswer;
        foreach (var (name, id) in before.Fx.Ids)
            Assert.True(after.Fx.Id(name) == id, "the same native material ids on both sides of the swap");
        after.List.Restore(new ReloadReader(writer.ToArray()));

        Assert.True(after.List.Count == 4, "the four effects, still alive");
        Assert.True(after.Fx.Refs.Values.All(r => r == 0), "and no material reference taken again");

        int drawsBefore = before.Fx.Draws.Count;
        before.List.DrawEffects(0.05);
        after.List.DrawEffects(0.05);
        var a = before.Fx.Draws.Skip(drawsBefore).ToList();
        var b = after.Fx.Draws;
        Assert.True(a.Count == b.Count && a.Count == 4, "the next frame draws the same four meshes");
        for (int i = 0; i < a.Count; i++)
            Assert.True(a[i].Material == b[i].Material && a[i].Vertices.SequenceEqual(b[i].Vertices),
                        $"mesh {i} is the same after the reload as it would have been without one");
    }

    // ---- the native entries ----------------------------------------------------------

    [Fact]
    public unsafe void TheNativeEntriesReachTheListWithTheirArguments()
    {
        var rig = new Rig();
        var host = new ClientEffectListHost(rig.List, rig.Factory);
        var binding = new FxClientBinding(new Crossings(), new Services(host));
        int spark = rig.Fx.Id("effects/spark");

        float* line = stackalloc float[17];
        for (int i = 0; i < 17; i++)
            line[i] = i + 1;
        binding.AddLine(line, spark);

        float* quad = stackalloc float[19];
        for (int i = 0; i < 19; i++)
            quad[i] = 100 + i;
        binding.AddQuad(quad, 5u, spark);

        Assert.True(rig.List.Count == 2, "a line and a quad");
        FxLineData d = ((FxLine)rig.List[0]!).Data;
        Assert.True(d.Start.X == 1 && d.End.X == 4 && d.StartVelocity.X == 7 && d.EndVelocity.X == 10
                    && d.StartAlpha == 13 && d.EndAlpha == 14 && d.StartScale == 15 && d.EndScale == 16 && d.DieTime == 17,
                    "FXLineData_t's seventeen floats in their order");
        FxQuadData q = ((FxQuad)rig.List[1]!).Data;
        Assert.True(q.Origin.X == 100 && q.Normal.X == 103 && q.StartScale == 106 && q.DieTime == 108 && q.LifeTime == 109
                    && q.StartAlpha == 110 && q.Color.X == 112 && q.Yaw == 115 && q.AlphaBias == 118 && (uint)q.Flags == 5u,
                    "FXQuadData_t's nineteen floats in their order");
        Assert.True(rig.Fx.Refs[spark] == 1, "the line took a reference, the quad's was the caller's");
    }

    private sealed class Services(IClientEffectListHost host) : IClientFxServices
    {
        public IClientEffectDispatch? Dispatch => null;
        public IClientEffects? Effects => null;
        public IClientEffectListHost? EffectList => host;
        public IClientSpriteHost? Sprites => null;
        public IClientSpriteTrailHost? SpriteTrails => null;
        public IClientEffectPrecacheHost? Precache => null;
        public IClientSmokeFogHost? SmokeFog => null;
        public IClientTempEntHost? TempEnts => null;
    }
}
