using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SourceSharp.Mods;

/// <summary>
/// The server's cfg/admins.yaml:
/// <code>
/// admins:
///   - name: alice
///     key: MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE...
/// </code>
/// </summary>
[YamlSerializable]
public sealed class AdminsFile
{
    public List<AdminEntry>? Admins { get; set; }
}

[YamlSerializable]
public sealed class AdminEntry
{
    public string? Name { get; set; }

    /// <summary>The public key, base64, as the client's <c>ss_admin_pubkey</c> prints it.</summary>
    public string? Key { get; set; }
}

/// <summary>What cfg/admins.yaml grants: public keys in their canonical base64, each with its admin's name.</summary>
public sealed record AdminsConfig(IReadOnlyDictionary<string, string> Keys, IReadOnlyList<string> Problems)
{
    public static readonly AdminsConfig None = new(new Dictionary<string, string>(), []);

    /// <summary>
    /// Fails closed: a file that does not read grants nobody, and an entry that does not
    /// read grants nothing, each with a line saying why. A stranger never writes this
    /// file, but its owner mistypes it, and that must not leave them guessing.
    /// </summary>
    public static AdminsConfig Parse(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return None;

        AdminsFile file;
        try
        {
            IDeserializer d = new StaticDeserializerBuilder(new ModsYamlContext())
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();
            file = d.Deserialize<AdminsFile>(yaml) ?? new AdminsFile();
        }
        catch (YamlException e)
        {
            return new AdminsConfig(new Dictionary<string, string>(), [$"line {e.Start.Line}: {e.InnerException?.Message ?? e.Message}"]);
        }

        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var (entry, i) in (file.Admins ?? []).Select((e, i) => (e, i)))
        {
            if (Canonical(entry.Key) is { } key)
                keys[key] = string.IsNullOrWhiteSpace(entry.Name) ? "(unnamed)" : entry.Name;
            else
                problems.Add($"admin {i + 1} ({entry.Name ?? "unnamed"}): the key is not base64");
        }
        return new AdminsConfig(keys, problems);
    }

    private static string? Canonical(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;
        try
        {
            return Convert.ToBase64String(Convert.FromBase64String(key.Trim()));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
