namespace SourceSharp.Abi;

/// <summary>
/// What <c>ClientColorCorrection</c> is being asked to do.
/// Mirrors SourceSharpColorCorrectionOp — the three together are one lookup's
/// whole life.
/// </summary>
public enum ColorCorrectionOp
{
    /// <summary>Load the file and answer a handle, or 0 if it would not load.</summary>
    Add = 0,

    /// <summary>How much of it to apply, 0..1.</summary>
    SetWeight = 1,

    /// <summary>Release it.</summary>
    Remove = 2,
}
