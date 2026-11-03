using Xunit;

namespace SourceSharp.Ai.Tests.Skills;

public class EditHistoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    private static EditHistory Fresh() => new(StandardSkills.StandardPool(), StandardSkills.StandardPool().Empty("bot"), []);

    [Fact]
    public void AStagedEditMovesOneSkill()
    {
        EditHistory h = Fresh();
        h.Stage(0, 3);
        Assert.Equal(("3,0,0,0,0,0,0,0,0,0", 57), (string.Join(",", h.Current.Levels), h.PointsLeft));
    }

    [Fact]
    public void TenEditsThreeUndosTwoRedosLandWhereExpected()
    {
        EditHistory h = Fresh();
        for (int i = 0; i < 10; i++)
            h.Stage(i, i + 1);
        h.Undo();
        h.Undo();
        h.Undo();
        h.Redo();
        h.Redo();
        // Edits 1..9 applied (skill i at i + 1), the tenth undone.
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 0], h.Current.Levels.ToArray());
    }

    [Fact]
    public void ANewEditClearsRedo()
    {
        EditHistory h = Fresh();
        h.Stage(0, 1);
        h.Stage(1, 1);
        h.Undo();
        h.Stage(2, 1);
        Assert.False(h.CanRedo, "the edit after an undo starts a new branch");
    }

    [Fact]
    public void UndoReachesBackSixtyFourEdits()
    {
        EditHistory h = Fresh();
        for (int i = 0; i < 70; i++)
            h.Stage(0, i % 2 == 0 ? 1 : -1);
        int undone = 0;
        while (h.Undo())
            undone++;
        Assert.Equal(EditHistory.UndoDepth, undone);
    }

    [Fact]
    public void AnEditPastTheCapIsRefusedAndNotRecorded()
    {
        EditHistory h = Fresh();
        h.Stage(0, 10);
        SkillVerdict v = h.Stage(0, 1);
        Assert.Equal((false, 10, true), (v.Accepted, h.Current["accuracy"], h.Undo() && !h.Undo()));
    }

    [Fact]
    public void AnEditPastThePoolIsRefused()
    {
        EditHistory h = Fresh();
        for (int i = 0; i < 6; i++)
            h.Stage(i, 10);
        Assert.False(h.Stage(6, 1).Accepted, "the 61st point is refused");
    }

    [Fact]
    public void SavesAreNumberedInOrder()
    {
        EditHistory h = Fresh();
        h.Save(T0, "first");
        h.Stage(0, 1);
        h.Save(T0.AddMinutes(1), "second");
        Assert.Equal([1, 2], h.Revisions.Select(r => r.Number));
    }

    [Fact]
    public void RevisionsKeepTheLastThirtyTwo()
    {
        EditHistory h = Fresh();
        for (int i = 0; i < 40; i++)
            h.Save(T0.AddMinutes(i), "r" + i);
        Assert.Equal((32, 9), (h.Revisions.Count, h.Revisions[0].Number));
    }

    [Fact]
    public void RestoringARevisionMakesItCurrent()
    {
        EditHistory h = Fresh();
        h.Stage(0, 5);
        h.Save(T0, "five");
        h.Stage(0, 3);
        h.Restore(1, T0.AddMinutes(1));
        Assert.Equal(5, h.Current["accuracy"]);
    }

    [Fact]
    public void RestoringAppendsARestoredRevision()
    {
        EditHistory h = Fresh();
        h.Save(T0, "first");
        h.Stage(0, 3);
        h.Save(T0.AddMinutes(1), "second");
        h.Restore(1, T0.AddMinutes(2));
        Assert.Equal(("restored #1", 3, 3), (h.Revisions[^1].Note, h.Revisions[^1].Number, h.Revisions.Count));
    }

    [Fact]
    public void ARestoreCanBeUndone()
    {
        EditHistory h = Fresh();
        h.Save(T0, "empty");
        h.Stage(0, 4);
        h.Restore(1, T0.AddMinutes(1));
        h.Undo();
        Assert.Equal(4, h.Current["accuracy"]);
    }

    [Fact]
    public void AStartThatDoesNotFitIsRefused()
        => Assert.Throws<ArgumentException>(() => new EditHistory(StandardSkills.StandardPool(),
               new SkillProfile("x", StandardSkills.Ten, [10, 10, 10, 10, 10, 10, 10, 0, 0, 0]), []));

    [Fact]
    public void RevisionNumbersContinueFromTheSavedOnes()
    {
        var saved = new ProfileRevision(7, T0, "old", [.. new int[10]]);
        var h = new EditHistory(StandardSkills.StandardPool(), StandardSkills.StandardPool().Empty("bot"), [saved]);
        Assert.Equal(8, h.Save(T0.AddMinutes(1), "new").Number);
    }
}
