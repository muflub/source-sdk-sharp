using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Input;
using SourceSharp.Shared;

using TF2.Descent.Shared;

namespace TF2.Descent.Client.Belt;

//=============================================================================
// The hero's belt keys (plan_descent_logic HE8): the keypad's 1 to 4, which
// neither TF2's list nor the skill keys (4 to 9) bind. A press sends the
// server tf2_descent_belt <slot>, which this client answers nothing to, so the
// engine forwards it (as the skill keys' cast line goes).
//=============================================================================

/// <summary>The belt keys' shared numbers.</summary>
public static class BeltKeys
{
    /// <summary>The keys of belt slots 1 to 4.</summary>
    public static IReadOnlyList<string> SlotKeys => ["KP_END", "KP_DOWNARROW", "KP_PGDN", "KP_LEFTARROW"];

    /// <summary>The line a belt key sends the server.</summary>
    public static string DrinkLine(int slot) => $"tf2_descent_belt {slot}";
}

[KeyAction("tf2_descent_belt 1", "Belt 1", Section = "Descent", Order = 1110, Keys = ["KP_END"])]
public sealed class Belt1Key;

[KeyAction("tf2_descent_belt 2", "Belt 2", Section = "Descent", Order = 1120, Keys = ["KP_DOWNARROW"])]
public sealed class Belt2Key;

[KeyAction("tf2_descent_belt 3", "Belt 3", Section = "Descent", Order = 1130, Keys = ["KP_PGDN"])]
public sealed class Belt3Key;

[KeyAction("tf2_descent_belt 4", "Belt 4", Section = "Descent", Order = 1140, Keys = ["KP_LEFTARROW"])]
public sealed class Belt4Key;

/// <summary>
/// The hero's potion belt as his client knows it (HE8; for the HUD's belt strip, UI2): what
/// the server last sent in <see cref="BeltMessage"/>. Ticks are the server's; a sweep's
/// fraction is asked with the client's tick.
/// </summary>
public interface IBeltState
{
    /// <summary>True once the server has sent a belt this level.</summary>
    bool Received { get; }

    /// <summary>Always <see cref="Shared.Belt.Slots"/> (4).</summary>
    int SlotCount { get; }

    /// <summary>The potion base in slot <paramref name="i"/> (0-based); "" for an empty one.</summary>
    string BaseOf(int i);

    /// <summary>The potion's name in slot <paramref name="i"/>; "" for an empty one.</summary>
    string NameOf(int i);

    /// <summary>How many potions slot <paramref name="i"/> holds.</summary>
    int CountOf(int i);

    /// <summary>The server tick the shared cooldown ends; 0 when the belt is ready.</summary>
    int ReadyTick { get; }

    /// <summary>The cooldown's whole length in ticks; 0 when none ran.</summary>
    int CooldownTicks { get; }

    /// <summary>How much of the cooldown is left at <paramref name="tick"/>: 1 just drunk, 0 ready.</summary>
    float CooldownFraction(int tick);
}

/// <summary>
/// <see cref="IBeltState"/> over the belt message. It outlives a level as every client
/// service does (memory: client-state-outlives-level), so a level's end clears it: the
/// server sends it again when the client arrives.
/// </summary>
[SideService(Side.Client, As = [typeof(IBeltState)])]
public sealed class BeltState : IBeltState, IBeltHook, IClientLevelHook, IOrderedHook
{
    /// <summary>Beside the mod's other level hooks (10, 11) and the core's: unique in the whole container.</summary>
    public int HookOrder => 4810;

    private BeltView? _belt;

    public bool Received => _belt is not null;

    public int SlotCount => Shared.Belt.Slots;

    public string BaseOf(int i) => _belt is { } b && i >= 0 && i < b.Bases.Count ? b.Bases[i] : "";

    public string NameOf(int i) => _belt is { } b && i >= 0 && i < b.Names.Count ? b.Names[i] : "";

    public int CountOf(int i) => _belt is { } b && i >= 0 && i < b.Counts.Count ? b.Counts[i] : 0;

    public int ReadyTick => _belt?.ReadyTick ?? 0;

    public int CooldownTicks => _belt?.CooldownTicks ?? 0;

    public float CooldownFraction(int tick) =>
        _belt is { ReadyTick: > 0, CooldownTicks: > 0 } b && b.ReadyTick > tick
            ? Math.Clamp((b.ReadyTick - tick) / (float)b.CooldownTicks, 0f, 1f)
            : 0f;

