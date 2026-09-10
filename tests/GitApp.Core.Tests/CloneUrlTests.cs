using GitApp.Services;

namespace GitApp.Core.Tests;

/// <summary>
/// Working out the folder name a clone will create.
///
/// Getting this wrong is worse than it sounds: the app tells the user where
/// the repository will land before cloning, and then adds that path to the
/// repository list afterwards. A mismatch means a successful clone the app
/// cannot find.
/// </summary>
public class CloneUrlTests
{
    [Theory]
    [InlineData("https://github.com/masonasons/fastgh", "fastgh")]
    [InlineData("https://github.com/masonasons/fastgh.git", "fastgh")]
    [InlineData("https://github.com/masonasons/fastgh/", "fastgh")]
    [InlineData("git@github.com:microsoft/react-native-windows.git", "react-native-windows")]
    [InlineData("ssh://git@codeberg.org/user/my-repo.git", "my-repo")]
    [InlineData("https://gitlab.com/group/subgroup/project.git", "project")]
    [InlineData("  https://github.com/user/spaced.git  ", "spaced")]
    public void DerivesTheFolderNameGitWouldUse(string url, string expected) =>
        Assert.Equal(expected, GitService.RepositoryNameFromUrl(url));

    [Fact]
    public void KeepsDotsInsideTheName()
    {
        // Only a trailing ".git" is a suffix to strip. A repository genuinely
        // called "docs.github.com" must survive intact.
        Assert.Equal("docs.github.com", GitService.RepositoryNameFromUrl("https://github.com/github/docs.github.com"));
        Assert.Equal("docs.github.com", GitService.RepositoryNameFromUrl("https://github.com/github/docs.github.com.git"));
    }

    [Fact]
    public void FallsBackRatherThanReturningNothing()
    {
        // An empty name would build a path pointing at the parent directory
        // and clone into the wrong place.
        Assert.Equal("repository", GitService.RepositoryNameFromUrl("   "));
        Assert.Equal("repository", GitService.RepositoryNameFromUrl("https://github.com/"));
    }
}
