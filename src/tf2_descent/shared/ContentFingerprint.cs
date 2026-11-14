// Compiled into both TF2.Descent.Server and TF2.Descent.Client, as Messages.cs.

namespace TF2.Descent.Shared;

/// <summary>
/// The fingerprint of the content rows the owner's messages name by index
/// (plan_descent_logic UI3): FNV-1a 32 over each table's ids in table order,
/// a table ending with a 0 byte. The server computes it from the rules'
/// tables, the client from the bytes it embeds; a client whose tables differ
/// from the server's would name every item and skill wrongly, so it shows
/// nothing it cannot name rightly and says so once.
/// </summary>
public static class ContentFingerprint
{
    private const uint Offset = 2166136261u;
    private const uint Prime = 16777619u;

    /// <summary>The fingerprint of <paramref name="tables"/>, each a list of row ids in order.</summary>
    public static uint Of(params IEnumerable<string>[] tables)
    {
        uint hash = Offset;
        foreach (var table in tables)
        {
            foreach (string id in table)
            {
                foreach (char c in id)
                {
                    hash = (hash ^ (byte)c) * Prime;
                    hash = (hash ^ (byte)(c >> 8)) * Prime;
                }
                hash = (hash ^ (byte)'\n') * Prime;
            }
            hash = (hash ^ 0) * Prime;
        }
        return hash;
    }
}
