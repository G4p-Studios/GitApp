using GitApp.Domain;

namespace GitApp.Core.Tests;

public class ReadmeParserTests
{
    [Fact]
    public void HeadingsCarryALevelSoAScreenReaderCanJump()
    {
        var blocks = ReadmeDocument.Parse("# GitApp\n\n## Why\n\nBecause.");

        Assert.Equal(ReadmeBlockKind.Heading, blocks[0].Kind);
        Assert.Equal(1, blocks[0].Level);
        Assert.Equal("GitApp", blocks[0].Text);
        Assert.Equal(2, blocks[1].Level);
        Assert.Equal("Why", blocks[1].Text);
        Assert.Equal(ReadmeBlockKind.Paragraph, blocks[2].Kind);
    }

    [Fact]
    public void AFencedCodeBlockIsOneBlockNotOneElementPerCharacter()
    {
        var blocks = ReadmeDocument.Parse("""
            Intro.

            ```csharp
            var x = 1;
            var y = 2;
            ```
            """);

        var code = blocks.Single(b => b.Kind == ReadmeBlockKind.Code);
        Assert.Equal("csharp", code.Language);
        Assert.Contains("var x = 1;", code.Text);
        Assert.StartsWith("Code block, csharp, 2 lines.", code.AccessibleName);
        Assert.Contains("var y = 2;", code.AccessibleName);
    }

    [Fact]
    public void LinksAreLiftedOutSoTheyCanBeRealControls()
    {
        var blocks = ReadmeDocument.Parse("See the [contributing guide](https://example.com/contributing) for details.");

        var paragraph = Assert.Single(blocks);
        Assert.Equal("See the contributing guide for details.", paragraph.Text);
        var link = Assert.Single(paragraph.Links);
        Assert.Equal("contributing guide", link.Text);
        Assert.Equal("https://example.com/contributing", link.Url);
        Assert.Equal("contributing guide, link", link.AccessibleName);
    }

    [Fact]
    public void EmphasisMarkersAreStrippedRatherThanReadAsPunctuation()
    {
        var blocks = ReadmeDocument.Parse("This is **important** and _useful_.");

        Assert.Equal("This is important and useful.", Assert.Single(blocks).Text);
    }

    [Fact]
    public void AListStaysOneBlockWithOneItemPerLine()
    {
        var blocks = ReadmeDocument.Parse("""
            - First
            - Second
            1. Third
            """);

        var list = Assert.Single(blocks);
        Assert.Equal(ReadmeBlockKind.List, list.Kind);
        Assert.Equal("First\nSecond\nThird", list.Text);
    }

    [Fact]
    public void AnEmptyOrMissingReadmeIsAnEmptyDocumentNotAFakeHeading()
    {
        Assert.Empty(ReadmeDocument.Parse(null));
        Assert.Empty(ReadmeDocument.Parse("   "));
    }

    [Fact]
    public void AnUnclosedFenceDoesNotThrow()
    {
        var blocks = ReadmeDocument.Parse("```\nstill going");

        var code = Assert.Single(blocks);
        Assert.Equal("still going", code.Text);
    }
}
