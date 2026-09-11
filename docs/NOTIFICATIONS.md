# Notifications

Status: 2026-09-11. Milestone 5. The polling and model layer is built and
unit-tested in `GitApp.Core`. The Windows toast, the poll loop, and the in-app
inbox screen are written but **unverified**: they live in the MAUI app, which
targets Windows and macOS and cannot be built or screen-reader-tested on the
Linux Cloud Agent. Nothing in the toast-and-inbox section below counts as done
under the one rule in `AGENTS.md` until a Windows run confirms it in NVDA. The
Windows checklist is at the end.

## What is built and tested (GitApp.Core)

- `GitHubClient.GetNotificationsAsync` — a conditional poll of
  `GET /notifications` that returns a `NotificationPage`.
- `GitHubClient.MarkThreadReadAsync` — mark one thread read, as opening it on
  github.com does.
- `NotificationPoller` — when to poll next, and which notifications are new
  enough to announce.
- `NotificationInbox` — holds the inbox and merges each poll in place, reporting
  only what moved (ARCHITECTURE 4.6) so the view keeps untouched rows.
- `GitHubNotification`, `NotificationPage`, `NotificationLinks` — the row, the
  poll result, and the API-url-to-web-url mapping, each testable without a
  window.

## What is written but unverified (the MAUI app)

- `ToastModule` — a Windows toast per new notification, over Windows App SDK
  app notifications, with a portable no-op default. Activation carries the
  thread id back so a toast opens the inbox on the right row.
- `NotificationService` — the app-wide background poll loop: a dispatcher timer
  that wakes every fifteen seconds, asks the scheduler whether a poll is due,
  and drives the client, the poller, the inbox and the toast. Self-guarding, so
  it idles while signed out.
- `NotificationsPage` and `NotificationsViewModel` — the inbox screen, an F6
  `Inbox` pane listing unread notifications newest first, each openable on
  github.com or markable read. Reached from a Notifications button on the
  GitHub screen.

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

## The Windows checklist — what a run still has to confirm

None of the MAUI code above has been compiled or heard, because the Cloud Agent
is Linux and the app targets Windows. A Windows run has to confirm, in NVDA and
Narrator both:

- **It builds.** `dotnet build src/GitApp/GitApp.csproj -f
  net10.0-windows10.0.19041.0` with the tree still at zero warnings. The toast
  module assumes the Windows App SDK's `Microsoft.Windows.AppNotifications`
  namespace is on the Windows target; confirm it resolves.
- **The toast appears and reads.** A new notification raises a toast whose title
  and body are spoken as words, in the same content order as the inbox row.
  Because the app is unpackaged (`WindowsPackageType=None`), confirm
  `AppNotificationManager.Register()` succeeds; ARCHITECTURE 4.4 anticipates a
  sparse package for persistent identity, which activation after exit needs.
- **Activation lands on the item.** Invoking a toast opens the inbox with focus
  on the notification it named, not on the window root or the list top.
- **The inbox reads as a screen, not a duplicate.** F6 reaches the Inbox pane
  and announces it; rows read content-first with unread as a separate element;
  a background poll that updates the list does not move the cursor or re-read
  rows the user already heard (ARCHITECTURE 3.3, 3.5, 4.6). The diff viewer's
  double-speak is the failure to watch for — read the Speech Viewer, not just
  the tree.
- **Mark read and open behave.** Mark read updates the row and the heading in
  place; open reaches github.com; the toast does not also speak on top of the
  screen reader reading it.

## Live verification against a real account

Everything in `GitApp.Core` is tested against recorded JSON and scripted
responses. The 304 path, the `X-Poll-Interval` honouring, and the backoff are
the behaviours a live account will not show on demand, which is why they are
unit-tested rather than left for a manual pass — but the success path has not
been seen against a real inbox with a real token, and needs one.