    public void OnBelt(BeltView belt) => _belt = belt;

    /// <summary>The level is going: the next level's arrival brings the belt again.</summary>
    public void OnLevelShutdown() => _belt = null;

    /// <summary>A belt as the console shows it.</summary>
    public static string Describe(IBeltState belt)
    {
        var c = CultureInfo.InvariantCulture;
        if (!belt.Received)
            return "[TF2Descent/Belt] client belt: none received\n";
        var sb = new StringBuilder();
        sb.Append(c, $"[TF2Descent/Belt] client belt: ready tick {belt.ReadyTick} length {belt.CooldownTicks}\n");
        for (int i = 0; i < belt.SlotCount; i++)
        {
            string what = belt.BaseOf(i).Length == 0 ? "empty" : $"{belt.BaseOf(i)} '{belt.NameOf(i)}' x{belt.CountOf(i)}";
            sb.Append(c, $"  belt {i + 1}: {what}\n");
        }
        return sb.ToString();
    }
}

/// <summary>
/// The hero's gear wear as his client knows it (HE9; for the HUD's gear warning, UI2): each
/// equipment slot's last level crossing the server sent, and the worst of them.
/// </summary>
public interface IGearWearState
{
    /// <summary>Every slot the server has told of this level, by slot id.</summary>
    IReadOnlyDictionary<string, GearWearView> Slots { get; }

    /// <summary>The slot's last crossing, or null.</summary>
    GearWearView? Of(string slot);

    /// <summary>The worst piece to warn of (unusable, then broken, then low; the lowest fraction first); null when none.</summary>
    GearWearView? Warning { get; }
}

/// <summary><see cref="IGearWearState"/> over the gear wear message; cleared when the level ends.</summary>
[SideService(Side.Client, As = [typeof(IGearWearState)])]
public sealed class GearWearState : IGearWearState, IGearWearHook, IClientLevelHook, IOrderedHook
{
    public int HookOrder => 4811;

    private readonly Dictionary<string, GearWearView> _slots = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, GearWearView> Slots => _slots;

    public GearWearView? Of(string slot) => _slots.GetValueOrDefault(slot);

    public GearWearView? Warning =>
        _slots.Values.Where(v => v.Level is GearWearLevel.Low or GearWearLevel.Broken or GearWearLevel.Unusable)
            .OrderByDescending(v => v.Level)
            .ThenBy(v => v.MaxDurability > 0 ? v.Durability / (double)v.MaxDurability : 0)
            .Cast<GearWearView?>()
            .FirstOrDefault();

    public void OnGearWear(GearWearView wear)
    {
        if (wear.Level == GearWearLevel.Empty)
            _slots.Remove(wear.Slot);
        else
            _slots[wear.Slot] = wear;
    }

    public void OnLevelShutdown() => _slots.Clear();

    /// <summary>A gear wear state as the console shows it.</summary>
    public static string Describe(IGearWearState wear)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        string warning = wear.Warning is { } w ? $"{w.Slot} {w.Level}" : "none";
        sb.Append(c, $"[TF2Descent/Gear] client gear wear: {wear.Slots.Count} slots, warning {warning}\n");
        foreach (var v in wear.Slots.Values.OrderBy(v => v.Slot, StringComparer.Ordinal))
            sb.Append(c, $"  {v.Slot}: '{v.Name}' {v.Level} {v.Durability}/{v.MaxDurability}\n");
        return sb.ToString();
    }
}

/// <summary><c>tf2_descent_belt_cl</c>: the belt and gear wear this client was sent (cheat).</summary>
[ConCommand("tf2_descent_belt_cl", Flags = ConVarFlags.Cheat, Help = "tf2_descent_belt_cl: the belt and gear wear this client holds")]
public sealed class ClientBeltCommand(ILog log, IBeltState belt, IGearWearState wear)
{
    public void Execute() => log.Msg(BeltState.Describe(belt) + GearWearState.Describe(wear));
}

/// <summary>The client's dispatch of the belt's and the gear wear's hooks.</summary>
[SideService(Side.Client)]
public sealed class BeltClientRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry)
    {
        registry.AddHooks<IBeltHook>();
        registry.AddHooks<IGearWearHook>();
    }
}
