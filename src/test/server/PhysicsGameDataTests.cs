using SourceSharp.Abi;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// PhysicsGameData against physics_gamedata.h: PhysicsGameDataFromHandle
/// stores (handle + 1) | tag, PhysicsHandleFromGameData undoes it.
/// </summary>
public class PhysicsGameDataTests
{
    [Fact]
    public void A_tagged_word_is_the_handle_plus_one()
        => Assert.Equal(new EntityHandle(0x0001_0005u),
                        PhysicsGameData.Owner(unchecked((nint)0x8000_0000_0001_0006UL)));

    [Fact]
    public void Handle_zero_the_world_stores_one()
        => Assert.Equal(new EntityHandle(0u), PhysicsGameData.Owner(unchecked((nint)0x8000_0000_0000_0001UL)));

    [Fact]
    public void No_game_data_is_no_owner()
        => Assert.Equal(EntityHandle.None, PhysicsGameData.Owner(0));

    [Fact]
    public void An_untagged_word_is_an_sdk_pointer_and_names_no_managed_entity()
        => Assert.Equal(EntityHandle.None, PhysicsGameData.Owner(unchecked((nint)0x0000_7f12_3456_7890UL)));

    [Fact]
    public void The_word_for_an_owner_is_its_handle_plus_one_tagged()
        => Assert.Equal(unchecked((nint)0x8000_0000_0001_0006UL), PhysicsGameData.FromHandle(new EntityHandle(0x0001_0005u)));

    [Fact]
    public void No_owner_carries_no_word()
        => Assert.Equal((nint)0, PhysicsGameData.FromHandle(EntityHandle.None));
}
