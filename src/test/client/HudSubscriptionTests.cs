using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The HUD hears the user message events it was handed, and no others.
///
/// <para>
/// The four events it listens to (Damage, HudMsg, HudText, KeyHintText) were
/// static events on the handler classes, so a HUD heard every handler in the
/// process. They are the client container's handler instances now, taken
/// through the HUD's constructor.
/// </para>
/// </summary>
public class HudSubscriptionTests
{
    // Armour 7, health 93, bits 4, from (1, 0, 0): a hit with an origin, which stock's indicator shows.
    private static readonly byte[] ADamagePayload = { 7, 93, 4, 0, 0, 0, 0, 0, 0x80, 0x3F, 0, 0, 0, 0, 0, 0, 0, 0 };

    private static (Hud Hud, DamageMessage Damage) Subscribed()
    {
        var damage = new DamageMessage();
        var hud = new Hud(new RecordingLog(), Fake().Returns("Clock.Now", 5f), Fake(), damage, new HudMsgMessage(),
                          new HudTextMessage(), new KeyHintTextMessage(), new BuildInfo());
        hud.Subscribe();
        return (hud, damage);
    }

    [Fact]
    public void TheHudHearsDamageOnTheHandlerItWasGiven()
    {
        var (hud, damage) = Subscribed();

        ((IUserMessage)damage).OnReceived(new UserMessageReader(ADamagePayload));

        Assert.True(hud.DamageFraction(5f) > 0f, "a damage message on its own handler lights the indicator");
    }

    [Fact]
    public void TheHudDoesNotHearAnotherHandlersDamage()
    {
        var (hud, _) = Subscribed();

        ((IUserMessage)new DamageMessage()).OnReceived(new UserMessageReader(ADamagePayload));

        Assert.True(hud.DamageFraction(5f) == 0f, "another handler's damage does not reach this HUD");
    }

    /// <summary>
    /// Stock's server sends an empty Damage message after a spawn and the frame
    /// after each hit (the remembered kinds changed); stock's indicator plays
    /// only when health or armour was taken (CHudDamageIndicator::MsgFunc_Damage).
    /// </summary>
    [Fact]
    public void An_empty_Damage_message_does_not_light_the_indicator()
    {
        var (hud, damage) = Subscribed();
        ((IUserMessage)damage).OnReceived(new UserMessageReader(new byte[18]));
        Assert.True(hud.DamageFraction(5f) == 0f, "nothing taken, nothing shown");

        // The armour alone is a hit.
        var armourOnly = (byte[])ADamagePayload.Clone();
        armourOnly[1] = 0;
        ((IUserMessage)damage).OnReceived(new UserMessageReader(armourOnly));
        Assert.True(hud.DamageFraction(5f) > 0f, "armour taken lights it");
    }
}
