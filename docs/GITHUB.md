# Signing in, and the remote repository list

Status: built and verified as far as an account allows, 2026-09-11.
Milestone 3, first slice. Home is now the front door (`docs/HOME.md`);
this screen is still the full remote repository list, reached from Places
or the command palette.

## What works

The GitHub screen has two panes, cycled with F6: Account and Repositories.
Escape goes back.

Sign in with a personal access token, and the list of every repository the
account owns, collaborates on, or reaches through an organization appears,
newest activity first. Each row can be opened in GitApp or cloned straight
to disk, and a clone started here is added to the local repository list on
the main screen rather than left for the user to go and find.

```
GitApp, G4p-Studios, public, C#, 3 stars, updated 5 hours ago, Git made super simple, yet so powerful
```

Name first, because that is what the user is scanning for and everything
else is confirmation. Visibility comes early: pushing to the wrong one of a
public and a private fork matters. The description goes last and is never
truncated — a long tail costs nothing when arrowing, because moving to the
next row interrupts speech, while a cut-off sentence cannot be heard in
full at all.

A filter field narrows the list and announces the count, because with a few
hundred repositories arrowing is not a way to find one.

## Personal access tokens first, browser sign-in second

The architecture note calls device flow the primary path, and the code for
it is written and tested. It is not the default, for a reason that is not
technical: **GitApp has no registered GitHub OAuth application.** Borrowing
another product's client ID — the GitHub CLI's, say — would mean users
granting access to something that is not this app, appearing in their
authorized-apps list under someone else's name. That is not acceptable
even though it would work.

So until an OAuth app is registered, sign-in is by token, and the browser
button is **hidden rather than disabled**. A permanently greyed-out control
is a puzzle for someone who cannot see that it is greyed out.

Anyone can enable it now by putting their own client ID in `settings.json`
as `GitHubClientId`.

The device flow, when it runs, deliberately puts the code **on the page
rather than in a dialog**: a modal would have to be dismissed before the
user could reach their browser, and could not be closed from underneath
them when the token arrives. The user code is also spaced out
(`W D J B - M J H T`) because a screen reader reads `WDJB` as a mumble and
the code has to be typed exactly.

## Tokens

Windows Credential Manager, through the Win32 API directly rather than
MAUI's `SecureStorage`. SecureStorage on Windows goes through
`ApplicationData.Current`, which needs package identity, and GitApp is
unpackaged — so it throws, and a token store that throws is a sign-in
screen that never works. Other platforms keep SecureStorage, which already
wraps the keychain.

They appear in Credential Manager as "GitApp: github.com", so the user can
see and revoke what the app holds without the app's cooperation. A
credential only the app can find is one the user cannot audit.

A revoked token is cleared when GitHub rejects it, but **not** when the
request merely fails: being offline is not a reason to make someone sign in
again.

## Errors are the feature

A live account never shows you a revoked token, a rate limit, or a
truncated reply, and those are exactly the moments a screen reader user is
left with nothing to act on. They are faked in tests and the wording is
asserted, because the wording is the product:

| Situation | What is said |
| --- | --- |
| Bad token | GitHub rejected that token. It may be mistyped, expired, or revoked. Check it and try again. |
| Rate limited | GitHub is rate limiting this app. Try again in about 12 minutes. |
| Missing scope | GitHub refused the request. The token is probably missing the repo scope. |
| No network | Could not reach GitHub. Check your internet connection. |
| Bad JSON | GitHub sent a reply GitApp could not read. |

The rate limit case is the one that matters most: "403 Forbidden" tells the
user nothing, while the reset time is sitting in a response header.

Paging runs to exhaustion, and a failure part way through is reported
rather than returning what arrived. A half-loaded list that claims to be
complete is worse than an error, because the repository you wanted is
simply absent.

## REST for the list, GraphQL for the repository view

ARCHITECTURE 4.3 specifies GraphQL for list screens, and the reason given
is that partial data arriving in waves re-renders a list and moves focus. A
single REST call to `/user/repos` returns every field this screen shows in
one response, so that reason is already met. GraphQL earns its complexity
on the repository view (`docs/REPOSITORY-VIEW.md`), where the last commit
touching each file cannot be had from REST in one request at all.

## What is not verified

**The signed-in path.** Everything above the 401 is tested against real
GitHub; the success path needs a real token and nobody has pasted one yet.
Parsing, paging, filtering and the row wording are covered by unit tests
against recorded JSON, but the list has never been seen with real data.

**Device flow end to end.** The polling state machine is unit tested
against every documented response, including `slow_down`, which cannot be
provoked reliably against the live service. The real round trip needs a
registered client ID.
