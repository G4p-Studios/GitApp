# Issues and pull requests

Status: reading built and verified with a real account, 2026-09-11
(milestone 3). Commenting, closing and reopening, merging, and whole-
pull-request review are built (milestone 4). The comment pane has been
seen to read correctly live; the write actions have not yet been
exercised against a real repository.

From the repository screen, Issues and Pull requests are their own lists,
the way they are tabs on github.com. Putting them in the Files/Readme/About
F6 cycle would mean a pane you have to pass through to reach the README,
and a list of issues is not a fact about the files you are looking at.

Escape backs out one level: conversation to list, list to repository.

## What a row has to say

github.com's issue table is a grid. Title, number, labels, author and age
sit in columns, and speech has no columns. So a row is one name, content
first, the same rule as every other list (`ARCHITECTURE.md` 3.3).

| Row | Announcement |
| --- | --- |
| Open issue | `Fold large differences, open, accessibility, by alexoloopios, 3 comments, 5 hours ago, issue 17` |
| Closed issue | `Sign in with a token, closed, 1 day ago, issue 4` |
| Draft PR | `Port to MAUI, open, draft, maui into main, work-in-progress, by alexoloopios, 2 comments, 5 hours ago, pull request 12` |

The title leads, because that is what distinguishes one row from the next.
Leading with "issue" would make every row open with the same word. The
number is last, the way a commit hash is last: confirmation, not the scan
target. "Pull request" is the full phrase; "PR" is three letters a screen
reader spells.

Zero comments are omitted rather than spoken. "No comments" on every quiet
issue is the same tax as leading with the kind.

## The list

Two panes, Filter then List.

Filter is first because Open against Closed changes what every row means,
and because with a few hundred issues arrowing is not a way to find one.
The filter matches title, number, author, label, and for pull requests the
branch names. The count is announced, the same as the repository list.

GitHub's issue list can be thousands of closed items. Paging to exhaustion
the way the repository list does would look like a hang. We fetch the 200
most recently updated in the chosen state, and if more exist the summary
says so: `Showing 200 of 1043 closed issues. More exist; type to filter.`
A truncated list that claims to be complete is the failure being avoided.

Pull requests have an extra state, Merged. Closed means closed without
merging; github.com's GraphQL distinguishes them and so do we.

Enter, Space, or a click opens the conversation.

## The conversation

Three panes: Conversation, Comment, About.

The conversation is a document, not a list. The title is heading 1, each
comment is heading 3 under a heading 2 "Comments", and the bodies are the
same Markdown document as the README (`docs/REPOSITORY-VIEW.md`): arrow
keys move a caret, links stay in the sentence as hyperlinks. Body headings
are shifted down one level so they cannot outrank the title.

Reviews are part of the conversation, in date order with the comments,
the way github.com's timeline shows "X approved these changes". The
verdict is in the heading: `reviewer approved, 2 hours ago`, `other
requested changes, 1 hour ago`. A review with no words and no verdict is
the shell GitHub wraps around line comments, which are not fetched, and
is left out rather than shown as "Empty comment".

About is the github.com sidebar, one fact per control: state, labels,
assignees, milestone, and for a pull request the branches, commit count,
diffstat, the review decision, and whether it can merge. "No one
assigned" and "No milestone" are said, because an empty sidebar is
indistinguishable from a missing one.

Review threads on lines, files changed as a list, and the timeline of
label-and-assignment events are not in this slice. The conversation and
the merge facts are what you open a pull request to read.

## Writing a comment

The comment box is its own pane, under the conversation where github.com
puts it, and before About in the F6 order so the cycle reads top to
bottom like the page. Its own pane because the alternative is arrowing
to the end of a long conversation to find the editor; F6 twice from the
title is the same distance on every issue.

The editor is a plain multi-line text field named "Comment", with the
hint "Markdown. Control Enter posts." Control+Enter posts from inside the
editor, the same key github.com uses, and only while the editor has focus:
from the conversation it would post something the user cannot see they
are posting. The Post comment button does the same for anyone who
prefers a button, and is disabled while the draft is blank or a post is
in flight.

Posting announces `Posting comment` and then `Comment posted. 3
comments.` The new comment is appended to the conversation from what the
mutation returned, not by reloading: a reload re-renders every comment
above it, and a re-render under a screen reader is a screen that went
quiet and started over. Focus stays in the now-empty editor.

A failed post announces GitHub's reason and leaves the draft exactly as
it was. The one thing worse than a comment that did not post is a comment
that did not post and is gone. The same rule governs Escape: with an
unposted draft, Escape asks "Leave without posting it?" in a dialog
rather than backing out silently. Leave and Stay are the two answers;
Stay is the default the dialog's own Escape gives.

The mutation is GraphQL `addComment`, keyed by the node id fetched with
the conversation. A fine-grained token needs the Issues and Pull
requests permissions set to read and write; without them GitHub replies
"Resource not accessible by personal access token", which is spoken as
is.

## Closing and reopening

One toolbar button whose name says which way it goes: "Close issue",
"Reopen pull request". No confirmation, matching github.com, because the
opposite action is one press of the same button. `Closing issue 17` then
`Issue 17 closed.`; the button's name flips, the metadata line and the
About state update. Merged pull requests hide the button: merged is
final.

## Merging

"Merge pull request" appears in the toolbar only when GitHub says the
branches combine (`mergeable: MERGEABLE`), the pull request is open and
not a draft, and the repository allows at least one merge method. Branch
protection is not checked up front; GitHub refuses the merge and its
reason, for instance a failing required check, is spoken as is.

Pressing it opens a choice of the allowed methods in github.com's own
words: Create a merge commit, Squash and merge, Rebase and merge. Only
the methods the repository permits are offered, so nothing is presented
that would fail on press. Choosing one is the confirmation; there is no
second "are you sure". Cancel announces `Merge cancelled.` so Escape out
of the dialog is not silence.

`Merging pull request 12 into main` then `Pull request 12 merged into
main.` The Merge and Close buttons both hide, so focus is placed on the
conversation deliberately rather than left wherever WinUI drops it.

## Reviewing

On an open pull request the comment pane has two more buttons beside
Post comment: Approve and Request changes. Both submit a review on the
whole pull request with the draft as its words. Approve may be wordless.
Request changes with an empty draft is refused here, `Write what needs to
change first, in the comment box.`, rather than sent to GitHub, which
would refuse it less clearly. GitHub's other refusals, such as approving
your own pull request, are spoken as is.

The review is appended to the conversation with its verdict in the
heading, the draft clears, the review decision in About updates, and
focus stays in the editor. Line-by-line review needs the pull request's
diff on screen and is not built.

## GraphQL

One request for a page of the list, one request for a conversation. REST
`/issues` would mix pull requests into the issue list and still need a
second call for comments. GraphQL `issue(number)` does not return pull
requests, so the two kinds stay on separate screens the way github.com
keeps them on separate tabs.
