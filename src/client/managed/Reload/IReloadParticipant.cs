using System.Buffers.Binary;
using System.Text;

namespace SourceSharp.Client;

/// <summary>
/// A client service that holds per-level or per-frame state across a client
/// hot reload (plan_client_novalve §3.8): the service tier beside the entity
/// tiers. Registered through DI like any service; the client module walks
/// every one at its reload save and restore, and carries each as bytes in a
/// section named by the contract it was registered under, so a renamed
/// service is a carried section nobody restores, which is reported, rather
/// than a restore that silently does nothing.
/// </summary>
public interface IReloadParticipant
{
    /// <summary>Writes what the reloaded service needs. Called on the old build, at the frame boundary.</summary>
    void Save(ReloadWriter writer);

    /// <summary>Reads it back into the new build's freshly constructed service.</summary>
    void Restore(ReloadReader reader);
}

/// <summary>Bytes for one participant's section: little-endian primitives and length-prefixed UTF-8.</summary>
public sealed class ReloadWriter
{
    private readonly List<byte> _bytes = [];

    public void Write(int value)
    {
        Span<byte> four = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(four, value);
        _bytes.AddRange(four);
    }

    public void Write(float value) => Write(BitConverter.SingleToInt32Bits(value));

    public void Write(bool value) => _bytes.Add(value ? (byte)1 : (byte)0);

    /// <summary>A length-prefixed run of bytes (a carried blob another assembly formats, such as the UI's windows).</summary>
    public void Write(ReadOnlySpan<byte> value)
    {
        Write(value.Length);
        _bytes.AddRange(value);
    }

    public void Write(string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        Write(utf8.Length);
        _bytes.AddRange(utf8);
    }

    public byte[] ToArray() => _bytes.ToArray();
}

/// <summary>
/// Reads what a <see cref="ReloadWriter"/> wrote. A read past the end, or a
/// length that does not fit, throws <see cref="ReloadFormatException"/>: the
/// walk reports the section as failed rather than restoring half of it.
/// </summary>
public sealed class ReloadReader(byte[] bytes)
{
    private int _at;

    /// <summary>Whether every byte was read: a section with bytes left over was written by another shape.</summary>
    public bool AtEnd => _at == bytes.Length;

    public int ReadInt()
    {
        Need(4);
        int value = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(_at, 4));
        _at += 4;
        return value;
    }

    public float ReadFloat() => BitConverter.Int32BitsToSingle(ReadInt());

    public bool ReadBool()
    {
        Need(1);
        return bytes[_at++] != 0;
    }

    public byte[] ReadBytes()
    {
        int length = ReadInt();
        if (length < 0)
            throw new ReloadFormatException($"a byte run of length {length}");
        Need(length);
        byte[] value = bytes.AsSpan(_at, length).ToArray();
        _at += length;
        return value;
    }

    public string ReadString()
    {
        int length = ReadInt();
        if (length < 0)
            throw new ReloadFormatException($"a string of length {length}");
        Need(length);
        string value = Encoding.UTF8.GetString(bytes, _at, length);
        _at += length;
        return value;
    }

    private void Need(int count)
    {
        if (count > bytes.Length - _at)
            throw new ReloadFormatException($"{count} byte(s) wanted at {_at} of {bytes.Length}");
    }
}

/// <summary>A participant's section did not read back as it was written.</summary>
public sealed class ReloadFormatException(string message) : Exception(message);
