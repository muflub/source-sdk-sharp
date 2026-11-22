using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Store;

/// <summary>
/// Opens experience stores for the server's binding (plan_bots_teams_tf2 Part
/// G, G.3): the <see cref="IExperienceStores"/> of a mod that lists this
/// assembly. A mod without it has no database, and its binding keeps no file.
/// A file another process holds locked as the game opens it is retried in the
/// background, never waited on (<see cref="SqliteExperienceStore.OpenRetrying"/>).
/// </summary>
[SideService(Side.Server, As = [typeof(IExperienceStores)])]
public sealed class SqliteExperienceStores(IModInfo mod) : IExperienceStores
{
    public IExperienceStore Open(string path)
    {
        SqliteNative.ResolveFrom(mod.ManagedDir);
        return SqliteExperienceStore.OpenRetrying(path);
    }
}

/// <summary>
/// Where SQLite's native library is (G.10's risk). The game loads a mod's
/// assemblies from memory into its own load context, which knows no directory
/// to probe, so the provider's import of <c>e_sqlite3</c> is answered here
/// from the managed directory's <c>runtimes/&lt;rid&gt;/native</c>, where the
/// build copies it. A host that finds the library itself (a test host, by its
/// deps file) is left to: a missing file answers nothing.
/// </summary>
internal static class SqliteNative
{
    /// <summary>The library as SQLitePCLRaw's provider imports it.</summary>
    public const string Library = "e_sqlite3";

    /// <summary>The library's file under <paramref name="managedDir"/> for this process's platform.</summary>
    public static string PathIn(string managedDir)
    {
        string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        string file = OperatingSystem.IsWindows() ? Library + ".dll" : OperatingSystem.IsMacOS() ? "lib" + Library + ".dylib" : "lib" + Library + ".so";
        return Path.Combine(managedDir, "runtimes", $"{os}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}", "native", file);
    }

    /// <summary>Answers the provider's import from <paramref name="managedDir"/>; once for each load of the provider's assembly, the first directory given.</summary>
    public static void ResolveFrom(string managedDir)
    {
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(SQLitePCL.SQLite3Provider_e_sqlite3).Assembly, (name, _, _) => Load(name, managedDir));
        }
        catch (InvalidOperationException)
        {
            // An earlier store of this process set it for this load of the provider.
        }
    }

    /// <summary>The handle of <paramref name="name"/> when it is SQLite's and its file is under <paramref name="managedDir"/>; else 0, and the runtime looks where it always does.</summary>
    public static nint Load(string name, string managedDir)
        => name == Library && File.Exists(PathIn(managedDir)) && NativeLibrary.TryLoad(PathIn(managedDir), out nint handle) ? handle : 0;
}
