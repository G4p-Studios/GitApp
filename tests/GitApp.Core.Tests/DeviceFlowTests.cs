using GitApp.Domain;
using GitApp.GitHub;

namespace GitApp.Core.Tests;

/// <summary>
/// The polling state machine, which is the part that cannot be provoked
/// reliably against the real service.
/// </summary>
public class DeviceFlowTests
{
    private static GitHubResult<string>? Interpret(string json, ref TimeSpan interval) =>
        DeviceFlow.Interpret(json, ref interval);

    [Fact]
    public void PendingMeansKeepWaiting()
    {
        var interval = TimeSpan.FromSeconds(5);

        Assert.Null(Interpret("""{"error":"authorization_pending"}""", ref interval));
        Assert.Equal(TimeSpan.FromSeconds(5), interval);
    }

    [Fact]
    public void SlowDownRaisesTheIntervalToWhatGitHubAsksFor()
    {
        var interval = TimeSpan.FromSeconds(5);

        Assert.Null(Interpret("""{"error":"slow_down","interval":10}""", ref interval));
        Assert.Equal(TimeSpan.FromSeconds(10), interval);
    }

    [Fact]
    public void SlowDownWithoutANumberStillBacksOff()
    {
        // Polling on regardless is how an app gets its client ID throttled.
        var interval = TimeSpan.FromSeconds(5);

        Assert.Null(Interpret("""{"error":"slow_down"}""", ref interval));
        Assert.True(interval > TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ATokenEndsThePoll()
    {
        var interval = TimeSpan.FromSeconds(5);

        var result = Interpret("""{"access_token":"ghu_abc","token_type":"bearer"}""", ref interval);

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("ghu_abc", result.Value);
    }

    [Fact]
    public void CancellingInTheBrowserSaysThatRatherThanTimingOut()
    {
        var interval = TimeSpan.FromSeconds(5);

        var result = Interpret("""{"error":"access_denied"}""", ref interval);

        Assert.False(result!.Success);
        Assert.Contains("cancelled in the browser", result.Error!);
    }

    [Fact]
    public void AnExpiredCodeSaysToStartAgain()
    {
        var interval = TimeSpan.FromSeconds(5);

        var result = Interpret("""{"error":"expired_token"}""", ref interval);

        Assert.Contains("Start again", result!.Error!);
    }

    [Fact]
    public void AnUnknownErrorPrefersGitHubsOwnDescription()
    {
        var interval = TimeSpan.FromSeconds(5);

        var result = Interpret(
            """{"error":"unsupported_grant_type","error_description":"Grant type is not supported"}""",
            ref interval);

        Assert.Equal("Grant type is not supported", result!.Error);
    }

    [Fact]
    public async Task WithoutAClientIdItSaysToUseATokenInstead()
    {
        using var flow = new DeviceFlow(string.Empty);

        var result = await flow.StartAsync();

        Assert.False(result.Success);
        Assert.Contains("personal access token", result.Error!);
    }

    [Fact]
    public void TheUserCodeIsSpacedSoItIsReadCharacterByCharacter()
    {
        var code = new DeviceCode("WDJB-MJHT", "https://github.com/login/device",
            "dev", TimeSpan.FromSeconds(5), DateTimeOffset.UtcNow.AddMinutes(15));

        // Read as a word, "WDJB" is a mumble, and the user has to type it
        // exactly. Spacing forces character reading without depending on a
        // screen reader setting this app does not control.
        Assert.Equal("W D J B - M J H T", code.SpokenCode);
        Assert.Contains("W D J B - M J H T", code.AccessibleName);
    }
}

public class RelativeTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(30, "just now")]
    [InlineData(60, "1 minute ago")]
    [InlineData(60 * 5, "5 minutes ago")]
    [InlineData(3600, "1 hour ago")]
    [InlineData(3600 * 5, "5 hours ago")]
    [InlineData(86400 * 1, "24 hours ago")]
    [InlineData(86400 * 2, "2 days ago")]
    [InlineData(86400 * 3, "3 days ago")]
    [InlineData(86400 * 90, "3 months ago")]
    [InlineData(86400 * 400, "1 year ago")]
    public void ReadsTheWayGitAndGitHubBothSayIt(int secondsAgo, string expected) =>
        Assert.Equal(expected, RelativeTime.From(Now.AddSeconds(-secondsAgo), Now));

    [Fact]
    public void AFutureTimestampFromClockSkewDoesNotSayInMinusThree()
    {
        Assert.Equal("just now", RelativeTime.From(Now.AddMinutes(5), Now));
    }
}
