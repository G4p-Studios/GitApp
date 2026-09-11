# GitApp
The only app you'll need to quickly access and keep track of GitHub and other Git repos.

## What does it do?
- Access the same features of GitHub in a native interface, using .NET MAUI so the layout you know from github.com becomes a real Windows UI built from native controls.
- Log into a GitHub account to access repositories you have created or are a collaborator of, and be able to perform the same actions as on the GitHub website.
- Manage and clone repositories on your computer, and use the app to commit and push changes, as well as do other Git related actions.
- Get notified of updates you are subscribed to on GitHub.

## Accessibility

GitApp is built screen-reader-first. Every feature has to be fully operable
with the keyboard alone and has to read correctly in NVDA and Narrator before
it counts as done. See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the
contract and [docs/SPIKE-MAUI.md](docs/SPIKE-MAUI.md) for the measurements
behind the framework choice.

## Contributing

Read [AGENTS.md](AGENTS.md) first. It is the working guide for this
repository: conventions, how to build, and how accessibility is verified
here. `CLAUDE.md` points at the same file so every agent and every person
works from one set of rules.

## Why the name GitApp?
Simple, because you can do anything you would usually do on github.com, GitHub Desktop, and eventually even use other Git hosts like GitLab and Codeberg.