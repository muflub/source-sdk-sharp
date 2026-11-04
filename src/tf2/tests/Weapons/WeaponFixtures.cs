using System.Text;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Shared;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The shipped weapon scripts (TestData/weapons/README), read as text, parsed,
/// or served sealed as the pack holds them (<c>.ctx</c>, ICE level 0 under
/// <see cref="TFEncryption.Key"/>).
/// </summary>
internal static class WeaponFixtures
{
    /// <summary>The 33 fixture names: the class scripts' weapons, the compound bow, the builder and the sapper.</summary>
    public static IEnumerable<string> Names() =>
        Directory.GetFiles(RepoLayout.Repo("src", "tf2", "tests", "TestData", "weapons"), "tf_weapon_*.txt")
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .Order(StringComparer.Ordinal);

    public static string Text(string weaponName) =>
        File.ReadAllText(RepoLayout.Repo("src", "tf2", "tests", "TestData", "weapons", weaponName + ".txt"));

    public static TFWeaponScript Parse(string weaponName)
    {
        var document = KeyValuesText.Parse(Text(weaponName));
        Assert.NotEmpty(document.Children);
        return TFWeaponScriptParser.Parse(document.Children[0], weaponName);
    }

    /// <summary>Asserts the fixture text sets <paramref name="key"/> to <paramref name="value"/> (quoted or bare).</summary>
    public static void Says(string text, string key, string value) =>
        Assert.Matches(new Regex("^\\s*\"?" + Regex.Escape(key) + "\"?\\s+\"?" + Regex.Escape(value) + "\"?\\s*$",
                                 RegexOptions.Multiline | RegexOptions.IgnoreCase), text);

    /// <summary>Asserts the fixture text does not set <paramref name="key"/> at all.</summary>
    public static void Lacks(string text, string key) =>
        Assert.DoesNotMatch(new Regex("^\\s*\"?" + Regex.Escape(key) + "\"?\\s", RegexOptions.Multiline | RegexOptions.IgnoreCase), text);

    /// <summary>The server's composed weapon rows over the shipped scripts, and the schema text when given.</summary>
    public static TFWeaponTypes Types(string? schemaText = null)
    {
        var files = ShippedWithClasses();
        if (schemaText is not null)
            files.Bytes[TF2.Shared.Econ.ItemSchema.FilePath] = Encoding.UTF8.GetBytes(schemaText);
        var log = new RecordingLog();
        return new TFWeaponTypes(new TFWeaponScripts(log, files), new TFClassTable(log, files), new TF2.Shared.Econ.ItemSchema(files, log));
    }

    public static byte[] Seal(byte[] plain)
    {
        var ice = new IceKey(0);
        ice.Set(Encoding.ASCII.GetBytes(TFEncryption.Key));
        byte[] sealedBytes = plain.ToArray();
        for (int at = 0; at + 8 <= sealedBytes.Length; at += 8)
            ice.Encrypt(plain.AsSpan(at, 8), sealedBytes.AsSpan(at, 8));
        return sealedBytes;
    }

    /// <summary>Every fixture as the shipped pack holds it: <c>scripts/&lt;name&gt;.ctx</c> only.</summary>
    public static TFClassTableTests.Files Shipped() => Shipped(new TFClassTableTests.Files());

    /// <summary>
    /// The scripts outside the milestone set (TestData/weapons-extra/README), sealed as
    /// shipped, added to <paramref name="files"/>: for the facts that name one.
    /// </summary>
    public static TFClassTableTests.Files WithExtras(TFClassTableTests.Files files)
    {
        foreach (string path in Directory.GetFiles(RepoLayout.Repo("src", "tf2", "tests", "TestData", "weapons-extra"), "tf_weapon_*.txt"))
            files.Bytes[TFWeaponScripts.ScriptName(Path.GetFileNameWithoutExtension(path)) + ".ctx"] = Seal(File.ReadAllBytes(path));
        return files;
    }

    /// <summary>The weapon scripts and the ten class scripts, sealed as shipped.</summary>
    public static TFClassTableTests.Files ShippedWithClasses() => Shipped(TFClassTableTests.Files.Shipped());

    private static TFClassTableTests.Files Shipped(TFClassTableTests.Files files)
    {
        foreach (string name in Names())
            files.Bytes[TFWeaponScripts.ScriptName(name) + ".ctx"] = Seal(Encoding.UTF8.GetBytes(Text(name)));
        return files;
    }
}
