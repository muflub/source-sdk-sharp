using SourceSharp.Abi;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.TempEnts;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A weapon's muzzle-flash animation event draws the temp entities' particle
/// flash, as stock's C_BaseAnimating did (lane c5-particles), and still lights
/// the dlight: the named <c>AE_MUZZLEFLASH</c> / <c>AE_NPC_MUZZLEFLASH</c>
/// through <c>DispatchMuzzleEffect</c> (the type, the entity's own handle, the
/// named attachment, the person), the numbered <c>CL_EVENT_*MUZZLEFLASH*</c>
/// through <c>FireObsoleteEvent</c> (the world-space flash on the view entity).
/// </summary>
public class AnimationMuzzleFlashRouteTests
{
    /// <summary>The animation events' sounds: none of these facts is about one.</summary>
    private sealed class NoSounds : SourceSharp.Client.Sounds.IClientEntitySounds
    {
        public void EmitFrom(EntityHandle entity, string soundName, Vector? origin = null)
        {
        }
    }

    private sealed class Flashes : ILegacyMuzzleFlashes
    {
        public readonly List<string> Calls = [];

        public void MuzzleFlash(int type, uint handle, int attachment, bool firstPerson)
            => Calls.Add($"attached {type} {handle} {attachment} {firstPerson}");

        public void MuzzleFlashAt(int type, uint handle, bool firstPerson) => Calls.Add($"world {type} {handle} {firstPerson}");
    }

    private sealed class View(uint handle) : IViewEntity
    {
        public uint ViewEntity() => handle;
    }

    private sealed class Brass : ILegacyBrass
    {
        public readonly List<(Vector Origin, QAngle Angles, QAngle Gun, int Type)> Calls = [];

        public void EjectBrass(in Vector origin, in QAngle angles, in QAngle gunAngles, int type) => Calls.Add((origin, angles, gunAngles, type));
    }

    private static readonly EntityHandle s_viewModel = new(0x00030044);

    private static readonly Brass s_noBrass = new();

    private static (AnimationEventEffects Effects, Flashes Flashes, RecordingEngine Engine) Make(bool hasAttachment = true)
        => Make(s_noBrass, new FxTestRig(), hasAttachment);

    private static (AnimationEventEffects Effects, Flashes Flashes, RecordingEngine Engine) Make(Brass brass, FxTestRig view,
                                                                                               bool hasAttachment = true)
    {
        var engine = new RecordingEngine { Strict = false };
        engine.Returns("EntityRender.LookupAttachment", hasAttachment ? 3 : 0);
        engine.Returns("EntityRender.GetAttachment", hasAttachment);
        engine.Returns("EntityRender.GetAttachment.origin", new Vector(1, 2, 3));
        engine.Returns("EntityRender.GetAttachment.angles", new Vector(10, 20, 30));
        engine.Returns("Entities.GetOrigin", new Vector(0, 0, 0));
        engine.Returns("Entities.GetAngles", new Vector(4, 5, 6));
        var flashes = new Flashes();
        return (new AnimationEventEffects(engine, engine, new NoSounds(), engine, flashes, new View(0x00010001), brass, view), flashes, engine);
    }

    private static AnimationEventFired Named(string name, string options)
        => new(s_viewModel, "ss_viewmodel", 2, new ModelAnimationEvent(0, 0f, 0, AnimationEventTypes.NewEventSystem, name, options));

    private static AnimationEventFired Numbered(int id, string options)
        => new(s_viewModel, "ss_viewmodel", 2, new ModelAnimationEvent(0, 0f, id, 0, string.Empty, options));

    [Fact]
    public void A_view_models_named_flash_draws_on_its_own_attachment_first_person()
    {
        var (effects, flashes, engine) = Make();
        effects.Fire(Named("AE_MUZZLEFLASH", "PISTOL muzzle"));
        Assert.Equal([$"attached 4 {s_viewModel.Value} 3 True"], flashes.Calls);
        Assert.Contains(engine.Calls, c => c.Member == "Visibility.DynamicLightUpdate");
    }

    [Fact]
    public void A_third_person_named_flash_is_not_first_person()
    {
        var (effects, flashes, _) = Make();
        effects.Fire(Named("AE_NPC_MUZZLEFLASH", "SMG1 muzzle"));
        Assert.Equal([$"attached 2 {s_viewModel.Value} 3 False"], flashes.Calls);
    }

    [Fact]
    public void A_numbered_flash_draws_the_world_path_on_the_view_entity()
    {
        var (effects, flashes, _) = Make();
        effects.Fire(Numbered(5011, "2"));
        Assert.Equal(["world 2 65537 True"], flashes.Calls);
        effects.Fire(Numbered(5003, "4"));
        Assert.Equal(["world 2 65537 True", "world 4 65537 False"], flashes.Calls);
    }

    [Fact]
    public void No_attachment_no_flash()
    {
        var (effects, flashes, _) = Make(hasAttachment: false);
        effects.Fire(Named("AE_MUZZLEFLASH", "SMG1 muzzle"));
        effects.Fire(Numbered(5001, "2"));
        Assert.Empty(flashes.Calls);
    }

    /// <summary>
    /// <c>CL_EVENT_EJECTBRASS1</c>: stock's C_BaseAnimating threw a shell from
    /// attachment 2 at that attachment's angles, the entity's own angles as the
    /// gun's, <c>atoi( options )</c> its type, when the view was within 256
    /// units of the entity.
    /// </summary>
    [Fact]
    public void The_brass_event_throws_a_shell_from_attachment_two()
    {
        var brass = new Brass();
        var (effects, _, engine) = Make(brass, new FxTestRig { Listener = new Vector(100, 0, 0) });
        effects.Fire(Numbered(6001, "1"));
        Assert.Equal([(new Vector(1, 2, 3), new QAngle(10, 20, 30), new QAngle(4, 5, 6), 1)], brass.Calls);
        Assert.Contains(engine.Calls, c => c.Member == "EntityRender.GetAttachment" && (int)c.Args[1]! == 2);
    }

    [Fact]
    public void No_shell_when_the_view_is_256_units_away_or_the_attachment_is_missing()
    {
        var brass = new Brass();
        var (far, _, _) = Make(brass, new FxTestRig { Listener = new Vector(256, 0, 0) });
        far.Fire(Numbered(6001, "0"));
        var (none, _, _) = Make(brass, new FxTestRig(), hasAttachment: false);
        none.Fire(Numbered(6001, "0"));
        Assert.Empty(brass.Calls);
    }
}
