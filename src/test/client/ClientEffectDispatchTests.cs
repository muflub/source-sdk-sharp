using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed effect dispatch (plan_client_novalve §2 cluster 4): a name runs
/// the native registration the table picked, by walk index, with the data
/// untouched; a name that matches nothing runs nothing and is counted, with
/// stock's DevMsg at developer 1 only. And the native ingress
/// (IManagedFxClient.DispatchEffect) reaches it.
/// </summary>
public unsafe class ClientEffectDispatchTests
{
    private sealed class FakeRegistrations(params string[] names) : IClientEffectRegistrations
    {
        public readonly List<(int Index, EffectData Data)> Runs = [];
        public int NameReads;

        public int Count => names.Length;

        public string? Name(int index)
        {
            NameReads++;
            return index >= 0 && index < names.Length ? names[index] : null;
        }

        public void Run(int index, in EffectData data) => Runs.Add((index, data));
    }

    private sealed class FakeDispatch : IClientEffectDispatch, IClientFxServices, IClientEffects
    {
        public readonly List<(string Name, EffectData Data)> Calls = [];
        public readonly List<string> EffectCalls = [];

        void IClientEffectDispatch.Dispatch(string name, in EffectData data) => Calls.Add((name, data));

        IClientEffectDispatch? IClientFxServices.Dispatch => this;

        IClientEffects? IClientFxServices.Effects => this;

        IClientEffectListHost? IClientFxServices.EffectList => null;

        IClientSpriteHost? IClientFxServices.Sprites => null;

        IClientSpriteTrailHost? IClientFxServices.SpriteTrails => null;

        IClientEffectPrecacheHost? IClientFxServices.Precache => null;

        IClientSmokeFogHost? IClientFxServices.SmokeFog => null;

        IClientTempEntHost? IClientFxServices.TempEnts => null;

        void IClientEffects.Smoke(Vector origin, int modelIndex, float scale, float framerate)
            => EffectCalls.Add($"Smoke {origin.X} {origin.Y} {origin.Z} {modelIndex} {scale} {framerate}");
        void IClientEffects.Sparks(Vector position, int magnitude, int trailLength, Vector? direction)
            => EffectCalls.Add($"Sparks {position.X} {magnitude} {trailLength} {(direction is Vector d ? d.Z.ToString() : "null")}");
        void IClientEffects.Dust(Vector origin, Vector direction, float size, float speed)
            => EffectCalls.Add($"Dust {origin.Y} {direction.Z} {size} {speed}");
        void IClientEffects.MuzzleFlash(Vector origin, QAngle angles, float scale, int type)
            => EffectCalls.Add($"MuzzleFlash {origin.Z} {angles.Yaw} {scale} {type}");
        void IClientEffects.MetalSparks(Vector position, Vector direction)
            => EffectCalls.Add($"MetalSparks {position.X} {direction.Y}");
        void IClientEffects.EnergySplash(Vector position, Vector direction, bool explosive)
            => EffectCalls.Add($"EnergySplash {position.X} {direction.X} {explosive}");
        void IClientEffects.Ricochet(Vector position, Vector direction)
            => EffectCalls.Add($"Ricochet {position.Z} {direction.Z}");
        void IClientEffects.SuppressEffectsSounds(bool suppress)
            => EffectCalls.Add($"SuppressEffectsSounds {suppress}");
    }

    private static (ClientEffectDispatch Dispatch, FakeRegistrations Natives, RecordingEngine Engine) Rig(string developer = "0")
    {
        var natives = new FakeRegistrations("RPGShotDown", "Impact", "GlassImpact", "watersplash");
        RecordingEngine engine = TestServices.Fake();
        engine.Returns("Console.FindConVarValue", developer);
        return (new ClientEffectDispatch(natives, engine), natives, engine);
    }

    [Fact]
    public void ANameRunsTheRegistrationTheTablePickedWithTheDataUntouched()
    {
        var (dispatch, natives, _) = Rig();
        var data = new EffectData { Origin = new Vector(1f, 2f, 3f), Flags = 5, EntIndex = 7, Scale = 2f };

        dispatch.Dispatch("WATERSPLASH", in data);

        Assert.True(natives.Runs.Count == 1, "one registration ran");
        Assert.True(natives.Runs[0].Index == 3, "the one stock's walk picks for the name, case folded");
        Assert.True(natives.Runs[0].Data.Origin.X == 1f && natives.Runs[0].Data.Flags == 5
                    && natives.Runs[0].Data.EntIndex == 7 && natives.Runs[0].Data.Scale == 2f,
                    "with the data the dispatch carried");
        Assert.True(dispatch.Ran == 1 && dispatch.Unclaimed == 0, "counted as ran");
        Assert.True(dispatch.RunsByName["watersplash"] == 1, "under the registered spelling");
    }

