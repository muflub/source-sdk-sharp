//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Render;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// Where an attachment of the local viewmodel is for something drawn in the
/// WORLD's pass: stock's <c>C_BaseViewModel::FormatViewModelAttachment</c>,
/// which every <c>GetAttachment</c> on a viewmodel answers through.
/// </summary>
public interface IViewModelAttachmentFormat
{
    /// <summary>
    /// <paramref name="attachment"/>, a point on <paramref name="entity"/>
    /// as its skeleton places it, converted from the viewmodel's field of view
    /// to the world's when <paramref name="entity"/> is the local viewmodel;
    /// any other entity's point unchanged.
    /// </summary>
    Vector ForWorldPass(EntityHandle entity, Vector attachment);
}

/// <summary>
/// <see cref="IViewModelAttachmentFormat"/> over the live entities and the
/// frame's player view.
///
/// <para>
/// Only the LOCAL viewmodel: it is the one posed at the eye and drawn with
/// the viewmodel's projection. Stock never holds anyone else's (a viewmodel
/// is sent to its owner only); this build networks them and poses them where
/// the server put them, which no field-of-view conversion about this eye
/// describes.
/// </para>
/// </summary>
public sealed class ViewModelAttachmentFormat(IClientStandalones standalones, IPlayerView view) : IViewModelAttachmentFormat
{
    public Vector ForWorldPass(EntityHandle entity, Vector attachment)
    {
        if (standalones.GetByIndex(entity.EntryIndex) is not ClientSsViewModel { IsLocal: true } viewModel
            || viewModel.Handle != entity || !view.TryGet(out PlayerView player))
            return attachment;

        return ViewModelFov.Format(attachment, inverse: false, player);
    }
}
