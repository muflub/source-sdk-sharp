using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The per-client transmit answer (tf2 F06): every registered
/// <see cref="ITransmitRule"/> is asked, a <see cref="TransmitAnswer.Never"/>
/// beats an <see cref="TransmitAnswer.Always"/>, and an
/// <see cref="TransmitAnswer.Always"/> beats the default — which is the PVS
/// check the engine's walk does anyway, so a rule with no opinion, and a
/// mod with no rule, leave the walk stock's flag matrix.
/// </summary>
public static class TransmitRules
{
    /// <summary>The chain's answer for <paramref name="entity"/> and client edict <paramref name="recipientSlot"/>.</summary>
    public static TransmitAnswer Combine(IReadOnlyList<ITransmitRule> rules, EntityHandle entity, int recipientSlot)
    {
        var answer = TransmitAnswer.Default;

        // Every rule is asked even under a Never: the chain's contract is
        // all-asked, so a rule can count on hearing about every edict.
        foreach (ITransmitRule rule in rules)
        {
            TransmitAnswer one = rule.ShouldTransmit(entity, recipientSlot);

            if (one == TransmitAnswer.Never)
                answer = TransmitAnswer.Never;
            else if (one == TransmitAnswer.Always && answer == TransmitAnswer.Default)
                answer = TransmitAnswer.Always;
        }

        return answer;
    }
}
