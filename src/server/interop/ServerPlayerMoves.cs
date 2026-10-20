using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// One player's movement state on the server (plan_server_native_v2 T4g).
///
/// <para>
/// It was CStandalonePlayer's: m_Local, m_vecAbsVelocity, m_vecBaseVelocity,
/// m_fFlags, m_MoveType and the rest of what IMovementPlayer exposed, which
/// shell_movement.cpp copied into a MoveState for every command and back out
/// after it. The loop's own state object is the store now, kept between
/// commands, and the words it does not hold are the record's. The values a
/// fresh one starts with are the native constructor's.
/// </para>
/// </summary>
public sealed class ServerPlayerMove
{
    /// <summary>
    /// The loop's state, kept: what it reads and writes is what the player is.
    /// The per-command inputs (the command, the clock, the engine's facts) are
    /// written over it before each run.
    /// </summary>
    public MoveState Move => _move ??= Fresh();

    // Made on first read with the native constructor's values (a constructor
    // only stores, by the interop layer's rule).
    private MoveState? _move;

    /// <summary>
    /// m_nTickBase: the tick this player's next command runs at. 0 is "never
    /// placed", which the next batch places as single player does.
    /// </summary>
    public int TickBase { get; set; }

    /// <summary>What the player stands on (m_hGround), or <see cref="EntityHandle.None"/>.</summary>
    public EntityHandle Ground { get; set; }

    /// <summary>Where the last command left the player (m_vecPreviouslyPredictedOrigin).</summary>
    public Vector PreviouslyPredictedOrigin { get; set; }

    /// <summary>m_flMaxSpeed: never written after the constructor's 320 in this build.</summary>
    public float MaxSpeed { get; set; } = 320f;

    /// <summary>
    /// m_Local.m_flOldForwardMove: SetupMove's input, which stock's FinishMove
    /// never stores, so it is the constructor's 0 for the player's life.
    /// </summary>
    public float OldForwardMove { get; set; }

    /// <summary>
    /// What the last command's move ran into, one entry per entity (the move
    /// helper's touch list, RecordMoveTouch): all are counted, the first
    /// <see cref="PlayerShadowFacts.TouchMax"/> listed, and bit i of
    /// <see cref="TouchedGround"/> says entry i was the ground when touched.
    /// </summary>
    public int Touched { get; private set; }

    public uint TouchedGround { get; private set; }

    public uint[] TouchedEntities { get; } = new uint[PlayerShadowFacts.TouchMax];

    /// <summary>A command starts with nothing touched (stock's PlayerRunCommand clears m_touchedPhysObject).</summary>
    public void ClearTouches()
    {
        Touched = 0;
        TouchedGround = 0;
    }

    /// <summary>One entity the move ran into, and whether it was the ground then.</summary>
    public void RecordTouch(EntityHandle entity)
    {
        if (Touched < TouchedEntities.Length)
        {
            TouchedEntities[Touched] = entity.Value;
            if (entity == Ground && entity != EntityHandle.None)
                TouchedGround |= 1u << Touched;
        }

        Touched++;
    }

    /// <summary>
    /// CStandalonePlayer::Spawn's movement words: a client, walking; the eye
    /// at the hull's view height.
    /// </summary>
    public void Spawn(Vector viewOffset)
    {
        Move.Flags |= ServerCommandRules.FlClient;
        Move.MoveType = GameMovement.MoveTypeWalk;
        Move.ViewOffset = viewOffset;
    }

    /// <summary>The native constructor's values (shell_standalone_player.cpp, CPlayerLocalData's).</summary>
    private static MoveState Fresh() => new()
    {
        SurfaceFriction = 1f,
        ViewOffset = new Vector(0f, 0f, 64f),
        EntityGravity = 1f,
        MaxSpeed = 320f,
        ClientMaxSpeed = 320f,
        MoveType = GameMovement.MoveTypeWalk,
        StepSize = 18f,
        AllowAutoMovement = true,
        LaggedMovementValue = 1f,
        ModelScale = 1f,
        IsServer = true,
    };
}

/// <summary>
/// The server's players' movement state, one <see cref="ServerPlayerMove"/>
/// per player handle (plan_server_native_v2 T4g). Per composition: two game
/// instances share nothing, and a hot reload starts from the published state.
/// </summary>
public sealed class ServerPlayerMoves
{
    private readonly Dictionary<uint, ServerPlayerMove> _moves = new();

    /// <summary>
    /// Fills a new record from what an earlier composition published for the
    /// same player (a hot reload); answers false, leaving the constructor's
    /// values, for a player never published.
    /// </summary>
    public Func<EntityHandle, ServerPlayerMove, bool>? Seed { get; set; }

    /// <summary>The player's record, made (seeded, or the constructor's values) the first time it is asked for.</summary>
    public ServerPlayerMove For(EntityHandle player)
    {
        if (!_moves.TryGetValue(player.Value, out ServerPlayerMove? move))
        {
            _moves[player.Value] = move = new ServerPlayerMove();
            Seed?.Invoke(player, move);
        }

        return move;
    }

    /// <summary>The record, or null when this composition has not seen the player.</summary>
    public ServerPlayerMove? Find(EntityHandle player)
        => _moves.TryGetValue(player.Value, out ServerPlayerMove? move) ? move : null;

    public void Forget(EntityHandle player) => _moves.Remove(player.Value);

    public void ForgetAll() => _moves.Clear();

    public int Count => _moves.Count;
}

/// <summary>
/// The player shadow's facts with the movement words from the managed record
/// (plan_server_native_v2 T4g): native's facts are the body it holds, the
/// shell's origin and the ground's physics object; the velocity, flags, move
/// type, the move's outputs and the touch list are the command loop's. A
/// ground the record has let go of (an upward push between commands) names no
/// physics object.
/// </summary>
public sealed class MovedPlayerShadowNative(IPlayerShadowNative native, ServerPlayerMoves moves, ServerMoveData data)
    : IPlayerShadowNative
{
    public bool Facts(EntityHandle player, out PlayerShadowFacts facts)
    {
        if (!native.Facts(player, out facts))
            return false;

        if (moves.Find(player) is not { } m)
            return true;

        MoveState s = m.Move;
        facts.Velocity = s.Velocity;
        facts.OutWishVel = data.OutWishVel;
        facts.OutStepHeight = data.OutStepHeight;
        facts.MaxSpeed = m.MaxSpeed;
        facts.Flags = s.Flags;
        facts.MoveType = s.MoveType;

        if (m.Ground == EntityHandle.None || m.Ground.Value != facts.Ground)
        {
            facts.Ground = m.Ground.Value;
            facts.GroundMoveType = 0;
            facts.GroundObject = 0;
        }

        facts.Touched = m.Touched;
        facts.TouchedGround = m.TouchedGround;
        for (int i = 0; i < PlayerShadowFacts.TouchMax; i++)
            facts.TouchedEntities[i] = i < m.Touched ? m.TouchedEntities[i] : 0u;

        return true;
    }

    public bool Adopt(EntityHandle player, nint stand, nint crouch, nint controller)
        => native.Adopt(player, stand, crouch, controller);

    public void Release(EntityHandle player) => native.Release(player);

    /// <summary>After a command, the move's step height is consumed (the global CMoveData's).</summary>
    public void Commit(EntityHandle player, int state, bool afterCommand)
    {
        if (afterCommand)
            data.OutStepHeight = 0f;

        native.Commit(player, state, afterCommand);
    }

    public nint Environment() => native.Environment();
}
