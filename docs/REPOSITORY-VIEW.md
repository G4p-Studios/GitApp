# The repository view

Status: designed, not built. Part of milestone 3.

This is the screen you get on github.com when you open a repository: the
file browser, with the commit that last touched each file, and the README
rendered underneath. It is the page people actually spend their time on, and
reproducing it faithfully is most of what "access the same features of
GitHub in a native interface" means.

## What github.com shows, in order

Taken from the real page, top to bottom:

1. **Owner and repository name**, with visibility (Public or Private).
2. **A branch selector**, and counts of branches and tags.
3. **A search-this-repository field.**
4. **Add file** and **Code** buttons.
5. **The latest commit**: author avatar and login, commit subject, short
   hash, relative time, and the total commit count for the branch.
6. **The file table.** One row per entry, directories first, each with name,
   the subject of the commit that last touched it, and how long ago.
7. **The README**, rendered, below the table.
8. **A sidebar**: description, topics, Readme/Activity/custom properties
   links, stars, watchers, forks, Releases, Packages, Contributors.

## What it has to become in speech

The visual page is a grid. Read in source order it would be unusable, for
the same reason a diff is: the relationship between a filename and the
commit beside it is carried by horizontal alignment, which speech does not
have.

So the file table follows the same rule as every other list in this app
(`ARCHITECTURE.md` 3.3): one row, one name, content first.

| Row | Announcement |
| --- | --- |
| Directory | `docs, folder, Add the accessible diff viewer, 5 hours ago` |
| File | `readme.md, file, Port from React Native Windows to .NET MAUI, 6 hours ago` |
| Latest commit | `Add the accessible diff viewer, by alexoloopios and claude, 5 hours ago, 1aabfb0, 17 commits on main` |

Open questions on that table, which need a listen-through before they are
settled:

- **Is the last-touch commit worth saying on every row?** It is the most
  distinctive thing about a file after its name, and it is also the longest.
  It may belong behind a key, the way column detail does in Explorer.
- **Directories first, or one alphabetical list?** github.com groups them;
  saying "folder" or "file" per row may make the grouping audible enough
  that either order works.

## Structure

The repository view is a set of panes like every other screen, cycled with
F6 (`ARCHITECTURE.md` 3.4):

- **Files** — the table above, plus the branch selector and the path
  breadcrumb when inside a directory.
- **Readme** — the rendered README as a document, not a list. Headings must
  expose `HeadingLevel` so a screen reader can jump by heading; that is the
  whole reason for rendering Markdown rather than showing it raw.
- **About** — description, topics, stars, watchers, forks, releases.

The latest-commit line sits at the top of the Files pane rather than in its
own pane. It is one fact, and a pane the user cycles through to hear one
sentence is friction.

## Why this is not a web view

It would be quicker to host the github.com page in a WebView2 and be done.
That gives up the whole premise: the announcements would be GitHub's, the
keyboard model would be the browser's, F6 would do nothing, and every
accessibility decision in this document would belong to someone else. The
point is native controls with names we control.

## Dependencies

Needs the GitHub API layer (`ARCHITECTURE.md` 4.3) and auth (4.5) first, so
it comes after the device-flow sign-in and the repository list. Markdown
rendering for the README is its own decision and is not yet made; the
requirement is heading semantics, link lists, and code blocks that do not
read punctuation aloud one character at a time.
