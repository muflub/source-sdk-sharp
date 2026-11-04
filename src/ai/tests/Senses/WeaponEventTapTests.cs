using SourceSharp.Ai.Hl2mp.Senses;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.Senses;

/// <summary>
/// Shots and reloads seen (plan_hl2mp_ai §4.5): a bot counts another
/// player's shot or reload by sight only while it sees that player; an
/// unsighted shooter's shot is no event at all, and a 357's reload makes no
/// sound for the hearing to count (the weapon table names none).
/// </summary>
public sealed class WeaponEventTapTests
{
    private static readonly ActorId Watching = new(3, 1);
    private static readonly ActorId Blind = new(4, 1);
    private static readonly ActorId Shooter = new(2, 0x4000_0001);
    private static readonly int Smg1 = WeaponCatalog.Stock().IndexOf("weapon_smg1");

    private static List<(ActorId, SightTracker)> Watchers()
    {
        var sees = new SightTracker(8);
        var p = new VisionParams(6000f, 90f, 0.03f, 0.5f);
        for (int i = 0; i < 4; i++)
            sees.Observe(Shooter, SightStage.Clear, 1f, p, 0.015f);
        Assert.True(sees.Sees(2), "the fixture's watcher sees the shooter");
        var blind = new SightTracker(8);
        blind.Observe(Shooter, SightStage.Trace, 0f, p, 0.015f);
        return [(Watching, sees), (Blind, blind)];
    }

    private static List<AiEvent> Perceive(PlayerWeaponEvent e, int count = 1)
    {
        var into = new List<AiEvent>();
        WeaponEventTap.Perceive(2, Smg1, e, count, Watchers(), new Tick(9), into);
        return into;
    }

    [Fact]
    public void ASightedShotIsSeenWithItsRoundsAndWeapon()
    {
        AiEvent e = Assert.Single(Perceive(PlayerWeaponEvent.Fired, 3));
        Assert.Equal((AiEventKind.SeenFire, Watching, Shooter, 3, Smg1), (e.Kind, e.Bot, e.Other, e.Code, e.Code2));
    }

    /// <summary>A grenade or a ball says nothing of the clip, and above all is no reload's end.</summary>
    [Fact]
    public void ASecondTriggerTellsAWatcherNothingOfTheClip() => Assert.Empty(Perceive(PlayerWeaponEvent.AltFired));

    [Fact]
    public void AnUnsightedShootersShotIsNoEventForTheBotThatCannotSeeIt()
        => Assert.DoesNotContain(Perceive(PlayerWeaponEvent.Fired), e => e.Bot == Blind);

    [Fact]
    public void ASightedReloadIsSeenAtItsStartLoopAndEnd()
    {
        Assert.Equal(ReloadPhase.Start, (ReloadPhase)Assert.Single(Perceive(PlayerWeaponEvent.ReloadStarted)).Code);
        AiEvent loop = Assert.Single(Perceive(PlayerWeaponEvent.ReloadShell, 1));
        Assert.Equal((ReloadPhase.Loop, 1f), ((ReloadPhase)loop.Code, loop.Amount));
        AiEvent end = Assert.Single(Perceive(PlayerWeaponEvent.ReloadFinished, 45));
        Assert.Equal((ReloadPhase.End, 45f), ((ReloadPhase)end.Code, end.Amount));
    }

    [Fact]
    public void APlayerDoesNotSeeItsOwnShot()
    {
        var into = new List<AiEvent>();
        WeaponEventTap.Perceive(3, Smg1, PlayerWeaponEvent.Fired, 1, Watchers(), new Tick(9), into);
        Assert.DoesNotContain(into, e => e.Bot == Watching);
    }

    [Fact]
    public void A357ReloadMakesNoSoundTheHearingCouldCount()
    {
        WeaponType magnum = WeaponCatalog.Stock().For("weapon_357")!.Value;
        Assert.Equal("", magnum.ReloadSound);
        var classifier = new SoundClassifier(WeaponCatalog.Stock());
        int m = WeaponCatalog.Stock().IndexOf("weapon_357");
        Assert.True(WeaponCatalog.Stock().All.All(w => w.ReloadSound.Length == 0 || classifier.Classify(w.ReloadSound).Weapon != m),
                    "no reload name maps to the 357");
    }
}
