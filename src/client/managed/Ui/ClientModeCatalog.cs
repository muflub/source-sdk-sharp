using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>Client half of <c>sv_mode_active</c>: the id of the mode the server plays.</summary>
[ConVar("sv_mode_active", "", Flags = ConVarFlags.Replicated,
        Help = "Set by the server: the id of the game mode playing (sv_mode's)")]
public sealed class ClModeActiveConVar : StringConVar { }

/// <summary>The mode the server plays now, as this client knows it (team play reads it).</summary>
public interface IActiveMode
{
    /// <summary>Whether the catalog is a server's (true) or this client's own build (false).</summary>
    bool FromServer { get; }

    /// <summary>The id of the mode the server plays now; null with no server, or one that has not said.</summary>
    string? ActiveModeId { get; }

    /// <summary>The mode the server plays now, from the catalog; null when unknown.</summary>
    ModeSpec? ActiveMode { get; }
}

/// <summary>
/// The client's copy of the game-mode catalog (plan_bots_teams_tf2 D.2): the
/// pieces the server sends as this player comes in, joined; the playing mode's
/// id from the replicated <c>sv_mode_active</c>; and, with no server's copy (at
/// the main menu, or before the pieces arrive), the core's own modes as this
/// client builds them. A level going drops the server's copy, so the next
/// level's (another server's, maybe) replaces it.
/// </summary>
public sealed class ClientModeCatalog(ClModeActiveConVar active, ILog log) : ICatalogSource, IActiveMode, IModeCatalogHook, IClientLevelHook, IOrderedHook
{
    private readonly List<byte> _bytes = [];
    private IReadOnlyList<CatalogMode>? _server;
    private IReadOnlyList<CatalogMode>? _own;

    /// <summary>Its own place on the level chain (whose members each hold one).</summary>
    public int HookOrder => 30;

    public IReadOnlyList<CatalogMode> Modes => _server ?? (_own ??= [.. CoreModeSpecs.All().Select(m => new CatalogMode(m, new MapSupport(true, "")))]);

    public bool FromServer => _server is not null;

    public string? ActiveModeId => active.Value is { Length: > 0 } id ? id : null;

    public ModeSpec? ActiveMode
        => ActiveModeId is { } id ? Modes.FirstOrDefault(m => string.Equals(m.Spec.Id, id, StringComparison.OrdinalIgnoreCase))?.Spec : null;

    public void OnModeCatalogPiece(ModeCatalogPiece piece)
    {
        if (piece.Index == 0)
            _bytes.Clear();
        _bytes.AddRange(piece.Text);
        if (piece.Index + 1 < piece.Count)
            return;
        _server = ModeCatalogText.Read(System.Text.Encoding.UTF8.GetString(_bytes.ToArray()));
        _bytes.Clear();
        log.Msg($"[SourceSharp/Modes] catalog: {string.Join(", ", _server.Select(m => m.Spec.Id))}\n");
    }

    void IClientLevelHook.OnLevelShutdown()
    {
        _server = null;
        _bytes.Clear();
    }
}
