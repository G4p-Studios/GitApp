# Developing GitApp

## Prerequisites

- **.NET 10 SDK** (10.0.400 or newer).
- **The MAUI workload.** `dotnet workload install maui-windows` on Windows,
  or `maui` for everything. Visual Studio 2026 installs it with the .NET MAUI
  workload selected.
- **Windows 11** for the Windows target. A Mac with Xcode for Mac Catalyst.

Check what you have:

```powershell
dotnet --version
dotnet workload list
```

Nothing else. No Node, no Windows SDK pin, no Developer Mode, no packaged
deployment. All of those were React Native Windows requirements and are gone.

## Build and run

```powershell
dotnet build
dotnet run --project src/GitApp/GitApp.csproj
```

The project targets whichever platform you are building on: Windows builds
`net10.0-windows`, macOS builds `net10.0-maccatalyst`. There is deliberately
no combined build, because neither OS can build the other's target.

## Tests

```powershell
dotnet test
```

`tests/GitApp.Core.Tests/` covers the porcelain parsing in `GitApp.Core`,
which is the logic most able to be quietly wrong. Fixtures are real git
output, not examples from the documentation.

There is deliberately no unit test asserting that a CollectionView reports
set positions or handles arrow keys. MAUI supplies those and re-asserting
them would be testing Microsoft's code. What is missing instead is the live
UI Automation layer described below, and that gap is real.

## How this project verifies accessibility

Read the automation tree of the running app. Do not reason about the API and
assume.

Every accessibility bug found so far was invisible to both the compiler and
any prop-level test, and several looked correct in source:

- A container marked non-accessible deleted two whole regions from the
  automation tree.
- `CollectionView.Focus()` returns true and focuses the scroll host, so
  entering a pane landed on an unnamed Pane and a screen reader said nothing
  useful.
- `FocusManager.TryFocusAsync(...).GetResults()` throws when the operation has
  not completed, which inside a try/catch is indistinguishable from "could
  not focus".

A PowerShell session with `UIAutomationClient` catches all three:

```powershell
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$p = Get-Process -Name GitApp | Where-Object { $_.MainWindowHandle -ne 0 }
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$w = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
# then walk with [System.Windows.Automation.TreeWalker]::ControlViewWalker
```

Useful properties per element: `ControlType`, `Name`, `PositionInSetProperty`,
`SizeOfSetProperty`, and the `Is*PatternAvailableProperty` family.

To read what NVDA actually says, turn on Speech Viewer (NVDA menu, Tools,
Speech Viewer) and read its edit control with `WM_GETTEXT`. Reading the UIA
`Name` of that control does not work: it is capped at 4096 characters and
returns a stale snapshot. It has no TextPattern either.

**Do this as well as the tree, not instead of it, and not only at the end.**
The tree shows structure; the Speech Viewer shows the sequence. The diff
viewer's folding had a perfect tree while speaking every action twice, once
as NVDA read the focused row and once as the app announced it. Nothing in
the tree can show that, because the duplication is in time.

Take a note of the buffer length before each keystroke and read only what is
new after it, otherwise the earlier output drowns the step under test. Leave
about 1.4 seconds between steps: the announcer coalesces anything inside its
500 ms window, so faster keypresses measure the throttle rather than the
feature.

## Known environment traps

### Foreground stealing blocks scripted keyboard tests

Windows will not let a background process take the foreground, so scripted
key-sending drifts into whatever window actually has focus. This is not
theoretical: during development it typed into a browser and opened a menu.

Always confirm the target window is genuinely foreground before sending
anything, and abort if it is not.

### Arrow keys need the extended-key flag

`keybd_event` with a zero scan code and no `KEYEVENTF_EXTENDEDKEY` turns an
arrow key into a character. Symptom: a screen reader reads a stray letter and
the list does not move. Pass the flag and a real scan code from
`MapVirtualKey`.

### Mac Catalyst is unverified

Nothing on macOS has been tested. F6 has no implementation there, and it is
the wrong gesture for macOS regardless. See `docs/SPIKE-MAUI.md`.

## Where things live

`docs/ARCHITECTURE.md` section 5 has the full layout. The short version:
`src/GitApp/Accessibility/` holds the four pieces the framework does not
supply, and `src/GitApp/Platforms/` holds the two things focus cannot do
portably.
