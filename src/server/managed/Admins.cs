using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Mods;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>The address players reach this server at, where the machine's own interfaces do not show it (behind NAT).</summary>
[ConVar("sv_admin_address", "", Help = "The ip or ip:port players connect to this server by, when it is not one of the machine's own: admin keys are signed for it")]
public sealed class AdminAddressConVar : StringConVar;

/// <summary>Whether the address a proof was signed for is this server.</summary>
public static class AdminAddress
{
    /// <param name="claimed">The <c>ip:port</c> the client says it connected to.</param>
    /// <param name="port">The port this server is bound to.</param>
    /// <param name="own">This machine's interface addresses.</param>
    /// <param name="configured"><c>sv_admin_address</c>: an ip (on the bound port) or an ip:port (a forwarded one).</param>
    public static bool IsOurs(string claimed, int? port, IEnumerable<string> own, string? configured)
    {
        int colon = claimed.LastIndexOf(':');
        if (colon <= 0)
            return false;
        string ip = claimed[..colon];
        bool onOurPort = port is { } p && claimed[(colon + 1)..] == p.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(configured))
        {
            string named = configured.Trim();
            return named.Contains(':') ? named == claimed : named == ip && onOurPort;
        }
        return onOurPort && own.Contains(ip);
    }

    public static IEnumerable<string> OwnInterfaces()
        => NetworkInterface.GetAllNetworkInterfaces()
                           .Where(n => n.OperationalStatus is OperationalStatus.Up or OperationalStatus.Unknown)
                           .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                           .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                           .Select(a => a.Address.ToString());
}

/// <summary>
/// The server's side of the admin menu. A player is an admin when they host this listen
/// server, or have answered <see cref="AdminGate"/>'s challenge with a key in
/// cfg/admins.yaml. The menu is built and judged here, per admin: the client is told
/// what it may choose and names its choice by id, so nothing it sends is ever a command.
/// </summary>
public sealed class ServerAdmins : IAdminStatus, IClientLifecycle
{
    public const string ListFile = "cfg/admins.yaml";

    private readonly AdminGate _gate;
    private readonly bool _dedicated;
    private readonly IUserMessageSender _messages;
    private readonly IAdminMenu _menu;
    private readonly IPlayerNotices _notices;
    private readonly ILog _log;
    private readonly PlayerNames? _names;
    private readonly IConnectedClients? _clients;

    public ServerAdmins(AdminGate gate, bool dedicated, IUserMessageSender messages, IAdminMenu menu, IPlayerNotices notices, ILog log,
                        PlayerNames? names = null, IConnectedClients? clients = null)
    {
        _gate = gate;
        _dedicated = dedicated;
        _messages = messages;
        _menu = menu;
        _notices = notices;
        _log = log;
        _names = names;
        _clients = clients;
    }

    public static ServerAdmins For(IEngineHost host, IEngineFiles files, AdminAddressConVar adminAddress, IBoundPort port, IUserMessageSender messages,
                                   IAdminMenu menu, IPlayerNotices notices, IGameClock clock, ILog log, PlayerNames? names = null,
                                   IConnectedClients? clients = null)
    {
        IReadOnlyDictionary<string, string> List()
        {
            // Read at each use, so an edit needs no restart and a key taken off goes at once.
            var config = AdminsConfig.Parse(files.ReadGameFile(ListFile));
            foreach (string problem in config.Problems)
                log.Warning($"[SourceSharp/Admin] {ListFile}: {problem}\n");
            return config.Keys;
        }

        var gate = new AdminGate(() => clock.Now, List,
                                 address => AdminAddress.IsOurs(address, port.Read(), AdminAddress.OwnInterfaces(), adminAddress.Value),
                                 AdminGate.SecureNonce);
        return new ServerAdmins(gate, host.IsDedicatedServer, messages, menu, notices, log, names, clients);
    }

