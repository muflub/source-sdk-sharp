//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The server's useable ladders, as the movement loop reads them: stock's
/// <c>CFuncLadder::s_Ladders</c> and <c>CInfoLadderDismount::s_Dismounts</c>,
/// as a service rather than two statics. Each func_useableladder and
/// info_ladder_dismount joins at spawn and leaves when removed; a level's end
/// empties both.
/// </summary>
public sealed class UseableLadders : ILadderWorld, IGameLifecycle
{
    private readonly ConVarRegistry _convars;
    private readonly IEnginePlayers _players;
    private readonly Func<MoveConstants> _constants;

    private readonly List<FuncUseableLadder> _ladders = new();
    private readonly List<InfoLadderDismount> _dismounts = new();

    /// <param name="convars">This side's ConVars, for the three ladder ones.</param>
    /// <param name="players">The players, to name the one a ladder hears about.</param>
    /// <param name="constants">The movement constants, for the player's hull a ladder settles its points with.</param>
    public UseableLadders(ConVarRegistry convars, IEnginePlayers players, Func<MoveConstants> constants)
    {
        _convars = convars ?? throw new ArgumentNullException(nameof(convars));
        _players = players ?? throw new ArgumentNullException(nameof(players));
        _constants = constants ?? throw new ArgumentNullException(nameof(constants));
    }

    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.UseableLadders;

    void IGameLifecycle.OnLevelShutdown()
    {
        _ladders.Clear();
        _dismounts.Clear();
    }

    /// <summary>The player's hull (VEC_HULL_MIN / VEC_HULL_MAX), from the movement constants.</summary>
    public MoveConstants Hull() => _constants();

    public void Add(FuncUseableLadder ladder)
    {
        if (!_ladders.Contains(ladder))
            _ladders.Add(ladder);
    }

    public void Remove(FuncUseableLadder ladder) => _ladders.Remove(ladder);

    public void AddDismount(InfoLadderDismount dismount)
    {
        if (!_dismounts.Contains(dismount))
            _dismounts.Add(dismount);
    }

    public void RemoveDismount(InfoLadderDismount dismount) => _dismounts.Remove(dismount);

    /// <summary>Every dismount point, in spawn order: what FindEntityByClassnameWithin walks.</summary>
    public IReadOnlyList<InfoLadderDismount> AllDismounts => _dismounts;

    /// <summary>Every ladder, in spawn order.</summary>
    public IReadOnlyList<FuncUseableLadder> AllLadders => _ladders;

    public int Count => _ladders.Count;

    public UseableLadder At(int index) => Of(_ladders[index]);

    public bool TryGet(int entityIndex, out UseableLadder ladder)
    {
        if (Find(entityIndex) is { } found)
        {
            ladder = Of(found);
            return true;
        }

        ladder = default;
        return false;
    }

    public IReadOnlyList<Vector> Dismounts(int ladderIndex)
        => Find(ladderIndex) is { } ladder ? ladder.DismountOrigins : Array.Empty<Vector>();

    public LadderConVars ConVars => new(
        AutoDismount: _convars.Get<SvAutoLadderDismountConVar>()?.Value ?? true,
        AutoMountDot: _convars.Get<SvLadderAutoMountDotConVar>()?.Value ?? 0.4f,
        UseOnly: _convars.Get<SvLadderUseOnlyConVar>()?.Value ?? false);

    public void PlayerGotOn(int ladderIndex, int playerIndex)
        => Find(ladderIndex)?.PlayerGotOn(_players.GetHandle(playerIndex));

    public void PlayerGotOff(int ladderIndex, int playerIndex)
        => Find(ladderIndex)?.PlayerGotOff(_players.GetHandle(playerIndex));

    private FuncUseableLadder? Find(int entityIndex)
    {
        foreach (FuncUseableLadder ladder in _ladders)
        {
            if (ladder.Handle.EntryIndex == entityIndex)
                return ladder;
        }

        return null;
    }

    private static UseableLadder Of(FuncUseableLadder ladder)
        => new(ladder.Handle.EntryIndex, ladder.State.Top, ladder.State.Bottom,
               Enabled: !ladder.State.Disabled, Fake: ladder.State.Fake);
}
