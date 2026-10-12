namespace SourceSharp.Abi;

/// <summary>
/// One entry of the engine's own console registry. Mirrors
/// SourceSharpConsoleEntry — fixed buffers and all, because the strings are
/// copied out of a list this side does not own and must not hold pointers into.
/// </summary>
public unsafe struct ConsoleEntry
{
    public fixed byte Name[80];
    public fixed byte Value[128];
    public fixed byte Default[128];
    public fixed byte Help[400];
    public int Flags;
    public int IsCommand;

    /// <summary>The bounds it clamps to, if it has any. HasMin says whether Min was written.</summary>
    public int HasMin;
    public float Min;
    public int HasMax;
    public float Max;
}
