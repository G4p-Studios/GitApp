# GitApp Architecture and Accessibility Specification

Status: draft 1, 2026-09-10
Target stack: .NET MAUI 10, C#, Windows first with Mac Catalyst for macOS

This document is the reference for how GitApp is built and, more importantly, for
the accessibility contract every screen must satisfy. It is written to be read
before code, and amended when reality disagrees with it.

## 1. Scope and constraints

GitApp is a Windows desktop client for GitHub and, later, other Git hosts. It
covers three areas that are usually separate products:

1. Browsing and acting on remote GitHub data: repositories, issues, pull
   requests, reviews, releases, actions, discussions.
2. Managing local clones: clone, fetch, pull, push, branch, stage, commit,
   merge, rebase, stash, submodules.
3. Staying informed: GitHub notification subscriptions surfaced as Windows
   toasts and an in-app inbox.

The binding constraint is that **every one of those must be fully operable by a
screen reader user with the keyboard alone**, and must feel like a Windows
application rather than a website in a frame. Visual design borrows from
github.com; interaction design borrows from Windows.

macOS is a goal, via Mac Catalyst, and the stack was chosen for it. It is not
a v1 ship target: Windows ships first and Catalyst is unverified on real
hardware. Linux is out, and so is a built-in diff editor. Other Git hosts,
GitLab and Codeberg, are a services-layer concern designed for from the start
but shipping after GitHub.

## 2. Stack decision record

### 2.1 The decision

.NET MAUI 10, C#, targeting Windows first and Mac Catalyst for macOS.

The project began on React Native Windows and moved after two things became
clear. First, macOS support and a single codebase became stated goals, and
RNW could not meet them: `react-native-macos` was at 0.81.9 while RNW was at
0.84, which are different React Native core versions, so one codebase could
not target both. Second, and more important, a spike showed MAUI supplies the
accessibility substrate that RNW Fabric requires an application to build for
itself.

The full measurements are in `docs/SPIKE-MAUI.md`. The short version:

- RNW Fabric composes visuals directly and implements UI Automation itself.
  No control it renders carries accessibility; every role, pattern, set
  position and keyboard behaviour is asserted by hand.
- MAUI on Windows renders WinUI controls, which arrive with Microsoft's own
  automation peers already correct.

Measured against a like-for-like shell, the same surface cost 1,547 lines of
hand-written accessibility and theme code on RNW and 134 lines of XAML and
code-behind on MAUI.

### 2.2 What MAUI supplies, verified

Read from a live UI Automation client against a running app, not from
documentation.

| Capability | Status |
| --- | --- |
| List and ListItem control types | Free from `CollectionView` |
| `PositionInSet` / `SizeOfSet` | Free and correct, spoken as "1 of 4" |
| `SelectionItem` pattern on rows | Free |
| `Selection`, `Scroll`, `ItemContainer` on the list | Free |
| Roving focus, single tab stop | Free |
| Arrow keys, Home, End | Free |
| Initial focus on the first item | Free |
| Light, dark and high contrast themes | Free |
| Focused control announced on window activation | Free |

`ItemContainer` deserves a specific mention: Fabric does not implement it at
all, and it is the pattern that lets a screen reader reach items virtualized
out of view. On RNW that was a documented gap with only a partial workaround.

### 2.3 What is still ours to build

| Gap | Why | Where |
| --- | --- | --- |
| F6 region navigation | Neither MAUI nor Windows has a landmark concept; F6 is a convention, not a feature | `Accessibility/PaneNavigation.cs` plus a Windows key hook |
| Announcement policy | `SemanticScreenReader.Announce` is plumbing with no rate limiting, coalescing or start/finish pairing | `Accessibility/Announcer.cs` |
| Focus restoration | Nothing remembers where focus should return to when a menu or dialog closes | `Accessibility/FocusManager.cs` |
| Focusing a list from outside | `CollectionView.Focus()` lands on the scroll host, which reports as an unnamed Pane and says nothing useful | `Platforms/Windows/PlatformFocus.Windows.cs` |
| Remembering the focused row | A row is a platform item container, not a `VisualElement`, so a pane cannot hold a reference to it | `PlatformFocus.TrackFocusWithin` |

