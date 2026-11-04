using SourceSharp.Ai.Hl2mp.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Trees;

/// <summary>
/// When each weapon's second trigger is worth pressing (plan_hl2mp_ai §5.4, ruling 2026-11-03: "Bots
/// should use alt fire"), pure over what the bot can see. Facts of the decide's hands are <see cref="AltFireTests"/>.
/// </summary>
public sealed class AltFireRuleTests
{
    private static readonly AltFireFacts Grenade = new(AltFireKind.Lob, 1000f, 400f, 250f, 100f);
    private static readonly AltFireFacts Ball = new(AltFireKind.Ball, 1000f, 0f, 0f, 1000f);
    private static readonly AltFireFacts Double = new(AltFireKind.Double, AmmoCost: 2, FromClip: true);
    private static readonly AltFireFacts Frag = new(AltFireKind.Underhand, 350f, 600f, 250f, 125f);

    /// <summary>Someone running across at mid range, fully in view, on a floor level with the bot: a reason for nothing.</summary>
    private static readonly AltFireSituation Plain = new(true, 600f, 0f, 0f, 190f, 0f, 0, 1f, false, false);

    [Fact]
    public void AGrenadeHasNoReasonAtSomeoneJustRunningAcross()
        => Assert.Equal(AltFireReason.None, Hl2mpAltFire.ReasonFor(in Grenade, in Plain));

    [Fact]
    public void AGrenadeGoesAtAGroup()
        => Assert.Equal(AltFireReason.Group, Hl2mpAltFire.ReasonFor(in Grenade, Plain with { OthersNear = 1 }));

    [Fact]
    public void AGrenadeGoesAtACamper()
    {
        Assert.Equal(AltFireReason.None, Hl2mpAltFire.ReasonFor(in Grenade, Plain with { StillSeconds = Hl2mpAltFire.CamperSeconds - 0.1f }));
        Assert.Equal(AltFireReason.Camper, Hl2mpAltFire.ReasonFor(in Grenade, Plain with { StillSeconds = Hl2mpAltFire.CamperSeconds }));
    }

    [Fact]
    public void AGrenadeGoesAtSomeoneBehindCover()
        => Assert.Equal(AltFireReason.Cover, Hl2mpAltFire.ReasonFor(in Grenade, Plain with { VisibleFraction = 0.4f }));

    [Fact]
    public void AGrenadeGoesAtACornerOnlyDownAClearLine()
    {
        AltFireSituation gone = Plain with { Sighted = false };
        Assert.Equal(AltFireReason.None, Hl2mpAltFire.ReasonFor(in Grenade, in gone));
        Assert.Equal(AltFireReason.Corner, Hl2mpAltFire.ReasonFor(in Grenade, gone with { CornerClear = true }));
    }

    /// <summary>A bot does not lob one at its feet (the ruling: self-damage must count), whatever the reason.</summary>
    [Theory]
    [InlineData(280f, false)]
    [InlineData(290f, true)]
    public void NoGrenadeInsideItsOwnBlast(float distance, bool goes)
    {
        AltFireSituation there = Plain with { Distance = distance, OthersNear = 1 };
        Assert.Equal(goes ? AltFireReason.Group : AltFireReason.None, Hl2mpAltFire.ReasonFor(in Grenade, in there));
    }

    /// <summary>Someone running at the bot will be inside the blast by the time it lands.</summary>
    [Fact]
    public void NoGrenadeAtSomeoneWhoWillBeInsideItsBlastByThen()
    {
        AltFireSituation there = Plain with { Distance = 300f, OthersNear = 1 };
        Assert.Equal(AltFireReason.Group, Hl2mpAltFire.ReasonFor(in Grenade, in there));
        Assert.Equal(AltFireReason.None, Hl2mpAltFire.ReasonFor(in Grenade, there with { ClosingSpeed = 190f }));
    }

    [Fact]
    public void NoGrenadePastItsReach()
        => Assert.Equal(AltFireReason.None, Hl2mpAltFire.ReasonFor(in Grenade, Plain with { Distance = Hl2mpAltFire.LobReach + 1f, OthersNear = 1 }));

    [Fact]
    public void NoGrenadeWithAFriendNear()
        => Assert.Equal(AltFireReason.None, Hl2mpAltFire.ReasonFor(in Grenade, Plain with { OthersNear = 1, FriendNear = true }));

    /// <summary>A ball takes 500 from what it touches and keeps flying, with no blast: a direct hit, someone coming or going along the line.</summary>
    [Fact]
    public void ABallGoesDownTheLineNotAcrossIt()
    {
        Assert.Equal(AltFireReason.None, Hl2mpAltFire.ReasonFor(in Ball, in Plain));
        Assert.Equal(AltFireReason.DownTheLine, Hl2mpAltFire.ReasonFor(in Ball, Plain with { AcrossSpeed = 20f, ClosingSpeed = 190f }));
    }

    /// <summary>It has no blast to fear: near is limited only by the view having time to lead it.</summary>
    [Theory]
    [InlineData(Hl2mpAltFire.BallNearest - 1f, false)]
    [InlineData(Hl2mpAltFire.BallNearest, true)]
    [InlineData(Hl2mpAltFire.BallReach, true)]
    [InlineData(Hl2mpAltFire.BallReach + 1f, false)]
    public void ABallHasItsRange(float distance, bool goes)
        => Assert.Equal(goes ? AltFireReason.DownTheLine : AltFireReason.None,
                        Hl2mpAltFire.ReasonFor(in Ball, Plain with { Distance = distance, AcrossSpeed = 0f }));

