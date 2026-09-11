using GitApp.Domain;
using GitApp.Services;

namespace GitApp.Core.Tests;

/// <summary>
/// The inbox merge, held to ARCHITECTURE 4.6: a background poll reports only
/// what moved, so the view can leave untouched rows — and the focus on them —
/// alone. The read state is local and a poll must not clobber it.
/// </summary>
public class NotificationInboxTests
{
    private static GitHubNotification Note(
        string id, bool unread = true, DateTimeOffset? updatedAt = null) =>
        new(
            Id: id,
            RepositoryFullName: "G4p-Studios/GitApp",
            Title: $"Notification {id}",
            SubjectKind: GitHubSubjectKind.Issue,
            Reason: "subscribed",
            Unread: unread,
            UpdatedAt: updatedAt ?? DateTimeOffset.Parse("2026-09-11T10:00:00Z"));

    private static NotificationPage Page(params GitHubNotification[] items) =>
        new(items, "token", 60, NotModified: false);

    [Fact]
    public void TheFirstPollAddsEverything()
    {
        var inbox = new NotificationInbox();

        var change = inbox.Apply(Page(Note("1"), Note("2")));

        Assert.Equal(2, change.Added.Count);
        Assert.Empty(change.Updated);
        Assert.Empty(change.Removed);
        Assert.Equal(2, inbox.Items.Count);
    }

    [Fact]
    public void AnUnchangedPollReportsNothing()
    {
        var inbox = new NotificationInbox();
        inbox.Apply(Page(Note("1"), Note("2")));

        var change = inbox.Apply(Page(Note("1"), Note("2")));

        Assert.False(change.AnyChange);
        Assert.Equal(2, inbox.Items.Count);
    }

    [Fact]
    public void ANewThreadIsReportedAsAddedOnly()
    {
        var inbox = new NotificationInbox();
        inbox.Apply(Page(Note("1")));

        var change = inbox.Apply(Page(Note("2"), Note("1")));

        Assert.Equal("2", Assert.Single(change.Added).Id);
        Assert.Empty(change.Updated);
        Assert.Empty(change.Removed);
    }

    [Fact]
    public void AThreadThatDroppedOffIsReportedAsRemoved()
    {
        var inbox = new NotificationInbox();
        inbox.Apply(Page(Note("1"), Note("2")));

        var change = inbox.Apply(Page(Note("1")));

        Assert.Equal("2", Assert.Single(change.Removed));
        Assert.Single(inbox.Items);
    }

    [Fact]
    public void AThreadWhoseTimestampMovedIsReportedAsUpdated()
    {
        var inbox = new NotificationInbox();
        var when = DateTimeOffset.Parse("2026-09-11T10:00:00Z");
        inbox.Apply(Page(Note("1", updatedAt: when)));

        var change = inbox.Apply(Page(Note("1", updatedAt: when.AddMinutes(5))));

        Assert.Equal("1", Assert.Single(change.Updated).Id);
        Assert.Empty(change.Added);
        Assert.Empty(change.Removed);
    }

    [Fact]
    public void A304LeavesTheInboxAlone()
    {
        var inbox = new NotificationInbox();
        inbox.Apply(Page(Note("1"), Note("2")));

        var change = inbox.Apply(NotificationPage.Unchanged("token", 60));

        Assert.Same(InboxChange.None, change);
        Assert.Equal(2, inbox.Items.Count);
    }

    [Fact]
    public void ThePageOrderBecomesTheInboxOrder()
    {
        var inbox = new NotificationInbox();

        inbox.Apply(Page(Note("3"), Note("1"), Note("2")));

        Assert.Equal(new[] { "3", "1", "2" }, inbox.Items.Select(n => n.Id));
    }

    [Fact]
    public void MarkingReadDropsTheUnreadCountAndHoldsThePosition()
    {
        var inbox = new NotificationInbox();
        inbox.Apply(Page(Note("1"), Note("2"), Note("3")));

        var changed = inbox.MarkRead("2");

        Assert.True(changed);
        Assert.Equal(2, inbox.UnreadCount);
        Assert.False(inbox.Items[1].Unread);
        Assert.Equal(new[] { "1", "2", "3" }, inbox.Items.Select(n => n.Id));
    }

    [Fact]
    public void MarkingAnAlreadyReadThreadChangesNothing()
    {
        var inbox = new NotificationInbox();
        inbox.Apply(Page(Note("1", unread: false)));

        Assert.False(inbox.MarkRead("1"));
        Assert.False(inbox.MarkRead("nope"));
    }

    [Fact]
    public void TheHeadingCountsUnreadInWords()
    {
        var inbox = new NotificationInbox();
        Assert.Equal("No unread notifications", inbox.Heading);

        inbox.Apply(Page(Note("1")));
        Assert.Equal("1 unread notification", inbox.Heading);

        inbox.Apply(Page(Note("1"), Note("2")));
        Assert.Equal("2 unread notifications", inbox.Heading);
    }

    [Fact]
    public void ClearEmptiesTheInboxForSignOut()
    {
        var inbox = new NotificationInbox();
        inbox.Apply(Page(Note("1"), Note("2")));

        inbox.Clear();

        Assert.Empty(inbox.Items);
        Assert.Equal(0, inbox.UnreadCount);
    }
}
