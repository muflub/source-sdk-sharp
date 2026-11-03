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
/// The client half of the viewmodel attachment: the weapon's own model riding
/// the hands.
///
/// <para>
/// Stock's <c>C_ViewmodelAttachmentModel</c> draws only when the owner is the
/// local player in first person, in the viewmodel pass, bonemerged onto the
/// viewmodel entity. The same three decisions here: the local player's
/// attachment joins the pass and merges onto the <c>predicted_viewmodel</c> the
/// server names — as stock's follows its viewmodel entity — and everyone
/// else's draws nothing, for the same reason the viewmodel itself does.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.SsViewmodelAttachment)]
public sealed class ClientSsViewmodelAttachment : ClientStandaloneEntity
{
    private readonly IEngineHud _screen;
    private readonly IClientStandalones? _entities;

    [Inject]
    public ClientSsViewmodelAttachment(IEngineEntityRender render, IEngineHud screen,
                                       IClientStandalones? entities = null)
        : base(render)
    {
        _screen = screen;
        _entities = entities;
    }

    public override string ClassName => EntityNames.SsViewmodelAttachment;

    /// <summary>The same state the server writes through, decoded per update.</summary>
    [NetState] public ViewmodelAttachmentNetState State = new();

    /// <summary>Whether this attachment is the local player's — in the pass.</summary>
    public bool IsLocal { get; private set; }

    // The viewmodel entity index the merge last pointed at, so the ask is
    // edge-triggered and follows a re-created viewmodel. -1 is none.
    private int _mergedOnto = -1;

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType) => Decide();

    /// <summary>
    /// Re-decides whose eyes this belongs at and re-merges, for when the
    /// VIEWER moved rather than the attachment — the same edge notification
    /// that re-runs <see cref="ClientSsViewModel.Reconsider"/>.
    /// </summary>
    public void Reconsider() => Decide();

    /// <summary>
    /// Whose eyes, then the merge — the whole of every update and of every
    /// <see cref="Reconsider"/>.
    /// </summary>
    private void Decide()
    {
        // Whose eyes, decided per update like the viewmodel's own: any of the
        // three terms can move, and the arms' own rule applies to the sleeve
        // merged onto them (see ClientSsViewModel.ViewerMayDrawViewModel for
        // the stock citation — the attachment is stock's
        // C_ViewmodelAttachmentModel, which draws from the same pass).
        int local = _screen.LocalPlayerIndex;
        bool isLocal = local > 0 && State.OwnerIndex == local && ViewerMayDrawViewModel(local);

        if (isLocal != IsLocal)
        {
            IsLocal = isLocal;
            Render.SetViewModel(Handle, isLocal);

            if (!isLocal)
                Render.SetCustomDraw(Handle, new Vector(-1, -1, -1), new Vector(1, 1, 1));
        }

        if (!isLocal)
        {
            if (_mergedOnto >= 0)
            {
                Render.SetBoneMergeSource(Handle, -1);
                _mergedOnto = -1;
            }
            return;
        }

        // The viewmodel the server named, by index: the skeleton is read when
        // the bones are built, so a viewmodel that has not arrived yet is
        // merged onto as soon as it does, with no second decode of this one.
        int source = State.ViewModelIndex;

        if (source != _mergedOnto)
        {
            Render.SetBoneMergeSource(Handle, source);
            _mergedOnto = source;
        }
    }

    /// <summary>
    /// Whether the local player may see a viewmodel at all: alive, and not
    /// riding an observer camera — the same rule as
    /// <see cref="ClientSsViewModel.ViewerMayDrawViewModel"/>, for the same
    /// stock reason; without a registry (a hand-built test's attachment) the
    /// viewer's state cannot be asked and the old decision stands.
    /// </summary>
    private bool ViewerMayDrawViewModel(int localIndex)
    {
        if (_entities is null)
            return true;

        return _entities.GetByIndex(localIndex) is ClientSsPlayer viewer
               && viewer.State.Alive
               && viewer.State.ObserverMode == (int)ObserverMode.None;
    }

    public override void OnSpawn()
    {
        // Draw nothing until the first update says whose this is — see the
        // class remarks.
        Render.SetCustomDraw(Handle, new Vector(-1, -1, -1), new Vector(1, 1, 1));
    }

    public override void OnRemoved()
    {
        IsLocal = false;
        _mergedOnto = -1;
    }
}
