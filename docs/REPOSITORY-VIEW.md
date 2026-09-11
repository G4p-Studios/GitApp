# The repository view

Status: built, 2026-09-11. Part of milestone 3.

This is the screen you get on github.com when you open a repository: the
file browser, with the commit that last touched each file, and the README
rendered underneath. It is the page people actually spend their time on, and
reproducing it faithfully is most of "access the same features of GitHub in
a native interface".

Open a repository from the GitHub list. Clone stays on the row; Open now
means this screen, not a browser tab. github.com is a button on this screen
instead, because once you are looking at a repository you might still want
the web page, and until you are, the in-app view is the point.

## What github.com shows, and what this screen does

Taken from the real page, top to bottom, then mapped onto panes.

1. **Owner and repository name**, with visibility. Heading of the Files pane.
2. **A branch selector**, and counts of branches and tags. The picker is in
   Files; the counts live in About, because a pane you F6 through to hear
   "12 branches, 3 tags" after already knowing the branch is friction.
3. **A search-this-repository field.** Not built. That is GitHub code
   search, a different API, and a different screen.
4. **Add file** and **Code** buttons. Add file is write (milestone 4).
   Clone is already on this screen and on the list.
5. **The latest commit.** Branch HEAD, at the top of Files, not its own
   pane. It is one fact. The file rows already carry last-touch, so the
   banner does not change to "last commit in this folder" when you
   descend: two different facts would then sound like the same one.
6. **The file table.** Directories first, then files, alphabetical inside
   each group, matching github.com. A parent-folder row appears once you
   are not at the root.
7. **The README**, rendered, in its own pane.
8. **About**: description, topics, language, stars, watchers, forks,
   releases, branch and tag counts.

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
| Parent | `Parent folder` |
| Latest commit | `Add the accessible diff viewer, by alexoloopios and claude, 5 hours ago, 1aabfb0, 17 commits on main` |

The two open questions on that table, and what we did:

- **Last-touch on every row.** Kept. It is the most distinctive thing
  about a file after its name. It is also the longest, and a listen-through
  may yet move it behind a key. Omitting it before that listen would be
  guessing. The GraphQL round-trip this needs is why the repository view
  is GraphQL at all (ARCHITECTURE 4.3): REST cannot return last-touch per
  file in one request.
- **Directories first.** Yes. Saying "folder" or "file" on every row makes
  the grouping audible, and matching github.com's order means a sighted
  user and a screen reader user are pointing at the same third row.

## Structure

Three panes, cycled with F6 (`ARCHITECTURE.md` 3.4):

- **Files** — branch picker, breadcrumb when inside a directory, latest
  commit, file table. Enter or Space opens a folder; Left goes up one
  folder; Escape goes up, and at the root returns to the repository list.
- **Readme** — the rendered README as a document, not a list. Headings
  expose `HeadingLevel` so a screen reader can jump by heading.
- **About** — one control per fact.

The latest-commit line sits at the top of the Files pane rather than in its
own pane.

Opening an individual file is not built. Enter on a file says so, rather
than doing nothing, because silence after a keypress reads as the key
having failed.

## Markdown, and why it is not a web view

Hosting github.com in a WebView2 would give up the premise: the
announcements would be GitHub's, the keyboard model would be the browser's,
F6 would do nothing, and every accessibility decision in this document
would belong to someone else.

The README is parsed in `GitApp.Core` into headings, paragraphs, lists and
fenced code, then rendered as native labels. That parser is not CommonMark;
it covers the shapes that carry accessibility meaning and leaves the rest
as text rather than guessing it into the wrong role.

- Headings get `SemanticProperties.HeadingLevel`. That is the whole reason
  for rendering Markdown at all.
- A fenced code block is **one** label, summarised as "Code block, csharp,
  12 lines" and then the code. One element per character would spell
  punctuation aloud.
- Links are lifted out of the paragraph and rendered as buttons, because
  MAUI has no portable hyperlink control. NVDA's link list will not find
  them; the button list will. The paragraph itself keeps the visible text
  without the URL, so the destination is not spelled inside the sentence.

## GraphQL, in two requests, shown as one

The first query is the repository, the branch tip, and the tree. The
second is last-touch commits (aliased `history(path:)` per entry) and the
README blob. The file table is not filled until both have returned, so it
does not rebuild under the cursor with names that then grow a commit
subject. If the second query fails, the files still appear, without
last-touch and without a README: those are confirmation, not the table,
and hiding the files would be worse than a shorter row.

Paging last-touch in chunks of forty keeps a busy repository under
GitHub's query complexity limit. A failure part way through keeps what
arrived, for the same reason: a half-annotated list is still a list.

## What is not verified

The signed-in path. Parsing, last-touch merging, README rendering and the
row wording are covered by unit tests against recorded GraphQL, but this
screen has not been opened against a live token. Same gap as the
repository list (`docs/GITHUB.md`).
