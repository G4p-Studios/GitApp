# Home, the feed, and the command palette

Status: 2026-09-14. The window now opens on Home, a github.com-style
dashboard, rather than on the local workbench. Local repositories are a
place you go to, not the front door.

## Why Home is first

The people this app exists for live on GitHub as much as they live in a
folder on disk. Opening to a local list meant signing in was a button you
had to find, and the feed — what fellow developers just starred, forked,
or opened — was not in the app at all.

If a token is already in Credential Manager, Home restores the session and
loads the feed without asking. That is "signing in automatically": not
skipping GitHub, just not making someone paste the same token every launch.
Restore runs before any local clone is inspected, so a slow disk does not
leave Home looking signed out. If there is no token, or GitHub rejected
it, Home shows the token field in the Feed pane and says so.

Until signed in, F6 is Places then Feed. Top repositories joins the cycle
once there is an account, so an empty jump list is not a pane you have to
tab through.

## Layout

Three panes, cycled with F6:

- **Places** — Home, Local repositories, Your repositories, Notifications.
  Enter opens the selected place.
- **Top repositories** — a short jump list, most recently pushed. Enter
  opens that repository in GitApp.
- **Feed** — `GET /users/{login}/received_events`, the same events
  github.com's dashboard is made of: stars, forks, pushes, pull requests,
  issues. Actor first, because that is what distinguishes one row from the
  next in a mixed feed (ARCHITECTURE 3.3). Enter opens the repository, or
  the browser when there is no repository to open.

Escape from Local, GitHub, or the command palette returns to the previous
screen. Home itself has nowhere to go back to.

The local workbench is unchanged: add, clone, stage, commit, fetch, pull,
push, the diff viewer. It is reached from Places, from the Home button on
that screen, or from the command palette.

## Command palette

Control+Shift+P, the same chord as VS Code and Quill. A Commands button on
Home says the shortcut in its hint, so it is discoverable without a cheat
sheet.

The palette is a screen: a search field and a list. An empty query is the
whole catalogue, so it is immediately arrowable. Typing narrows by
substring of the title or the category, and the heading says how many
remain (the heading is not announced on each letter: the search field
already speaks what was typed). Enter from the search field or the list
runs the selected command (or the first match). Space in the search field
is a space. Escape closes it.

Commands are go-to (Home, Local, Your repositories, Notifications) and a
handful of local Git actions. A command that cannot run right now says so
rather than doing nothing.

## Feed wording

GitHub's event `type` is never spoken raw. `WatchEvent` is "starred",
`refs/heads/main` is "main", `review_requested` is "requested a review on".
An event this app has not seen before still reads: the word Event is
dropped and the rest is split into words, so a new type is a sentence
instead of a silent row.

## The Windows checklist — heard 2026-09-14 in NVDA

Unpackaged `net10.0-windows10.0.19041.0` build, zero warnings, signed in as
Alex Chapman. Speech Viewer via `WM_GETTEXT` on the RICHEDIT child.

- **Home is the front door.** Launch spoke `GitApp`, `Places grouping`,
  `Home, Your GitHub feed 1 of 4`, `Places pane`, `Loading your GitHub
  home`, then `29 events`. Status read `Signed in as Alex Chapman,
  alexoloopios`. No personal access token field.
- **F6.** Places, Top repositories (`GitApp, G4p-Studios… 1 of 7`), Feed
  (`liruifengv opened pull request… 1 of 29`, actor first), then wrap to
  Places. Enter on a feed row opened that repository in GitApp; Escape
  returned to Home.
- **Places.** Local repositories opened the workbench (`gitapp, main, up
  to date…`). Escape returned to `Local repositories… 2 of 4`, not the
  first row. Your repositories opened the GitHub list (`59 repositories`).
  Notifications opened the unread inbox.
- **Command palette.** Control+Shift+P landed on `Search commands`.
  Typing `loc` spoke the letters only. Enter from the search field ran
  Local repositories.
- **Still unverified.** Opening a feed row that has no repository, which
  should go to the browser. Narrator. Mac Catalyst.
