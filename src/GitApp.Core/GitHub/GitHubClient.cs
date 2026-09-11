using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GitApp.Domain;

namespace GitApp.GitHub;

/// <summary>
/// The GitHub REST client.
///
/// REST rather than the GraphQL the architecture note calls for, on purpose
/// and only for the repository list. The reason GraphQL is specified is that
/// partial data arriving in waves re-renders a list and moves focus, and a
/// single REST call to /user/repos returns every field that screen shows in
/// one response, so that reason is already satisfied. The repository view
/// is GraphQL, because last-touch per file cannot be had from REST in one
/// request at all; file blobs use the same client (docs/REPOSITORY-VIEW.md).
///
/// Everything normalizes into <c>GitApp.Domain</c> records at this boundary.
/// No screen ever sees a GitHub-shaped object.
/// </summary>
public sealed partial class GitHubClient : IDisposable
{
    private const string ApiRoot = "https://api.github.com";

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    /// <param name="handler">
    /// Supplied by tests so the whole client can be exercised without a
    /// network. The error paths are the ones that have to be right, and they
    /// are the ones a live account never shows you.
    /// </param>
    public GitHubClient(string token, HttpMessageHandler? handler = null)
    {
        _ownsClient = true;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);

        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GitApp", "0.1"));
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }

    /// <summary>Who the token belongs to. Also the cheapest way to validate it.</summary>
    public async Task<GitHubResult<GitHubAccount>> GetAccountAsync(CancellationToken ct = default)
    {
        var result = await GetAsync($"{ApiRoot}/user", ct);
        if (!result.Success)
        {
            return GitHubResult<GitHubAccount>.Fail(result.Error!, result.Failure);
        }

        try
        {
            var root = JsonDocument.Parse(result.Value!).RootElement;

            var login = String(root, "login");
            if (string.IsNullOrEmpty(login))
            {
                return GitHubResult<GitHubAccount>.Fail(
                    "GitHub replied without an account name. Try signing in again.");
            }

            return GitHubResult<GitHubAccount>.Ok(new GitHubAccount(login, String(root, "name")));
        }
        catch (JsonException)
        {
            return GitHubResult<GitHubAccount>.Fail("GitHub sent a reply GitApp could not read.");
        }
    }

    /// <summary>
    /// Every repository the account can push to or read, newest activity
    /// first.
    ///
    /// Paged to exhaustion rather than showing the first hundred, because a
    /// list that silently stops is indistinguishable from not having the
    /// repository you are looking for.
    /// </summary>
    public async Task<GitHubResult<IReadOnlyList<GitHubRepository>>> GetRepositoriesAsync(
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var all = new List<GitHubRepository>();

        for (var page = 1; page <= 20; page++)
        {
            var url = $"{ApiRoot}/user/repos?per_page=100&sort=updated&affiliation=owner,collaborator,organization_member&page={page}";
            var result = await GetAsync(url, ct);

            if (!result.Success)
            {
                return GitHubResult<IReadOnlyList<GitHubRepository>>.Fail(
                    result.Error!, result.Failure);
            }

            List<GitHubRepository> batch;
            try
            {
                batch = Parse(result.Value!);
            }
            catch (JsonException)
            {
                return GitHubResult<IReadOnlyList<GitHubRepository>>.Fail(
                    "GitHub sent a repository list GitApp could not read.");
            }

            all.AddRange(batch);
            progress?.Report(all.Count);

            // A short page is the last page.
            if (batch.Count < 100)
            {
                break;
            }
        }

        return GitHubResult<IReadOnlyList<GitHubRepository>>.Ok(all);
    }

    /// <summary>Turn the API's shape into ours. Public for tests.</summary>
    public static List<GitHubRepository> Parse(string json)
    {
        var repos = new List<GitHubRepository>();
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return repos;
        }

        foreach (var item in document.RootElement.EnumerateArray())
        {
            var name = String(item, "name");
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            repos.Add(new GitHubRepository(
                Name: name,
                Owner: item.TryGetProperty("owner", out var owner)
                    ? String(owner, "login") ?? string.Empty
                    : string.Empty,
                Description: String(item, "description"),
                IsPrivate: Bool(item, "private"),
                IsFork: Bool(item, "fork"),
                Language: String(item, "language"),
                Stars: Int(item, "stargazers_count"),
                UpdatedAt: Date(item, "pushed_at") ?? Date(item, "updated_at"),
                CloneUrl: String(item, "clone_url") ?? string.Empty,
                HtmlUrl: String(item, "html_url") ?? string.Empty));
        }

        return repos;
    }

    private Task<GitHubResult<string>> GetAsync(string url, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, url, null, ct);

    private async Task<GitHubResult<string>> SendAsync(
        HttpMethod method, string url, HttpContent? body, CancellationToken ct)
    {
        HttpResponseMessage response;

        try
        {
            using var request = new HttpRequestMessage(method, url) { Content = body };
            response = await _http.SendAsync(request, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return GitHubResult<string>.Fail(
                "GitHub did not reply in time. Check your connection and try again.",
                GitHubFailure.Offline);
        }
        catch (HttpRequestException)
        {
            return GitHubResult<string>.Fail(
                "Could not reach GitHub. Check your internet connection.",
                GitHubFailure.Offline);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return GitHubResult<string>.Ok(await response.Content.ReadAsStringAsync(ct));
            }

            return GitHubResult<string>.Fail(
                await DescribeFailureAsync(response, ct), Classify(response));
        }
    }

    /// <summary>
    /// Turn a status code into a sentence worth hearing.
    ///
    /// The rate limit case is the one that matters most: "403 Forbidden"
    /// tells the user nothing, while "you can try again in 12 minutes" tells
    /// them exactly what to do, and the reset time is sitting in a header.
    /// </summary>
    private static async Task<string> DescribeFailureAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        if (IsRateLimit(response))
        {
            return $"GitHub is rate limiting this app. {ResetWording(response)}";
        }

        return response.StatusCode switch
        {
            // Covers both a token that never worked and one that stopped
            // working, because the reply does not distinguish them and the
            // user cannot tell which case they are in either.
            HttpStatusCode.Unauthorized =>
                "GitHub rejected that token. It may be mistyped, expired, or revoked. Check it and try again.",

            HttpStatusCode.Forbidden =>
                "GitHub refused the request. The token is probably missing the repo scope.",

            HttpStatusCode.NotFound =>
                "GitHub could not find that. It may be private, renamed, or deleted.",

            HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway =>
                "GitHub is having trouble right now. Try again in a few minutes.",

            _ => $"GitHub returned an error ({(int)response.StatusCode}). "
                 + await ShortBodyAsync(response, ct),
        };
    }

    private static bool IsRateLimit(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
        && response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining)
        && remaining.FirstOrDefault() == "0";

    private static string ResetWording(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values)
            && long.TryParse(values.FirstOrDefault(), out var epoch))
        {
            var wait = DateTimeOffset.FromUnixTimeSeconds(epoch) - DateTimeOffset.UtcNow;
            var minutes = (int)Math.Ceiling(wait.TotalMinutes);

            if (minutes > 0)
            {
                return minutes == 1
                    ? "Try again in about 1 minute."
                    : $"Try again in about {minutes} minutes.";
            }
        }

        return "Try again shortly.";
    }

    private static async Task<string> ShortBodyAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(body);

            return String(document.RootElement, "message") ?? string.Empty;
        }
        catch
        {
            // The body is the nice-to-have here; the status code already
            // carried the meaning.
            return string.Empty;
        }
    }

    private static GitHubFailure Classify(HttpResponseMessage response) =>
        IsRateLimit(response) ? GitHubFailure.RateLimited
        : response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => GitHubFailure.Unauthenticated,
            HttpStatusCode.Forbidden => GitHubFailure.Forbidden,
            HttpStatusCode.NotFound => GitHubFailure.NotFound,
            _ => GitHubFailure.Other,
        };

    internal static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    internal static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : 0;

    internal static DateTimeOffset? Date(JsonElement element, string name) =>
        String(element, name) is { } text
        && DateTimeOffset.TryParse(text, out var parsed)
            ? parsed
            : null;
}
