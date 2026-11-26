using SourceSharp.Abi;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Server;

namespace SourceSharp.Ai.TF2.Senses;

/// <summary>
/// Each player index's lives: its spawns, counted as they happen (every client sees a player respawn). A disguise's alias
/// is one per disguise of one life (<see cref="TFBotGameView.DisguiseKey"/>), so a mark a bot puts on the alias never
/// outlives the Spy's life (review F7: the actor serial is per connection).
/// </summary>
[SideService(Side.Server)]
public sealed class TFPlayerLives : IPlayerSpawnHook, IOrderedHook
{
    private readonly int[] _lives = new int[VisionPass.Slots];

    /// <summary>After the game's own spawn hooks, in the AI's range.</summary>
    public int HookOrder => ModStages.First + 907;

    public void OnSpawning(EntityHandle player, float now)
    {
    }

    public void OnSpawned(EntityHandle player, float now)
    {
        int index = player.EntryIndex;
        if ((uint)index < (uint)_lives.Length)
            _lives[index]++;
    }

    /// <summary>How many times the player in <paramref name="index"/> has spawned.</summary>
    public int LifeOf(int index) => (uint)index < (uint)_lives.Length ? _lives[index] : 0;
}
