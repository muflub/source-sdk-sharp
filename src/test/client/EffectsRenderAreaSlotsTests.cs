using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The effects-render forwarder table's slot count, as the bind check reads it:
/// a library serving fewer slots than the struct reads is refused, so the count
/// is the ABI's own fact. 35 since the slot-addressed follow: the blast jump's
/// smoke wears a foot per slot, so a follow names its slot too (ParticleSystem-
/// FollowEntitySlot), beside the start/stop pair and the beam's follow-entity row.
/// </summary>
public class EffectsRenderAreaSlotsTests
{
    [Fact]
    public void TheEffectsRenderTableCarriesTheSlotRows()
        => Assert.Equal(35, Forwarders.SlotCount<ShimEffectsRenderClientFwd>());
}
