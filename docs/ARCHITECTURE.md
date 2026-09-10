# GitApp Architecture and Accessibility Specification

Status: draft 1, 2026-09-10
Target stack: React Native Windows 0.84 (Fabric), Hermes, C++/WinRT native modules

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

Non-goals for v1: macOS and Linux, a built-in diff editor, Git hosting providers
other than GitHub. The abstraction is designed for other hosts, but only GitHub
ships in v1.

## 2. Stack decision record

### 2.1 The decision

React Native Windows 0.84 on the Fabric architecture. RNW 0.82 removed the
legacy Paper renderer entirely, so Fabric is the only option and all research
below targets it.

Fabric does not render XAML controls. It composes visuals directly with Windows
Composition and implements UI Automation itself, in
`CompositionDynamicAutomationProvider`. Practically: **no control we render
comes with accessibility attached.** Every announcement, pattern, and keyboard
behaviour is something we assert explicitly in props. This is the central fact
that shapes the rest of this document.

### 2.2 What Fabric's UIA provider actually gives us

Verified by reading the provider source rather than the documentation, which
lags. Source: `vnext/Microsoft.ReactNative/Fabric/Composition/`.

Patterns implemented: Invoke, Value, RangeValue, Toggle, ExpandCollapse,
Selection, SelectionItem, Scroll, ScrollItem, Text, Annotation.

Properties implemented: Name, FullDescription, HelpText, ControlType,
AutomationId, AccessKey, HeadingLevel, Level, PositionInSet, SizeOfSet,
ItemStatus, ItemType, LiveSetting, IsEnabled, IsKeyboardFocusable,
HasKeyboardFocus, IsOffscreen, IsContentElement, IsControlElement.

Control types reachable through `accessibilityRole` and `role`: button, checkbox,
combobox, hyperlink, image, list, listitem, menu, menubar, menuitem, progressbar,
radio, scrollbar, spinbutton, splitbutton, tab, tablist, textinput, toolbar,
**tree, treeitem**, pane, group, text, slider, separator, statusbar, header,
document, window, custom.

The presence of Tree and TreeItem control types, plus ExpandCollapse, Level,
PositionInSet and SizeOfSet, means a correct screen-reader tree **is** buildable
in RNW. It is not free, but it is not blocked either. The same applies to lists
reporting "item 7 of 240".

### 2.2a Which role prop to use

`accessibilityRole` and `role` are not aliases. They resolve through two
separate native functions, `GetControlTypeFromString` and
`GetControlTypeFromRole` respectively, and neither covers everything. Choosing
the wrong one degrades silently to a Group with no warning at build or run time.

- Use **`accessibilityRole`** for: `pane`, `tree`, `treeitem`, `keyboardkey`,
  `splitbutton`, `toolbar`, `menubar`, `list`, `listitem`, and the ordinary
  control roles.
- Use **`role`** for: `status` (the only route to UIA StatusBar), `table`,
  `application`, `document`, `separator`, `columnheader`, `rowheader`.

The TypeScript surface lags the native mapping in at least one place:
`accessibilityRole="pane"` is accepted natively but missing from the shipped
union, so it needs a documented cast. `src/a11y/PaneHost.tsx` carries the only
one; do not add more without verifying against the provider source.

### 2.3 What is missing, and what we do about it

