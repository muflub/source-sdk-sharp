using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

namespace TF2.Client.Player;

/// <summary>
/// The client half of the <c>tf_player_data</c> carrier: the decoded
/// <see cref="TFPlayerNetState"/> of one player, the server's word. The local
/// player's is the basis <see cref="TFPlayerPrediction"/> resets its working
/// copy to each frame; nothing on this side writes it. Each carrier is in
/// <see cref="TFClientCarriers"/> from its first update to its removal.
///
/// <para>
/// Each decode also dresses the player's body, as stock draws it per viewer:
/// an ENEMY of the carrier sees the disguise class's model in the disguise
/// team's base skin (<c>C_TFPlayer::ValidateModelIndex</c> :8999-9003 and
/// <c>GetSkin</c> :7802-7805), while team-mates and the carrier see the spy's
/// own networked model (:9024-9032; the paper-mask bodygroup there needs a
/// bodygroup seam we do not have — docs/divergences.md). The server never
/// changes the model, so the body is decided here at the decode and carried
/// in the viewer-local override.
/// </para>
/// </summary>
[ClientStandaloneEntity(TFEntityNames.PlayerData)]
public sealed class ClientTFPlayerData : ClientStandaloneEntity
{
    private readonly TFClientCarriers _carriers;
    private readonly IClientStandalones _standalones;
    private readonly TFClientPlayerResources _resources;
    private readonly IEngineHud _hud;
    private readonly TFClassTable _classes;
    private readonly TFClientConditionDriver _conditions;
    private readonly ILog _log;

    /// <summary>The condition words the last decode carried, so this decode's
    /// arms run for the difference (stock's <c>m_nOldConditions</c> words on
    /// the entity): empty until the first, whose adds then all run.</summary>
    private TFCondMask _syncedConditions;

    /// <summary>The skin last written for this carrier's player, so a decode
    /// that decided the same body writes nothing.</summary>
    private int _lastSkin = -1;

    /// <summary>The model last overridden for this carrier's player; "" is
    /// none. Kept so a decode that decided the same body writes nothing.</summary>
    private string _lastModel = "";

    public ClientTFPlayerData(IEngineEntityRender render, TFClientCarriers carriers,
                              IClientStandalones standalones, TFClientPlayerResources resources,
                              IEngineHud hud, TFClassTable classes, TFClientConditionDriver conditions, ILog log)
        : base(render)
    {
        _carriers = carriers;
        _standalones = standalones;
        _resources = resources;
        _hud = hud;
        _classes = classes;
        _conditions = conditions;
        _log = log;
    }

    public override string ClassName => TFEntityNames.PlayerData;

    /// <summary>The same type the server writes through, decoded per update.</summary>
    [NetState] public TFPlayerNetState State = new();

    /// <summary>
    /// How many updates have decoded into <see cref="State"/>: a fresh decode
    /// is what the prediction compare waits for (ClientArsenal's rule).
    /// </summary>
    public int Decodes { get; private set; }

    public override void OnSpawn() => _carriers.Track(this);

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        Decodes++;
        _carriers.Track(this);

        // The decode's changed conditions, as stock's OnDataChanged runs
        // SyncConditions per word (tf_player_shared.cpp:1452-1457): the arms
        // see the new words, then the seen words move. Before the player's own
        // shell has arrived the arms would name a default handle, so the words
        // are held instead — neither synced nor seen — and the first decode
        // that names a shell reports them (the join decodes again on the
        // team and class writes that follow).
        TFCondMask now = State.Conditions();
        if (PlayerHandle() != EntityHandle.None)
        {
            _conditions.Sync(PlayerHandle(), _syncedConditions, in now);
            _syncedConditions = now;
        }

