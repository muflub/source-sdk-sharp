using System.Security.Cryptography;
using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>Why the server said no to an admin.</summary>
public enum AdminRefusal : byte
{
    /// <summary>The key is not in the server's cfg/admins.txt, or the proof does not verify.</summary>
    NotAdmin,

    /// <summary>Too many failures: wait.</summary>
    LockedOut,

    /// <summary>This player is no admin, and asked to do something only one may.</summary>
    NotAllowed,
}

/// <summary>What the client asks of the server's admin, under <see cref="Key"/> as a net message.</summary>
public static class AdminWire
{
    public const string Key = "SourceSharp:admin";

    public const byte Open = 1, Prove = 2, Run = 3;

    public static byte[] WriteOpen() => [Open];

    public static byte[] WriteRun(string id) => [Run, .. Encoding.UTF8.GetBytes(id)];

    /// <summary>The proof: the public key and the signature, each after a two-byte length, then the address signed for.</summary>
    public static byte[] WriteProve(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> signature, string address)
    {
        byte[] at = Encoding.UTF8.GetBytes(address);
        var w = new byte[1 + 2 + publicKey.Length + 2 + signature.Length + at.Length];
        w[0] = Prove;
        int i = 1;
        Put(w, ref i, publicKey);
        Put(w, ref i, signature);
        at.CopyTo(w, i);
        return w;
    }

    private static void Put(byte[] w, ref int i, ReadOnlySpan<byte> part)
    {
        w[i++] = (byte)(part.Length >> 8);
        w[i++] = (byte)part.Length;
        part.CopyTo(w.AsSpan(i));
        i += part.Length;
    }

    /// <summary>False for anything malformed: this is what a stranger sends.</summary>
    public static bool TryReadProve(ReadOnlySpan<byte> payload, out byte[] publicKey, out byte[] signature, out string address)
    {
        publicKey = [];
        signature = [];
        address = "";
        if (payload.Length < 1 || payload[0] != Prove)
            return false;
        var rest = payload[1..];
        if (!Take(ref rest, out publicKey) || !Take(ref rest, out signature))
            return false;
        address = Encoding.UTF8.GetString(rest);
        return true;
    }

    private static bool Take(ref ReadOnlySpan<byte> rest, out byte[] part)
    {
        part = [];
        if (rest.Length < 2)
            return false;
        int n = (rest[0] << 8) | rest[1];
        if (rest.Length < 2 + n)
            return false;
        part = rest.Slice(2, n).ToArray();
        rest = rest[(2 + n)..];
        return true;
    }
}

/// <summary>
/// The admin challenge's cryptography: ECDSA over P-256 with SHA-256. What is signed
/// names its purpose, the server's one-use nonce, and the address the client believes
/// it is talking to, so a signature is good for one answer to one server: a malicious
/// server that relays a real server's challenge gets a signature naming itself, which
/// the real one refuses.
/// </summary>
public static class AdminProof
{
    public const int NonceBytes = 32;

    private const string Purpose = "SourceSharp admin v1";

    public static byte[] Message(ReadOnlySpan<byte> nonce, string address)
        => [.. Encoding.UTF8.GetBytes(Purpose), (byte)'\n', .. nonce, (byte)'\n', .. Encoding.UTF8.GetBytes(address)];

    public static byte[] Sign(ECDsa key, ReadOnlySpan<byte> nonce, string address)
        => key.SignData(Message(nonce, address), HashAlgorithmName.SHA256);

    /// <summary>Whether the signature is the key's over this nonce and address; false for anything malformed.</summary>
    public static bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> nonce, string address)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicKey, out _);
            return key.KeySize == 256 && key.VerifyData(Message(nonce, address), signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}

/// <summary>The server's challenge: sign this, and only you can.</summary>
public partial struct AdminChallenge
{
    [UserMessageArray(MaxCount = AdminProof.NonceBytes)]
    public partial byte[] Nonce { get; set; }
}

/// <summary>A piece of the admin menu: lines of depth, action id (empty for a folder), label and whether it keeps the menu up, tab-separated, as long as it takes.</summary>
public partial struct AdminMenuPiece
{
    public partial byte Index { get; set; }

    public partial byte Count { get; set; }

    [UserMessageArray(MaxCount = AdminMenuText.PieceBytes)]
    public partial byte[] Text { get; set; }
}

public partial struct AdminRefused
{
    public partial byte Reason { get; set; }
}

/// <summary>What the admin messages tell the client.</summary>
public interface IAdminHook
{
    void OnAdminChallenge(AdminChallenge challenge);

    void OnAdminMenuPiece(AdminMenuPiece piece);

