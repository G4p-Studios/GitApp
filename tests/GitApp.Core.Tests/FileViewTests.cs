using System.Net;
using System.Text;
using GitApp.Domain;
using GitApp.GitHub;

namespace GitApp.Core.Tests;

public class FileViewTests
{
    private const string TextBlob = """
        {
          "data": {
            "repository": {
              "object": {
                "byteSize": 27,
                "isBinary": false,
                "text": "hello\nworld\n"
              }
            }
          }
        }
        """;

    [Fact]
    public void LineAnnouncementIsContentFirst()
    {
        Assert.Equal("hello, line 1", new FileLine(1, "hello").AccessibleName);
        Assert.Equal("blank, line 7", new FileLine(7, "").AccessibleName);
        Assert.Equal("blank, line 2", new FileLine(2, "   ").AccessibleName);
    }

    [Fact]
    public void SplitLinesDoesNotInventARowForATrailingNewline()
    {
        var lines = GitHubFile.SplitLines("hello\nworld\n");
        Assert.Equal(2, lines.Count);
        Assert.Equal("hello, line 1", lines[0].AccessibleName);
        Assert.Equal("world, line 2", lines[1].AccessibleName);
    }

    [Fact]
    public void SplitLinesKeepsAWindowsNewlineAsOneBreak()
    {
        var lines = GitHubFile.SplitLines("a\r\nb\r\n");
        Assert.Equal(2, lines.Count);
        Assert.Equal("a", lines[0].Content);
        Assert.Equal("b", lines[1].Content);
    }

    [Fact]
    public void EmptyTextIsNoLines()
    {
        Assert.Empty(GitHubFile.SplitLines(""));
        Assert.Empty(GitHubFile.SplitLines(null));
    }

    [Fact]
    public void AFileThatIsOnlyANewlineIsOneBlankLine()
    {
        var lines = GitHubFile.SplitLines("\n");
        Assert.Single(lines);
        Assert.Equal("blank, line 1", lines[0].AccessibleName);
    }

