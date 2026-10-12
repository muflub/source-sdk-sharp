using System.Text.RegularExpressions;
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Tests;

/// <summary>
/// Which temp entity classes a <c>shell_te_forwards*.cpp</c> file forwards, read off
/// disk — in EITHER of the two forms a forward can be in while they are being
/// emptied into C#.
///
/// <para>
/// A forward used to name its class in the file:
/// </para>
/// <code>
/// if ( !SourceSharpTe_Begin( TeHandle( s_hClass, "CTESparks" ) ) ) return;
/// SourceSharpTe_SetInt( "Magnitude", magnitude );
/// </code>
/// <para>
/// A converted one names an id instead, and the class it means is the
/// dispatcher's business:
/// </para>
/// <code>
/// SourceSharpTe_Forward( SOURCESHARP_TE_FWD_SPARKS, filter, delay, f, 6, i, 2 );
/// </code>
///
/// <para>
/// SEVEN TESTS LOOKED FOR THE FIRST FORM ONLY, and went red the moment the sweep
/// converted the files they watch — not because anything broke, but because they
/// could no longer see what they were watching. That is a bad failure: it says
/// "this forward is missing" about a forward that is right there. So the
/// recognition lives here, in one place, and answers the same set either way.
/// </para>
///
/// <para>
/// THE ID IS RESOLVED BY ASKING THE DISPATCHER, not by a table written here. A
/// second mapping from id to class is a second thing to drift; <c>Build</c> is
/// the one that actually runs, so what it answers is by definition what the
/// forward does.
/// </para>
/// </summary>
public static class NativeForwards
{
    private static readonly Regex Handle =
        new(@"TeHandle\(\s*\w+\s*,\s*""(\w+)""\s*\)", RegexOptions.Compiled);

    private static readonly Regex Forward =
        new(@"SourceSharpTe_Forward\(\s*(SOURCESHARP_TE_FWD_\w+)", RegexOptions.Compiled);

    /// <summary>Every class the given source forwards, in either form.</summary>
    public static IReadOnlyCollection<string> ClassesIn(string source)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in Handle.Matches(source))
            found.Add(m.Groups[1].Value);

        foreach (Match m in Forward.Matches(source))
        {
            if (ClassOfId(m.Groups[1].Value) is { } cls)
                found.Add(cls);
        }

        return found;
    }

    /// <summary>
    /// The wire class a <c>SOURCESHARP_TE_FWD_*</c> id forwards, or null if the
    /// dispatcher has no arm for it yet.
    /// </summary>
    public static string? ClassOfId(string nativeId)
    {
        // SOURCESHARP_TE_FWD_ARMOR_RICOCHET -> ArmorRicochet
        string managed = string.Concat(nativeId["SOURCESHARP_TE_FWD_".Length..]
                                       .Split('_')
                                       .Select(w => w.Length == 0
                                                   ? w
                                                   : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));

        foreach (TeForward id in Enum.GetValues<TeForward>())
        {
            if (!string.Equals(id.ToString(), managed, StringComparison.OrdinalIgnoreCase))
                continue;

            // GENEROUS SPANS, because an arm refuses a short argument list --
            // this is asking WHICH CLASS, not exercising the unpack.
            TempEntState? state = TeForwardDispatch.Build(id, new float[64], new int[64]);

            return state is null ? null : TempEntityAttribute.ClassNameOf(state.GetType());
        }

        return null;
    }
}