| Gap | Consequence | Mitigation |
| --- | --- | --- |
| No `IGridProvider`, `ITableProvider` or `IGridItemProvider` | `role="table"` sets a control type but screen readers get no row and column navigation. Commit lists and file tables cannot be real grids. | Model tabular data as a list of rows whose accessible name concatenates the columns, with column headers carried in `accessibilityDescription`. Where a true grid is required, use a XAML island (section 3.6). |
| No `IItemContainerProvider` or `IVirtualizedItemProvider` | Screen readers cannot reach items scrolled out of a virtualized list. | Set `accessibilityPosInSet` and `accessibilitySetSize` from the full dataset length, not the rendered window, so counts are honest. Keep virtualization windows generous and page explicitly rather than scrolling infinitely. |
| `UIA_LabeledByPropertyId` is not implemented, although `accessibilityLabelledBy` exists in the TypeScript surface | The prop silently does nothing. | **Never use `accessibilityLabelledBy`.** Compose the full string into `accessibilityLabel`. Enforced by lint rule. |
| No LandmarkType or LocalizedLandmarkType | No landmark navigation between regions. | Provide our own region navigation: F6 pane cycling (section 3.4), with each pane given `role="pane"` and a spoken name. |
| No `IWindowProvider`, no native menu bar | Dialogs are not natively modal and menus are not native menus. | Dialogs use `accessibilityViewIsModal` plus a manual focus trap. The menu bar is hand-built on the menubar, menu and menuitem control types with ExpandCollapse and Invoke. |
| UIA ItemStatus is hardcoded to `Busy` or empty, from `accessibilityState.busy` alone | No arbitrary per-item status text, so "syncing", "3 conflicts ahead" and similar cannot ride on the item itself. | Use `accessibilityState.busy` for the binary case, `accessibilityValue.text` where the control has a real value, and otherwise a live region. See 3.3. |
| `accessibilityRole` and `role` resolve through two different native functions with different coverage | Neither prop is a superset. `accessibilityRole` handles `pane`, `tree` and `treeitem`, which `role` lacks; `role` reaches `status` (UIA StatusBar), `table` and `application`, which `accessibilityRole` lacks. An unmatched `accessibilityRole` silently degrades to a Group. | Pick per control against the table in 2.2a, and never assume the two are interchangeable. The remaining `aria-*` aliases are still incomplete (upstream issue 11905, open), so use `accessibility*` props for everything except the roles listed as role-only. |
| WinUI Fluent brush names resolve to hardcoded light-theme values (upstream issue 11489) | `PlatformColor('TextFillColorPrimary')` and the rest of the Fluent palette return light colours in every theme, so the obvious modern choice fails in dark mode exactly like a hex literal. | Use only the `UIColorType` and `UIElementType` names, which are theme and high-contrast aware. See 3.8. A custom resource loader is the eventual route to real Fluent colours. |
| "Advanced Screen Reader Readability", upstream issue 11901, still open | Parts of N-of-M, HelpText, Description and Value handling are still in flux. | Pin the RNW version. Every upgrade runs the screen reader regression suite (section 3.7) before merge. |

### 2.4 Honest risk statement

RNW gives us a competent UIA substrate and a real escape hatch, but a large
share of this project's effort is accessibility infrastructure that WPF or
wxPython would have supplied for free. The mitigation is to build that
infrastructure once, as described in section 3, and then forbid feature screens
from doing accessibility by hand.

## 3. Accessibility architecture

### 3.1 The rule

Feature screens never set raw accessibility props. They compose primitives from
`src/a11y/` that have the correct UIA semantics baked in and tested. A screen
that reaches for `accessibilityRole` directly is a bug, caught by lint.

This is the only way the contract survives contact with feature work.

### 3.2 Primitive layer

`src/a11y/primitives/` exports, at minimum:

- `Button`, `ToggleButton`, `SplitButton`, `Link`
- `TextField`, `SearchField`, `ComboBox`, `Checkbox`, `RadioGroup`
- `List` and `ListRow`. Owns roving tabindex, type-to-select, PositionInSet and
  SizeOfSet from the total count, Home, End, Page Up, Page Down, and the
  Applications key opening the row context menu.
- `Tree` and `TreeNode`. Adds Level, ExpandCollapse, Left and Right arrow to
  collapse and expand, and asterisk to expand all siblings.
- `Tabs` and `Tab`. Tablist and tab control types, Ctrl+Tab and Ctrl+Shift+Tab,
  arrow keys within the strip.
- `MenuBar`, `Menu`, `MenuItem`. Alt to enter, mnemonics via
  `accessibilityAccessKey`, arrow navigation, Escape to leave with focus
  restored to the point of origin.
- `Dialog`. Focus trap, initial focus, `accessibilityViewIsModal`, Escape, and
  focus restoration on close.
- `Announcer`. The live region service, section 3.5.

