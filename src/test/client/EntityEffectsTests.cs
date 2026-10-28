using System.Text.RegularExpressions;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="EntityEffect"/> against the header it mirrors.
///
/// <para>
/// The values are the ENGINE's, and they travel: the server writes them into
/// <see cref="EngineBlockHead.Effects"/>, the <c>m_fEffects</c> prop puts them
/// on the wire, and native client code reads the same word through
/// <c>ViewSubject::EffectActive</c> with engine-side <c>EF_*</c> constants. A
/// number invented on the managed side would be a silent disagreement about
/// what a hidden entity is, so each one is read out of <c>src/public/const.h</c>
/// rather than restated here.
/// </para>
/// </summary>
public class EntityEffectsTests
{
    private static readonly Dictionary<string, int> Header = ParseHeader();

    private static Dictionary<string, int> ParseHeader()
    {
        string path = Path.Combine(RepoLayout.SdkTree, "src", "public", "const.h");
        string text = File.ReadAllText(path);

        // The `// entity effects` block and nothing else in the file: EF_ names
        // appear in comments elsewhere, and a whole-file scan would pick up
        // whatever a comment happened to spell.
        int at = text.IndexOf("// entity effects", StringComparison.Ordinal);
        Assert.True(at >= 0, "const.h still has the `// entity effects` enum");

        int end = text.IndexOf("};", at, StringComparison.Ordinal);
        Assert.True(end > at, "and that enum is closed");

        var found = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (Match m in Regex.Matches(text[at..end], @"(EF_[A-Z_]+)\s*=\s*(0x[0-9a-fA-F]+)"))
            found[m.Groups[1].Value] = Convert.ToInt32(m.Groups[2].Value, 16);

        return found;
    }

    [Fact]
    public void EveryHeaderBitHasAManagedName()
    {
        // The half that catches an ADDITION rather than a drift. Ten facts each
        // naming one bit would go on passing after the engine grew an eleventh,
        // and the managed enum would then be a partial vocabulary that looks
        // complete.
        Assert.Equal(10, Header.Count);

        foreach (var (name, bit) in Header)
            Assert.True(Enum.IsDefined(typeof(EntityEffect), bit),
                        $"{name} = 0x{bit:x} has no EntityEffect member");
    }

    [Fact]
    public void TheWireCarriesEveryBitTheEnumDeclares()
    {
        // m_fEffects is sent EF_MAX_BITS wide and unsigned. A bit above that
        // width would be set on the server, dropped by the encoder, and read as
        // clear on the client — which for EF_NODRAW is a brush that is hidden on
        // one side of the wire and drawn on the other.
        string text = File.ReadAllText(
            Path.Combine(RepoLayout.SdkTree, "src", "public", "const.h"));

        var m = Regex.Match(text, @"EF_MAX_BITS\s*=\s*(\d+)");
        Assert.True(m.Success, "const.h declares EF_MAX_BITS");

        int bits = int.Parse(m.Groups[1].Value);
        int widest = Header.Values.Max();

        Assert.True(widest < (1 << bits),
                    $"the widest EF_ bit is 0x{widest:x} and the wire carries {bits} bits");
    }
}
