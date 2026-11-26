using SourceSharp.Abi;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// Where the other players look up or down, as this client draws them: each
/// player's networked <see cref="Shared.PlayerNetState.EyePitch"/> (stock's
/// <c>m_angEyeAngles[PITCH]</c>), kept as history and read the interpolation
/// amount in the past, as stock's <c>AddVar( &amp;m_angEyeAngles,
/// &amp;m_iv_angEyeAngles, LATCH_SIMULATION_VAR )</c> interpolates it
/// (c_hl2mp_player.cpp:36-37, c_tf_player.cpp:3874), the same delay the bodies
/// are drawn with.
///
/// <para>
/// <b>Once a frame, not once a decode.</b> A new look is read in the past, so
/// at the decode that carries it the read still answers the old one; stock
/// poses remote players every frame (<c>UpdateClientSideAnimation</c>), so
/// the new look shows once the delay has passed whether or not the player
/// decodes again. Here each frame files the decodes and asks every remote
/// player to follow his look (<see cref="ClientSsPlayer.FollowLook"/>), which
/// drives his animation state only when the answer moved.
/// </para>
/// </summary>
public sealed class RemotePlayerLooks(IClientStandalones standalones, IGameClock clock, IInterpolationAmount interp,
                                      IEngineHud hud) : IClientFrameThink, IOrderedHook
{
    /// <summary>Its place in the client's frame thinks.</summary>
    public const int Order = 8200;

    public int HookOrder => Order;

    private sealed class Track(ClientSsPlayer player)
    {
        public readonly ClientSsPlayer Player = player;
        public readonly InterpolationRing Ring = new(InterpolationKind.Angle, new InterpolationArms());
        public int Decodes = int.MinValue;
    }

    private readonly Dictionary<int, Track> _byPlayer = [];
    private readonly List<ClientStandaloneEntity> _live = [];

    /// <summary>
    /// File <paramref name="player"/>'s networked pitch if this is a decode not
    /// yet filed; a different entity in the slot starts a new history.
    /// </summary>
    public void Note(ClientSsPlayer player, float now, float amount)
    {
        if (player.Index <= 0)
            return;

        if (!_byPlayer.TryGetValue(player.Index, out Track? track) || !ReferenceEquals(track.Player, player))
            _byPlayer[player.Index] = track = new Track(player);

        if (track.Decodes == player.Decodes)
            return;

        track.Decodes = player.Decodes;
        track.Ring.NoteChanged(now, amount, now, player.State.EyePitch, 0f, 0f);
    }

    /// <summary>Player <paramref name="playerIndex"/>'s pitch at <c>now − amount</c>, or false with none filed.</summary>
    public bool TryPitch(int playerIndex, out float pitch) =>
        TryPitchAt(playerIndex, clock.Now, interp.Amount, out pitch);

    /// <inheritdoc cref="TryPitch"/>
    public bool TryPitchAt(int playerIndex, float now, float amount, out float pitch)
    {
        pitch = 0f;
        if (!_byPlayer.TryGetValue(playerIndex, out Track? track)
            || !track.Ring.Interpolate(now, amount, allowExtrapolation: false, maxExtrapolationAmount: 0f,
                                       out float read, out _, out _))
            return false;

        pitch = read;
        return true;
    }

    /// <summary>File this frame's decodes, then have every remote player follow his look.</summary>
    public void ClientThink(float frameTime)
    {
        standalones.CopyLive(_live);
        float now = clock.Now;
        float amount = interp.Amount;
        int local = hud.LocalPlayerIndex;

        foreach (ClientStandaloneEntity entity in _live)
            if (entity is ClientSsPlayer player)
                Note(player, now, amount);

        foreach (ClientStandaloneEntity entity in _live)
            if (entity is ClientSsPlayer player && player.Index != local)
                player.FollowLook();
    }
}
