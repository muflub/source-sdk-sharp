using System.Globalization;
using System.Text;

using SourceSharp.Client;
using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed screen-space effect manager (<see cref="ScreenSpaceEffectManager"/>)
/// against stock's CScreenSpaceEffectManager, run offline from the pristine
/// ScreenSpaceEffects.cpp by src/test/oracle/native/screenspaceeffects_oracle_test.cpp
/// (<c>Fixtures/screenspaceeffects-stock.tsv</c>, README beside it).
///
/// <para>
/// The golden carries its stimulus: each <c>S</c> row is a step, driven here on
/// the managed manager with the oracle's registrations made in the oracle's
/// order (alpha, beta enabled, the stock example, filmgrain, one with no
/// effect), and every row stock wrote after it must be the row the managed
/// manager's recording effects write.
/// </para>
/// </summary>
public class ScreenSpaceEffectManagerOracleTests
{
    private sealed class Recording(string name, bool enabled, List<string> log) : IScreenSpaceEffect
    {
        private bool _enabled = enabled;

        public void Init() => log.Add($"E\t{name}\tInit");
        public void Shutdown() => log.Add($"E\t{name}\tShutdown");

        public void SetParameters(KeyValuesNode? parameters)
        {
            string value = parameters is null ? "(null)"
                : parameters.Children.FirstOrDefault(c => c.NameText == "value") is { } v ? Encoding.Latin1.GetString(v.StringValue) : "(none)";
            log.Add($"E\t{name}\tSetParameters\t{(parameters is null ? "(null)" : parameters.NameText)}\t{value}");
        }

        public void Render(int x, int y, int w, int h, RenderPlanBuilder plan)
            => log.Add(string.Create(CultureInfo.InvariantCulture, $"E\t{name}\tRender\t{x}\t{y}\t{w}\t{h}"));

        public void Enable(bool enable)
        {
            log.Add($"E\t{name}\tEnable\t{(enable ? 1 : 0)}");
            _enabled = enable;
        }

        public bool IsEnabled()
        {
            log.Add($"E\t{name}\tIsEnabled\t{(_enabled ? 1 : 0)}");
            return _enabled;
        }
    }

    private sealed class Rig
    {
        public readonly List<string> Log = [];
        public string CommandLine = "game";
        public readonly ScreenSpaceEffectManager Manager;
        public readonly Dictionary<string, IScreenSpaceEffect> Effects = new(StringComparer.Ordinal);

        public Rig()
        {
            Manager = new ScreenSpaceEffectManager(w => Log.Add("W\t" + w.TrimEnd('\n')),
                                                   p => CommandLine.Split(' ').Contains(p, StringComparer.OrdinalIgnoreCase));
            Effects["alpha"] = new Recording("alpha", false, Log);
            Effects["beta"] = new Recording("beta", true, Log);
            Effects["exampleeffect"] = new ExampleScreenSpaceEffect();
            Effects["filmgrain"] = new Recording("filmgrain", false, Log);
            // The oracle's construction order: two before the stock file, its example, two after.
            Manager.Register("alpha", Effects["alpha"]);
            Manager.Register("beta", Effects["beta"]);
            Manager.Register("exampleeffect", Effects["exampleeffect"]);
            Manager.Register("filmgrain", Effects["filmgrain"]);
            Manager.Register("hollow", null);
        }

        private IScreenSpaceEffect? Named(string n) => Effects.TryGetValue(n, out IScreenSpaceEffect? e) ? e : null;

        private string NameOf(IScreenSpaceEffect? e) => e is null ? "(null)" : Effects.First(p => ReferenceEquals(p.Value, e)).Key;

        private static KeyValuesNode? Params(string kv, string value, bool exampleParam)
        {
            if (kv == "-")
                return null;
            var root = new KeyValuesNode(Encoding.Latin1.GetBytes(kv));
            var v = new KeyValuesNode(Encoding.Latin1.GetBytes("value")) { Type = KeyValuesType.String, StringValue = Encoding.Latin1.GetBytes(value) };
            root.FirstChild = v;
            if (exampleParam)
                v.Next = new KeyValuesNode(Encoding.Latin1.GetBytes("example_param")) { Type = KeyValuesType.String, StringValue = Encoding.Latin1.GetBytes("x") };
            return root;
        }

        private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

