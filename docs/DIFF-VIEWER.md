# The diff viewer

Status: implemented, 2026-09-11. Folding and the context setting added the same day.

Diff presentation is the hardest accessibility problem in a Git client, and
the one every existing client handles badly. This document is the design, and
the reasoning, so the decisions can be argued with later.

## The problem

A diff is two-dimensional. Side-by-side panes, colour for added and removed,
alignment carrying the relationship between the halves. Speech is
one-dimensional and has no colour. Reading a diff aloud in source order
produces something unusable: the listener cannot tell which side a line came
from, cannot tell where one change ends and the next begins, and has no sense
of how much is left.

So the viewer's job is to linearise the diff without losing the three things
the shape was carrying: **what kind of line this is**, **where it sits**, and
**how much change there is**.

## Prior art: VS Code's Accessible Diff Viewer

VS Code solves this well, and it is the model worth copying. Pressing F7 in a
diff editor opens a linear view of the unified diff, navigated with Up and
Down, with each row given an aria-label.

Read from `src/vs/editor/browser/widget/diffEditor/components/accessibleDiffViewer.ts`:

| Row | Announcement |
| --- | --- |
| Hunk header | `Difference {i} of {n}: original line {a}, {N} lines changed, modified line {b}, {M} lines changed` |
| Unchanged | `{content} unchanged line {n}` |
| Added | `+ {content} modified line {m}` |
| Deleted | `- {content} original line {o}` |
| Empty line | the word `blank` |

The container is labelled "Accessible Diff Viewer. Use arrow up and down to
navigate." Enter returns to the editor at that line.

Three things it gets right, which we copy outright:

1. **Position in the whole**, on every hunk header. "Difference 2 of 5" is
   the single most useful thing to say, because it is what the scrollbar was
   telling a sighted reader.
2. **Line numbers on every row**, and the *pair* of numbers on unchanged
   lines when they differ. That is what lets a listener map what they hear
   back onto the file.
3. **Speaking the word "blank"** for an empty line. Silence is
   indistinguishable from a line that did not get read.

## Where we deviate, and why

**Words rather than `+` and `-`.**

VS Code prefixes added and deleted lines with the literal characters `+` and
`-`. At default punctuation levels NVDA does not speak either of them. So the
distinguishing marker the user *sees* is not one they *hear*: an added line
and an unchanged line differ only in the phrase at the end, "modified line
12" against "unchanged line 12".

That is a fragile way to carry the most important bit of information on the
row, and it depends on a screen reader setting the app does not control. It
is the same reasoning that keeps arrow glyphs out of the sync summary
elsewhere in this app.

So we say the word.

**Content first, classification after.**

Given the observation above, the experience a VS Code user actually gets is
content first with the type at the end, since the symbol is silent. That
ordering turns out to be the right one anyway, for a reason worth stating:
the alternative, leading with "added" on every row, means twenty consecutive
added lines all open with the same word, and the listener waits through it
every time to reach the part that differs.

Content is what distinguishes one row from the next, so content leads. This
is the same rule the rest of the app follows for list rows
(`docs/ARCHITECTURE.md` 3.3).

## The announcements

| Row | Announcement |
| --- | --- |
| Hunk header | `Difference 2 of 5, original line 154, 12 lines changed, modified line 159, 39 lines changed` |
| Folded hunk header | `Difference 7 of 7, collapsed, original line 527, 18 lines changed, modified line 588, 89 lines changed` |
| New file | `Difference 1 of 1, nothing in the original, modified line 1, 105 lines changed` |
| Unchanged, same number | `{content}, unchanged line 12` |
| Unchanged, renumbered | `{content}, original line 12, modified line 15` |
| Added | `{content}, added, modified line 15` |
| Removed | `{content}, removed, original line 12` |
| Empty line | `blank, added, modified line 15` |
| Binary file | `{name}, binary file, no text diff available` |

Pluralisation is real: "1 line changed", not "1 lines changed".

A side with no lines at all is named rather than counted at zero. A new
file's header used to open "original line 0, no lines changed", which is
both untrue and confusing, and leaving the listener to infer the absence
from a missing clause is the one thing speech cannot do.

## Large differences fold

A four-hundred line hunk is technically navigable and practically not: it is
four hundred keypresses to find out whether the next difference was worth
reading. So a hunk longer than **40 lines** starts folded, and its header
says so.

Folding is modelled on a tree, not on a separate summary view, because the
tree is the interaction Windows users already have — it is what the Settings
app's expanders do, and what a screen reader already knows how to report.
The folded hunk stays in the same flat list, in the same place, contributing
its header. Reading the shape of a whole file is then a few Down presses
over the headers, and drilling in is one more key.

