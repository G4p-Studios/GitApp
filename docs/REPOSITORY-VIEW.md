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
5. **The latest commit.** Branch HEAD, at the foot of Files between the
   table and the README, not its own pane. It is one fact. The file rows
   already carry last-touch, so the banner does not change to "last
   commit in this folder" when you descend: two different facts would
   then sound like the same one.
6. **The file table.** Directories first, then files, alphabetical inside
   each group, matching github.com. A parent-folder row appears once you
   are not at the root.
7. **The README**, rendered, in its own pane.
8. **About**: description, topics, language, stars, watchers, forks,
   releases, open issues and pull requests, branch and tag counts. The
   releases fact is a button that opens the releases screen, as the
   sidebar's Releases section does on github.com (`docs/RELEASES.md`).

Issues and pull requests are not panes on this screen. They are their own
lists, reached from the toolbar, documented in `docs/ISSUES.md`. A pane
you F6 through to reach the README is friction, and a list of issues is
not a fact about the files you are looking at.

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

Three panes, cycled with F6 (`ARCHITECTURE.md` 3.4), placed where
github.com places them: Files in the main column with the README beneath
it, and About as the sidebar on the right. A sighted user and a screen
reader user then describe the same screen to each other.

- **Files** — branch picker, breadcrumb when inside a directory, file
  table, then the latest commit. Enter or Space opens a folder or a file;
  Left goes up one folder; Escape goes up, and at the root returns to the
  repository list.
- **Readme** — the README as one native document. Arrow keys move a
  caret line by line; links stay in the sentence they belong to.
- **About** — one control per fact. The releases count is a button and
  opens the releases screen; everything else is text.

The latest-commit line is the last thing in the Files pane, between the
table and the README: subject on one line, author, age and hash on the
next, spoken as one name. It is text, not a control, because there is
nowhere for it to go yet; a commit screen would make it a link.

Enter or a click on a file opens it on its own screen. A submodule stays
on the tree and says so: fetching a submodule as a blob would be the
wrong object.

## Opening a file

A new screen, not a fourth pane on the tree. The file table is a place
you pick from; the contents are a place you read. Mixing them would mean
F6-ing past hundreds of source lines to reach the README of the folder
you were in.

Two panes: **File** then **About**. Escape returns to the file table.

- **Markdown** (`.md`, `.markdown`, `.mdown`, `.mdwn`) is the same native
  document as the README: arrow keys move a caret, links sit in the
  sentence as hyperlinks, Enter on a link opens it. File headings are
  shifted down one level so they cannot outrank the filename. Showing
  source instead would give up that reading model. The source remains on
  github.com, via the toolbar button.
- **Other text** is a line list, Consolas, with the line number in a
  gutter the way the diff viewer is. Each row announces content first,
  line number last: `return false;, line 15`. Leading with the number
  would open every row with "line". An empty line says `blank, line 7`;
  silence is indistinguishable from a failed read.
- **Binary** says "This file is not text." **Larger than GitHub will
  return as text** (about a megabyte) says "This file is too large to
  show here." Both offer github.com on the toolbar. An empty file says
  it is empty.
- **About** is path, branch, size, line count or why it cannot be shown,
  and the last-touch commit if the tree had it. Size is words, never
  "KB": a screen reader reads "KB" as letters.

The load is one GraphQL blob (`object(expression: "{branch}:{path}")`)
and the line list is not filled until it has returned, so the list does
not rebuild under the cursor. Completion is the filename, then what
arrived: `Program.cs, 412 lines, 18 kilobytes`. Intra-line highlighting
is not here; it is unsolved in the diff viewer too.

## Markdown, and why it is not a web view

Hosting github.com in a WebView2 would give up the premise: the
announcements would be GitHub's, the keyboard model would be the browser's,
F6 would do nothing, and every accessibility decision in this document
would belong to someone else. WebView2 is an HWND island, so the page's
preview-key hook never sees F6 while it has focus.

A stack of labels is not a document either. Labels are not in the tab
order, arrow keys cannot move a caret through them, and the only
focusable things were the links, which had been pulled out of their
sentences and rendered as buttons. A listen-through found both of those
at once: you could not read the README with the arrow keys, and every
link said "button".

So the README is one read-only RichEditBox, the same caret-reading shape
as Notepad. It lives in the XAML tree, so F6 still cycles panes. The
parser in `GitApp.Core` is not CommonMark; it covers the shapes that
carry meaning and leaves the rest as text rather than guessing it into
the wrong role.

- Arrow keys move the caret and read line by line. That is the whole
  reason this is a document control rather than labels.
- Links stay in the sentence as hyperlinks (`ITextRange.Link`), not
  buttons after the paragraph. The name is the visible text; the control
  type is already "link". Enter on the caret opens the destination.
- A fenced code block is one run, summarised as "Code block, csharp, 2
  lines" and then the code. One element per character would spell
  punctuation aloud.
- Headings are bold and larger in the same document. They are no longer
  separate UIA heading elements, because that was the label stack that
  could not be read. Heading jump in browse mode is the remaining
  question; being able to read the file is not.

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

The repository screen itself has been opened against a real account; that
is how the label-stack README was found. The document control's parsing
and link ranges are unit tested. It has not yet been listened through:
caret reading, Enter on a link, and F6 out of the README. The file view
has the same gap.