        public void Step(string[] f)
        {
            switch (f[0])
            {
                case "init":
                    CommandLine = f.Length > 1 ? f[1] : "game";
                    Manager.Init();
                    break;
                case "shutdown": Manager.Shutdown(); break;
                case "get": Log.Add("G\t" + NameOf(Manager.Get(f[1]))); break;
                case "setparams-name": Manager.SetParameters(f[1], Params(f[2], f[3], f.Length > 4 && f[4] == "1")); break;
                case "setparams-effect": ScreenSpaceEffectManager.SetParameters(Named(f[1]), Params(f[2], f[3], f.Length > 4 && f[4] == "1")); break;
                case "enable-name": Manager.Enable(f[1]); break;
                case "enable-effect": ScreenSpaceEffectManager.Enable(Named(f[1])); break;
                case "disable-name": Manager.Disable(f[1]); break;
                case "disable-effect": ScreenSpaceEffectManager.Disable(Named(f[1])); break;
                case "disableall": Manager.DisableAll(); break;
                case "render": Manager.RenderEffects(I(f[1]), I(f[2]), I(f[3]), I(f[4]), new RenderPlanBuilder()); break;
                case "example-enabled": Log.Add("X\t" + (Effects["exampleeffect"].IsEnabled() ? 1 : 0)); break;
                default: throw new InvalidOperationException("the golden names a step this replay does not know: " + f[0]);
            }
        }
    }

    private static List<string> Stock()
    {
        string path = RepoLayout.Repo("src", "test", "client", "Fixtures", "screenspaceeffects-stock.tsv");
        Assert.True(File.Exists(path), "the recorded stock screen-space effects are missing: " + path);
        var rows = File.ReadAllLines(path).Where(l => l.Length > 0).ToList();
        Assert.True(rows.Count(r => r.StartsWith("S\t", StringComparison.Ordinal)) > 25 && rows.Count(r => r.StartsWith("E\t", StringComparison.Ordinal)) > 40,
                    "the oracle parsed");
        return rows;
    }

    [Fact]
    public void EveryStepDoesWhatStocksDid()
    {
        var rig = new Rig();
        int steps = 0, held = 0;
        List<string> stock = Stock();
        for (int i = 0; i < stock.Count;)
        {
            string s = stock[i++];
            Assert.StartsWith("S\t", s);
            var expected = new List<string>();
            while (i < stock.Count && !stock[i].StartsWith("S\t", StringComparison.Ordinal))
                expected.Add(stock[i++]);
            rig.Log.Clear();
            rig.Step(s[2..].Split('\t'));
            Assert.True(expected.SequenceEqual(rig.Log),
                        $"step {s.Replace('\t', ' ')}:\n  stock   {string.Join(" | ", expected)}\n  managed {string.Join(" | ", rig.Log)}");
            steps++;
            held += expected.Count;
        }
        // The stimulus reached the subject: every step ran and most wrote rows.
        Assert.True(steps > 25 && held > 50, $"{steps} steps, {held} rows held");
    }

    [Fact]
    public void TheNewestRegistrationIsWalkedFirst()
    {
        var rig = new Rig();
        Assert.Equal(["hollow", "filmgrain", "exampleeffect", "beta", "alpha"], rig.Manager.Registrations.Select(r => r.Name));
    }

    [Fact]
    public void TheGameRegistersStocksExampleEffect()
    {
        var m = ScreenSpaceEffectManager.WithStockEffects(_ => { }, _ => false);
        var only = Assert.Single(m.Registrations);
        Assert.Equal("exampleeffect", only.Name);
        Assert.IsType<ExampleScreenSpaceEffect>(only.Effect);
    }

    [Fact]
    public void FilmgrainWithNoSuchEffectWarnsAndInitGoesOn()
    {
        // Stock dereferences the lookup's NULL here (the documented divergence).
        var warnings = new List<string>();
        var example = new ExampleScreenSpaceEffect();
        var m = new ScreenSpaceEffectManager(warnings.Add, p => p == "-filmgrain");
        m.Register("exampleeffect", example);
        m.Init();
        Assert.Equal(["Could not find screen space effect filmgrain\n"], warnings);
        Assert.False(example.IsEnabled());
    }

    [Fact]
    public void TheExampleEffectNeverRenders()
    {
        var plan = new RenderPlanBuilder();
        var example = new ExampleScreenSpaceEffect();
        example.Init();
        example.Enable(true);
        example.Render(0, 0, 1280, 720, plan);
        Assert.False(example.IsEnabled());
        Assert.Empty(plan.Ops);
    }

    [Fact]
    public void AReloadGivesEachEffectBackItsFlag()
    {
        var before = new Rig();
        before.Manager.Init();
        before.Manager.Enable("alpha");
        var writer = new ReloadWriter();
        ((IReloadParticipant)before.Manager).Save(writer);

        var after = new Rig();
        after.Log.Clear();
        ((IReloadParticipant)after.Manager).Restore(new ReloadReader(writer.ToArray()));
        Assert.Contains("E\talpha\tInit", after.Log);
        Assert.Equal("E\talpha\tEnable\t1", after.Log[after.Log.IndexOf("E\talpha\tInit") + 1]);
        Assert.Equal("E\tbeta\tEnable\t1", after.Log[after.Log.IndexOf("E\tbeta\tInit") + 1]);
        Assert.Equal("E\tfilmgrain\tEnable\t0", after.Log[after.Log.IndexOf("E\tfilmgrain\tInit") + 1]);
    }
}