Every one of these was found by running the app and reading the automation
tree, not by reasoning about the API surface. That remains the working
method; see section 3.7.

### 2.4 Honest risk statement

Two risks are live.

**Mac Catalyst is untested.** Everything measured so far is Windows. Catalyst
is an iOS UI running on macOS and VoiceOver treats it accordingly; native
macOS remains an upstream discussion rather than a shipping target. Nothing
should be promised about macOS until a Catalyst spike runs on real hardware.

**MAUI has open accessibility issues**, around 30 under the `t/a11y` label.
The composition is reassuring rather than alarming, since many are
Microsoft's own conformance passes filing their findings and closures land
steadily, but the count is not zero and `CollectionView` keyboard navigation
was among them until this project measured it working.

## 3. Accessibility architecture

### 3.1 The rule

Feature screens use framework controls directly and lean on what MAUI already
gets right. They do not hand-roll accessibility semantics.

This is a change of emphasis from the React Native design, where the rule was
that screens must never touch accessibility props, because a shared primitive
layer owned every role and pattern. That layer existed because Fabric supplied
nothing. MAUI supplies most of it, so an equivalent wrapper would be ceremony
that hides working behaviour behind our own bugs.

What screens still must not do:

- Write a literal colour. Use `AppThemeBinding` or a system brush, so light,
  dark and high contrast all keep working (3.8).
- Invent their own region structure. Wrap content in `Pane` (3.4).
- Announce directly through `SemanticScreenReader`. Go through `Announcer`,
  which owns the rate limiting (3.5).
- Split a row's content into separate accessible children. Give the row one
  name, in reading order (3.3).

### 3.2 What replaced the primitive layer

`src/GitApp/Accessibility/` is four small pieces rather than a control
library:

- `Announcer` - announcement policy: throttling, coalescing, dropped repeats,
  and the start/finish pairing for long operations.
- `FocusManager` - the pane registry and the focus restore stack.
- `Pane` and `PaneNavigation` - regions and F6 cycling.
- `PlatformFocus` - the two things focus cannot do portably: focusing into a
  list, and remembering which row was focused.

Everything else that used to live there (roving tabindex, arrow keys, set
positions, selection state, initial focus, theming) is supplied by the
framework and verified in `docs/SPIKE-MAUI.md`.

### 3.3 Naming and description contract

Three distinct slots, used consistently across the whole app:

- `SemanticProperties.Description` is the identity of the control. Short,
  front-loaded with the distinguishing word, and never containing the control
  type, since the control type is already announced. For a list row, the full
  row content as one string, in reading order, comma-separated. Note the name:
  on Windows this becomes the UIA *Name*, not FullDescription, which is the
  opposite of what the property name suggests.
- `SemanticProperties.Hint` is what invoking the control does, and only
  appears when that is non-obvious.
- `SemanticProperties.HeadingLevel` marks headings so a screen reader can jump
  between them.

`AutomationProperties.Name` is obsolete in MAUI 10 and the compiler says so.
Do not reach for it.

Transient state such as "syncing" or "3 conflicts" goes in one of two places:

- The status line (3.5), for anything about the operation in progress.
- A separate, individually named element in the row, for state that belongs
  to the item and persists.

Never fold transient state into the row's own name. The name becomes unstable,
and a screen reader re-announces the whole row every time the state ticks.
This rule cost real effort to discover on the previous stack, where the
obvious slot for it, UIA ItemStatus, turned out to be hardcoded and unusable.
The rule outlived the framework.

### 3.4 Focus and navigation model

Windows conventions, implemented by us because nothing implements them for us:

- **Tab and Shift+Tab** move between controls and between composite widgets,
  never into every row of a list. Lists and trees are a single tab stop with a
  roving tabindex inside.
- **F6 and Shift+F6** cycle panes in order: sidebar, primary content, detail
  pane, status bar. Each pane announces its name on entry. This is our substitute
  for the missing landmark support and it is mandatory on every screen.
