using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The playing mode's id, replicated (plan_bots_teams_tf2 D.2): the client
/// reads team play and the mode's settings from it, rather than guessing team
/// deathmatch from the roster until someone joins a team.
/// </summary>
[ConVar("sv_mode_active", "", Flags = ConVarFlags.Replicated,
        Help = "Set by the server: the id of the game mode playing (sv_mode's)")]
public sealed class SvModeActiveConVar : StringConVar { }

/// <summary>
/// The catalog's way to the clients: the playing mode's id in
/// <c>sv_mode_active</c>, written when it changes (once a frame, after the mode
/// proxy settles), and the whole catalog, with whether this map can play each
/// mode, to each player as they come in.
/// </summary>
public sealed class ModeCatalogPublisher(GameModes modes, GameModeCatalog catalog, SvModeActiveConVar published, IUserMessageSender messages)
    : IFrameLifecycle, IClientLifecycle
{
    int IFrameLifecycle.FrameOrder => (int)FrameStage.ModeCatalog;

    void IFrameLifecycle.OnThink(float frameTime) => Publish();

    /// <summary>Writes the playing mode's id when it differs from what the clients were told.</summary>
    public void Publish()
    {
        string id = modes.ActiveId ?? "";
        if (published.Value != id)
            published.SetValue(id);
    }

    public int ClientLifecycleOrder => (int)LifecycleStage.ModeCatalog;

    public void OnClientActive(int entIndex) => Send(entIndex);

    /// <summary>The catalog, in pieces, to one player (or to everyone at 0).</summary>
    public void Send(int entIndex)
    {
        var pieces = AdminMenuText.Pieces(ModeCatalogText.Write(Entries(catalog)));
        for (int i = 0; i < pieces.Count; i++)
            messages.Send(new ModeCatalogPiece { Index = (byte)i, Count = (byte)pieces.Count, Text = pieces[i] }, entIndex, reliable: true);
    }

    /// <summary>Every mode with whether the map playing can play it.</summary>
    public static IReadOnlyList<CatalogMode> Entries(GameModeCatalog catalog)
        => [.. catalog.Modes().Select(m => new CatalogMode(m, catalog.Supports(catalog.CurrentMap, m.Id)))];
}
