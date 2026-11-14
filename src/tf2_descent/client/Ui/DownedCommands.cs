using System.Globalization;
using System.Text;

using SourceSharp.Abi;

namespace TF2.Descent.Client.Ui;

/// <summary>
/// <c>tf2_descent_downed_cl</c>: every hero's downed state as this client holds it, what the downed
/// screen's source answers, and the corpses this client was sent (cheat): the client's half of
/// the WO6 and WO5 live proofs.
/// </summary>
[ConCommand("tf2_descent_downed_cl", Flags = ConVarFlags.Cheat, Help = "tf2_descent_downed_cl: the heroes' downed state, the downed screen's values and the corpses this client holds")]
public sealed class ClientDownedCommand(ILog log, IHeroStates heroes, IDescentDowned downed, ICorpseState corpses, IClientTicks ticks)
{
    public void Execute()
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append(c, $"[TF2Descent/Downed] client tick {ticks.Now}\n");
        foreach (var h in heroes.All())
            sb.Append(c, $"  hero {h.PlayerIndex}: downed {h.Downed} ticks {h.DownedStartTick}..{h.DownedEndTick}, reviver {h.ReviverIndex} ticks {h.ReviveStartTick}..{h.ReviveEndTick}\n");
        sb.Append(c, $"  screen: downed {downed.Downed}, bleed {downed.BleedOutSecondsLeft:0.0}/{downed.BleedOutTotal:0.0}s, ");
        sb.Append(c, $"revived {downed.BeingRevived} by '{downed.ReviverName}' {downed.ReviveFraction:0%}, nearest '{downed.NearestMemberName}' {downed.NearestMemberDistance}u\n");
        foreach (var corpse in corpses.All())
            sb.Append(c, $"  corpse of {corpse.OwnerIndex} at ({corpse.At.X:0} {corpse.At.Y:0} {corpse.At.Z:0}): {corpse.Items} item(s){(corpse.Hardcore ? ", hardcore" : "")}\n");
        sb.Append(c, $"  local corpse: {(corpses.Local() is { } mine ? $"({mine.At.X:0} {mine.At.Y:0} {mine.At.Z:0})" : "none")}\n");
        log.Msg(sb.ToString());
    }
}
