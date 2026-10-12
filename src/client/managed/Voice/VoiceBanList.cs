//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Client.Voice;

/// <summary>
/// THE PLAYERS THIS CLIENT HAS MUTED (stock CVoiceBanMgr, voice_banmgr.cpp), and
/// their file voice_ban.dt, byte for byte stock's: a little-endian 1, then each
/// id's 32 bytes, bucket by bucket (the byte sum of the id, 0..255), each
/// bucket in the order its ids were added. A file of another version, or no
/// longer than its version, reads as no bans; a partial id at the end is not
/// read. Held to the oracle's golden (Fixtures/voiceban-stock.tsv).
/// </summary>
public sealed class VoiceBanList
{
    /// <summary>SIGNED_GUID_LEN: an id's bytes.</summary>
    public const int IdBytes = 32;

    /// <summary>BANMGR_FILEVERSION.</summary>
    public const int FileVersion = 1;

    public const string FileName = "voice_ban.dt";

    private readonly List<byte[]>[] _buckets = Enumerable.Range(0, 256).Select(_ => new List<byte[]>()).ToArray();

    /// <summary>An id as player_info_t's guid holds it: the text's bytes, zero padded to 32.</summary>
    public static byte[] IdOf(string guid)
    {
        var id = new byte[IdBytes];
        System.Text.Encoding.ASCII.GetBytes(guid, 0, Math.Min(guid.Length, IdBytes), id, 0);
        return id;
    }

    public bool IsBanned(ReadOnlySpan<byte> id) => Find(id) >= 0;

    public void SetBanned(ReadOnlySpan<byte> id, bool banned)
    {
        int at = Find(id);
        if (banned && at < 0)
            _buckets[Bucket(id)].Add(id[..IdBytes].ToArray());
        else if (!banned && at >= 0)
            _buckets[Bucket(id)].RemoveAt(at);
    }

    /// <summary>The list from a file's bytes (stock CVoiceBanMgr::Init), replacing what was held.</summary>
    public void Load(ReadOnlySpan<byte> file)
    {
        foreach (var b in _buckets)
            b.Clear();
        if (file.Length <= sizeof(int) || BitConverter.ToInt32(file[..sizeof(int)]) != FileVersion)
            return;
        int ids = (file.Length - sizeof(int)) / IdBytes;
        for (int i = 0; i < ids; i++)
            SetBanned(file.Slice(sizeof(int) + i * IdBytes, IdBytes), true);
    }

    /// <summary>The file's bytes (stock CVoiceBanMgr::SaveState).</summary>
    public byte[] Save()
    {
        var file = new List<byte>(sizeof(int) + IdBytes * _buckets.Sum(b => b.Count));
        file.AddRange(BitConverter.GetBytes(FileVersion));
        foreach (var bucket in _buckets)
            foreach (var id in bucket)
                file.AddRange(id);
        return file.ToArray();
    }

    private static int Bucket(ReadOnlySpan<byte> id)
    {
        int sum = 0;
        for (int i = 0; i < IdBytes; i++)
            sum += unchecked((sbyte)id[i]);
        return sum & 0xFF;
    }

    private int Find(ReadOnlySpan<byte> id)
    {
        var bucket = _buckets[Bucket(id)];
        for (int i = 0; i < bucket.Count; i++)
            if (bucket[i].AsSpan().SequenceEqual(id[..IdBytes]))
                return i;
        return -1;
    }
}
