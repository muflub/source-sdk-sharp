using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// The scoreboard, as it goes on the wire: one row per player slot.
///
/// <para>
/// THIS IS THE ONLY DECLARATION OF THE LAYOUT, and that is the whole point of
/// the port. Stock's <c>player_manager</c> spelled its table out twice — ten
/// <c>SendPropArray3</c> lines in <c>game/server/player_resource.cpp</c> and ten
/// matching <c>RecvPropArray3</c> lines in <c>game/client/c_playerresource.cpp</c>,
/// two files in two DLLs with nothing comparing them. The engine pairs a
/// RecvTable to a SendTable BY NAME during the connect handshake, so a prop
/// renamed, reordered or dropped on one side only is not a missing value: it is
/// a bitstream that stops making sense partway through, and the symptom is a
/// client that begins joining and stops with nothing in any log.
/// </para>
///
/// <para>
/// A sibling lane hit exactly that with the netentity shell's three real props
/// and had to add <c>netentity_table_pairing_test.cpp</c> to diff the two hand
/// copies. There is nothing to diff here. Both sides publish this one class
/// through <c>StandaloneNetStates.PublishAll</c>, both get their offsets from
/// the generator's table, and the entity rides a tier ServerClass that already
/// exists on both shells — so this port adds NO SendTable and NO RecvTable at
/// all. What the two sides could still disagree about is the field NAMES the
/// native reader asks for, and that is what
/// <c>sourcesharp/test/playerresource_pairing_test.cpp</c> pins.
/// </para>
///
/// <para>
/// WHY THE ROWS ARE NARROW. Ten <c>int</c> runs of 102 would be 4,080 bytes and
/// no tier carries more than 2,048 (<c>generic_nettable.h</c>). Every value here
/// fits the width it is given with room over — stock sent score and deaths in
/// twelve bits, team in four and the three flags in one each — so nothing is
/// lost by saying so. The declared total is 1,020 bytes, which lands in the
/// 1,024-byte tier.
/// </para>
///
/// <para>
/// WHAT STOCK CARRIED AND THIS DOES NOT, both stated rather than discovered:
/// </para>
/// <para>
/// <c>m_iAccountID</c> — dropped. It is a SteamID account id, it is not on
/// <c>IGameResources</c>, and nothing compiled into this build reads it.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.PlayerManager)]
public partial class PlayerResourceNetState
{
    /// <summary>
    /// How many rows there are: <c>MAX_PLAYERS_ARRAY_SAFE</c> from
    /// <c>game/shared/shareddefs.h</c>, which is <c>MAX_PLAYERS + 1</c>.
    ///
    /// <para>
    /// Every consumer of <c>IGameResources</c> loops <c>1..MAX_PLAYERS</c> and
    /// indexes by ENTITY index, so slot zero exists and is never a player — the
    /// same reason stock sized its arrays this way rather than at MAX_PLAYERS.
    /// </para>
    ///
    /// <para>
    /// 34, NOT 102, AND THE LIVE RUN IS WHAT SETTLED IT. shareddefs.h gives
    /// MAX_PLAYERS as 101 under <c>HL2MP</c> and 33 otherwise, and this mod is
    /// an HL2MP mod — but NEITHER <c>client_csharp.vpc</c> NOR
    /// <c>server_csharp.vpc</c> defines HL2MP, so both DLLs compile the 33.
    /// The first version of this class read the branch rather than the build
    /// and declared 102; the native reader, which bounds its walk by the
    /// engine's own <c>MAX_PLAYERS_ARRAY_SAFE</c>, logged
    /// "player resource: 34 slot(s)" and two thirds of the payload was
    /// networked and never read. <c>Engine.MaxPlayerSlots</c> is 33 for the
    /// same reason and says so.
    /// </para>
    ///
    /// <para>
    /// It costs a tier as well as the bytes: 102 rows is 1,020 bytes, which
    /// fills the 1,024 tier to 99.6% (<c>nettable player_manager</c> reported
    /// <c>nearCeiling: true</c>) and carries 256 payload words per update. 34
    /// rows is 340 bytes, the 512 tier, and 128 words.
    /// </para>
    ///
    /// <para>
    /// The native reader does NOT hold a copy of this number. It asks C# for
    /// <c>Score.0</c>, then <c>Score.1</c> for the stride, then walks upward
    /// until a row does not resolve — so the count is asked like everything
    /// else, and shrinking or growing this constant needs no native change. It
    /// does say so loudly if this number ever exceeds what the engine's own
    /// bound lets it read.
    /// </para>
    /// </summary>
    public const int Slots = 34;

