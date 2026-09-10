# The diff viewer

Status: implemented, 2026-09-11

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
| Unchanged, same number | `{content}, unchanged line 12` |
| Unchanged, renumbered | `{content}, original line 12, modified line 15` |
| Added | `{content}, added, modified line 15` |
| Removed | `{content}, removed, original line 12` |
| Empty line | `blank, added, modified line 15` |
| Binary file | `{name}, binary file, no text diff available` |

Pluralisation is real: "1 line changed", not "1 lines changed".

## Navigation

- **Up and Down** move through every row, headers included, so the diff can
  be read straight through.
- **F7 and Shift+F7** jump to the next and previous hunk header, matching VS
  Code. F6 is already pane cycling, so there is no clash.
- The diff is a pane like any other, reachable with F6.

Set positions are reported across the whole flat list, so a listener always
knows how far through they are, not just within the current hunk.

## Open questions

- **How much context.** Currently three lines, git's default. More context
  helps orientation and costs listening time. This should probably become a
  setting, but a default has to be chosen and three is defensible.
- **Intra-line differences.** When one word changes on a long line, the
  viewer says the whole line twice, once as removed and once as added, and
  the listener has to spot the difference themselves. VS Code has the same
  problem. Announcing character-level changes is possible and might be worse:
  "line 12, changed, word 4 from foo to bar" is precise and hard to follow.
  Unresolved.
- **Very large hunks.** A 400-line hunk is technically navigable and
  practically not. Some form of summarise-then-drill-in is probably needed.
- **Word wrap and long lines.** A 300-character line read in one breath is
  its own problem, separate from diffing.
