using SourceSharp.Abi;
using SourceSharp.Ai.Ui;

namespace SourceSharp.Ai.Client.Setup;

/// <summary>
/// The names a page's bots will be called (plan_hl2mp_ai §3.5.1): the game's own pools
/// (<see cref="IBotSetupGame.BotNamesPath"/>, read once through the engine's file seam) and
/// <see cref="ExamplesPerPage"/> names drawn as the server draws a preset bot's, from the
/// Medium preset spread by a seed (<see cref="SkillSpread.Draw"/>) and named by its most
/// distinctive trait (<see cref="BotNamer.Name"/>). The server's own seeds differ, so these
/// are examples of the pool, not the bots' names. No pool, no examples.
/// </summary>
[SideService(Side.Client, As = [typeof(IBotNamePoolSource)])]
public sealed class ClientBotNames(IEngineFiles files, IBotSetupGame game) : IBotNamePoolSource
{
    /// <summary>How many names the page shows.</summary>
    public const int ExamplesPerPage = 3;

    /// <summary>Mixed into an example's seeds, apart from the server's match seeds.</summary>
    public const ulong ExampleSalt = 0x4E414D45UL;

    private BotNamePools? _pools;
    private bool _read;
    private string[]? _examples;

    /// <summary>The game's pools, or null when its file is missing or wrong.</summary>
    public BotNamePools? Pools
    {
        get
        {
            if (_read)
                return _pools;
            _read = true;
            try
            {
                _pools = files.ReadGameFile(game.BotNamesPath) is { } json ? BotNamePools.Parse(json) : null;
            }
            catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
            {
                _pools = null;
            }

            return _pools;
        }
    }

    public int ExampleCount => Examples().Length;

    public string Example(int i) => Examples()[i];

    /// <summary>The examples, drawn once.</summary>
    private string[] Examples() => _examples ??= Pools is { } pools ? Draw(pools, ExamplePreset) : [];

    /// <summary>The preset the examples are drawn for: the slider's middle stop, Medium.</summary>
    public const string ExamplePreset = "normal";

    /// <summary><see cref="ExamplesPerPage"/> distinct names for bots of <paramref name="preset"/> from <paramref name="pools"/>.</summary>
    public static string[] Draw(BotNamePools pools, string preset)
    {
        ArgumentNullException.ThrowIfNull(pools);
        if (!SkillPresets.TryGet(preset, out SkillProfile? shape))
            throw new ArgumentException($"no preset '{preset}'", nameof(preset));
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new string[ExamplesPerPage];
        for (int i = 0; i < names.Length; i++)
        {
            SkillProfile skills = SkillSpread.Draw(shape!, AiRandom.SeedFor(ExampleSalt, i));
            names[i] = BotNamer.Name(pools, skills, AiRandom.SeedFor(ExampleSalt ^ 1UL, i), taken);
            taken.Add(names[i]);
        }

        return names;
    }
}
