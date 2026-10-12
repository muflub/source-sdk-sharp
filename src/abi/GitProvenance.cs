namespace SourceSharp.Abi;

/// <summary>
/// Which source a build was made from: branch, commit, and whether the tree had
/// uncommitted work in it.
///
/// <para>
/// Read from a file the build writes beside its output (see
/// <c>tools/build-stamp</c>) rather than worked out at run time, because the
/// only one of the three that matters most cannot be worked out later: whether
/// the tree was dirty is a fact about the moment of the build, and the tree has
/// almost certainly changed since. A run-time <c>git status</c> would answer a
/// question nobody asked.
/// </para>
///
/// <para>
/// A BUILD WITH UNCOMMITTED WORK MUST NOT PRESENT AS A CLEAN NAMED COMMIT. That
/// is the failure this shape is built around, so the three ways of not knowing
/// are kept apart: no file at all reads as <c>unknown</c>, a file whose dirty
/// flag is missing reads as <c>+?</c>, and only an explicit "0" reads as clean.
/// </para>
/// </summary>
public readonly record struct GitProvenance(string Branch, string Commit, bool? Dirty)
{
    /// <summary>No build stamp file was found, or it said nothing.</summary>
    public static readonly GitProvenance Unknown = new(string.Empty, string.Empty, null);

    /// <summary>Whether anything at all is known about where this came from.</summary>
    public bool IsKnown => Branch.Length > 0 || Commit.Length > 0;

    /// <summary>
    /// What a detached HEAD is called. Agents in this tree reset to a sha
    /// routinely, so this is a normal reading and not an error.
    /// </summary>
    public const string Detached = "(detached)";

    /// <summary>The branch, or the fact that there is not one.</summary>
    public string BranchName => Branch.Length > 0 ? Branch : Detached;

    /// <summary>
    /// The whole triple, e.g. <c>example-trunk-branch@45e801f2+dirty</c>.
    /// One string because the three are only useful together.
    /// </summary>
    public string Describe()
    {
        if (!IsKnown)
            return "unknown";

        string commit = Commit.Length > 0 ? $"@{Commit}" : string.Empty;

        string dirty = Dirty switch
        {
            true  => "+dirty",
            false => string.Empty,

            // Git could not be asked. Silence here would be the same rendering
            // as a clean tree, which is the one thing this must never say by
            // accident.
            null  => "+?",
        };

        return $"{BranchName}{commit}{dirty}";
    }

    public override string ToString() => Describe();

    /// <summary>
    /// Parses the <c>key=value</c> file the build writes.
    ///
    /// Deliberately not JSON: the file has four flat fields, it is written by a
    /// shell script, and a parser that cannot be confused by a quote is worth
    /// more here than a format. Unknown keys are ignored so the file can grow
    /// without an older game refusing to read it.
    /// </summary>
    public static GitProvenance Parse(string text)
    {
        string branch = string.Empty;
        string commit = string.Empty;
        bool? dirty = null;

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();

            int equals = line.IndexOf('=');
            if (equals <= 0)
                continue;

            string key = line[..equals].Trim();
            string value = line[(equals + 1)..].Trim();

            switch (key)
            {
                case "branch": branch = value; break;
                case "commit": commit = value; break;

                // Anything that is not an explicit "0" or "1" leaves this
                // unknown rather than guessing, and unknown does not read as
                // clean.
                case "dirty" when value == "1": dirty = true; break;
                case "dirty" when value == "0": dirty = false; break;
            }
        }

        return new GitProvenance(branch, commit, dirty);
    }

    /// <summary>
    /// Reads the file if it is there. <see cref="Unknown"/> rather than a throw
    /// when it is not: a game must not fail to start because a debugging aid is
    /// missing a file.
    /// </summary>
    public static GitProvenance Read(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : Unknown;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Unknown;
        }
    }

    /// <summary>The name of the file the build writes, in each output directory.</summary>
    public const string FileName = "sourcesharp.build";
}
