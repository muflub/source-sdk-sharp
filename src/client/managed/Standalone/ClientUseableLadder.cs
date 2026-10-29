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
/// The client half of func_useableladder (stock's <c>C_FuncLadder</c>): the
/// ladder the client's prediction climbs. It draws nothing; it holds the
/// server's word for the two mount points and the dismount points it has
/// found, which <see cref="ClientUseableLadders"/> hands the movement loop.
/// </summary>
[ClientStandaloneEntity(EntityNames.FuncUseableladder)]
public sealed class ClientUseableLadder : ClientStandaloneEntity
{
    public ClientUseableLadder(IEngineEntityRender render)
        : base(render)
    {
    }

    public override string ClassName => EntityNames.FuncUseableladder;

    [NetState] public UseableLadderNetState State = new();

    private readonly List<ClientLadderDismount> _dismounts = new();
    private readonly List<Vector> _dismountOrigins = new();

    /// <summary>The dismount points found so far, in the order they were found.</summary>
    public IReadOnlyList<Vector> DismountOrigins => _dismountOrigins;

    /// <summary>
    /// <c>C_FuncLadder::SearchForDismountPoints</c>, the client arm: every
    /// dismount point this client knows of within 100 of the top, of the
    /// bottom, and of each point 40 apart up the ladder, added once. The client
    /// cannot see a point's target, so it claims every point near it, as
    /// stock's client does. Points found stay found: the list only grows.
    /// </summary>
    public void SearchForDismountPoints(IEnumerable<ClientLadderDismount> known)
    {
        var all = known as IReadOnlyCollection<ClientLadderDismount> ?? new List<ClientLadderDismount>(known);

        Vector top = State.Top;
        Vector bottom = State.Bottom;

        Vector bottomToTop = top - bottom;
        float ladderLength = MoveVec.Normalize(ref bottomToTop);

        FindNearby(all, top);
        FindNearby(all, bottom);

        while (true)
        {
            ladderLength -= 40.0f;
            if (ladderLength <= 0.0f)
                break;

            bottom += 40.0f * bottomToTop;
            FindNearby(all, bottom);
        }
    }

    private void FindNearby(IReadOnlyCollection<ClientLadderDismount> all, Vector origin)
    {
        const float radiusSqr = 100.0f * 100.0f;

        foreach (ClientLadderDismount spot in all)
        {
            Vector d = spot.State.Origin - origin;
            if (d.X * d.X + d.Y * d.Y + d.Z * d.Z >= radiusSqr)
                continue;

            if (!_dismounts.Contains(spot))
            {
                _dismounts.Add(spot);
                _dismountOrigins.Add(spot.State.Origin);
            }
        }
    }
}

/// <summary>
/// The client half of info_ladder_dismount (stock's
/// <c>C_InfoLadderDismount</c>): where a player may get off, for the client's
/// prediction.
/// </summary>
[ClientStandaloneEntity(EntityNames.InfoLadderDismount)]
public sealed class ClientLadderDismount : ClientStandaloneEntity
{
    public ClientLadderDismount(IEngineEntityRender render)
        : base(render)
    {
    }

    public override string ClassName => EntityNames.InfoLadderDismount;

    [NetState] public LadderDismountNetState State = new();
}
