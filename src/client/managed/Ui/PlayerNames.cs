using SourceSharp.Abi;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// The name a player is shown by: stock's UTIL_GetFilteredPlayerName
/// (util_shared.cpp), which every displayed name went through on its way out
/// of g_PR->GetPlayerName.
/// </summary>
public interface IPlayerNameFilter
{
    string Filter(string name);
}

/// <summary>No filter: the name as it came (stock's arm when Steam has no utils, and the tests' default).</summary>
public sealed class IdentityNameFilter : IPlayerNameFilter
{
    public string Filter(string name) => name;
}

/// <summary>
/// Player names as the UI shows them: the stock g_PR->GetPlayerName role, the
/// ONE place a screen asks for a player's name (plan_client_novalve C6, lane
/// c6-input). The engine's player info, its name passed through the filter.
/// </summary>
public interface IPlayerNames
{
    /// <summary>The engine's player info for <paramref name="slot"/>, the name filtered; false when the engine has none.</summary>
    bool TryGet(int slot, out ClientPlayerInfo info);
}

/// <summary><see cref="IPlayerNames"/> over the engine's player info.</summary>
public sealed class EnginePlayerNames(Func<IVtableEngineClient?> engine, IPlayerNameFilter filter) : IPlayerNames
{
    private IVtableEngineClient? _engine;

    public bool TryGet(int slot, out ClientPlayerInfo info)
    {
        _engine ??= engine();
        if (_engine is not { } e || !e.TryGetPlayerInfo(slot, out info))
        {
            info = default;
            return false;
        }

        info = info with { Name = filter.Filter(info.Name) };
        return true;
    }
}
