using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// One titles.txt entry. Mirrors SourceSharpTextMessage — fixed buffers,
/// because the engine's own copy is freed on a level change.
/// </summary>
public unsafe struct TextMessage
{
    public int Effect;
    public byte R1, G1, B1, A1;
    public byte R2, G2, B2, A2;
    public float X, Y;
    public float FadeIn, FadeOut, HoldTime, FxTime;
    public fixed byte Message[512];

    /// <summary>The message this one takes down when it arrives. Usually empty.</summary>
    public fixed byte ClearMessage[64];

    /// <summary>
    /// The words, as a string.
    ///
    /// Here rather than at the call site because the client assembly is not
    /// compiled unsafe, and reading a fixed buffer is the one thing that needs
    /// to be. The pointer never leaves this method.
    /// </summary>
    public string GetMessage()
    {
        fixed (byte* p = Message)
            return Marshal.PtrToStringUTF8((nint)p) ?? string.Empty;
    }

    /// <summary>The name of the message this one takes down, or empty.</summary>
    public string GetClearMessage()
    {
        fixed (byte* p = ClearMessage)
            return Marshal.PtrToStringUTF8((nint)p) ?? string.Empty;
    }
}
