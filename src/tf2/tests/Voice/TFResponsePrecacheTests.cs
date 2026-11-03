using SourceSharp.Abi;
using SourceSharp.Tests;

using TF2.Server.Voice;

using Xunit;

namespace TF2.Tests.Voice;

/// <summary>
/// The response rules' level precache (CResponseSystem::Precache,
/// AI_ResponseSystem.cpp:1831-1894): every scene response's sounds through the
/// image, every speak response's sound script, all of it through
/// PrecacheScriptSound — as stock's PrecacheInstancedScene and
/// CBaseEntity::PrecacheScriptSound were — when the level init asks.
/// </summary>
public sealed class TFResponsePrecacheTests
{
    private sealed class StagedRules(params (ResponseKind Kind, string Value)[] responses) : IResponseRules
    {
        public IEnumerable<(ResponseKind Kind, string Value)> Responses { get; } = responses;

        public bool FindBestResponse(ResponseCriteriaSet criteria, out MatchedResponse response, IResponseFilter? filter = null)
        {
            response = default!;
            return false;
        }
    }

    private static (TFResponsePrecache Precache, RecordingEngine Engine) Rig(
        (ResponseKind Kind, string Value)[] responses,
        params (string Scene, int Msecs, string[] Sounds)[] scenes)
    {
        TestServices.Services(out var engine);
        var rules = new StagedRules(responses);
        var image = new TFSceneImage(TFSceneImageTests.Files(
            ("scenes/scenes.image", TFSceneImageTests.Image(scenes))));
        return (new TFResponsePrecache(rules, image, engine), engine);
    }

    private static string[] Precached(RecordingEngine engine)
        => engine.Calls.Where(c => c.Member == "Precache.PrecacheScriptSound")
                       .Select(c => Assert.IsType<string>(c.Args[0])).ToArray();

    private static void Load(TFResponsePrecache precache)
        => ((IGameLifecycle)precache).OnLevelInitPreEntity("ctf_2fort");

    [Fact]
    public void A_scene_response_precaches_every_sound_the_image_holds_for_it()
    {
        var (precache, engine) = Rig(
            [(ResponseKind.Scene, "scenes/Player/Medic/low/611")],
            ("scenes/Player/Medic/low/611.vcd", 1730, ["Medic.Medic01", "Medic.MedicBlip"]));

        Load(precache);

        Assert.Equal(["Medic.Medic01", "Medic.MedicBlip"], Precached(engine));
    }

    [Fact]
    public void A_gender_scene_precaches_both_of_its_variants()
    {
        var (precache, engine) = Rig(
            [(ResponseKind.Scene, "scenes/Player/$gender/low/1")],
            ("scenes/Player/male/low/1.vcd", 100, ["Male.Line"]),
            ("scenes/Player/female/low/1.vcd", 110, ["Female.Line"]));

        Load(precache);

        Assert.Equal(["Male.Line", "Female.Line"], Precached(engine));
    }

    [Fact]
    public void A_speak_response_precaches_its_sound_script_by_name()
    {
        var (precache, engine) = Rig([(ResponseKind.Speak, "Medic.Medic01")]);

        Load(precache);

        Assert.Equal(["Medic.Medic01"], Precached(engine));
    }

    [Fact]
    public void An_unheld_scene_and_an_unmanned_kind_precache_nothing()
    {
        var (precache, engine) = Rig(
            [(ResponseKind.Scene, "scenes/Player/Medic/low/999"), (ResponseKind.None, "Medic.Medic01")]);

        Load(precache);

        Assert.Empty(Precached(engine));
    }
}