    /// <summary>A charging bot has no gun for about 0.6 s: up close it keeps the gun it has.</summary>
    [Theory]
    [InlineData(150f)]
    [InlineData(300f)]
    public void NoBallUpClose(float distance)
        => Assert.Equal(AltFireReason.None, Hl2mpAltFire.ReasonFor(in Ball, Plain with { Distance = distance, AcrossSpeed = 0f }));

    [Fact]
    public void NoBallAtSomeoneOutOfSight()
        => Assert.Equal(AltFireReason.None, Hl2mpAltFire.ReasonFor(in Ball, Plain with { Sighted = false, CornerClear = true, AcrossSpeed = 0f }));

    [Theory]
    [InlineData(Hl2mpAltFire.DoubleReach, true)]
    [InlineData(Hl2mpAltFire.DoubleReach + 1f, false)]
    public void BothBarrelsAreForUpClose(float distance, bool goes)
        => Assert.Equal(goes ? AltFireReason.Close : AltFireReason.None, Hl2mpAltFire.ReasonFor(in Double, Plain with { Distance = distance }));

    /// <summary>Roll on its own floor, underhand to another; past the throw's distance overhand is the primary's; inside the blast nothing.</summary>
    [Theory]
    [InlineData(350f, 0f, AltFireReason.Roll)]
    [InlineData(350f, 64f, AltFireReason.Lob)]
    [InlineData(Hl2mpAltFire.FragThrowFrom, 0f, AltFireReason.None)]
    [InlineData(200f, 0f, AltFireReason.None)]
    public void AFragRollsOrLobsOrIsThrown(float distance, float heightOff, AltFireReason why)
        => Assert.Equal(why, Hl2mpAltFire.ReasonFor(in Frag, Plain with { Distance = distance, HeightOff = heightOff }));

    /// <summary>A crossbow's scope, an RPG's laser, a pistol's nothing: no trigger to press in a fight.</summary>
    [Fact]
    public void NoSecondTriggerNoReason()
        => Assert.Equal(AltFireReason.None, Hl2mpAltFire.ReasonFor(default, Plain with { OthersNear = 3, StillSeconds = 9f, AcrossSpeed = 0f }));

    private static readonly FragSituation Gunfight = new(true, 500f, 0f, 0, 0f, 1f, false, false, false, true, false);

    /// <summary>A gunfight with a loaded gun and a clear shot goes on being one: nobody drops their gun to throw.</summary>
    [Fact]
    public void NoFragInAGunfightWithAClearShot() => Assert.Equal(AltFireReason.None, Hl2mpAltFire.FragReasonFor(in Frag, in Gunfight));

    [Fact]
    public void AFragGoesAtAGroup() => Assert.Equal(AltFireReason.Group, Hl2mpAltFire.FragReasonFor(in Frag, Gunfight with { OthersNear = 1 }));

    [Fact]
    public void AFragGoesAtSomeoneJustBehindACorner()
    {
        FragSituation gone = Gunfight with { Sighted = false };
        Assert.Equal(AltFireReason.None, Hl2mpAltFire.FragReasonFor(in Frag, in gone));
        Assert.Equal(AltFireReason.Corner, Hl2mpAltFire.FragReasonFor(in Frag, gone with { BehindCorner = true }));
    }

    [Fact]
    public void AFragGoesAtACampersSpot()
    {
        Assert.Equal(AltFireReason.Camper, Hl2mpAltFire.FragReasonFor(in Frag, Gunfight with { Sighted = false, CamperSpot = true }));
        Assert.Equal(AltFireReason.Camper, Hl2mpAltFire.FragReasonFor(in Frag, Gunfight with { VisibleFraction = 0.4f, StillSeconds = 2f }));
    }

    [Fact]
    public void AFragGoesAtSomeoneUpOrDownAGunCannotEasilyReach()
    {
        Assert.Equal(AltFireReason.Height, Hl2mpAltFire.FragReasonFor(in Frag, Gunfight with { HeightOff = 128f, VisibleFraction = 0.4f }));
        Assert.Equal(AltFireReason.Height, Hl2mpAltFire.FragReasonFor(in Frag, Gunfight with { HeightOff = -128f, VisibleFraction = 0.6f }));
        Assert.Equal(AltFireReason.None, Hl2mpAltFire.FragReasonFor(in Frag, Gunfight with { HeightOff = 128f }));
    }

    /// <summary>Never at its feet; on the way out nearer than that, because it will not be there when it goes off.</summary>
    [Theory]
    [InlineData(280f, false, false)]
    [InlineData(280f, true, true)]
    [InlineData(Hl2mpAltFire.EscapeNearest - 1f, true, false)]
    public void NoFragAtItsFeetUnlessEscaping(float distance, bool escaping, bool goes)
    {
        FragSituation there = Gunfight with { Distance = distance, OthersNear = 1, Escaping = escaping };
        Assert.Equal(goes, Hl2mpAltFire.FragReasonFor(in Frag, in there) != AltFireReason.None);
    }

    [Fact]
    public void NoFragPastItsReachOrWithAFriendNear()
    {
        Assert.Equal(AltFireReason.None, Hl2mpAltFire.FragReasonFor(in Frag, Gunfight with { Distance = Hl2mpAltFire.FragReach + 1f, OthersNear = 1 }));
        Assert.Equal(AltFireReason.None, Hl2mpAltFire.FragReasonFor(in Frag, Gunfight with { OthersNear = 1, FriendNear = true }));
    }

    [Theory]
    [InlineData(600f, 0f, FragHow.Throw)]
    [InlineData(350f, 0f, FragHow.Roll)]
    [InlineData(350f, 96f, FragHow.Lob)]
    public void TheGeometryChoosesHowItLeaves(float distance, float heightOff, FragHow how) => Assert.Equal(how, Hl2mpAltFire.HowFor(distance, heightOff));
}
