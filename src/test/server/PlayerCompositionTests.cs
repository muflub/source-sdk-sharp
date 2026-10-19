using System.Collections.Generic;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_native_v2 T4d: PlayerRespawn, GetPlayerShootOrigin and
/// TracePlayerAim were player-only sequences over slots the shim already had.
/// Each fact is the native body's sequence, read off shell_entityapi.cpp
/// before the move:
///
/// <list type="bullet">
/// <item>PlayerRespawn: SetObserver( false, OBS_NONE ) then Spawn(), on a
/// player only.</item>
/// <item>GetPlayerShootOrigin: the player's EyePosition(), zero for anything
/// that is not a player.</item>
/// <item>TracePlayerAim: from EyePosition() along AngleVectors( v_angle ) for
/// the range, MASK_SHOT, skipping the shooter; the hit's handle and the end
/// point, or the invalid handle for a non-player.</item>
/// </list>
///
/// Over a recorder of <see cref="IPlayerSequenceSlots"/> rather than a fake
/// shim root: EntityIsPlayer and EntityGetEyePosition are
/// [SuppressGCTransition] slots, which a managed callback may not serve.
/// </summary>
public sealed class PlayerCompositionTests
{
    private sealed class Slots : IPlayerSequenceSlots
    {
        public readonly List<string> Calls = new();
        public bool IsPlayer = true;
        public Vector Eye = new(1f, 2f, 64f);
        public Vector EyeAngles = new(0f, 90f, 0f);

        public bool EntityIsPlayer(EntityHandle h)
        {
            Calls.Add($"IsPlayer {h.Value}");
            return IsPlayer;
        }

        public Vector EntityGetEyePosition(EntityHandle h)
        {
            Calls.Add($"EyePosition {h.Value}");
            return Eye;
        }

        public Vector EntityGetEyeAngles(EntityHandle h)
        {
            Calls.Add($"EyeAngles {h.Value}");
            return EyeAngles;
        }

        public TraceResult TraceLine(Vector start, Vector end, TraceMask mask, EntityHandle ignore)
        {
            Calls.Add($"TraceLine ({start.X} {start.Y} {start.Z}) ({end.X:0.###} {end.Y:0.###} {end.Z:0.###}) {mask} ignore {ignore.Value}");
            return new TraceResult(true, 0.5f, new Vector(7f, 8f, 9f), new Vector(0f, 0f, 1f), new EntityHandle(0x00050006));
        }

        public void PlayerSetObserverMode(EntityHandle player, ObserverMode mode)
            => Calls.Add($"SetObserverMode {player.Value} {mode}");

        public void SpawnEntity(EntityHandle entity) => Calls.Add($"Spawn {entity.Value}");
    }

    private readonly Slots _slots = new();

    private static readonly EntityHandle Player = new(0x00030004);

    [Fact]
    public void ARespawnLeavesObservingThenSpawnsThePlayer()
    {
        PlayerSequences.Respawn(_slots, Player);

        Assert.Equal(new[] { "IsPlayer 196612", "SetObserverMode 196612 None", "Spawn 196612" }, _slots.Calls);
    }

    [Fact]
    public void ARespawnOfSomethingThatIsNotAPlayerDoesNothing()
    {
        _slots.IsPlayer = false;

        PlayerSequences.Respawn(_slots, Player);

        Assert.Equal(new[] { "IsPlayer 196612" }, _slots.Calls);
    }

    [Fact]
    public void AShotStartsAtThePlayersEye()
    {
        Assert.Equal(new Vector(1f, 2f, 64f), PlayerSequences.ShootOrigin(_slots, Player));
    }

    [Fact]
    public void SomethingThatIsNotAPlayerShootsFromNowhere()
    {
        _slots.IsPlayer = false;

        Assert.Equal(Vector.Zero, PlayerSequences.ShootOrigin(_slots, Player));
        Assert.Equal(new[] { "IsPlayer 196612" }, _slots.Calls);
    }

    [Fact]
    public void TheAimIsAShotTraceFromTheEyeAlongTheViewSkippingTheShooter()
    {
        EntityHandle hit = PlayerSequences.Aim(_slots, Player, 100f, out Vector hitPoint);

        Assert.Equal(new EntityHandle(0x00050006), hit);
        Assert.Equal(new Vector(7f, 8f, 9f), hitPoint);
        // Yaw 90: forward is +Y; the end is 100 units along it from the eye.
        Assert.Equal(new[]
        {
            "IsPlayer 196612", "EyePosition 196612", "EyeAngles 196612",
            "TraceLine (1 2 64) (1 102 64) Shot ignore 196612",
        }, _slots.Calls);
    }

    [Fact]
    public void ThePitchOfTheViewTiltsTheAimAndAPositivePitchLooksDown()
    {
        _slots.EyeAngles = new Vector(90f, 0f, 0f);

        PlayerSequences.Aim(_slots, Player, 100f, out _);

        Assert.Contains("TraceLine (1 2 64) (1 2 -36) Shot ignore 196612", _slots.Calls);
    }

    [Fact]
    public void ANonPlayerAimsAtNothingAndTracesNothing()
    {
        _slots.IsPlayer = false;

        EntityHandle hit = PlayerSequences.Aim(_slots, Player, 100f, out Vector hitPoint);

        Assert.Equal(EntityHandle.None, hit);
        Assert.Equal(Vector.Zero, hitPoint);
        Assert.Equal(new[] { "IsPlayer 196612" }, _slots.Calls);
    }
}
