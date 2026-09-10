# Spike: does .NET MAUI carry the accessibility weight?

Date: 2026-09-10
Verdict: yes, and by a wide margin on Windows. The open issue that prompted
this spike does not reproduce.

## Why this was run

Two things forced the question. First, the window activation regression in
RNW Fabric (`docs/ARCHITECTURE.md` 2.3a, upstream issue 16435). Second, and
more decisive, macOS support and a single codebase became stated goals, and
`react-native-macos` is at 0.81.9 while RNW is at 0.84, so one RN codebase
cannot target both today.

Before recommending a move, the specific risk had to be tested rather than
argued: `dotnet/maui#27622`, "[Accessibility] Windows CollectionView Keyboard
Navigation", open since February 2025. `CollectionView` is where GitApp lives.

The spike is a MAUI 10 page mirroring the RNW shell: a named sidebar holding a
list of repositories, a detail pane with three buttons, and a status line.
Measured with the same UI Automation client used on the RNW build.

## Results

### The UIA tree

```
Window 'MAUI A11y Spike'
  Pane 'AppWindow Custom Title Bar'
  Pane
    Custom
      Group 'Repositories'
        Text 'Repositories'
        List 'Repositories'
          ListItem 'gitapp, main, 2 ahead' [focusable] [FOCUSED]
          ListItem 'react-native-windows, main, 14 behind' [focusable]
          ListItem 'nvda, master, up to date' [focusable]
          ListItem 'dotfiles, main, 1 ahead, 3 behind' [focusable]
      Group 'Repository details'
        Text 'gitapp'
        Text 'On branch main, 2 ahead'
        Button 'Fetch' [focusable]
        Button 'Pull' [focusable]
        Button 'Push' [focusable]
      Text 'Status'
```

### Patterns and properties, unprompted

Every list item reported `PositionInSet` 1 to 4 and `SizeOfSet` 4, with
`SelectionItem` and `ScrollItem` patterns available. The container reported
`Selection`, `Scroll` and **`ItemContainer`**.

`ItemContainer` is the pattern Fabric does not implement at all, and it is
what lets a screen reader reach items that are virtualized out of view. On
RNW this is section 2.3's second row: a documented gap with a workaround. Here
it is simply present.

### Keyboard navigation, the thing being tested

Focus tracked through a real UIA client while sending keystrokes:

```
start        : ListItem 'gitapp, main, 2 ahead'  (1 of 4)
after Down   : ListItem 'react-native-windows, main, 14 behind'  (2 of 4)
after Down   : ListItem 'nvda, master, up to date'  (3 of 4)
after Up     : ListItem 'react-native-windows, main, 14 behind'  (2 of 4)
after End    : ListItem 'dotfiles, main, 1 ahead, 3 behind'  (4 of 4)
after Home   : ListItem 'gitapp, main, 2 ahead'  (1 of 4)
after Tab    : Button 'Fetch'
after Tab    : Button 'Pull'
```

Arrow keys move within the list, Home and End work, and Tab leaves the list
rather than walking through every row. That last line is the single-tab-stop
invariant `tests/a11y/List.test.tsx` exists to defend. **Issue 27622 does not
reproduce in MAUI 10.**

Initial focus also lands on the first item with no code, which is the fix
that took a deferred effect and a split entry/container slot on RNW.

### Theme

The spike rendered in dark mode with zero configuration. On RNW this needed
three hand-written palettes and a custom hook, because no `PlatformColor`
name returns dark values under Fabric (`docs/ARCHITECTURE.md` 3.8).

### What NVDA actually says

Captured from NVDA 2026.2's Speech Viewer, read out of its edit control.

Activating the window:

```
MAUI A11y Spike
Repositories  grouping
Repositories  list
gitapp, main, 2 ahead  1 of 4
```

Arrowing down twice, then Tab:

```
react-native-windows, main, 14 behind  2 of 4
nvda, master, up to date  3 of 4
Repository details  grouping
Fetch  button  Downloads new commits without changing your working tree
```

Two things to draw out.

**Set positions are spoken.** "1 of 4", "2 of 4", "3 of 4", with no code on
our side. This is the behaviour the RNW `List` primitive computes by hand.

**The activation announcement is largely intact.** Compare against the same
measurement on the other three apps:

| App | NVDA on activation |
| --- | --- |
| Gallery (Legacy), Paper/UWP | title, "window", focused control |
| Gallery, RNW Fabric | title only |
| GitApp, RNW Fabric | title only |
| This spike, MAUI 10 | title, containing group, list, **focused control with its position** |

MAUI does not say the word "window", so it is not identical to the legacy
UWP behaviour. But it announces the full context chain down to the focused
item, which is the part of the regression that actually leaves a screen
reader user stranded. Prediction made before measuring was that MAUI would
behave like Fabric here. That was wrong.

## What it costs to hand-build the same thing

| | RNW Fabric | MAUI 10 |
| --- | --- | --- |
| List and ListItem control types | `accessibilityRole` per row | free from `CollectionView` |
| PositionInSet / SizeOfSet | computed by hand, `totalCount` threaded through | free |
| SelectionItem pattern | absent; approximated with `accessibilityState.selected` | free |
| ItemContainer pattern | absent, no workaround | free |
| Roving tabindex, single tab stop | hand-written | free |
| Arrow keys, Home, End, Page Up/Down | hand-written | free |
| Type-to-select | hand-written | not yet verified |
| Initial focus | hand-written | free |
| Dark mode and high contrast | three palettes plus a hook | free |

Line count for the equivalent surface: **1,547 lines** in `src/a11y` and
`src/theme` against **134 lines** of XAML and code-behind in the spike.

## Caveats, stated plainly

- **The activation bug is mostly fixed, contrary to the prediction made
  before this was measured.** See the NVDA section above. The word "window"
  is still absent, because the MAUI window is `WinUIDesktopWin32WindowClass`
  rather than a UWP `ApplicationFrameWindow`. But the substantive half, the
  focused control being announced on activation, works. That was the part
  worth caring about.
- **macOS means Mac Catalyst**, not native AppKit. Native macOS remains a
  discussion upstream. A Catalyst app is an iOS UI on the Mac and VoiceOver
  treats it that way. This should be tested on real hardware before macOS is
  promised to anyone.
- Not yet tested: trees, grids for commit and diff views, rich text editing
  for comments, type-to-select, and the F6 pane model.
- MAUI has around 30 open issues under `t/a11y`. Many are Microsoft's own
  conformance passes filing their findings, which is a healthier signal than
  it first appears, but the count is not zero.

## Recommendation

Port to MAUI. The accessibility substrate this project would otherwise own
forever is supplied by the framework and, where measured, it is correct. The
non-UI layers in `domain/`, `services/` and `state/` are unwritten, so the
cost of moving is the shell and roughly 1,500 lines that mostly stop being
necessary.

Most of `docs/ARCHITECTURE.md` survives: section 1, the naming contract in
3.3, the focus model in 3.4, the announcement rules in 3.5, and all of
section 4. Section 2 is replaced by a MAUI capability audit, of which this
document is the first half.
