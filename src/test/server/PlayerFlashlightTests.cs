using System.Collections.Generic;
using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The server's flashlight (PlayerFlashlight.cs): CHL2MP_Player's
/// FlashlightIsOn / TurnOn / TurnOff and CBasePlayer::ImpulseCommands'
/// impulse 100, over the EF_DIMLIGHT bit in the player's engine block.
/// </summary>
public class PlayerFlashlightTests
{
    private static readonly EntityHandle Player = new(3u);

    /// <summary>The bits, kept per entity; every Set is recorded.</summary>
    private sealed class Bits : IEntityEffectBits
    {
        public readonly Dictionary<EntityHandle, int> Words = new();
        public readonly List<(EntityHandle Entity, EntityEffect Bits, bool On)> Sets = new();

        public int Read(EntityHandle entity) => Words.GetValueOrDefault(entity);

        public void Set(EntityHandle entity, EntityEffect bits, bool on)
        {
            Sets.Add((entity, bits, on));
            int old = Read(entity);
            Words[entity] = on ? old | (int)bits : old & ~(int)bits;
        }
    }

    private static (PlayerFlashlight Light, Bits Bits, RecordingEngine Engine) Rig(bool alive = true, bool allowed = true)
    {
        var engine = Fake();
        engine.Returns("Players.IsAlive", alive);
        var bits = new Bits();
        var light = new PlayerFlashlight(bits, engine, engine) { Allowed = () => allowed };
        return (light, bits, engine);
    }

    private static List<string> Sounds(RecordingEngine engine)
        => engine.Calls.Where(c => c.Member == "Sound.EntityEmitSound").Select(c => (string)c.Args[1]!).ToList();

    [Fact]
    public void Impulse100LightsTheFlashlightThenPutsItOut()
    {
        var (light, bits, _) = Rig();

        Assert.True(light.OnImpulse(Player, 100), "impulse 100 is the flashlight's");
        Assert.True(light.IsOn(Player), "the first press lights it");
        Assert.Equal((int)EntityEffect.DimLight, bits.Read(Player));

        Assert.True(light.OnImpulse(Player, 100), "and so is the second");
        Assert.False(light.IsOn(Player), "the second press puts it out");
        Assert.Equal(0, bits.Read(Player));
    }

    [Fact]
    public void TheToggleLeavesTheOtherEffectBitsAlone()
    {
        var (light, bits, _) = Rig();
        bits.Words[Player] = (int)(EntityEffect.NoShadow | EntityEffect.NoDraw);

        light.OnImpulse(Player, 100);
        Assert.Equal((int)(EntityEffect.NoShadow | EntityEffect.NoDraw | EntityEffect.DimLight), bits.Read(Player));

        light.OnImpulse(Player, 100);
        Assert.Equal((int)(EntityEffect.NoShadow | EntityEffect.NoDraw), bits.Read(Player));
    }

    [Fact]
    public void OnPlaysTheOnSoundAndOffTheOffSound()
    {
        var (light, _, engine) = Rig();

        light.OnImpulse(Player, 100);
        light.OnImpulse(Player, 100);

        Assert.Equal(new[] { PlayerFlashlight.OnSound, PlayerFlashlight.OffSound }, Sounds(engine));
        Assert.Equal("HL2Player.FlashlightOn", PlayerFlashlight.OnSound);
        Assert.Equal("HL2Player.FlashlightOff", PlayerFlashlight.OffSound);
    }

    [Fact]
    public void ADisallowedFlashlightDoesNotLight()
    {
        var (light, bits, engine) = Rig(allowed: false);

        Assert.True(light.OnImpulse(Player, 100), "the impulse is still the flashlight's");
        Assert.False(light.IsOn(Player), "but cs_sv_allow_flashlight 0 keeps it dark");
        Assert.Empty(bits.Sets);
        Assert.Empty(Sounds(engine));
    }