    [Theory]
    [InlineData("README.md", true)]
    [InlineData("notes.markdown", true)]
    [InlineData("notes.MDOWN", true)]
    [InlineData("README", false)]
    [InlineData("Program.cs", false)]
    [InlineData("image.png", false)]
    public void MarkdownIsRecognisedByExtension(string name, bool expected) =>
        Assert.Equal(expected, GitHubFile.IsMarkdownName(name));

    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1, "1 byte")]
    [InlineData(18, "18 bytes")]
    [InlineData(999, "999 bytes")]
    [InlineData(1000, "1 kilobyte")]
    [InlineData(1500, "2 kilobytes")]
    [InlineData(18000, "18 kilobytes")]
    [InlineData(1_000_000, "1 megabyte")]
    [InlineData(2_400_000, "2 megabytes")]
    public void SizeIsSpokenInWords(int bytes, string expected) =>
        Assert.Equal(expected, GitHubFile.FormatSize(bytes));

    [Fact]
    public void ParseFileReadsTextAndCountsLines()
    {
        var file = GitHubClient.ParseFile(
            TextBlob, "src/Hello.cs", "main", "https://github.com/G4p-Studios/GitApp");

        Assert.NotNull(file);
        Assert.Equal("Hello.cs", file!.Name);
        Assert.Equal("src/Hello.cs", file.Path);
        Assert.Equal(27, file.ByteSize);
        Assert.False(file.IsBinary);
        Assert.False(file.TooLarge);
        Assert.Equal(2, file.Lines.Count);
        Assert.Equal("Hello.cs, 2 lines, 27 bytes", file.Summary);
        Assert.Equal(
            "https://github.com/G4p-Studios/GitApp/blob/main/src/Hello.cs",
            file.HtmlUrl);
        Assert.Contains("on main", file.AboutFacts);
        Assert.Contains("2 lines", file.AboutFacts);
        Assert.True(file.ShowLines);
        Assert.False(file.ShowDocument);
    }

    [Fact]
    public void ParseFileTreatsANullTextBlobAsTooLarge()
    {
        var json = """
            {
              "data": {
                "repository": {
                  "object": {
                    "byteSize": 2000000,
                    "isBinary": false,
                    "text": null
                  }
                }
              }
            }
            """;

        var file = GitHubClient.ParseFile(json, "huge.sql", "main", "https://github.com/a/b");
        Assert.NotNull(file);
        Assert.True(file!.TooLarge);
        Assert.Equal("huge.sql, too large to show here, 2 megabytes", file.Summary);
        Assert.Equal("This file is too large to show here.", file.UnavailableMessage);
        Assert.True(file.ShowUnavailable);
        Assert.False(file.ShowLines);
    }

    [Fact]
    public void ParseFileTreatsABinaryBlobAsNotText()
    {
        var json = """
            {
              "data": {
                "repository": {
                  "object": {
                    "byteSize": 24000,
                    "isBinary": true,
                    "text": null
                  }
                }
              }
            }
            """;

        var file = GitHubClient.ParseFile(json, "logo.png", "main", "https://github.com/a/b");
        Assert.NotNull(file);
        Assert.True(file!.IsBinary);
        Assert.False(file.TooLarge);
        Assert.Equal("logo.png, not text, 24 kilobytes", file.Summary);
        Assert.Equal("This file is not text.", file.UnavailableMessage);
    }

    [Fact]
    public void ParseFileRendersMarkdownByExtension()
    {
        var json = """
            {
              "data": {
                "repository": {
                  "object": {
                    "byteSize": 12,
                    "isBinary": false,
                    "text": "# Hello\n"
                  }
                }
              }
            }
            """;

        var file = GitHubClient.ParseFile(json, "docs/README.md", "main", "https://github.com/a/b");
        Assert.NotNull(file);
        Assert.True(file!.IsMarkdown);
        Assert.True(file.ShowDocument);
        Assert.False(file.ShowLines);
        Assert.Equal("README.md, rendered as markdown, 12 bytes", file.Summary);
        Assert.Contains("rendered as markdown", file.AboutFacts);
    }

    [Fact]
    public void ParseFileDoesNotTreatAFolderAsATooLargeFile()
    {
        var json = """
            {
              "data": {
                "repository": {
                  "object": {}
                }
              }
            }
            """;

        Assert.Null(GitHubClient.ParseFile(json, "src", "main", "https://github.com/a/b"));
    }

    [Fact]
    public void ParseFileReturnsNullWhenTheBlobIsMissing()
    {
        var json = """
            {
              "data": {
                "repository": {
                  "object": null
                }
              }
            }
            """;

        Assert.Null(GitHubClient.ParseFile(json, "gone.cs", "main", "https://github.com/a/b"));
    }

    [Fact]
    public void LastTouchJoinsTheAboutFacts()
    {
        var file = GitHubClient.ParseFile(
            TextBlob, "Hello.cs", "main", "https://github.com/a/b")!
            with
            {
                LastSubject = "Add Hello",
                LastTouched = DateTimeOffset.Parse("2026-09-11T12:00:00Z"),
            };

        Assert.Contains("Add Hello", file.AboutFacts);
    }

    [Fact]
    public void FileExpressionIsBranchColonPath() =>
        Assert.Equal("main:src/Hello.cs", GitHubClient.FileExpression("main", "src/Hello.cs"));

    [Fact]
    public async Task GetFileAsyncParsesABlob()
    {
        var handler = new ScriptedHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().Result;
            Assert.Contains("FileBlob", body, StringComparison.Ordinal);
            Assert.Contains("main:src/Hello.cs", body, StringComparison.Ordinal);
            return Json(HttpStatusCode.OK, TextBlob);
        });

        using var client = new GitHubClient("t", handler);
        var result = await client.GetFileAsync(
            "G4p-Studios", "GitApp", "main", "src/Hello.cs",
            "https://github.com/G4p-Studios/GitApp");

        Assert.True(result.Success);
        Assert.Equal("Hello.cs", result.Value!.Name);
        Assert.Equal(2, result.Value.Lines.Count);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
            _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }
}
