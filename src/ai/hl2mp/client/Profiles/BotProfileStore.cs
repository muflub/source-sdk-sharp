using System.Collections.Immutable;
using SourceSharp.Abi;

namespace SourceSharp.Ai.Hl2mp.Client.Profiles;

/// <summary>Where the profiles live: their file names and their bytes.</summary>
public interface IProfileDisk
{
    /// <summary>The names of the <c>.json</c> files in <see cref="BotProfileStore.Directory"/>, without the extension.</summary>
    IReadOnlyList<string> Names();

    /// <summary>The bytes of the file at <paramref name="path"/> (relative to the mod's game dir), or null when there is none.</summary>
    byte[]? Read(string path);

    /// <summary>Writes the file at <paramref name="path"/>, making its directory; null, or why it could not.</summary>
    string? Write(string path, byte[] bytes);
}

/// <summary>
/// The player's custom bot profiles (plan_hl2mp_ai §3.5): one
/// <see cref="ProfileFile"/> per profile at <c>cfg/ai/bots/&lt;name&gt;.json</c>
/// in the mod's game dir, the name refused unless it is a profile's name
/// (never path-joined otherwise), and the built-in presets answered as
/// read-only profiles without a file. Every load and every save is judged
/// by the pool; the server judges again whatever this did.
/// </summary>
[SideService(Side.Client)]
public sealed class BotProfileStore(IProfileDisk disk)
{
    public const string Directory = "cfg/ai/bots";

    /// <summary>The pool every profile is judged by.</summary>
    public SkillPool Pool { get; } = StandardSkills.StandardPool();

    /// <summary>The file a profile called <paramref name="name"/> lives in; the name must already be valid.</summary>
    public static string PathOf(string name)
        => ProfileFile.NameRefusal(name) is { } why ? throw new ArgumentException(why, nameof(name)) : $"{Directory}/{name}.json";

    /// <summary>Whether <paramref name="name"/> is a built-in preset's (case ignored: a file may not shadow one).</summary>
    public static bool IsBuiltIn(string name)
    {
        foreach (string preset in SkillPresets.Names)
        {
            if (string.Equals(preset, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>The saved profiles' names, in order, leaving out any file whose name is not a profile's or is a preset's.</summary>
    public IReadOnlyList<string> SavedNames()
        => [.. disk.Names().Where(n => ProfileFile.NameRefusal(n) is null && !IsBuiltIn(n)).Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The profile called <paramref name="name"/>: a preset (read-only, no revisions) or the saved file, judged again.</summary>
    public SkillVerdict Load(string name, out BotProfileDocument? document)
    {
        document = null;
        if (ProfileFile.NameRefusal(name) is { } why)
            return SkillVerdict.Refuse(why);
        if (IsBuiltIn(name))
        {
            SkillPresets.TryGet(name.ToLowerInvariant(), out SkillProfile? preset);
            document = new BotProfileDocument(preset!, Pool.Points, []);
            return SkillVerdict.Ok;
        }

        string path = PathOf(name);
        if (disk.Read(path) is not { } bytes)
            return SkillVerdict.Refuse($"no profile '{name}': expected {path}");
        return ProfileFile.Read(bytes, path, name, Pool, out document);
    }

    /// <summary>Writes <paramref name="profile"/> and its revisions under its own name; refused when the name or the pool says no.</summary>
    public SkillVerdict Save(SkillProfile profile, IEnumerable<ProfileRevision> revisions)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(revisions);
        if (ProfileFile.NameRefusal(profile.Name) is { } why)
            return SkillVerdict.Refuse(why);
        if (IsBuiltIn(profile.Name))
            return SkillVerdict.Refuse($"'{profile.Name}' is a built-in profile; save it under another name");
        SkillVerdict verdict = Pool.Judge(profile);
        if (!verdict.Accepted)
            return verdict;
        foreach (ProfileRevision r in revisions)
        {
            verdict = Pool.Judge(new SkillProfile(profile.Name, profile.Skills, r.Levels));
            if (!verdict.Accepted)
                return SkillVerdict.Refuse($"revision #{r.Number}: {verdict.Refusal}");
        }

        byte[] bytes = ProfileFile.Write(new BotProfileDocument(profile, Pool.Points, [.. revisions]));
        return disk.Write(PathOf(profile.Name), bytes) is { } failed ? SkillVerdict.Refuse(failed) : SkillVerdict.Ok;
    }
}
