namespace SourceSharp.Abi;

/// <summary>
/// Ninety-one copies of one declared blob: ninety intermediate slots — one per
/// unacknowledged command — and one pristine copy of the last state that
/// arrived from the server. The managed twin of the native
/// SourceSharpPredictionRing (prediction_ring.h), carrying its two rules,
/// because their absence is a silent wrong answer rather than an error:
///
/// <para>
/// 1. A slot remembers WHICH command it holds. The ring is indexed modulo 90,
/// so command 95 lands where command 5 did — a compare against a slot holding
/// a different command's state is two unrelated states compared and reported
/// as misprediction. A slot asked for a command it does not hold answers "no
/// basis", never "different".
/// </para>
///
/// <para>
/// 2. A length that disagrees is a refusal, not a truncation. The blob's
/// length moves when a managed reload reshapes a class, and a compare at the
/// wrong length silently checks half the state. The owner re-inits when the
/// length moves, which forgets every slot.
/// </para>
/// </summary>
public sealed class PredictionRing
{
    /// <summary>Stock's MULTIPLAYER_BACKUP: commands that can be in flight.</summary>
    public const int Slots = 90;

    private byte[] _store = Array.Empty<byte>();
    private readonly int[] _commands = new int[Slots];
    private int _bytes;
    private bool _pristine;

    public int BlobBytes => _bytes;

    /// <summary>Sizes for one blob length, forgetting everything held.</summary>
    public void Init(int blobBytes)
    {
        if (blobBytes <= 0)
        {
            _store = Array.Empty<byte>();
            _bytes = 0;
        }
        else if (blobBytes != _bytes)
        {
            _store = new byte[blobBytes * (Slots + 1)];
            _bytes = blobBytes;
        }

        Reset();
    }

    /// <summary>Forgets every slot and the pristine copy, keeping the storage.</summary>
    public void Reset()
    {
        Array.Fill(_commands, -1);
        _pristine = false;
    }

    private Span<byte> SlotBytes(int slot) => _store.AsSpan(slot * _bytes, _bytes);
    private Span<byte> PristineBytes => _store.AsSpan(Slots * _bytes, _bytes);

    /// <summary>Saves one command's state. Refuses a wrong length outright.</summary>
    public bool SaveCommand(int command, ReadOnlySpan<byte> blob)
    {
        if (_bytes == 0 || blob.Length != _bytes || command < 0)
            return false;

        int slot = command % Slots;
        blob.CopyTo(SlotBytes(slot));
        _commands[slot] = command;
        return true;
    }

    /// <summary>The saved bytes for a command, or empty for "no basis".</summary>
    public ReadOnlySpan<byte> CommandBytes(int command)
    {
        if (_bytes == 0 || command < 0)
            return default;

        int slot = command % Slots;
        return _commands[slot] == command ? SlotBytes(slot) : default;
    }

    /// <summary>Damages one byte of a command's slot — the poke's mechanism.</summary>
    public bool CorruptCommand(int command, int offset)
    {
        if (_bytes == 0 || offset < 0 || offset >= _bytes || command < 0)
            return false;

        int slot = command % Slots;

        if (_commands[slot] != command)
            return false;

        SlotBytes(slot)[offset] ^= 0xFF;
        return true;
    }

    /// <summary>The restore point: the server's last word.</summary>
    public bool SavePristine(ReadOnlySpan<byte> blob)
    {
        if (_bytes == 0 || blob.Length != _bytes)
            return false;

        blob.CopyTo(PristineBytes);
        _pristine = true;
        return true;
    }

    /// <summary>The pristine copy, or empty when nothing has arrived yet.</summary>
    public ReadOnlySpan<byte> Pristine => _pristine ? PristineBytes : default;
}
