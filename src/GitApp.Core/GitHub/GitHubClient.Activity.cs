using System.Text.Json;
using GitApp.Domain;

namespace GitApp.GitHub;

/// <summary>
/// The Home feed and the short "top repositories" list. REST, because the
/// events endpoints have no GraphQL equivalent that returns this in one
/// shot, and a single page of received events is the dashboard github.com
/// shows (docs/HOME.md).
/// </summary>
public sealed partial class GitHubClient
{
    /// <summary>
    /// Events from people you follow and repositories you watch, newest
    /// first. This is github.com's Home feed. GitHub keeps about thirty days.
    /// </summary>
    public async Task<GitHubResult<IReadOnlyList<ActivityEvent>>> GetReceivedEventsAsync(
        string login,
        int perPage = 30,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(login))
        {
            return GitHubResult<IReadOnlyList<ActivityEvent>>.Fail(
                "Cannot load the feed without an account name.");
        }

        var count = Math.Clamp(perPage, 1, 100);
        var url = $"{ApiRoot}/users/{Uri.EscapeDataString(login)}/received_events?per_page={count}";
        var result = await GetAsync(url, ct);
        if (!result.Success)
        {
            return GitHubResult<IReadOnlyList<ActivityEvent>>.Fail(
                result.Error!, result.Failure);
        }

        try
        {
            return GitHubResult<IReadOnlyList<ActivityEvent>>.Ok(ParseEvents(result.Value!));
        }
        catch (JsonException)
        {
            return GitHubResult<IReadOnlyList<ActivityEvent>>.Fail(
                "GitHub sent a feed GitApp could not read.");
        }
    }

    /// <summary>
    /// A handful of repositories, most recently pushed, for the Home sidebar.
    /// One page only: this is a jump list, not the full catalogue.
    /// </summary>
    public async Task<GitHubResult<IReadOnlyList<GitHubRepository>>> GetRecentRepositoriesAsync(
        int count = 7,
        CancellationToken ct = default)
    {
        var perPage = Math.Clamp(count, 1, 100);
        var url = $"{ApiRoot}/user/repos?per_page={perPage}&sort=pushed&affiliation=owner,collaborator,organization_member&page=1";
        var result = await GetAsync(url, ct);
        if (!result.Success)
        {
            return GitHubResult<IReadOnlyList<GitHubRepository>>.Fail(
                result.Error!, result.Failure);
        }

        try
        {
            return GitHubResult<IReadOnlyList<GitHubRepository>>.Ok(Parse(result.Value!));
        }
        catch (JsonException)
        {
            return GitHubResult<IReadOnlyList<GitHubRepository>>.Fail(
                "GitHub sent a repository list GitApp could not read.");
        }
    }

    internal static IReadOnlyList<ActivityEvent> ParseEvents(string json)
    {
        using var document = JsonDocument.Parse(json);
        var events = new List<ActivityEvent>();

        foreach (var item in document.RootElement.EnumerateArray())
        {
            var id = String(item, "id");
            var type = String(item, "type");
            var actor = item.TryGetProperty("actor", out var actorEl)
                ? String(actorEl, "login")
                : null;
            var repo = item.TryGetProperty("repo", out var repoEl)
                ? String(repoEl, "name")
                : null;

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(type)
                || string.IsNullOrEmpty(actor))
            {
                continue;
            }

            string? action = null;
            string? refType = null;
            string? gitRef = null;
            string? title = null;
            int? number = null;

            if (item.TryGetProperty("payload", out var payload)
                && payload.ValueKind == JsonValueKind.Object)
            {
                action = String(payload, "action");
                refType = String(payload, "ref_type");
                gitRef = String(payload, "ref");

                if (payload.TryGetProperty("issue", out var issue))
                {
                    title = String(issue, "title");
                    number = Int(issue, "number") is var n and > 0 ? n : null;
                }
                else if (payload.TryGetProperty("pull_request", out var pull))
                {
                    title = String(pull, "title");
                    number = Int(pull, "number") is var pn and > 0 ? pn : null;
                }
                else if (payload.TryGetProperty("release", out var release))
                {
                    title = String(release, "name") ?? String(release, "tag_name");
                }
            }

            var created = Date(item, "created_at") ?? DateTimeOffset.MinValue;
            var summary = ActivityEvent.Sentence(
                type, actor, repo, action, refType, gitRef, title, number);
            var html = string.IsNullOrEmpty(repo)
                ? $"https://github.com/{actor}"
                : $"https://github.com/{repo}";

            events.Add(new ActivityEvent(id, type, actor, repo, summary, created, html));
        }

        return events;
    }
}
