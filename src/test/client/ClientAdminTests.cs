using System.Security.Cryptography;

using SourceSharp.Abi;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>This player's admin key on disk.</summary>
public sealed class AdminKeysTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ss-admin-").FullName;

    private string KeyFile => Path.Combine(_dir, "nested", "admin_key.pem");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private AdminKeys Keys() => new(KeyFile);

    [Fact]
    public void The_key_s_home_is_outside_the_game()
    {
        string path = AdminKeys.DefaultPath();
        Assert.True(Path.IsPathRooted(path), path);
        Assert.EndsWith(Path.Combine("SourceSharp", "admin_key.pem"), path, StringComparison.Ordinal);
    }

    /// <summary>What .NET answers for a folder that does not exist yet, when not told to make it.</summary>
    [Fact]
    public void No_folder_is_refused_rather_than_a_relative_path_in_the_game_s_directory()
        => Assert.Throws<InvalidOperationException>(() => AdminKeys.InFolder(""));

    [Fact]
    public void There_is_no_key_until_one_is_made()
    {
        var keys = Keys();
        Assert.False(keys.HasKey);
        Assert.Equal("", keys.PublicKey);
        Assert.Null(keys.Load(out string? problem));
        Assert.Null(problem);
    }

    [Fact]
    public void A_generated_key_is_kept_and_signs()
    {
        var keys = Keys();
        keys.Generate();
        Assert.True(keys.HasKey);

        using var key = new AdminKeys(KeyFile).Load(out _);
        Assert.NotNull(key);
        var nonce = new byte[AdminProof.NonceBytes];
        Assert.True(AdminProof.Verify(Convert.FromBase64String(keys.PublicKey), AdminProof.Sign(key!, nonce, "a:1"), nonce, "a:1"));
        Assert.Equal(keys.PublicKey + "\n", File.ReadAllText(keys.PublicKeyFile));
    }

    [Fact]
    public void Generating_never_replaces_a_key()
    {
        var keys = Keys();
        keys.Generate();
        string first = keys.PublicKey;
        keys.Generate();
        Assert.Equal(first, keys.PublicKey);
    }

    [Fact]
    public void Only_its_owner_can_read_the_key_file()
    {
        if (OperatingSystem.IsWindows())
            return;
        Keys().Generate();
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(KeyFile));
    }

    [Fact]
    public void A_key_others_can_read_is_refused_rather_than_used()
    {
        if (OperatingSystem.IsWindows())
            return;
        var keys = Keys();
        keys.Generate();
        File.SetUnixFileMode(KeyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        Assert.Null(keys.Load(out string? problem));
        Assert.Equal("#SS_UI_Admin_KeyExposed", problem);
    }

    [Fact]
    public void A_key_can_be_set_from_a_pem_file()
    {
        using var theirs = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string file = Path.Combine(_dir, "theirs.pem");
        File.WriteAllText(file, theirs.ExportPkcs8PrivateKeyPem());

        var keys = Keys();
        keys.Set(file);
        Assert.Null(keys.Problem);
        Assert.Equal(Convert.ToBase64String(theirs.ExportSubjectPublicKeyInfo()), keys.PublicKey);
    }

    [Fact]
    public void A_key_can_be_set_as_base64_and_replaces_the_one_there()
    {
        var keys = Keys();
        keys.Generate();
        using var theirs = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        keys.Set(Convert.ToBase64String(theirs.ExportPkcs8PrivateKey()));
        Assert.Equal(Convert.ToBase64String(theirs.ExportSubjectPublicKeyInfo()), keys.PublicKey);
    }

    public static TheoryData<string> NotKeys()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var p256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var rsa = RSA.Create(2048);
        return
        [
            "not a key",
            "AAAA",
            "",
            Convert.ToBase64String(other.ExportPkcs8PrivateKey()),
            other.ExportPkcs8PrivateKeyPem(),
            rsa.ExportPkcs8PrivateKeyPem(),
            // The public half alone cannot sign.
            p256.ExportSubjectPublicKeyInfoPem(),
        ];
    }

    [Theory]
    [MemberData(nameof(NotKeys))]
    public void What_is_no_p256_private_key_is_refused_and_leaves_the_key_there(string text)
    {
        var keys = Keys();
        keys.Generate();
        string kept = keys.PublicKey;

        keys.Set(text);
        Assert.Equal("#SS_UI_Admin_KeyBad", keys.Problem);
        Assert.Equal(kept, keys.PublicKey);
    }
}

/// <summary>The client's answers: a server asks, and this player has to have asked first.</summary>
public sealed class ClientAdminTests : IDisposable
{
    private const string Address = "203.0.113.5:27015";

