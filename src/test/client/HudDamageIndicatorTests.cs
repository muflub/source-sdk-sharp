using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Which Damage messages light the damage indicator: stock's
/// CHudDamageIndicator::MsgFunc_Damage (hl2/hud_damageindicator.cpp) ignores
/// one with no origin unless it is drowning damage, and starts no animation
/// when neither health nor armour was taken.
/// </summary>
public class HudDamageIndicatorTests
{
    private static (Hud Hud, DamageMessage Damage) Subscribed()
    {
        var damage = new DamageMessage();
        var hud = new Hud(new RecordingLog(), Fake().Returns("Clock.Now", 5f), Fake(), damage, new HudMsgMessage(),
                          new HudTextMessage(), new KeyHintTextMessage(), new BuildInfo());
        hud.Subscribe();
        return (hud, damage);
    }

    // armour byte, health byte, damage bits (long), origin (three floats): stock's layout.
    private static byte[] Payload(int armour, int health, int bits, float x, float y, float z)
    {
        var b = new List<byte> { (byte)armour, (byte)health };
        b.AddRange(BitConverter.GetBytes(bits));
        b.AddRange(BitConverter.GetBytes(x));
        b.AddRange(BitConverter.GetBytes(y));
        b.AddRange(BitConverter.GetBytes(z));
        return [.. b];
    }

    private static bool Lit(byte[] payload)
    {
        var (hud, damage) = Subscribed();
        ((IUserMessage)damage).OnReceived(new UserMessageReader(payload));
        return hud.DamageFraction(5f) > 0f;
    }

    [Fact]
    public void Damage_from_a_place_lights_it()
        => Assert.True(Lit(Payload(0, 20, (int)DamageType.Bullet, 100f, 50f, 0f)));

    [Fact]
    public void Damage_with_no_origin_is_ignored()
        => Assert.False(Lit(Payload(0, 20, (int)DamageType.Fall, 0f, 0f, 0f)));

    [Fact]
    public void Drowning_with_no_origin_still_lights_it()
        => Assert.True(Lit(Payload(0, 10, (int)DamageType.Drown, 0f, 0f, 0f)));

    [Fact]
    public void A_message_that_took_neither_health_nor_armour_lights_nothing()
        => Assert.False(Lit(Payload(0, 0, (int)DamageType.Bullet, 100f, 50f, 0f)));
}
