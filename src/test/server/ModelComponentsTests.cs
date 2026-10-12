using System.Collections.Generic;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_novalve.md P2.4/P3: what precaching a model brings with it, found
/// in C#. Held live to stock's model sounds cache and event walk on dm_lockdown
/// (the same sounds on both sides); these pin the rules the map does not reach.
/// </summary>
public class ModelComponentsTests
{
    private const int New = ModelComponents.TypeNewEventSystem;

    private static ModelSequence Seq(params ModelSequenceEvent[] events)
        => new(0, "idle", "ACT_IDLE", 0, 1f, events);

    private static ModelSequenceEvent Ev(int id, int type, string name, string options)
        => new(0, 0f, id, type, name, options);

    [Fact]
    public void NamedSoundEventsGiveTheirOptions()
    {
        var names = ModelComponents.SoundNames(new[] { Seq(
            Ev(0, New, "AE_SV_PLAYSOUND", "Door.Open"),
            Ev(0, New, "ae_sv_exclude_player_sound", "Door.Close"),
            Ev(0, New, "AE_CL_PLAYSOUND", "Npc.Step"),
            Ev(0, New, "AE_SOMETHING_ELSE", "Ignored")) }, "m.mdl");

        Assert.Equal(new[] { "Door.Open", "Door.Close", "Npc.Step" }, names);
    }

    [Fact]
    public void AnEmptyClientSoundIsWarnedAboutAndSkipped()
    {
        var warnings = new List<string>();
        var names = ModelComponents.SoundNames(new[] { Seq(Ev(0, New, "AE_CL_PLAYSOUND", "")) }, "m.mdl", warnings.Add);

        Assert.Empty(names);
        Assert.Single(warnings);
        Assert.Contains("empty soundname", warnings[0]);
    }

    [Fact]
    public void OldSystemSoundAndFootstepIdsAreRead()
    {
        var names = ModelComponents.SoundNames(new[] { Seq(
            Ev(ModelComponents.ClEventSound, 0, "", "A.Sound"),
            Ev(ModelComponents.ScriptEventSound, 0, "", "B.Sound"),
            Ev(ModelComponents.ScriptEventSoundVoice, 0, "", "C.Sound"),
            Ev(ModelComponents.ClEventFootstepLeft, 0, "", ""),
            Ev(ModelComponents.ClEventFootstepRight, 0, "", "NPC_Metro")) }, "m.mdl");

        Assert.Equal(new[]
        {
            "A.Sound", "B.Sound", "C.Sound",
            "NPC_CombineS.RunFootstepLeft", "NPC_CombineS.RunFootstepRight",
            "NPC_CombineS.FootstepLeft", "NPC_CombineS.FootstepRight",
            "NPC_Metro.RunFootstepLeft", "NPC_Metro.RunFootstepRight",
            "NPC_Metro.FootstepLeft", "NPC_Metro.FootstepRight",
        }, names);
    }

    [Fact]
    public void AClientParticleEventGivesItsFirstToken()
    {
        var names = ModelComponents.EventParticles(new[] { Seq(
            Ev(0, New, "AE_CL_CREATE_PARTICLE_EFFECT", "fire_small follow_attachment eyes"),
            Ev(0, New, "AE_CL_CREATE_PARTICLE_EFFECT", " leading_space"),
            Ev(ModelComponents.AeClCreateParticleEffectOrdinal, 0, "", "old_system_effect"),
            Ev(6004, 0, "", "not_an_effect")) });

        // The leading-space one is an empty token and is not precached.
        Assert.Equal(new[] { "fire_small", "old_system_effect" }, names);
    }

    [Fact]
    public void TheFirstTokenIsTier1sNexttoken()
    {
        Assert.Equal("a", ModelComponents.FirstToken("a b"));
        Assert.Equal("", ModelComponents.FirstToken(" a"));
        Assert.Equal(255, ModelComponents.FirstToken(new string('x', 300)).Length);
    }
}
