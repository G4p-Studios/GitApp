using System.Text.Json;
using System.Net.Http.Headers;

namespace GitApp.GitHub;

/// <summary>
/// What the user has to do, and the code they have to type.
/// </summary>
public sealed record DeviceCode(
    string UserCode,
    string VerificationUri,
    string DeviceCodeValue,
    TimeSpan Interval,
    DateTimeOffset ExpiresAt)
{
    /// <summary>
    /// Spoken and shown.
    ///
    /// The code is spelled out with spaces between the characters, because
    /// a screen reader reads "WDJB-MJHT" as a word-like mumble and the user
    /// has to type it exactly. Spacing forces character-by-character
    /// reading without depending on a punctuation or verbosity setting we
    /// do not control.
    /// </summary>
    public string SpokenCode => string.Join(" ", UserCode.Replace("-", " - ").ToCharArray())
        .Replace("   ", " ");

    public string AccessibleName =>
        $"Go to {VerificationUri} and enter the code {SpokenCode}";
}

/// <summary>
/// GitHub's OAuth device flow.
///
/// No embedded browser, by design. An embedded web view is an accessibility
/// dead end: the screen reader has to cross a boundary between the app and
/// a browser engine, the keyboard model changes underneath the user, and
/// none of the naming in this app applies inside it. Device flow keeps the
/// browser the user already knows how to drive, with their own settings and
/// their own screen reader support, and it avoids shipping a client secret.
/// </summary>
public sealed class DeviceFlow : IDisposable
{
    private const string CodeUrl = "https://github.com/login/device/code";
    private const string TokenUrl = "https://github.com/login/oauth/access_token";

    /// <summary>
    /// What GitApp asks for. `repo` covers private repositories,
    /// `read:org` makes organization repositories visible, and `notifications`
    /// is milestone 5. Nothing broader: a token that can delete repositories
    /// is not needed to read them.
    /// </summary>
    public const string Scopes = "repo read:org notifications";

    private readonly HttpClient _http;
    private readonly string _clientId;

    public DeviceFlow(string clientId, HttpMessageHandler? handler = null)
    {
        _clientId = clientId;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GitApp", "0.1"));
    }

    public void Dispose() => _http.Dispose();

    public async Task<GitHubResult<DeviceCode>> StartAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_clientId))
        {
            return GitHubResult<DeviceCode>.Fail(
                "GitApp has no GitHub client ID configured, so browser sign-in is "
                + "unavailable. Use a personal access token instead.");
        }

        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["scope"] = Scopes,
        });

        try
        {
            using var response = await _http.PostAsync(CodeUrl, body, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (Text(root, "device_code") is not { Length: > 0 } device
                || Text(root, "user_code") is not { Length: > 0 } user)
            {
                return GitHubResult<DeviceCode>.Fail(
                    Text(root, "error_description")
                    ?? "GitHub would not start the sign-in. Try a personal access token instead.");
            }

            var interval = Number(root, "interval") is { } seconds && seconds > 0 ? seconds : 5;
            var expires = Number(root, "expires_in") is { } life && life > 0 ? life : 900;

            return GitHubResult<DeviceCode>.Ok(new DeviceCode(
                UserCode: user,
                VerificationUri: Text(root, "verification_uri") ?? "https://github.com/login/device",
                DeviceCodeValue: device,
                Interval: TimeSpan.FromSeconds(interval),
                ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(expires)));
        }
        catch (HttpRequestException)
        {
            return GitHubResult<DeviceCode>.Fail(
                "Could not reach GitHub. Check your internet connection.", GitHubFailure.Offline);
        }
        catch (JsonException)
        {
            return GitHubResult<DeviceCode>.Fail("GitHub sent a reply GitApp could not read.");
        }
    }

    /// <summary>
    /// Poll until the user finishes in the browser, or time runs out.
    ///
    /// <paramref name="onWaiting"/> fires once per poll so the caller can
    /// keep saying something. Several minutes of silence while a user works
    /// through a browser is indistinguishable from the app having given up.
    /// </summary>
    public async Task<GitHubResult<string>> WaitForTokenAsync(
        DeviceCode code,
        Action<TimeSpan>? onWaiting = null,
        CancellationToken ct = default)
    {
        var interval = code.Interval;

        while (DateTimeOffset.UtcNow < code.ExpiresAt)
        {
            ct.ThrowIfCancellationRequested();

            await Task.Delay(interval, ct);
            onWaiting?.Invoke(code.ExpiresAt - DateTimeOffset.UtcNow);

            var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["device_code"] = code.DeviceCodeValue,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            });

            string json;
            try
            {
                using var response = await _http.PostAsync(TokenUrl, body, ct);
                json = await response.Content.ReadAsStringAsync(ct);
            }
            catch (HttpRequestException)
            {
                // A blip mid-flow is not a failed sign-in. Keep polling;
                // the expiry is what ends this.
                continue;
            }

            GitHubResult<string>? outcome;
            try
            {
                outcome = Interpret(json, ref interval);
            }
            catch (JsonException)
            {
                continue;
            }

            if (outcome is not null)
            {
                return outcome;
            }
        }

        return GitHubResult<string>.Fail(
            "The sign-in code expired before it was used. Start again to get a new one.");
    }

    /// <summary>
    /// One poll response. Null means keep waiting.
    ///
    /// Separated out and internal so the state machine can be tested: the
    /// slow_down and authorization_pending branches are the ones that go
    /// wrong, and they are the hardest to provoke against the real service.
    /// </summary>
    internal static GitHubResult<string>? Interpret(string json, ref TimeSpan interval)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (Text(root, "access_token") is { Length: > 0 } token)
        {
            return GitHubResult<string>.Ok(token);
        }

        switch (Text(root, "error"))
        {
            case "authorization_pending":
                return null;

            case "slow_down":
                // GitHub tells us the new floor; obey it or get throttled.
                interval = Number(root, "interval") is { } seconds && seconds > 0
                    ? TimeSpan.FromSeconds(seconds)
                    : interval + TimeSpan.FromSeconds(5);
                return null;

            case "expired_token":
                return GitHubResult<string>.Fail(
                    "The sign-in code expired before it was used. Start again to get a new one.");

            case "access_denied":
                return GitHubResult<string>.Fail("Sign-in was cancelled in the browser.");

            case { } other:
                return GitHubResult<string>.Fail(
                    Text(root, "error_description") ?? $"GitHub refused the sign-in ({other}).");

            default:
                return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;
}