- **Arrow keys** move within a composite widget only. Left and Right collapse
  and expand where a list has foldable rows, and Left from inside a folded
  region first steps out to its header, as a tree does. The diff viewer is
  the only such list today (`docs/DIFF-VIEWER.md`).
- **Escape** backs out one level. It closes a menu, dialog, or filter, and always
  restores focus to the element that opened it.
- **Applications key and Shift+F10** open the context menu for the focused item.
  Every list row and tree node has one, and it duplicates every action available
  by mouse. This is the primary accessible action surface in the app.
- **Alt** enters the menu bar.

**Every window opens with a control focused.** `PaneHost` focuses the first
pane's content on mount, and a pane's content control claims the entry point so
that entering a pane lands on the list rather than on the wrapper. Nothing sets
this for us: without it the window has no focused descendant, which shows up as
no focus indicator until the user presses Tab, and as a screen reader with
nothing to announce beyond the window title. Windows restores focus to the last
focused child on reactivation, but only if something held focus to begin with.

The initial focus is deliberately silent. The screen reader already announces
the window and the newly focused control on activation, so emitting our own
"X pane" on top of that is duplicate speech.

**Which pane is active is decided by focus, not by F6.** A mouse click, a Tab,
or a jump key like F7 all move focus into a pane without going through the
cycler, and a manager that only learns about F6 then cycles from the wrong
place and offers pane-specific keys to the wrong pane. The platform focus
tracker reports every arrival, so the two can never disagree.

Focus restoration is centralized in a `FocusManager` service. Any code path that
destroys the focused element must have pushed a restore target first. Losing
focus to the window root is the single most common way a screen reader app
becomes unusable, and RNW will do it silently on re-render.

`view.focus()` works imperatively in Fabric as of 0.84; earlier versions required
native workarounds. That is one of the reasons the version floor is 0.84.

### 3.5 Announcements

`Announcer` wraps two channels:

- `SemanticScreenReader.Announce` for discrete events: "Pushed 3 commits to
  origin/main", "Clone failed: authentication required".
- The status line for progress that updates in place, such as fetch and
  checkout progress. `Polite` by default, and `Assertive` only for errors that
  stop the user's current task.

MAUI supplies the plumbing but no policy, and the policy was the hard part.

Rules: no announcement fires more than once every 500 ms; identical
consecutive messages are dropped, because they are almost always a re-render;
progress announcements report meaningful milestones rather than every percent;
and every long-running Git operation announces both start and completion,
because silence reads as a hang.

**Announce only what focus will not announce.** If focus lands somewhere, or
the focused element's own name changes underneath it, the screen reader reads
it, and an announcement on top of that is the same sentence twice. So the
announcer is for things with no focused representation: an operation
completing, an error, a key that deliberately did nothing. Code that moves
focus and announces the destination is announcing it twice. This is invisible
in the UI Automation tree, because the duplication is in time rather than in
structure; it was found by listening, and it is the reason listening is not
optional (3.7).

### 3.6 Dropping to the platform

The React Native design needed an escape hatch, XAML islands, for semantics
Fabric could not express. On MAUI the framework already renders WinUI, so the
equivalent is a handler customisation or a small file under `Platforms/`.

Two exist so far, both under `Platforms/Windows/`:

- `PaneNavigation.Windows.cs` hooks F6 and F7, and the fold keys, because MAUI
  has no cross-platform way to observe a keystroke that no control has
  claimed.
- `PlatformFocus.Windows.cs` focuses into a list, remembers which row had
  focus, and names the control that hosts a `CollectionView`'s EmptyView.

That last one is worth stating, because it is a framework defect rather than
a missing API. An empty `CollectionView` still holds a tab stop: MAUI's
`EmptyViewContentControl`, which is `IsTabStop=True` and has **no automation
peer at all**. Tab onto it and UI Automation reports the content island's
root window, so a screen reader says nothing. Setting an automation name on
it creates the peer; a localized control type of "status" stops it being
announced as "custom".

