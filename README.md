# MacroStudio

A local Windows 11 desktop macro recorder built with C#, WPF, MVVM and .NET 10 LTS. It records system-wide mouse/keyboard input using low-level Windows hooks, groups mouse paths, and replays with `SendInput`.

## Build and run

Install the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), then run in PowerShell:

```powershell
./scripts/build.ps1
dotnet run --project src/MacroStudio -c Release
```

The script also accepts a project-local `.dotnet/dotnet.exe`. There are no third-party NuGet dependencies. `./scripts/build.ps1 -Package` builds, tests and publishes a self-contained x64 distribution to `artifacts/MacroStudio/` plus `artifacts/MacroStudio-win-x64.zip`. Launch `MacroStudio.exe` there. The included Interaction Lab is under `Lab/` and opens from the toolbar. For ARM64, change the publish RID to `win-arm64` in the script.

The release for this workspace is `artifacts/MacroStudio-1.0/MacroStudio.exe`; its ZIP is `artifacts/MacroStudio-1.0-win-x64.zip`. To reproduce that path, use `./scripts/build.ps1 -Package -OutputDirectory artifacts/MacroStudio-1.0`. Close the app and lab before overwriting their package folder, or choose a different output folder.

## Use

1. Open **Interaction lab** or another target app. Press **Record** (default **F8**), perform the task, then press **Esc** to stop. Recording is explicit and has a visible compact controller.
2. Select a step and edit its delay, duration, text and comment in the right panel, then **Apply properties**. **Edit properties** opens coordinates, keys, paths and window targets. Insert opens the same editor with all manual action types available. Down and up are separate actions; two click pairs reproduce a double-click and a held pair surrounds a drag path.
3. Save as local `.json`, reopen with File → Open, then **Play** (**F9**). **F10** pauses/resumes; **Esc** cancels waits and repeats and releases engine-held input. Shortcuts are configurable under **Shortcuts**, with duplicate/OS registration conflict detection.

Playback speed accepts 0.05–100×, repeats 1–1000000, or until stopped. Options include skipping mouse paths and restoring recorded target-window bounds. The inspector offers one-action, selected-range (inclusive first-to-last) and from-selection playback. One-action playback runs once. Playback has an initial cancellable one-second delay; recorded delays are retained.

Multi-select with Ctrl/Shift. Drag selected rows to move them before another row, or use ↑/↓. Ctrl+X/C/V/D cuts, copies, pastes and duplicates actions; Delete removes them. Enable/disable, undo/redo (100 snapshots), comments and search work locally. Ctrl+N/O/S manages files and Ctrl+Z/Y history. File operations prompt for unsaved changes, preserve recent files and validate loaded data.

Desktop coordinates are physical pixels across the virtual desktop, including negative monitor coordinates. Window-relative recording stores physical offsets from the target's **outer window rectangle**, not client-area DIPs. Targets store process names and titles, never HWNDs. Playback uses exact title, unique partial title, or a unique process window; ambiguity produces an error. Layout changes within an app can require editing or rerecording coordinates. Choose window-relative recording before Record.

The compact controller uses `WS_EX_NOACTIVATE` and does not steal the target's keyboard focus. MacroStudio-owned windows and control shortcuts are omitted. Its own tagged playback input is ignored by hooks. Input from other automation/accessibility tools can be recorded. Keep only one MacroStudio instance running.

Macros stay wherever you save them. Theme, shortcuts and up to ten recent paths are stored in `%LOCALAPPDATA%/MacroStudio/preferences.json`. Recording captures keys: avoid typing secrets while recording. The app has no network features or telemetry.

Windows restricts input into elevated apps and secure desktops. Use the same privilege level for target and recorder; injection failures explain this limitation. Windows can also refuse focus changes. A secure desktop, locked session or unavailable target cannot be automated. Stop reports cleanup failures if Windows prevents releasing input.

## Verification

`./scripts/build.ps1` runs 17 core and seven editor checks. `./scripts/verify-native.ps1` publishes a separate lab and runs nine opt-in native checks: activate the freshly launched lab, then leave mouse/keyboard input alone until it completes. It records test input only, writes evidence under `work/native-verification`, aborts its input if the lab loses focus, and closes the lab when done. Run on an unlocked interactive desktop. Middle clicks inside a text editor can activate autoscroll; the test uses the counter button for auxiliary clicks.

See [verification](docs/VERIFICATION.md) for checks actually run and hardware-dependent checks, [format](docs/FORMAT.md) for the open JSON format, and [implementation plan](docs/IMPLEMENTATION-PLAN.md) for architecture and sources.

Visual targeting, OCR, branching, scheduling and AI are outside this core version.
