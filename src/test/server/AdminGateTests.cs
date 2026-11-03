using System.Security.Cryptography;

using SourceSharp.Mods;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>The admin challenge: every way a stranger can reach it.</summary>
public class AdminGateTests
{
    private const string Here = "203.0.113.5:27015", Elsewhere = "198.51.100.9:27015";
    private const int Slot = 3;

    private float _now = 100f;
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly Dictionary<string, string> _keys = [];
    private readonly AdminGate _gate;

    public AdminGateTests()
    {
        _keys[Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo())] = "alice";
        _gate = new AdminGate(() => _now, () => _keys, a => a == Here, AdminGate.SecureNonce);
    }

    private byte[] Public => _key.ExportSubjectPublicKeyInfo();

    private bool Answer(byte[] nonce, string signedFor = Here, string sentFor = Here)
        => _gate.Prove(Slot, Public, AdminProof.Sign(_key, nonce, signedFor), sentFor);

    [Fact]
    public void A_listed_key_answering_its_challenge_is_granted()
    {
        Assert.False(_gate.IsGranted(Slot));
        Assert.True(Answer(_gate.Challenge(Slot)!));
        Assert.True(_gate.IsGranted(Slot));
    }

    [Fact]
    public void A_refusal_says_which_check_failed()
    {
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var nonce = _gate.Challenge(Slot)!;
        _gate.Prove(Slot, stranger.ExportSubjectPublicKeyInfo(), AdminProof.Sign(stranger, nonce, Here), Here, out string notListed);
        Assert.Contains("not on the list", notListed, StringComparison.Ordinal);

        nonce = _gate.Challenge(Slot)!;
        _gate.Prove(Slot, Public, AdminProof.Sign(_key, nonce, Elsewhere), Elsewhere, out string elsewhere);
        Assert.Contains(Elsewhere, elsewhere, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_not_on_the_list_is_refused()
    {
        var nonce = _gate.Challenge(Slot)!;
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.False(_gate.Prove(Slot, stranger.ExportSubjectPublicKeyInfo(), AdminProof.Sign(stranger, nonce, Here), Here));
        Assert.False(_gate.IsGranted(Slot));
    }

    [Fact]
    public void A_listed_public_key_with_a_stranger_s_signature_is_refused()
    {
        var nonce = _gate.Challenge(Slot)!;
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.False(_gate.Prove(Slot, Public, AdminProof.Sign(stranger, nonce, Here), Here));
    }

    [Fact]
    public void A_recorded_answer_replayed_on_the_same_connection_is_refused()
    {
        var nonce = _gate.Challenge(Slot)!;
        var signature = AdminProof.Sign(_key, nonce, Here);
        Assert.True(_gate.Prove(Slot, Public, signature, Here));
        Assert.False(_gate.Prove(Slot, Public, signature, Here));
    }

    /// <summary>The attack as it would be run: record a real admin's proof, reconnect without their rights, send it again.</summary>
    [Fact]
    public void A_recorded_answer_replayed_after_reconnecting_grants_nothing()
    {
        var nonce = _gate.Challenge(Slot)!;
        var signature = AdminProof.Sign(_key, nonce, Here);
        Assert.True(_gate.Prove(Slot, Public, signature, Here));
        _gate.Forget(Slot);
        Assert.False(_gate.IsGranted(Slot));

        Assert.False(_gate.Prove(Slot, Public, signature, Here));
        Assert.False(_gate.IsGranted(Slot));
    }

    [Fact]
    public void A_recorded_answer_replayed_against_a_new_challenge_grants_nothing()
    {
        var signature = AdminProof.Sign(_key, _gate.Challenge(Slot)!, Here);
        Assert.True(_gate.Prove(Slot, Public, signature, Here));
        _gate.Forget(Slot);

        _gate.Challenge(Slot);
        Assert.False(_gate.Prove(Slot, Public, signature, Here));
        Assert.False(_gate.IsGranted(Slot));
    }

    [Fact]
    public void A_recorded_answer_replayed_from_another_slot_grants_nothing()
    {
        var signature = AdminProof.Sign(_key, _gate.Challenge(Slot)!, Here);
        Assert.True(_gate.Prove(Slot, Public, signature, Here));

        _gate.Challenge(Slot + 1);
        Assert.False(_gate.Prove(Slot + 1, Public, signature, Here));
        Assert.False(_gate.IsGranted(Slot + 1));
    }

    [Fact]
    public void A_recorded_answer_replayed_after_a_server_restart_grants_nothing()
    {
        var signature = AdminProof.Sign(_key, _gate.Challenge(Slot)!, Here);
        var restarted = new AdminGate(() => _now, () => _keys, a => a == Here, AdminGate.SecureNonce);

        restarted.Challenge(Slot);
        Assert.False(restarted.Prove(Slot, Public, signature, Here));
    }

    /// <summary>The check above must be the nonce: the same proof over the nonce the server holds is what passes.</summary>
    [Fact]
    public void Only_the_nonce_separates_a_replay_from_a_good_answer()
    {
        byte[] fixedNonce = new byte[AdminProof.NonceBytes];
        var gate = new AdminGate(() => _now, () => _keys, a => a == Here, () => fixedNonce);
        var signature = AdminProof.Sign(_key, fixedNonce, Here);

        gate.Challenge(Slot);
        Assert.True(gate.Prove(Slot, Public, signature, Here));
        gate.Forget(Slot);

        Assert.False(gate.Prove(Slot, Public, signature, Here));

        gate.Challenge(Slot);
        Assert.True(gate.Prove(Slot, Public, signature, Here));
    }

    [Fact]
    public void A_leaver_s_open_challenge_does_not_pass_to_whoever_takes_the_slot()
    {
        var nonce = _gate.Challenge(Slot)!;
        _gate.Forget(Slot);
        Assert.False(Answer(nonce));
    }

    [Fact]
    public void A_nonce_is_the_full_width_of_real_randomness()
    {
        var nonce = _gate.Challenge(Slot)!;
        Assert.Equal(AdminProof.NonceBytes, nonce.Length);
        Assert.Contains(nonce, b => b != 0);
    }

    [Fact]
    public void Taking_a_key_off_the_list_ends_its_rights()
    {
        Assert.True(Answer(_gate.Challenge(Slot)!));
        _keys.Clear();
        Assert.False(_gate.IsGranted(Slot));
    }

    [Fact]
    public void Reconnecting_does_not_clear_a_lockout()
    {
        for (int i = 0; i < AdminGate.MaxFailures; i++)
        {
            _gate.Challenge(Slot);
            Assert.False(_gate.Prove(Slot, Public, [0], Here));
        }
        _gate.Forget(Slot);
        Assert.True(_gate.LockedOut(Slot));
        Assert.Null(_gate.Challenge(Slot));
    }

    [Fact]
    public void An_old_failure_does_not_count_towards_a_new_lockout()
    {
        _gate.Challenge(Slot);
        Assert.False(_gate.Prove(Slot, Public, [0], Here));
        _now += AdminGate.LockoutSeconds + 1f;
        for (int i = 0; i < AdminGate.MaxFailures - 1; i++)
        {
            _gate.Challenge(Slot);
            Assert.False(_gate.Prove(Slot, Public, [0], Here));
        }
        Assert.False(_gate.LockedOut(Slot));
    }

    [Fact]
    public void A_signature_over_an_older_nonce_does_not_answer_a_new_one()
    {
        var old = _gate.Challenge(Slot)!;
        var signature = AdminProof.Sign(_key, old, Here);
        _gate.Challenge(Slot);
        Assert.False(_gate.Prove(Slot, Public, signature, Here));
    }

    [Fact]
    public void A_wrong_answer_spends_the_nonce()
    {
        var nonce = _gate.Challenge(Slot)!;
        Assert.False(_gate.Prove(Slot, Public, [1, 2, 3], Here));
        Assert.False(_gate.Prove(Slot, Public, AdminProof.Sign(_key, nonce, Here), Here));
    }

    [Fact]
    public void A_nonce_expires()
    {
        var nonce = _gate.Challenge(Slot)!;
        _now += AdminGate.NonceSeconds + 1f;
        Assert.False(Answer(nonce));
    }

    [Fact]
    public void An_answer_with_no_challenge_is_refused()
        => Assert.False(_gate.Prove(Slot, Public, AdminProof.Sign(_key, new byte[32], Here), Here));

    /// <summary>The relay: a malicious server hands its victim our nonce, and what comes back names the malicious server.</summary>
    [Fact]
    public void A_signature_made_for_another_server_is_refused()
    {
        var nonce = _gate.Challenge(Slot)!;
        Assert.False(Answer(nonce, signedFor: Elsewhere, sentFor: Elsewhere));
    }

    [Fact]
    public void A_signature_for_another_server_claimed_as_ours_is_refused()
    {
        var nonce = _gate.Challenge(Slot)!;
        Assert.False(Answer(nonce, signedFor: Elsewhere, sentFor: Here));
    }

    [Fact]
    public void Another_slot_cannot_answer_a_slot_s_challenge()
    {
        var nonce = _gate.Challenge(Slot)!;
        Assert.False(_gate.Prove(Slot + 1, Public, AdminProof.Sign(_key, nonce, Here), Here));
        Assert.False(_gate.IsGranted(Slot + 1));
    }

    [Fact]
    public void Repeated_failures_lock_the_slot_out_until_the_wait_is_over()
    {
        for (int i = 0; i < AdminGate.MaxFailures; i++)
        {
            _gate.Challenge(Slot);
            Assert.False(_gate.Prove(Slot, Public, [0], Here));
        }
        Assert.True(_gate.LockedOut(Slot));
        Assert.Null(_gate.Challenge(Slot));

        _now += AdminGate.LockoutSeconds + 1f;
        Assert.False(_gate.LockedOut(Slot));
        Assert.True(Answer(_gate.Challenge(Slot)!));
    }

    [Fact]
    public void Leaving_ends_the_rights_for_whoever_gets_the_slot_next()
    {
        Assert.True(Answer(_gate.Challenge(Slot)!));
        _gate.Forget(Slot);
        Assert.False(_gate.IsGranted(Slot));
    }

    [Fact]
    public void Each_challenge_is_new()
        => Assert.False(_gate.Challenge(Slot)!.SequenceEqual(_gate.Challenge(Slot)!));

    [Theory]
    [InlineData(new byte[] { 2 })]
    [InlineData(new byte[] { 2, 0, 200, 1 })]
    [InlineData(new byte[] { 1, 0, 0, 0, 0 })]
    [InlineData(new byte[0])]
    public void A_malformed_proof_reads_as_none(byte[] payload)
        => Assert.False(AdminWire.TryReadProve(payload, out _, out _, out _));

    [Fact]
    public void A_proof_reads_back_as_written()
    {
        Assert.True(AdminWire.TryReadProve(AdminWire.WriteProve([1, 2], [3], "a:1"), out var pub, out var sig, out string address));
        Assert.Equal(new byte[] { 1, 2 }, pub);
        Assert.Equal(new byte[] { 3 }, sig);
        Assert.Equal("a:1", address);
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3 })]
    [InlineData(new byte[0])]
    public void A_public_key_that_is_no_key_verifies_nothing(byte[] junk)
        => Assert.False(AdminProof.Verify(junk, [1], new byte[32], Here));

    [Fact]
    public void The_yaml_list_gives_each_key_its_name()
    {
        var config = AdminsConfig.Parse("# who may\nadmins:\n  - name: alice\n    key: AAAA\n  - key: BBBB\n");
        Assert.Empty(config.Problems);
        Assert.Equal("alice", config.Keys["AAAA"]);
        Assert.Equal("(unnamed)", config.Keys["BBBB"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("admins:\n")]
    public void No_list_grants_nobody(string? yaml)
    {
        Assert.Empty(AdminsConfig.Parse(yaml).Keys);
        Assert.Empty(AdminsConfig.Parse(yaml).Problems);
    }

    [Theory]
    [InlineData("admins: [unclosed")]
    [InlineData("admins:\n  - nmae: alice\n    key: AAAA\n")]
    public void A_file_that_does_not_read_grants_nobody_and_says_why(string yaml)
    {
        var config = AdminsConfig.Parse(yaml);
        Assert.Empty(config.Keys);
        Assert.NotEmpty(config.Problems);
    }

    [Fact]
    public void A_bad_key_grants_nothing_and_leaves_the_rest()
    {
        var config = AdminsConfig.Parse("admins:\n  - name: typo\n    key: not base64!\n  - name: bob\n    key: AAAA\n");
        Assert.Equal("AAAA", Assert.Single(config.Keys.Keys));
        Assert.Contains("typo", Assert.Single(config.Problems));
    }

    [Fact]
    public void A_name_cannot_forge_a_menu_line()
    {
        var text = AdminMenuText.Write([new AdminMenuLine(0, "kick:2", "Bob\n0\tmap:evil\tEvil")]);
        var read = AdminMenuText.Read(text);
        var line = Assert.Single(read);
        Assert.Equal("kick:2", line.Id);
    }

    [Fact]
    public void A_long_menu_comes_back_whole_from_its_pieces()
    {
        var lines = Enumerable.Range(0, 200).Select(i => new AdminMenuLine(1, $"map:m{i}", $"Map number {i}")).ToList();
        string text = AdminMenuText.Write(lines);
        var pieces = AdminMenuText.Pieces(text);
        Assert.True(pieces.Count > 1);
        Assert.All(pieces, p => Assert.True(p.Length <= AdminMenuText.PieceBytes));
        Assert.Equal(lines, AdminMenuText.Read(System.Text.Encoding.UTF8.GetString(pieces.SelectMany(p => p).ToArray())));
    }
}