    void OnAdminRefused(AdminRefused refused);
}

[UserMessage("AdminChallenge")]
public sealed class AdminChallengeMessage : IUserMessage<AdminChallenge>
{
    private readonly IHooks<IAdminHook>? _hooks;

    public AdminChallengeMessage()
    {
    }

    [Inject]
    public AdminChallengeMessage(IHooks<IAdminHook>? hooks) => _hooks = hooks;

    public void OnReceived(AdminChallenge message) => _hooks?.Each(nameof(IAdminHook.OnAdminChallenge), h => h.OnAdminChallenge(message));
}

[UserMessage("AdminMenuPiece")]
public sealed class AdminMenuPieceMessage : IUserMessage<AdminMenuPiece>
{
    private readonly IHooks<IAdminHook>? _hooks;

    public AdminMenuPieceMessage()
    {
    }

    [Inject]
    public AdminMenuPieceMessage(IHooks<IAdminHook>? hooks) => _hooks = hooks;

    public void OnReceived(AdminMenuPiece message) => _hooks?.Each(nameof(IAdminHook.OnAdminMenuPiece), h => h.OnAdminMenuPiece(message));
}

[UserMessage("AdminRefused")]
public sealed class AdminRefusedMessage : IUserMessage<AdminRefused>
{
    private readonly IHooks<IAdminHook>? _hooks;

    public AdminRefusedMessage()
    {
    }

    [Inject]
    public AdminRefusedMessage(IHooks<IAdminHook>? hooks) => _hooks = hooks;

    public void OnReceived(AdminRefused message) => _hooks?.Each(nameof(IAdminHook.OnAdminRefused), h => h.OnAdminRefused(message));
}

/// <summary>One line of the admin menu: its nesting, what taking it runs (empty for a folder), what it says, and whether the menu stays up after it.</summary>
public readonly record struct AdminMenuLine(int Depth, string Id, string Label, bool Stay = false);

/// <summary>
/// A place in the admin menu, as the server builds it for one admin: a folder of
/// <see cref="Children"/>, or an action with an <see cref="Id"/> that <see cref="Run"/>
/// carries out. The client only ever names an id; what it does is decided here, on a
/// fresh build, so a stale or forged id finds nothing.
/// </summary>
public sealed record AdminNode(string Label, string Id = "", Action? Run = null, IReadOnlyList<AdminNode>? Children = null, bool Stay = false)
{
    public static AdminNode Folder(string label, IReadOnlyList<AdminNode> children) => new(label, Children: children);

    public static IEnumerable<AdminMenuLine> Flatten(IEnumerable<AdminNode> nodes, int depth = 0)
    {
        foreach (var node in nodes)
        {
            yield return new AdminMenuLine(depth, node.Id, node.Label, node.Stay);
            foreach (var line in Flatten(node.Children ?? [], depth + 1))
                yield return line;
        }
    }

    public static AdminNode? Find(IEnumerable<AdminNode> nodes, string id)
    {
        if (id.Length == 0)
            return null;
        foreach (var node in nodes)
        {
            if (node.Id == id && node.Run is not null)
                return node;
            if (Find(node.Children ?? [], id) is { } found)
                return found;
        }
        return null;
    }
}

/// <summary>What a mod adds to the admin menu's first page (the AI's bots).</summary>
public interface IAdminMenuProvider
{
    IEnumerable<AdminNode> AdminItems(int slot);
}

/// <summary>The admin menu as text on the wire, and its pieces.</summary>
public static class AdminMenuText
{
    /// <summary>Under a user message's 255 bytes, with the piece's own header.</summary>
    public const int PieceBytes = 200;

    public static string Write(IEnumerable<AdminMenuLine> lines)
        => string.Join('\n', lines.Select(l => $"{l.Depth}\t{Clean(l.Id)}\t{Clean(l.Label)}\t{(l.Stay ? 1 : 0)}"));

    // A name with a tab or a newline in it (a player's) must not forge a line.
    private static string Clean(string text) => text.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    public static List<AdminMenuLine> Read(string text)
    {
        var lines = new List<AdminMenuLine>();
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\t');
            if (f.Length == 4 && int.TryParse(f[0], out int depth))
                lines.Add(new AdminMenuLine(depth, f[1], f[2], f[3] == "1"));
        }
        return lines;
    }

    public static List<byte[]> Pieces(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var pieces = new List<byte[]>();
        for (int at = 0; at < bytes.Length; at += PieceBytes)
            pieces.Add(bytes[at..Math.Min(bytes.Length, at + PieceBytes)]);
        if (pieces.Count == 0)
            pieces.Add([]);
        return pieces;
    }
}
