extern alias client;

using System.Linq;
using CAbi = client::SourceSharp.Abi;

/// <summary><see cref="DeclaredConVars"/> for the client's registry, in a project that holds both sides.</summary>
internal static class DeclaredClientConVars
{
    /// <summary>The instance <paramref name="convars"/> declared; a missing declaration fails the fact.</summary>
    public static T Declared<T>(this CAbi.ConVarRegistry convars) where T : CAbi.ConVarBase
        => convars.All.OfType<T>().First();
}