        DressBody();
    }

    public override void OnRemoved() => _carriers.Untrack(this);

    /// <summary>
    /// Stock's <c>IsEnemyPlayer</c> (c_tf_player.cpp:5384-5412): the target
    /// plays the other team, and nobody is an enemy of a spectator's or an
    /// unassigned local (the chain's else answers false).
    /// </summary>
    public static bool IsEnemyViewer(int localTeam, int targetTeam)
        => localTeam == (int)TFTeam.Red ? targetTeam == (int)TFTeam.Blue
         : localTeam == (int)TFTeam.Blue ? targetTeam == (int)TFTeam.Red
         : false;

    /// <summary>
    /// The skin of <paramref name="state"/>'s body as <paramref name="localTeam"/>
    /// sees it: the disguise team's base skin under <c>TF_COND_DISGUISED</c>
    /// (c_tf_player.cpp:7802-7805), for an enemy viewer only; everyone else
    /// sees the real team's. An invulnerable body wears the über skin, two on
    /// from its team's base (:7827-7833). None when the team is not one of the
    /// two (stock's default arm answers 0; ours leaves the shell's alone until
    /// known).
    /// </summary>
    public static int? SkinFor(TFPlayerNetState state, int realTeam, int localTeam)
    {
        int? skin = null;

        if (state.Conditions().Has(TFCond.Disguised) && IsEnemyViewer(localTeam, realTeam))
            skin = (int)state.DisguiseTeam - 2;
        else if (realTeam is (int)TFTeam.Red or (int)TFTeam.Blue)
            skin = realTeam - 2;

        // "3 and 4 are invulnerable" (:7827): the über body on either team's
        // base, the disguise's included. The hide-unless-damaged arm's
        // last-damage time is the MvM vaccine's, on no carrier.
        if (skin is int seen && state.IsInvulnerable())
            return seen + 2;

        return skin;
    }

    /// <summary>
    /// The class whose body <paramref name="localTeam"/> draws
    /// <paramref name="state"/>'s player as: the disguise class for an enemy
    /// viewer under <c>TF_COND_DISGUISED</c> (stock's ValidateModelIndex,
    /// c_tf_player.cpp:8999-9003), else null — the spy's own networked model,
    /// the override cleared.
    /// </summary>
    public static TFClass? BodyClassFor(TFPlayerNetState state, int realTeam, int localTeam)
        => state.Conditions().Has(TFCond.Disguised) && IsEnemyViewer(localTeam, realTeam)
            ? (TFClass)state.DisguiseClass : null;

    /// <summary>Writes the decode's body onto this carrier's player's shell,
    /// when the body is one of ours and the decision moved. The model rides
    /// the viewer-local override (an empty name clears it); the skin rides
    /// the block's skin row.</summary>
    private void DressBody()
    {
        int realTeam = RealTeam();
        int localTeam = LocalTeam();

        foreach (var entity in _standalones.OfClass(EntityNames.Player))
        {
            if (entity.Index != State.OwnerIndex)
                continue;

            string model = BodyClassFor(State, realTeam, localTeam) is { } body
                           && _classes[body].Model is { Length: > 0 } body_model ? body_model : "";
            if (model != _lastModel)
            {
                Render.SetModelOverride(entity.Handle, model);
                _log.Msg($"[TF2] disguise: player {State.OwnerIndex} drawn as " +
                         (model.Length > 0 ? model : "the spy's own model") + "\n");
                _lastModel = model;
            }

            if (SkinFor(State, realTeam, localTeam) is { } wanted && wanted != _lastSkin)
            {
                Render.SetSkin(entity.Handle, wanted);
                _lastSkin = wanted;
            }

            return;
        }
    }

    /// <summary>The player's shell, for the arms that attach to it; none when it has not arrived.</summary>
    private EntityHandle PlayerHandle()
        => _standalones.GetByIndex(State.OwnerIndex) is { } player ? player.Handle : default;

    /// <summary>The player's real team, as the scoreboard's row carries it; 0 when no resource has arrived.</summary>
    private int RealTeam()
        => _resources.Live is { } resource ? resource.State.Team[State.OwnerIndex] : 0;

    /// <summary>The local player's team by the same row; 0 when none has arrived.</summary>
    private int LocalTeam()
        => _resources.Live is { } resource && _hud.LocalPlayerIndex < TFPlayerResourceNetState.Slots
            ? resource.State.Team[_hud.LocalPlayerIndex] : 0;
}

/// <summary>
/// The client's live <c>tf_player_data</c> carriers, found by owner. Kept by
/// the carriers themselves rather than looked up in the standalone directory,
/// because the movement rules take their <see cref="ITFPlayerData"/> at the
/// core's movement resolution, before the directory is registered (the
/// server's carriers are dependency-free for the same reason). At most one per
/// player, so the walk is short.
/// </summary>
[SideService(Side.Client)]
public sealed class TFClientCarriers
{
    private readonly List<ClientTFPlayerData> _live = new();

    /// <summary>How many carriers are live.</summary>
    public int Count => _live.Count;

    public void Track(ClientTFPlayerData carrier)
    {
        if (!_live.Contains(carrier))
            _live.Add(carrier);
    }

    public void Untrack(ClientTFPlayerData carrier) => _live.Remove(carrier);

    /// <summary>The carrier owned by <paramref name="playerIndex"/>, or null.</summary>
    public ClientTFPlayerData? Of(int playerIndex)
    {
        foreach (var carrier in _live)
        {
            if (carrier.State.OwnerIndex == playerIndex)
                return carrier;
        }

        return null;
    }
}
