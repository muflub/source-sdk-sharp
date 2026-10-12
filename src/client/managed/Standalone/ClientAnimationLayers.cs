using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// Animation LAYERS: a second animation blended on top of the first — the
/// gesture that waves while the legs keep walking, the flinch over an idle, the
/// aim offset that turns a torso without moving the feet.
///
/// <para>
/// <b>Stock's machinery for this is not in this build and is not coming back.</b>
/// <c>CBaseAnimatingOverlay</c> was evicted as unreachable, and restoring it
/// was considered and rejected: what it does is bookkeeping — which slots are
/// filled, what they weigh, what order they blend in, when they fade — and
/// bookkeeping is exactly what this port moves into C#. What was left for
/// native code is one more <c>AccumulatePose</c> per filled slot and the order
/// they go in, which is <c>anim_layer_rule.h</c> with a gtest on it.
/// </para>
///
/// <para>
/// So this class is the policy half: it names the layers, decides their weights
/// over time, and pushes the table. Everything it does could be done by any
/// other managed class that wants a gesture — the shell holds a table, not a
/// notion of what a gesture is.
/// </para>
///
/// <para>
/// SIDE-NESS: bucket 3, the client's, and that is a decision worth stating
/// because it could reasonably go the other way. Stock's overlays are
/// networked, because stock's server poses the same skeleton to trace hitboxes
/// against. This hierarchy's server has a skeleton that answers one narrower
/// question — where a child parented to an attachment sits — and nothing here
/// parents anything to an attachment on a model that is gesturing. So a layer
/// bends a skeleton only the local viewer reads, which is the pose-parameter
/// argument applied again. <b>When something does parent to a gesturing model,
/// this becomes dual and the layer TABLE goes on the wire as indices and
/// weights.</b> Never the cycle: that is a clock, and a clock both sides
/// legitimately disagree about every frame is not prediction state.
/// </para>
/// </summary>
public static class ClientAnimationLayers
{
    /// <summary>
    /// How many layer slots an entity has. Mirrors
    /// <c>SOURCESHARP_MAX_ANIM_LAYERS</c>.
    ///
    /// <para>
    /// Restated rather than crossed, because it is the length of a loop on both
    /// sides and a crossing per frame to ask a constant would be silly — and it
    /// is checked against the native header by
    /// <c>AnimationLayerTests.TheSlotCountAgreesWithTheNativeHeader</c>, which
    /// is the rule this tree learned from a stale tier table.
    /// </para>
    /// </summary>
    public const int MaxLayers = 8;