    /// <summary>A player as the log names them, so a gate can find the line: the name, the slot and the user id.</summary>
    public string Who(int slot)
    {
        string name = _names?.NameOf(slot) is { Length: > 0 } n ? n : "?";
        string id = _clients is null ? "?" : _clients.UserId(slot).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"{name} (slot {slot}, user id {id})";
    }

    public int ClientLifecycleOrder => (int)LifecycleStage.Admins;

    public bool IsAdmin(int slot) => IsHost(slot) || _gate.IsGranted(slot);

    private bool IsHost(int slot) => !_dedicated && slot == 1;

    public void OnClientDisconnected(int entIndex) => _gate.Forget(entIndex);

    public bool OnClientMessage(int entIndex, string key, ReadOnlySpan<byte> payload)
    {
        if (key != AdminWire.Key)
            return false;
        if (payload.IsEmpty)
            return true;

        switch (payload[0])
        {
            case AdminWire.Open:
                Open(entIndex);
                break;
            case AdminWire.Prove:
                Prove(entIndex, payload);
                break;
            case AdminWire.Run:
                Run(entIndex, Encoding.UTF8.GetString(payload[1..]));
                break;
        }
        return true;
    }

    private void Open(int slot)
    {
        if (IsAdmin(slot))
        {
            SendMenu(slot);
            return;
        }
        if (_gate.Challenge(slot) is { } nonce)
            _messages.Send(new AdminChallenge { Nonce = nonce }, slot, reliable: true);
        else
            Refuse(slot, AdminRefusal.LockedOut);
    }

    private void Prove(int slot, ReadOnlySpan<byte> payload)
    {
        // Malformed is a wrong answer: it spends the nonce and counts as a failure.
        AdminWire.TryReadProve(payload, out byte[] publicKey, out byte[] signature, out string address);
        if (_gate.Prove(slot, publicKey, signature, address, out string why))
        {
            _log.Msg($"[SourceSharp/Admin] slot {slot} proved the key of {_gate.NameOf(slot)} ({Fingerprint(publicKey)})\n");
            SendMenu(slot);
            return;
        }
        _log.Warning($"[SourceSharp/Admin] slot {slot} failed the challenge with key {Fingerprint(publicKey)}: {why}\n");
        Refuse(slot, _gate.LockedOut(slot) ? AdminRefusal.LockedOut : AdminRefusal.NotAdmin);
    }

    private void Run(int slot, string id)
    {
        if (!IsAdmin(slot))
        {
            _log.Warning($"[SourceSharp/Admin] {Who(slot)} is no admin and asked for {id}: refused\n");
            Refuse(slot, AdminRefusal.NotAllowed);
            _notices.Refused(slot, $"Admin: {id} refused: you are not an admin on this server");
            return;
        }
        if (AdminNode.Find(_menu.Build(slot), id) is { } node)
        {
            if (node.Take() is { } why)
            {
                // The admin who chose it hears why, as a player typing the command would.
                _log.Warning($"[SourceSharp/Admin] admin {Who(slot)} asked for {id}, refused: {why}\n");
                _notices.Refused(slot, $"Admin: {id} refused: {why}");
                return;
            }
            _log.Msg($"[SourceSharp/Admin] admin {Who(slot)} ran {id}\n");
            return;
        }
        // A player gone, or a map off the list, since the menu was sent.
        _log.Warning($"[SourceSharp/Admin] admin {Who(slot)} asked for {id}, which is not on the menu now\n");
        _notices.Refused(slot, $"Admin: {id} refused: it is not on the menu now");
    }

    private void SendMenu(int slot)
    {
        var pieces = AdminMenuText.Pieces(AdminMenuText.Write(AdminNode.Flatten(_menu.Build(slot))));
        for (int i = 0; i < pieces.Count; i++)
        {
            _messages.Send(new AdminMenuPiece { Index = (byte)i, Count = (byte)pieces.Count, Text = pieces[i] }, slot, reliable: true);
        }
    }

    private void Refuse(int slot, AdminRefusal why) => _messages.Send(new AdminRefused { Reason = (byte)why }, slot, reliable: true);