Each primitive ships with a UIA tree snapshot test (section 3.7). The snapshot
is the contract.

### 3.3 Naming and description contract

Three distinct slots, used consistently across the whole app:

- `accessibilityLabel` is the identity of the control. Short, front-loaded with
  the distinguishing word, and never containing the control type, since the
  control type is already announced. For a list row, the full row content as one
  string, in reading order, comma-separated.
- `accessibilityDescription` is supplementary context a user may want but does
  not need every time. It maps to FullDescription. Column headers for a row,
  relative timestamps, CI status detail.
- `accessibilityHint` is what invoking the control does, and only appears when
  that is non-obvious.

Transient state such as "syncing" or "3 conflicts" needs care, because the
obvious slot is not available. UIA ItemStatus is **not** settable to arbitrary
text on Fabric: the provider hardcodes it to the literal string `Busy` or empty,
derived solely from `accessibilityState.busy`. There is no
`accessibilityItemStatus` prop. So:

- `accessibilityState.busy` for the binary in-progress case, which yields the
  standard "Busy" announcement.
- `accessibilityValue.text` for status text on a control that legitimately has a
  value, since it reaches the Value pattern.
- Otherwise a separate live region element (3.5), not the row's own label.

Never encode transient state in `accessibilityLabel`; it makes the label
unstable, and screen readers re-announce the whole label on every change.

### 3.4 Focus and navigation model

Windows conventions, implemented by us because nothing implements them for us:

- **Tab and Shift+Tab** move between controls and between composite widgets,
  never into every row of a list. Lists and trees are a single tab stop with a
  roving tabindex inside.
- **F6 and Shift+F6** cycle panes in order: sidebar, primary content, detail
  pane, status bar. Each pane announces its name on entry. This is our substitute
  for the missing landmark support and it is mandatory on every screen.
- **Arrow keys** move within a composite widget only.
- **Escape** backs out one level. It closes a menu, dialog, or filter, and always
  restores focus to the element that opened it.
- **Applications key and Shift+F10** open the context menu for the focused item.
  Every list row and tree node has one, and it duplicates every action available
  by mouse. This is the primary accessible action surface in the app.
- **Alt** enters the menu bar.

Focus restoration is centralized in a `FocusManager` service. Any code path that
destroys the focused element must have pushed a restore target first. Losing
focus to the window root is the single most common way a screen reader app
becomes unusable, and RNW will do it silently on re-render.

`view.focus()` works imperatively in Fabric as of 0.84; earlier versions required
native workarounds. That is one of the reasons the version floor is 0.84.

### 3.5 Announcements

`Announcer` wraps two mechanisms:

- `AccessibilityInfo.announceForAccessibility` for discrete events: "Pushed 3
  commits to origin/main", "Clone failed: authentication required".
- `accessibilityLiveRegion` on a status element for progress that updates in
  place, such as fetch and checkout progress. Use `polite` by default, and
  `assertive` only for errors that stop the user's current task.

Rules: no announcement fires more than once every 500 ms; progress announcements
report meaningful milestones rather than every percent; and every long-running
Git operation announces both start and completion, because silence reads as a
hang.

### 3.6 The XAML island escape hatch

`ContentIslandComponentView` lets us host a real WinUI control inside the Fabric
tree, and the Fabric UIA provider delegates through `ChildSiteLink` to the hosted
framework's own automation provider. A hosted WinUI control therefore brings its
complete, Microsoft-tested accessibility with it.

This is expensive, costing a C++/WinRT component per island plus a styling seam,
so it is reserved for cases where Fabric genuinely cannot express the semantics:

- A real data grid, if commit or file tables prove unusable as lists.
- The rich text editor for comment composition, if we need Text pattern support
  beyond what Fabric's `ITextProvider2` offers.

Decide case by case, and only after a screen reader test has proven the Fabric
version inadequate. Do not reach for islands by default; a codebase half in each
is worse than either one.

### 3.7 Verification

Accessibility is verified by tests, not by intention.