Skipping the stop would have been the easier fix and the wrong one. "There
is nothing here" is real information, and the placeholder text on screen is
how a sighted user gets it. So the stop stays and says the same words.

The rule for adding a fourth: prove the portable API cannot do it, and write
down what you observed. All of the above carry that reasoning inline, because
in each case the obvious portable call appears to succeed while doing the
wrong thing.

### 3.7 Verification

Accessibility is verified by tests, not by intention.

1. **Unit tests over the parsing layer**, in `tests/GitApp.Core.Tests/`.
   `GitApp.Core` has no UI framework dependency, so the logic that is easiest
   to get quietly wrong is testable as plain functions. The fixtures are real
   git output rather than examples from the documentation, because the
   failure being guarded against is a mismatch between the two.

   The rename case has its own test. A type 2 porcelain record is followed by
   its original path as a separate NUL-delimited field, so a naive split
   attributes every subsequent row to the wrong file. That is silent, and it
   would tell a user they are about to commit a file they are not.

2. **Live UI Automation checks**, against the running app. Not yet automated.
   `microsoft/react-native-gallery` does this with a C# test project driving
   the deployed app through `System.Windows.Automation` and scanning with
   Axe.Windows; that is the model to copy.

   The gap is not theoretical. Every accessibility bug in this project so far
   was invisible to the compiler and to any unit test, and several looked
   correct in source. Until it is automated, the check is manual and the
   method is in `docs/DEVELOPING.md`.

3. **Keyboard reachability.** For each screen, every interactive element
   reachable by Tab and arrow keys from the pane root, and every mouse action
   available from the keyboard.

4. **Manual screen reader pass.** NVDA and Narrator, both, before any feature
   is called done. JAWS before each release.

   This is not a formality on top of item 2, and the diff viewer's folding
   proved it: the UI Automation tree was correct in every detail while every
   fold and every jump was spoken twice, once by NVDA reading the row and
   once by us announcing it. Duplication, timing, and anything clipped by the
   next utterance exist only in the speech stream. Reading NVDA's Speech
   Viewer is the cheap version of this, and the method is in
   `docs/DEVELOPING.md`.

CI gates on items 1 and 3. Items 2 and 4 gate the release.

### 3.8 Theming, dark mode and high contrast

MAUI follows the system light, dark and high contrast themes on its own,
verified by running the app while toggling the OS setting.

This was a significant piece of hand-written machinery on React Native. Under
Fabric no `PlatformColor` name returned dark-theme values: the WinUI Fluent
names were aliased to a hardcoded light table, and the classic `UIElementType`
names tracked high contrast but not light and dark. Three palettes and a
selection hook existed to work around that. None of it is needed here.

The single rule that survives, and the reason that machinery existed:
**never write a literal colour in a view.** Under MAUI that means
`AppThemeBinding` or a system brush. A hex literal cannot respond to a theme,
and the failure is invisible to whichever developer is not using the theme
that breaks.

`src/GitApp/Theme/Tokens.cs` holds spacing, radii, type sizes and control
metrics. It deliberately holds no colours.

## 4. System architecture

### 4.1 Layers

One C# project, `src/GitApp/`:

- `Views/` and the page files - feature UI in XAML.
- `Accessibility/` - Announcer, FocusManager, Pane, PaneNavigation, PlatformFocus.
- `Theme/` - spacing, type and metric tokens. No colours.
- `Domain/` - provider-agnostic models: Repo, Commit, PullRequest, and so on.
- `Services/` - GitHubClient, GitService, NotificationService, AuthService.
- `ViewModels/` - per-screen state.
- `Platforms/` - the small amount that cannot be portable.

Being an ordinary desktop .NET process matters for more than tidiness.
Launching `git.exe` and reading repositories from arbitrary paths both need an
unsandboxed process, which is exactly what the UWP app model would have
forbidden.

