using SourceSharp.Ai.TF2.Senses;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The voice menu, in (plan_bots_teams_tf2_part_g G.2.4, <see cref="TFVoiceTap"/>): a voice line's
/// sound, as the game's scripts name it, is the callout a bot's client would hear, told to the
/// bots the sound is sent to and to nobody else.
/// </summary>
public sealed class TFVoiceTapTests
{
    private static readonly ActorId A = new(2, 1), B = new(3, 2), C = new(4, 3);
    private static readonly ActorId Human = new(1, 0x4000_0005);

    [Theory]
    [InlineData("Soldier.Medic02", CalloutKind.Help, 1)]
    [InlineData("Medic.Medic01", CalloutKind.Help, 1)]
    [InlineData("Heavy.HelpMe03", CalloutKind.Help, 0)]
    [InlineData("Engineer.SentryAhead01", CalloutKind.Hazard, 0)]
    public void AVoiceLinesSoundIsItsCallout(string sound, CalloutKind kind, int count)
        => Assert.Equal((kind, count), TFVoiceTap.LineOf(sound));

    /// <summary>A Medic's other lines, a weapon's sounds, a raw wave and a line this tap does not know are nobody's call.</summary>
    [Theory]
    [InlineData("Medic.Go01")]
    [InlineData("Medic.Thanks02")]
    [InlineData("Weapon_Medigun.Healing")]
    [InlineData("Soldier.MedicFollow01")]
    [InlineData("Soldier.Medic")]
    [InlineData("vo/soldier_medic01.mp3")]
    [InlineData("Soldier.PainSevere01")]
    [InlineData("Building_Sentrygun.Alert")]
    public void AnyOtherSoundIsNoCall(string sound)
        => Assert.Equal(CalloutKind.None, TFVoiceTap.LineOf(sound).Kind);

    [Fact]
    public void TheCallIsToldToTheBotsTheSoundIsSentToAndNotToItsSpeaker()
    {
        var into = new List<AiEvent>();
        var at = new AiVector(100f, 200f, 0f);
        TFVoiceTap.Tell(A, at, CalloutKind.Help, 1, [A.Slot, B.Slot, Human.Slot], [A, B, C], new Tick(9), into);
        AiEvent heard = Assert.Single(into);
        Assert.Equal((AiEventKind.Callout, B, A, CalloutKind.Help, 1, at), (heard.Kind, heard.Bot, heard.Other, heard.CalloutKind, heard.CalloutCount, heard.Position));
    }

    /// <summary>A person's line reaches the bots as a bot's does.</summary>
    [Fact]
    public void APersonsCallIsToldToEveryBotThatHearsIt()
    {
        var into = new List<AiEvent>();
        TFVoiceTap.Tell(Human, default, CalloutKind.Hazard, 0, [A.Slot, C.Slot], [A, B, C], new Tick(9), into);
        Assert.Equal([A, C], into.Select(e => e.Bot));
    }

    /// <summary>The memory keeps a call from a teammate and drops an enemy's: the tap need not know anyone's team.</summary>
    [Fact]
    public void TheMemoryKeepsATeammatesCallAndDropsAnEnemys()
    {
        var f = new TFField();
        ActorId friend = new(4, 0x4000_0002), foe = new(5, 0x4000_0001);
        f.Memory.Apply(AiEvent.ActorTeam(f.Bot, new Tick(1), friend, TFField.Red, 0));
        f.Memory.Apply(AiEvent.ActorTeam(f.Bot, new Tick(1), foe, TFField.Blue, 0));
        f.StepHearing(TFField.Self(new AiVector(2048f, 256f, 0f)), TFField.Ctf(), []);
        var into = new List<AiEvent>();
        TFVoiceTap.Tell(foe, new AiVector(1f, 0f, 0f), CalloutKind.Help, 0, [f.Bot.Slot], [f.Bot], new Tick(20), into);
        f.Memory.Apply(into[0]);
        Assert.Equal(CalloutKind.None, f.Memory.LastHelpCall.Kind);
        into.Clear();
        TFVoiceTap.Tell(friend, new AiVector(2f, 0f, 0f), CalloutKind.Help, 1, [f.Bot.Slot], [f.Bot], new Tick(21), into);
        f.Memory.Apply(into[0]);
        Assert.Equal((CalloutKind.Help, friend, 1), (f.Memory.LastHelpCall.Kind, f.Memory.LastHelpCall.Speaker, f.Memory.LastHelpCall.Count));
    }

    [Fact]
    public void AHazardCallIsKeptApartFromAHelpCall()
    {
        var f = new TFField();
        ActorId friend = new(4, 0x4000_0002);
        f.Memory.Apply(AiEvent.ActorTeam(f.Bot, new Tick(1), friend, TFField.Red, 0));
        f.StepHearing(TFField.Self(new AiVector(2048f, 256f, 0f)), TFField.Ctf(), []);
        var at = new AiVector(3000f, 100f, 0f);
        f.Memory.Apply(AiEvent.Callout(f.Bot, new Tick(20), friend, new SayIntent(CalloutKind.Hazard, at, 0, 0, 0, 0, false)));
        Assert.Equal((CalloutKind.Hazard, at, CalloutKind.None), (f.Memory.LastHazardCall.Kind, f.Memory.LastHazardCall.Place, f.Memory.LastHelpCall.Kind));
    }
}
