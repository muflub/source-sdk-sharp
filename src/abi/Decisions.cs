namespace SourceSharp.Abi;

// Return types for the decision callbacks.
//
// These replace bool, bool? and bare int. The problem with the old signatures
// was not that they were wrong but that they were unreadable at the call site:
// GetPlayerRelationship returned bool?, where null meant "use the stock rule"
// and true meant "teammate" (its slot, and the PlayerRelationship enum that
// replaced the bool?, left in the serialised ABI pass, 2026-10-20), and
// AllowFlashlight returned an int whose 1/0/-1 meaning lived only in a comment.
//
// The underlying values are pinned to what the ABI already sends, so native is
// unchanged — the shell still reads an int32 and still treats a negative as
// "C# declined, apply the stock rule".

/// <summary>
/// A yes/no answer that C# may decline to give.
///
/// <see cref="UseStockRule"/> is deliberately the negative value: the native
/// side tests for &lt; 0 to mean "not answered", so any future member added on
/// the declining side keeps working without touching the shell.
/// </summary>
public enum Decision
{
    /// <summary>C# has no opinion; the shell applies Source's own rule.</summary>
    UseStockRule = -1,

    No  = 0,
    Yes = 1,
}

/// <summary>
/// Whether C# consumed a client command.
///
/// Two-valued, but named rather than bool because the polarity is not obvious
/// from the call site: "true" read equally well as "the command was valid".
/// </summary>
public enum CommandResult
{
    /// <summary>Fall through to the stock handler.</summary>
    NotHandled = 0,

    /// <summary>C# handled it; the shell stops here.</summary>
    Handled = 1,
}
