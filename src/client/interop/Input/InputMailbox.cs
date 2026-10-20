using System.Runtime.InteropServices;
using SourceSharp.Abi;

namespace SourceSharp.Client.Input;

/// <summary>
/// What managed publishes in the mailbox's <c>nPublished</c> word
/// (shell_input.cpp's INPUT_PUBLISHED_*). Native skips a call only on a bit
/// managed set, and only once <see cref="Valid"/> is set.
/// </summary>
[Flags]
public enum InputPublished : uint
{
    None = 0,
    Valid = 1u << 0,

    /// <summary>ActivateMouse would change nothing: the mouse is active, or was never initialised.</summary>
    ActivateNoop = 1u << 1,

    /// <summary>DeactivateMouse would change nothing: the mouse is inactive, or was never initialised.</summary>
    DeactivateNoop = 1u << 2,

    /// <summary>What CAM_IsThirdPerson answers.</summary>
    ThirdPerson = 1u << 3,
}

/// <summary>When a screen shake changes nothing in the rumble mixer (shell_input.cpp's INPUT_SHAKE_SKIP_*).</summary>
public enum ShakeSkip : uint
{
    Never = 0,
    Always = 1,

    /// <summary>When the shake's bits equal the published value's.</summary>
    WhenEqual = 2,
}

/// <summary>
/// The mailbox block, field for field as shell_input.cpp's
/// SourceSharpInputMailbox: fixed-width words, no padding, 32 bytes. Both
/// sides state the offsets (COMPILE_TIME_ASSERT there,
/// InputMailboxLayoutTests here).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct InputMailboxBlock
{
    public uint StructSize;
    public uint KeyboardSampleSeq;
    public float KeyboardSampleTime;
    public uint JoystickSampleSeq;
    public float JoystickSampleTime;
    public uint Published;
    public uint ShakeSkip;
    public uint ShakeBits;
}

/// <summary>
/// The input mailbox: native's sample-time stores, read without a crossing,
/// and managed's published no-op answers, which native carries out without
/// one.
/// </summary>
public interface IInputMailbox
{
    /// <summary>
    /// The keyboard sample time native last stored, when it stored one since
    /// <paramref name="seq"/>; <paramref name="seq"/> moves up to it.
    /// </summary>
    bool TakeKeyboardSampleTime(ref uint seq, out float time);

    /// <summary>The joystick's, as <see cref="TakeKeyboardSampleTime"/>.</summary>
    bool TakeJoystickSampleTime(ref uint seq, out float time);

    void Publish(InputPublished published);

    void PublishShake(ShakeSkip skip, float value);
}

/// <summary><see cref="IInputMailbox"/> over the native block (IShimInputClient::InputMailbox).</summary>
public sealed unsafe class InputMailbox : IInputMailbox
{
    private readonly InputMailboxBlock* _block;

    private InputMailbox(InputMailboxBlock* block) => _block = block;

    /// <summary>Over the block at <paramref name="address"/>, which must say it is this layout's size.</summary>
    public static InputMailbox Over(nint address)
    {
        if (address == 0)
            throw new ArgumentException("no input mailbox", nameof(address));
        var block = (InputMailboxBlock*)address;
        if (block->StructSize != (uint)sizeof(InputMailboxBlock))
            throw new InvalidOperationException(
                $"the input mailbox says it is {block->StructSize} bytes and this build's is {sizeof(InputMailboxBlock)}");
        return new InputMailbox(block);
    }

    /// <summary>The game's block, from IShimInputClient::InputMailbox; null when native has none.</summary>
    public static InputMailbox? Open(ModuleEngine engine, out string why)
    {
        nint address = (nint)engine.Api.Areas.Require<InputClientArea>().InputMailbox();
        if (address == 0)
        {
            why = "IShimInputClient::InputMailbox answered null";
            return null;
        }
        why = "";
        return Over(address);
    }

    public bool TakeKeyboardSampleTime(ref uint seq, out float time)
        => Take(ref seq, _block->KeyboardSampleSeq, _block->KeyboardSampleTime, out time);

    public bool TakeJoystickSampleTime(ref uint seq, out float time)
        => Take(ref seq, _block->JoystickSampleSeq, _block->JoystickSampleTime, out time);

    private static bool Take(ref uint seq, uint stored, float value, out float time)
    {
        time = value;
        if (stored == seq)
            return false;
        seq = stored;
        return true;
    }

    public void Publish(InputPublished published) => _block->Published = (uint)published;

    public void PublishShake(ShakeSkip skip, float value)
    {
        _block->ShakeBits = BitConverter.SingleToUInt32Bits(value);
        _block->ShakeSkip = (uint)skip;
    }
}
