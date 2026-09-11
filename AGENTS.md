# Working on GitApp

An accessible, screen-reader-first Git and GitHub client for Windows and
(eventually) macOS. Read this before changing anything. It is written for
whoever picks the work up next, human or otherwise.

## The one rule

**Every feature has to be fully operable from the keyboard and has to read
correctly in a screen reader before it counts as done.** Not "afterwards",
not "in a follow-up". A feature that works visually and reads badly is not
half finished, it is broken, because the people this app exists for are the
ones who cannot see it.

If you have to choose between shipping a screen and shipping a screen that
reads correctly, ship neither and say so.

## Stack

.NET MAUI 10, C#, XAML. `net10.0-windows10.0.19041.0` and
`net10.0-maccatalyst`. Git operations shell out to `git.exe` and parse
porcelain v2; there is no libgit2.

This project ran on React Native Windows for a day and was ported off it.
Do not propose going back, and do not propose a third framework. The
reasoning and the measurements are in `docs/ARCHITECTURE.md` section 2 and
`docs/SPIKE-MAUI.md`.

## Layout

```
src/GitApp.Core/      no UI framework dependency, all the testable logic
  Domain/             records that carry their own AccessibleName
  Services/           GitService (partial), parsers, JSON stores
src/GitApp/           the MAUI app
  Accessibility/      Announcer, FocusManager, Pane, PlatformFocus
  Platforms/Windows/  the three things MAUI cannot do portably
  ViewModels/         hand-rolled MVVM, no toolkit
tests/GitApp.Core.Tests/
docs/                 the specs; read ARCHITECTURE.md first
```

## Build, run, test

```
dotnet build src/GitApp/GitApp.csproj -f net10.0-windows10.0.19041.0
dotnet test  tests/GitApp.Core.Tests/GitApp.Core.Tests.csproj
```

Stop the running app before building or the DLL is locked (MSB3021).
Warnings are errors in practice: the tree is at zero and should stay there.

## Conventions that are not obvious from the code

- **Announcement strings live in the domain, not the view.** `FileChange`,
  `CommitInfo`, `DiffLine` and friends each expose `AccessibleName`. That is
  deliberate: it is the part most likely to be quietly wrong, and in
  `GitApp.Core` it is testable without starting a window.
- **Content first, classification after.** A row says what distinguishes it
  before it says what kind of thing it is. Twenty consecutive rows opening
  with the same word means the listener waits through it every time.
- **Say words, not symbols.** No `+`, `-`, `→` or arrows carrying meaning.
  Screen readers do not speak them at default punctuation levels, so the
  marker the user sees is not one they hear.
- **Announce only what focus will not announce.** If focus moves, or the
  focused element's name changes, the screen reader reads it; announcing on
  top of that says everything twice. See `ARCHITECTURE.md` 3.5.
- **Every long operation announces start and completion.** Silence reads as
  a hang. `Announcer.Operation(start)` returns the completion callback.
- **Never swallow a failure that changes what the user sees.** A silently
  empty list is indistinguishable from having nothing, and worse for someone
  who cannot glance at the window. `RepositoryStore.LoadError` is the
  pattern.
- **F6 cycles panes**; F7 moves between differences; Enter, Space, Left and
  Right fold; Escape backs out; Control+Enter posts what is being written
  (`PaneNavigation.SubmitHandler`). Pane order leaves gaps (10, 20, 25,
  30) so panes can be inserted.
- **A screen owns its panes and its keys.** `PaneNavigation.Attach(page)`
  runs on every appearance, clears the pane registry and the key handlers,
  and refills them from that page. Registering panes on a load event is not
  enough once there is more than one screen: a page swap does not reliably
  unload the old page, and its panes linger.
- **Focus must land on every screen change**, and the first attempt fails
  because nothing is loaded yet. Use
  `PaneNavigation.FocusFirstPaneWhenReady(page)`, which retries.

## How to verify accessibility here

Do not reason about the API and assume. **Read the live UI Automation tree
of the running app, and listen to it through NVDA's Speech Viewer.** Every
accessibility bug found in this project was invisible to the compiler and to
unit tests, and several looked correct in source.

The two checks find different things, and you need both:

- The **tree** shows structure: names, roles, set positions, whether an
  element is exposed at all.
- The **Speech Viewer** shows sequence: duplication, clipping, order. The
  diff viewer's folding once had a perfect tree while speaking every action
  twice. Nothing in a tree can show that.

Method, traps, and a warning about sending synthetic keystrokes to a window
that is not in the foreground: `docs/DEVELOPING.md`.

## Where the design decisions are written down

- `docs/ARCHITECTURE.md` — the governing spec. Accessibility contract,
  system architecture, milestones. Start here.
- `docs/DIFF-VIEWER.md` — the diff viewer, and why it deviates from VS Code.
- `docs/GITHUB.md` — sign-in, token storage, and the remote repository list.
- `docs/REPOSITORY-VIEW.md` — the github.com-style repository screen.
- `docs/ISSUES.md` — issues and pull requests, read only.
- `docs/DEVELOPING.md` — prerequisites, build, verification method, traps.
- `docs/SPIKE-MAUI.md` — the measurements behind the framework choice.

When you make a decision that a reasonable person would question later,
write it in the relevant doc with the reasoning, not just the conclusion.
Several decisions here look arbitrary until you know what was observed.

## State of play

Milestones 1 and 2 are done: the pane shell, and real Git operations against
real repositories — add, remove, clone, status, stage, commit, fetch, pull,
push, history, branch switching and creation, conflict resolution, and the
diff viewer with folding.

Milestone 3 is in progress: GitHub read. Sign-in by personal access token
and the remote repository list are built and work (`docs/GITHUB.md`);
browser sign-in is written but needs a registered OAuth client ID; the
repository screen and file view are built (`docs/REPOSITORY-VIEW.md`);
issues and pull requests are built (`docs/ISSUES.md`).

Milestone 4, GitHub write, has started: commenting on issues and pull
requests is built (`docs/ISSUES.md`, "Writing a comment"). Review, merge
and releases are not.

Known gaps, all recorded in the docs rather than hidden: Mac Catalyst is
entirely unverified and the owner has no Mac to test it on; the live UI
Automation test layer is still manual; intra-line diff highlighting is
unsolved.
