using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The effects-render forwarder table's slot count, as the bind check reads it:
/// a library serving fewer slots than the struct reads is refused, so the count
/// is the ABI's own fact. 34 since the slot-addressed pair: a caller that counts
/// its own systems starts and stops one in a numbered slot (ParticleSystem-
/// StartSlot, ParticleSystemStopSlot), after the beam's follow-entity row.
/// </summary>
public class EffectsRenderAreaSlotsTests
{
    [Fact]
    public void TheEffectsRenderTableCarriesTheSlotRows()
        => Assert.Equal(34, Forwarders.SlotCount<ShimEffectsRenderClientFwd>());
}
