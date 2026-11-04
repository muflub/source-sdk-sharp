//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of a weapon's attached model — the flamethrower's pilot
/// light, stock's <c>DrawEconEntityAttachedModels</c> (econ_entity.cpp:95).
///
/// <para>
/// Stock hangs the same model from the weapon's bones twice: in the world
/// pass from the weapon's own draw (<c>CEconEntity::OnInternalDrawModel</c>,
/// econ_entity.cpp:1960), and — through <c>C_ViewmodelAttachmentModel</c> — in
/// the viewmodel pass for the carrier. One entity here stands in for both of
/// the bearer's copies by deciding per client, the way the viewmodel
/// attachment does: the LOCAL bearer's copy joins the viewmodel pass and
/// merges onto the host the server names (the hands' attachment, else the
/// viewmodel itself); every other client's copy stays in the world, merged
/// onto the weapon. What the local bearer gives up is their own world copy
/// while the viewmodel copy stands in — the third-person camera sees the
/// weapon's, not the model's second skeleton, which stock draws from a render
/// this hierarchy carries once.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.SsAttachedModel)]
public sealed class ClientSsAttachedModel : ClientStandaloneEntity
{
    private readonly IEngineHud _screen;
    private readonly IRenderPolicyWishes _policy;
    private readonly IClientStandalones? _entities;

    public ClientSsAttachedModel(IEngineEntityRender render, IEngineHud screen)
        : this(render, new NoRenderPolicyWishes(), screen)
    {
    }

    /// <summary>A flame whose world copy asks no render policy (a test's).</summary>
    public ClientSsAttachedModel(IEngineEntityRender render, IEngineHud screen, IClientStandalones? entities)
        : this(render, new NoRenderPolicyWishes(), screen, entities)
    {
    }

    [Inject]
    public ClientSsAttachedModel(IEngineEntityRender render, IRenderPolicyWishes policy, IEngineHud screen,
                                 IClientStandalones? entities = null)
        : base(render)
    {
        _policy = policy;
        _screen = screen;
        _entities = entities;
    }

    public override string ClassName => EntityNames.SsAttachedModel;

    /// <summary>The same state the server writes through, decoded per update.</summary>
    [NetState] public AttachedModelNetState State = new();

    /// <summary>Whether this copy is drawn at the bearer's own eyes.</summary>
    public bool InPass { get; private set; }

    // Whichever skeleton the merge last pointed at, so the ask is
    // edge-triggered and follows a re-created host. -1 is none.
    private int _mergedOnto = -1;

    // Whether the owner-seen wish last stood, so the ask is edge-triggered too.
    private bool _wishedOwnerSeen;

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType) => Decide();

    /// <summary>
    /// Re-decides on the VIEWER's move, the same edge notification
    /// <see cref="ClientSsViewModel.Reconsider"/> rides: a kill or a respawn
    /// updates the player, not the model hanging off their gun.
    /// </summary>
    public void Reconsider() => Decide();

    private void Decide()
    {
        // Whose eyes, the viewmodel's own rule (the model hangs off the gun the
        // bearer sees; see ClientSsViewModel.DecideAndPresent for the stock
        // citation).
        int local = _screen.LocalPlayerIndex;
        bool isLocal = local > 0 && State.OwnerIndex == local && BearerMayDraw(local);

        // Into the pass when the bearer is this client's and the model's flags
        // say the viewmodel pass gets it at all.
        bool inPass = isLocal && (State.Flags & (int)AttachedModelDisplayFlags.ViewModel) != 0;

        if (inPass != InPass)
        {
            InPass = inPass;
            Render.SetViewModel(Handle, inPass);
        }

        // The bones: the pass host in the pass, the weapon's in the world —
        // named by index like the viewmodel attachment's, so a host that has
        // not arrived yet is merged onto as soon as it does.
        int source = inPass ? State.HostIndex
                   : (State.Flags & (int)AttachedModelDisplayFlags.WorldModel) != 0 ? State.WeaponIndex
                   : -1;

        if (source > 0 && source != _mergedOnto)
        {
            Render.SetBoneMergeSource(Handle, source);
            _mergedOnto = source;
        }
        else if (source <= 0 && _mergedOnto >= 0)
        {
            Render.SetBoneMergeSource(Handle, -1);
            _mergedOnto = -1;
        }

        // The world copy obeys the carried weapon's own gate (stock draws it
        // FROM the weapon's draw, c_basecombatweapon.cpp:421): hidden while the
        // first-person camera is inside its bearer. In the pass the wish is
        // moot — the viewmodel rule answers first — and asked anyway, per
        // decode like the weapon's.
        bool wish = local > 0 && State.OwnerIndex == local;

        if (wish != _wishedOwnerSeen)
        {
            _wishedOwnerSeen = wish;
            _policy.WantOwnerSeen(Handle, wish);
        }
    }

    /// <summary>The viewmodel's own viewer gate, the same three terms.</summary>
    private bool BearerMayDraw(int localIndex)
    {
        if (_entities is null)
            return true;

        return _entities.GetByIndex(localIndex) is ClientSsPlayer bearer
               && bearer.State.Alive
               && bearer.State.ObserverMode == (int)ObserverMode.None;
    }

    public override void OnRemoved()
    {
        InPass = false;
        _mergedOnto = -1;

        if (_wishedOwnerSeen)
        {
            _wishedOwnerSeen = false;
            _policy.WantOwnerSeen(Handle, false);
        }
    }
}
