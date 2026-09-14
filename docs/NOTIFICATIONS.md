# Notifications

Status: 2026-09-14. Milestone 5. The polling and model layer is built and
unit-tested in `GitApp.Core`. The Windows toast, the poll loop, and the in-app
inbox were verified in NVDA Speech Viewer on an unpackaged Windows build
against a live account. Narrator has not been run. Toast *activation* (click
the toast, land on that row) and Open in browser were not verified: the
former is hard to drive from a script, and the latter steals the foreground.
The checklist at the end records what was heard.

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

## What the MAUI app does

- `ToastModule` — a Windows toast per new notification, over Windows App SDK
  app notifications, with a portable no-op default. Activation carries the
  thread id back so a toast opens the inbox on the right row. Toasts appear
  unpackaged; clicking one to land on that row is still unverified.
- `NotificationService` — the app-wide background poll loop: a dispatcher timer
  that wakes every fifteen seconds, asks the scheduler whether a poll is due,
  and drives the client, the poller, the inbox and the toast. Self-guarding, so
  it idles while signed out. The first successful body is a baseline, so
  opening the app does not toast the whole inbox.
- `NotificationsPage` and `NotificationsViewModel` — the inbox screen, an F6
  `Inbox` pane listing unread notifications newest first, each openable on
  github.com or markable read. Reached from a Notifications button on the
  GitHub screen. Heard in NVDA; see the checklist.

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

The first successful body is a **baseline**, not news. Without that, opening
the app toasted every unread thread at once (heard on the first Windows run:
fifty toasts, speech flooded). `Observe` seeds `_seen` on that first body and
returns nothing to announce. Later polls still toast threads that are new or
whose `updated_at` moved.

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

## The Windows checklist — heard 2026-09-14 in NVDA

Unpackaged `net10.0-windows10.0.19041.0` build, zero warnings, signed in.
Speech Viewer via `WM_GETTEXT` on the RICHEDIT child (the frame caption is
not the speech). Discord toasts in the same buffer were ignored.

- **It builds.** Confirmed. `AppNotificationManager.Register()` succeeded
  unpackaged: toasts appeared as `GitApp, {title}, {reason}, {subject}, in
  {repo}. window`.
- **The first poll does not toast the inbox.** Confirmed after the baseline
  change. Opening GitHub showed 59 repositories and zero toast-like
  `GitApp, … window` lines.
- **The inbox reads as a screen.** Opening it spoke `Inbox grouping`,
  `Notifications list`, the first row as `1 of 50`, then `Inbox pane`.
  Arrow down spoke each row once, content-first, with set position. No
  double-speak. Unread is a separate `Text: unread` child in the UIA tree;
  it is not in `AccessibleName` and is not a tab stop, so arrowing the list
  does not say "unread". That matches 3.3; this screen is unread-only, so
  the marker would be the same word on every row.
- **F6 reaches the Inbox pane.** The screen has one pane, and chrome (Back,
  Refresh, Mark all read) sits outside it. `CyclePane` used to no-op when
  `_panes.Count < 2`, which stranded focus on Back. It now re-enters the
  only pane. From Back, F6 restored the last control inside the list and
  announced `Inbox pane`.
- **Mark read.** The unread inbox removes the row (it does not sit there
  looking identical). Destroying the focused Mark read button dumped onto
  Back and NVDA spoke that first. The fix parks focus on the heading
  *before* the network call, yields so WinUI actually moves it, then
  restores onto the neighbour after the row is gone. Heard:
  `{count} unread notifications`, the neighbour as `3 of 49`, then
  `Marked read. {title}`. Focus afterwards was the neighbour list item.
  Heading count followed the GitHub call.
- **Still unverified.** Toast activation landing on that thread. Open in
  browser (would steal the foreground). Narrator. Paging past GitHub's
  first 50. The signed-in Account pane still exposes the personal access
  token field (pre-existing; not introduced here).

## Live verification against a real account

Everything in `GitApp.Core` is tested against recorded JSON and scripted
responses. The 304 path, the `X-Poll-Interval` honouring, and the backoff are
the behaviours a live account will not show on demand, which is why they are
unit-tested rather than left for a manual pass — but the success path has not
been seen against a real inbox with a real token, and needs one.
