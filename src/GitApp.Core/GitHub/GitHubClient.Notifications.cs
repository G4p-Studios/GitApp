using System.Net;
using System.Text.Json;
using GitApp.Domain;

namespace GitApp.GitHub;

/// <summary>
/// Notifications, milestone 5. REST, because the notifications endpoints have
/// no GraphQL equivalent (ARCHITECTURE 4.3, 4.4).
///
/// Polling done the way GitHub asks for it:
///
/// - Every request is conditional. When we have a <c>Last-Modified</c> from
///   the previous reply we send it back as <c>If-Modified-Since</c>, and a
///   304 answer — the usual answer — returns no body and does not count
///   against the rate limit. That is the difference between an app that can
///   poll every minute all day and one that burns its hourly budget by
///   lunch.
/// - The reply's <c>X-Poll-Interval</c> is GitHub's floor on how often this
///   app may ask again. It is carried out on the <see cref="NotificationPage"/>
///   so the caller's scheduler can honour it; see
///   <see cref="GitApp.Services.NotificationPoller"/>.
///
/// A 304 is a success here, not a failure, which is why this cannot reuse the
/// plain <c>SendAsync</c> path: that treats every non-2xx as an error to be
/// worded and announced, and "nothing changed" is neither.
/// </summary>
public sealed partial class GitHubClient
{
    /// <summary>GitHub's documented default when no X-Poll-Interval is sent.</summary>
    private const int DefaultPollIntervalSeconds = 60;

    /// <summary>
    /// Poll the notifications inbox.
    /// </summary>
    /// <param name="pollToken">
    /// The <see cref="NotificationPage.PollToken"/> from the previous poll, or
    /// null on the first. Echoed as <c>If-Modified-Since</c> so an unchanged
    /// inbox comes back as a free 304.
    /// </param>
    /// <param name="includeRead">
    /// False, the default, returns only unread threads — the inbox's real
    /// content. True asks for read ones too, for a "show everything" view.
    /// </param>
    public async Task<GitHubResult<NotificationPage>> GetNotificationsAsync(
        string? pollToken = null,
        bool includeRead = false,
        CancellationToken ct = default)
    {
        var url = $"{ApiRoot}/notifications?all={(includeRead ? "true" : "false")}";

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(pollToken))
            {
                // Round-trip GitHub's own Last-Modified string verbatim, so a
                // reformat on our side can never turn a would-be 304 into a
                // full 200 that spends rate limit for no new data.
                request.Headers.TryAddWithoutValidation("If-Modified-Since", pollToken);
            }

            response = await _http.SendAsync(request, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return GitHubResult<NotificationPage>.Fail(
                "GitHub did not reply in time. Check your connection and try again.",
                GitHubFailure.Offline);
        }
        catch (HttpRequestException)
        {
            return GitHubResult<NotificationPage>.Fail(
                "Could not reach GitHub. Check your internet connection.",
                GitHubFailure.Offline);
        }

        using (response)
        {
            var interval = ReadPollInterval(response);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                // The common case: nothing new, and the token stays as it was.
                return GitHubResult<NotificationPage>.Ok(
                    NotificationPage.Unchanged(pollToken, interval));
            }

            if (!response.IsSuccessStatusCode)
            {
                return GitHubResult<NotificationPage>.Fail(
                    await DescribeFailureAsync(response, ct), Classify(response));
            }

            var token = FirstHeader(response, "Last-Modified") ?? pollToken;

            try
            {
                var items = ParseNotifications(await response.Content.ReadAsStringAsync(ct));
                return GitHubResult<NotificationPage>.Ok(
                    new NotificationPage(items, token, interval, NotModified: false));
            }
            catch (JsonException)
            {
                return GitHubResult<NotificationPage>.Fail(
                    "GitHub sent a notifications list GitApp could not read.");
            }
        }
    }

    /// <summary>
    /// Mark one thread read, the way opening it on github.com does. GitHub
    /// answers 205 with no body, which the shared sender already counts as
    /// success.
    /// </summary>
    public async Task<GitHubResult<bool>> MarkThreadReadAsync(
        string threadId, CancellationToken ct = default)
    {
        var result = await SendAsync(
            HttpMethod.Patch, $"{ApiRoot}/notifications/threads/{threadId}", null, ct);

        return result.Success
            ? GitHubResult<bool>.Ok(true)
            : GitHubResult<bool>.Fail(result.Error!, result.Failure);
    }

    /// <summary>Turn the REST notifications array into ours. Public for tests.</summary>
    public static IReadOnlyList<GitHubNotification> ParseNotifications(string json)
    {
        var items = new List<GitHubNotification>();
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return items;
        }

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (ReadNotification(element) is { } notification)
            {
                items.Add(notification);
            }
        }

        return items;
    }

    internal static GitHubNotification? ReadNotification(JsonElement element)
    {
        var id = String(element, "id");
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        string title = string.Empty;
        string? url = null;
        var kind = GitHubSubjectKind.Other;
        if (element.TryGetProperty("subject", out var subject) && subject.ValueKind == JsonValueKind.Object)
        {
            title = String(subject, "title") ?? string.Empty;
            url = String(subject, "url");
            kind = SubjectKind(String(subject, "type"));
        }

        // A row with no title would be announced as just its reason and repo,
        // which reads as a glitch and cannot be acted on. Skip it, the same as
        // a nameless repository row.
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var repo = string.Empty;
        if (element.TryGetProperty("repository", out var repository) && repository.ValueKind == JsonValueKind.Object)
        {
            repo = String(repository, "full_name") ?? string.Empty;
        }

        return new GitHubNotification(
            Id: id,
            RepositoryFullName: repo,
            Title: title,
            SubjectKind: kind,
            Reason: String(element, "reason") ?? string.Empty,
            Unread: Bool(element, "unread"),
            UpdatedAt: Date(element, "updated_at") ?? DateTimeOffset.UtcNow,
            Url: url);
    }

    internal static GitHubSubjectKind SubjectKind(string? type) => type switch
    {
        "PullRequest" => GitHubSubjectKind.PullRequest,
        "Issue" => GitHubSubjectKind.Issue,
        "Release" => GitHubSubjectKind.Release,
        "Commit" => GitHubSubjectKind.Commit,
        "Discussion" => GitHubSubjectKind.Discussion,
        _ => GitHubSubjectKind.Other,
    };

    private static int ReadPollInterval(HttpResponseMessage response)
    {
        if (FirstHeader(response, "X-Poll-Interval") is { } value
            && int.TryParse(value, out var seconds)
            && seconds > 0)
        {
            return seconds;
        }

        return DefaultPollIntervalSeconds;
    }

    /// <summary>
    /// A header by name, looking on both the response and its content, because
    /// <c>Last-Modified</c> is a content header while <c>X-Poll-Interval</c> is
    /// a response header and a caller should not have to know which is which.
    /// </summary>
    private static string? FirstHeader(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
        {
            return values.FirstOrDefault();
        }

        if (response.Content.Headers.TryGetValues(name, out var contentValues))
        {
            return contentValues.FirstOrDefault();
        }

        return null;
    }
}