    /// <summary>Frags. Stock sent this in twelve bits, signed; a short is wider.</summary>
    [NetArray(Slots)] public partial NetArray<short> Score { get; }

    /// <summary>Deaths. Twelve bits in stock, for the same reason.</summary>
    [NetArray(Slots)] public partial NetArray<short> Deaths { get; }

    /// <summary>
    /// What the scoreboard and the map overview show as a health bar.
    ///
    /// Clamped at zero by the writer, never sent negative: stock did the same
    /// (<c>MAX( 0, pPlayer-&gt;GetHealth() )</c>) because a dead player's health
    /// is an overkill amount below zero and a bar drawn from it runs backwards.
    /// </summary>
    [NetArray(Slots)] public partial NetArray<short> Health { get; }

    /// <summary>
    /// The engine's per-connection id, which is the identity a scoreboard row
    /// survives a respawn on.
    ///
    /// A short rather than stock's full int. Userids are handed out from one
    /// upward per connection and reset with the level, so 32,767 of them is a
    /// server that has churned every slot three hundred times in one map.
    /// </summary>
    [NetArray(Slots)] public partial NetArray<short> UserId { get; }

    /// <summary>
    /// Which team. Four bits in stock; a byte is wider.
    ///
    /// The row is the PLAYER's entity index, like every other run here.
    /// </summary>
    [NetArray(Slots)] public partial NetArray<byte> Team { get; }

    /// <summary>
    /// A team's score, for the scoreboard header and the team menu
    /// (tf2 stage 0.7 part C: the plan's "the team score fields it needs").
    ///
    /// <para>
    /// THE INDEX IS THE TEAM, not the player — the one run on this class
    /// that is not a per-player row, which is why it is sized at
    /// <see cref="EngineLimits.MaxTeams"/> and refreshed by its own method
    /// rather than by <see cref="SetRow"/>. Stock kept a team's score on the
    /// C_Team entity and reached it through GetGlobalTeam; C_Team left this
    /// build with the server gc cull, so a team's numbers have never been on
    /// any table. This run is where the mode's IScoring puts them —
    /// team-scored games (TF2's two teams today, the plan's Stage 1+) need
    /// them client-side, and a run nothing reads is this repo's documented
    /// quiet failure, so the client reader asks for it by name beside the
    /// seven player rows (playerresource_pairing_test.cpp pins the pair).
    /// </para>
    /// </summary>
    [NetArray(EngineLimits.MaxTeams)] public partial NetArray<short> TeamScore { get; }

    /// <summary>
    /// The client's ping in ms, smoothed, set every two seconds (stock's
    /// m_iPing; plan_client_ui U3). Kept apart from <see cref="SetRow"/>, which
    /// runs every think, because stock moves it on its own cadence.
    /// </summary>
    [NetArray(Slots)] public partial NetArray<short> Ping { get; }

    /// <summary>
    /// The three one-bit facts, packed into one run instead of three.
    ///
    /// Three <c>byte</c> runs would be 306 bytes for three bits per row. Nothing
    /// outside this class should test these by hand — <see cref="IsConnected"/>
    /// and its neighbours are the interface, so the packing has one copy and the
    /// native reader is given the same bit numbers by name.
    /// </summary>
    [NetArray(Slots)] public partial NetArray<byte> Flags { get; }

