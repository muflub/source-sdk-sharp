using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The map's chapter title, read from worldspawn and announced by C# (Phase 12).
///
/// <para>
/// worldspawn itself is a permanent native adapter — it is edict 0, and the
/// engine names it by index rather than by handle — but its keys are this
/// side's to read, and <c>chaptertitle</c> is the only one of the thirteen whose
/// consequence is a game decision rather than engine plumbing. Stock spends it
/// inside <c>CWorld::Precache</c>: it creates an <c>env_message</c> by
/// classname, hands it the title, and triggers it one second later. That path
/// has been dead in this build since env_message became managed — the native
/// factory answers NULL for a managed classname — and it was dead on any
/// multiplayer server before that: with no activator and no SF_MESSAGE_ALL,
/// <c>CMessage::Use</c> falls back to <c>UTIL_GetLocalPlayer()</c>, which is
/// NULL whenever maxClients &gt; 1, so stock showed the title to nobody.
/// </para>
///
/// <para>
/// So the decision is remade here rather than transliterated, and differently
/// on purpose: a title announced one second after the world spawns reaches
/// nobody on a server where players arrive later. Each player is shown the
/// title once per level, on their first spawn — the same moment stock's
/// <c>gametitle</c> key already uses (player.cpp shows the game logo from
/// first spawn). The message is the same <c>HudText</c> a managed env_message
/// sends: the titles.txt NAME, resolved and drawn by the managed HUD.
/// Recorded in docs/divergences.md.
/// </para>
/// </summary>
public sealed class ChapterTitle
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    public ChapterTitle(ILog log, IUserMessageSender messages)
    {
        _log = log;
        Messages = messages;
    }

    /// <summary>putting the announcement on the wire.</summary>
    private readonly IUserMessageSender Messages;

    /// <summary>The level's title, or empty. Set once per level.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>
    /// Who has been shown it, by handle rather than by entity index: an index
    /// is reused the moment its player disconnects, and the newcomer on the
    /// same slot has not seen anything yet.
    /// </summary>
    private readonly HashSet<uint> _shown = new();

    /// <summary>
    /// A new level's title. Empty means the map set none, and the level shows
    /// nothing — which is every stock multiplayer map.
    /// </summary>
    public void LevelInit(string title)
    {
        Title = title ?? string.Empty;
        _shown.Clear();

        // Stock's DevMsg( 2, "Chapter title: ..." ), kept: it is the one
        // observable stock's own path had on a multiplayer server.
        if (Title.Length != 0)
            _log.Msg($"[SourceSharp/Server] chapter title: {Title}\n");
    }

    /// <summary>
    /// A player spawned. Answers whether the title went out to them — false on
    /// a respawn, on a titleless level, or on a title too long to send.
    /// </summary>
    public bool OnPlayerSpawned(EntityHandle player, int entIndex)
    {
        if (Title.Length == 0)
            return false;

        if (!_shown.Add(player.Value))
            return false;

        if (!Messages.Send(new HudText { Name = Title }, entIndex, reliable: true))
        {
            _log.Warning("[SourceSharp/Server] chapter title too long for HudText; not sent\n");
            return false;
        }

        return true;
    }
}
