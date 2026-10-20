using System.Text.RegularExpressions;

using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The render-target slots both sides number (C7, rendertexture.cpp's getters):
/// <see cref="RenderTargetSlot"/> against render_target_store.h's SS_RT_*,
/// name for name and value for value. A slot inserted on one side shifts every
/// slot after it and still compiles.
/// </summary>
public class RenderTargetStoreAbiTests
{
    [Fact]
    public void TheSlotsAreTheStoresSlots()
    {
        string path = RepoLayout.Repo("src", "client", "native", "render_target_store.h");
        Assert.True(File.Exists(path), "the native store's header is missing: " + path);
        var native = Regex.Matches(File.ReadAllText(path), @"\bSS_RT_(\w+)\s*=\s*(\d+)\s*,")
                          .ToDictionary(m => m.Groups[1].Value == "COUNT" ? "Count" : m.Groups[1].Value,
                                        m => int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
        var managed = Enum.GetValues<RenderTargetSlot>().ToDictionary(v => v.ToString(), v => (int)v);

        Assert.True(native.Count == 18, $"the header numbers 17 slots and the count ({native.Count} found)");
        Assert.Equal(managed.OrderBy(p => p.Key, StringComparer.Ordinal), native.OrderBy(p => p.Key, StringComparer.Ordinal));
    }
}
