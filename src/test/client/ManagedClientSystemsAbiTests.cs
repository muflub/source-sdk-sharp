using System.Text.RegularExpressions;

using SourceSharp.Client.ClientSystems;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client-system stages both sides number (C7): <see cref="ClientSystemStage"/>
/// against shell_managed_clientsystems_client.cpp's SS_CLIENT_SYSTEM_STAGE_*,
/// name for name and value for value, and every managed row the native file
/// adapts is a system managed registers.
/// </summary>
public class ManagedClientSystemsAbiTests
{
    private static string Native()
    {
        string path = RepoLayout.Repo("src", "client", "native", "shell_managed_clientsystems_client.cpp");
        Assert.True(File.Exists(path), "the native adapter is missing: " + path);
        return File.ReadAllText(path);
    }

    [Fact]
    public void TheStagesAreTheAdaptersStages()
    {
        var native = Regex.Matches(Native(), @"\bSS_CLIENT_SYSTEM_STAGE_(\w+)\s*=\s*(\d+)\s*,")
                          .ToDictionary(m => m.Groups[1].Value,
                                        m => int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
        var managed = Enum.GetValues<ClientSystemStage>().ToDictionary(v => v.ToString(), v => (int)v);
        Assert.True(native.Count == 7, $"the adapter numbers seven stages ({native.Count} found)");
        Assert.Equal(managed.OrderBy(p => p.Key, StringComparer.Ordinal), native.OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryAdaptedRowIsAManagedSystem()
    {
        var rows = Regex.Matches(Native(), @"static CManagedClientSystemRow s_Row\(""(\w+)""\);")
                        .Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(rows);
        var names = new[] { new ViewportClientSystem(new NullLayout()).Name,
                            new SourceSharp.Client.Render.PixelVisibility.PixelVisibilityClientSystem(null!).Name,
                            new SourceSharp.Client.Render.GlowOverlays.GlowOverlayClientSystem(null!).Name };
        foreach (string row in rows)
            Assert.Contains(row, names);
    }

    private sealed class NullLayout : SourceSharp.Client.Interop.IClientModeLayout
    {
        public void Layout() { }
    }
}
