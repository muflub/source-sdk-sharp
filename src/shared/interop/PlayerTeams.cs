namespace SourceSharp.Abi;

/// <summary>
/// Stock's <c>CBaseEntity::ChangeTeam</c> store: a player's team by handle,
/// which names one player for its life. Unassigned (0) until set; -1 for a
/// handle that is not a player, since 0 is a real team. The caller says
/// whether the handle is a player now.
/// </summary>
internal sealed class PlayerTeams
{
    private readonly Dictionary<EntityHandle, int> _teams = new();

    public int Get(EntityHandle player, bool isPlayer)
    {
        if (isPlayer)
            return _teams.GetValueOrDefault(player);

        // A gone player's handle never names a player again.
        _teams.Remove(player);
        return -1;
    }

    public void Set(EntityHandle player, int team, bool isPlayer)
    {
        if (isPlayer)
            _teams[player] = team;
    }
}
