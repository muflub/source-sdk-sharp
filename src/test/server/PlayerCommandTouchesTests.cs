using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The per-command trigger touches (stock's ProcessImpacts): each registered
/// trigger is asked once per command of its player, in registration order,
/// and dropped when it answers false or is gone.
/// </summary>
public sealed class PlayerCommandTouchesTests
{
    private static readonly EntityHandle Player = new(0x0001_0001u);
    private static readonly EntityHandle Other = new(0x0001_0002u);
    private static readonly EntityHandle A = new(0x0002_0010u);
    private static readonly EntityHandle B = new(0x0002_0011u);

    /// <summary>A trigger that answers <see cref="Holding"/> and logs every touch.</summary>
    private sealed class Touch : ScriptedEntity, IPlayerCommandTouch
    {
        public readonly List<EntityHandle> Touched = [];
        public bool Holding = true;

        public bool TouchFromCommand(EntityHandle player)
        {
            Touched.Add(player);
            return Holding;
        }
    }

    private readonly Touch _a = new();
    private readonly Touch _b = new();

    private PlayerCommandTouches Make()
        => new(h => h == A ? _a : h == B ? _b : null);

    [Fact]
    public void EachHeldTriggerTouchesOncePerCommand()
    {
        var touches = Make();
        touches.Hold(Player, A);
        touches.Hold(Player, A);
        touches.Hold(Player, B);

        touches.ProcessImpacts(Player);
        touches.ProcessImpacts(Player);

        Assert.Equal((2, 2), (_a.Touched.Count, _b.Touched.Count));
    }

    [Fact]
    public void AnotherPlayersCommandTouchesNothingOfThisOnes()
    {
        var touches = Make();
        touches.Hold(Player, A);

        touches.ProcessImpacts(Other);

        Assert.Empty(_a.Touched);
    }

    [Fact]
    public void ATriggerThatLetsGoIsDroppedAndTheRestKept()
    {
        var touches = Make();
        touches.Hold(Player, A);
        touches.Hold(Player, B);
        _a.Holding = false;

        touches.ProcessImpacts(Player);
        touches.ProcessImpacts(Player);

        Assert.True(_a.Touched.Count == 1 && _b.Touched.Count == 2 && !touches.Holds(Player, A) && touches.Holds(Player, B),
                    $"a {_a.Touched.Count}, b {_b.Touched.Count}");
    }

    [Fact]
    public void ATriggerThatIsGoneIsDropped()
    {
        var touches = new PlayerCommandTouches(_ => null);
        touches.Hold(Player, A);

        touches.ProcessImpacts(Player);

        Assert.False(touches.Holds(Player, A));
    }

    private sealed class HearsEveryTouch(bool every) : ScriptedEntity
    {
        public readonly List<EntityHandle> Touches = [];
        public override bool TouchEveryTick => every;
        public override void OnTouch(EntityHandle toucher) => Touches.Add(toucher);
    }

    /// <summary>
    /// ProcessImpacts' PhysicsImpact: what the move ran into has its touch, once a command,
    /// if its class hears every touch. A player walking into a hurting func_rotating was
    /// reported to nobody, so it never threw them.
    /// </summary>
    [Fact]
    public void WhatTheMoveRanIntoIsTouchedOnceIfItHearsEveryTouch()
    {
        var player = new EntityHandle(0x0001_0001u);
        var hears = new HearsEveryTouch(true);
        var deaf = new HearsEveryTouch(false);
        var hearsAt = new EntityHandle(0x0002_0001u);
        var deafAt = new EntityHandle(0x0002_0002u);
        var moves = new SourceSharp.Server.Interop.ServerPlayerMoves();
        var record = moves.For(player);
        record.RecordTouch(hearsAt);
        record.RecordTouch(deafAt);
        var touches = new PlayerCommandTouches(h => h == hearsAt ? hears : h == deafAt ? deaf : null, moves);

        touches.ProcessImpacts(player);

        Assert.Equal([player], hears.Touches);
        Assert.Empty(deaf.Touches);

        record.ClearTouches();
        touches.ProcessImpacts(player);
        Assert.Single(hears.Touches);
    }
}