The `Domain/` layer exists so that GitLab and Codeberg can later be added as
additional `Services/` implementations behind the same models. Screens never
reference a provider client directly. That separation is also what makes the
UI framework replaceable: when the shell moved from React Native to MAUI,
nothing below the view layer would have needed to change, had it existed yet.

### 4.2 Git engine: bundled git.exe, not libgit2

Three options were considered.

- **libgit2 via a native module.** In-process and structured, but it does not run
  hooks, does not support Git LFS, does not use the user's credential helpers,
  and lags upstream Git on newer features. It would also mean writing and
  maintaining a large C++ surface.
- **isomorphic-git in JavaScript.** There is no Node runtime and no `fs` in RNW,
  so it needs a native filesystem shim anyway, and it is slow on large
  repositories.
- **git.exe as a subprocess.** Full fidelity: hooks, LFS, submodules, credential
  helpers, worktrees, everything the user's own Git does. This is the approach
  GitHub Desktop takes, through dugite.

We take the third. `Services/GitProcess` is a thin wrapper over
`System.Diagnostics.Process` that spawns git.exe, streams stdout and stderr,
and reports exit codes. All parsing lives above it, in `Services/GitService`.

This is markedly simpler than it was going to be: the previous stack needed a
C++/WinRT native module and a JavaScript bridge to reach the same place. A
normal .NET process can just start a process.

Rules for that wrapper:

- Parse `--porcelain=v2` with `-z` NUL-delimited output. Never parse
  human-readable Git output; it is localized and unstable.
- Always pass `--no-optional-locks` for read operations, so that background
  refreshes never block the user's own use of Git elsewhere.
- Set `GIT_TERMINAL_PROMPT=0` so a credential prompt can never hang the process
  invisibly. Handle authentication failure as a returned error we can announce.
- Long operations stream progress, which `Announcer` converts into milestones.
- Ship a portable Git alongside the app rather than depending on a system
  install, with an opt-out setting for users who prefer their own.

### 4.3 GitHub API layer

- **GraphQL v4** for list and detail screens: one request per screen, fetching
  exactly the fields the screen renders. This matters for accessibility as much
  as for performance, because partial data arriving in waves causes re-renders
  that move focus and re-trigger announcements.
- **REST v3** where GraphQL has no equivalent, most notably the notifications
  endpoints.
- All responses normalize into `domain/` models at the service boundary. Screens
  never see a GitHub-shaped object.

### 4.4 Notifications

Desktop clients cannot receive webhooks, so this is polling, done correctly:

- `GET /notifications` with `If-Modified-Since`. A 304 response does not count
  against the rate limit.
- Honour the `X-Poll-Interval` response header as the minimum interval. Never
  poll faster, and back off on secondary rate limits.
- New items raise a Windows toast through `ToastModule`, and land in an in-app
  inbox that is itself a fully accessible list. The toast is a convenience; the
  inbox is the real surface, because toasts are transient and easy to miss.
- Toast activation routes through a protocol handler into the relevant screen,
  placing focus on the item rather than the window root.

Windows App SDK app notifications require package identity. GitApp therefore
ships with a sparse package, a decision that also constrains installer design.

### 4.5 Authentication and secrets

- **OAuth device flow** is the primary path: the user opens a browser and types a
  short code. There is no embedded web view. This is deliberate, because an
  embedded browser control inside RNW would be an accessibility dead end, and
  device flow also avoids shipping a client secret.
- **Personal access token entry** is the fallback, and the initial mechanism for
  other Git hosts.
- Tokens are stored in the Win32 Credential Manager, which works without
  package identity. MAUI's `SecureStorage` is not used on Windows because it
  needs identity and GitApp is unpackaged; other platforms keep it.
  See `Platforms/Windows/CredentialStore.Windows.cs`.
- Tokens never land in a file this app writes, never appear in a log line,
  and never reach an error message. The last one is not left to inspection:
  `Services/Redaction.cs` filters text on its way to the user, because the
  risky paths are the generic ones where some unanticipated exception is
  formatted and announced.

### 4.6 State management

MVVM with `CommunityToolkit.Mvvm`, and `ObservableCollection` for lists that
change under a bound control.

