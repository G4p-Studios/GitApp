# Notifications

Status: service layer built and unit-tested, 2026-09-11. Milestone 5, first
slice. The Windows toast and the in-app inbox screen are not built yet; this
document covers the polling and model layer they will sit on, which lives in
`GitApp.Core` and is testable without a window.

## What is built

- `GitHubClient.GetNotificationsAsync` — a conditional poll of
  `GET /notifications` that returns a `NotificationPage`.
- `GitHubClient.MarkThreadReadAsync` — mark one thread read, as opening it on
  github.com does.
- `NotificationPoller` (`Services/`) — when to poll next, and which
  notifications are new enough to announce.
- `GitHubNotification` and `NotificationPage` (`Domain/`) — the row and the
  poll result, each owning its own reading string.

## Polling done the way GitHub asks

ARCHITECTURE 4.4 sets the rules; this is how they land in code.

**Every request is conditional.** The first poll sends nothing; every reply
carries a `Last-Modified`, and the next poll sends it straight back as
`If-Modified-Since`. The usual answer is `304 Not Modified` with no body, and a
304 does not count against the hourly rate limit. That is the whole game: an
app that polls unconditionally every minute spends its budget by mid-morning,
while one that polls conditionally can watch all day.

The token is GitHub's own `Last-Modified` string, round-tripped verbatim
through `TryAddWithoutValidation` rather than parsed to a `DateTimeOffset` and
reformatted. A reformat that changed one character — a `GMT` to `+00:00` — would
turn a would-be 304 into a full 200 that spends rate limit for no new data, and
nothing on screen would show that it happened.

**A 304 is a success, not an error.** This is why the notifications poll cannot
reuse the plain `SendAsync` path the rest of the client uses: that path treats
every non-2xx as a failure to be worded and announced, and "nothing changed" is
neither. `GetNotificationsAsync` handles the 304 itself and returns a
`NotificationPage` with `NotModified` set and the token unchanged.

**`X-Poll-Interval` is a floor, not a suggestion.** GitHub says the minimum
seconds before the next poll; asking sooner earns a *secondary* rate limit,
which is worse than the primary one because leaning on it extends it. The
interval is carried out on `NotificationPage.PollIntervalSeconds`, and
`NotificationPoller` never schedules the next poll sooner than the larger of
that and this app's own floor.

## What counts as new

Polling returns the same unread threads every time until they are read. If the
inbox raised a toast for everything each reply carried, it would announce the
whole inbox on a timer. So `NotificationPoller` remembers the id and the
`updated_at` of every thread it has seen, and treats a thread as worth
announcing only when it is **new or newly changed, and still unread**:

- a thread whose id it has not seen — a genuinely new notification;
- a thread it has seen whose `updated_at` moved forward — a new comment or push
  on a conversation already in the inbox;
- and in both cases, only while `unread` is true.

A thread that arrives already read, or comes back unchanged on the next poll,
raises nothing. This is verified in `NotificationTests`.

## Backoff

A failed poll does not just retry on the same beat.

- A **rate limit** doubles the wait on each consecutive hit, capped at an hour.
  Backing off is the only thing that clears a secondary limit; retrying on
  schedule prolongs it.
- **Offline or a 5xx** waits the normal floor and tries again. Being offline is
  not the app's fault and does not deserve an ever-widening gap; the next poll
  will either reconnect or not.

A successful poll resets the ramp, so one rate limit does not haunt the rest of
the session.

## The row, and why unread is not in its name

`GitHubNotification.AccessibleName` leads with the title, then why it arrived,
then what it is, then where, then when:

```
Fix the parser crash, review requested, pull request, in G4p-Studios/GitApp, 2 hours ago
```

Title first because it is what distinguishes one row from the next; leading with
the reason or the repository would open every row in the same repository with
the same words, and a listener would wait through them on every line
(ARCHITECTURE 3.3).

Whether the thread is unread is deliberately **not** in that string. Unread
flips to read the instant the user opens the thread, and 3.3 is explicit that
state which ticks underneath a row must not live in the row's own name, or the
screen reader re-reads the entire row every time it changes. It rides a
separate, individually named element instead, exposed as `UnreadIndicator`.

The reason is spoken as words, never as GitHub's token: `review_requested`
becomes "review requested", `state_change` becomes "closed or reopened". A
screen reader reads `review_requested` as "review underscore requested", and the
underscore is not something the listener can act on. An unfamiliar reason falls
back to its own words with the underscores spoken as spaces
(`NotificationReason.Word`).

## What is not built, and needs a Windows run

- **The toast.** Windows App SDK app notifications need package identity;
  ARCHITECTURE 4.4 records that GitApp ships a sparse package for exactly this.
  The toast is a convenience, and its activation must route through a protocol
  handler into the relevant screen with focus on the item, not the window root.
- **The inbox screen.** The real surface, because toasts are transient and easy
  to miss. A fully accessible list of `GitHubNotification` rows, an F6 pane like
  every other screen, with mark-as-read and open on each row.
- **Live verification.** Everything here is tested against recorded JSON and
  scripted responses. The 304 path, the `X-Poll-Interval` honouring, and the
  backoff are exactly the behaviours a live account will not show you on demand,
  which is why they are unit-tested rather than left for a manual pass — but the
  success path has not yet been seen against a real inbox with a real token.
