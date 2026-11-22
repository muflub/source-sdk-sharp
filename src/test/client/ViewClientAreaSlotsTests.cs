using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The view forwarder table's slot count, as the bind check reads it: a library
/// serving fewer slots than the struct reads is refused, so the count is the
/// ABI's own fact. 31 since the screen-overlay pair: the condition arms set the
/// full-screen material by name and read the standing one's name back
/// (ScreenOverlaySet, ScreenOverlayGet), after the hull trace against named
/// entities. 33 since the prediction's entity clip (ClientSolidEntities,
/// ClientClipHullToEntities).
/// </summary>
public class ViewClientAreaSlotsTests
{
    [Fact]
    public void TheViewClientTableCarriesTheEntityClipSlots()
        => Assert.Equal(33, Forwarders.SlotCount<ShimViewClientFwd>());
}
