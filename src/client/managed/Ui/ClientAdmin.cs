using System.Security.Cryptography;

using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>The server this client last joined, as the engine reports it: what an admin proof is signed for.</summary>
public sealed class ConnectedServer
{
    public string Address { get; private set; } = "";

    public void Joined(uint hostOrderIp, ushort port)
        => Address = $"{hostOrderIp >> 24}.{(hostOrderIp >> 16) & 0xFF}.{(hostOrderIp >> 8) & 0xFF}.{hostOrderIp & 0xFF}:{port}";
}

/// <summary>
/// This player's admin key: ECDSA P-256, in a file only they can read, outside every
/// game directory so no server, map or mod can reach it. Its public half sits beside
/// it, to copy into a server's cfg/admins.yaml.
/// </summary>
public sealed class AdminKeys(string path) : IAdminKeySource
{
    private const string P256 = "1.2.840.10045.3.1.7";

    public static string DefaultPath()
        => InFolder(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create));

    // No home to keep it in is an error: a relative path would put the key in the game's directory.
    internal static string InFolder(string folder)
        => Path.IsPathRooted(folder)
            ? Path.Combine(folder, "SourceSharp", "admin_key.pem")
            : throw new InvalidOperationException("this user has no folder to keep an admin key in");

    public bool HasKey => File.Exists(path);

    public string PublicKeyFile => path + ".pub";

    public string PublicKey => Load(out _) is { } key ? Public(key) : "";

    /// <summary>What went wrong with the last thing asked of it, or with the key on disk; a token.</summary>
    public string? Problem { get; private set; }

    /// <summary>The key, or null with <paramref name="problem"/> saying why (a token). Null with none when there is no key yet.</summary>
    public ECDsa? Load(out string? problem)
    {
        problem = null;
        if (!File.Exists(path))
            return null;
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite)) != 0)
        {
            problem = "#SS_UI_Admin_KeyExposed";
            return null;
        }
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problem = "#SS_UI_Admin_KeyUnreadable";
            return null;
        }
    }

    /// <summary>Makes a key only when there is none: an existing key is never overwritten by this.</summary>
    public void Generate()
    {
        Problem = null;
        if (HasKey)
            return;
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Write(key);
    }

    /// <summary>Takes a key from a file's path or as text, PEM or base64; what does not read as a P-256 key leaves the one there alone.</summary>
    public void Set(string pathOrKey)
    {
        Problem = null;
        string text = pathOrKey.Trim();
        try
        {
            if (File.Exists(text))
                text = File.ReadAllText(text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Problem = "#SS_UI_Admin_KeyUnreadable";
            return;
        }
        using var key = Parse(text);
        if (key is null)
        {
            Problem = "#SS_UI_Admin_KeyBad";
            return;
        }
        Write(key);
    }

    private static ECDsa? Parse(string text)
    {
        var key = ECDsa.Create();
        try
        {
            if (text.Contains("-----BEGIN", StringComparison.Ordinal))
                key.ImportFromPem(text);
            else
                key.ImportPkcs8PrivateKey(Convert.FromBase64String(text), out _);
            // A public key parses too, and cannot sign.
            if (key.ExportParameters(true).Curve.Oid.Value == P256)
                return key;
        }
        catch (Exception e) when (e is CryptographicException or ArgumentException or FormatException)
        {
        }
        key.Dispose();
        return null;
    }

    private static string Public(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    private void Write(ECDsa key)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Whole or not at all, and never readable by anyone else, not even for a moment.
        string temporary = path + ".new";
        File.Delete(temporary);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var file = new StreamWriter(temporary, options))
            file.Write(key.ExportPkcs8PrivateKeyPem());
        File.Move(temporary, path, overwrite: true);
        File.WriteAllText(PublicKeyFile, Public(key) + "\n");
    }
}

