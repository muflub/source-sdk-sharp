using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A server player's movement state into its networked state
/// (plan_server_native_v2 T4g): what standalone_player_sync.cpp's
/// SourceSharpPlayerSync_Write copied from the native player into the blob,
/// thirty words, now from the managed record the command loop keeps. The
/// three words the engine's side holds (the shell's origin, health, alive)
/// native writes beside them (PlayerSyncEngineSide).
/// </summary>
public sealed class PlayerMovePublisher(Func<EntityHandle, SsPlayer?> players) : IPlayerMovePublisher
{
    /// <summary>ground_handle_wire.h's GROUNDWIRE_NONE: no ground.</summary>
    public const int GroundWireNone = SsPlayer.GroundWireNone;

    public void Publish(EntityHandle player, ServerPlayerMove move)
    {
        if (players(player) is { } p)
            Write(p.State, move);
    }

    /// <summary>The record's words into <paramref name="state"/>.</summary>
    public static void Write(PlayerNetState state, ServerPlayerMove move)
    {
        MoveState s = move.Move;

        state.MaxSpeed = move.MaxSpeed;
        state.FallVelocity = s.FallVelocity;
        state.DuckTime = s.DuckTime;
        state.DuckJumpTime = s.DuckJumpTime;
        state.JumpTime = s.JumpTime;
        state.StepSize = s.StepSize;
        state.WaterJumpTime = s.WaterJumpTime;
        state.SurfaceFriction = s.SurfaceFriction;
        state.OldForwardMove = move.OldForwardMove;
        state.Flags = (uint)s.Flags;
        state.OldButtons = s.OldButtons;
        state.TickBase = move.TickBase;
        state.GroundEntity = GroundWire(move.Ground);

        state.Ducked = s.Ducked;
        state.Ducking = s.Ducking;
        state.InDuckJump = s.InDuckJump;
        state.AllowAutoMovement = s.AllowAutoMovement;
        state.WaterLevel = (byte)s.WaterLevel;
        state.WaterType = (byte)s.WaterType;
        state.MoveType = (byte)s.MoveType;

        state.Velocity = s.Velocity;
        state.BaseVelocity = s.BaseVelocity;
        state.ViewOffset = s.ViewOffset;
        state.PunchAngle = QAngle.FromVector(s.PunchAngle);
        state.PunchAngleVel = QAngle.FromVector(s.PunchAngleVel);
        state.LadderNormal = s.LadderNormal;
        state.WaterJumpVel = s.WaterJumpVel;
    }

    /// <summary>
    /// SourceSharpPlayerSync_EncodeGround: the ground's entry index, or none
    /// for no ground (or an index past the wire's eleven bits).
    /// </summary>
    public static int GroundWire(EntityHandle ground)
    {
        if (ground == EntityHandle.None)
            return GroundWireNone;

        int index = ground.EntryIndex;
        return index < 0 || index > SsPlayer.GroundWireIndexMask ? GroundWireNone : index;
    }

    /// <summary>
    /// A record from a state an earlier composition published (a hot
    /// reload): every networked word back, and the rest the constructor's.
    /// A state never published reads StepSize 0, which no published state
    /// carries (the step height is the constructor's 18 for a player's life),
    /// and seeds nothing.
    /// </summary>
    public static bool Seed(PlayerNetState state, ServerPlayerMove move)
    {
        if (state.StepSize == 0f)
            return false;

        MoveState s = move.Move;
        move.MaxSpeed = state.MaxSpeed;
        s.FallVelocity = state.FallVelocity;
        s.DuckTime = state.DuckTime;
        s.DuckJumpTime = state.DuckJumpTime;
        s.JumpTime = state.JumpTime;
        s.StepSize = state.StepSize;
        s.WaterJumpTime = state.WaterJumpTime;
        s.SurfaceFriction = state.SurfaceFriction;
        move.OldForwardMove = state.OldForwardMove;
        s.Flags = (int)state.Flags;
        s.OldButtons = state.OldButtons;
        move.TickBase = state.TickBase;
        s.Ducked = state.Ducked;
        s.Ducking = state.Ducking;
        s.InDuckJump = state.InDuckJump;
        s.AllowAutoMovement = state.AllowAutoMovement;
        s.WaterLevel = state.WaterLevel;
        s.WaterType = state.WaterType;
        s.MoveType = state.MoveType;
        s.Velocity = state.Velocity;
        s.BaseVelocity = state.BaseVelocity;
        s.ViewOffset = state.ViewOffset;
        s.PunchAngle = state.PunchAngle.AsVector();
        s.PunchAngleVel = state.PunchAngleVel.AsVector();
        s.LadderNormal = state.LadderNormal;
        s.WaterJumpVel = state.WaterJumpVel;
        move.PreviouslyPredictedOrigin = state.Origin;
        return true;
    }
}
