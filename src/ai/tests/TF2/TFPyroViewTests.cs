using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Senses;
using TF2.Server.Projectiles;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// What the bot's view carries of a projectile's owner and team (the Pyro lane's shared change: a reflect tells its own
/// projectile from an enemy's): the owner read off the projectile, the row's team else the owner's, and the owner only in
/// the owner's own view (a player knows his own projectiles, not whose another's is).
/// </summary>
public sealed class TFPyroViewTests
{
    private sealed class Owned(int owner) : ITFOwnedProjectile
    {
        public int OwnerIndex => owner;
    }

    /// <summary>A pipe's owner reaches the view off the projectile itself (it read 0 before), with the team its row carries.</summary>
    [Fact]
    public void AProjectilesOwnerIsReadOffIt()
        => Assert.Equal((TFField.Blue, 7), TFBotGameView.TeamOf(TFField.Blue, new Owned(7), _ => TFField.Red));

    /// <summary>An arrow, whose row carries no team, is drawn in its owner's team; it read team 0 before.</summary>
    [Fact]
    public void AnArrowIsDrawnInItsOwnersTeam()
        => Assert.Equal((TFField.Red, 9), TFBotGameView.TeamOf(0, new Owned(9), slot => slot == 9 ? TFField.Red : 0));

    /// <summary>An arrow with no owner (its owner index the empty handle's) has no owner and no team.</summary>
    [Fact]
    public void AnOwnerlessArrowHasNoOwner()
        => Assert.Equal((0, 0), TFBotGameView.TeamOf(0, new Owned(EntityHandle.None.EntryIndex), _ => TFField.Red));

    private static readonly EntityHandle Viewer = new(0x0001_0000u | 3);

    /// <summary>The view as the binding builds it; its projectile arm reads nothing of the engine.</summary>
    private static TFBotGameView View() => new(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);

    private static ThingFacts Pipe(int owner) => new(900, ThingKind.Projectile, (int)TFThingType.Pipe, default, default, TFField.Blue, owner);

    /// <summary>Another player's pipe reaches the bot's view with no owner; its own keeps its own slot.</summary>
    [Fact]
    public void OnlyTheOwnerSeesWhoseAProjectileIs()
    {
        ThingFacts theirs = Pipe(5), mine = Pipe(Viewer.EntryIndex);
        View().RenderThing(Viewer, ref theirs);
        View().RenderThing(Viewer, ref mine);
        Assert.Equal((0, Viewer.EntryIndex), (theirs.OwnerSlot, mine.OwnerSlot));
    }
}