The accessibility-driven constraint is unchanged by the port: **updates must
not move focus or re-announce unchanged content.** In practice that means
mutating an existing `ObservableCollection` rather than replacing it, since a
wholesale replacement rebuilds every container and throws away both the focus
position and the screen reader's sense of place; raising `PropertyChanged`
only for properties that actually changed; and never reordering a list
underneath the user's cursor as the result of a background refresh.

## 5. Repository layout

- `GitApp.slnx` - the solution.
- `src/GitApp/` - the MAUI application.
  - `Accessibility/` - Announcer, FocusManager, Pane, PaneNavigation, PlatformFocus.
  - `Domain/` - provider-agnostic models.
  - `Services/` - GitHubClient, GitService, NotificationService, AuthService.
  - `Theme/` - spacing, type and metric tokens.
  - `Platforms/Windows/` - the F6 key hook and the focus helpers.
  - `Platforms/MacCatalyst/` - the macOS equivalents, once Catalyst is verified.
- `src/GitApp.Core/` - Domain and Services, with no UI framework dependency,
  so the logic is testable and survives a change of shell.
- `docs/` - this specification, the MAUI spike, and the developer notes.
- `tests/GitApp.Core.Tests/` - unit tests over the parsing layer.

High contrast is a first-class theme rather than an afterthought. MAUI
supplies it, and the theme rule in 3.8 is what keeps it working.

## 6. Milestones

1. **Foundations.** Done. App shell, F6 pane model, FocusManager, Announcer,
   and the platform focus helpers.
2. **Local repositories.** Done. Add, remove and clone repositories, status,
   stage, unstage, commit, fetch, pull, push, history, branch switching and
   creation, conflict resolution, and the diff viewer with folding and a
   context setting, all against real repositories through git.exe.
3. **GitHub read.** Auth, repository browse, issues, pull requests, code
   view. Sign-in, the remote repository list (`docs/GITHUB.md`), the
   repository screen and file view (`docs/REPOSITORY-VIEW.md`), and
   issues and pull requests (`docs/ISSUES.md`) are built.
4. **GitHub write.** Comment, review, merge, release management.
   Commenting, close and reopen, merge, and whole-pull-request review
   are built (`docs/ISSUES.md`); the releases list and creating a
   release are built (`docs/RELEASES.md`). None of it has yet been
   exercised against a real repository.
5. **Notifications.** Polling, toasts, inbox. The polling and model layer
   is built and unit-tested in `GitApp.Core`. The Windows toast, the
   background poll loop and the in-app inbox were heard in NVDA on an
   unpackaged Windows build; toast activation and Open in browser were
   not. The checklist is in `docs/NOTIFICATIONS.md`.
6. **Beyond GitHub.** GitLab and Codeberg behind the existing domain models.

Pull still fast-forwards first, because a fast-forward cannot conflict and so
can never leave the user somewhere they did not ask to be. When that is
refused, the app offers a merge rather than performing one, and says that
conflicts may follow.

Conflict resolution names each side by its branch, "Keep main" and
"Keep feature", never "ours" and "theirs". Those two words are ambiguous even
to people who use git daily, and worse heard than read: during a merge
"ours" is the branch you are on, but during a rebase it is the opposite.

## 7. Open questions

- **Mac Catalyst is entirely unverified.** No Apple hardware is available to
  this project. F6 has no implementation there and is the wrong gesture for
  macOS regardless; region navigation will need a VoiceOver-native answer.
- **Diff presentation** has a first implementation and its own design
  document, `docs/DIFF-VIEWER.md`. The remaining questions there are real:
  intra-line changes, very large hunks, and how much context to read.
- **Large repositories are untested.** Status parsing is linear, but a
  working tree with thousands of changes has not been tried, and neither has
  a history longer than the thirty commits currently requested.
- **Whether to bundle Git.** Currently GitApp calls whatever `git` is on
  PATH, and says so plainly when it cannot find one. Bundling removes a
  prerequisite at the cost of installer size.
