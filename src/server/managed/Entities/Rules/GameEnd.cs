using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>Ends the round. The map's way of saying somebody won.</summary>
[ScriptedEntity(EntityNames.GameEnd)]
public sealed class GameEnd : ScriptedEntity
{
    public GameEnd(IEngineHost host, Intermission? intermission = null)
    {
        Host = host;
        _intermission = intermission;
    }

    // Stock's game_end calls g_pGameRules->EndMultiplayerGame(), which is
    // GoToIntermission in multiplayer.
    private readonly Intermission? _intermission;

    /// <summary>the process and the match.</summary>
    private readonly IEngineHost Host;
    [ScriptedEvent("EndGame")]
    private void OnEndGame()
    {
        if (_intermission is not null)
            _intermission.Begin(Host.CurrentTime);
        else
            Host.EndRound();
    }
}