| Key | Effect |
| --- | --- |
| Enter, Space | Fold or unfold the difference under the cursor |
| Right | Unfold |
| Left | From inside a difference, go out to its header. From the header, fold it |

Left taking two steps matters more than it looks. Without the first step,
getting out of a four-hundred line block means arrowing back up through all
of it — the exact problem folding exists to solve.

Three consequences of doing it in the flat list:

- **The difference numbers do not renumber.** "Difference 7 of 7" is the
  seventh difference in the file whether or not the six before it are
  folded. Only the set positions move, and those describe the rows actually
  present, which is what they should describe.
- **F7 still lands on every difference**, folded or not, so the jump keys
  and the fold state are independent.
- **The opening summary counts what is hidden**: `MainViewModel.cs, 7
  differences, 137 lines added, 5 lines removed, unstaged, 1 large
  difference collapsed`. Learning there is hidden content at the start beats
  discovering it by arriving at a header the list stops after.

Folding is a reading position, not a property of the file, so it resets when
a different file is shown.

## How much context

Three lines, git's own default, adjustable in the diff pane from 0, 3, 6, 12
or 25. Discrete choices rather than a free number, so the control is one
arrow press per step and reads as a short list rather than a text field.

This is an accessibility setting, not a cosmetic one. Context is what it
costs to listen to a diff, and the right answer is genuinely different for
someone reading at 200 words a minute and someone reading at 700. It
persists in `settings.json` beside the repository list.

## Navigation

- **Up and Down** move through every row, headers included, so the diff can
  be read straight through.
- **F7 and Shift+F7** jump to the next and previous hunk header, matching VS
  Code. F6 is already pane cycling, so there is no clash.
- The diff is a pane like any other, reachable with F6.

Set positions are reported across the whole flat list, so a listener always
knows how far through they are, not just within the current hunk.

## Open questions

- **Intra-line differences.** When one word changes on a long line, the
  viewer says the whole line twice, once as removed and once as added, and
  the listener has to spot the difference themselves. VS Code has the same
  problem. Announcing character-level changes is possible and might be worse:
  "line 12, changed, word 4 from foo to bar" is precise and hard to follow.
  Unresolved.
- **Word wrap and long lines.** A 300-character line read in one breath is
  its own problem, separate from diffing.
- **Expand all.** With several folded differences, opening each one is a
  keypress apiece. Tree views use `*` on the numeric keypad for this, which
  is discoverable to almost nobody. No key chosen yet.
- **Is 40 lines the right threshold?** It is a judgement, not a measurement:
  below it, deciding to unfold costs more than arrowing through. It is
  stored as a setting but has no control yet, because a number nobody can
  reach is not yet a preference.

## Announce nothing the screen reader will already say

Folding was listened to through NVDA 2026.2 on 2026-09-11, and that caught
something the UI Automation tree cannot show: **every step spoke twice.**

```
Difference 1 of 1, expanded, original line 100, 26 lines changed, modified line 100, 66 lines changed  1 of 87
Difference 1 of 1 expanded, 86 lines
```

The first line is NVDA reading the row. The second is ours. The tree looks
identical either way, because the duplication is in time, not in structure.

The cause was a wrong prediction, written into a comment before it was
tested: that a row which is already focused will not be re-read when only
its name changes. NVDA does re-read it. And on F7, where focus genuinely
moves, it was always going to.

So the rule is now explicit, and it applies beyond the diff viewer:

> Announce only what focus will not announce. If focus lands somewhere, or
> the focused element's own name changes, the screen reader reads it. An
> announcement on top of that is the same sentence twice.

`MoveToDiffRow` announces only when `TryFocusSelectedItem` returns false,
which happens when virtualization has left the row without a container and
nothing would be read at all. The explicit announcements that remain are the
ones where nothing moves: "No more differences" at the ends of the file.

There is no ExpandCollapse pattern on the header rows. MAUI's CollectionView
gives no way to supply one without replacing the item container, so the
state is carried as the words "collapsed" and "expanded" in the name. That
turns out to be enough: it is what a screen reader would say from the
pattern anyway, and NVDA re-reads the name on change, which is exactly the
event the pattern would have raised. If another screen reader does not, the
fix is a custom automation peer on the Windows item container.

One known wart: pressing F7 twice past the last difference speaks once and
then goes quiet, because the announcer drops identical consecutive
messages. That rule exists to stop re-renders repeating themselves, and a
deliberate second keypress is not a re-render.