1. **Prop-level tree snapshots**, in `tests/a11y/`. These run under Jest and
   assert the accessibility props we declare: control type, name, description,
   set positions, and the single-tab-stop invariant. Fast, they gate CI, and
   they catch most regressions. They are not a UIA dump, and they say so.
2. **Live UIA snapshots**, against the running app. `microsoft/react-native-gallery`
   solves this with a C# test project that drives the deployed app through
   `System.Windows.Automation` and scans it with **Axe.Windows**, the engine
   behind Accessibility Insights, writing a committed JSON snapshot. That is
   the model to copy: it is the only way to see what a screen reader actually
   sees, including what Fabric decides not to put in the tree.

   Not yet built here, and the gap is not theoretical. Two bugs in the first
   shell were invisible to every prop-level test and to the type checker, and
   surfaced only on reading the live tree: `accessible={false}` on a container
   deleted both panes from the UIA tree, and a missing `accessible` removed
   the status bar and its live region. Both files read as correct.
3. **Keyboard reachability tests.** For each screen, assert that every
   interactive element is reachable by Tab and arrow keys from the pane root, and
   that every mouse action has a context-menu equivalent.
4. **Manual screen reader pass.** NVDA and Narrator, both, before any feature is
   called done. JAWS before each release. A per-screen checklist lives beside
   the screen's code.

CI gates on items 1 and 3. Items 2 and 4 gate the release.

### 3.8 Theming, dark mode and high contrast

Colour is an accessibility concern here, not a cosmetic one, and Fabric has a
trap in it that is worth stating precisely.

RNW 0.84 made `Text` colour theme-aware by default. Any hardcoded background
therefore breaks in the opposite theme: a white background plus a theme-aware
foreground is white-on-white in dark mode, which renders as a blank window with
no error anywhere. That is not a hypothetical; it is how the first shell
shipped.

**Every colour in the app is a `PlatformColor` from `src/theme`. No hex
literals.** A test enforces this (`tests/a11y/noHardcodedColors.test.ts`),
because the failure is invisible in whichever theme the developer happens to be
using.

Which platform colour names are safe is the non-obvious part. Fabric's
`Theme::TryGetPlatformColor` resolves in four tiers:

1. An app-registered custom resource loader.
2. `UISettings.GetColorValue(UIColorType)` — theme aware. `Accent`,
   `Background`, `Foreground`.
3. `UISettings.UIElementColor(UIElementType)` — theme aware **and high
   contrast aware**, because the system returns the active HC palette.
   `Window`, `WindowText`, `ButtonFace`, `ButtonText`, `Highlight`,
   `HighlightText`, `GrayText`, `Hotlight`, and the rest of the classic set.
4. WinUI Fluent brush names, aliased to a table of **hardcoded light-theme
   values** (upstream issue 11489).

Tier 4 is the trap, and it is the tier that looks correct.
`TextFillColorPrimary`, `ControlFillColorDefault` and `SolidBackgroundFillColorBase`
are exactly what a WinUI XAML app would use, and under Fabric they return light
values in every theme.

The conclusion, verified by running the app rather than by reading the table:
**no `PlatformColor` name returns dark-theme values.** Tier 4 is hardcoded
light, and the tier 3 `UIElementType` names are the classic Win32 system
colours, which track high contrast but not the Windows 11 light/dark setting.
An app built entirely on platform colours is therefore permanently light.

So GitApp uses three palettes, selected by `useTheme()`:

- **High contrast** is entirely tier 2 and 3 platform colours. Under a high
  contrast theme the user has chosen those colours and the app must obey them.
  This palette takes priority over everything, including an explicit light or
  dark preference.
- **Light** and **dark** are Fluent's own values, written out in
  `src/theme/palette.ts` and selected with `useColorScheme()`. That file is the
  one place in the app allowed to name a colour, and a test enforces it.
- The accent is `PlatformColor('Accent')` in all three, since tier 2 resolves
  it correctly in every theme.

Selection always uses a pair, `selected` with `selectedText`. High contrast
redefines both, and using one with a colour of our own produces unreadable
rows.

