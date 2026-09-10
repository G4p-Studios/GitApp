# Developing GitApp

## Prerequisites

- **Visual Studio 2026** (18.x). RNW 0.84 requires it; VS 2022 will not build this project.
  Workloads: Desktop development with C++, Universal Windows Platform development,
  and a Windows 11 SDK.
- **Node 22.11 or newer.**
- **Windows 11 SDK 10.0.26100.0.** See the pin below before changing this.
- **Developer Mode enabled.** Settings, System, For developers, Developer Mode.
  Without it the app compiles but cannot deploy. See below.

Microsoft ships a dependency checker that installs what is missing:

```powershell
# from an elevated PowerShell prompt
node_modules\react-native-windows\scripts\rnw-dependencies.ps1
```

## Setup

```powershell
npm install
npx react-native run-windows --arch x64
```

## Known environment traps

Four things cost time on first setup. All four fail in ways that do not point
at the cause.

### The Windows SDK is pinned, deliberately

`windows/ExperimentalFeatures.props` sets `WindowsTargetPlatformVersion` and
`TargetPlatformVersion` to `10.0.26100.0`.

Without that pin the build fails with `MSB8036: The Windows SDK version
10.0.22621.0 was not found`, even on a machine with a newer SDK installed. The
cause is a template and framework interaction: the generated `.vcxproj` ships

```xml
<WindowsTargetPlatformVersion>10.0</WindowsTargetPlatformVersion>
```

where `10.0` means "latest installed". RNW's
`Microsoft.ReactNative.WindowsSdk.Default.props` then applies its New
Architecture floor:

```xml
Condition="'$(RnwNewArch)'=='true' And VersionLessThan($(WindowsTargetPlatformVersion), '10.0.22621.0')"
```

`10.0` compares as lower than `10.0.22621.0`, so the floor replaces "latest"
with a literal `10.0.22621.0`. If that exact SDK is absent, the build stops.

The pin works because both `gitapp.vcxproj` and `gitapp.Package.wapproj`
import `ExperimentalFeatures.props` before the RNW property sheet, and every
default in that sheet is conditional on the value being empty. The `.vcxproj`
value was also made conditional so it acts as a fallback rather than
overwriting the pin.

Raise the pin when the build machines move to a newer SDK. Leave
`TargetPlatformMinVersion` alone; it governs which Windows versions the app
still runs on.

### Deploy fails with exit error code 5

```
DeployRecipeFailure: Deploying ...\gitapp.Package.build.appxrecipe - exit error code 5
```

Code 5 is ACCESS_DENIED, and it means Developer Mode is off. The compile
succeeds and produces `windows\x64\Debug\gitapp.exe`; only registering the
app package fails, which is why the error arrives at the very end of a long
build and looks worse than it is.

Enable it in Settings, System, For developers. From an elevated prompt:

```powershell
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" `
  /t REG_DWORD /f /v AllowDevelopmentWithoutDevLicense /d 1
```

Running the built `.exe` directly instead is not a workaround. The cpp-app
template builds against the Windows App SDK and expects package identity; the
process starts, creates no window, and exits nothing to the console. GitApp
needs package identity anyway for toast notifications, so packaged deployment
is the supported path rather than a development convenience.

### `npm install` and the `allow-scripts` config key

npm 11.19 rejects the `allow-scripts` key in any project-scoped install:

```
npm error code EALLOWSCRIPTS
npm error --allow-scripts is not allowed in project-scoped installs.
```

If a user-level `~/.npmrc` sets `allow-scripts`, every `npm install` in every
project fails, including the React Native template download. Remove it with
`npm config delete allow-scripts` and pass the flag per command instead when
installing a global package that needs lifecycle scripts.

### The React Native CLI exits 0 on failure

`react-native init-windows` and `run-windows` can print a build failure and
still exit with status 0. Do not trust the exit code in scripts or CI. Check
for the built binary, or grep the log for `error MSB` and `Build failed`.

## Tests

```powershell
npm run typecheck      # tsc --noEmit
npm run test:a11y      # accessibility contracts
npm test               # everything
```

`npm run typecheck` matters more than usual here. `tsconfig.json` maps
`react-native` to the `react-native-windows` type surface; without that
mapping every accessibility prop in `src/a11y` is a type error. If typecheck
suddenly reports unknown props like `accessibilityPosInSet` or `onKeyDown`,
that mapping has been lost, and the code is being checked against the wrong
platform.

### The accessibility suite

`tests/a11y/` asserts the props we declare, not a live UI Automation tree.
Reading a real UIA tree needs the app running under an automation client and
belongs in an E2E suite that does not exist yet.

What it does catch is the class of regression that actually ships: a row that
loses its label, a list whose set size quietly becomes the rendered window
length, a composite widget that grows a second tab stop.

Snapshots in `tests/a11y/__snapshots__/` are the accessibility contract and are
committed deliberately. A diff there is a review conversation, not a nuisance;
update them with `npm run test:a11y:update` only once you have decided the new
tree is correct.

Automated tests are a floor, not a ceiling. Every feature still needs a manual
pass with NVDA and Narrator before it is called done. See
`docs/ARCHITECTURE.md` section 3.7.

## Where things live

See `docs/ARCHITECTURE.md` section 5 for the full layout. The short version:
`src/a11y/` holds the primitives every screen is required to build on, and
screens never set raw accessibility props themselves.
