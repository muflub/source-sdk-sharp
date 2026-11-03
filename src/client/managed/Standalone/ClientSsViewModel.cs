//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.WeaponEffects;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of the viewmodel (Phase 10), and since the render half
/// landed, its PRESENTER.
///
/// <para>
/// Decodes <see cref="ViewModelNetState"/> — which sequence the arms should be
/// in, written by the same machine on both sides — and decides the two things
/// that are policy rather than mechanism: WHOSE eyes this entity belongs at,
/// and WHAT it plays. The local player's viewmodel goes into the renderer's
/// viewmodel pass (drawn at the eyes, viewmodel FOV, depth hack — see
/// <see cref="IEngineEntityRender.SetViewModel"/>); everyone else's keeps the
/// draw-nothing custom draw, because a pair of severed arms standing inside
/// every other player is worse than no viewmodel.
/// </para>
///
/// <para>
/// The sequence it presents comes from two writers: the decoded state (the
/// server's word, phase-correct for a client that is not predicting) and the
/// replay's working copy (<see cref="ClientArsenal"/> pushes after each
/// replayed command, which is what makes the fire animation start the frame
/// the trigger is pressed rather than a round trip later). Both go through
/// <see cref="Present"/>, which replays nothing to native unless the pair
/// (sequence, parity) actually moved — the parity bump is stock's own signal
/// that the same sequence must restart.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.PredictedViewModel)]
public sealed class ClientSsViewModel : ClientStandaloneEntity
{
    private readonly IEngineHud _screen;
    private readonly IWeaponDrawEffects _effects;
    private readonly IClientStandalones? _entities;

    /// <summary>A viewmodel whose weapon draws nothing beside it (a test's).</summary>
    public ClientSsViewModel(IEngineEntityRender render, IEngineHud screen,
                             IClientStandalones? entities = null)
        : this(render, screen, new NoWeaponDrawEffects(), entities)
    {
    }

    [Inject]
    public ClientSsViewModel(IEngineEntityRender render, IEngineHud screen, IWeaponDrawEffects effects,
                             IClientStandalones? entities = null)
        : base(render)
    {
        _screen = screen;
        _effects = effects;
        _entities = entities;
    }

    public override string ClassName => EntityNames.PredictedViewModel;

    /// <summary>The same type the server writes through, decoded per update.</summary>
    [NetState] public ViewModelNetState State = new();

    /// <summary>See ClientSsWeapon.Decodes — the replay's freshness gate.</summary>
    public int Decodes { get; private set; }

    /// <summary>Whether this viewmodel is the viewer's own — in the pass.</summary>
    public bool IsLocal { get; private set; }

