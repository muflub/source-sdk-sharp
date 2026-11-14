using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Descent.Client.Ui;
using TF2.Descent.Shared;

namespace TF2.Descent.Client.Boss;

/// <summary>
/// The HUD's boss bar on live data (<see cref="IDescentBoss"/>; plan_descent_logic HO13, UI2): what
/// the server last sent in <see cref="BossBarMessage"/>: present only while this hero stands in
/// the boss's arena and the boss lives, with its name, life, most life, phase and phases. It
/// outlives a level as every client service does (memory: client-state-outlives-level), so a
/// level's end clears it.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentBoss)])]
public sealed class BossBarSource : IDescentBoss, IBossBarHook, IClientLevelHook, IOrderedHook
{
    /// <summary>Beside the belt's (4810, 4811): unique in the container.</summary>
    public int HookOrder => 4820;

    private BossBarView _bar = BossBarView.Absent;

    /// <summary>Bars received this level.</summary>
    public int Received { get; private set; }

    public bool Present => _bar.Present;

    public string Name => _bar.Name;

    public int Life => _bar.Life;

    public int LifeMax => _bar.LifeMax;

    public int PhaseCount => _bar.Phases;

    public int Phase => _bar.Phase;

    public void OnBossBar(BossBarView bar)
    {
        _bar = bar;
        Received++;
    }

    public void OnLevelShutdown()
    {
        _bar = BossBarView.Absent;
        Received = 0;
    }

    /// <summary>The bar as the console shows it.</summary>
    public static string Describe(IDescentBoss boss) => boss.Present
        ? string.Create(CultureInfo.InvariantCulture, $"[TF2Descent/Boss] client boss bar: {boss.Name} {boss.Life}/{boss.LifeMax}, phase {boss.Phase + 1} of {boss.PhaseCount}\n")
        : "[TF2Descent/Boss] client boss bar: none\n";
}

/// <summary><c>tf2_descent_boss_cl</c>: the boss bar this client holds (cheat).</summary>
[ConCommand("tf2_descent_boss_cl", Flags = ConVarFlags.Cheat, Help = "tf2_descent_boss_cl: the boss bar this client holds")]
public sealed class ClientBossCommand(ILog log, IDescentBoss boss)
{
    public void Execute() => log.Msg(BossBarSource.Describe(boss));
}

/// <summary>The client's dispatch of the boss bar's hook.</summary>
[SideService(Side.Client)]
public sealed class BossClientRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry) => registry.AddHooks<IBossBarHook>();
}
