using SourceSharp.Abi;

// Compiled into both TF2.Descent.Server and TF2.Descent.Client, as Messages.cs.

namespace TF2.Descent.Shared;

//=============================================================================
// THE BOSS BAR ON THE WIRE (plan_descent_logic HO13; the HUD's boss bar, UI2):
// the level's boss, to each hero standing in its arena: its name, its life and
// most life, the phase it is in and how many it has. Sent when it changes (at
// most ten times a second) and once more, absent, when the hero leaves the
// arena or the boss dies.
//=============================================================================

/// <summary>The boss bar as a hero's client draws it.</summary>
public interface IBossBarHook
{
    void OnBossBar(BossBarView bar);
}

/// <summary>A received boss bar: absent, or a boss's row in robots.json plus one, its name, life, most life, phase (0-based) and phases.</summary>
public sealed record BossBarView(bool Present, int Template, string Name, int Life, int LifeMax, int Phase, int Phases)
{
    public static BossBarView Absent { get; } = new(false, 0, "", 0, 0, 0, 0);
}

/// <summary>The boss bar on the wire: well under the engine's 255 bytes.</summary>
public partial struct BossBar
{
    /// <summary>The longest name carried; a longer one is cut.</summary>
    public const int MaxName = 32;

    public partial byte Present { get; set; }

    public partial byte Template { get; set; }

    [UserMessageString(MaxLength = MaxName)]
    public partial string Name { get; set; }

    public partial int Life { get; set; }

    public partial int LifeMax { get; set; }

    public partial byte Phase { get; set; }

    public partial byte Phases { get; set; }

    public BossBarView View() =>
        Present == 0 ? BossBarView.Absent : new BossBarView(true, Template, Name ?? "", Life, LifeMax, Phase, Phases);

    /// <summary>The bar of <paramref name="view"/> as it goes on the wire (a name cut to <see cref="MaxName"/>).</summary>
    public static BossBar Of(BossBarView view) => new()
    {
        Present = (byte)(view.Present ? 1 : 0),
        Template = (byte)Math.Clamp(view.Template, 0, 255),
        Name = view.Name.Length <= MaxName ? view.Name : view.Name[..MaxName],
        Life = view.Life,
        LifeMax = view.LifeMax,
        Phase = (byte)Math.Clamp(view.Phase, 0, 255),
        Phases = (byte)Math.Clamp(view.Phases, 0, 255),
    };
}

[UserMessage(Name)]
public sealed class BossBarMessage(IHooks<IBossBarHook>? hooks) : IUserMessage<BossBar>
{
    public BossBarMessage() : this(null) { }

    public const string Name = "boss_bar";

    public void OnReceived(BossBar message)
    {
        var view = message.View();
        hooks?.Each(nameof(IBossBarHook.OnBossBar), h => h.OnBossBar(view));
    }
}
