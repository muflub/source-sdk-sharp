using System.Security.Cryptography;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>The admin flow as a client drives it, through the messages a client can send: everything here is what a stranger can reach.</summary>
public class ServerAdminsTests
{
    private const string Here = "203.0.113.5:27015";
    private const int Host = 1, Player = 3;

    private sealed class Sender : IUserMessageSender
    {
        public readonly List<(string Key, byte[] Bytes, int Player)> Sent = [];

        public void Send(string key, ReadOnlySpan<byte> payload, int player = 0, bool reliable = true) => Sent.Add((key, payload.ToArray(), player));
    }

    private sealed class Menu(Action ran) : IAdminMenu
    {
        public int Builds { get; private set; }

        public IReadOnlyList<AdminNode> Build(int slot)
        {
            Builds++;
            return [AdminNode.Folder("#Folder", [new AdminNode("Leaf", "do:it", ran)])];
        }
    }

    private float _now = 100f;
    private int _ran;
    private readonly Sender _sender = new();
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly Dictionary<string, string> _list = [];
    private readonly RecordingLog _log = new();
    private readonly Menu _menu;

    public ServerAdminsTests()
    {
        _menu = new Menu(() => _ran++);
        _list[Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo())] = "alice";
    }

    private ServerAdmins Admins(bool dedicated = false)
        => new(new AdminGate(() => _now, () => _list, a => a == Here, AdminGate.SecureNonce), dedicated, _sender, _menu, _log);

    private static void From(ServerAdmins admins, int slot, byte[] payload)
        => Assert.True(admins.OnClientMessage(slot, AdminWire.Key, payload));

    private bool SentMenuTo(int slot) => _sender.Sent.Any(s => s.Player == slot && s.Key == UserMessageKey.Of(typeof(AdminMenuPieceMessage)));

    private AdminRefusal? LastRefusal(int slot)
    {
        var last = _sender.Sent.LastOrDefault(s => s.Player == slot && s.Key == UserMessageKey.Of(typeof(AdminRefusedMessage)));
        return last.Bytes is null ? null : (AdminRefusal)last.Bytes[0];
    }

    // The challenge a client gets by opening: its nonce, as the wire carries it.
    private byte[] Challenge(ServerAdmins admins, int slot)
    {
        _sender.Sent.Clear();
        From(admins, slot, AdminWire.WriteOpen());
        var (key, bytes, player) = Assert.Single(_sender.Sent);
        Assert.Equal(UserMessageKey.Of(typeof(AdminChallengeMessage)), key);
        Assert.Equal(slot, player);
        return bytes[^AdminProof.NonceBytes..];
    }

    private byte[] Proof(byte[] nonce, string address = Here)
        => AdminWire.WriteProve(_key.ExportSubjectPublicKeyInfo(), AdminProof.Sign(_key, nonce, address), address);

    [Fact]
    public void Another_key_s_message_is_left_for_its_owner()
        => Assert.False(Admins().OnClientMessage(Player, "SourceSharp:other", [AdminWire.Open]));

    [Fact]
    public void A_listen_server_s_host_is_an_admin_with_no_challenge()
    {
        var admins = Admins();
        Assert.True(admins.IsAdmin(Host));
        From(admins, Host, AdminWire.WriteOpen());
        Assert.True(SentMenuTo(Host));
    }

    [Fact]
    public void A_dedicated_server_has_no_host()
    {
        var admins = Admins(dedicated: true);
        Assert.False(admins.IsAdmin(Host));
        Challenge(admins, Host);
        Assert.False(SentMenuTo(Host));
    }

    [Fact]
    public void A_player_is_no_admin_until_they_prove_a_listed_key_and_then_gets_the_menu()
    {
        var admins = Admins();
        var nonce = Challenge(admins, Player);
        Assert.False(admins.IsAdmin(Player));
        Assert.False(SentMenuTo(Player));

        From(admins, Player, Proof(nonce));
        Assert.True(admins.IsAdmin(Player));
        Assert.True(SentMenuTo(Player));
    }

    /// <summary>The replay the user named: a client records a good proof, and sends it again once it holds no rights.</summary>
    [Fact]
    public void A_proof_replayed_after_reconnecting_gets_no_rights_and_no_menu()
    {
        var admins = Admins();
        var proof = Proof(Challenge(admins, Player));
        From(admins, Player, proof);
        Assert.True(admins.IsAdmin(Player));

        admins.OnClientDisconnected(Player);
        _sender.Sent.Clear();
        Challenge(admins, Player);
        _sender.Sent.Clear();
        From(admins, Player, proof);

        Assert.False(admins.IsAdmin(Player));
        Assert.False(SentMenuTo(Player));
        Assert.Equal(AdminRefusal.NotAdmin, LastRefusal(Player));
    }

    [Fact]
    public void A_proof_with_no_challenge_open_gets_nothing()
    {
        var admins = Admins();
        From(admins, Player, Proof(new byte[AdminProof.NonceBytes]));
        Assert.False(admins.IsAdmin(Player));
        Assert.False(SentMenuTo(Player));
    }

    [Fact]
    public void A_proof_made_for_another_server_gets_nothing()
    {
        var admins = Admins();
        From(admins, Player, Proof(Challenge(admins, Player), "198.51.100.9:27015"));
        Assert.False(admins.IsAdmin(Player));
    }

    [Theory]
    [InlineData(new byte[] { AdminWire.Prove })]
    [InlineData(new byte[] { AdminWire.Prove, 255, 255, 1 })]
    [InlineData(new byte[] { 200, 1, 2, 3 })]
    [InlineData(new byte[0])]
    public void Junk_is_claimed_and_grants_nothing(byte[] payload)
    {
        var admins = Admins();
        Challenge(admins, Player);
        From(admins, Player, payload);
        Assert.False(admins.IsAdmin(Player));
    }

    [Fact]
    public void Failing_repeatedly_is_refused_as_locked_out_even_after_reconnecting()
    {
        var admins = Admins();
        for (int i = 0; i < AdminGate.MaxFailures; i++)
        {
            Challenge(admins, Player);
            From(admins, Player, AdminWire.WriteProve([1], [2], Here));
        }
        admins.OnClientDisconnected(Player);
        _sender.Sent.Clear();
        From(admins, Player, AdminWire.WriteOpen());
        Assert.Equal(AdminRefusal.LockedOut, LastRefusal(Player));
    }

    [Fact]
    public void Someone_who_is_no_admin_cannot_run_an_item_even_knowing_its_id()
    {
        var admins = Admins();
        From(admins, Player, AdminWire.WriteRun("do:it"));
        Assert.Equal(0, _ran);
        Assert.Equal(0, _menu.Builds);
        Assert.Equal(AdminRefusal.NotAllowed, LastRefusal(Player));
    }

    [Fact]
    public void An_admin_runs_an_item_by_its_id_on_a_fresh_menu()
    {
        var admins = Admins();
        From(admins, Player, Proof(Challenge(admins, Player)));
        int builds = _menu.Builds;
        From(admins, Player, AdminWire.WriteRun("do:it"));
        Assert.Equal(1, _ran);
        Assert.Equal(builds + 1, _menu.Builds);
    }

    [Theory]
    [InlineData("nothing:here")]
    [InlineData("changelevel evil")]
    [InlineData("")]
    public void An_id_not_on_the_menu_runs_nothing(string id)
    {
        var admins = Admins();
        From(admins, Host, AdminWire.WriteRun(id));
        Assert.Equal(0, _ran);
    }

    [Fact]
    public void Leaving_ends_a_player_s_rights_so_a_run_afterwards_does_nothing()
    {
        var admins = Admins();
        From(admins, Player, Proof(Challenge(admins, Player)));
        admins.OnClientDisconnected(Player);
        From(admins, Player, AdminWire.WriteRun("do:it"));
        Assert.Equal(0, _ran);
    }

    [Fact]
    public void A_key_taken_off_the_list_cannot_run_anything_more()
    {
        var admins = Admins();
        From(admins, Player, Proof(Challenge(admins, Player)));
        _list.Clear();
        From(admins, Player, AdminWire.WriteRun("do:it"));
        Assert.Equal(0, _ran);
    }

    [Fact]
    public void No_secret_reaches_the_log()
    {
        var admins = Admins();
        var nonce = Challenge(admins, Player);
        From(admins, Player, Proof(nonce));
        string log = string.Join('\n', _log.Lines.Select(l => l.Text));
        Assert.Contains("alice", log, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo()), log, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(nonce), log, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("10.0.0.2:27015", 27015, "", true)]
    [InlineData("10.0.0.2:27016", 27015, "", false)]
    [InlineData("203.0.113.5:27015", 27015, "", false)]
    [InlineData("203.0.113.5:27015", 27015, "203.0.113.5", true)]
    [InlineData("203.0.113.5:27016", 27015, "203.0.113.5", false)]
    [InlineData("203.0.113.5:40000", 27015, "203.0.113.5:40000", true)]
    [InlineData("10.0.0.2:27015", 27015, "203.0.113.5", false)]
    [InlineData("nonsense", 27015, "", false)]
    public void The_address_signed_for_must_be_this_server(string claimed, int port, string configured, bool ours)
        => Assert.Equal(ours, AdminAddress.IsOurs(claimed, port, ["10.0.0.2", "127.0.0.1"], configured));

    [Fact]
    public void With_no_known_port_no_address_is_ours()
        => Assert.False(AdminAddress.IsOurs("10.0.0.2:27015", null, ["10.0.0.2"], ""));
}