    /// <summary>In the server's player list right now.</summary>
    public const int FlagConnected = 1 << 0;

    /// <summary>Playing rather than waiting to respawn.</summary>
    public const int FlagAlive = 1 << 1;

    /// <summary>
    /// The row holds real data, whether or not the player is still here.
    ///
    /// Stock's <c>m_bValid</c>, and it is not the same question as
    /// <see cref="FlagConnected"/>: a scoreboard keeps showing a player who has
    /// just left, and every stock accessor asks <c>IsConnected || IsValid</c>
    /// before answering rather than <c>IsConnected</c> alone.
    /// </summary>
    public const int FlagValid = 1 << 2;

    /// <summary>Whether <paramref name="slot"/> is a row at all.</summary>
    public static bool InRange(int slot) => (uint)slot < (uint)Slots;

    /// <summary>Reads one flag bit, answering false for a slot that is not a row.</summary>
    public bool HasFlag(int slot, int bit)
        => InRange(slot) && (Flags[slot] & bit) != 0;

    /// <inheritdoc cref="HasFlag"/>
    public bool IsConnected(int slot) => HasFlag(slot, FlagConnected);

    /// <inheritdoc cref="HasFlag"/>
    public bool IsAlive(int slot) => HasFlag(slot, FlagAlive);

    /// <inheritdoc cref="HasFlag"/>
    public bool IsValid(int slot) => HasFlag(slot, FlagValid);

    /// <summary>
    /// Writes one row whole — the shape the writer actually wants, and the only
    /// place the flag word is assembled.
    /// </summary>
    public void SetRow(int slot, int score, int deaths, int health, int userId,
                       int team, bool connected, bool alive, bool valid)
    {
        if (!InRange(slot))
            return;

        Score[slot]  = Saturate(score);
        Deaths[slot] = Saturate(deaths);
        Health[slot] = Saturate(health < 0 ? 0 : health);
        UserId[slot] = Saturate(userId);
        Team[slot]   = (byte)(team < 0 || team > byte.MaxValue ? 0 : team);

        int bits = 0;
        if (connected) bits |= FlagConnected;
        if (alive)     bits |= FlagAlive;
        if (valid)     bits |= FlagValid;

        Flags[slot] = (byte)bits;
    }

    /// <summary>
    /// Clears one row: nobody is there, and nothing about them is worth showing.
    ///
    /// Stock's <c>UpdateDisconnectedPlayer</c> left score, deaths, team and
    /// health at whatever the departed player last had, and cleared only the
    /// connected/valid pair — which is right for a scoreboard that keeps showing
    /// a player who dropped mid-round, and wrong for a slot somebody else has
    /// since joined into. Clearing the flags is what makes every accessor answer
    /// zero, so the stale numbers are unreachable either way; they are cleared
    /// here as well so that a packet dump of an empty slot reads as empty.
    /// </summary>
    public void ClearRow(int slot)
    {
        SetRow(slot, 0, 0, 0, 0, 0, connected: false, alive: false, valid: false);
        if (InRange(slot))
            Ping[slot] = 0;
    }

    /// <summary>Every row, at level start.</summary>
    public void ClearAll()
    {
        for (int slot = 0; slot < Slots; slot++)
            ClearRow(slot);
    }

    /// <summary>
    /// A value narrowed to the run's width, clamped rather than wrapped.
    ///
    /// A wrap turns a score of 32,768 into -32,768, which draws as a player in
    /// enormous deficit; a clamp draws as a player at the top. Neither is
    /// reachable in a real round, and only one of them is nonsense if it is.
    /// </summary>
    private static short Saturate(int value)
        => value > short.MaxValue ? short.MaxValue
         : value < short.MinValue ? short.MinValue
         : (short)value;
}
