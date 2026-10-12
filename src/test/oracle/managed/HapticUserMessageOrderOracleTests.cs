using System.Text;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.OracleTests;

/// <summary>
/// The six haptics messages the managed list declares, against
/// <c>RegisterHapticMessages()</c> in <c>public/haptics/haptic_msgs.cpp</c>, the
/// stock file they were transcribed from: each name, under its bare stock key,
/// with stock's size.
///
/// <para>
/// <b>BY NAME, NOT BY POSITION</b> since plan_side_isolation P2b. A message's
/// engine id is its key's place in key order, carried to every client by the
/// SsUserMessages table, so the six no longer sit at ids 0-5 and there is no
/// order left to pin; what stock still decides is the names and the sizes
/// (the engine validates a fixed-size message against the size). Reads the
/// pristine upstream clone through <see cref="ValveSdk"/>.
/// </para>
/// </summary>
public class HapticUserMessageOrderOracleTests
{
    private static IReadOnlyList<UserMessageDeclaration> Declared()
    {
        var registry = new UserMessageRegistry(new RecordingLog(), receives: false);
        registry.RegisterAll(typeof(FadeMessage).Assembly);
        return registry.Messages;
    }

    [Fact]
    public void EveryMessageStocksFileRegistersIsDeclaredWithItsSize()
    {
        var native = RegistrationsIn(ValveSdk.Read("src/public/haptics/haptic_msgs.cpp"));
        var declared = Declared().ToDictionary(m => m.Name, StringComparer.Ordinal);

        Assert.True(native.Count == 6,
            $"haptic_msgs.cpp registers {native.Count} messages, not 6; the file has changed");

        foreach (var (name, size) in native)
        {
            Assert.True(declared.TryGetValue(name, out var d),
                $"haptic_msgs.cpp registers {name}, and no declaration keys a message under that bare name");
            Assert.True(d!.Size == size,
                $"{name} is {size} bytes in haptic_msgs.cpp and declared Size = {d.Size} here");
        }
    }

    // usermessages->Register( "Name", size ), in source order.
    private static readonly Regex NativeRegister =
        new(@"usermessages\s*->\s*Register\s*\(\s*""([^""]+)""\s*,\s*(-?\d+)\s*\)", RegexOptions.Compiled);

    private static List<(string Name, int Size)> RegistrationsIn(string code)
    {
        var found = new List<(string, int)>();

        foreach (Match m in NativeRegister.Matches(
                     StripComments(code)))
        {
            found.Add((m.Groups[1].Value, int.Parse(m.Groups[2].Value)));
        }

        return found;
    }
    /// <summary>
    /// Strips // and /* */ comments, so a hook someone commented out does not
    /// read as live. Preserves newlines so nothing else shifts.
    /// </summary>
    private static string StripComments(string code)
    {
        var sb = new StringBuilder(code.Length);
        bool inLine = false, inBlock = false, inString = false;

        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];
            char n = i + 1 < code.Length ? code[i + 1] : '\0';

            if (inLine)
            {
                if (c == '\n') { inLine = false; sb.Append(c); }
                continue;
            }

            if (inBlock)
            {
                if (c == '*' && n == '/') { inBlock = false; i++; }
                else if (c == '\n') sb.Append(c);
                continue;
            }

            if (inString)
            {
                if (c == '\\' && n != '\0') { sb.Append(c).Append(n); i++; continue; }
                if (c == '"') inString = false;
                sb.Append(c);
                continue;
            }

            if (c == '/' && n == '/') { inLine = true; continue; }
            if (c == '/' && n == '*') { inBlock = true; i++; continue; }
            if (c == '"') { inString = true; sb.Append(c); continue; }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