    [Fact]
    public void ANameThatMatchesNothingRunsNothingAndIsCounted()
    {
        var (dispatch, natives, engine) = Rig();

        dispatch.Dispatch("NoSuchEffect", new EffectData());

        Assert.True(natives.Runs.Count == 0, "nothing ran");
        Assert.True(dispatch.Unclaimed == 1 && dispatch.Ran == 0, "counted unclaimed");
        Assert.True(engine.CountOf("Console.Msg") == 0, "and silent below developer 1, as stock's DevMsg is");
    }

    [Fact]
    public void AnUnclaimedNameSaysSoAtDeveloperOne()
    {
        var (dispatch, _, engine) = Rig(developer: "1");

        dispatch.Dispatch("NoSuchEffect", new EffectData());

        Assert.True(engine.CountOf("Console.Msg") == 1, "one line at developer 1");
        Assert.True(engine.Last("Console.Msg")!.Value.ArgAs<string>(0)
                    == "DispatchEffect: effect 'NoSuchEffect' not found on client\n",
                    "spelt as stock spells it");
    }

    [Fact]
    public void TheNativeListIsReadOncePerLoad()
    {
        var (dispatch, natives, _) = Rig();

        dispatch.Dispatch("Impact", new EffectData());
        dispatch.Dispatch("Impact", new EffectData());

        Assert.True(natives.NameReads == 4, $"four names read once, read {natives.NameReads}");
        Assert.True(natives.Runs.Count == 2 && natives.Runs.All(r => r.Index == 1), "both ran Impact");
    }

    [Fact]
    public void TheNativeIngressReachesTheTableWithTheNameAndData()
    {
        var target = new FakeDispatch();
        var crossings = new Crossings();
        var binding = new FxClientBinding(crossings, target);
        byte[] name = Encoding.UTF8.GetBytes("Impact\0");
        var data = new EffectData { Magnitude = 4f, SurfaceProp = 12 };

        fixed (byte* pName = name)
            binding.DispatchEffect(pName, &data);

        Assert.True(target.Calls.Count == 1 && target.Calls[0].Name == "Impact", "the name arrived");
        Assert.True(target.Calls[0].Data.Magnitude == 4f && target.Calls[0].Data.SurfaceProp == 12, "and the data");
        Assert.True(crossings.Read(CrossingOp.ClientDispatchEffectIn) == 1, "and the crossing was counted");
    }

    [Fact]
    public void ANativeIngressWithNoNameDispatchesNothing()
    {
        var target = new FakeDispatch();
        var binding = new FxClientBinding(new Crossings(), target);
        var data = new EffectData();

        binding.DispatchEffect(null, &data);

        Assert.Empty(target.Calls);
    }

    [Fact]
    public void TheIEffectsIngressCarriesEachCallWithItsArguments()
    {
        var target = new FakeDispatch();
        var crossings = new Crossings();
        var binding = new FxClientBinding(crossings, target);
        float* a = stackalloc float[] { 1f, 2f, 3f };
        float* b = stackalloc float[] { 4f, 5f, 6f };

        binding.Smoke(a, 7, 1.5f, 2.5f);
        binding.Sparks(a, 2, 3, null);
        binding.Sparks(a, 2, 3, b);
        binding.Dust(a, b, 8f, 9f);
        binding.MuzzleFlash(a, b, 1.25f, 4);
        binding.MetalSparks(a, b);
        binding.EnergySplash(a, b, 1);
        binding.Ricochet(a, b);
        binding.SuppressEffectsSounds(1);

        Assert.Equal(
            [
                "Smoke 1 2 3 7 1.5 2.5", "Sparks 1 2 3 null", "Sparks 1 2 3 6", "Dust 2 6 8 9",
                "MuzzleFlash 3 5 1.25 4", "MetalSparks 1 5", "EnergySplash 1 4 True", "Ricochet 3 6",
                "SuppressEffectsSounds True",
            ],
            target.EffectCalls);
        Assert.True(crossings.Read(CrossingOp.ClientFxSparks) == 2 && crossings.Read(CrossingOp.ClientFxSmoke) == 1,
                    "each call counted its crossing");
    }
}
