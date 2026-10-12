using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What cs_weapons' lines say — the report over slots read as untyped handles.
/// The command is a console name and a write; everything that decides content
/// is <see cref="WeaponInventory"/> and is checked here.
/// </summary>
public class WeaponInventoryTests
{
    [Fact]
    public void ReportNamesEverySlotWithItsHandleAndMarksTheActiveOne()
    {
        var slots = new List<WeaponInventory.Slot>
        {
            new(0, 0x0000_1234, "weapon_pistol", Active: false),
            new(2, 0x0056_0078, "weapon_shotgun", Active: true),
        };

        var lines = WeaponInventory.Report(slots, viewModelHandle: 0x00AB_CDEF);

        Assert.Equal(3, lines.Count);
        Assert.Contains("slot  0", lines[0]);
        Assert.Contains("weapon_pistol", lines[0]);
        Assert.Contains("0x00001234", lines[0]);
        Assert.DoesNotContain("(active)", lines[0]);

        Assert.Contains("slot  2", lines[1]);
        Assert.Contains("weapon_shotgun", lines[1]);
        Assert.Contains("(active)", lines[1]);

        Assert.Contains("viewmodel: handle 0x00ABCDEF", lines[2]);
    }

    [Fact]
    public void EmptyInventoryAndMissingViewModelAreSaidNotSkipped()
    {
        // "No weapons" and "never asked" must not look the same in a listing —
        // the same rule ClientAttachments' report follows.
        var lines = WeaponInventory.Report(new List<WeaponInventory.Slot>(), uint.MaxValue);

        Assert.Equal(2, lines.Count);
        Assert.Contains("no weapons", lines[0]);
        Assert.Contains("viewmodel: none", lines[1]);
    }

    [Fact]
    public void SlotCountIsTheNativeArraysBound()
    {
        // MAX_WEAPONS in shareddefs.h. If this drifts, cs_weapons silently
        // stops seeing the tail of the inventory.
        Assert.Equal(48, WeaponInventory.SlotCount);
    }
}