/// <summary>
/// The client's side of the admin menu. It answers a challenge only when this player
/// has just asked, and once: a server cannot collect signatures by sending challenges,
/// nor pop a menu up on its own. The menu's contents are the server's; a choice goes
/// back as its id. See ServerAdmins for who may do what.
/// </summary>
public sealed class ClientAdmin(RadialMenus radial, Action<byte[]> send, AdminKeys keys, ConnectedServer server, IFwdEngineClient engine, IGameClock clock, ILog log)
    : IAdminHook
{
    /// <summary>How long after the key the server may still answer.</summary>
    public const float AskSeconds = 10f;

    private readonly List<byte> _menu = [];
    private float _askedUntil = float.NegativeInfinity;
    private bool _proved;

    private bool Asked => clock.Now < _askedUntil;

    public void Open()
    {
        if (!engine.IsInGame())
            return;
        if (radial.ShowingFor(this))
        {
            radial.Close();
            return;
        }
        _askedUntil = clock.Now + AskSeconds;
        _proved = false;
        _menu.Clear();
        send(AdminWire.WriteOpen());
    }

    public void OnAdminChallenge(AdminChallenge challenge)
    {
        if (!Asked || _proved)
        {
            log.Warning("[SourceSharp/Admin] the server sent a challenge nobody asked for: ignored\n");
            return;
        }
        _proved = true;

        using var key = keys.Load(out string? problem);
        if (key is null)
        {
            Say(problem ?? "#SS_UI_Admin_NoKey");
            return;
        }
        if (server.Address.Length == 0)
        {
            Say("#SS_UI_Admin_NoAddress");
            return;
        }
        var signature = AdminProof.Sign(key, challenge.Nonce, server.Address);
        send(AdminWire.WriteProve(key.ExportSubjectPublicKeyInfo(), signature, server.Address));
    }

    public void OnAdminMenuPiece(AdminMenuPiece piece)
    {
        if (!Asked)
            return;
        _menu.AddRange(piece.Text);
        if (piece.Index + 1 < piece.Count)
            return;
        _askedUntil = float.NegativeInfinity;
        var lines = AdminMenuText.Read(System.Text.Encoding.UTF8.GetString(_menu.ToArray()));
        _menu.Clear();
        int at = 0;
        radial.Show(new RadialPage("#SS_UI_Admin_Title", Entries(lines, ref at, 0)), this, engine.IsInGame, "adminmenu");
    }

    public void OnAdminRefused(AdminRefused refused)
    {
        if (!Asked)
            return;
        _askedUntil = float.NegativeInfinity;
        Say((AdminRefusal)refused.Reason switch
        {
            AdminRefusal.LockedOut => "#SS_UI_Admin_LockedOut",
            _ => "#SS_UI_Admin_NotAdmin",
        });
    }

    private void Say(string token)
        => radial.Show(new RadialPage("#SS_UI_Admin_Title", [new RadialEntry(token, Enabled: false)]), this, engine.IsInGame, "adminmenu");

    // The lines at this depth, each folder holding the deeper ones that follow it.
    private List<RadialEntry> Entries(List<AdminMenuLine> lines, ref int at, int depth)
    {
        var entries = new List<RadialEntry>();
        while (at < lines.Count && lines[at].Depth == depth)
        {
            var line = lines[at++];
            if (line.Id.Length > 0)
            {
                string id = line.Id;
                entries.Add(new RadialEntry(line.Label, () => send(AdminWire.WriteRun(id)), KeepOpen: line.Stay));
                continue;
            }
            var inside = Entries(lines, ref at, depth + 1);
            if (inside.Count == 0)
                entries.Add(new RadialEntry(line.Label, Enabled: false, Detail: "#SS_UI_Admin_Nothing"));
            else
                entries.Add(new RadialEntry(line.Label, Open: () => new RadialPage(line.Label, inside)));
        }
        return entries;
    }
}

[ConCommand("adminmenu", Help = "The server's admin menu: at once for its host, else after proving your admin key")]
public sealed class AdminMenuCommand(ClientAdmin admin) : IConCommand
{
    public void Execute(string args) => admin.Open();
}

[ConCommand("ss_admin_keygen", Help = "Makes your admin key if you have none, and prints the line for a server's cfg/admins.yaml")]
public sealed class AdminKeygenCommand(AdminKeys keys, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        bool had = keys.HasKey;
        keys.Generate();
        log.Msg(had ? "[SourceSharp/Admin] you already have a key: it was kept\n" : "[SourceSharp/Admin] made a new key\n");
        log.Msg($"[SourceSharp/Admin] in the server's cfg/admins.yaml: admins: [{{ name: you, key: {keys.PublicKey} }}]\n");
    }
}

[ConCommand("ss_admin_pubkey", Help = "Prints your admin key's public half, for a server's cfg/admins.yaml")]
public sealed class AdminPubkeyCommand(AdminKeys keys, ILog log) : IConCommand
{
    public void Execute(string args)
        => log.Msg(keys.HasKey ? $"[SourceSharp/Admin] {keys.PublicKey}\n" : "[SourceSharp/Admin] no key yet: ss_admin_keygen makes one\n");
}
