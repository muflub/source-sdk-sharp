using System.Text.RegularExpressions;

using SourceSharp.Client.Ui;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The UI's key codes are the engine's (public/inputsystem/ButtonCode.h): the
/// menu root forwards vgui's codes untranslated (plan_client_ui U3). They were
/// 109/110 for the mouse buttons, two off MOUSE_LEFT and MOUSE_RIGHT.
/// </summary>
public class UiKeyCodeTests
{
    [Fact]
    public void Every_ui_key_is_the_engine_s_button_code()
    {
        string path = Directory.EnumerateFiles(RepoLayout.Sdk("src", "public"), "ButtonCode.h", SearchOption.AllDirectories).FirstOrDefault()
                      ?? throw new FileNotFoundException("ButtonCode.h is not in the farm; a check of nothing would pass");
        var codes = Parse(File.ReadAllText(path));
        Assert.True(codes.Count > 100, $"{codes.Count} codes parsed");

        var names = new Dictionary<UiKey, string>
        {
            [UiKey.Enter] = "KEY_ENTER", [UiKey.Space] = "KEY_SPACE", [UiKey.Backspace] = "KEY_BACKSPACE",
            [UiKey.Tab] = "KEY_TAB", [UiKey.Escape] = "KEY_ESCAPE", [UiKey.Delete] = "KEY_DELETE",
            [UiKey.Home] = "KEY_HOME", [UiKey.End] = "KEY_END", [UiKey.PageUp] = "KEY_PAGEUP",
            [UiKey.PageDown] = "KEY_PAGEDOWN", [UiKey.Up] = "KEY_UP", [UiKey.Left] = "KEY_LEFT",
            [UiKey.Down] = "KEY_DOWN", [UiKey.Right] = "KEY_RIGHT",
            [UiKey.MouseLeft] = "MOUSE_LEFT", [UiKey.MouseRight] = "MOUSE_RIGHT",
        };
        foreach (var key in Enum.GetValues<UiKey>().Where(k => k != UiKey.None))
        {
            Assert.True(names.TryGetValue(key, out var name), $"UiKey.{key} has no engine name in this fact");
            Assert.Equal((key, codes[name]), (key, (int)key));
        }
    }

    /// <summary>The ButtonCode_t enum's values: each name one more than the last, or as assigned.</summary>
    private static Dictionary<string, int> Parse(string header)
    {
        int start = header.IndexOf("enum ButtonCode_t", StringComparison.Ordinal);
        string body = header[start..header.IndexOf("};", start, StringComparison.Ordinal)];
        body = Regex.Replace(body, @"//[^\n]*|/\*.*?\*/", "", RegexOptions.Singleline);
        var values = new Dictionary<string, int>();
        int v = -1;
        foreach (string raw in body[(body.IndexOf('{') + 1)..].Split(','))
        {
            string item = raw.Trim();
            if (item.Length == 0 || item.StartsWith('#'))
                continue;
            var m = Regex.Match(item, @"^(\w+)\s*(?:=\s*(.+))?$", RegexOptions.Singleline);
            if (!m.Success)
                continue;
            if (m.Groups[2].Success)
            {
                // Past the mouse the enum names macros this does not evaluate; the UI needs none of them.
                if (!TryEval(m.Groups[2].Value.Trim(), values, out v))
                    break;
            }
            else
                v++;
            values[m.Groups[1].Value] = v;
        }
        return values;
    }

    private static bool TryEval(string expr, Dictionary<string, int> values, out int total)
    {
        total = 0;
        int sign = 1;
        foreach (Match t in Regex.Matches(expr, @"[+-]|\w+"))
        {
            if (t.Value == "+") { sign = 1; continue; }
            if (t.Value == "-") { sign = -1; continue; }
            if (int.TryParse(t.Value, out int n)) total += sign * n;
            else if (values.TryGetValue(t.Value, out int known)) total += sign * known;
            else return false;
        }
        return true;
    }
}
