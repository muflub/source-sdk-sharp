namespace SourceSharp.Abi;

/// <summary>One shared library, as the kernel says this process has it mapped.</summary>
/// <param name="Path">The file it was mapped from.</param>
/// <param name="Replaced">
/// The kernel marked the mapping "(deleted)": the file at that path is not the
/// one running any more. A build replaces a .so by renaming a new file over it
/// — deliberately, so a running game keeps its old inode rather than having its
/// executable pages rewritten underneath it — and this flag is what that looks
/// like from inside the process. It means "you rebuilt native and did not
/// restart", which is exactly the question a build stamp exists to answer.
/// </param>
public readonly record struct MappedModule(string Path, bool Replaced);

/// <summary>
/// Which file the native half of this process is actually running from.
///
/// <para>
/// Asked of <c>/proc/self/maps</c> rather than assumed from a directory layout,
/// because the point of a build stamp is to be right about what is loaded. A
/// path worked out from a convention would be right on the machine it was
/// written for and would quietly stamp the wrong file anywhere else — and it
/// could not see the case above, where the file at the expected path has already
/// been replaced.
/// </para>
///
/// <para>
/// Costs no C++ at all, which is the point: the process's own memory map is
/// readable from C#, so nothing has to be added to the ABI to answer this.
/// </para>
/// </summary>
public static class ProcMaps
{
    /// <summary>Where Linux publishes this process's mappings.</summary>
    public const string SelfMaps = "/proc/self/maps";

    /// <summary>
    /// The first mapping whose file has this name.
    ///
    /// <para>
    /// A line is <c>address perms offset dev inode path</c>, and the path is
    /// optional — anonymous mappings have five fields and are skipped.
    /// </para>
    ///
    /// <para>
    /// <b>THE PATH IS THE WHOLE REMAINDER OF THE LINE, SPACES INCLUDED.</b>
    /// This used to split on every space and take the sixth field, with a
    /// comment saying no library in this tree has a space in its path. That was
    /// true and it was the wrong set: every ENGINE module is under
    /// <c>steamapps/common/Source SDK Base 2013 Multiplayer/</c>, so
    /// <c>engine.so</c> and <c>libvstdlib.so</c> came back as
    /// <c>…/steamapps/common/Source</c> and nothing ever matched. The
    /// symptom was a flat "engine.so is not mapped" from the module-opening
    /// layer, in a process that had it mapped -- the failure the direct-vtable
    /// binding hit first. That layer is gone (plan_native_api.md Phase 1: an
    /// interface is acquired through the engine's own factories, switched on in
    /// native, and nothing opens a module), so the cref that used to be here
    /// named a type this assembly no longer has. THIS FILE SURVIVES IT because
    /// its remaining reader is BUILD PROVENANCE -- each side's own entry asks
    /// where an assembly was mapped from -- which reads already-mapped memory
    /// and acquires nothing.
    /// </para>
    ///
    /// <para>
    /// So the five fixed fields are split off and everything after them is the
    /// path. The <c>(deleted)</c> marker is still separable from a filename
    /// that happens to end in those characters, because the kernel writes it
    /// after a space and this only strips it as a suffix.
    /// </para>
    /// </summary>
    public static MappedModule? Locate(IEnumerable<string> lines, string fileName)
    {
        foreach (string line in lines)
        {
            string[] fields = line.Split(
                (char[]?)null, 6, StringSplitOptions.RemoveEmptyEntries);

            if (fields.Length < 6)
                continue;

            string path = fields[5].Trim();

            const string deletedMarker = " (deleted)";

            bool wasDeleted = path.EndsWith(deletedMarker, StringComparison.Ordinal);

            if (wasDeleted)
                path = path[..^deletedMarker.Length];

            // Only absolute paths are files. "[heap]", "[stack]" and the vdso
            // occupy the same column.
            if (path.Length == 0 || path[0] != '/')
                continue;

            if (!string.Equals(System.IO.Path.GetFileName(path), fileName, StringComparison.Ordinal))
                continue;

            return new MappedModule(path, wasDeleted);
        }

        return null;
    }

    /// <summary>
    /// THE SAME, FOR A LIBRARY NAMED BY SONAME RATHER THAN BY FILE NAME.
    ///
    /// <para>
    /// <c>libstdc++.so.6</c> is a soname. The file the kernel reports is
    /// <c>libstdc++.so.6.0.35</c>, and <see cref="Locate(IEnumerable{string},
    /// string)"/> compares file names — so the exact match finds nothing in a
    /// process that has it mapped. That is not hypothetical: it is how the RTTI
    /// reader first came back with every typeinfo record classified Unknown, in
    /// a game where <c>libstdc++</c> is a DT_NEEDED of both <c>engine.so</c> and
    /// <c>filesystem_stdio.so</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Exact first, always.</b> A prefix that also matched a bare name would
    /// let <c>engine.so</c> find <c>engine.so.bak</c>, so the versioned form is
    /// only considered when there is no exact match, and only when what follows
    /// the soname is a <c>.</c> and then digits and dots — which is what a
    /// version is and what a backup or a variant is not.
    /// </para>
    /// </summary>
    public static MappedModule? LocateSoname(IEnumerable<string> lines, string soname)
    {
        MappedModule? found = null;

        foreach (string line in lines)
        {
            string[] fields = line.Split(
                (char[]?)null, 6, StringSplitOptions.RemoveEmptyEntries);

            if (fields.Length < 6)
                continue;

            string path = fields[5].Trim();

            const string deletedMarker = " (deleted)";

            bool wasDeleted = path.EndsWith(deletedMarker, StringComparison.Ordinal);

            if (wasDeleted)
                path = path[..^deletedMarker.Length];

            if (path.Length == 0 || path[0] != '/')
                continue;

            string name = System.IO.Path.GetFileName(path);

            if (string.Equals(name, soname, StringComparison.Ordinal))
                return new MappedModule(path, wasDeleted);

            if (found is null && IsVersionOf(name, soname))
                found = new MappedModule(path, wasDeleted);
        }

        return found;
    }

    /// <summary>Whether <paramref name="name"/> is <paramref name="soname"/> plus a version suffix.</summary>
    public static bool IsVersionOf(string name, string soname)
    {
        if (name.Length <= soname.Length + 1
            || !name.StartsWith(soname, StringComparison.Ordinal)
            || name[soname.Length] != '.')
            return false;

        for (int i = soname.Length + 1; i < name.Length; i++)
        {
            if (name[i] != '.' && !char.IsAsciiDigit(name[i]))
                return false;
        }

        return true;
    }

    /// <summary>The same, of this process.</summary>
    public static MappedModule? LocateSoname(string soname)
    {
        try
        {
            return File.Exists(SelfMaps) ? LocateSoname(File.ReadLines(SelfMaps), soname) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The same, of this process. Null rather than a throw anywhere this is not
    /// Linux or the file cannot be read — a missing stamp is survivable and a
    /// game that would not start without one is not.
    /// </summary>
    public static MappedModule? Locate(string fileName)
    {
        try
        {
            return File.Exists(SelfMaps) ? Locate(File.ReadLines(SelfMaps), fileName) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
