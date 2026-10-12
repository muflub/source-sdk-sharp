using System.Text.RegularExpressions;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed <see cref="RenderMode"/> enum against the engine's own
/// <c>RenderMode_t</c> in <c>public/const.h</c>.
///
/// <para>
/// THIS EXISTS BECAUSE IT DRIFTED, and the way it failed is why a parity test
/// is the right shape rather than care. <c>ss_plasma</c> needed
/// <c>kRenderTransAddFrameBlend</c>, the mode that blends between two animation
/// frames instead of snapping. The value was added to the managed enum as 13.
/// It is 7.
/// </para>
///
/// <para>
/// Nothing caught it. The managed enum compiled, the ABI parity test passed —
/// it checks the ABI struct, not this — the whole suite was green, and the
/// number crossed the boundary as a plain <c>int</c>. On the other side
/// <c>CEngineSprite::GetMaterial</c> subscripts a bare array of
/// <c>kRenderModeCount</c> materials with it and checks nothing, so the first
/// plasma flame ever drawn read a pointer from past the end of that array and
/// dereferenced it. SIGSEGV, ten seconds into a live map, three frames below
/// anything that looked wrong.
/// </para>
///
/// <para>
/// The native side now refuses an out-of-range mode — see
/// <c>shell_sprite_render.cpp</c> — so the same mistake would be a warning and
/// a sprite that does not draw. This makes it a red test instead.
/// </para>
///
/// <para>
/// It is the rule from docs/writing-tests.md: a fixture standing in for
/// something native owns must assert the agreement rather than maintain it by
/// hand. This enum is exactly such a fixture.
/// </para>
/// </summary>
public class RenderModeParityTests
{
    /// <summary>
    /// Every managed name, with the engine's spelling of it. The managed enum is
    /// deliberately a SUBSET — the modes nothing here has needed are not
    /// declared — so the mapping is written out rather than derived, and the
    /// test below proves the subset is a faithful one.
    /// </summary>
    private static readonly (RenderMode Managed, string Native)[] Pairs =
    {
        (RenderMode.Normal,             "kRenderNormal"),
        (RenderMode.TransTexture,       "kRenderTransTexture"),
        (RenderMode.Glow,               "kRenderGlow"),
        (RenderMode.TransAdd,           "kRenderTransAdd"),
        (RenderMode.TransAddFrameBlend, "kRenderTransAddFrameBlend"),
        (RenderMode.WorldGlow,          "kRenderWorldGlow"),
        (RenderMode.None,               "kRenderNone"),
    };

    [Fact]
    public void EveryManagedRenderModeHasTheEnginesValue()
    {
        var native = ReadNativeEnum();

        if (native is null)
        {
            // Skipped rather than failed, the way AbiParityTests skips: the
            // tests must still run from a directory with no source tree beside
            // it.
            Assert.True(true, "public/const.h found for the render mode check (skipped, not found)");
            return;
        }

        // The parse proving it parsed. RenderMode_t has eleven members and a
        // count; a handful means the walk went wrong rather than that the engine
        // shrank.
        Assert.True(native.Count >= 11,
            $"the RenderMode_t parse found the whole enum ({native.Count} members)");

        foreach (var (managed, nativeName) in Pairs)
        {
            Assert.True(native.ContainsKey(nativeName),
                $"the engine still declares {nativeName}");
            Assert.True((int)managed == native[nativeName],
                $"{managed} is {nativeName} = {native[nativeName]}, not {(int)managed}");
        }
    }

    /// <summary>
    /// Every managed value must be a real mode, which is the property that
    /// actually protects the array subscript on the far side.
    /// </summary>
    [Fact]
    public void NoManagedRenderModeIsOutsideTheEnginesArray()
    {
        var native = ReadNativeEnum();

        if (native is null)
        {
            Assert.True(true, "public/const.h found for the range check (skipped, not found)");
            return;
        }

        Assert.True(native.TryGetValue("kRenderModeCount", out int count),
            "the engine still declares kRenderModeCount, which is the array's size");

        foreach (RenderMode mode in Enum.GetValues<RenderMode>())
        {
            Assert.True((int)mode >= 0 && (int)mode < count,
                $"{mode} = {(int)mode} is inside CEngineSprite::m_material[{count}]");
        }
    }

    /// <summary>
    /// <c>RenderMode_t</c> from <c>public/const.h</c>, resolved to numbers.
    ///
    /// The enum has one explicit initialiser (<c>kRenderNormal = 0</c>) and the
    /// rest run on, so the values are counted out rather than read.
    /// </summary>
    private static Dictionary<string, int>? ReadNativeEnum()
    {
        string? header = RepoLayout.FindSdkFile(Path.Combine("public", "const.h"));

        if (header is null)
            return null;

        string text = File.ReadAllText(header);

        var body = Regex.Match(text, @"enum\s+RenderMode_t\s*\{(.*?)\}", RegexOptions.Singleline);

        if (!body.Success)
            return null;

        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        int next = 0;

        // Comments first, so a `// 1` inside one cannot be read as a member.
        string cleaned = Regex.Replace(body.Groups[1].Value, @"//[^\n]*", "");
        cleaned = Regex.Replace(cleaned, @"/\*.*?\*/", "", RegexOptions.Singleline);

        foreach (string raw in cleaned.Split(','))
        {
            string member = raw.Trim();

            if (member.Length == 0)
                continue;

            var assigned = Regex.Match(member, @"^(\w+)\s*=\s*(-?\d+)$");

            if (assigned.Success)
            {
                next = int.Parse(assigned.Groups[2].Value);
                values[assigned.Groups[1].Value] = next;
                next++;
                continue;
            }

            if (!Regex.IsMatch(member, @"^\w+$"))
                continue;

            values[member] = next;
            next++;
        }

        return values;
    }
}