    /// <summary>
    /// Puts one named sequence into one layer slot on every matching entity,
    /// and says what happened to each.
    ///
    /// <para>
    /// By NAME, and a name no model carries is reported rather than swallowed —
    /// the bug that cost this project months was a sequence name nothing
    /// answered to, returning quietly.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Set(IReadOnlyList<ClientStandaloneEntity> live,
                                            IEngineEntityRender render,
                                            string filter, int slot, string sequenceName,
                                            float weight, int order)
    {
        var lines = new List<string>();

        if (slot < 0 || slot >= MaxLayers)
        {
            // Refused here rather than in the shell, which would refuse it
            // silently. A slot number out of range is a caller's arithmetic
            // being wrong, and a gesture that never appears is how that would
            // otherwise present.
            lines.Add($"no such layer slot {slot} — an entity has {MaxLayers}, numbered 0 to {MaxLayers - 1}");
            return lines;
        }

        int set = 0;

        foreach (var entity in live)
        {
            if (filter.Length > 0 && filter != "*" &&
                !entity.ClassName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            // "off" rather than a sequence name, so a caller can clear a slot
            // without knowing what is in it.
            if (string.Equals(sequenceName, "off", StringComparison.OrdinalIgnoreCase))
            {
                render.SetAnimLayer(entity.Handle, slot, -1, 0f, 1f, order, restart: true);
                set++;
                lines.Add($"{entity.ClassName}  layer {slot} off");
                continue;
            }

            int sequence = render.LookupSequence(entity.Handle, sequenceName);

            if (sequence < 0)
            {
                lines.Add($"{entity.ClassName}  {render.GetModelName(entity.Handle)}  " +
                          $"NO SEQUENCE '{sequenceName}'");
                continue;
            }

            render.SetAnimLayer(entity.Handle, slot, sequence, weight, 1f, order, restart: true);
            set++;

            lines.Add($"{entity.ClassName}  layer {slot} <- '{sequenceName}' as sequence {sequence}, " +
                      $"weight {weight:0.00}, order {order}");
        }

        lines.Add($"{set} layer(s) set");

        return lines;
    }

    /// <summary>
    /// What every live entity is really carrying, read back out of the shell.
    ///
    /// <para>
    /// Read BACK, not remembered. A report that printed the table this side had
    /// pushed would agree with itself whatever the shell did with it, and every
    /// interesting failure is on the far side: a slot refused for being out of
    /// range, a sequence the model does not have, a clock that never started
    /// because the sequence had no duration.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Report(IReadOnlyList<ClientStandaloneEntity> live,
                                               IEngineEntityRender render,
                                               string filter)
    {
        var lines = new List<string>();

        int looked = 0;
        int carrying = 0;

        foreach (var entity in live)
        {
            if (filter.Length > 0 && filter != "*" &&
                !entity.ClassName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (render.IsDormant(entity.Handle))
                continue;

            looked++;

            int active = render.AnimLayer(entity.Handle, 0, out _, out _, out _, out _);

            if (active <= 0)
                continue;

            carrying++;
            lines.Add($"{entity.ClassName}  {render.GetModelName(entity.Handle)}  " +
                      $"{active} layer(s)");

            for (int slot = 0; slot < MaxLayers; slot++)
            {
                render.AnimLayer(entity.Handle, slot, out int sequence, out float weight,
                                 out int order, out float cycle);

                if (sequence < 0)
                    continue;

                lines.Add($"    {slot}  sequence {sequence}  weight {weight:0.00}  " +
                          $"order {order}  cycle {cycle:0.000}");
            }
        }

        lines.Add($"{looked} live entit(ies) looked at, {carrying} carrying a layer");

        if (carrying == 0)
        {
            // Said out loud, for the reason cs_procbones says its version. A
            // screenshot of a level where nothing is layering proves nothing
            // about layers in either direction, and somebody will take one.
            lines.Add("nothing on this hierarchy fills a layer slot yet — the mechanism is here " +
                      "and no gameplay uses it, so cs_setlayer is how you see one");
        }

        return lines;
    }
}

/// <summary>The console name for <see cref="ClientAnimationLayers.Report"/>.</summary>
[ConCommand("cs_animlayers",
            Help = "What every live client entity is carrying in its animation layer slots, " +
                   "read back out of the shell.")]
public sealed class AnimLayersCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IClientStandalones _standalones;
    private readonly IEngineEntityRender _render;

    public AnimLayersCommand(ILog log, IClientStandalones standalones, IEngineEntityRender render)
    {

        _log = log;
        _standalones = standalones;
        _render = render;
    }

    public void Execute(string args)
    {
        foreach (string line in ClientAnimationLayers.Report(_standalones.Live(), _render, args.Trim()))
            _log.Msg($"[SourceSharp/Client] {line}\n");
    }
}

/// <summary>The console name for <see cref="ClientAnimationLayers.Set"/>.</summary>
[ConCommand("cs_setlayer",
            Help = "cs_setlayer <classname-filter> <slot> <sequence-name|off> [weight] [order]. " +
                   "Blends one sequence on top of whatever a matching entity is playing.")]
public sealed class SetLayerCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IClientStandalones _standalones;
    private readonly IEngineEntityRender _render;

    public SetLayerCommand(ILog log, IClientStandalones standalones, IEngineEntityRender render)
    {

        _log = log;
        _standalones = standalones;
        _render = render;
    }

    public void Execute(string args)
    {
        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 3 || !int.TryParse(parts[1], out int slot))
        {
            _log.Msg("[SourceSharp/Client] cs_setlayer <classname-filter> <slot> " +
                    "<sequence-name|off> [weight] [order]\n");
            return;
        }

        float weight = parts.Length > 3 && float.TryParse(parts[3], out float w) ? w : 1f;
        int order = parts.Length > 4 && int.TryParse(parts[4], out int o) ? o : 0;

        foreach (string line in ClientAnimationLayers.Set(_standalones.Live(), _render,
                                                          parts[0], slot, parts[2], weight, order))
            _log.Msg($"[SourceSharp/Client] {line}\n");
    }
}
