# Releases

Status: built, not yet exercised against a real repository. Part of
milestone 4.

## Where it lives

Releases open from the About pane of the repository screen, where the
"3 releases" fact is a button. That is where github.com puts them: the
sidebar's Releases section is both the count and the way in. A toolbar
button was considered and rejected, because it would put the action away
from the fact it describes, and because the toolbar is already the
longest row of buttons on the screen. Escape returns to the repository
and announces its name, the same as Issues and Pull requests.

The other About facts stay plain text. One button among labels is what
github.com's sidebar is too: most of it is facts, and the parts that go
somewhere are links.

## Structure

Three panes, cycled with F6: Releases, Details, New release. The same
top-to-bottom order as github.com's releases page, which lists the
releases and offers "Draft a new release".

- **Releases** is the list, one row per release. Arrowing changes the
  selection and the Details pane follows.
- **Details** is the selected release: title as heading 1, a metadata
  line, the notes as the same Markdown document as the README (caret
  reading, real hyperlinks), then the assets under a heading 2.
- **New release** is the form.

## What a row has to say

| Row | Announcement |
| --- | --- |
| Named release | `GitApp 0.2, tag v0.2.0, latest, by alexoloopios, 2 assets, 1 day ago` |
| Unnamed draft | `v0.1.0-beta, draft, pre-release, 10 days ago` |

The title leads. The tag follows only when it says something the title
does not: an unnamed release is shown by its tag, as github.com does, and
saying the tag twice would be the kind of repetition the whole app avoids.
Draft, pre-release and latest come next because they change what the
release means; then who and when. Zero assets are omitted rather than
spoken.

Asset sizes are words, `19 megabytes`, not `18.9 MB`, because a screen
reader spells the unit. Downloading is not built; "View on github.com"
opens the selected release where the assets can be fetched.

## Creating a release

The form is tag, target branch, title, notes, pre-release, save as draft.
The tag is required and the only required field; GitHub creates it on
the target branch if it does not exist, which is also github.com's
behaviour. The title is optional, and the button says what it will do:
"Publish release", or "Save draft" once the draft box is ticked.

Control+Enter publishes from inside the notes or the tag field, the same
key the comment box uses. `Publishing v0.3.0` then `GitApp 0.3
published.`; for a draft, `Draft v0.3.0 saved. It is not public until
published on github.com.`, because a saved draft that sounds like a
publish is a release the user thinks is out and is not.

A bad tag is refused in the app with a sentence saying what is wrong,
before GitHub sees it: blank, contains spaces, or not a valid ref name.
GitHub's own refusal for a duplicate tag is a 422 whose message is
"Validation Failed", so that case is reworded: `A release with that tag
may already exist, or the target branch may not.`

On success the release is inserted at the top of the list and selected,
the form clears, and focus stays where it was. On failure the form is
left exactly as typed; the tag is the thing most worth not retyping.
Escape with anything typed in the form asks "Leave without publishing
it?" rather than backing out silently.

## API

The list is one GraphQL request: the releases, their assets, the default
branch and the branches a new release can target. Creating a release is
REST `POST /repos/{owner}/{repo}/releases`, because GraphQL has no
mutation for it. Editing, deleting and publishing a draft are not built;
uploading assets is not built.
