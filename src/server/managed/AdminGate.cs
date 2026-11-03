using System.Security.Cryptography;

using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Who has proved an admin key, and how. Nothing here touches the engine, so every path a
/// stranger can reach is a fact.
/// <para>
/// No answer can be used twice. A nonce is the server's own randomness, belongs to one
/// slot and answers once, right or wrong; asking again replaces it, and it expires. So a
/// recorded proof, replayed on this connection, a later one, another slot or after a
/// restart, names a nonce the server no longer holds. What is signed also names the
/// address, so a proof made for another server is no good here (<see cref="AdminProof"/>).
/// </para>
/// <para>
/// Rights last for the connection and only while the key stays on the list. Failures
/// outlive the connection, so reconnecting does not clear a lockout. Game thread only.
/// </para>
/// </summary>
public sealed class AdminGate(Func<float> now, Func<IReadOnlyDictionary<string, string>> keys, Func<string, bool> isOurAddress, Func<byte[]> randomNonce)
{
    public const float NonceSeconds = 30f, LockoutSeconds = 60f;

    public const int MaxFailures = 3;

    private readonly Dictionary<int, (byte[] Nonce, float Expires)> _nonces = new();
    private readonly Dictionary<int, (int Count, float Last)> _failures = new();
    private readonly Dictionary<int, string> _granted = new();

    /// <summary>The nonce for a player to sign; null while they are locked out.</summary>
    public byte[]? Challenge(int slot)
    {
        if (LockedOut(slot))
            return null;
        var nonce = randomNonce();
        _nonces[slot] = (nonce, now() + NonceSeconds);
        return nonce;
    }

    public bool LockedOut(int slot)
        => _failures.TryGetValue(slot, out var f) && f.Count >= MaxFailures && now() < f.Last + LockoutSeconds;

    /// <summary>The slot proved a key this connection, and the list still holds it: taking a key off ends its rights at once.</summary>
    public bool IsGranted(int slot) => _granted.TryGetValue(slot, out string? key) && keys().ContainsKey(key);

    /// <summary>Takes a player's answer. The nonce is spent whatever the answer, so none can be tried twice.</summary>
    public bool Prove(int slot, byte[] publicKey, byte[] signature, string address) => Prove(slot, publicKey, signature, address, out _);

    /// <param name="why">Why it failed, for the server's log: an operator needs to tell a key missing from the list from an address that is not this server's.</param>
    public bool Prove(int slot, byte[] publicKey, byte[] signature, string address, out string why)
    {
        why = "";
        if (LockedOut(slot))
        {
            why = "locked out";
            return false;
        }
        bool had = _nonces.Remove(slot, out var issued);
        string key = Convert.ToBase64String(publicKey);
        if (!had)
            why = "no challenge open";
        else if (now() >= issued.Expires)
            why = "the challenge expired";
        else if (!keys().ContainsKey(key))
            why = "the key is not on the list";
        else if (!isOurAddress(address))
            why = $"it was signed for {address}, which is not this server (sv_admin_address names the address players use)";
        else if (!AdminProof.Verify(publicKey, signature, issued.Nonce, address))
            why = "the signature does not verify";
        else
        {
            _granted[slot] = key;
            _failures.Remove(slot);
            return true;
        }
        var last = _failures.GetValueOrDefault(slot);
        // Long enough ago, it starts a new count rather than adding to an old one.
        int count = now() >= last.Last + LockoutSeconds ? 1 : last.Count + 1;
        _failures[slot] = (count, now());
        return false;
    }

    /// <summary>The name the list gives the slot's key; null when it has not proved one.</summary>
    public string? NameOf(int slot)
        => _granted.TryGetValue(slot, out string? key) && keys().TryGetValue(key, out string? name) ? name : null;

    /// <summary>The slot's player left: whoever gets it next has proved nothing. Failures stay until they age out.</summary>
    public void Forget(int slot)
    {
        _granted.Remove(slot);
        _nonces.Remove(slot);
    }

    public static byte[] SecureNonce() => RandomNumberGenerator.GetBytes(AdminProof.NonceBytes);
}
