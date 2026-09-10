using GitApp.Domain;
using GitApp.Services;

namespace GitApp.Core.Tests;

/// <summary>
/// Parsing <c>git status --porcelain=v2 -z</c>.
///
/// These fixtures are real output, captured from git 2.55 rather than
/// written from the documentation, because the failure this guards against
/// is a mismatch between what the docs describe and what git emits.
///
/// NUL is written as \0 here; the real stream is NUL-delimited with a
/// trailing terminator.
/// </summary>
public class StatusParsingTests
{
    private const char Nul = '\0';

    private static string Fields(params string[] records) =>
        string.Join(Nul, records) + Nul;

    [Fact]
    public void ReadsBranchAndUpstream()
    {
        var raw = Fields(
            "# branch.oid d1cc283527d0803eb2b5666a43da3fe9598ad20d",
            "# branch.head main",
            "# branch.upstream origin/main",
            "# branch.ab +2 -3");

        var status = GitService.ParseStatus(raw);

        Assert.Equal("main", status.Branch);
        Assert.Equal("origin/main", status.Upstream);
        Assert.Equal(2, status.Ahead);
        Assert.Equal(3, status.Behind);
        Assert.Equal("2 ahead, 3 behind", status.SyncDescription);
    }

    [Fact]
    public void ReportsNoUpstreamRatherThanPretendingItIsUpToDate()
    {
        // A branch with no upstream has no branch.ab line at all. Reading
        // that as "0 ahead, 0 behind" would tell the user they are in sync
        // with a remote that does not exist.
        var raw = Fields(
            "# branch.oid d1cc283",
            "# branch.head main");

        var status = GitService.ParseStatus(raw);

        Assert.Null(status.Upstream);
        Assert.Equal("no upstream", status.SyncDescription);
    }

    [Fact]
    public void DetectsDetachedHead()
    {
        var raw = Fields(
            "# branch.oid d1cc283",
            "# branch.head (detached)");

        var status = GitService.ParseStatus(raw);

        Assert.True(status.IsDetachedHead);
        Assert.Equal("detached head", status.SyncDescription);
    }

    [Fact]
    public void ReadsOrdinaryChanges()
    {
        var raw = Fields(
            "# branch.head main",
            "1 .M N... 100644 100644 100644 08876d7 08876d7 src/GitApp/MainPage.xaml",
            "1 M. N... 100644 100644 100644 003d5d1 003d5d1 src/GitApp/MauiProgram.cs",
            "1 .D N... 100644 100644 000000 643628c 643628c src/GitApp/Domain/Repo.cs");

        var status = GitService.ParseStatus(raw);

        Assert.Equal(3, status.Changes.Count);

        var modified = status.Changes.Single(c => c.Path.EndsWith("MainPage.xaml"));
        Assert.Equal(ChangeKind.Unmodified, modified.Staged);
        Assert.Equal(ChangeKind.Modified, modified.Unstaged);
        Assert.False(modified.IsStaged);

        var staged = status.Changes.Single(c => c.Path.EndsWith("MauiProgram.cs"));
        Assert.Equal(ChangeKind.Modified, staged.Staged);
        Assert.True(staged.IsStaged);

        var deleted = status.Changes.Single(c => c.Path.EndsWith("Repo.cs"));
        Assert.Equal(ChangeKind.Deleted, deleted.Unstaged);
    }

    [Fact]
    public void ReadsUntrackedFiles()
    {
        var raw = Fields(
            "# branch.head main",
            "? src/GitApp.Core/Services/GitService.cs");

        var status = GitService.ParseStatus(raw);

        var change = Assert.Single(status.Changes);
        Assert.Equal("src/GitApp.Core/Services/GitService.cs", change.Path);
        Assert.Equal(ChangeKind.Untracked, change.Unstaged);
        Assert.Equal("untracked", change.StateDescription);
    }

    [Fact]
    public void RenameConsumesItsOriginalPathWithoutShiftingLaterRecords()
    {
        // The regression this exists for. A type 2 record's path is followed
        // by the original path as a separate NUL-delimited field, so a naive
        // split treats that original path as the next record and every
        // subsequent row is attributed to the wrong file.
        var raw = Fields(
            "# branch.head main",
            "2 R. N... 100644 100644 100644 aaa111 aaa111 R100 src/New.cs",
            "src/Old.cs",
            "1 .M N... 100644 100644 100644 bbb222 bbb222 src/Untouched.cs");

        var status = GitService.ParseStatus(raw);

        Assert.Equal(2, status.Changes.Count);

        var renamed = status.Changes.Single(c => c.Path == "src/New.cs");
        Assert.Equal(ChangeKind.Renamed, renamed.Staged);
        Assert.Equal("src/Old.cs", renamed.OriginalPath);

        // The record after the rename must still be itself.
        var untouched = status.Changes.Single(c => c.Path == "src/Untouched.cs");
        Assert.Equal(ChangeKind.Modified, untouched.Unstaged);
    }

    [Fact]
    public void ReadsConflicts()
    {
        var raw = Fields(
            "# branch.head main",
            "u UU N... 100644 100644 100644 100644 aaa bbb ccc src/Conflicted.cs");

        var status = GitService.ParseStatus(raw);

        var change = Assert.Single(status.Changes);
        Assert.True(change.IsConflicted);
        Assert.Equal("conflicted", change.StateDescription);
        Assert.Single(status.Conflicted);
    }

    [Fact]
    public void ConflictsBlockCommitting()
    {
        // Committing with unresolved conflicts is a mistake the UI should
        // prevent rather than let git reject after the fact.
        var raw = Fields(
            "# branch.head main",
            "1 M. N... 100644 100644 100644 aaa aaa src/Staged.cs",
            "u UU N... 100644 100644 100644 100644 aaa bbb ccc src/Conflicted.cs");

        var status = GitService.ParseStatus(raw);

        Assert.NotEmpty(status.Staged);
        Assert.False(status.CanCommit);
    }

    [Fact]
    public void HandlesPathsContainingSpaces()
    {
        // Paths are the last field precisely so spaces need no quoting.
        var raw = Fields(
            "# branch.head main",
            "1 .M N... 100644 100644 100644 aaa aaa docs/my notes file.md");

        var status = GitService.ParseStatus(raw);

        Assert.Equal("docs/my notes file.md", Assert.Single(status.Changes).Path);
    }

    [Fact]
    public void HandlesEmptyOutputFromACleanRepository()
    {
        var status = GitService.ParseStatus(Fields("# branch.head main", "# branch.upstream origin/main", "# branch.ab +0 -0"));

        Assert.False(status.HasChanges);
        Assert.Equal("up to date", status.SyncDescription);
        Assert.Equal("no changes", status.ChangeSummary);
        Assert.False(status.CanCommit);
    }
}