The native title bar is separate from all of this: it is drawn by DWM, not by
Fabric, so it needs `DWMWA_USE_IMMERSIVE_DARK_MODE` set on the window handle at
startup. Without it a dark app keeps a light title bar, which is the most
visible way a Windows app looks unfinished. `windows/gitapp/gitapp.cpp` does
this from the same UISettings signal the JS theme uses, so the two cannot
disagree.

Replacing the literals with real Fluent resources means registering a custom
resource loader natively, at tier 1. That is a reasonable later project and is
not worth blocking feature work on.

## 4. System architecture

### 4.1 Layers

JavaScript, running on Hermes:

- `screens/` — feature UI, composed only from accessibility primitives.
- `a11y/` — primitives, FocusManager, Announcer, keymap registry.
- `domain/` — provider-agnostic models: Repo, Commit, PullRequest, and so on.
- `services/` — GitHubClient, GitService, NotificationService, AuthService.
- `state/` — Zustand stores and the TanStack Query cache.

Native, as C++/WinRT turbo modules:

- `GitModule` — hosts the git.exe process.
- `CredentialModule` — Windows Credential Manager.
- `ToastModule` — Windows App SDK app notifications.
- `ShellModule` — file pickers, Explorer integration, protocol handler.

The `domain/` layer exists so that GitLab and Codeberg can later be added as
additional `services/` implementations behind the same models. Screens never
import a provider client directly.

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

We take the third. `GitModule` is a thin C++/WinRT turbo module that spawns
git.exe, streams stdout and stderr, and reports exit codes. All parsing lives in
TypeScript, in `services/GitService`.

Rules for that module:

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
- Tokens are stored through `CredentialModule` using the Win32 Credential Manager
  API, which works without package identity. Tokens never touch `AsyncStorage`,
  never appear in logs, and are redacted from error reports.

### 4.6 State management

Zustand for app state, TanStack Query for the server cache. Redux was rejected as
ceremony this project does not need.

The accessibility-driven constraint on state is that **re-renders must not move
focus or re-announce unchanged content.** In practice that means stable list item
keys derived from server IDs, selectors narrow enough that a background refresh
does not re-render a focused row, and optimistic updates that never reorder a
list underneath the user's cursor.

## 5. Repository layout

- `src/a11y/` — primitives, FocusManager, Announcer, keymap.
- `src/screens/` — one directory per screen, each with its accessibility checklist.
- `src/domain/` — provider-agnostic models.
- `src/services/` — GitHubClient, GitService, NotificationService, AuthService.
- `src/state/` — stores and query definitions.
- `src/theme/` — design tokens derived from github.com, for light, dark and high contrast.
- `windows/` — C++/WinRT native modules and the RNW app project.
- `docs/` — this file and its successors.
- `tests/a11y/` — prop-level accessibility snapshots, the theme-discipline
  check, and keyboard reachability tests.
- `tests/services/` — Git porcelain parsing fixtures and API contract tests.

High contrast is a first-class theme rather than an afterthought: it is one of
the `theme/` targets and it appears in the visual review checklist.

## 6. Milestones

1. **Foundations.** RNW 0.84 app shell, F6 pane model, menu bar, FocusManager,
   Announcer, and the UIA snapshot test harness. Nothing else. This milestone
   exists to prove the accessibility substrate before any feature depends on it.
2. **Local repositories.** GitModule, clone, status, stage, commit, push, pull,
   branch switching. Verified with NVDA end to end.
3. **GitHub read.** Auth, repository browse, issues, pull requests, code view.
4. **GitHub write.** Comment, review, merge, release management.
5. **Notifications.** Polling, toasts, inbox.
6. **Beyond GitHub.** GitLab and Codeberg behind the existing domain models.

## 7. Open questions

- Does a commit list built as a `list` of composite rows read acceptably in NVDA
  and JAWS, or does it need a XAML island grid? Resolve with a prototype during
  milestone 1, before the pattern is used in twenty places.
- Diff presentation for screen readers is genuinely unsolved in every Git client,
  FastGH included. It deserves its own design document rather than a paragraph
  here.
- Whether to bundle Git or require it. Bundling is assumed above, but installer
  size may argue otherwise.
