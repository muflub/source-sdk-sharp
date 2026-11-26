using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Knowledge;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The death pits in a TF2 bot's map knowledge (the Pyro's ledge shove): a <c>trigger_hurt</c> that kills, as the map sets its
/// damage, is a hurt zone; a hurting one that does not kill, or one that starts disabled, is not.
/// </summary>
public sealed class TFPyroMapTests
{
    private static MapEntity Hurt(string damage, string? startDisabled = null)
    {
        var keys = new List<KeyValuePair<string, string>> { new("classname", "trigger_hurt"), new("damage", damage) };
        if (startDisabled is not null)
            keys.Add(new("StartDisabled", startDisabled));
        return new MapEntity("trigger_hurt", null, default, new AiVector(-64, -64, -900), new AiVector(64, 64, -500), Keys: keys);
    }

    private static int HurtZones(params MapEntity[] lump)
        => TFMapKnowledge.Build("cp_test", new MapEntities(lump, null), new WeaponKnowledge([]), default, BodyKnowledge.Default())
                         .TeamZones.Count(z => z.Kind == ZoneKind.Hurt);

    /// <summary>cp_foundry's pits hurt 20,000 a second: a hurt zone, its bounds the brush's.</summary>
    [Fact]
    public void AKillingTriggerHurtIsAHurtZone()
    {
        MapKnowledge map = TFMapKnowledge.Build("cp_test", new MapEntities([Hurt("20000")], null), new WeaponKnowledge([]), default, BodyKnowledge.Default());
        TeamZone pit = map.TeamZones.Single(z => z.Kind == ZoneKind.Hurt);
        Assert.Equal((new AiVector(-64, -64, -900), new AiVector(64, 64, -500)), (pit.Mins, pit.Maxs));
    }

    /// <summary>A trigger_hurt of 20 a second (the default) hurts and does not kill: no hurt zone. One that starts disabled is none either.</summary>
    [Fact]
    public void ATriggerHurtThatDoesNotKillIsNone()
        => Assert.Equal((0, 0, 1), (HurtZones(Hurt("20")), HurtZones(Hurt("20000", "1")), HurtZones(Hurt("300"))));
}