    private sealed class Pointer : IRadialPointer
    {
        public void Centre()
        {
        }
    }

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 100f;
    }

    private readonly string _dir = Directory.CreateTempSubdirectory("ss-admin-").FullName;
    private readonly List<byte[]> _sent = [];
    private readonly RadialMenus _radial = new(new Pointer());
    private readonly Clock _clock = new();
    private readonly ChatFeedTests.RecordingEngineClient _engine = new([]);
    private readonly AdminKeys _keys;
    private readonly ClientAdmin _admin;

    public ClientAdminTests()
    {
        _keys = new AdminKeys(Path.Combine(_dir, "key.pem"));
        var server = new ConnectedServer();
        server.Joined(0xCB007105, 27015);
        _admin = new ClientAdmin(_radial, _sent.Add, _keys, server, _engine, _clock, new RecordingLog());
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static AdminChallenge Nonce(byte fill = 7)
    {
        var nonce = new byte[AdminProof.NonceBytes];
        Array.Fill(nonce, fill);
        return new AdminChallenge { Nonce = nonce };
    }

    private void Menu(params AdminMenuLine[] lines)
    {
        var pieces = AdminMenuText.Pieces(AdminMenuText.Write(lines));
        for (int i = 0; i < pieces.Count; i++)
            _admin.OnAdminMenuPiece(new AdminMenuPiece { Index = (byte)i, Count = (byte)pieces.Count, Text = pieces[i] });
    }

    [Fact]
    public void The_key_opens_by_asking_the_server()
    {
        _admin.Open();
        Assert.Equal([AdminWire.Open], Assert.Single(_sent));
    }

    [Fact]
    public void Out_of_a_game_the_key_asks_nothing()
    {
        _engine.InGame = false;
        _admin.Open();
        Assert.Empty(_sent);
    }

    [Fact]
    public void A_challenge_nobody_asked_for_signs_nothing()
    {
        _keys.Generate();
        _admin.OnAdminChallenge(Nonce());
        Assert.Empty(_sent);
    }

    [Fact]
    public void A_challenge_after_the_wait_signs_nothing()
    {
        _keys.Generate();
        _admin.Open();
        _sent.Clear();
        _clock.Now += ClientAdmin.AskSeconds + 1f;
        _admin.OnAdminChallenge(Nonce());
        Assert.Empty(_sent);
    }

    [Fact]
    public void One_ask_answers_one_challenge()
    {
        _keys.Generate();
        _admin.Open();
        _sent.Clear();
        _admin.OnAdminChallenge(Nonce(1));
        _admin.OnAdminChallenge(Nonce(2));
        Assert.Single(_sent);
    }

    [Fact]
    public void The_answer_signs_the_nonce_for_the_server_this_client_joined()
    {
        _keys.Generate();
        _admin.Open();
        _sent.Clear();
        var challenge = Nonce();
        _admin.OnAdminChallenge(challenge);

        Assert.True(AdminWire.TryReadProve(Assert.Single(_sent), out var publicKey, out var signature, out string address));
        Assert.Equal(Address, address);
        Assert.Equal(_keys.PublicKey, Convert.ToBase64String(publicKey));
        Assert.True(AdminProof.Verify(publicKey, signature, challenge.Nonce, Address));
        Assert.False(AdminProof.Verify(publicKey, signature, Nonce(9).Nonce, Address));
    }

    [Fact]
    public void With_no_key_it_says_so_and_sends_nothing()
    {
        _admin.Open();
        _sent.Clear();
        _admin.OnAdminChallenge(Nonce());
        Assert.Empty(_sent);
        Assert.True(_radial.ShowingFor(_admin));
        Assert.Equal("#SS_UI_Admin_NoKey", _radial.Item(0).Label);
    }

    [Fact]
    public void A_menu_nobody_asked_for_does_not_appear()
    {
        Menu(new AdminMenuLine(0, "do:it", "Leaf"));
        Assert.False(_radial.Showing);
    }

    [Fact]
    public void A_refusal_nobody_asked_for_does_not_appear()
    {
        _admin.OnAdminRefused(new AdminRefused { Reason = (byte)AdminRefusal.NotAdmin });
        Assert.False(_radial.Showing);
    }

    [Fact]
    public void A_refusal_shows_as_a_line_that_cannot_be_chosen()
    {
        _admin.Open();
        _admin.OnAdminRefused(new AdminRefused { Reason = (byte)AdminRefusal.LockedOut });
        Assert.Equal("#SS_UI_Admin_LockedOut", _radial.Item(0).Label);
        Assert.False(_radial.Item(0).Enabled);
    }

    [Fact]
    public void Folders_open_and_a_leaf_sends_its_id()
    {
        _admin.Open();
        _sent.Clear();
        Menu(new AdminMenuLine(0, "", "#Maps"), new AdminMenuLine(1, "map:a", "a"), new AdminMenuLine(1, "map:b", "b"), new AdminMenuLine(0, "restart", "#Restart"));
        Assert.Equal(2, _radial.ItemCount);
        Assert.Equal("#Maps", _radial.Title == "" ? "" : _radial.Item(0).Label);

        _radial.Choose(0);
        Assert.Equal(2, _radial.ItemCount);
        Assert.Equal("b", _radial.Item(1).Label);
        Assert.Empty(_sent);

        _radial.Choose(1);
        Assert.False(_radial.Showing);
        Assert.Equal(AdminWire.WriteRun("map:b"), Assert.Single(_sent));
    }

    [Fact]
    public void An_empty_folder_cannot_be_opened()
    {
        _admin.Open();
        Menu(new AdminMenuLine(0, "", "#Kick"), new AdminMenuLine(0, "restart", "#Restart"));
        Assert.False(_radial.Item(0).Enabled);
    }

    [Fact]
    public void The_key_again_closes_it_and_asks_nothing()
    {
        _admin.Open();
        Menu(new AdminMenuLine(0, "restart", "#Restart"));
        _sent.Clear();
        _admin.Open();
        Assert.False(_radial.Showing);
        Assert.Empty(_sent);
    }

    [Fact]
    public void Leaving_the_game_closes_it()
    {
        _admin.Open();
        Menu(new AdminMenuLine(0, "restart", "#Restart"));
        _engine.InGame = false;
        Assert.False(_radial.Showing);
    }

    [Theory]
    [InlineData(0xCB007105u, 27015, "203.0.113.5:27015")]
    [InlineData(0x7F000001u, 27016, "127.0.0.1:27016")]
    public void The_server_s_address_reads_as_the_engine_reported_it(uint ip, int port, string address)
    {
        var server = new ConnectedServer();
        server.Joined(ip, (ushort)port);
        Assert.Equal(address, server.Address);
    }
}