    [Fact]
    public void ADisallowedFlashlightThatIsLitCanStillBePutOut()
    {
        var (light, bits, _) = Rig(allowed: false);
        bits.Words[Player] = (int)EntityEffect.DimLight;

        light.OnImpulse(Player, 100);

        Assert.False(light.IsOn(Player), "TurnOff has no allow test in stock");
    }

    [Fact]
    public void ADeadPlayerCannotLightIt()
    {
        var (light, bits, _) = Rig(alive: false);

        light.TurnOn(Player);

        Assert.False(light.IsOn(Player));
        Assert.Empty(bits.Sets);
    }

    [Fact]
    public void ADeadPlayersImpulseIsNotTheFlashlights()
    {
        var (light, bits, _) = Rig(alive: false);

        Assert.False(light.OnImpulse(Player, 100), "stock ran ImpulseCommands for the living only");
        Assert.Empty(bits.Sets);
    }

    [Fact]
    public void PuttingOutADeadPlayersLightIsSilent()
    {
        var (light, bits, engine) = Rig(alive: false);
        bits.Words[Player] = (int)EntityEffect.DimLight;

        light.TurnOff(Player);

        Assert.False(light.IsOn(Player));
        Assert.Empty(Sounds(engine));
    }

    [Fact]
    public void OtherImpulsesAreNotTheFlashlights()
    {
        var (light, bits, _) = Rig();

        foreach (int impulse in new[] { 0, 99, 101, 201 })
            Assert.False(light.OnImpulse(Player, impulse), impulse + " is not the flashlight's");
        Assert.Empty(bits.Sets);
    }

    [Fact]
    public void TheTwoSoundsArePrecached()
    {
        var declared = typeof(PlayerFlashlight).GetCustomAttributes(typeof(PrecacheAttribute), false)
            .Cast<PrecacheAttribute>()
            .Where(p => p.Kind == PrecacheKind.Sound)
            .Select(p => p.Asset)
            .OrderBy(a => a, System.StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { PlayerFlashlight.OffSound, PlayerFlashlight.OnSound }, declared);
    }

    // ---- the block word ----------------------------------------------------

    [Fact]
    public void TheWordIsTheBlockHeadsEffectsField()
        => Assert.Equal(EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Effects)), EngineBlockEffects.Offset);

    [Fact]
    public void ApplySetsAndClearsTheBitInTheEffectsWord()
    {
        var block = new byte[EngineBlock.SizeBytes];

        Assert.True(EngineBlockEffects.Apply(block, EntityEffect.DimLight, on: true), "a set that moves the word reports it");
        Assert.Equal((int)EntityEffect.DimLight, EngineBlockEffects.Read(block));
        Assert.Equal(4, block[EngineBlockEffects.Offset]);

        Assert.False(EngineBlockEffects.Apply(block, EntityEffect.DimLight, on: true), "setting a set bit changes nothing");

        Assert.True(EngineBlockEffects.Apply(block, EntityEffect.DimLight, on: false), "and the clear moves it back");
        Assert.Equal(0, EngineBlockEffects.Read(block));
        Assert.All(block, b => Assert.Equal(0, b));
    }

    [Fact]
    public void AShortSpanIsNoBlock()
    {
        var shortBlock = new byte[EngineBlock.SizeBytes - 1];

        Assert.False(EngineBlockEffects.Apply(shortBlock, EntityEffect.DimLight, on: true));
        Assert.Equal(0, EngineBlockEffects.Read(shortBlock));
        Assert.All(shortBlock, b => Assert.Equal(0, b));
    }

    [Fact]
    public void TheEngineAdapterAnnouncesAChangeAtTheEffectsOffset()
    {
        // The recorded engine answers no block, so nothing changes and nothing is
        // announced: the adapter reads before it marks.
        var engine = Fake();
        var adapter = new EngineEntityEffectBits(engine, engine);

        adapter.Set(Player, EntityEffect.DimLight, on: true);

        Assert.Equal(0, engine.CountOf("Networking.MarkEntityBlockChanged"));
        Assert.Equal(1, engine.CountOf("Entities.GetEngineBlock"));
    }
}
