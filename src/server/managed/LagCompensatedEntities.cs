using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// One entity that is not a player and opted into the lag compensation
/// (<see cref="ScriptedEntity.LagCompensated"/>), as the frame finds it.
/// </summary>
/// <param name="Handle">The entity.</param>
/// <param name="HistorySerial">
/// <see cref="ScriptedEntity.LagHistorySerial"/>: a record taken under another
/// serial is someone else's past.
/// </param>
/// <param name="MaxSpeed"><see cref="ScriptedEntity.LagCompensationMaxSpeed"/>.</param>
public readonly record struct LagCompensatedEntity(EntityHandle Handle, int HistorySerial, float MaxSpeed);

/// <summary>
/// The entities other than players the lag compensation keeps a history for
/// and moves back. Stock has no such list (it walks the players only).
/// </summary>
public interface ILagCompensatedEntities
{
    /// <summary>Every entity opted in now, into <paramref name="into"/> (cleared first).</summary>
    void Collect(List<LagCompensatedEntity> into);

    /// <summary><paramref name="entity"/> as it stands now; false when it is not opted in (or gone).</summary>
    bool TryGet(EntityHandle entity, out LagCompensatedEntity now);
}

/// <summary>
/// <see cref="ILagCompensatedEntities"/> over the managed entities: each live
/// <see cref="ScriptedEntity"/> asked <see cref="ScriptedEntity.LagCompensated"/>.
/// </summary>
public sealed class ScriptedLagCompensatedEntities(EntityDirectory directory) : ILagCompensatedEntities
{
    // The live entities, copied each frame; kept so the copy allocates nothing.
    private readonly List<ScriptedEntity> _live = new();

    /// <inheritdoc/>
    public void Collect(List<LagCompensatedEntity> into)
    {
        into.Clear();
        directory.CopyLive(_live);

        foreach (ScriptedEntity entity in _live)
        {
            if (entity.LagCompensated)
                into.Add(Of(entity));
        }

        // Hold no entity past the frame.
        _live.Clear();
    }

    /// <inheritdoc/>
    public bool TryGet(EntityHandle entity, out LagCompensatedEntity now)
    {
        if (directory.Resolve(entity) is { LagCompensated: true } scripted)
        {
            now = Of(scripted);
            return true;
        }

        now = default;
        return false;
    }

    private static LagCompensatedEntity Of(ScriptedEntity entity)
        => new(entity.Handle, entity.LagHistorySerial, entity.LagCompensationMaxSpeed);
}
