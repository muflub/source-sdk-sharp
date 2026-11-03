using System.Collections.Immutable;

namespace SourceSharp.Ai;

/// <summary>One saved state of a profile: its number, when, a note, and the levels.</summary>
public readonly record struct ProfileRevision(int Number, DateTimeOffset At, string Note, ImmutableArray<int> Levels);

/// <summary>
/// A profile being edited, with its history (plan_hl2mp_ai §3.5, the
/// interpretation stated there): undo/redo of point edits, a ring of the last
/// <see cref="UndoDepth"/>; and the saved profile's revision list, capped at
/// <see cref="RevisionCap"/> (oldest dropped), where a restore makes the
/// chosen revision current and appends "restored #n", so nothing is lost by
/// restoring. Pure: the caller hands in the time.
/// </summary>
public sealed class EditHistory
{
    public const int UndoDepth = 64;

    public const int RevisionCap = 32;

    private readonly SkillPool _pool;
    private readonly LinkedList<SkillProfile> _undo = new();
    private readonly Stack<SkillProfile> _redo = new();
    private readonly List<ProfileRevision> _revisions;
    private int _nextRevision;

    public EditHistory(SkillPool pool, SkillProfile start, IEnumerable<ProfileRevision> revisions)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(revisions);
        SkillVerdict verdict = pool.Judge(start);
        if (!verdict.Accepted)
            throw new ArgumentException(verdict.Refusal, nameof(start));
        _pool = pool;
        Current = start;
        _revisions = revisions.OrderBy(r => r.Number).ToList();
        _nextRevision = _revisions.Count == 0 ? 1 : _revisions[^1].Number + 1;
        TrimRevisions();
    }

    public SkillProfile Current { get; private set; }

    /// <summary>The points left in the pool.</summary>
    public int PointsLeft => _pool.Points - Current.Spent;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public IReadOnlyList<ProfileRevision> Revisions => _revisions;

    /// <summary>
    /// Moves skill <paramref name="index"/> by <paramref name="delta"/> points
    /// (the editor's −/+ buttons, ±5 with shift), if the result fits the pool.
    /// A new edit clears the redo list.
    /// </summary>
    public SkillVerdict Stage(int index, int delta)
    {
        if ((uint)index >= (uint)Current.Levels.Length)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"the profile has {Current.Levels.Length} skills");
        SkillProfile next = Current.With(index, Current.Levels[index] + delta);
        SkillVerdict verdict = _pool.Judge(next);
        if (!verdict.Accepted)
            return verdict;
        Push(next);
        return verdict;
    }

    public bool Undo()
    {
        if (_undo.Last is not { } last)
            return false;
        _undo.RemoveLast();
        _redo.Push(Current);
        Current = last.Value;
        return true;
    }

    public bool Redo()
    {
        if (!_redo.TryPop(out SkillProfile? next))
            return false;
        _undo.AddLast(Current);
        Current = next;
        return true;
    }

    /// <summary>Appends the current levels as a revision.</summary>
    public ProfileRevision Save(DateTimeOffset at, string note)
    {
        var r = new ProfileRevision(_nextRevision++, at, note, Current.Levels);
        _revisions.Add(r);
        TrimRevisions();
        return r;
    }

    /// <summary>
    /// Makes revision <paramref name="number"/> current (an undoable edit) and
    /// records that as a new revision, "restored #n".
    /// </summary>
    public ProfileRevision Restore(int number, DateTimeOffset at)
    {
        int i = _revisions.FindIndex(r => r.Number == number);
        if (i < 0)
            throw new ArgumentOutOfRangeException(nameof(number), number, "no such revision");
        var restored = new SkillProfile(Current.Name, Current.Skills, _revisions[i].Levels);
        SkillVerdict verdict = _pool.Judge(restored);
        if (!verdict.Accepted)
            throw new InvalidOperationException($"revision #{number} does not fit the pool: {verdict.Refusal}");
        Push(restored);
        return Save(at, $"restored #{number}");
    }

    private void Push(SkillProfile next)
    {
        _undo.AddLast(Current);
        if (_undo.Count > UndoDepth)
            _undo.RemoveFirst();
        _redo.Clear();
        Current = next;
    }

    private void TrimRevisions()
    {
        if (_revisions.Count > RevisionCap)
            _revisions.RemoveRange(0, _revisions.Count - RevisionCap);
    }
}
