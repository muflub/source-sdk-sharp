using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Ui;

using TF2.Descent.Client.Content;
using TF2.Descent.Client.Items;
using TF2.Descent.Client.Skills;
using TF2.Descent.Shared;

namespace TF2.Descent.Client.Ui;

//=============================================================================
// The controls the windows lacked (the polish lane, after the coordinator's
// play): the skill bar's slots as the HUD strip and the skill window draw
// them, the verb that assigns into one, the backpack's drop, and the status
// line an open window shows so a refusal is not lost under it. Each asks the
// server with a verb it already answers; the server decides.
//=============================================================================

/// <summary>
/// The skill bar's six slots (<see cref="IDescentSkillSlots"/>) from the bar the server
/// last sent: each slot's skill name token from the content the client embeds (an id the
/// client does not have shows as nothing), the charging slot, and the cooldown: the bar
/// carries the tick a slot is ready again, and this keeps the client's tick when that
/// moved later (a cast), so the share left is <c>(ready - now) / (ready - then)</c>.
/// Before a bar arrives, six empty slots.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentSkillSlots)])]
public sealed class SkillSlotsSource(SkillBarState bar, IClientTicks ticks, DescentCatalog catalog)
    : IDescentSkillSlots, ISkillBarHook, IClientLevelHook, IOrderedHook
{
    /// <summary>After the bar's own state (20), so a read in the same dispatch sees it.</summary>
    public int HookOrder => 21;

    private readonly int[] _ready = new int[SkillBar.Slots];
    private readonly int[] _from = new int[SkillBar.Slots];

    public int SlotCount => SkillBar.Slots;

    public string SlotNameToken(int slot) =>
        bar.Bar is { } view && slot >= 0 && slot < view.Skills.Count && view.Skills[slot] is { Length: > 0 } id && catalog.Skill(id) is { } skill
            ? skill.NameToken
            : "";

    public float SlotCooldown(int slot)
    {
        if (slot < 0 || slot >= SkillBar.Slots)
            return 0f;
        int ready = _ready[slot], from = _from[slot], now = ticks.Now;
        if (ready <= now || ready <= from)
            return 0f;
        return Math.Clamp((ready - now) / (float)(ready - from), 0f, 1f);
    }

    public bool SlotCharging(int slot) => bar.Bar is { ChargeSlot: > 0 } view && view.ChargeSlot == slot + 1;

    /// <summary>A new bar: a slot whose ready tick moved later started a cooldown now.</summary>
    public void OnSkillBar(SkillBarView view)
    {
        int now = ticks.Now;
        for (int i = 0; i < SkillBar.Slots && i < view.ReadyTicks.Count; i++)
        {
            int ready = view.ReadyTicks[i];
            if (ready > now && ready != _ready[i])
                _from[i] = now;
            _ready[i] = ready;
        }
    }

    /// <summary>The client's ticks start again with a level: the cooldowns kept are the last level's.</summary>
    public void OnLevelShutdown()
    {
        Array.Clear(_ready);
        Array.Clear(_from);
    }
}

/// <summary>A skill-bar assignment (<see cref="IDescentSkillSlotActions"/>): <c>tf2_descent_assign &lt;slot 1-6&gt; &lt;skill&gt;</c>.</summary>
[SideService(Side.Client, As = [typeof(IDescentSkillSlotActions)])]
public sealed class SkillSlotActions(DescentCatalog catalog, IDescentCommandLine line) : IDescentSkillSlotActions
{
    public void Assign(int slot, int skillId)
    {
        if (slot < 0 || slot >= SkillBar.Slots || DescentCatalog.At(catalog.Skills, skillId) is not { } skill)
            return;
        line.Send(string.Create(CultureInfo.InvariantCulture, $"tf2_descent_assign {slot + 1} {skill.Id}"));
    }
}

/// <summary>The backpack's drop (<see cref="IDescentItemDrop"/>): <c>tf2_descent_item_drop &lt;cell&gt;</c> for a cell holding an item.</summary>
[SideService(Side.Client, As = [typeof(IDescentItemDrop)])]
public sealed class ItemDropAction(IItemViews items, IDescentCommandLine line) : IDescentItemDrop
{
    public void Drop(int cell)
    {
        if (cell >= 0 && items.In(ItemContainer.Backpack, cell) is not null)
            line.Send(string.Create(CultureInfo.InvariantCulture, $"tf2_descent_item_drop {cell}"));
    }
}

/// <summary>
/// An open window's status line (<see cref="IDescentWindowStatus"/>): every notice the
/// server sends (a refusal's <c>#Descent_Refused_*</c>, a skill's refusal, a recovery) worded
/// as the HUD's notice line words it (a token through the mod's language file, its
/// <c>%s1</c> the argument; the UI's <see cref="IUiLocalisation"/> asked when the notice
/// comes, as <see cref="HudNotices"/> asks it), shown for <see cref="ShowSeconds"/>. The
/// HUD's line still shows it too; under a window it is hidden.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentWindowStatus)])]
public sealed class WindowStatusSource(IGameClock clock, IServiceLocator services)
    : IDescentWindowStatus, INoticeHook, IClientLevelHook, IOrderedHook
{
    /// <summary>How long a notice stays on the line.</summary>
    public const float ShowSeconds = 5f;

    /// <summary>After the HUD's notices (11).</summary>
    public int HookOrder => 12;

    private float _at = float.NegativeInfinity;

    public string Text { get; private set; } = "";

    public bool Showing => Text.Length > 0 && clock.Now >= _at && clock.Now - _at <= ShowSeconds;

    public void OnNotice(string text, string arg)
    {
        Text = text.Length > 0 && text[0] == '#'
            ? (services.Resolve<IUiLocalisation>()?.Find(text) ?? text).Replace("%s1", arg, StringComparison.Ordinal)
            : text;
        _at = clock.Now;
    }

    /// <summary>The clock starts again with a level: the last level's line is gone.</summary>
    public void OnLevelShutdown()
    {
        Text = "";
        _at = float.NegativeInfinity;
    }
}
