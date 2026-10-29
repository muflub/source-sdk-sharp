//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

namespace SourceSharp.Client;

/// <summary>
/// The client's useable ladders, as the prediction's movement loop reads them
/// (stock's client <c>CFuncLadder::s_Ladders</c> and
/// <c>CInfoLadderDismount::s_Dismounts</c>): the live
/// <see cref="ClientUseableLadder"/> and <see cref="ClientLadderDismount"/>
/// entities, asked of the directory rather than kept.
///
/// <para>
/// A ladder's dismount points are searched for when the loop asks for them,
/// which is stock's client searching when the player gets on
/// (<c>SetLadder</c>): the points arrive with the PVS, so a list built once at
/// the first update could be missing ones a later update brought.
/// </para>
///
/// <para>
/// The ladder never hears about a player here: OnPlayerGotOnLadder, its sound
/// and OnPlayerGotOffLadder are the server's.
/// </para>
/// </summary>
public sealed class ClientUseableLadders(IClientStandalones standalones, ConVarRegistry convars) : ILadderWorld
{
    // One command's ladders: Count fills it and At reads it, as the loop asks.
    private readonly List<ClientUseableLadder> _ladders = new();

    public int Count
    {
        get
        {
            _ladders.Clear();
            foreach (var entity in standalones.OfClass(EntityNames.FuncUseableladder))
            {
                if (entity is ClientUseableLadder ladder)
                    _ladders.Add(ladder);
            }

            return _ladders.Count;
        }
    }

    public UseableLadder At(int index) => Of(_ladders[index]);

    public bool TryGet(int entityIndex, out UseableLadder ladder)
    {
        if (entityIndex >= 0 && standalones.GetByIndex(entityIndex) is ClientUseableLadder found)
        {
            ladder = Of(found);
            return true;
        }

        ladder = default;
        return false;
    }

    public IReadOnlyList<Vector> Dismounts(int ladderIndex)
    {
        if (ladderIndex < 0 || standalones.GetByIndex(ladderIndex) is not ClientUseableLadder ladder)
            return Array.Empty<Vector>();

        var known = new List<ClientLadderDismount>();
        foreach (var entity in standalones.OfClass(EntityNames.InfoLadderDismount))
        {
            if (entity is ClientLadderDismount spot)
                known.Add(spot);
        }

        ladder.SearchForDismountPoints(known);
        return ladder.DismountOrigins;
    }

    public LadderConVars ConVars => new(
        AutoDismount: convars.Get<SvAutoLadderDismountConVar>()?.Value ?? true,
        AutoMountDot: convars.Get<SvLadderAutoMountDotConVar>()?.Value ?? 0.4f,
        UseOnly: convars.Get<SvLadderUseOnlyConVar>()?.Value ?? false);

    public void PlayerGotOn(int ladderIndex, int playerIndex)
    {
    }

    public void PlayerGotOff(int ladderIndex, int playerIndex)
    {
    }

    private static UseableLadder Of(ClientUseableLadder ladder)
        => new(ladder.Index, ladder.State.Top, ladder.State.Bottom,
               Enabled: !ladder.State.Disabled, Fake: ladder.State.Fake);
}