    /// <summary>Enough to tell keys apart in a log.</summary>
    private static string Fingerprint(byte[] publicKey) => Convert.ToHexString(SHA256.HashData(publicKey))[..16];
}

/// <summary>What an admin may do: built fresh for each menu and each choice.</summary>
public interface IAdminMenu
{
    IReadOnlyList<AdminNode> Build(int slot);
}

/// <summary>
/// What a mode adds to each player's folder in the admin menu (team
/// deathmatch's moves to a team): the core gives every player Kick and
/// Spectate, and each registered member adds its own.
/// </summary>
public interface IAdminPlayerActions
{
    /// <summary>The actions on <paramref name="player"/>, whose user id is <paramref name="userId"/>; ids name the user id.</summary>
    IEnumerable<AdminNode> ActionsFor(EntityHandle player, int userId);
}

/// <summary>
/// The game's admin menu (plan_bots_teams_tf2 D.4): Map (the candidates, and
/// the next map), Mode (the catalog's modes, those this map cannot play
/// unavailable with the reason), Players (kick, spectate and a mode's moves),
/// Settings (the playing mode's), Votes (call one as the server, cancel the
/// one running) and Restart, then every <see cref="IAdminMenuProvider"/>'s
/// folders (team deathmatch's Teams, the AI's Bots).
/// </summary>
public sealed class AdminMenuBuilder(IEngineConsole console, IEnginePlayers players, IEngineHost host, IEngineEnvironment environment,
                                     IConnectedClients clients, PlayerNames names, IEngineEntities entities, ServiceContainer services,
                                     GameModeCatalog catalog, ModeSettingWrites writes, MapCandidates candidates, ITeams teams) : IAdminMenu
{
    public IReadOnlyList<AdminNode> Build(int slot)
    {
        var items = CoreFolders(
        [
            (AdminCoreFolders.Map, () => AdminNode.Folder("#SS_UI_Admin_Map", Maps(candidates.All(), line => console.ExecuteServerCommand(line + "\n")))),
            (AdminCoreFolders.Mode, () => AdminNode.Folder("#SS_UI_Admin_Mode", Modes())),
            (AdminCoreFolders.Players, () => AdminNode.Folder("#SS_UI_Admin_Players", Players(slot))),
            (AdminCoreFolders.Settings, () => AdminNode.Folder("#SS_UI_Vote_Settings", Settings(catalog.Active?.Settings ?? [], writes.Current, writes.Write))),
            (AdminCoreFolders.Votes, () => AdminNode.Folder("#SS_UI_Admin_Votes", Votes())),
            (AdminCoreFolders.Restart, () => new AdminNode("#SS_UI_Admin_Restart", "restart", () => console.ExecuteServerCommand("mp_restartgame 1\n"))),
        ], services.ResolveAll<IAdminMenuScope>());
        foreach (var provider in services.ResolveAll<IAdminMenuProvider>())
            items.AddRange(provider.AdminItems(slot));
        return items;
    }

    /// <summary>
    /// The core's folders, each by its <see cref="AdminCoreFolders"/> key, that every one of
    /// <paramref name="scopes"/> offers; a hidden folder is never built.
    /// </summary>
    public static List<AdminNode> CoreFolders(IReadOnlyList<(string Key, Func<AdminNode> Make)> folders, IEnumerable<IAdminMenuScope> scopes)
    {
        var all = scopes.ToList();
        return [.. folders.Where(f => all.TrueForAll(s => s.Offers(f.Key))).Select(f => f.Make())];
    }

    /// <summary>
    /// Each candidate, by its label (the modes it plays, no nav), to change to
    /// now; then a Next map folder of the same, each setting <c>nextlevel</c>.
    /// </summary>
    public static List<AdminNode> Maps(IReadOnlyList<MapCandidate> maps, Action<string> run)
    {
        var items = maps.Select(c => new AdminNode(c.Label, "map:" + c.Map, () => run($"changelevel {c.Map}"))).ToList();
        if (maps.Count > 0)
            items.Add(AdminNode.Folder("#SS_UI_Admin_NextMap",
                                       [.. maps.Select(c => new AdminNode(c.Label, "nextmap:" + c.Map, () => run($"nextlevel {c.Map}")))]));
        return items;
    }

    /// <summary>
    /// The playing mode's settings with no vote: a folder for each the game has,
    /// of the values it does not hold now, every row the mode lists (an admin's
    /// alone and the unvotable too). Each choice is written through
    /// <paramref name="write"/>, which honours when the row applies and answers
    /// why a write is refused.
    /// </summary>
    public static List<AdminNode> Settings(IEnumerable<SettingSpec> settings, Func<string, string?> current, Func<string, string, string?> write)
        => GameVoteOptions.Of([], [], null, false, false, false, settings, current)
                          .GroupBy(o => o.Id[..o.Id.IndexOf('=')])
                          .Select(g => AdminNode.Folder(CoreModeSpecs.SettingToken(g.Key),
                                                        [.. g.Select(o => AdminNode.Refusable(o.Label, "setting:" + o.Id,
                                                                                              () => write(g.Key, o.Id[(o.Id.IndexOf('=') + 1)..])))]))
                          .ToList();

    /// <summary>
    /// The catalog's modes; one this map cannot play is shown unavailable with
    /// the reason (no flag, no control points). Resolved here, not constructed
    /// with: the mode proxy comes up after the services.
    /// </summary>
    private List<AdminNode> Modes()
        => services.Resolve<GameModes>() is { } modes
            ? Modes(catalog.Modes(), id => catalog.Supports(catalog.CurrentMap, id), id => modes.SwitchTo(id))
            : [];

    /// <summary>Each of <paramref name="modes"/>, switched to by <paramref name="switchTo"/>; one <paramref name="support"/> refuses is unavailable with its reason.</summary>
    public static List<AdminNode> Modes(IReadOnlyList<ModeSpec> modes, Func<string, MapSupport> support, Action<string> switchTo)
        => [.. modes.Select(m => new AdminNode(m.Title.StartsWith('#') ? m.Title : GameVoteOptions.ModeLabel(m.Id), "mode:" + m.Id,
                                               () => switchTo(m.Id),
                                               Unavailable: support(m.Id) is { Supported: false } no ? no.Reason : null))];

    /// <summary>
    /// A folder each player in the game: Kick (not the admin themselves, nor a
    /// listen server's host), Spectate (one not watching already), then each
    /// <see cref="IAdminPlayerActions"/>'s. By user id, which no later player
    /// reuses: a slot in a stale menu may have changed hands.
    /// </summary>
    private List<AdminNode> Players(int self)
    {
        var people = new List<AdminPlayer>();
        var globals = environment.Globals;
        for (int other = 1; other <= globals.MaxClients; other++)
        {
            EntityHandle player = players.GetHandle(other);
            if (entities.IsValid(player))
                people.Add(new AdminPlayer(other, player, clients.UserId(other), names.NameOf(other), teams.Team(player) == Server.Teams.TeamSpectator));
        }
        return Players(people, self, host.IsDedicatedServer, services.ResolveAll<IAdminPlayerActions>(),
                       p => clients.Kick(p.Slot, "Kicked by an admin"), p => teams.Join(p.Handle, Server.Teams.TeamSpectator));
    }

    /// <summary>One player in the game, as the Players folder lists them.</summary>
    public readonly record struct AdminPlayer(int Slot, EntityHandle Handle, int UserId, string Name, bool Spectating);

    /// <summary>
    /// A folder each of <paramref name="people"/>: Kick (not the admin in
    /// <paramref name="self"/>, nor a listen server's host), Spectate (one not
    /// watching already), then each of <paramref name="actions"/>'. By user id,
    /// which no later player reuses: a slot in a stale menu may have changed hands.
    /// </summary>
    public static List<AdminNode> Players(IEnumerable<AdminPlayer> people, int self, bool dedicated, IEnumerable<IAdminPlayerActions> actions,
                                          Action<AdminPlayer> kick, Action<AdminPlayer> spectate)
    {
        var folders = new List<AdminNode>();
        foreach (var person in people)
        {
            var items = new List<AdminNode>();
            if (person.Slot != self && (dedicated || person.Slot != 1))
                items.Add(new AdminNode("#SS_UI_Admin_Kick", $"kick:{person.UserId}", () => kick(person)));
            if (!person.Spectating)
                items.Add(new AdminNode("#SS_UI_Admin_Spectate", $"players:spectate:{person.UserId}", () => spectate(person)));
            foreach (var provider in actions)
                items.AddRange(provider.ActionsFor(person.Handle, person.UserId));
            if (items.Count > 0)
                folders.Add(AdminNode.Folder(person.Name, items));
        }
        return folders;
    }

    /// <summary>
    /// Call a vote as the server (each issue players may call, by kind, its
    /// refusals shown unavailable), and cancel the one running. The admin's own
    /// changes are the other folders' actions, which need no vote. Resolved
    /// here: the votes take the admins, who take this menu.
    /// </summary>
    private List<AdminNode> Votes()
        => services.Resolve<ServerVotes>() is { } votes && services.Resolve<IVoteOptions>() is { } options
            ? Votes(options.Build(), options.Refusal, votes.CallAsServer, votes.Cancel, votes.Running)
            : [];

    /// <summary>
    /// A Call folder of <paramref name="choices"/> by kind (a setting's by its
    /// row), each <paramref name="call"/>ed as the server, one
    /// <paramref name="refusal"/> refuses unavailable with the reason; and
    /// Cancel, unavailable unless a vote is <paramref name="running"/>.
    /// </summary>
    public static List<AdminNode> Votes(IReadOnlyList<VoteOption> choices, Func<VoteOption, string?> refusal, Func<VoteOption, string?> call,
                                        Func<string?> cancel, bool running)
    {
        AdminNode Call(VoteOption o)
            => AdminNode.Refusable(o.Label.Length > 0 ? o.Label : KindLabel(o.Kind), $"vote:{VoteOptionWords.WordOf(o.Kind)}:{o.Id}",
                                   () => call(o), unavailable: refusal(o));
        AdminNode Group(IGrouping<string, VoteOption> g)
        {
            VoteKind kind = g.First().Kind;
            if (g.Count() == 1 && g.First().Label.Length == 0)
                return Call(g.First());
            return AdminNode.Folder(kind == VoteKind.Setting ? CoreModeSpecs.SettingToken(g.Key) : KindLabel(kind), [.. g.Select(Call)]);
        }
        var calls = choices.GroupBy(o => o.Kind == VoteKind.Setting ? o.Id[..Math.Max(0, o.Id.IndexOf('='))] : o.Kind.ToString())
                           .Select(Group)
                           .ToList();
        return
        [
            AdminNode.Folder("#SS_UI_Admin_VoteCall", calls),
            AdminNode.Refusable("#SS_UI_Admin_VoteCancel", "vote:cancel", cancel, unavailable: running ? null : "no vote is running"),
        ];
    }

    /// <summary>What the vote menus call a kind.</summary>
    public static string KindLabel(VoteKind kind) => kind switch
    {
        VoteKind.Map => "#SS_UI_Vote_ChangeMap",
        VoteKind.NextMap => "#SS_UI_Vote_NextMap",
        VoteKind.Mode => "#SS_UI_Vote_Mode",
        VoteKind.Restart => "#SS_UI_Vote_Restart",
        VoteKind.Scramble => "#SS_UI_Admin_Scramble",
        VoteKind.Extend => "#SS_UI_Vote_Extend",
        VoteKind.Setting => "#SS_UI_Vote_Settings",
        _ => kind.ToString(),
    };
}
