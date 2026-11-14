using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Shared;
using TF2.Shared.Econ;

namespace SourceSharp.Ai.Replay;

/// <summary>
/// TF2's blast-jump simulator as the tool builds it with no game (plan_bots_teams_tf2_part_g G.5):
/// the mod's class table over a Soldier class script read from disk, every convar of the mod's
/// shared assembly at its declared default (a console that refuses each registration, so each holds
/// its default), and engine seams that refuse every question, as the Jump facts' rig has them.
/// </summary>
public static class OfflineTF2
{
    /// <summary>The stock rocket launcher's script numbers (tf_weapon_rocketlauncher: Damage 90, TimeFireDelay 0.8), which the game reads from its script.</summary>
    public const float RocketDamage = 90f, RocketRefire = 0.8f;

    /// <summary>A console that holds nothing: every convar registration is refused, so each convar holds its declared default.</summary>
    private sealed class NoConsole : IEngineConsole
    {
        public void ExecuteServerCommand(string command)
        {
        }

        public string? FindConVarValue(string name) => null;

        public IReadOnlyList<string> CompleteCommandLine(string line) => [];

        public string GetConVarValue(int id) => "";

        public void Msg(string message)
        {
        }

        public int RegisterConCommand(string name, string help, ConVarFlags flags) => -1;

        public int RegisterConVar(string name, string defaultValue, string help, int flags, float? min = null, float? max = null) => -1;

        public void SendClientCommand(EntityHandle player, string command)
        {
        }

        public void SetConVarValue(int id, string value)
        {
        }

        public void SetConVarValueByName(string name, string value)
        {
        }

        public int Snapshot() => 0;

        public bool SnapshotEntry(int index, out NativeConsoleEntry entry)
        {
            entry = default;
            return false;
        }

        public void Warning(string message)
        {
        }
    }

    /// <summary>The game's files: the Soldier's class script from <paramref name="soldierScript"/>, and nothing else.</summary>
    private sealed class ClassScripts(string soldierScript) : IEngineFiles
    {
        public int GameFileSize(string path) => ReadGameFileBytes(path)?.Length ?? -1;

        public string? ReadGameFile(string path) => null;

        public byte[]? ReadGameFileBytes(string path)
            => path == TFClassTable.ScriptName(TFClass.Soldier) + ".txt" ? File.ReadAllBytes(soldierScript) : null;

        public byte[]? ReadFileOnPath(string path, string pathId) => pathId == "GAME" ? ReadGameFileBytes(path) : null;
    }

    private sealed class NoLog : ILog
    {
        public void Msg(string message)
        {
        }

        public void Warning(string message)
        {
        }
    }

    /// <summary>An engine seam that answers nothing: a simulation that asks it fails, naming the question.</summary>
    public class Refusing : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => throw new InvalidOperationException($"the simulation asked the engine: {method?.DeclaringType?.Name}.{method?.Name}");
    }

    /// <summary>The class table over the Soldier's script at <paramref name="soldierScript"/>.</summary>
    public static TFClassTable Classes(string soldierScript) => new(new NoLog(), new ClassScripts(soldierScript));

    /// <summary>The simulators' factory, as the game's container builds it, over the class table and the mod's convars at their defaults.</summary>
    public static BlastJumpSims Sims(TFClassTable classes)
    {
        ConVarRegistry v = ConVarRegistry.For(new NoConsole());
        v.RegisterAll(typeof(TFClampBackSpeedConVar).Assembly);
        T C<T>() where T : ConVarBase => v.All.OfType<T>().First();
        return new BlastJumpSims(classes, new AttributeHooks(), DispatchProxy.Create<IEnginePlayers, Refusing>(), DispatchProxy.Create<IEngineEntities, Refusing>(),
                                 C<TFScoutAirDashCountConVar>(), C<TFSpyMaxCloakedSpeedConVar>(), C<TFClampBackSpeedConVar>(), C<TFClampBackSpeedMinConVar>(),
                                 C<TFPlayerMovementRestartFreezeConVar>(), C<TFMovementAircurrentFrictionMultConVar>(), C<TFMovementLostFootingRestickConVar>(),
                                 C<TFMovementLostFootingFrictionConVar>(), C<TFMaxChargeSpeedConVar>(), C<TFParachuteMaxSpeedXYConVar>(),
                                 C<TFParachuteMaxSpeedZConVar>(), C<TFParachuteMaxSpeedOnfireZConVar>(), C<TFClampAirducksConVar>(),
                                 C<TFParachuteAirControlConVar>(), C<TFDemomanChargeFrametimeScalingConVar>());
    }
}
