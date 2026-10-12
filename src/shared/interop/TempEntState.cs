namespace SourceSharp.Abi;

/// <summary>
/// The base of every temp entity declaration — what a caller fills in and hands
/// over.
///
/// <para>
/// A declaration is now a CLASS rather than a struct so that a derived table's
/// declaration can inherit its base table's fields, and a caller can write the
/// whole effect in one object initialiser:
/// </para>
/// <code>
/// var state = new TeSparks
/// {
///     Origin      = position,     // DT_TEParticleSystem's
///     Magnitude   = magnitude,    // DT_TESparks' own
///     TrailLength = trailLength,
///     Direction   = direction ?? Vector.Zero,
/// };
/// </code>
/// <para>
/// which is the shape the wire has anyway: the base nests at datatable offset
/// zero and its props read at their own offsets out of the same object.
/// </para>
///
/// <para>
/// THE INHERITANCE IS THE LAYOUT, WHICH IS THE WIRE, so
/// <c>EntityLayout.Build</c> walks the chain ROOT FIRST and sorts each level
/// only against itself. A metadata token is declaration order within one type
/// and nothing more; across types it is order in the compiled image, so sorting
/// an inherited set by token can put a derived field before a base one. See the
/// comment there — it is the one thing about this design that must not be
/// simplified.
/// </para>
/// </summary>
public abstract class TempEntState
{
    /// <summary>
    /// This declaration's bytes, laid out as native expects them: the base's
    /// fields first, then this class's own.
    ///
    /// <para>
    /// Every field is written from its own <c>[TeProp]</c>, which carries the
    /// wire name — so a caller never spells <c>m_vecPos</c> and a rename of the
    /// C# member cannot silently stop writing a field.
    /// </para>
    /// </summary>
    public byte[] ToBlob(TempEntType type)
    {
        var blob = new byte[type.BlobBytes];
        var writer = new TempEntWriter(type, blob);

        int written = TempEntDeclarationWriter.WriteChain(ref writer, this);

        if (written == 0)
        {
            type.Log.Warning(
                $"[SourceSharp] temp entity '{type.Class.ClassName}' wrote NO fields from its " +
                $"declaration {GetType().Name}. Every field would go out at zero -- an effect " +
                "at the world origin with a model index of nothing -- so this is refused " +
                "loudly rather than sent.\n");
        }

        return blob;
    }
}
