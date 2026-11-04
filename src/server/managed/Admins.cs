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
    private readonly ILog _log;

    public ServerAdmins(AdminGate gate, bool dedicated, IUserMessageSender messages, IAdminMenu menu, ILog log)
    {
        _gate = gate;
        _dedicated = dedicated;
        _messages = messages;
        _menu = menu;
        _log = log;
    }

    public static ServerAdmins For(IEngineHost host, IEngineFiles files, IEngineConsole console, IBoundPort port, IUserMessageSender messages,
                                   IAdminMenu menu, IGameClock clock, ILog log)
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
                                 address => AdminAddress.IsOurs(address, port.Read(), AdminAddress.OwnInterfaces(), console.FindConVarValue("sv_admin_address")),
                                 AdminGate.SecureNonce);
        return new ServerAdmins(gate, host.IsDedicatedServer, messages, menu, log);
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
            _log.Warning($"[SourceSharp/Admin] slot {slot} is no admin and asked for {id}\n");
            Refuse(slot, AdminRefusal.NotAllowed);
            return;
        }
        if (AdminNode.Find(_menu.Build(slot), id) is { Run: { } run })
        {
            _log.Msg($"[SourceSharp/Admin] slot {slot} ran {id}\n");
            run();
            return;
        }
        // A player gone, or a map off the list, since the menu was sent.
        _log.Warning($"[SourceSharp/Admin] slot {slot} asked for {id}, which is not on the menu now\n");
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

/// <summary>The game's: its maps and modes, its players and the mods' additions.</summary>
public sealed class AdminMenuBuilder(IEngineFiles files, IEngineConsole console, IEnginePlayers players, IEngineHost host, IEngineEnvironment environment,
                                     IConnectedClients clients, PlayerNames names, IEngineEntities entities, ServiceContainer services) : IAdminMenu
{
    public IReadOnlyList<AdminNode> Build(int slot)
    {
        var items = new List<AdminNode>
        {
            AdminNode.Folder("#SS_UI_Admin_Map", Maps()),
            AdminNode.Folder("#SS_UI_Admin_Mode", Modes()),
            AdminNode.Folder("#SS_UI_Admin_Kick", Kicks(slot)),
            AdminNode.Folder("#SS_UI_Vote_Settings", Settings(GameVoteOptions.SettingsOf(services), console.FindConVarValue,
                                                              command => console.ExecuteServerCommand(command + "\n"))),
            new("#SS_UI_Admin_Restart", "restart", () => console.ExecuteServerCommand("mp_restartgame 1\n")),
        };
        foreach (var provider in services.ResolveAll<IAdminMenuProvider>())
            items.AddRange(provider.AdminItems(slot));
        return items;
    }

    /// <summary>
    /// The game's settings, as the vote menu has them and with no vote: a folder for each
    /// the game has, of the values it does not hold now. Not the vote's convars: those say
    /// what players may call, and an admin needs no call.
    /// </summary>
    public static List<AdminNode> Settings(IEnumerable<VoteSetting> settings, Func<string, string?> current, Action<string> run)
        => GameVoteOptions.Of([], [], null, false, false, false, settings, current)
                          .GroupBy(o => o.Id[..o.Id.IndexOf('=')])
                          .Select(g => AdminNode.Folder("#SS_UI_Vote_Setting_" + g.Key,
                                                        [.. g.Select(o => new AdminNode(o.Label, "setting:" + o.Id, () => run(o.Command)))]))
                          .ToList();

    private List<AdminNode> Maps()
        => Intermission.ReadCycle(files, console.FindConVarValue("mapcyclefile"))
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .Select(map => new AdminNode(map, "map:" + map, () => console.ExecuteServerCommand($"changelevel {map}\n")))
                       .ToList();

    // Resolved here, not constructed with: the mode proxy comes up after the services.
    private List<AdminNode> Modes()
    {
        if (services.Resolve<GameModes>() is not { } modes)
            return [];
        return modes.Registered.Select(m => new AdminNode(m.Id, "mode:" + m.Id, () => modes.SwitchTo(m.Id))).ToList();
    }

    // By user id, which no later player reuses: a slot in a stale menu may have changed hands.
    private List<AdminNode> Kicks(int self)
    {
        var kicks = new List<AdminNode>();
        var globals = environment.Globals;
        for (int other = 1; other <= globals.MaxClients; other++)
        {
            if (other == self || (!host.IsDedicatedServer && other == 1))
                continue;
            if (!entities.IsValid(players.GetHandle(other)))
                continue;
            int target = other;
            kicks.Add(new AdminNode(names.NameOf(other), $"kick:{clients.UserId(other)}", () => clients.Kick(target, "Kicked by an admin")));
        }
        return kicks;
    }
}
