using System.Globalization;
using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// The game-mode catalog as text, for the wire (plan_bots_teams_tf2 D.2): a
/// line per mode (<c>M</c>) followed by a line per setting of it (<c>S</c>),
/// fields split by tabs. Ids, labels and reasons lose any tab, newline or
/// separator they carry: none is the server's to send.
/// </summary>
public static class ModeCatalogText
{
    private const char Field = '\t', Choice = '\u001f', Pair = '\u001e', List = ',';

    public static string Write(IEnumerable<CatalogMode> modes)
    {
        var text = new StringBuilder();
        foreach (var (spec, support) in modes)
        {
            text.Append('M').Append(Field).Append(Clean(spec.Id)).Append(Field).Append(Clean(spec.Title)).Append(Field)
                .Append(spec.Teamplay ? '1' : '0').Append(Field).Append(support.Supported ? '1' : '0').Append(Field)
                .Append(Clean(support.Reason)).Append(Field).Append(string.Join(List, spec.RequiresAnyOf.Select(c => Clean(c).Replace(List.ToString(), "", StringComparison.Ordinal)))).Append('\n');
            foreach (var s in spec.Settings)
            {
                text.Append('S').Append(Field).Append(Clean(s.Key)).Append(Field).Append((int)s.Kind).Append(Field)
                    .Append(Clean(s.Default)).Append(Field).Append((int)s.Applies).Append(Field).Append(s.Votable ? '1' : '0').Append(Field)
                    .Append(s.AdminOnly ? '1' : '0').Append(Field).Append(Number(s.Min)).Append(Field).Append(Number(s.Max)).Append(Field)
                    .Append(string.Join(Choice, s.Choices.Select(c => Clean(c.Value) + Pair + Clean(c.Label)))).Append('\n');
            }
        }
        return text.ToString();
    }

    /// <summary>The modes of <paramref name="text"/>; a line it cannot read is skipped, never a guess.</summary>
    public static List<CatalogMode> Read(string text)
    {
        var modes = new List<CatalogMode>();
        string[]? mode = null;
        var settings = new List<SettingSpec>();

        void Close()
        {
            if (mode is null)
                return;
            string[] requires = mode[6].Length == 0 ? [] : mode[6].Split(List);
            modes.Add(new CatalogMode(new ModeSpec(mode[1], mode[2], mode[3] == "1", [.. settings], requires),
                                      new MapSupport(mode[4] == "1", mode[5])));
            settings.Clear();
            mode = null;
        }

        foreach (string line in text.Split('\n'))
        {
            string[] f = line.Split(Field);
            if (f[0] == "M" && f.Length == 7)
            {
                Close();
                mode = f;
            }
            else if (f[0] == "S" && f.Length == 10 && mode is not null && ReadSetting(f) is { } setting)
            {
                settings.Add(setting);
            }
        }
        Close();
        return modes;
    }

    private static SettingSpec? ReadSetting(string[] f)
    {
        if (!int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int kind) || !Enum.IsDefined((SettingKind)kind)
            || !int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int applies) || !Enum.IsDefined((SettingApplies)applies)
            || !float.TryParse(f[7], NumberStyles.Float, CultureInfo.InvariantCulture, out float min)
            || !float.TryParse(f[8], NumberStyles.Float, CultureInfo.InvariantCulture, out float max))
            return null;
        var choices = f[9].Length == 0 ? [] : f[9].Split(Choice).Select(c => c.Split(Pair)).Where(p => p.Length == 2)
                                                      .Select(p => new SettingChoice(p[0], p[1])).ToList();
        return new SettingSpec(f[1], (SettingKind)kind, choices, f[3], (SettingApplies)applies, f[5] == "1", f[6] == "1", min, max);
    }

    private static string Number(float value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Clean(string text)
    {
        var clean = new StringBuilder(text.Length);
        foreach (char c in text)
            if (c is not (Field or '\n' or '\r' or Choice or Pair))
                clean.Append(c);
        return clean.ToString();
    }
}

/// <summary>A piece of the game-mode catalog (<see cref="ModeCatalogText"/>), sent as a player comes in.</summary>
public partial struct ModeCatalogPiece
{
    public partial byte Index { get; set; }

    public partial byte Count { get; set; }

    [UserMessageArray(MaxCount = AdminMenuText.PieceBytes)]
    public partial byte[] Text { get; set; }
}

/// <summary>What the catalog's arrival tells the client.</summary>
public interface IModeCatalogHook
{
    void OnModeCatalogPiece(ModeCatalogPiece piece);
}

[UserMessage("ModeCatalogPiece")]
public sealed class ModeCatalogPieceMessage : IUserMessage<ModeCatalogPiece>
{
    private readonly IHooks<IModeCatalogHook>? _hooks;

    /// <summary>The server's: a piece goes nowhere.</summary>
    public ModeCatalogPieceMessage()
    {
    }

    [Inject]
    public ModeCatalogPieceMessage(IHooks<IModeCatalogHook>? hooks) => _hooks = hooks;

    public void OnReceived(ModeCatalogPiece message)
        => _hooks?.Each(nameof(IModeCatalogHook.OnModeCatalogPiece), hook => hook.OnModeCatalogPiece(message));
}
