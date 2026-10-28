using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// worldspawn's arrival on the client, from stock's <c>C_World::OnDataChanged</c>
/// (c_world.cpp): on the create, the normal client mode, a start-dark map's
/// fade in from black, and the renderer's occlusion areas and prop fade range.
/// </summary>
public class WorldArrivalTests
{
    private sealed class Fades
    {
        public readonly List<(int Duration, int Hold, FadeFlags Flags)> Seen = [];

        public void Fade(int duration, int hold, FadeFlags flags) => Seen.Add((duration, hold, flags));
    }

    private static byte[] Blob(bool startDark = false, float maxOccludee = 0f, float minOccluder = 0f,
                               float minPropWidth = 0f, float maxPropWidth = 0f)
    {
        var state = new WorldNetState();
        state.BindLocal();
        state.StartDark = startDark;
        state.MaxOccludeeArea = maxOccludee;
        state.MinOccluderArea = minOccluder;
        state.MinPropScreenSpaceWidth = minPropWidth;
        state.MaxPropScreenSpaceWidth = maxPropWidth;
        return state.Snapshot();
    }

    private static (WorldArrivalActions Actions, float[] Plan, Fades Fades) Arrive(byte[] blob, DataUpdateType type)
    {
        var fades = new Fades();
        var plan = new float[WorldArrival.PlanFloats];
        WorldArrivalActions actions = WorldArrival.Over(fades.Fade).OnDataChanged(blob, type, plan);
        return (actions, plan, fades);
    }

    [Fact]
    public void TheCreateForcesTheNormalMode()
        => Assert.True(Arrive(Blob(), DataUpdateType.Created).Actions.HasFlag(WorldArrivalActions.NormalMode));

    [Fact]
    public void TheCreateHandsTheRendererTheOcclusionAreas()
    {
        var (actions, plan, _) = Arrive(Blob(maxOccludee: 300f, minOccluder: 70f), DataUpdateType.Created);

        Assert.True(actions.HasFlag(WorldArrivalActions.Occlusion));
        Assert.Equal(300f, plan[0]);
        Assert.Equal(70f, plan[1]);
    }

    [Fact]
    public void TheCreateHandsTheRendererThePropFadeRange()
    {
        var (actions, plan, _) = Arrive(Blob(minPropWidth: 0.7f, maxPropWidth: 4f), DataUpdateType.Created);

        Assert.True(actions.HasFlag(WorldArrivalActions.ScreenFadeRange));
        Assert.Equal(0.7f, plan[2]);
        Assert.Equal(4f, plan[3]);
    }

    [Fact]
    public void AStartDarkMapFadesInFromBlackAfterAHold()
    {
        var (_, _, fades) = Arrive(Blob(startDark: true), DataUpdateType.Created);

        // (1 << SCREENFADE_FRACBITS) * 5.0f and * 1.0f, FFADE_IN | FFADE_PURGE.
        Assert.Equal([(5 * 512, 512, FadeFlags.In | FadeFlags.Purge)], fades.Seen);
    }

    [Fact]
    public void AMapThatDoesNotStartDarkDoesNotFade()
        => Assert.Empty(Arrive(Blob(startDark: false), DataUpdateType.Created).Fades.Seen);

    [Fact]
    public void AnUpdateAfterTheCreateDoesNothing()
    {
        var (actions, _, fades) = Arrive(Blob(startDark: true, maxOccludee: 300f), DataUpdateType.DataTableChanged);

        Assert.Equal(WorldArrivalActions.None, actions);
        Assert.Empty(fades.Seen);
    }

    [Fact]
    public void APlanTooShortForTheRenderersValuesOnlyResetsTheMode()
    {
        var fades = new Fades();
        WorldArrivalActions actions = WorldArrival.Over(fades.Fade).OnDataChanged(Blob(), DataUpdateType.Created, new float[2]);

        Assert.Equal(WorldArrivalActions.NormalMode, actions);
    }

    [Fact]
    public void TheModuleRoutesTheWorldsUpdateToTheArrival()
    {
        string module = File.ReadAllText(RepoLayout.Repo("src", "client", "managed", "ClientModule.cs"));

        Assert.Contains("_services.Require<WorldArrival>().OnDataChanged(blob, (DataUpdateType)updateType, plan)", module);
        Assert.Contains("_services.AddInstance(WorldArrival.Over(", module);
    }

    // ---- the binding -------------------------------------------------------

    private sealed class Host : IClientRuntimeHost
    {
        public int Seen;
        public int Type = -1;
        public void Frame(float frameTime) { }
        public void OnStage(int stage) { }
        public void LevelInit(string mapName) { }
        public void LevelShutdown() { }
        public void InvalidateMdlCache() { }
        public bool ParticlesNeedFullReload(int previousMapParticleFiles) => true;
        public void LevelInitPreEntity() { }
        public void PostInit(int adapterMaxDxLevel) { }
        public string? MapMaterialReloadSet(string nextBsp, string previousBsp) => null;
        public void MapMaterialSearchPath(string? path, string? pathId, bool addToHead) { }
        public void MapMaterialMapAccess(bool open) { }
        public string? MapMaterialReloadPlan(string? subString) => null;
        public int WorldDataChanged(ReadOnlySpan<byte> blob, int updateType, Span<float> plan)
        {
            Seen = blob.Length;
            Type = updateType;
            plan[0] = 42f;
            return 7;
        }
    }

    [Fact]
    public unsafe void TheBindingHandsTheBlobOverAndCountsTheCrossing()
    {
        var host = new Host();
        var crossings = new Crossings();
        var log = new RecordingLog();
        var models = new ModelDescriptors(_ => -1, _ => string.Empty, log);
        var binding = new ClientRuntimeBinding(crossings, log, host, new StandaloneMirror(), models,
                                               new BreakModels(_ => -1, _ => -1, _ => null, models, log));
        byte[] blob = new byte[16];
        float* plan = stackalloc float[4];
        int r;
        fixed (byte* pb = blob)
            r = binding.WorldDataChanged(pb, blob.Length, (int)DataUpdateType.Created, plan, 4);

        Assert.Equal(7, r);
        Assert.Equal(16, host.Seen);
        Assert.Equal((int)DataUpdateType.Created, host.Type);
        Assert.Equal(42f, plan[0]);
        Assert.Equal(1, crossings.Read(CrossingOp.ClientWorldDataChanged));
    }
}