    /// <summary>What was last handed to the renderer, so Present is edge-triggered.</summary>
    public int ShownSequence { get; private set; } = -1;
    public int ShownParity { get; private set; } = -1;

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        Decodes++;
        DecideAndPresent();
    }

    /// <summary>
    /// Re-decides whose eyes this belongs at and re-presents, for when the
    /// VIEWER moved rather than the viewmodel: the local player dying,
    /// respawning or entering an observer mode re-runs this through
    /// <see cref="ClientSsPlayer"/>'s edge notification.
    /// </summary>
    public void Reconsider() => DecideAndPresent();

    /// <summary>
    /// Whose eyes, then the sequence and skin — the tail of every update and
    /// of every <see cref="Reconsider"/>.
    /// </summary>
    private void DecideAndPresent()
    {
        // Whose eyes. Re-decided per update rather than once, because any of
        // the three terms can move: the owner is networked state, the local
        // index is 0 until the client is actually in a level, and the viewer's
        // own life is networked too.
        int local = _screen.LocalPlayerIndex;
        bool isLocal = local > 0 && State.OwnerIndex == local && ViewerMayDrawViewModel(local);

        if (isLocal != IsLocal)
        {
            IsLocal = isLocal;
            Render.SetViewModel(Handle, isLocal);

            if (!isLocal)
            {
                // Out of the pass and back to drawing nothing — the class
                // remarks say why a world-drawn viewmodel is worse than none.
                Render.SetCustomDraw(Handle, new Vector(-1, -1, -1), new Vector(1, 1, 1));
                ShownSequence = -1;
                ShownParity = -1;
            }
        }

        // The server's word. When the replay is also pushing, Present's edge
        // gate makes the second writer free: a held prediction decodes to the
        // same (sequence, parity) the replay already presented.
        Present(State.Sequence, State.AnimationParity, State.PlaybackRate);
        ShowSkin(State.Skin);
    }

    /// <summary>
    /// Whether the local player may see a viewmodel at all: alive, and not
    /// riding an observer camera.
    ///
    /// <para>
    /// Stock's <c>C_BaseViewModel::ShouldDraw</c> (c_baseviewmodel.cpp:250-268)
    /// is the citation: under HLTV/Replay it draws only in eye mode on its own
    /// owner; otherwise it falls to <c>C_BaseCombatWeapon::ShouldDraw</c>,
    /// which refuses anything not <c>DC_WEAPON_VIEWMODEL</c> while the
    /// thirdperson camera is out (the <c>m_bDrawViewmodel</c> the server sets
    /// exists only on LCS, view.cpp:1143-1153 — in Source 2013 the thirdperson
    /// and observer case is what drops the viewmodel, and the fork's
    /// viewrender.cpp:1011-1047 comment records the same for the owner's body:
    /// the view render skips it exactly when a first-person view has it). Our
    /// pass is decided managed-side, so the same rule lives here: a dead
    /// player is looking through a death cam or a chase camera, not through
    /// the viewmodel's eyes, and the arms must leave the pass with them.
    /// Without a registry (a hand-built test's viewmodel) the viewer's state
    /// cannot be asked and the old decision stands.
    /// </para>
    /// </summary>
    private bool ViewerMayDrawViewModel(int localIndex)
    {
        if (_entities is null)
            return true;

        return _entities.GetByIndex(localIndex) is ClientSsPlayer viewer
               && viewer.State.Alive
               && viewer.State.ObserverMode == (int)ObserverMode.None;
    }

    /// <summary>The skin last handed to the renderer, so <see cref="ShowSkin"/> is edge-triggered.</summary>
    public int ShownSkin { get; private set; }

    /// <summary>
    /// Puts the weapon's chosen skin on the drawn viewmodel, once per change:
    /// from the server's word and from each replayed command alike.
    /// </summary>
    public void ShowSkin(int skin)
    {
        if (!IsLocal || skin == ShownSkin)
            return;

        ShownSkin = skin;
        Render.SetSkin(Handle, skin);
    }

    /// <summary>
    /// Hands a sequence to the renderer, once per actual change.
    ///
    /// <para>
    /// A new sequence starts from its beginning; the SAME sequence with a
    /// parity that is AHEAD restarts — stock's AnimationParity contract, kept
    /// for stock's reason (a second shot must fire the animation again rather
    /// than being invisible). Anything else is a repeat and goes nowhere,
    /// which is what lets both the decode and every replayed command call this
    /// every time.
    /// </para>
    ///
    /// <para>
    /// <b>AHEAD rather than DIFFERENT, and the difference is a bug that was
    /// live.</b> Two writers reach here — the server's decode and the
    /// prediction replay — and they disagree whenever the prediction
    /// mispredicts, which for this entity it does and has been logging
    /// ("MISPREDICTION on 'predicted_viewmodel' — Sequence, AnimationParity"). The
    /// server's OLDER parity then arrived after the replay had already
    /// presented the newer one, and a plain inequality read that as a second
    /// restart. The animation was invisibly restarting several times per shot;
    /// what made it visible was the animation-event dispatcher, which fires a
    /// muzzle flash per restart and lit 77 of them in a second from a weapon
    /// that fires 13 times a second — then 43, then 7, then 45 on identical
    /// bursts, unstable because the amount of disagreement is.
    /// </para>
    /// </summary>
    public void Present(int sequence, int parity, float playbackRate)
    {
        if (!IsLocal || sequence < 0)
            return;

        if (sequence == ShownSequence && parity == ShownParity)
            return;

        bool restart = sequence == ShownSequence;

        // A restart of the sequence already showing has to be a parity that
        // moved FORWARD on its three-bit ring. Backwards is a stale packet
        // overtaken by prediction, and replaying it restarts an animation
        // nobody asked to restart.
        //
        // Only when the sequence is unchanged: a different sequence is a
        // different animation whatever its parity says, and refusing it on a
        // parity comparison would leave the wrong model pose on screen.
        if (restart && !AnimationParity.IsAhead(ShownParity, parity))
            return;

        ShownSequence = sequence;
        ShownParity = parity;

        Render.PlaySequence(Handle, sequence, playbackRate > 0f ? playbackRate : 1f, restart);
    }

    public override void OnSpawn()
    {
        // Draw nothing until the first update says whose this is — see the
        // class remarks. The local one leaves this state in OnDataChanged.
        Render.SetCustomDraw(Handle, new Vector(-1, -1, -1), new Vector(1, 1, 1));
    }

    public override int OnDraw() => 0;

    /// <summary>
    /// C_BaseViewModel::DrawModel's tail: the model has drawn, and its weapon
    /// draws what it hangs off it (ViewModelDrawn), in the viewmodel pass.
    /// </summary>
    public override int OnDrawExtras(int flags)
    {
        _effects.ViewModelDrawn(this);
        return -1;
    }
}
