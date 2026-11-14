using SourceSharp.Abi;

// Compiled into both TF2.Descent.Server and TF2.Descent.Client, as Messages.cs.

namespace TF2.Descent.Shared;

/// <summary>
/// The hero's skill bar as its owner's client draws it (plan_descent_logic
/// HE6): what each slot holds, the server tick each slot's skill is ready
/// again, the slot charging and since when, and the toggles that are on.
/// </summary>
public interface ISkillBarHook
{
    void OnSkillBar(SkillBarView bar);
}

/// <summary>A received skill bar: one row a slot, ticks the server's.</summary>
/// <param name="Skills">The skill id in each slot, "" for an empty one (<see cref="SkillBar.Slots"/> of them).</param>
/// <param name="ReadyTicks">The server tick each slot's skill can be cast again; 0 when it is ready.</param>
/// <param name="ChargeSlot">The slot whose key is held on a charge skill (1..<see cref="SkillBar.Slots"/>), 0 for none.</param>
/// <param name="ChargeStartTick">The server tick the charge began; 0 with no charge.</param>
/// <param name="ToggledMask">Bit <c>n - 1</c> set while slot <c>n</c>'s toggle is on.</param>
/// <param name="ChargeTicks">The server ticks a full charge of the charging skill takes, as the skills runtime resolves it; 0 with no charge.</param>
public sealed record SkillBarView(IReadOnlyList<string> Skills, IReadOnlyList<int> ReadyTicks, int ChargeSlot,
                                  int ChargeStartTick, int ToggledMask, int ChargeTicks = 0);

/// <summary>
/// The skill bar on the wire: sent to the hero's own client when it changes
/// (a slot assigned, a cast, a charge begun or ended, a toggle switched).
/// One message of at most 6 x 24 characters of ids, 6 ticks and the charge's
/// length, under the engine's 255 bytes.
/// </summary>
public partial struct SkillBar
{
    /// <summary>A hero's skill slots.</summary>
    public const int Slots = 6;

    /// <summary>The longest skill id the bar carries (the table's longest is 17).</summary>
    public const int MaxId = 24;

    /// <summary>The slots' skill ids joined by ',' in slot order.</summary>
    [UserMessageString(MaxLength = Slots * (MaxId + 1))]
    public partial string Skills { get; set; }

    [UserMessageArray(MaxCount = Slots)]
    public partial int[] ReadyTicks { get; set; }

    public partial byte ChargeSlot { get; set; }

    public partial int ChargeStartTick { get; set; }

    public partial byte ToggledMask { get; set; }

    /// <summary>The server ticks the charging skill's full charge takes (its last stage); 0 with no charge.</summary>
    public partial int ChargeTicks { get; set; }

    /// <summary>The bar as the client reads it: missing slots empty, missing ticks ready.</summary>
    public SkillBarView View()
    {
        string[] ids = (Skills ?? "").Split(',');
        var skills = new string[Slots];
        var ready = new int[Slots];
        for (int i = 0; i < Slots; i++)
        {
            skills[i] = i < ids.Length ? ids[i] : "";
            ready[i] = ReadyTicks is { } r && i < r.Length ? r[i] : 0;
        }

        return new SkillBarView(skills, ready, ChargeSlot, ChargeStartTick, ToggledMask, ChargeTicks);
    }
}

[UserMessage(Name)]
public sealed class SkillBarMessage(IHooks<ISkillBarHook>? hooks) : IUserMessage<SkillBar>
{
    public SkillBarMessage() : this(null) { }

    public const string Name = "skill_bar";

    public void OnReceived(SkillBar message)
    {
        var view = message.View();
        hooks?.Each(nameof(ISkillBarHook.OnSkillBar), h => h.OnSkillBar(view));
    }
}
