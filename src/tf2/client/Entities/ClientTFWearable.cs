//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;

using TF2.Client.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

namespace TF2.Client.Entities;

/// <summary>
/// The client's <c>tf_wearable</c>: the worn cosmetic drawn on its owner's
/// bones, stock's <c>CEconWearable</c> client half (econ_wearable.cpp:354-460).
/// The model rides the networked block (the server's
/// <c>CEconWearable::Spawn</c> <c>SetModel</c>, econ_wearable.cpp:96); what a
/// decode decides here is the three things stock decides per frame:
///
/// <para>
/// THE BONES — <c>EF_BONEMERGE</c> (:115): the item's skeleton is the owner's,
/// set here through the merge follower, so the hat follows the body through
/// every gesture the animation state plays. THE SKIN — <c>m_nSkin</c> plus
/// <c>CTFWearable::GetSkin</c>'s client arm (tf_item_wearable.cpp:635-654): an
/// enemy viewer sees a disguised spy's wearables in the DISGUISE team's skin;
/// the viewer's own teammates and the spy's own eyes see the worn one. THE
/// HIDE — <c>CEconWearable::ShouldDraw</c>'s first-person arm (:375-393): while
/// the camera is inside the owner, the world copy goes with the body, whose
/// first-person frame the viewmodel serves.
/// </para>
///
/// <para>
/// The death and the departure need nothing here: the server's wearables die
/// with their player (<c>TFWearableSpawns</c>), and a removed entity's render
/// state goes with it — only the policy's wish, which outlives the entity, is
/// cleared, as the attached model's is. A re-disguise into the other team
/// between condition edges rides the next decode of the player's words.
/// </para>
/// </summary>
[ClientStandaloneEntity(TFEntityNames.Wearable)]
public sealed class ClientTFWearable : ClientStandaloneEntity
{
    private readonly IRenderPolicyWishes _policy;
    private readonly IEngineHud _screen;
    private readonly TFClientCarriers _carriers;
    private readonly TFClientPlayerResources _resources;

    [Inject]
    public ClientTFWearable(IEngineEntityRender render, IRenderPolicyWishes policy, IEngineHud screen,
                            TFClientCarriers carriers, TFClientPlayerResources resources)
        : base(render)
    {
        _policy = policy;
        _screen = screen;
        _carriers = carriers;
        _resources = resources;
    }

    public override string ClassName => TFEntityNames.Wearable;

    /// <summary>The same state the server writes through, decoded per update.</summary>
    [NetState] public TFWearableNetState State = new();

    // Whose skeleton the merge last pointed at, so the ask is edge-triggered
    // and follows a re-created owner. -1 is none.
    private int _mergedOnto = -1;

    // The skin the render was last set to; MinValue is not yet set.
    private int _skin = int.MinValue;

    // Whether the owner-seen wish last stood, so the ask is edge-triggered too.
    private bool _wishedOwnerSeen;

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType) => Redecide();

    /// <summary>
    /// Re-runs the three decisions: the wearable's own decode asks it, and so
    /// does the owner's disguise condition, through
    /// <see cref="TFWearableSkinFollow"/> — the skin is the one answer that
    /// moves without this entity's own state moving. Every arm is
    /// edge-triggered, so a re-run that decides the same is silent.
    /// </summary>
    public void Redecide()
    {
        // The bones: the owner is named by index, as every carried entity's is.
        int owner = State.OwnerIndex;

        if (owner > 0 && owner != _mergedOnto)
        {
            Render.SetBoneMergeSource(Handle, owner);
            _mergedOnto = owner;
        }
        else if (owner <= 0 && _mergedOnto >= 0)
        {
            Render.SetBoneMergeSource(Handle, -1);
            _mergedOnto = -1;
        }

        int skin = Skin();
        if (skin != _skin)
        {
            _skin = skin;
            Render.SetSkin(Handle, skin);
        }

        // Stock's first-person arm: the owner's own world copy hides while the
        // camera is inside them, per decode like the carried weapon's wish.
        bool wish = _screen.LocalPlayerIndex > 0 && State.OwnerIndex == _screen.LocalPlayerIndex;

        if (wish != _wishedOwnerSeen)
        {
            _wishedOwnerSeen = wish;
            _policy.WantOwnerSeen(Handle, wish);
        }
    }

    /// <summary>
    /// The worn skin: the server's (<c>CEconWearable::GetSkin</c>'s team
    /// ternary), unless this viewer is an enemy of a disguised owner —
    /// <c>CTFWearable::GetSkin</c>'s client arm (tf_item_wearable.cpp:635-654),
    /// whose <c>iLocalTeam &gt; LAST_SHARED_TEAM</c> and <code>pLocalPlayer !=
    /// pPlayer</code> guards are the two early returns here.
    /// </summary>
    private int Skin()
    {
        int owner = State.OwnerIndex;

        if (_resources.Live is not { } resources)
            return State.Skin;

        int local = _screen.LocalPlayerIndex;
        int localTeam = resources.State.Team[local];
        int ownerTeam = owner > 0 ? resources.State.Team[owner] : 0;

        if (local <= 0 || local == owner
            || !ClientTFPlayerData.IsEnemyViewer(localTeam, ownerTeam)
            || _carriers.Of(owner) is not { } data
            || !data.State.InCond(TFCond.Disguised))
        {
            return State.Skin;
        }

        // The disguise team's base skin, the body arm's ternary
        // ((int)state.DisguiseTeam - 2) over the wearable.
        return data.State.DisguiseTeam - 2;
    }

    public override void OnRemoved()
    {
        _mergedOnto = -1;
        _skin = int.MinValue;

        if (_wishedOwnerSeen)
        {
            _wishedOwnerSeen = false;
            _policy.WantOwnerSeen(Handle, false);
        }
    }
}

/// <summary>
/// The wearables' arm of the client condition driver: a disguise's start and
/// end re-skin every wearable the spy wears, for the viewers the change
/// matters to — the enemy's (<c>CTFWearable::GetSkin</c>'s client arm runs per
/// draw in stock, where ours is a push; the decode of the wearable itself
/// cannot see a condition that lives on the owner).
/// </summary>
[SideService(Side.Client)]
public sealed class TFWearableSkinFollow(IClientStandalones entities) : ITFClientConditionHook, IOrderedHook
{
    public int HookOrder => TFClientConditionOrders.WearableSkins;

    public void OnAdded(EntityHandle player, TFCond cond)
    {
        if (cond == TFCond.Disguised)
            Redress(player);
    }

    public void OnRemoved(EntityHandle player, TFCond cond)
    {
        if (cond == TFCond.Disguised)
            Redress(player);
    }

    private void Redress(EntityHandle player)
    {
        foreach (var entity in entities.Live())
            if (entity is ClientTFWearable wearable && wearable.State.OwnerIndex == player.EntryIndex)
                wearable.Redecide();
    }
}
