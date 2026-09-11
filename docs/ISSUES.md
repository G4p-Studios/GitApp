# Issues and pull requests

Status: built and verified with a real account, 2026-09-11. Part of
milestone 3. Read only: commenting, reviewing and merging are milestone 4.

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

Two panes: Conversation and About.

The conversation is a document, not a list. The title is heading 1, each
comment is heading 3 under a heading 2 "Comments", and the bodies are the
same Markdown document as the README (`docs/REPOSITORY-VIEW.md`): arrow
keys move a caret, links stay in the sentence as hyperlinks. Body headings
are shifted down one level so they cannot outrank the title.

About is the github.com sidebar, one fact per control: state, labels,
assignees, milestone, and for a pull request the branches, commit count,
diffstat, and whether it can merge. "No one assigned" and "No milestone"
are said, because an empty sidebar is indistinguishable from a missing one.

Review threads, files changed as a list, and the timeline of label-and-
assignment events are not in this slice. The conversation and the merge
facts are what you open a pull request to read. Writing a comment or a
review is milestone 4.

## GraphQL

One request for a page of the list, one request for a conversation. REST
`/issues` would mix pull requests into the issue list and still need a
second call for comments. GraphQL `issue(number)` does not return pull
requests, so the two kinds stay on separate screens the way github.com
keeps them on separate tabs.
